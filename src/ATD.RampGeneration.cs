// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using UnityEngine;
using AutoTerrainDesignations.Access;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using EntityId = Mafi.Core.EntityId;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private struct RampCandidate
        {
            public Tile2i[] OreTiles;
            public bool[] LaneHasOre;
            public Tile2i Direction;
            public Tile2i[] RampTiles;
            public int[] LaneAttachmentDepths;
            public int Score;

            public RampCandidate(Tile2i[] oreTiles, bool[] laneHasOre, Tile2i direction, Tile2i[] rampTiles, int[] laneAttachmentDepths, int score)
            {
                OreTiles = oreTiles;
                LaneHasOre = laneHasOre;
                Direction = direction;
                RampTiles = rampTiles;
                LaneAttachmentDepths = laneAttachmentDepths;
                Score = score;
            }
        }

        private struct RampStep
        {
            public Tile2i[] Tiles;
            public int[] NwHeights;
            public int[] NeHeights;
            public int[] SeHeights;
            public int[] SwHeights;

            public RampStep(Tile2i[] tiles, int[] nwHeights, int[] neHeights, int[] seHeights, int[] swHeights)
            {
                Tiles = tiles;
                NwHeights = nwHeights;
                NeHeights = neHeights;
                SeHeights = seHeights;
                SwHeights = swHeights;
            }
        }

        private struct RampTilePlan
        {
            public Tile2i Tile;
            public int NwHeight;
            public int NeHeight;
            public int SeHeight;
            public int SwHeight;

            public RampTilePlan(Tile2i tile, int nwHeight, int neHeight, int seHeight, int swHeight)
            {
                Tile = tile;
                NwHeight = nwHeight;
                NeHeight = neHeight;
                SeHeight = seHeight;
                SwHeight = swHeight;
            }
        }

        private struct RampVertexAccumulator
        {
            public int HeightSum;
            public int Samples;
            public bool HasFixedHeight;
            public int FixedHeight;

            public void Add(int height, bool isFixed)
            {
                HeightSum += height;
                Samples++;

                if (!isFixed)
                {
                    return;
                }

                if (!HasFixedHeight)
                {
                    FixedHeight = height;
                    HasFixedHeight = true;
                }
                else
                {
                    FixedHeight = (FixedHeight + height) / 2;
                }
            }
        }

        private struct RampVertexState
        {
            public int Height;
            public bool IsFixed;

            public RampVertexState(int height, bool isFixed)
            {
                Height = height;
                IsFixed = isFixed;
            }
        }

        private sealed class RampGenerationResult
        {
            public RampPlacementOutcome Outcome = RampPlacementOutcome.Failed;
            public Tile2i TopRowTile = default;
            public bool InitialReachabilityEvaluated;
            public bool AllClustersInitiallyConnected;
            public bool SuppressWarningNotification;
        }

        private struct RampVertexEdge
        {
            public Tile2i A;
            public Tile2i B;

            public RampVertexEdge(Tile2i a, Tile2i b)
            {
                A = a;
                B = b;
            }
        }

        /// <summary>
        /// Absolute world tile coordinates occupied by static buildings near the current tower area.
        /// The capture extends beyond the area far enough to cover access-search and candidate-ray margins.
        /// Farming automation may reuse the result briefly; an explicit accessway request forces a refresh.
        /// </summary>
        private static readonly HashSet<Tile2i> s_buildingOccupiedTiles = new HashSet<Tile2i>();
        private static readonly Dictionary<Tile2i, HashSet<int>> s_buildingFixedHeights2ByTile =
            new Dictionary<Tile2i, HashSet<int>>();

        /// <summary>
        /// Origin tile coordinates (4-tile-grid-aligned) of every terrain designation currently present in the
        /// tower area. Rebuilt once per <see cref="CreateAccessRamp"/> call via a single
        /// <see cref="TerrainDesignationsManager.SelectDesignationsInArea"/> scan so that
        /// <see cref="IsFreeRampTile"/> can use fast <see cref="HashSet{T}.Contains"/> lookups instead of
        /// thousands of individual <see cref="TerrainDesignationsManager.GetDesignationAt"/> API calls.
        /// </summary>
        private static readonly HashSet<Tile2i> s_designationOriginsInArea = new HashSet<Tile2i>();
        private static EntityId s_buildingOccupiedTilesCachedTowerId = EntityId.Invalid;
        private static int s_buildingOccupiedTilesCachedTick = int.MinValue;

        internal enum RampPlacementOutcome { Failed, Truncated, Crested, NotAccessible }

        private const uint ALL_DESIGNATION_TILES_MASK = 0x1FFFFFF;
        private const uint READY_PERIMETER_MASK = 0x1F8C63F;

        // How many farming ticks to reuse a cached s_buildingOccupiedTiles result for the same
        // tower. Buildings inside an active designation area almost never change, so a ~60-second
        // window is safe and prevents repeated expensive GetAllEntitiesOfType scans that can stall
        // when another mod (e.g. CLExporter) holds the entity-collection lock concurrently.
        private const int BUILDING_OCCUPIED_TILES_CACHE_TICKS = 600;

        // Maximum number of unique ramp-mouth positions to test for vehicle reachability in a
        // single TryPlaceRampCandidates call.  Candidates are tried best-score-first, so the top
        // options are always evaluated.  Capping prevents runaway BFS cost when many candidates
        // share the same general area but none are reachable (the fallback is used anyway).
        private const int MAX_RAMP_REACHABILITY_CHECKS = 50;

        // The optional public fallback expands only the experimental
        // generated-origin domain. Ground navigation already extends beyond
        // the managed area.
        private const int OUTSIDE_TOWER_RAMP_FALLBACK_TILES = 16;

        private static void BuildBuildingOccupiedTiles(IAreaManagingTower tower, bool forceRefresh = false)
        {
            // Return the existing set if it was recently built for this same tower.
            bool hasTowerId = TryGetTowerEntityId(tower, out EntityId towerId);
            int ticksSinceCache = s_farmingAutomationTickIndex - s_buildingOccupiedTilesCachedTick;
            if (!forceRefresh
                && hasTowerId
                && towerId == s_buildingOccupiedTilesCachedTowerId
                && ticksSinceCache >= 0
                && ticksSinceCache < BUILDING_OCCUPIED_TILES_CACHE_TICKS)
            {
                return;
            }

            s_buildingOccupiedTiles.Clear();
            s_buildingFixedHeights2ByTile.Clear();
            if (s_entitiesManager == null)
            {
                return;
            }

            // Candidate origins can reach outside the tower bounding box through the ground-search margin,
            // and their terrain rays extend farther again. Capturing only Area.ContainsTile used to omit
            // nearby buildings (and the outside portion of a footprint straddling the area boundary).
            int buildingRaySafetyBoundary = BuildingSafetyBufferTiles;
            int captureMargin = RAMP_ACCESS_SEARCH_MARGIN_TILES
                + AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance
                + AutoTerrainDesignationsMod.AccessRayEndBuffer
                + buildingRaySafetyBoundary;
            Tile2i areaMin = tower.Area.BoundingBoxMin;
            Tile2i areaMax = tower.Area.BoundingBoxMax;
            int captureMinX = areaMin.X - captureMargin;
            int captureMinY = areaMin.Y - captureMargin;
            int captureMaxX = areaMax.X + captureMargin;
            int captureMaxY = areaMax.Y + captureMargin;
            int staticEntityCount = 0;
            int capturedEntityCount = 0;

            foreach (IStaticEntity entity in s_entitiesManager.GetAllEntitiesOfType<IStaticEntity>())
            {
                staticEntityCount++;
                Tile2i center = entity.CenterTile.Xy;
                int fixedHeight2 = entity.CenterTile.Z * 2;
                var occupiedTiles = entity.OccupiedTiles;
                bool capturedEntity = false;
                for (int i = 0; i < occupiedTiles.Length; i++)
                {
                    Tile2i absCoord = center + occupiedTiles[i].RelCoord;
                    if (absCoord.X >= captureMinX && absCoord.X <= captureMaxX
                        && absCoord.Y >= captureMinY && absCoord.Y <= captureMaxY)
                    {
                        capturedEntity = true;
                        s_buildingOccupiedTiles.Add(absCoord);
                        if (!s_buildingFixedHeights2ByTile.TryGetValue(absCoord, out HashSet<int> heights))
                        {
                            heights = new HashSet<int>();
                            s_buildingFixedHeights2ByTile[absCoord] = heights;
                        }
                        heights.Add(fixedHeight2);
                    }
                }
                if (capturedEntity)
                    capturedEntityCount++;
            }

            LogExperimentalAccessDebug(
                $"[ATD Building Snapshot] forceRefresh={forceRefresh} staticEntities={staticEntityCount} " +
                $"capturedEntities={capturedEntityCount} occupiedTiles={s_buildingOccupiedTiles.Count} " +
                $"captureBounds=({captureMinX},{captureMinY})..({captureMaxX},{captureMaxY})");

            if (hasTowerId)
            {
                s_buildingOccupiedTilesCachedTowerId = towerId;
                s_buildingOccupiedTilesCachedTick = s_farmingAutomationTickIndex;
            }
        }

        private static void BuildDesignationOriginsInArea(IAreaManagingTower tower)
        {
            s_designationOriginsInArea.Clear();
            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(
                tower.Area.BoundingBoxMin, tower.Area.BoundingBoxMax))
            {
                s_designationOriginsInArea.Add(designation.OriginTileCoord);
            }
        }

        private const int MAX_DESIGNATION_QUERY_ORIGIN_SPAN = 192;
        private const int DESIGNATION_QUERY_CHUNK_STRIDE = MAX_DESIGNATION_QUERY_ORIGIN_SPAN + 4;

        private static IEnumerable<TerrainDesignation> SelectDesignationsInAreaChunked(
            Tile2i fromCoord, Tile2i toCoord)
        {
            if (s_desigManager == null) yield break;

            var rawMin = new Tile2i(
                Math.Min(fromCoord.X, toCoord.X),
                Math.Min(fromCoord.Y, toCoord.Y));
            var rawMax = new Tile2i(
                Math.Max(fromCoord.X, toCoord.X),
                Math.Max(fromCoord.Y, toCoord.Y));
            Tile2i minOrigin = TerrainDesignation.GetOrigin(rawMin);
            Tile2i maxOrigin = TerrainDesignation.GetOrigin(rawMax);

            for (int y = minOrigin.Y; y <= maxOrigin.Y; y += DESIGNATION_QUERY_CHUNK_STRIDE)
            {
                int chunkMaxY = Math.Min(y + MAX_DESIGNATION_QUERY_ORIGIN_SPAN, maxOrigin.Y);
                for (int x = minOrigin.X; x <= maxOrigin.X; x += DESIGNATION_QUERY_CHUNK_STRIDE)
                {
                    int chunkMaxX = Math.Min(x + MAX_DESIGNATION_QUERY_ORIGIN_SPAN, maxOrigin.X);
                    foreach (TerrainDesignation designation in s_desigManager.SelectDesignationsInArea(
                        new Tile2i(x, y), new Tile2i(chunkMaxX, chunkMaxY)))
                    {
                        yield return designation;
                    }
                }
            }
        }

        private static HashSet<Tile2i> AddExistingTerrainWorkEndpoints(
            IAreaManagingTower tower,
            Dict<Tile2i, int> accessWorkDepths,
            Dict<Tile2i, int> cornerHeights,
            ISet<Tile2i>? contextOnlyTerrainWorkOrigins)
        {
            var added = new HashSet<Tile2i>();
            int miningEndpoints = 0;
            int dumpingEndpoints = 0;
            int levelingEndpoints = 0;
            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(
                tower.Area.BoundingBoxMin, tower.Area.BoundingBoxMax))
            {
                Tile2i origin = designation.OriginTileCoord;
                if (!ShouldAddExistingTerrainWorkEndpoint(
                        accessWorkDepths.ContainsKey(origin),
                        contextOnlyTerrainWorkOrigins?.Contains(origin)
                            ?? false,
                        IsRegisteredGeneratedAccesswayOrigin(tower, origin),
                        designation.IsFulfilled,
                        IsOriginInsideTower(tower, origin),
                        IsTerrainWorkDesignationProto(
                            designation.Prototype)))
                    continue;

                int nw = GetDesignationTargetHeightRounded(designation, 0, 0);
                int ne = GetDesignationTargetHeightRounded(designation, 4, 0);
                int se = GetDesignationTargetHeightRounded(designation, 4, 4);
                int sw = GetDesignationTargetHeightRounded(designation, 0, 4);
                accessWorkDepths[origin] = (int)Math.Round((nw + ne + se + sw) / 4f);
                cornerHeights[origin] = nw;
                cornerHeights[origin + new RelTile2i(4, 0)] = ne;
                cornerHeights[origin + new RelTile2i(4, 4)] = se;
                cornerHeights[origin + new RelTile2i(0, 4)] = sw;
                added.Add(origin);
                if (s_miningProto != null && designation.Prototype == s_miningProto)
                    miningEndpoints++;
                else if (s_dumpingProto != null && designation.Prototype == s_dumpingProto)
                    dumpingEndpoints++;
                else if (s_levelingProto != null && designation.Prototype == s_levelingProto)
                    levelingEndpoints++;
            }
            if (added.Count > 0)
            {
                LogExperimentalAccessDebug(
                    $"[ATD Access Ownership] externalTerrainEndpoints={added.Count} " +
                    $"mining={miningEndpoints} dumping={dumpingEndpoints} leveling={levelingEndpoints}");
            }
            return added;
        }

        private static bool ShouldAddExistingTerrainWorkEndpoint(
            bool alreadyRequested,
            bool contextOnly,
            bool generatedAccesswayOwned,
            bool fulfilled,
            bool insideTower,
            bool terrainWorkPrototype)
            => !alreadyRequested
                && !contextOnly
                && !generatedAccesswayOwned
                && !fulfilled
                && insideTower
                && terrainWorkPrototype;

        private static bool HasExistingTerrainWorkEndpoint(IAreaManagingTower tower)
            => CollectExistingTerrainWorkEndpointOrigins(tower).Count > 0;

        private static HashSet<Tile2i> CollectExistingTerrainWorkEndpointOrigins(
            IAreaManagingTower tower)
        {
            var origins = new HashSet<Tile2i>();
            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(
                tower.Area.BoundingBoxMin, tower.Area.BoundingBoxMax))
            {
                if (!designation.IsFulfilled
                    && !IsRegisteredGeneratedAccesswayOrigin(tower, designation.OriginTileCoord)
                    && IsOriginInsideTower(tower, designation.OriginTileCoord)
                    && IsTerrainWorkDesignationProto(designation.Prototype))
                    origins.Add(designation.OriginTileCoord);
            }
            return origins;
        }

        private static bool IsTerrainWorkDesignationProto(TerrainDesignationProto proto)
            => (s_miningProto != null && proto == s_miningProto)
                || (s_dumpingProto != null && proto == s_dumpingProto)
                || (s_levelingProto != null && proto == s_levelingProto);

        private static int GetDesignationTargetHeightRounded(
            TerrainDesignation designation, int x, int y)
            => (int)Math.Round(GetDesignationTargetHeightAt(designation.Data, x, y).Value.ToFloat());

        private static RampPlacementOutcome CreateAccessRamp(
            IAreaManagingTower tower,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights,
            TerrainManager terrMgr,
            int configuredRampWidth,
            out Tile2i topRowTile)
        {
            return CreateAccessRamp(tower, tileDepths, cornerHeights, terrMgr, configuredRampWidth, s_miningProto, null, null, out topRowTile);
        }

        private static RampPlacementOutcome CreateAccessRamp(
            IAreaManagingTower tower,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights,
            TerrainManager terrMgr,
            int configuredRampWidth,
            TerrainDesignationProto? rampProto,
            List<Tile2i>? placedRampOrigins,
            HashSet<Tile2i>? reservedRampTiles,
            out Tile2i topRowTile)
        {
            return CreateAccessRamp(
                tower,
                tileDepths,
                cornerHeights,
                terrMgr,
                configuredRampWidth,
                rampProto,
                placedRampOrigins,
                reservedRampTiles,
                useLocalSurfaceReference: false,
                allowExistingPlannedRampShortcut: true,
                out topRowTile);
        }

        private static RampPlacementOutcome CreateAccessRamp(
            IAreaManagingTower tower,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights,
            TerrainManager terrMgr,
            int configuredRampWidth,
            TerrainDesignationProto? rampProto,
            List<Tile2i>? placedRampOrigins,
            HashSet<Tile2i>? reservedRampTiles,
            bool useLocalSurfaceReference,
            bool allowExistingPlannedRampShortcut,
            out Tile2i topRowTile,
            HashSet<Tile2i>? forbiddenApproachClusterOrigins = null,
            bool emitNoCandidateWarnings = true)
        {
            var result = new RampGenerationResult();
            IEnumerator routine = CreateAccessRampCoroutine(
                tower,
                tileDepths,
                cornerHeights,
                terrMgr,
                configuredRampWidth,
                rampProto,
                placedRampOrigins,
                reservedRampTiles,
                useLocalSurfaceReference,
                allowExistingPlannedRampShortcut,
                result,
                forbiddenApproachClusterOrigins,
                emitNoCandidateWarnings: emitNoCandidateWarnings);
            while (routine.MoveNext()) { }
            topRowTile = result.TopRowTile;
            return result.Outcome;
        }

        private static IEnumerator CreateAccessRampCoroutine(
            IAreaManagingTower tower,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights,
            TerrainManager terrMgr,
            int configuredRampWidth,
            TerrainDesignationProto? rampProto,
            List<Tile2i>? placedRampOrigins,
            HashSet<Tile2i>? reservedRampTiles,
            bool useLocalSurfaceReference,
            bool allowExistingPlannedRampShortcut,
            RampGenerationResult result,
            HashSet<Tile2i>? forbiddenApproachClusterOrigins = null,
            ISet<Tile2i>? contextOnlyTerrainWorkOrigins = null,
            IReadOnlyCollection<Tile2i>? projectedProviderGoalOrigins = null,
            IReadOnlyList<Tile2i>? groundGoalOverride = null,
            bool emitNoCandidateWarnings = true,
            bool newPlannerOnly = false,
            ExperimentalAccessSliceControl? sliceControl = null)
        {
            result.TopRowTile = default;
            result.Outcome = RampPlacementOutcome.Failed;

            // World reset and a superseded Create Designations request both set this
            // shared flag. Farming access generation is a separate operation, however;
            // after a reload it must be allowed to start a fresh V2 search. Preserve a
            // currently active Create Designations cancellation so that operation can
            // still be superseded safely.
            if (!s_createDesignationsOperationActive)
                s_cancelExperimentalAccessSearch = false;

            if (s_desigManager == null || rampProto == null)
            {
                result.Outcome = RampPlacementOutcome.Failed;
                yield break;
            }

            InvalidateTowerReachabilityFlood();

            TerrainDesignationProto sourceWorkProto = rampProto;
            TerrainDesignationProto accesswayProto = s_levelingProto ?? sourceWorkProto;
            bool accesswayAllowsMixedWork = s_levelingProto != null && accesswayProto == s_levelingProto;

            // A user-triggered Create Designations request must see buildings placed since the previous
            // farming/pathfinding pass. Subsequent cluster snapshots reuse this freshly captured set.
            BuildBuildingOccupiedTiles(tower, forceRefresh: true);
            BuildDesignationOriginsInArea(tower);

            var accessWorkDepths = new Dict<Tile2i, int>();
            foreach (var pair in tileDepths)
                accessWorkDepths[pair.Key] = pair.Value;
            HashSet<Tile2i> existingEndpointOrigins = AddExistingTerrainWorkEndpoints(
                tower,
                accessWorkDepths,
                cornerHeights,
                contextOnlyTerrainWorkOrigins);
            if (accessWorkDepths.Count == 0)
            {
                result.Outcome = RampPlacementOutcome.Failed;
                yield break;
            }

            // 1. Group active designations into origin clusters
            List<List<Tile2i>> rawClusters = BuildDesignationOriginClusters(accessWorkDepths, terrMgr);
            if (rawClusters.Count == 0)
            {
                result.TopRowTile = default;
                result.Outcome = RampPlacementOutcome.Crested;
                yield break;
            }

            var miningIntent = new GenericWorkIntent("generated-mining-plan");
            var existingDesignationIntent = new GenericWorkIntent("existing-terrain-designation");
            var originClusters = new List<AccessOriginCluster>();
            int nextClusterId = 0;
            foreach (var rawCluster in rawClusters)
            {
                var accessOrigins = new List<AccessWorkOrigin>(rawCluster.Count);
                var sourceIntents = new List<AccessWorkIntent>();
                foreach (var origin in rawCluster)
                {
                    bool existingEndpoint = existingEndpointOrigins.Contains(origin);
                    AccessWorkIntent intent = existingEndpoint
                        ? existingDesignationIntent
                        : miningIntent;
                    accessOrigins.Add(new AccessWorkOrigin(
                        origin,
                        intent,
                        false,
                        existingEndpoint
                            ? AccessWorkOriginKind.ExternalTerrainWorkEndpoint
                            : AccessWorkOriginKind.GeneratedMiningPlan));
                    if (!sourceIntents.Contains(intent)) sourceIntents.Add(intent);
                }
                originClusters.Add(new AccessOriginCluster(
                    ++nextClusterId, accessOrigins, sourceIntents));
            }

            if (AutoTerrainDesignationsMod.TurningRampsExperimental)
            {
                foreach (AccessOriginCluster cluster in originClusters)
                {
                    string origins = cluster.Origins.Count <= 32
                        ? string.Join(",", cluster.Origins.Select(origin => origin.Origin.ToString()))
                        : $"{cluster.Origins.Count} origins";
                    int generatedMiningCount = cluster.Origins.Count(
                        origin => origin.Kind == AccessWorkOriginKind.GeneratedMiningPlan);
                    int externalEndpointCount = cluster.Origins.Count(
                        origin => origin.Kind == AccessWorkOriginKind.ExternalTerrainWorkEndpoint);
                    LogExperimentalAccessDebug(
                        $"[ATD Experimental Access Cluster] cluster={cluster.ClusterId} " +
                        $"count={cluster.Origins.Count} generatedMining={generatedMiningCount} " +
                        $"externalEndpoints={externalEndpointCount} origins=[{origins}]");
                }
            }

            AccessSearchSnapshot? experimentalSnapshot = null;
            LastExperimentalAccessSearch = null;
            LastExperimentalAccessPlan = null;
            bool suppressLegacyAccessRamps = newPlannerOnly
                || AutoTerrainDesignationsMod.SuppressLegacyAccessRamps;
            bool experimentalOperationSupported = TryGetExperimentalOperation(sourceWorkProto, out bool experimentalIsMining);
            bool experimentalWidthSupported = configuredRampWidth > 0;
            bool experimentalSearchEnabled = AutoTerrainDesignationsMod.TurningRampsExperimental
                && experimentalWidthSupported
                && experimentalOperationSupported
                && !IsLegacyRampMode(GetTowerVehicleClearance(tower));
            if (AutoTerrainDesignationsMod.TurningRampsExperimental && !experimentalSearchEnabled)
            {
                string reason = !experimentalWidthSupported
                    ? "tower accessway generation is disabled"
                    : IsLegacyRampMode(GetTowerVehicleClearance(tower))
                        ? "tower is configured for a legacy straight-only ramp width"
                    : $"designation proto {sourceWorkProto.Id.Value} is neither mining nor dumping";
                LogExperimentalAccessDebug($"[ATD Experimental Access] skipped because {reason}.");
            }
            if (suppressLegacyAccessRamps)
            {
                LogExperimentalAccessDebug("[ATD Access] legacy straight-ramp generator suppressed by mod setting.");
            }

            // 2. Identify existing access providers
            var existingProviders = new List<AccessProvider>();
            var accessibleAccessOrigins = new HashSet<Tile2i>();
            var inaccessibleAccessOrigins = new HashSet<Tile2i>();

            foreach (var origin in s_designationOriginsInArea)
            {
                if (accessWorkDepths.ContainsKey(origin)
                    && !IsRegisteredGeneratedAccesswayOrigin(tower, origin))
                    continue;

                Option<TerrainDesignation> existingDesignation = s_desigManager.GetDesignationAt(origin);
                if (existingDesignation.HasValue
                    && IsAccesswayDesignationProto(existingDesignation.Value.Prototype, accesswayProto))
                {
                    bool reachesTower = ExistingAccessOriginConnectsToTower(tower, origin, accessWorkDepths, accesswayProto, accessibleAccessOrigins, inaccessibleAccessOrigins);
                    existingProviders.Add(new AccessProvider(new[] { origin, origin.AddX(4), origin.AddY(4), origin.AddXy(4) }, reachesTower));
                }
            }

            // 3. Evaluate initial reachability
            var states = AccessReachability.EvaluateReachability(
                originClusters,
                existingProviders,
                tower,
                terrMgr,
                tile => IsClusterOriginReadyAndPathable(tower, tile),
                (origin, direction) => 
                {
                    Tile2i neighbor = new Tile2i(origin.X + direction.X, origin.Y + direction.Y);
                    return TryClusterEdgeConnectsToAccess(origin, neighbor, direction, accessWorkDepths, accesswayProto, terrMgr, out _);
                });
            result.InitialReachabilityEvaluated = true;
            result.AllClustersInitiallyConnected = originClusters.All(c =>
                states[c] == AccessClusterState.AccessibleDirect
                || states[c] == AccessClusterState.AccessibleViaProvider);
            bool usesGroundGoalOverride =
                groundGoalOverride != null && groundGoalOverride.Count > 0;
            if (usesGroundGoalOverride)
            {
                foreach (AccessOriginCluster cluster in originClusters)
                    states[cluster] = AccessClusterState.NeedsAccessway;
                LogExperimentalAccessDebug(
                    $"[ATD Planned Tower Access] forcing {originClusters.Count} " +
                    "terrain-work cluster(s) to use ghost goals instead of " +
                    "active-tower reachability");
            }
            RecordAccessClusterOverlay(originClusters, states);

            // Log initial states
            foreach (var cluster in originClusters)
            {
                var state = states[cluster];
                AccessDiagnostics.LogClusterState(new AccessAnalysisResult(cluster, state, AccessNeed.Mining, null, null, BlockedReason.None, 0f));
            }

            // If everything is already reachable, we can skip ramp generation
            if (allowExistingPlannedRampShortcut
                && !useLocalSurfaceReference
                && originClusters.All(c => states[c] == AccessClusterState.AccessibleDirect || states[c] == AccessClusterState.AccessibleViaProvider))
            {
                LogDebug("Skipping ramp generation: every excavation cluster already has tower-reachable access.");
                result.TopRowTile = default;
                result.Outcome = RampPlacementOutcome.Crested;
                yield break;
            }

            // 4. Sort unreachable clusters closest-to-tower first
            Tile2i towerPos;
            if (tower is IEntityWithPosition posEntity)
                towerPos = posEntity.Position2f.Tile2i;
            else
                towerPos = new Tile2i((tower.Area.BoundingBoxMin.X + tower.Area.BoundingBoxMax.X) / 2, (tower.Area.BoundingBoxMin.Y + tower.Area.BoundingBoxMax.Y) / 2);

            var unreachableClusters = originClusters.Where(c => states[c] == AccessClusterState.NeedsAccessway).ToList();
            unreachableClusters.Sort((left, right) =>
            {
                int leftMinDist = left.Origins.Min(o => (o.Origin.X - towerPos.X) * (o.Origin.X - towerPos.X) + (o.Origin.Y - towerPos.Y) * (o.Origin.Y - towerPos.Y));
                int rightMinDist = right.Origins.Min(o => (o.Origin.X - towerPos.X) * (o.Origin.X - towerPos.X) + (o.Origin.Y - towerPos.Y) * (o.Origin.Y - towerPos.Y));
                return leftMinDist.CompareTo(rightMinDist);
            });

            RampPlacementOutcome worstOutcome = RampPlacementOutcome.Crested;
            bool placedAny = false;
            bool validatedExistingRoute = false;

            // 5. Generate missing providers closest-first
            int unreachableClusterCount = unreachableClusters.Count;
            for (int clusterLoopIndex = 0; clusterLoopIndex < unreachableClusters.Count; clusterLoopIndex++)
            {
                var cluster = unreachableClusters[clusterLoopIndex];
                int currentClusterOrdinal = clusterLoopIndex + 1;
                // Re-evaluate state in case a previous loop iteration's placement connected this cluster
                if (states[cluster] == AccessClusterState.AccessibleDirect || states[cluster] == AccessClusterState.AccessibleViaProvider)
                {
                    LogDebug($"Skipping generation for cluster {cluster.ClusterId} because it is now reachable via provider.");
                    continue;
                }

                var clusterTileDepths = new Dict<Tile2i, int>();
                foreach (var origin in cluster.Origins)
                {
                    clusterTileDepths[origin.Origin] = accessWorkDepths[origin.Origin];
                }

                bool containsExternalTerrainEndpoint = cluster.Origins.Any(
                    origin => origin.Kind == AccessWorkOriginKind.ExternalTerrainWorkEndpoint);
                EvaluatedAccessCandidate? experimentalCandidate = null;
                bool experimentalCandidateUsesOutsideArea = false;
                string experimentalFailureSummary = string.Empty;
                if (experimentalSearchEnabled)
                {
                    List<Tile2i> accessibleFixedGoals = GetAccessibleFixedGoalOrigins(
                        originClusters, states, cluster);
                    if (projectedProviderGoalOrigins != null)
                    {
                        foreach (Tile2i providerGoal
                            in projectedProviderGoalOrigins)
                        {
                            if (!accessibleFixedGoals.Contains(providerGoal))
                                accessibleFixedGoals.Add(providerGoal);
                        }
                    }
                    if (usesGroundGoalOverride)
                        accessibleFixedGoals.Clear();
                    if (TryBuildExperimentalAccessSnapshot(tower, accessWorkDepths, cornerHeights, terrMgr,
                        experimentalIsMining, accesswayAllowsMixedWork, accessibleFixedGoals,
                        groundGoalOverride,
                        generatedAreaMarginTiles: 0,
                        out AccessSearchSnapshot refreshedSnapshot, out string refreshFailure))
                    {
                        experimentalSnapshot = refreshedSnapshot;
                        AccessSearchResult experimentalResult = null!;
                        AccessDesignationPlan? experimentalPlan = null;

                        LogExperimentalAccessDebug(
                            $"[ATD Experimental Access Search] cluster={cluster.ClusterId} " +
                            $"preparing fixedGoals={accessibleFixedGoals.Count} " +
                            $"towerGoals={refreshedSnapshot.GoalCount} starts={cluster.Origins.Count}");

                        AccessPathRequest request = BuildMergedGoalAccessRequest(
                            refreshedSnapshot, cluster, accessibleFixedGoals,
                            groundGoalOverride: groundGoalOverride);
                        LogExperimentalAccessDebug(
                            $"[ATD Experimental Access Width] cluster={cluster.ClusterId} " +
                            $"legacyRampWidth={configuredRampWidth} " +
                            $"resolvedVehicleWidth={refreshedSnapshot.VehicleWidth} " +
                            $"requiredWidth={request.RequiredWidth}");
                        Dictionary<Tile2i, string> designationStateBeforeSearch =
                            CaptureTerrainDesignationState(tower);
                        var experimentalDryRun = new ExperimentalAccessDryRunResult();
                        IEnumerator mergedSearch = RunExperimentalAccessDryRunSliced(
                            request, cluster, currentClusterOrdinal, unreachableClusterCount,
                            experimentalDryRun, sliceControl);
                        while (mergedSearch.MoveNext())
                            yield return mergedSearch.Current;
                        experimentalResult = experimentalDryRun.SearchResult!;

                        if (experimentalResult.SearchSpaceExhausted
                            && AllowRampsOutsideTowerAreas)
                        {
                            string inAreaFailure =
                                FormatExperimentalFailureSummary(experimentalResult);
                            LogExperimentalAccessDebug(
                                $"[ATD Outside-Area Fallback] cluster={cluster.ClusterId} " +
                                $"width={request.RequiredWidth} " +
                                $"phase=retry margin={OUTSIDE_TOWER_RAMP_FALLBACK_TILES} " +
                                $"inAreaFailure={inAreaFailure}");
                            if (TryBuildExperimentalAccessSnapshot(
                                    tower, accessWorkDepths, cornerHeights, terrMgr,
                                    experimentalIsMining, accesswayAllowsMixedWork,
                                    accessibleFixedGoals, groundGoalOverride,
                                    OUTSIDE_TOWER_RAMP_FALLBACK_TILES,
                                    out AccessSearchSnapshot outsideSnapshot,
                                    out string outsideSnapshotFailure))
                            {
                                experimentalSnapshot = outsideSnapshot;
                                request = BuildMergedGoalAccessRequest(
                                    outsideSnapshot, cluster, accessibleFixedGoals,
                                    groundGoalOverride: groundGoalOverride);
                                var outsideDryRun =
                                    new ExperimentalAccessDryRunResult();
                                IEnumerator outsideSearch =
                                    RunExperimentalAccessDryRunSliced(
                                        request, cluster, currentClusterOrdinal,
                                        unreachableClusterCount, outsideDryRun,
                                        sliceControl);
                                while (outsideSearch.MoveNext())
                                    yield return outsideSearch.Current;
                                experimentalResult = outsideDryRun.SearchResult!;
                                experimentalDryRun = outsideDryRun;
                                experimentalCandidateUsesOutsideArea =
                                    experimentalResult.Success;
                                LogExperimentalAccessDebug(
                                    $"[ATD Outside-Area Fallback] cluster={cluster.ClusterId} " +
                                    $"width={request.RequiredWidth} " +
                                    $"success={experimentalResult.Success} " +
                                    $"result={FormatExperimentalFailureSummary(experimentalResult)}");
                            }
                            else
                            {
                                LogExperimentalAccessDebug(
                                    $"[ATD Outside-Area Fallback] cluster={cluster.ClusterId} " +
                                    $"width={request.RequiredWidth} " +
                                    $"snapshotFailed={outsideSnapshotFailure}");
                            }
                        }
                        else if (!experimentalResult.Success
                            && AllowRampsOutsideTowerAreas)
                        {
                            LogExperimentalAccessDebug(
                                $"[ATD Outside-Area Fallback] cluster={cluster.ClusterId} " +
                                $"width={request.RequiredWidth} phase=skip " +
                                $"reason={experimentalResult.FailureReason} " +
                                "searchSpaceExhausted=false");
                        }
                        experimentalPlan = experimentalDryRun.Plan;
                        Dictionary<Tile2i, string> designationStateAfterSearch =
                            CaptureTerrainDesignationState(tower);
                        List<Tile2i> addedDuringSearch = designationStateAfterSearch.Keys
                            .Where(origin => !designationStateBeforeSearch.ContainsKey(origin))
                            .ToList();
                        List<Tile2i> removedDuringSearch = designationStateBeforeSearch.Keys
                            .Where(origin => !designationStateAfterSearch.ContainsKey(origin))
                            .ToList();
                        List<Tile2i> changedDuringSearch = designationStateAfterSearch.Keys
                            .Where(origin => designationStateBeforeSearch.TryGetValue(
                                origin, out string before)
                                && !string.Equals(before, designationStateAfterSearch[origin],
                                    StringComparison.Ordinal))
                            .ToList();
                        LogExperimentalAccessDebug(
                            $"[ATD Experimental Access Mutation Audit] cluster={cluster.ClusterId} " +
                            $"before={designationStateBeforeSearch.Count} after={designationStateAfterSearch.Count} " +
                            $"added={addedDuringSearch.Count} removed={removedDuringSearch.Count} " +
                            $"changed={changedDuringSearch.Count} " +
                            $"samples=[{string.Join(",", addedDuringSearch.Concat(removedDuringSearch)
                                .Concat(changedDuringSearch).Distinct().Take(24)
                                .Select(origin => $"({origin.X},{origin.Y})"))}]");

                        if (sliceControl?.CancellationRequested
                            ?? s_cancelExperimentalAccessSearch)
                        {
                            LogExperimentalAccessDebug(
                                $"[ATD Experimental Access] cluster={cluster.ClusterId} search cancelled by user");
                            result.Outcome = RampPlacementOutcome.Failed;
                            yield break;
                        }

                        if (experimentalResult.Success
                            && experimentalPlan != null
                            && experimentalPlan.IsValid
                            && experimentalPlan.Designations.Count == 0
                            && experimentalPlan.CleanupOrigins.Count == 0)
                        {
                            bool liveReady = cluster.Origins.All(origin =>
                                IsClusterOriginReadyAndPathable(
                                    tower, origin.Origin));
                            states[cluster] =
                                AccessReachability.ClassifyProjectedProvider(
                                    liveReady);
                            RecordAccessClusterOverlay(
                                originClusters, states);
                            validatedExistingRoute = true;
                            LogExperimentalAccessDebug(
                                $"[ATD Experimental Access] cluster={cluster.ClusterId} " +
                                $"selected={(liveReady ? "existing-route" : "waiting-for-provider")} " +
                                $"goal={experimentalResult.ReachedGoalKind} " +
                                $"reused={experimentalPlan.ReusedNodeCount}");
                            if (newPlannerOnly)
                            {
                                result.Outcome = RampPlacementOutcome.Crested;
                                yield break;
                            }
                            continue;
                        }

                        if (!experimentalResult.Success)
                        {
                            experimentalFailureSummary =
                                FormatExperimentalFailureSummary(experimentalResult);
                        }

                        if (string.IsNullOrEmpty(experimentalFailureSummary))
                        {
                            experimentalCandidate = EvaluateExperimentalAccessCandidate(
                                experimentalResult, experimentalPlan, towerPos, terrMgr);
                        }
                    }
                    else
                    {
                        experimentalSnapshot = null;
                        experimentalSearchEnabled = false;
                        experimentalFailureSummary = refreshFailure;
                        LogExperimentalAccessDebug($"[ATD Experimental Access] snapshot refresh failed: {refreshFailure}");
                    }
                }

                int rampWidth = Math.Max(1, Math.Min(5, configuredRampWidth));
                List<EvaluatedAccessCandidate> allEvaluated = new List<EvaluatedAccessCandidate>();
                
                // Retry 1: Configured width, no offset
                EvaluatedAccessCandidate? bestCandidate = containsExternalTerrainEndpoint || suppressLegacyAccessRamps
                    ? null
                    : FindBestCandidateForCluster(
                        tower,
                        towerPos,
                        clusterTileDepths,
                        cornerHeights,
                        terrMgr,
                        accesswayProto,
                        rampWidth,
                        lateralRetryOffset: 0,
                        reservedRampTiles,
                        useLocalSurfaceReference,
                        forbiddenApproachClusterOrigins,
                        out allEvaluated);

                // Retry 2: Configured width, lateral offset
                if (!containsExternalTerrainEndpoint && !suppressLegacyAccessRamps && bestCandidate == null && rampWidth > 1)
                {
                    int lateralRetryOffset = rampWidth / 2;
                    LogDebug($"Retrying ramp search for cluster {cluster.ClusterId} with sideways offset of {lateralRetryOffset}.");
                    bestCandidate = FindBestCandidateForCluster(
                        tower,
                        towerPos,
                        clusterTileDepths,
                        cornerHeights,
                        terrMgr,
                        accesswayProto,
                        rampWidth,
                        lateralRetryOffset,
                        reservedRampTiles,
                        useLocalSurfaceReference,
                        forbiddenApproachClusterOrigins,
                        out allEvaluated);
                }

                // Retry 3: Width 1, no offset
                if (!containsExternalTerrainEndpoint && !suppressLegacyAccessRamps && bestCandidate == null && rampWidth > 1)
                {
                    LogDebug($"Retrying ramp search for cluster {cluster.ClusterId} with width 1.");
                    bestCandidate = FindBestCandidateForCluster(
                        tower,
                        towerPos,
                        clusterTileDepths,
                        cornerHeights,
                        terrMgr,
                        accesswayProto,
                        1,
                        lateralRetryOffset: 0,
                        reservedRampTiles,
                        useLocalSurfaceReference,
                        forbiddenApproachClusterOrigins,
                        out allEvaluated);
                }

                bool preferExperimental = experimentalCandidate != null
                    && (bestCandidate == null || EvaluatedAccessCandidate.Compare(experimentalCandidate, bestCandidate) < 0);
                if (experimentalCandidate != null)
                {
                    string legacyMetrics = bestCandidate == null
                        ? "none"
                        : $"complete={bestCandidate.IsReachableNow},material={bestCandidate.MaterialMoved},mouthDistance={bestCandidate.MouthDistance},designations={bestCandidate.DesignationCount}";
                    string selectedGenerator = preferExperimental ? "experimental" : "legacy";
                    string decidedBy = bestCandidate == null
                        ? (suppressLegacyAccessRamps ? "legacy-suppressed" : "only-valid-candidate")
                        : EvaluatedAccessCandidate.GetDecidedBy(
                            preferExperimental ? experimentalCandidate : bestCandidate,
                            preferExperimental ? bestCandidate : experimentalCandidate);
                    LogExperimentalAccessDebug(
                        $"[ATD Experimental Access Selection] cluster={cluster.ClusterId} " +
                        $"experimental=[complete={experimentalCandidate.IsReachableNow},material={experimentalCandidate.MaterialMoved},mouthDistance={experimentalCandidate.MouthDistance},designations={experimentalCandidate.DesignationCount}] " +
                        $"legacy=[{legacyMetrics}] selected={selectedGenerator} decidedBy={decidedBy}");
                }
                if (preferExperimental)
                {
                    var source = (ExperimentalAccessCandidate)experimentalCandidate!.SourceCandidate;
                    var localPlacedOrigins = new List<Tile2i>();
                    if (TryPlaceExperimentalAccessCandidate(
                        experimentalSnapshot!,
                        accesswayProto,
                        source,
                        tower,
                        localPlacedOrigins,
                        reservedRampTiles,
                        out Tile2i experimentalTopTile,
                        out string experimentalPlacementFailure))
                    {
                        string decidedBy = bestCandidate == null
                            ? (suppressLegacyAccessRamps ? "legacy-suppressed" : "only-valid-candidate")
                            : EvaluatedAccessCandidate.GetDecidedBy(experimentalCandidate, bestCandidate);
                        List<Tile2i> providerOrigins = source.SearchResult.V2Route != null
                            ? source.SearchResult.V2Route.GeneratedProfiles.Keys.ToList()
                            : new List<Tile2i>(localPlacedOrigins);
                        if (providerOrigins.Count == 0)
                            providerOrigins.AddRange(localPlacedOrigins);
                        var experimentalProvider = new AccessProvider(providerOrigins, reachesGround: true);
                        existingProviders.Add(experimentalProvider);
                        states = AccessReachability.EvaluateReachability(
                            originClusters,
                            existingProviders,
                            tower,
                            terrMgr,
                            tile => IsClusterOriginReadyAndPathable(tower, tile),
                            (origin, direction) =>
                            {
                                Tile2i neighbor = new Tile2i(origin.X + direction.X, origin.Y + direction.Y);
                                return TryClusterEdgeConnectsToAccess(origin, neighbor, direction, accessWorkDepths, accesswayProto, terrMgr, out _);
                            });
                        string v2ValidationReason = "NotV2";
                        bool pendingPropRemoval = HasPendingExperimentalPropRemovalRequests();
                        if (pendingPropRemoval)
                            v2ValidationReason = "AcceptedPendingPropRemoval";
                        bool v2ProviderValid = pendingPropRemoval
                            || (source.SearchResult.V2Route != null
                            && ValidatePlacedV2Provider(
                                 source.SearchResult,
                                 source.Plan,
                                 accesswayProto,
                                 terrMgr,
                                 out v2ValidationReason));
                        bool overrideProviderValid = usesGroundGoalOverride
                            && source.SearchResult.Success
                            && (source.SearchResult.V2Route == null
                                || v2ProviderValid);
                        if (source.SearchResult.V2Route != null)
                            LogExperimentalAccessDebug(
                                $"[ATD V2 Placement Validation] cluster={cluster.ClusterId} " +
                                $"success={v2ProviderValid} reason={v2ValidationReason} " +
                                $"placedTerrain={localPlacedOrigins.Count} " +
                                $"providerOrigins={providerOrigins.Count}");
                        if (v2ProviderValid || overrideProviderValid)
                            states[cluster] = AccessClusterState.AccessibleViaProvider;
                        RecordAccessClusterOverlay(originClusters, states);
                        if (states[cluster] == AccessClusterState.AccessibleViaProvider
                            || states[cluster] == AccessClusterState.AccessibleDirect)
                        {
                            result.TopRowTile = experimentalTopTile;
                            placedAny = true;
                            if (experimentalCandidateUsesOutsideArea)
                                result.SuppressWarningNotification = true;
                            placedRampOrigins?.AddRange(localPlacedOrigins);
                            RegisterGeneratedAccesswayOrigins(tower, localPlacedOrigins);
                            ClearLastExperimentalCleanupMaterialization();
                            LogExperimentalAccessDebug($"[ATD Experimental Access] cluster={cluster.ClusterId} selected=true designations={localPlacedOrigins.Count} decidedBy={decidedBy}");
                            AccessDiagnostics.LogAccessProvided(
                                cluster.ClusterId,
                                "experimental-turning-ramp",
                                accesswayProto.Id.ToString(),
                                localPlacedOrigins[0],
                                "path",
                                decidedBy);
                            if (newPlannerOnly)
                            {
                                result.Outcome = RampPlacementOutcome.Crested;
                                yield break;
                            }
                            continue;
                        }

                        existingProviders.Remove(experimentalProvider);
                        RollBackExperimentalDesignations(localPlacedOrigins, tower, accesswayProto, reservedRampTiles);
                        RollBackLastExperimentalCleanupMaterialization(tower, reservedRampTiles);
                        states = AccessReachability.EvaluateReachability(
                            originClusters,
                            existingProviders,
                            tower,
                            terrMgr,
                            tile => IsClusterOriginReadyAndPathable(tower, tile),
                            (origin, direction) =>
                            {
                                Tile2i neighbor = new Tile2i(origin.X + direction.X, origin.Y + direction.Y);
                                return TryClusterEdgeConnectsToAccess(origin, neighbor, direction, accessWorkDepths, accesswayProto, terrMgr, out _);
                            });
                        RecordAccessClusterOverlay(originClusters, states);
                        LogExperimentalAccessDebug(
                            $"[ATD Experimental Access] cluster={cluster.ClusterId} "
                            + "post-placement provider validation failed"
                            + (newPlannerOnly
                                ? "; managed request fails closed."
                                : "; using legacy fallback."));
                    }
                    else
                    {
                        LogExperimentalAccessDebug(
                            $"[ATD Experimental Access] cluster={cluster.ClusterId} "
                            + $"placement failed reason={experimentalPlacementFailure}"
                            + (newPlannerOnly
                                ? "; managed request fails closed."
                                : "; using legacy fallback."));
                    }
                }

                if (bestCandidate != null)
                {
                    var rampCand = (RampCandidate)bestCandidate.SourceCandidate;
                    var localPlacedOrigins = new List<Tile2i>();
                    RampPlacementOutcome placementOutcome = TryPlaceRamp(
                        tower,
                        rampCand,
                        accessWorkDepths,
                        cornerHeights,
                        terrMgr,
                        accesswayProto,
                        localPlacedOrigins,
                        reservedRampTiles,
                        useLocalSurfaceReference,
                        dryRun: false,
                        out Tile2i topTile,
                        out _);

                    if (placementOutcome != RampPlacementOutcome.Failed)
                    {
                        result.TopRowTile = topTile;
                        placedAny = true;
                        placedRampOrigins?.AddRange(localPlacedOrigins);
                        RegisterGeneratedAccesswayOrigins(tower, localPlacedOrigins);

                        // Keep track of worst outcome
                        if (placementOutcome == RampPlacementOutcome.NotAccessible)
                        {
                            if (worstOutcome != RampPlacementOutcome.Failed && worstOutcome != RampPlacementOutcome.NotAccessible)
                                worstOutcome = RampPlacementOutcome.NotAccessible;
                        }
                        else if (placementOutcome == RampPlacementOutcome.Truncated)
                        {
                            if (worstOutcome != RampPlacementOutcome.Failed && worstOutcome != RampPlacementOutcome.NotAccessible && worstOutcome != RampPlacementOutcome.Truncated)
                                worstOutcome = RampPlacementOutcome.Truncated;
                        }

                        // Determine decidedBy dominant criterion
                        string decidedBy = "default";
                        var nextBest = allEvaluated.FirstOrDefault(c => c != bestCandidate && c.IsValid);
                        if (nextBest != null)
                        {
                            decidedBy = EvaluatedAccessCandidate.GetDecidedBy(bestCandidate, nextBest);
                        }
                        else if (allEvaluated.Any(c => c == bestCandidate))
                        {
                            decidedBy = "single-candidate";
                        }

                        AccessDiagnostics.LogAccessProvided(
                            cluster.ClusterId,
                            "generated-ramp",
                            accesswayProto.Id.ToString(),
                            rampCand.OreTiles[0],
                            rampCand.Direction.ToString(),
                            decidedBy);

                        // Fold new ramp tiles into existingProviders and re-flood
                        existingProviders.Add(new AccessProvider(localPlacedOrigins, reachesGround: bestCandidate.IsReachableNow));

                        states = AccessReachability.EvaluateReachability(
                            originClusters,
                            existingProviders,
                            tower,
                            terrMgr,
                            tile => IsClusterOriginReadyAndPathable(tower, tile),
                            (origin, direction) => 
                            {
                                Tile2i neighbor = new Tile2i(origin.X + direction.X, origin.Y + direction.Y);
                                return TryClusterEdgeConnectsToAccess(origin, neighbor, direction, accessWorkDepths, accesswayProto, terrMgr, out _);
                            });
                        RecordAccessClusterOverlay(originClusters, states);
                    }
                    else
                    {
                        worstOutcome = RampPlacementOutcome.Failed;
                        states[cluster] = AccessClusterState.Blocked;
                        RecordAccessClusterOverlay(originClusters, states);
                        AccessDiagnostics.LogClusterState(new AccessAnalysisResult(cluster, AccessClusterState.Blocked, AccessNeed.Mining, null, null, BlockedReason.NoCandidate, 0f));
                        if (emitNoCandidateWarnings)
                            Log.Warning($"[ATD Access] warning tower={towerPos} originCluster={cluster.ClusterId} reason=no valid accessway candidate{FormatOptionalAccessFailure(experimentalFailureSummary)}; work cannot progress");
                    }
                }
                else
                {
                    worstOutcome = RampPlacementOutcome.Failed;
                    states[cluster] = AccessClusterState.Blocked;
                    RecordAccessClusterOverlay(originClusters, states);
                    AccessDiagnostics.LogClusterState(new AccessAnalysisResult(cluster, AccessClusterState.Blocked, AccessNeed.Mining, null, null, BlockedReason.NoCandidate, 0f));
                    if (emitNoCandidateWarnings)
                        Log.Warning($"[ATD Access] warning tower={towerPos} originCluster={cluster.ClusterId} reason=no valid accessway candidate{FormatOptionalAccessFailure(experimentalFailureSummary)}; work cannot progress");
                }
            }

            if (!placedAny && !validatedExistingRoute && worstOutcome == RampPlacementOutcome.Crested)
            {
                result.Outcome = RampPlacementOutcome.Failed;
                yield break;
            }

            result.Outcome = worstOutcome;
            yield break;
        }

        private static string FormatExperimentalFailureSummary(AccessSearchResult result)
        {
            string reason = string.IsNullOrEmpty(result.FailureReason) ? "unknown" : result.FailureReason;
            if (result.Rejections.Count == 0)
                return reason;

            string topRejections = string.Join(",",
                result.Rejections
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Take(3)
                    .Select(pair => $"{pair.Key}:{pair.Value}"));
            return $"{reason}; topRejections={topRejections}";
        }

        private static Dictionary<Tile2i, string> CaptureTerrainDesignationState(
            IAreaManagingTower tower)
        {
            var result = new Dictionary<Tile2i, string>();
            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(
                tower.Area.BoundingBoxMin, tower.Area.BoundingBoxMax))
            {
                DesignationData data = designation.Data;
                result[designation.OriginTileCoord] =
                    designation.Prototype.Id.Value + ":" +
                    data.OriginTargetHeight.Value + "," +
                    data.PlusXTargetHeight.Value + "," +
                    data.PlusXyTargetHeight.Value + "," +
                    data.PlusYTargetHeight.Value;
            }
            return result;
        }

        private static List<Tile2i> GetAccessibleFixedGoalOrigins(
            IReadOnlyList<AccessOriginCluster> originClusters,
            Dictionary<AccessOriginCluster, AccessClusterState> states,
            AccessOriginCluster currentCluster)
        {
            var result = new List<Tile2i>();
            foreach (AccessOriginCluster cluster in originClusters)
            {
                if (cluster == currentCluster) continue;
                if (!states.TryGetValue(cluster, out AccessClusterState state)) continue;
                if (state != AccessClusterState.AccessibleDirect
                    && state != AccessClusterState.AccessibleViaProvider)
                    continue;

                foreach (AccessWorkOrigin origin in cluster.Origins)
                    result.Add(origin.Origin);
            }
            return result;
        }

        private static string FormatOptionalAccessFailure(string failureSummary)
            => string.IsNullOrEmpty(failureSummary)
                ? string.Empty
                : $"; experimental={failureSummary}";

        private static EvaluatedAccessCandidate? FindBestCandidateForCluster(
            IAreaManagingTower tower,
            Tile2i towerPos,
            Dict<Tile2i, int> clusterTileDepths,
            Dict<Tile2i, int> cornerHeights,
            TerrainManager terrMgr,
            TerrainDesignationProto rampProto,
            int rampWidth,
            int lateralRetryOffset,
            HashSet<Tile2i>? reservedRampTiles,
            bool useLocalSurfaceReference,
            HashSet<Tile2i>? forbiddenApproachClusterOrigins,
            out List<EvaluatedAccessCandidate> allEvaluated)
        {
            allEvaluated = new List<EvaluatedAccessCandidate>();

            List<RampCandidate> candidates = CollectRampCandidates(tower, clusterTileDepths, rampWidth, lateralRetryOffset, reservedRampTiles);
            if (candidates.Count == 0)
            {
                return null;
            }

            RefreshPathabilityAndInvalidateReachability();

            var testedMouthReachability = new Dictionary<Tile2i, bool>();
            int reachabilityChecks = 0;

            for (int candidateOrder = 0; candidateOrder < candidates.Count; candidateOrder++)
            {
                RampCandidate candidate = candidates[candidateOrder];
                RampPlacementOutcome dryOutcome = TryPlaceRamp(
                    tower,
                    candidate,
                    clusterTileDepths,
                    cornerHeights,
                    terrMgr,
                    rampProto,
                    null,
                    reservedRampTiles,
                    useLocalSurfaceReference,
                    dryRun: true,
                    out Tile2i dryTopRowTile,
                    out List<RampTilePlan> plannedTiles);

                if (dryOutcome == RampPlacementOutcome.Failed)
                {
                    continue;
                }

                // Real placement normalizes shared vertices before writing designations.
                // Score and validate that same geometry so ranking cannot select a plan
                // that changes shape (or becomes already fulfilled) only at commit time.
                NormalizeRampPlan(plannedTiles, clusterTileDepths, cornerHeights);
                if (!RampPlanMatchesDesignationOperation(plannedTiles, terrMgr, rampProto))
                {
                    continue;
                }
                if (plannedTiles.Count == 0)
                {
                    // This cluster was classified NeedsAccessway. A candidate that emits no
                    // designation cannot provide the missing connection and must not win as
                    // a zero-work legacy candidate.
                    continue;
                }

                bool approachMismatch = RampMouthApproachTargetMismatches(terrMgr, dryTopRowTile, candidate.Direction, candidate.OreTiles.Length, rampProto);
                bool approachForbidden = forbiddenApproachClusterOrigins != null && forbiddenApproachClusterOrigins.Count > 0
                    && RampMouthApproachInForbiddenCluster(dryTopRowTile, candidate.Direction, candidate.OreTiles.Length, rampProto, forbiddenApproachClusterOrigins);

                bool isMouthReachable = false;
                if (!approachMismatch && !approachForbidden)
                {
                    if (!testedMouthReachability.TryGetValue(dryTopRowTile, out isMouthReachable))
                    {
                        if (reachabilityChecks < MAX_RAMP_REACHABILITY_CHECKS)
                        {
                            isMouthReachable = IsRampMouthReachableFromTower(tower, dryTopRowTile);
                            testedMouthReachability[dryTopRowTile] = isMouthReachable;
                            reachabilityChecks++;
                        }
                        else
                        {
                            isMouthReachable = false;
                        }
                    }
                }

                // Mouth distance
                int dx = towerPos.X - dryTopRowTile.X;
                int dy = towerPos.Y - dryTopRowTile.Y;
                int mouthDistance = dx * dx + dy * dy;

                // Material moved
                int materialMoved = CalculateUselessMaterialMoved(plannedTiles, terrMgr);

                int designationCount = plannedTiles.Count;

                var evaluated = new EvaluatedAccessCandidate(
                    dryTopRowTile,
                    isValid: isMouthReachable,
                    isReachableNow: dryOutcome == RampPlacementOutcome.Crested && isMouthReachable,
                    mouthDistance: mouthDistance,
                    materialMoved: materialMoved,
                    designationCount: designationCount,
                    stableOrder: candidateOrder,
                    sourceCandidate: candidate);

                allEvaluated.Add(evaluated);
            }

            if (allEvaluated.Count == 0)
            {
                return null;
            }

            allEvaluated.Sort(EvaluatedAccessCandidate.Compare);
            return allEvaluated.FirstOrDefault(c => c.IsValid);
        }

        private static bool IsUsefulProduct(LooseProductProto product)
        {
            if (product == null) return false;
            if (product == LooseProductProto.Phantom) return false;
            if (!product.CanBeOnTerrain && product.TerrainMaterial == null) return false;

            string idStr = product.Id.ToString();
            if (idStr.IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (idStr.IndexOf("dirt", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            return true;
        }

        private static int CalculateUselessMaterialMoved(
            IEnumerable<RampTilePlan> plannedTiles,
            TerrainManager terrMgr)
        {
            float totalUseless = 0f;
            foreach (var plan in plannedTiles)
            {
                Tile2i centerTile = plan.Tile + new RelTile2i(2, 2);
                float currentSurfaceH = terrMgr.GetHeight(centerTile).Value.ToFloat();
                float targetH = (plan.NwHeight + plan.NeHeight + plan.SeHeight + plan.SwHeight) / 4.0f;

                if (targetH > currentSurfaceH)
                {
                    // Fill (target > surface): All filled volume is useless (soil/rock).
                    totalUseless += (targetH - currentSurfaceH);
                }
                else
                {
                    // Dig (target <= surface): Excavated volume minus the thickness of any useful product layers
                    float digDepth = currentSurfaceH - targetH;
                    float usefulDepth = 0f;
                    float cumulativeDepth = 0f;

                    TerrainLayerEnumerator enumerator = terrMgr.EnumerateLayers(terrMgr.GetTileIndex(centerTile));
                    while (enumerator.MoveNext())
                    {
                        TerrainMaterialThicknessSlim layer = enumerator.Current;
                        float thickness = layer.Thickness.Value.ToFloat();
                        if (cumulativeDepth >= digDepth)
                        {
                            break;
                        }
                        if (s_bedrockTerrainMaterial != null && layer.SlimId == s_bedrockTerrainMaterial.SlimId)
                        {
                            break;
                        }

                        TerrainMaterialProto mat = layer.SlimId.ToFull(terrMgr);
                        LooseProductProto minedProduct = mat.MinedProduct;
                        if (IsUsefulProduct(minedProduct))
                        {
                            float overlap = Math.Min(cumulativeDepth + thickness, digDepth) - cumulativeDepth;
                            if (overlap > 0f)
                            {
                                usefulDepth += overlap;
                            }
                        }
                        cumulativeDepth += thickness;
                    }

                    float uselessDepth = digDepth - usefulDepth;
                    if (uselessDepth > 0f)
                    {
                        totalUseless += uselessDepth;
                    }
                }
            }
            // Scale by 4 to match the 4-vertex height sum scale of the original calculation.
            return (int)Math.Round(totalUseless * 4f);
        }

        private static bool RampPlanMatchesDesignationOperation(
            IEnumerable<RampTilePlan> plannedTiles,
            TerrainManager terrMgr,
            TerrainDesignationProto rampProto)
        {
            bool isMining = RampProtoUsesMiningReadiness(rampProto);
            bool isDumping = RampProtoUsesDumpingReadiness(rampProto);
            if (!isMining && !isDumping) return true;

            const float tolerance = 0.01f;
            foreach (RampTilePlan plan in plannedTiles)
            {
                for (int y = 0; y <= 4; y++)
                {
                    float west = plan.NwHeight + (plan.SwHeight - plan.NwHeight) * (y / 4f);
                    float east = plan.NeHeight + (plan.SeHeight - plan.NeHeight) * (y / 4f);
                    for (int x = 0; x <= 4; x++)
                    {
                        float targetHeight = west + (east - west) * (x / 4f);
                        float terrainHeight = terrMgr.GetHeight(plan.Tile + new RelTile2i(x, y)).Value.ToFloat();
                        if (isMining && targetHeight > terrainHeight + tolerance) return false;
                        if (isDumping && targetHeight < terrainHeight - tolerance) return false;
                    }
                }
            }
            return true;
        }

        private static List<RampCandidate> CollectRampCandidates(
            IAreaManagingTower tower,
            Dict<Tile2i, int> tileDepths,
            int rampWidth,
            int lateralRetryOffset = 0,
            HashSet<Tile2i>? reservedRampTiles = null)
        {
            List<RampCandidate> candidates = new List<RampCandidate>();
            Tile2i towerPos;
            if (tower is IEntityWithPosition posEntity)
                towerPos = posEntity.Position2f.Tile2i;
            else
                towerPos = new Tile2i((tower.Area.BoundingBoxMin.X + tower.Area.BoundingBoxMax.X) / 2, (tower.Area.BoundingBoxMin.Y + tower.Area.BoundingBoxMax.Y) / 2);

            // Only probe from perimeter ore tiles — those that have at least one non-ore cardinal
            // neighbour.  Interior tiles (all four neighbours ore) either cannot produce a valid
            // ramp exit within the maxAttachmentDepth limit, or produce a candidate equivalent to
            // one already reachable from a nearby perimeter tile.  Skipping them can reduce the
            // candidate set by ~7× for large designation areas with no correctness loss.
            var perimeterOreTiles = new List<Tile2i>();
            foreach (Tile2i t in tileDepths.Keys)
            {
                if (!tileDepths.ContainsKey(new Tile2i(t.X + 4, t.Y)) ||
                    !tileDepths.ContainsKey(new Tile2i(t.X - 4, t.Y)) ||
                    !tileDepths.ContainsKey(new Tile2i(t.X, t.Y + 4)) ||
                    !tileDepths.ContainsKey(new Tile2i(t.X, t.Y - 4)))
                {
                    perimeterOreTiles.Add(t);
                }
            }

            foreach (Tile2i oreTile in perimeterOreTiles)
            {
                foreach (Tile2i direction in s_cardinalDirections)
                {
                    Tile2i perpendicular = GetPerpendicular(direction);
                    for (int startOffset = -rampWidth + 1; startOffset <= 0; startOffset++)
                    {
                        Tile2i firstOreTile = Offset(oreTile, Scale(perpendicular, startOffset));
                        if (lateralRetryOffset == 0)
                        {
                            TryAddRampCandidate(candidates, tower, towerPos, tileDepths, firstOreTile, direction, perpendicular, rampWidth, reservedRampTiles);
                        }
                        else
                        {
                            TryAddRampCandidate(
                                candidates,
                                tower,
                                towerPos,
                                tileDepths,
                                Offset(firstOreTile, Scale(perpendicular, -lateralRetryOffset)),
                                direction,
                                perpendicular,
                                rampWidth,
                                reservedRampTiles);
                            TryAddRampCandidate(
                                candidates,
                                tower,
                                towerPos,
                                tileDepths,
                                Offset(firstOreTile, Scale(perpendicular, lateralRetryOffset)),
                                direction,
                                perpendicular,
                                rampWidth,
                                reservedRampTiles);
                        }
                    }
                }
            }

            int anchoredCandidateCount = candidates.Count;
            AddPerimeterAccessPathCandidates(candidates, tower, towerPos, tileDepths, rampWidth, lateralRetryOffset, reservedRampTiles);
            if (candidates.Count > anchoredCandidateCount)
            {
                LogDebug(string.Format(
                    "Access path candidate search: anchored={0}, perimeter={1}, total={2}, width={3}, lateralOffset={4}.",
                    anchoredCandidateCount,
                    candidates.Count - anchoredCandidateCount,
                    candidates.Count,
                    rampWidth,
                    lateralRetryOffset));
            }

            return candidates;
        }

        private static void AddPerimeterAccessPathCandidates(
            List<RampCandidate> candidates,
            IAreaManagingTower tower,
            Tile2i towerPos,
            Dict<Tile2i, int> clusterTiles,
            int pathWidth,
            int lateralRetryOffset,
            HashSet<Tile2i>? reservedPathTiles)
        {
            var seen = new HashSet<string>();
            foreach (RampCandidate candidate in candidates)
                seen.Add(BuildRampCandidateKey(candidate));

            foreach (Tile2i edgeTile in clusterTiles.Keys)
            {
                foreach (Tile2i direction in s_cardinalDirections)
                {
                    if (clusterTiles.ContainsKey(Offset(edgeTile, direction)))
                        continue;

                    Tile2i perpendicular = GetPerpendicular(direction);
                    for (int startOffset = -pathWidth + 1; startOffset <= 0; startOffset++)
                    {
                        Tile2i firstEdgeTile = Offset(edgeTile, Scale(perpendicular, startOffset));
                        if (lateralRetryOffset == 0)
                        {
                            TryAddPerimeterAccessPathCandidate(
                                candidates,
                                seen,
                                tower,
                                towerPos,
                                clusterTiles,
                                firstEdgeTile,
                                direction,
                                perpendicular,
                                pathWidth,
                                reservedPathTiles);
                        }
                        else
                        {
                            TryAddPerimeterAccessPathCandidate(
                                candidates,
                                seen,
                                tower,
                                towerPos,
                                clusterTiles,
                                Offset(firstEdgeTile, Scale(perpendicular, -lateralRetryOffset)),
                                direction,
                                perpendicular,
                                pathWidth,
                                reservedPathTiles);
                            TryAddPerimeterAccessPathCandidate(
                                candidates,
                                seen,
                                tower,
                                towerPos,
                                clusterTiles,
                                Offset(firstEdgeTile, Scale(perpendicular, lateralRetryOffset)),
                                direction,
                                perpendicular,
                                pathWidth,
                                reservedPathTiles);
                        }
                    }
                }
            }
        }

        private static void TryAddPerimeterAccessPathCandidate(
            List<RampCandidate> candidates,
            HashSet<string> seen,
            IAreaManagingTower tower,
            Tile2i towerPos,
            Dict<Tile2i, int> clusterTiles,
            Tile2i firstEdgeTile,
            Tile2i direction,
            Tile2i perpendicular,
            int pathWidth,
            HashSet<Tile2i>? reservedPathTiles)
        {
            Tile2i[] edgeTiles = new Tile2i[pathWidth];
            Tile2i[] firstPathTiles = new Tile2i[pathWidth];
            bool[] laneHasCluster = Enumerable.Repeat(true, pathWidth).ToArray();
            int[] laneAttachmentDepths = new int[pathWidth];

            for (int lane = 0; lane < pathWidth; lane++)
            {
                Tile2i edgeTile = Offset(firstEdgeTile, Scale(perpendicular, lane));
                Tile2i pathTile = Offset(edgeTile, direction);

                if (!clusterTiles.ContainsKey(edgeTile))
                    return;
                if (clusterTiles.ContainsKey(pathTile))
                    return;

                int rampDepth = 1;
                if (!IsFreeRampTile(tower, pathTile, clusterTiles, rampDepth, direction, reservedPathTiles))
                    return;

                edgeTiles[lane] = edgeTile;
                firstPathTiles[lane] = pathTile;
                laneAttachmentDepths[lane] = 0;
            }

            var candidate = new RampCandidate(
                edgeTiles,
                laneHasCluster,
                direction,
                firstPathTiles,
                laneAttachmentDepths,
                ScoreRampCandidate(towerPos, edgeTiles, direction, firstPathTiles, laneHasCluster, laneAttachmentDepths) - 100);

            string key = BuildRampCandidateKey(candidate);
            if (!seen.Add(key))
                return;

            candidates.Add(candidate);
        }

        private static string BuildRampCandidateKey(RampCandidate candidate)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(candidate.Direction.X).Append(',').Append(candidate.Direction.Y);
            for (int i = 0; i < candidate.OreTiles.Length; i++)
            {
                sb.Append('|')
                    .Append(candidate.OreTiles[i].X).Append(',').Append(candidate.OreTiles[i].Y)
                    .Append(':')
                    .Append(candidate.LaneAttachmentDepths[i]);
            }

            return sb.ToString();
        }

        private static void TryAddRampCandidate(List<RampCandidate> candidates, IAreaManagingTower tower, Tile2i towerPos, Dict<Tile2i, int> tileDepths, Tile2i firstOreTile, Tile2i direction, Tile2i perpendicular, int rampWidth, HashSet<Tile2i>? reservedRampTiles)
        {
            Tile2i[] edgeOreTiles = new Tile2i[rampWidth];
            bool[] edgeLaneHasOre = new bool[rampWidth];
            int firstOreLane = -1;

            for (int lane = 0; lane < rampWidth; lane++)
            {
                Tile2i oreTile = Offset(firstOreTile, Scale(perpendicular, lane));
                edgeOreTiles[lane] = oreTile;

                if (tileDepths.ContainsKey(oreTile))
                {
                    edgeLaneHasOre[lane] = true;
                    if (firstOreLane < 0)
                    {
                        firstOreLane = lane;
                    }
                }
                else
                {
                    edgeLaneHasOre[lane] = false;
                }
            }

            if (firstOreLane < 0)
            {
                return;
            }

            if (!TryFindRampWidthOreAnchor(edgeOreTiles, direction, tileDepths, rampWidth + 8, rampWidth,
                out Tile2i[] anchorOreTiles, out int[] laneAttachmentDepths))
            {
                return;
            }

            bool[] laneHasOre = Enumerable.Repeat(true, rampWidth).ToArray();
            int maxAttachmentDepth = laneAttachmentDepths.Max();

            Tile2i[] rampTiles = new Tile2i[rampWidth];
            for (int lane = 0; lane < rampWidth; lane++)
            {
                rampTiles[lane] = Offset(anchorOreTiles[lane], Scale(direction, maxAttachmentDepth + 1));
                int rampDepth = Math.Max(1, (maxAttachmentDepth + 1) - laneAttachmentDepths[lane]);
                if (!IsFreeRampTile(tower, rampTiles[lane], tileDepths, rampDepth, direction, reservedRampTiles))
                {
                    return;
                }
            }

            int oreSumX = 0;
            int oreSumY = 0;
            for (int lane = 0; lane < rampWidth; lane++)
            {
                oreSumX += anchorOreTiles[lane].X;
                oreSumY += anchorOreTiles[lane].Y;
            }

            int oreMidX = oreSumX / rampWidth;
            int oreMidY = oreSumY / rampWidth;
            // No direction filter here: all four cardinal directions are candidates.
            // ScoreRampCandidate ranks by alignment toward the tower (alignmentScore term), so the
            // best-aligned direction is tried first. Directions pointing away from the tower are
            // kept as fallbacks for cases where the preferred corridor is blocked by a building or
            // void — IsFreeRampTile will reject those blocked corridors during placement.

            for (int lane = 0; lane < rampWidth; lane++)
            {
                for (int step = 1; step <= maxAttachmentDepth + 1; step++)
                {
                    Tile2i tile = Offset(anchorOreTiles[lane], Scale(direction, step));

                    if (step <= laneAttachmentDepths[lane])
                    {
                        if (!tileDepths.ContainsKey(tile))
                        {
                            return;
                        }
                    }
                    else
                    {
                        int rampDepth = (maxAttachmentDepth + 2) - step;
                        if (!IsFreeRampTile(tower, tile, tileDepths, rampDepth, direction, reservedRampTiles))
                        {
                            return;
                        }
                    }
                }
            }

            candidates.Add(new RampCandidate(
                anchorOreTiles,
                laneHasOre,
                direction,
                rampTiles,
                laneAttachmentDepths,
                ScoreRampCandidate(towerPos, anchorOreTiles, direction, rampTiles, laneHasOre, laneAttachmentDepths)));
        }

        private static bool TryFindRampWidthOreAnchor(
            Tile2i[] edgeOreTiles,
            Tile2i direction,
            Dict<Tile2i, int> tileDepths,
            int maxInsetSteps,
            int rampWidth,
            out Tile2i[] anchorOreTiles,
            out int[] laneAttachmentDepths)
        {
            int laneCount = edgeOreTiles.Length;
            Tile2i inward = new Tile2i(-direction.X, -direction.Y);
            anchorOreTiles = Array.Empty<Tile2i>();
            laneAttachmentDepths = Array.Empty<int>();
            int bestDepthSpread = int.MaxValue;
            int bestMaxDepth = int.MaxValue;

            for (int inset = 0; inset <= maxInsetSteps; inset++)
            {
                Tile2i[] candidateRow = new Tile2i[laneCount];
                bool allLanesHaveOre = true;

                for (int lane = 0; lane < laneCount; lane++)
                {
                    Tile2i tile = Offset(edgeOreTiles[lane], Scale(inward, inset));
                    candidateRow[lane] = tile;
                    if (!tileDepths.ContainsKey(tile))
                    {
                        allLanesHaveOre = false;
                        break;
                    }
                }

                if (allLanesHaveOre)
                {
                    int[] candidateDepths = new int[laneCount];
                    int minDepth = int.MaxValue;
                    int maxDepth = int.MinValue;
                    for (int lane = 0; lane < laneCount; lane++)
                    {
                        int depth = CountForwardOreDepth(candidateRow[lane], direction, tileDepths);
                        candidateDepths[lane] = depth;
                        minDepth = Math.Min(minDepth, depth);
                        maxDepth = Math.Max(maxDepth, depth);
                    }

                    int depthSpread = maxDepth - minDepth;
                    if (depthSpread < bestDepthSpread || (depthSpread == bestDepthSpread && maxDepth < bestMaxDepth))
                    {
                        bestDepthSpread = depthSpread;
                        bestMaxDepth = maxDepth;
                        anchorOreTiles = candidateRow;
                        laneAttachmentDepths = candidateDepths;
                    }

                    if (depthSpread == 0)
                        break;
                }
            }

            if (anchorOreTiles.Length == 0)
                return false;

            // Wider ramps hit natural ore-front jaggedness more often. The ramp placement logic
            // already uses per-lane face heights and connector tiles, so slightly larger spreads
            // are still handled coherently.
            int maxAllowedDepthSpread = rampWidth >= 5 ? 2 : 1;
            return bestDepthSpread <= maxAllowedDepthSpread;
        }

        private static RampPlacementOutcome TryPlaceRamp(IAreaManagingTower tower, RampCandidate candidate, Dict<Tile2i, int> tileDepths, Dict<Tile2i, int> cornerHeights, TerrainManager terrMgr, TerrainDesignationProto rampProto, List<Tile2i>? placedRampOrigins, HashSet<Tile2i>? reservedRampTiles, bool useLocalSurfaceReference, bool dryRun, out Tile2i topRowTile, out List<RampTilePlan> plannedTiles)
        {
            topRowTile = default;
            plannedTiles = new List<RampTilePlan>();
            int laneCount = candidate.OreTiles.Length;
            int[] laneFirstEdgeHeights = new int[laneCount];
            int[] laneSecondEdgeHeights = new int[laneCount];
            bool[] laneProcessed = new bool[laneCount];

            for (int lane = 0; lane < laneCount; lane++)
            {
                if (!candidate.LaneHasOre[lane])
                {
                    continue;
                }

                // Use the face tile (last ore tile in direction from anchor) so heights match the
                // actual boundary between ore and the first ramp tile, not the interior anchor edge.
                Tile2i faceTile = Offset(candidate.OreTiles[lane],
                    Scale(candidate.Direction, candidate.LaneAttachmentDepths[lane]));
                if (!TryGetOreLaneEdgeHeights(faceTile, candidate.Direction, cornerHeights,
                    out int firstEdgeHeight, out int secondEdgeHeight))
                {
                    return RampPlacementOutcome.Failed;
                }

                laneFirstEdgeHeights[lane] = firstEdgeHeight;
                laneSecondEdgeHeights[lane] = secondEdgeHeight;
                laneProcessed[lane] = true;
            }

            // Fill missing lanes from nearest ore lane so every row has a complete boundary.
            for (int lane = 0; lane < laneCount; lane++)
            {
                if (laneProcessed[lane])
                {
                    continue;
                }

                int nearestOreLane = FindNearestOreLane(candidate.LaneHasOre, lane);
                int laneDistance = Math.Abs(lane - nearestOreLane);
                int edgeDrop = Math.Min(2, laneDistance);
                laneFirstEdgeHeights[lane] = Math.Max(1, laneFirstEdgeHeights[nearestOreLane] - edgeDrop);
                laneSecondEdgeHeights[lane] = Math.Max(1, laneSecondEdgeHeights[nearestOreLane] - edgeDrop);
                laneProcessed[lane] = true;
            }

            Tile2i towerPos;
            if (tower is IEntityWithPosition posEntity)
            {
                towerPos = posEntity.Position2f.Tile2i;
            }
            else
            {
                towerPos = new Tile2i(
                    (tower.Area.BoundingBoxMin.X + tower.Area.BoundingBoxMax.X) / 2,
                    (tower.Area.BoundingBoxMin.Y + tower.Area.BoundingBoxMax.Y) / 2);
            }

            int towerReferenceHeight = GetSurfaceHeight(terrMgr, towerPos);

            // If the mouth approach (one tile beyond the mouth row in the ramp direction) lands
            // inside a non-ramp designation, the post-work z of that approach is the designation's
            // TARGET z at the shared edge — not the current surface. Using current surface here
            // would force the body to climb up to it (a wedge), even though the approach will
            // ultimately be excavated/filled to its target. By switching the reference to the
            // approach target, a face at the same target z produces a FLAT bridge body across
            // the gap (designated to the same target). When face z == approach target z, the
            // body stays at that z; when they differ, a partial climb still happens. No facing
            // designation → behave as before (reference = tower surface).
            int approachReferenceHeight = towerReferenceHeight;
            bool hasApproachReference = TryGetMouthApproachTargetHeight(
                candidate, rampProto, out int approachTargetZ);
            if (hasApproachReference)
                approachReferenceHeight = approachTargetZ;

            int[] currentBoundaryHeights = BuildEdgeBoundaryHeights(laneFirstEdgeHeights, laneSecondEdgeHeights);
            Tile2i[] currentTiles = (Tile2i[])candidate.RampTiles.Clone();
            int maxSteps = Mathf.Max(tileDepths.Count + 32, 32);
            int maxAttachmentDepth = candidate.LaneAttachmentDepths.Max();

            // Early-exit: if the ore face boundary already sits at the reference height, no ramp
            // body is needed (flat terrain / same-Z case). The in-loop stop check fires only AFTER
            // AddRampRowPlans, so without this guard one flat tile would always be placed.
            // Exception: when bridging into another designation (hasApproachReference), we DO want
            // that one flat tile placed — it's the bridge body that connects the two clusters.
            if (!hasApproachReference)
            {
                bool atOrAboveTowerLevelInitial = true;
                for (int c = 0; c < currentBoundaryHeights.Length; c++)
                {
                    if (currentBoundaryHeights[c] < approachReferenceHeight)
                    { atOrAboveTowerLevelInitial = false; break; }
                }
                int[] initialRefHeights = useLocalSurfaceReference || atOrAboveTowerLevelInitial
                    ? BuildSurfaceBoundaryHeights(terrMgr, candidate.RampTiles)
                    : BuildConstantBoundaryHeights(laneCount, approachReferenceHeight);
                if (BoundaryHeightsMatch(currentBoundaryHeights, initialRefHeights))
                {
                    AddGapConnectorPlans(plannedTiles, candidate, laneFirstEdgeHeights, laneSecondEdgeHeights);
                    if (!dryRun)
                    {
                        ApplyRampPlan(tower, plannedTiles, tileDepths, cornerHeights, rampProto, placedRampOrigins);
                    }
                    topRowTile = candidate.RampTiles[0];
                    return RampPlacementOutcome.Crested;
                }
            }

            for (int rampStepIndex = 0; rampStepIndex < maxSteps; rampStepIndex++)
            {
                for (int lane = 0; lane < laneCount; lane++)
                {
                    int laneBaseDepth = Math.Max(1, (maxAttachmentDepth + 1) - candidate.LaneAttachmentDepths[lane]);
                    int rampDepth = Math.Max(1, laneBaseDepth - rampStepIndex);
                    if (!IsFreeRampTile(tower, currentTiles[lane], tileDepths, rampDepth, candidate.Direction, reservedRampTiles))
                    {
                        return RampPlacementOutcome.Failed;
                    }
                }

                int[] referenceBoundaryHeights;
                if (useLocalSurfaceReference)
                {
                    referenceBoundaryHeights = BuildSurfaceBoundaryHeights(terrMgr, currentTiles);
                }
                else
                {
                    // Once the incline has reached the tower's z-level, switch to local surface
                    // heights so the ramp can continue rising to crest terrain that sits above
                    // the tower's elevation. The pathability check in TryPlaceRampCandidates
                    // ensures only accessible crested ramps are committed first.
                    // Exception: when bridging into another designation (hasApproachReference),
                    // we want a flat body at the approach z, NOT a wedge climbing back to surface.
                    bool atOrAboveTowerLevel = true;
                    for (int c = 0; c < currentBoundaryHeights.Length; c++)
                    {
                        if (currentBoundaryHeights[c] < approachReferenceHeight)
                        {
                            atOrAboveTowerLevel = false;
                            break;
                        }
                    }
                    referenceBoundaryHeights = (atOrAboveTowerLevel && !hasApproachReference)
                        ? BuildSurfaceBoundaryHeights(terrMgr, currentTiles)
                        : BuildConstantBoundaryHeights(laneCount, approachReferenceHeight);
                }
                int[] nextBoundaryHeights = new int[laneCount + 1];
                for (int corner = 0; corner < currentBoundaryHeights.Length; corner++)
                {
                    int currentHeight = currentBoundaryHeights[corner];
                    int referenceHeight = referenceBoundaryHeights[corner];
                    if (currentHeight < referenceHeight)
                    {
                        nextBoundaryHeights[corner] = currentHeight + 1;
                    }
                    else if (currentHeight > referenceHeight)
                    {
                        nextBoundaryHeights[corner] = currentHeight - 1;
                    }
                    else
                    {
                        nextBoundaryHeights[corner] = currentHeight;
                    }
                }

                // Keep side-to-side seams coherent even when one lane starts lower.
                for (int pass = 0; pass < laneCount; pass++)
                {
                    for (int i = 1; i < nextBoundaryHeights.Length; i++)
                    {
                        if (nextBoundaryHeights[i] > nextBoundaryHeights[i - 1] + 1)
                        {
                            nextBoundaryHeights[i] = nextBoundaryHeights[i - 1] + 1;
                        }
                    }

                    for (int i = nextBoundaryHeights.Length - 2; i >= 0; i--)
                    {
                        if (nextBoundaryHeights[i] > nextBoundaryHeights[i + 1] + 1)
                        {
                            nextBoundaryHeights[i] = nextBoundaryHeights[i + 1] + 1;
                        }
                    }
                }

                BuildRowTileHeights(candidate.Direction, currentBoundaryHeights, nextBoundaryHeights,
                    out int[] nwHeights, out int[] neHeights, out int[] seHeights, out int[] swHeights);

                AddRampRowPlans(plannedTiles, currentTiles, nwHeights, neHeights, seHeights, swHeights);
                bool reachedReferenceLevel = BoundaryHeightsMatch(nextBoundaryHeights, referenceBoundaryHeights);
                bool usesMiningReadiness = RampProtoUsesMiningReadiness(rampProto);
                bool usesDumpingReadiness = RampProtoUsesDumpingReadiness(rampProto);
                bool hasReadyMouthDesignation = usesMiningReadiness
                    ? reachedReferenceLevel && RowHasReadyMiningDesignation(rampProto, terrMgr, currentTiles, nwHeights, neHeights, seHeights, swHeights)
                    : reachedReferenceLevel
                        && usesDumpingReadiness
                        && RowHasReadyDumpingDesignation(rampProto, terrMgr, currentTiles, nwHeights, neHeights, seHeights, swHeights);

                bool isAboveSurfaceEverywhere = true;
                for (int lane = 0; lane < laneCount; lane++)
                {
                    int rowTopHeight = Math.Max(Math.Max(nwHeights[lane], neHeights[lane]), Math.Max(seHeights[lane], swHeights[lane]));
                    if (rowTopHeight < GetSurfaceHeight(terrMgr, currentTiles[lane]))
                    {
                        isAboveSurfaceEverywhere = false;
                        break;
                    }
                }

                if (hasReadyMouthDesignation
                    || (isAboveSurfaceEverywhere
                        && (reachedReferenceLevel
                            || (!usesMiningReadiness && !usesDumpingReadiness)))
                    || (hasApproachReference && reachedReferenceLevel))
                {
                    AddGapConnectorPlans(plannedTiles, candidate, laneFirstEdgeHeights, laneSecondEdgeHeights);
                    if (!dryRun)
                    {
                        ApplyRampPlan(tower, plannedTiles, tileDepths, cornerHeights, rampProto, placedRampOrigins);
                    }

                    topRowTile = currentTiles[0];
                    return RampPlacementOutcome.Crested;
                }

                Tile2i[] nextTiles = new Tile2i[laneCount];
                bool allInsideTower = true;
                for (int lane = 0; lane < laneCount; lane++)
                {
                    Tile2i nextTile = Offset(currentTiles[lane], candidate.Direction);
                    nextTiles[lane] = nextTile;
                    if (!IsInsideTowerArea(tower, nextTile))
                    {
                        allInsideTower = false;
                    }
                }

                if (!allInsideTower)
                {
                    AddGapConnectorPlans(plannedTiles, candidate, laneFirstEdgeHeights, laneSecondEdgeHeights);
                    if (!dryRun)
                    {
                        ApplyRampPlan(tower, plannedTiles, tileDepths, cornerHeights, rampProto, placedRampOrigins);
                    }

                    topRowTile = currentTiles[0];
                    return RampPlacementOutcome.Truncated;
                }

                for (int lane = 0; lane < laneCount; lane++)
                {
                    int laneBaseDepth = Math.Max(1, (maxAttachmentDepth + 1) - candidate.LaneAttachmentDepths[lane]);
                    int rampDepth = Math.Max(1, laneBaseDepth - (rampStepIndex + 1));
                    if (!IsFreeRampTile(tower, nextTiles[lane], tileDepths, rampDepth, candidate.Direction, reservedRampTiles))
                    {
                        return RampPlacementOutcome.Failed;
                    }
                }

                currentBoundaryHeights = nextBoundaryHeights;
                currentTiles = nextTiles;
            }

            return RampPlacementOutcome.Failed;
        }

        private static bool BoundaryHeightsMatch(int[] left, int[] right)
        {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        private static bool RampProtoUsesMiningReadiness(TerrainDesignationProto rampProto)
        {
            return s_desigManager != null
                && s_miningProto != null
                && rampProto == s_miningProto
                && rampProto.IsFulfilledMiningFn.HasValue;
        }

        private static bool RampProtoUsesDumpingReadiness(TerrainDesignationProto rampProto)
        {
            return s_desigManager != null
                && s_dumpingProto != null
                && rampProto == s_dumpingProto
                && rampProto.IsFulfilledDumpingFn.HasValue;
        }

        private static bool RowHasReadyMiningDesignation(
            TerrainDesignationProto rampProto,
            TerrainManager terrMgr,
            Tile2i[] tiles,
            int[] nwHeights,
            int[] neHeights,
            int[] seHeights,
            int[] swHeights)
        {
            if (!RampProtoUsesMiningReadiness(rampProto))
                return false;

            for (int lane = 0; lane < tiles.Length; lane++)
            {
                DesignationData data = new DesignationData(
                    tiles[lane],
                    new HeightTilesI(nwHeights[lane]),
                    new HeightTilesI(neHeights[lane]),
                    new HeightTilesI(seHeights[lane]),
                    new HeightTilesI(swHeights[lane]));
                if (IsProspectiveMiningDesignationReady(rampProto, terrMgr, data))
                    return true;
            }

            return false;
        }

        private static bool IsProspectiveMiningDesignationReady(
            TerrainDesignationProto rampProto,
            TerrainManager terrMgr,
            DesignationData data)
        {
            return IsProspectiveDesignationReady(
                rampProto, terrMgr, data, AccessHandoffOperation.Mining);
        }

        private static bool RowHasReadyDumpingDesignation(
            TerrainDesignationProto rampProto,
            TerrainManager terrMgr,
            Tile2i[] tiles,
            int[] nwHeights,
            int[] neHeights,
            int[] seHeights,
            int[] swHeights)
        {
            if (!RampProtoUsesDumpingReadiness(rampProto))
                return false;

            for (int lane = 0; lane < tiles.Length; lane++)
            {
                DesignationData data = new DesignationData(
                    tiles[lane],
                    new HeightTilesI(nwHeights[lane]),
                    new HeightTilesI(neHeights[lane]),
                    new HeightTilesI(seHeights[lane]),
                    new HeightTilesI(swHeights[lane]));
                if (IsProspectiveDumpingDesignationReady(rampProto, terrMgr, data))
                    return true;
            }

            return false;
        }

        private static bool IsProspectiveDumpingDesignationReady(
            TerrainDesignationProto rampProto,
            TerrainManager terrMgr,
            DesignationData data)
        {
            return IsProspectiveDesignationReady(
                rampProto, terrMgr, data, AccessHandoffOperation.Dumping);
        }

        private static bool IsProspectiveDesignationReady(
            TerrainDesignationProto proto,
            TerrainManager terrMgr,
            DesignationData data,
            AccessHandoffOperation operation)
        {
            if (!TryBuildProspectiveFulfilledBitmap(
                    proto, terrMgr, data, operation, out uint fulfilledBitmap))
                return false;

            return fulfilledBitmap != ALL_DESIGNATION_TILES_MASK
                && (fulfilledBitmap & READY_PERIMETER_MASK) != 0;
        }

        private static bool TryBuildProspectiveFulfilledBitmap(
            TerrainDesignationProto proto,
            TerrainManager terrMgr,
            DesignationData data,
            AccessHandoffOperation operation,
            out uint fulfilledBitmap)
        {
            fulfilledBitmap = 0;
            if (s_desigManager == null) return false;
            if (operation == AccessHandoffOperation.Mining && !proto.IsFulfilledMiningFn.HasValue)
                return false;
            if (operation == AccessHandoffOperation.Dumping && !proto.IsFulfilledDumpingFn.HasValue)
                return false;
            if (operation == AccessHandoffOperation.None) return false;

            for (int y = 0; y <= 4; y++)
            {
                for (int x = 0; x <= 4; x++)
                {
                    if (IsProspectiveDesignationTileFulfilled(proto, terrMgr, data, operation, x, y))
                        fulfilledBitmap |= GetDesignationMask(x, y);
                }
            }
            return true;
        }

        private static bool IsProspectiveDesignationTileFulfilled(
            TerrainDesignationProto proto,
            TerrainManager terrMgr,
            DesignationData data,
            AccessHandoffOperation operation,
            int x,
            int y)
        {
            if (s_desigManager == null) return false;
            if (operation == AccessHandoffOperation.Mining && !proto.IsFulfilledMiningFn.HasValue)
                return false;
            if (operation == AccessHandoffOperation.Dumping && !proto.IsFulfilledDumpingFn.HasValue)
                return false;
            if (operation == AccessHandoffOperation.None) return false;

            Tile2i tile = data.OriginTile + new RelTile2i(x, y);
            Tile2iAndIndex tileAndIndex = terrMgr.ExtendTileIndex(tile);
            HeightTilesF targetHeight = GetDesignationTargetHeightAt(data, x, y);
            bool upperEdge = x == 4 || y == 4;
            return operation == AccessHandoffOperation.Mining
                ? proto.IsFulfilledMiningFn.Value(s_desigManager, tileAndIndex, targetHeight, upperEdge)
                : proto.IsFulfilledDumpingFn.Value(s_desigManager, tileAndIndex, targetHeight, upperEdge);
        }

        private static HeightTilesF GetDesignationTargetHeightAt(DesignationData data, int x, int y)
        {
            HeightTilesF west = data.OriginTargetHeight.HeightTilesF.Lerp(data.PlusYTargetHeight.HeightTilesF, y, 4);
            HeightTilesF east = data.PlusXTargetHeight.HeightTilesF.Lerp(data.PlusXyTargetHeight.HeightTilesF, y, 4);
            return west.Lerp(east, x, 4);
        }

        private static uint GetDesignationMask(int x, int y)
        {
            return (uint)(1 << (x + y * 5));
        }

        private static void AddRampRowPlans(List<RampTilePlan> plannedTiles, Tile2i[] tiles, int[] nwHeights, int[] neHeights, int[] seHeights, int[] swHeights)
        {
            for (int lane = 0; lane < tiles.Length; lane++)
            {
                plannedTiles.Add(new RampTilePlan(tiles[lane], nwHeights[lane], neHeights[lane], seHeights[lane], swHeights[lane]));
            }
        }

        private static void AddGapConnectorPlans(List<RampTilePlan> plannedTiles, RampCandidate candidate, int[] laneFirstEdgeHeights, int[] laneSecondEdgeHeights)
        {
            int maxAttachDepth = candidate.LaneAttachmentDepths.Max();
            if (maxAttachDepth == 0) return; // all lanes flush — no gap possible

            int[] rampBoundaryHeights = BuildEdgeBoundaryHeights(laneFirstEdgeHeights, laneSecondEdgeHeights);

            int laneCount = candidate.LaneAttachmentDepths.Length;
            for (int lane = 0; lane < laneCount; lane++)
            {
                int laneDepth = candidate.LaneAttachmentDepths[lane];
                for (int step = laneDepth + 1; step <= maxAttachDepth; step++)
                {
                    Tile2i gapTile = Offset(candidate.OreTiles[lane], Scale(candidate.Direction, step));
                    plannedTiles.Add(CreateGapConnectorPlan(
                        gapTile,
                        candidate.Direction,
                        laneFirstEdgeHeights[lane],
                        laneSecondEdgeHeights[lane],
                        rampBoundaryHeights[lane],
                        rampBoundaryHeights[lane + 1]));
                }
            }
        }

        private static RampTilePlan CreateGapConnectorPlan(
            Tile2i tile,
            Tile2i direction,
            int backFirstHeight,
            int backSecondHeight,
            int frontFirstHeight,
            int frontSecondHeight)
        {
            int nwHeight;
            int neHeight;
            int seHeight;
            int swHeight;

            if (direction.X > 0)
            {
                nwHeight = backFirstHeight;
                neHeight = frontFirstHeight;
                seHeight = frontSecondHeight;
                swHeight = backSecondHeight;
            }
            else if (direction.X < 0)
            {
                nwHeight = frontFirstHeight;
                neHeight = backFirstHeight;
                seHeight = backSecondHeight;
                swHeight = frontSecondHeight;
            }
            else if (direction.Y > 0)
            {
                nwHeight = backFirstHeight;
                neHeight = backSecondHeight;
                seHeight = frontSecondHeight;
                swHeight = frontFirstHeight;
            }
            else
            {
                nwHeight = frontFirstHeight;
                neHeight = frontSecondHeight;
                seHeight = backSecondHeight;
                swHeight = backFirstHeight;
            }

            return new RampTilePlan(tile, nwHeight, neHeight, seHeight, swHeight);
        }

        private static void ApplyRampPlan(IAreaManagingTower tower, List<RampTilePlan> plannedTiles, Dict<Tile2i, int> tileDepths, Dict<Tile2i, int> cornerHeights, TerrainDesignationProto rampProto, List<Tile2i>? placedRampOrigins)
        {
            NormalizeRampPlan(plannedTiles, tileDepths, cornerHeights);

            foreach (RampTilePlan plannedTile in plannedTiles)
            {
                PlaceDesignation(tower, rampProto, plannedTile.Tile, plannedTile.NwHeight, plannedTile.NeHeight, plannedTile.SeHeight, plannedTile.SwHeight, placedRampOrigins);
            }
        }

        private static void NormalizeRampPlan(List<RampTilePlan> plannedTiles, Dict<Tile2i, int> tileDepths, Dict<Tile2i, int> cornerHeights)
        {
            if (plannedTiles.Count == 0)
            {
                return;
            }

            var plannedTileSet = new HashSet<Tile2i>(plannedTiles.Select(tile => tile.Tile));
            var vertexAccumulators = new Dictionary<Tile2i, RampVertexAccumulator>();
            var vertexEdges = new List<RampVertexEdge>(plannedTiles.Count * 4);

            foreach (RampTilePlan plannedTile in plannedTiles)
            {
                GetTileCornerCoordinates(plannedTile.Tile, out Tile2i nwCorner, out Tile2i neCorner, out Tile2i seCorner, out Tile2i swCorner);

                bool touchesWestOre = BordersOreDesignation(plannedTile.Tile.AddX(-4), plannedTileSet, tileDepths);
                bool touchesEastOre = BordersOreDesignation(plannedTile.Tile.AddX(4), plannedTileSet, tileDepths);
                bool touchesNorthOre = BordersOreDesignation(plannedTile.Tile.AddY(-4), plannedTileSet, tileDepths);
                bool touchesSouthOre = BordersOreDesignation(plannedTile.Tile.AddY(4), plannedTileSet, tileDepths);

                AddVertexCandidate(vertexAccumulators, nwCorner, GetAnchoredCornerHeight(cornerHeights, nwCorner, plannedTile.NwHeight), touchesWestOre || touchesNorthOre);
                AddVertexCandidate(vertexAccumulators, neCorner, GetAnchoredCornerHeight(cornerHeights, neCorner, plannedTile.NeHeight), touchesEastOre || touchesNorthOre);
                AddVertexCandidate(vertexAccumulators, seCorner, GetAnchoredCornerHeight(cornerHeights, seCorner, plannedTile.SeHeight), touchesEastOre || touchesSouthOre);
                AddVertexCandidate(vertexAccumulators, swCorner, GetAnchoredCornerHeight(cornerHeights, swCorner, plannedTile.SwHeight), touchesWestOre || touchesSouthOre);

                vertexEdges.Add(new RampVertexEdge(nwCorner, neCorner));
                vertexEdges.Add(new RampVertexEdge(neCorner, seCorner));
                vertexEdges.Add(new RampVertexEdge(seCorner, swCorner));
                vertexEdges.Add(new RampVertexEdge(swCorner, nwCorner));
            }

            var vertexStates = new Dictionary<Tile2i, RampVertexState>(vertexAccumulators.Count);
            foreach (KeyValuePair<Tile2i, RampVertexAccumulator> kvp in vertexAccumulators)
            {
                RampVertexAccumulator accumulator = kvp.Value;
                int initialHeight = accumulator.HasFixedHeight ? accumulator.FixedHeight : accumulator.HeightSum / Math.Max(1, accumulator.Samples);
                vertexStates[kvp.Key] = new RampVertexState(initialHeight, accumulator.HasFixedHeight);
            }

            RelaxRampVertexHeights(vertexStates, vertexEdges);

            for (int index = 0; index < plannedTiles.Count; index++)
            {
                RampTilePlan plannedTile = plannedTiles[index];
                GetTileCornerCoordinates(plannedTile.Tile, out Tile2i nwCorner, out Tile2i neCorner, out Tile2i seCorner, out Tile2i swCorner);
                plannedTile.NwHeight = vertexStates[nwCorner].Height;
                plannedTile.NeHeight = vertexStates[neCorner].Height;
                plannedTile.SeHeight = vertexStates[seCorner].Height;
                plannedTile.SwHeight = vertexStates[swCorner].Height;
                plannedTiles[index] = plannedTile;
            }
        }

        private static bool BordersOreDesignation(Tile2i neighborTile, HashSet<Tile2i> plannedTileSet, Dict<Tile2i, int> tileDepths)
        {
            return !plannedTileSet.Contains(neighborTile) && tileDepths.ContainsKey(neighborTile);
        }

        private static int GetAnchoredCornerHeight(Dict<Tile2i, int> cornerHeights, Tile2i corner, int fallbackHeight)
        {
            int anchoredHeight;
            return cornerHeights.TryGetValue(corner, out anchoredHeight) ? anchoredHeight : fallbackHeight;
        }

        private static void AddVertexCandidate(Dictionary<Tile2i, RampVertexAccumulator> vertexAccumulators, Tile2i vertex, int height, bool isFixed)
        {
            RampVertexAccumulator accumulator;
            if (vertexAccumulators.TryGetValue(vertex, out accumulator))
            {
                accumulator.Add(height, isFixed);
            }
            else
            {
                accumulator = new RampVertexAccumulator();
                accumulator.Add(height, isFixed);
            }

            vertexAccumulators[vertex] = accumulator;
        }

        private static void RelaxRampVertexHeights(Dictionary<Tile2i, RampVertexState> vertexStates, List<RampVertexEdge> vertexEdges)
        {
            const int maxIterations = 512;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                bool changed = false;

                foreach (RampVertexEdge edge in vertexEdges)
                {
                    RampVertexState a = vertexStates[edge.A];
                    RampVertexState b = vertexStates[edge.B];

                    int diff = a.Height - b.Height;
                    if (Math.Abs(diff) <= 1)
                    {
                        continue;
                    }

                    changed = true;
                    if (diff > 1)
                    {
                        ClampVertexPair(ref a, ref b);
                    }
                    else
                    {
                        ClampVertexPair(ref b, ref a);
                    }

                    vertexStates[edge.A] = a;
                    vertexStates[edge.B] = b;
                }

                if (!changed)
                {
                    return;
                }
            }
        }

        private static void ClampVertexPair(ref RampVertexState higher, ref RampVertexState lower)
        {
            if (higher.Height <= lower.Height + 1)
            {
                return;
            }

            if (higher.IsFixed && !lower.IsFixed)
            {
                lower.Height = higher.Height - 1;
                return;
            }

            if (!higher.IsFixed && lower.IsFixed)
            {
                higher.Height = lower.Height + 1;
                return;
            }

            if (higher.IsFixed && lower.IsFixed)
            {
                higher.Height = lower.Height + 1;
                return;
            }

            higher.Height--;
            if (higher.Height > lower.Height + 1)
            {
                lower.Height++;
            }
        }

        private static void GetTileCornerCoordinates(Tile2i tile, out Tile2i nwCorner, out Tile2i neCorner, out Tile2i seCorner, out Tile2i swCorner)
        {
            nwCorner = tile;
            neCorner = tile.AddX(4);
            seCorner = tile.AddXy(4);
            swCorner = tile.AddY(4);
        }

        private static void PlaceDesignation(IAreaManagingTower tower, TerrainDesignationProto proto, Tile2i tile, int nwHeight, int neHeight, int seHeight, int swHeight, List<Tile2i>? placedRampOrigins)
        {
            if (s_desigManager == null)
            {
                return;
            }

            DesignationData data = new DesignationData(tile,
                new HeightTilesI(nwHeight), new HeightTilesI(neHeight),
                new HeightTilesI(seHeight), new HeightTilesI(swHeight));

            if (!s_desigManager.AddOrReplaceDesignation(proto, data))
            {
                Log.Warning(string.Format("Failed to create ramp designation for tile {0}", tile));
                return;
            }

            RegisterGeneratedDesignationOrigin(tower, tile);

            // Keep the per-pass designation cache consistent so that subsequent CollectRampCandidates
            // calls within the same CreateAccessRamp invocation (shifted/narrow retries, multi-cluster
            // loops) do not accidentally treat newly placed ramp tiles as free.
            s_designationOriginsInArea.Add(tile);
            placedRampOrigins?.Add(tile);
        }

        /// <summary>
        /// If the mouth-approach tile (one step beyond the candidate's mouth row, lane 0, in the
        /// ramp direction) sits inside a non-ramp designation, returns that designation's TARGET
        /// z at the shared edge corner. Used by <see cref="TryPlaceRamp"/> to flatten the body
        /// when the approach is into another designation that will end up at the same z, turning
        /// what would otherwise be a wedge into a trivial bridge.
        /// </summary>
        private static bool TryGetMouthApproachTargetHeight(
            RampCandidate candidate, TerrainDesignationProto rampProto, out int approachTargetZ)
        {
            approachTargetZ = 0;
            if (s_desigManager == null) return false;
            Tile2i approachTile = Offset(candidate.RampTiles[0], candidate.Direction);
            Option<TerrainDesignation> opt = s_desigManager.GetDesignationAt(approachTile);
            if (!opt.HasValue) return false;
            if (IsAccesswayDesignationProto(opt.Value.Prototype, rampProto)) return false;
            // Sample the target z at the shared-edge midpoint of the facing designation cell.
            int sx, sy;
            if (candidate.Direction.X > 0)      { sx = 0; sy = 2; }
            else if (candidate.Direction.X < 0) { sx = 4; sy = 2; }
            else if (candidate.Direction.Y > 0) { sx = 2; sy = 0; }
            else                                { sx = 2; sy = 4; }
            approachTargetZ = (int)Math.Round(
                GetDesignationTargetHeightAt(opt.Value.Data, sx, sy).Value.ToFloat());
            return true;
        }

        /// <summary>
        /// Returns true if any lane's approach tile (one step beyond the ramp mouth) hosts a
        /// non-ramp designation whose origin is in <paramref name="forbiddenOrigins"/>. Used by
        /// the iterative cluster-by-cluster placement pass: clusters not yet connected to the
        /// tower are forbidden as approach targets, because BFS through undug terrain inside
        /// them currently reports reachable but the post-excavation result strands the mouth.
        /// Approaches into already-connected clusters (not in the forbidden set) are allowed
        /// and yield bridge candidates.
        /// </summary>
        private static bool RampMouthApproachInForbiddenCluster(
            Tile2i topRowTile, Tile2i direction, int laneCount,
            TerrainDesignationProto rampProto, HashSet<Tile2i> forbiddenOrigins)
        {
            if (s_desigManager == null) return false;
            Tile2i perp = GetPerpendicular(direction);
            for (int lane = 0; lane < laneCount; lane++)
            {
                Tile2i mouthTile = Offset(topRowTile, Scale(perp, lane));
                Tile2i approachTile = Offset(mouthTile, direction);
                Option<TerrainDesignation> opt = s_desigManager.GetDesignationAt(approachTile);
                if (!opt.HasValue) continue;
                if (IsAccesswayDesignationProto(opt.Value.Prototype, rampProto)) continue;
                if (forbiddenOrigins.Contains(opt.Value.OriginTileCoord)) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if any lane's approach tile (one step beyond the ramp mouth in
        /// <paramref name="direction"/>) hosts a non-ramp designation whose TARGET height at the
        /// shared edge differs from the mouth's surface height by more than one step. Such an
        /// approach will become a cliff or mound after excavation/dumping completes, stranding
        /// the mouth. Bridges between designations whose target z's match the mouth z are NOT
        /// flagged — they are first-class candidates.
        /// </summary>
        private static bool RampMouthApproachTargetMismatches(
            TerrainManager terrMgr, Tile2i topRowTile, Tile2i direction, int laneCount,
            TerrainDesignationProto rampProto)
        {
            if (s_desigManager == null) return false;
            Tile2i perp = GetPerpendicular(direction);
            for (int lane = 0; lane < laneCount; lane++)
            {
                Tile2i mouthTile = Offset(topRowTile, Scale(perp, lane));
                Tile2i approachTile = Offset(mouthTile, direction);
                Option<TerrainDesignation> opt = s_desigManager.GetDesignationAt(approachTile);
                if (!opt.HasValue) continue;
                if (IsAccesswayDesignationProto(opt.Value.Prototype, rampProto)) continue; // Adjacent ramp is fine.

                // Determine the two shared-edge corners (world tile coords) and the target-height
                // local sample positions inside the approach designation's 4×4 grid.
                Tile2i mouthCornerA, mouthCornerB;
                int approachAx, approachAy, approachBx, approachBy;
                if (direction.X > 0)
                {
                    mouthCornerA = mouthTile + new RelTile2i(4, 0);
                    mouthCornerB = mouthTile + new RelTile2i(4, 4);
                    approachAx = 0; approachAy = 0;
                    approachBx = 0; approachBy = 4;
                }
                else if (direction.X < 0)
                {
                    mouthCornerA = mouthTile;
                    mouthCornerB = mouthTile + new RelTile2i(0, 4);
                    approachAx = 4; approachAy = 0;
                    approachBx = 4; approachBy = 4;
                }
                else if (direction.Y > 0)
                {
                    mouthCornerA = mouthTile + new RelTile2i(0, 4);
                    mouthCornerB = mouthTile + new RelTile2i(4, 4);
                    approachAx = 0; approachAy = 0;
                    approachBx = 4; approachBy = 0;
                }
                else
                {
                    mouthCornerA = mouthTile;
                    mouthCornerB = mouthTile + new RelTile2i(4, 0);
                    approachAx = 0; approachAy = 4;
                    approachBx = 4; approachBy = 4;
                }

                int mouthZA = GetSurfaceHeight(terrMgr, mouthCornerA);
                int mouthZB = GetSurfaceHeight(terrMgr, mouthCornerB);
                float targetZA = GetDesignationTargetHeightAt(opt.Value.Data, approachAx, approachAy).Value.ToFloat();
                float targetZB = GetDesignationTargetHeightAt(opt.Value.Data, approachBx, approachBy).Value.ToFloat();

                if (Math.Abs(mouthZA - targetZA) > 1f || Math.Abs(mouthZB - targetZB) > 1f)
                    return true;
            }
            return false;
        }

        private static bool ExistingPlannedAccessProvidesAccessToAllClusters(IAreaManagingTower tower, Dict<Tile2i, int> tileDepths, TerrainDesignationProto accessProto, TerrainManager terrMgr)
        {
            if (s_vehiclePathFindingManager == null || s_excavatorPathFindingParams == null)
                return false;
            if (s_desigManager == null)
                return false;

            RefreshPathabilityAndInvalidateReachability();

            List<List<Tile2i>> rawClusters = BuildDesignationOriginClusters(tileDepths, terrMgr);
            if (rawClusters.Count == 0)
                return false;

            var miningIntent = new GenericWorkIntent("mining");
            var originClusters = new List<AccessOriginCluster>();
            int clusterId = 0;
            
            foreach (var rawCluster in rawClusters)
            {
                var accessOrigins = new List<AccessWorkOrigin>(rawCluster.Count);
                foreach (var origin in rawCluster)
                {
                    accessOrigins.Add(new AccessWorkOrigin(origin, miningIntent, false));
                }
                originClusters.Add(new AccessOriginCluster(++clusterId, accessOrigins, new[] { miningIntent }));
            }

            var existingProviders = new List<AccessProvider>();
            var accessibleAccessOrigins = new HashSet<Tile2i>();
            var inaccessibleAccessOrigins = new HashSet<Tile2i>();

            foreach (var origin in s_designationOriginsInArea)
            {
                if (tileDepths.ContainsKey(origin))
                    continue;

                Option<TerrainDesignation> existingDesignation = s_desigManager.GetDesignationAt(origin);
                if (existingDesignation.HasValue && existingDesignation.Value.Prototype == accessProto)
                {
                    // Existing ramps are mapped as providers. ReachesGround defines if they connect to tower.
                    bool reachesTower = ExistingAccessOriginConnectsToTower(tower, origin, tileDepths, accessProto, accessibleAccessOrigins, inaccessibleAccessOrigins);
                    existingProviders.Add(new AccessProvider(new[] { origin, origin.AddX(4), origin.AddY(4), origin.AddXy(4) }, reachesTower));
                }
            }

            var states = AccessReachability.EvaluateReachability(
                originClusters,
                existingProviders,
                tower,
                terrMgr,
                tile => IsClusterOriginReadyAndPathable(tower, tile),
                (origin, direction) => 
                {
                    Tile2i neighbor = new Tile2i(origin.X + direction.X, origin.Y + direction.Y);
                    return TryClusterEdgeConnectsToAccess(origin, neighbor, direction, tileDepths, accessProto, terrMgr, out _);
                });

            foreach (var cluster in originClusters)
            {
                var state = states[cluster];
                AccessDiagnostics.LogClusterState(new AccessAnalysisResult(cluster, state, AccessNeed.Mining, null, null, BlockedReason.None, 0f));
                
                if (state != AccessClusterState.AccessibleDirect && state != AccessClusterState.AccessibleViaProvider)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsTileTransitionTraversable(Tile2i t1, Tile2i t2, Dict<Tile2i, int> tileDepths, TerrainManager terrMgr)
        {
            // Active designation target profiles are authoritative. Adjacent origins can have
            // very different center heights on steep terrain while still sharing an exact,
            // traversable edge; the old center-height approximation split those into clusters.
            if (s_desigManager != null)
            {
                Tile2i direction = new Tile2i(t2.X - t1.X, t2.Y - t1.Y);
                Option<TerrainDesignation> firstDesignation = s_desigManager.GetDesignationAt(t1);
                Option<TerrainDesignation> secondDesignation = s_desigManager.GetDesignationAt(t2);
                if (firstDesignation.HasValue
                    && secondDesignation.HasValue
                    && TryGetEdgeTargetHeights(firstDesignation.Value, direction, out float firstA, out float firstB)
                    && TryGetEdgeTargetHeights(secondDesignation.Value, Scale(direction, -1), out float secondA, out float secondB))
                {
                    return Math.Abs(firstA - secondA) <= 0.01f
                        && Math.Abs(firstB - secondB) <= 0.01f;
                }
            }

            // Fallback for callers whose candidate origins have not yet become active
            // designations. This retains the previous coarse behavior for that case only.
            int h1 = GetSurfaceHeight(terrMgr, t1 + new RelTile2i(2, 2));
            int h2 = GetSurfaceHeight(terrMgr, t2 + new RelTile2i(2, 2));
            if (Math.Abs(h1 - h2) > 2)
            {
                return false;
            }

            if (tileDepths.TryGetValue(t1, out int target1) && tileDepths.TryGetValue(t2, out int target2))
            {
                if (Math.Abs(target1 - target2) > 2)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<List<Tile2i>> BuildDesignationOriginClusters(Dict<Tile2i, int> tileDepths, TerrainManager terrMgr)
        {
            var clusters = new List<List<Tile2i>>();
            var unvisited = new HashSet<Tile2i>(tileDepths.Keys);
            var queue = new Queue<Tile2i>();

            while (unvisited.Count > 0)
            {
                Tile2i seed = unvisited.First();
                unvisited.Remove(seed);
                queue.Enqueue(seed);

                var cluster = new List<Tile2i>();
                while (queue.Count > 0)
                {
                    Tile2i current = queue.Dequeue();
                    cluster.Add(current);

                    foreach (Tile2i direction in s_cardinalDirections)
                    {
                        Tile2i next = Offset(current, direction);
                        if (!unvisited.Contains(next))
                            continue;
                        if (!IsTileTransitionTraversable(current, next, tileDepths, terrMgr))
                            continue;

                        unvisited.Remove(next);
                        queue.Enqueue(next);
                    }
                }

                clusters.Add(cluster);
            }

            return clusters;
        }

        private static bool ClusterHasTowerReachableAccess(
            IAreaManagingTower tower,
            Dict<Tile2i, int> tileDepths,
            List<Tile2i> cluster,
            TerrainDesignationProto accessProto,
            TerrainManager terrMgr,
            HashSet<Tile2i> accessibleAccessOrigins,
            HashSet<Tile2i> inaccessibleAccessOrigins)
        {
            var clusterSet = new HashSet<Tile2i>(cluster);

            foreach (Tile2i origin in cluster)
            {
                foreach (Tile2i direction in s_cardinalDirections)
                {
                    Tile2i neighbor = Offset(origin, direction);
                    if (clusterSet.Contains(neighbor) || tileDepths.ContainsKey(neighbor))
                        continue;

                    if (!TryClusterEdgeConnectsToAccess(origin, neighbor, direction, tileDepths, accessProto, terrMgr, out bool connectsToExistingAccess))
                        continue;

                    if (!connectsToExistingAccess)
                    {
                        if (IsRampMouthReachableFromTower(tower, neighbor))
                            return true;
                        continue;
                    }

                    if (ExistingAccessOriginConnectsToTower(tower, neighbor, tileDepths, accessProto, accessibleAccessOrigins, inaccessibleAccessOrigins))
                        return true;
                }
            }

            return false;
        }

        private static bool TryClusterEdgeConnectsToAccess(
            Tile2i clusterOrigin,
            Tile2i accessOrigin,
            Tile2i direction,
            Dict<Tile2i, int> tileDepths,
            TerrainDesignationProto accessProto,
            TerrainManager terrMgr,
            out bool connectsToExistingAccess)
        {
            connectsToExistingAccess = false;
            if (s_desigManager == null)
                return false;

            Option<TerrainDesignation> clusterDesignation = s_desigManager.GetDesignationAt(clusterOrigin);
            if (!clusterDesignation.HasValue)
                return false;

            if (!TryGetEdgeTargetHeights(clusterDesignation.Value, direction, out float clusterA, out float clusterB))
                return false;

            Tile2i alignedAccessOrigin = new Tile2i(accessOrigin.X & -4, accessOrigin.Y & -4);
            Option<TerrainDesignation> accessDesignation = s_desigManager.GetDesignationAt(alignedAccessOrigin);
            if (accessDesignation.HasValue)
            {
                if (tileDepths.ContainsKey(alignedAccessOrigin))
                    return false;
                if (!IsAccesswayDesignationProto(accessDesignation.Value.Prototype, accessProto))
                    return false;
                if (!TryGetEdgeTargetHeights(accessDesignation.Value, Scale(direction, -1), out float accessA, out float accessB))
                    return false;

                connectsToExistingAccess = true;
                return Math.Abs(clusterA - accessA) <= 1f && Math.Abs(clusterB - accessB) <= 1f;
            }

            GetSharedEdgeSurfaceHeights(clusterOrigin, direction, terrMgr, out int surfaceA, out int surfaceB);
            return Math.Abs(clusterA - surfaceA) <= 1f && Math.Abs(clusterB - surfaceB) <= 1f;
        }

        private static bool TryGetEdgeTargetHeights(TerrainDesignation designation, Tile2i direction, out float first, out float second)
        {
            first = 0f;
            second = 0f;
            int ax, ay, bx, by;
            if (direction.X > 0)
            {
                ax = 4; ay = 0; bx = 4; by = 4;
            }
            else if (direction.X < 0)
            {
                ax = 0; ay = 0; bx = 0; by = 4;
            }
            else if (direction.Y > 0)
            {
                ax = 0; ay = 4; bx = 4; by = 4;
            }
            else if (direction.Y < 0)
            {
                ax = 0; ay = 0; bx = 4; by = 0;
            }
            else
            {
                return false;
            }

            first = GetDesignationTargetHeightAt(designation.Data, ax, ay).Value.ToFloat();
            second = GetDesignationTargetHeightAt(designation.Data, bx, by).Value.ToFloat();
            return true;
        }

        private static void GetSharedEdgeSurfaceHeights(Tile2i origin, Tile2i direction, TerrainManager terrMgr, out int first, out int second)
        {
            Tile2i a;
            Tile2i b;
            if (direction.X > 0)
            {
                a = origin.AddX(4);
                b = origin.AddXy(4);
            }
            else if (direction.X < 0)
            {
                a = origin;
                b = origin.AddY(4);
            }
            else if (direction.Y > 0)
            {
                a = origin.AddY(4);
                b = origin.AddXy(4);
            }
            else
            {
                a = origin;
                b = origin.AddX(4);
            }

            first = GetSurfaceHeight(terrMgr, a);
            second = GetSurfaceHeight(terrMgr, b);
        }

        private static bool ExistingAccessOriginConnectsToTower(
            IAreaManagingTower tower,
            Tile2i origin,
            Dict<Tile2i, int> tileDepths,
            TerrainDesignationProto accessProto,
            HashSet<Tile2i> accessibleAccessOrigins,
            HashSet<Tile2i> inaccessibleAccessOrigins)
        {
            Tile2i alignedOrigin = new Tile2i(origin.X & -4, origin.Y & -4);

            if (IsRampMouthReachableFromTower(tower, alignedOrigin))
                return true;

            if (accessibleAccessOrigins.Contains(alignedOrigin))
                return true;
            if (inaccessibleAccessOrigins.Contains(alignedOrigin))
                return false;

            if (!IsExistingAccessDesignation(alignedOrigin, tileDepths, accessProto))
                return false;

            var component = new List<Tile2i>();
            var visited = new HashSet<Tile2i>();
            var queue = new Queue<Tile2i>();
            visited.Add(alignedOrigin);
            queue.Enqueue(alignedOrigin);

            bool connects = false;
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                component.Add(current);

                if (IsRampMouthReachableFromTower(tower, current))
                    connects = true;

                foreach (Tile2i direction in s_cardinalDirections)
                {
                    Tile2i next = Offset(current, direction);
                    if (visited.Contains(next))
                        continue;
                    if (!IsExistingAccessDesignation(next, tileDepths, accessProto))
                        continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            HashSet<Tile2i> cache = connects ? accessibleAccessOrigins : inaccessibleAccessOrigins;
            foreach (Tile2i accessOrigin in component)
                cache.Add(accessOrigin);

            return connects;
        }

        private static bool IsExistingAccessDesignation(Tile2i origin, Dict<Tile2i, int> tileDepths, TerrainDesignationProto accessProto)
        {
            if (s_desigManager == null)
                return false;
            if (tileDepths.ContainsKey(origin))
                return false;

            Option<TerrainDesignation> existingDesignation = s_desigManager.GetDesignationAt(origin);
            return existingDesignation.HasValue
                && IsAccesswayDesignationProto(existingDesignation.Value.Prototype, accessProto);
        }

        private static bool IsAccesswayDesignationProto(
            TerrainDesignationProto proto,
            TerrainDesignationProto preferredProto)
        {
            return proto == preferredProto
                || (s_levelingProto != null && proto == s_levelingProto)
                || (s_miningProto != null && proto == s_miningProto)
                || (s_dumpingProto != null && proto == s_dumpingProto);
        }

        private static bool IsFreeRampTile(IAreaManagingTower tower, Tile2i tile, Dict<Tile2i, int> tileDepths, int rampDepth, Tile2i rampDirection, HashSet<Tile2i>? reservedRampTiles)
        {
            return IsFreeRampTile(tower, tile, tileDepths, rampDepth, rampDirection, reservedRampTiles, out _);
        }

        private static bool IsFreeRampTile(
            IAreaManagingTower tower,
            Tile2i tile,
            Dict<Tile2i, int> tileDepths,
            int rampDepth,
            Tile2i rampDirection,
            HashSet<Tile2i>? reservedRampTiles,
            out string reason)
        {
            reason = "";
            if (!IsInsideTowerArea(tower, tile))
            {
                reason = "NotInsideTowerArea";
                return false;
            }
            if (tileDepths.ContainsKey(tile))
            {
                reason = "InTileDepths";
                return false;
            }
            if (reservedRampTiles != null && reservedRampTiles.Contains(tile))
            {
                reason = "ReservedRampTiles";
                return false;
            }
            if (DoesTileOverlapBuildingFootprint(tile, rampDepth, rampDirection))
            {
                reason = "OverlapBuildingFootprint";
                return false;
            }
            if (s_designationOriginsInArea.Contains(new Tile2i(tile.X & -4, tile.Y & -4)))
            {
                reason = "HasExistingDesignation";
                return false;
            }
            return true;
        }

        private static bool DoesTileOverlapBuildingFootprint(Tile2i tile, int rampDepth, Tile2i rampDirection)
        {
            // A designation tile covers world tiles from (tile.X, tile.Y) to (tile.X+3, tile.Y+3).
            // The requested safety margin is 1 world tile per depth, plus one extra tile.
            // Apply margin only perpendicular to the ramp direction, not along the ramp axis.
            int safeDepth = Math.Max(1, rampDepth);
            int marginWorldTiles = safeDepth + 1;

            int minDx = 0;
            int maxDx = 3;
            int minDy = 0;
            int maxDy = 3;

            if (rampDirection.X != 0)
            {
                // Ramp goes east-west, so expand only north-south.
                minDy = -marginWorldTiles;
                maxDy = 3 + marginWorldTiles;
            }
            else
            {
                // Ramp goes north-south, so expand only east-west.
                minDx = -marginWorldTiles;
                maxDx = 3 + marginWorldTiles;
            }

            for (int dx = minDx; dx <= maxDx; dx++)
            {
                for (int dy = minDy; dy <= maxDy; dy++)
                {
                    if (s_buildingOccupiedTiles.Contains(new Tile2i(tile.X + dx, tile.Y + dy)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsInsideTowerArea(IAreaManagingTower tower, Tile2i tile)
        {
            return tower.Area.ContainsTile(tile)
                && tower.Area.ContainsTile(tile.AddX(3))
                && tower.Area.ContainsTile(tile.AddY(3))
                && tower.Area.ContainsTile(tile.AddXy(3));
        }

        private static int ScoreRampCandidate(Tile2i towerPos, Tile2i[] oreTiles, Tile2i direction, Tile2i[] rampTiles, bool[] laneHasOre, int[] laneAttachmentDepths)
        {
            int rampSumX = 0;
            int rampSumY = 0;
            for (int i = 0; i < rampTiles.Length; i++)
            {
                rampSumX += rampTiles[i].X;
                rampSumY += rampTiles[i].Y;
            }

            int oreSumX = 0;
            int oreSumY = 0;
            for (int i = 0; i < oreTiles.Length; i++)
            {
                oreSumX += oreTiles[i].X;
                oreSumY += oreTiles[i].Y;
            }

            int midX = rampSumX / rampTiles.Length;
            int midY = rampSumY / rampTiles.Length;
            int dx = towerPos.X - midX;
            int dy = towerPos.Y - midY;
            int distanceScore = dx * dx + dy * dy;

            int oreMidX = oreSumX / oreTiles.Length;
            int oreMidY = oreSumY / oreTiles.Length;
            int alignmentScore = (towerPos.X - oreMidX) * direction.X + (towerPos.Y - oreMidY) * direction.Y;

            int oreLaneCount = 0;
            int attachmentDepthSum = 0;
            for (int i = 0; i < laneHasOre.Length; i++)
            {
                if (laneHasOre[i])
                {
                    oreLaneCount++;
                    attachmentDepthSum += laneAttachmentDepths[i];
                }
            }

            // Prefer broader ore contact. Penalise interior anchors (higher D) so face candidates (D=0)
            // are always tried first - they produce correctly-connected ramps.
            int oreConnectivityBonus = oreLaneCount * 250;
            return distanceScore - alignmentScore * 8 - oreConnectivityBonus + attachmentDepthSum * 30;
        }

        private static bool TryGetOreLaneEdgeHeights(Tile2i oreTile, Tile2i direction, Dict<Tile2i, int> cornerHeights, out int firstHeight, out int secondHeight)
        {
            firstHeight = 0;
            secondHeight = 0;
            Tile2i firstCorner;
            Tile2i secondCorner;

            if (direction.X > 0)
            {
                firstCorner = oreTile.AddX(4);
                secondCorner = oreTile.AddXy(4);
            }
            else if (direction.X < 0)
            {
                firstCorner = oreTile;
                secondCorner = oreTile.AddY(4);
            }
            else if (direction.Y > 0)
            {
                firstCorner = oreTile.AddY(4);
                secondCorner = oreTile.AddXy(4);
            }
            else
            {
                firstCorner = oreTile;
                secondCorner = oreTile.AddX(4);
            }

            if (!cornerHeights.TryGetValue(firstCorner, out firstHeight) ||
                !cornerHeights.TryGetValue(secondCorner, out secondHeight))
            {
                return false;
            }

            return true;
        }

        private static int CountForwardOreDepth(Tile2i origin, Tile2i direction, Dict<Tile2i, int> tileDepths)
        {
            int depth = 0;
            Tile2i current = origin;

            while (true)
            {
                Tile2i next = Offset(current, direction);
                if (!tileDepths.ContainsKey(next))
                {
                    return depth;
                }

                depth++;
                current = next;
            }
        }

        private static int FindNearestOreLane(bool[] laneHasOre, int lane)
        {
            int bestLane = -1;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < laneHasOre.Length; i++)
            {
                if (!laneHasOre[i])
                {
                    continue;
                }

                int distance = Math.Abs(i - lane);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestLane = i;
                }
            }

            return bestLane >= 0 ? bestLane : lane;
        }

        private static int[] BuildBoundaryHeights(int[] laneHeights)
        {
            int[] boundaryHeights = new int[laneHeights.Length + 1];
            boundaryHeights[0] = laneHeights[0];
            boundaryHeights[laneHeights.Length] = laneHeights[laneHeights.Length - 1];

            for (int i = 1; i < laneHeights.Length; i++)
            {
                boundaryHeights[i] = Math.Max(laneHeights[i - 1], laneHeights[i]);
            }

            return boundaryHeights;
        }

        private static int[] BuildConstantBoundaryHeights(int laneCount, int height)
        {
            int[] boundaryHeights = new int[laneCount + 1];
            for (int i = 0; i < boundaryHeights.Length; i++)
                boundaryHeights[i] = height;
            return boundaryHeights;
        }

        private static int[] BuildSurfaceBoundaryHeights(TerrainManager terrMgr, Tile2i[] tiles)
        {
            int[] laneSurfaceHeights = new int[tiles.Length];
            for (int lane = 0; lane < tiles.Length; lane++)
                laneSurfaceHeights[lane] = GetSurfaceHeight(terrMgr, tiles[lane]);
            return BuildBoundaryHeights(laneSurfaceHeights);
        }

        private static int[] BuildEdgeBoundaryHeights(int[] firstEdgeHeights, int[] secondEdgeHeights)
        {
            int laneCount = firstEdgeHeights.Length;
            int[] boundaryHeights = new int[laneCount + 1];
            boundaryHeights[0] = firstEdgeHeights[0];
            boundaryHeights[laneCount] = secondEdgeHeights[laneCount - 1];

            for (int i = 1; i < laneCount; i++)
            {
                boundaryHeights[i] = Math.Max(secondEdgeHeights[i - 1], firstEdgeHeights[i]);
            }

            return boundaryHeights;
        }

        private static void BuildRowTileHeights(Tile2i direction, int[] backBoundaryHeights, int[] frontBoundaryHeights,
            out int[] nwHeights, out int[] neHeights, out int[] seHeights, out int[] swHeights)
        {
            int laneCount = backBoundaryHeights.Length - 1;
            nwHeights = new int[laneCount];
            neHeights = new int[laneCount];
            seHeights = new int[laneCount];
            swHeights = new int[laneCount];

            for (int lane = 0; lane < laneCount; lane++)
            {
                if (direction.X > 0)
                {
                    nwHeights[lane] = backBoundaryHeights[lane];
                    neHeights[lane] = frontBoundaryHeights[lane];
                    seHeights[lane] = frontBoundaryHeights[lane + 1];
                    swHeights[lane] = backBoundaryHeights[lane + 1];
                }
                else if (direction.X < 0)
                {
                    nwHeights[lane] = frontBoundaryHeights[lane];
                    neHeights[lane] = backBoundaryHeights[lane];
                    seHeights[lane] = backBoundaryHeights[lane + 1];
                    swHeights[lane] = frontBoundaryHeights[lane + 1];
                }
                else if (direction.Y > 0)
                {
                    nwHeights[lane] = backBoundaryHeights[lane];
                    neHeights[lane] = backBoundaryHeights[lane + 1];
                    seHeights[lane] = frontBoundaryHeights[lane + 1];
                    swHeights[lane] = frontBoundaryHeights[lane];
                }
                else
                {
                    nwHeights[lane] = frontBoundaryHeights[lane];
                    neHeights[lane] = frontBoundaryHeights[lane + 1];
                    seHeights[lane] = backBoundaryHeights[lane + 1];
                    swHeights[lane] = backBoundaryHeights[lane];
                }
            }
        }

        private static int GetSurfaceHeight(TerrainManager terrMgr, Tile2i tile)
        {
            return (int)Math.Floor(terrMgr.GetHeight(tile).Value.ToFloat());
        }

        private static Tile2i GetPerpendicular(Tile2i direction)
        {
            return direction.X != 0 ? new Tile2i(0, 4) : new Tile2i(4, 0);
        }

        private static Tile2i Scale(Tile2i delta, int factor)
        {
            return new Tile2i(delta.X * factor, delta.Y * factor);
        }
    }
}

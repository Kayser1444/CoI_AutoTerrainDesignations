using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Buildings.Forestry;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.PathFinding;
using Mafi.Core.Products;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.Terrain.Trees;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using AutoTerrainDesignations.Access;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static readonly RelTile2i[] s_experimentalGroundDirections =
        {
            new RelTile2i(1, 0), new RelTile2i(-1, 0), new RelTile2i(0, 1), new RelTile2i(0, -1)
        };

        internal static AccessSearchResult? LastExperimentalAccessSearch { get; private set; }
        internal static AccessDesignationPlan? LastExperimentalAccessPlan { get; private set; }
        private static readonly bool s_enableVerboseHandoffDiagnostics = false;
        private static UiRoot? s_uiRoot;
        private static bool s_cancelExperimentalAccessSearch;
        private static readonly List<PlacedExperimentalDesignation> s_lastExperimentalCleanupDesignations =
            new List<PlacedExperimentalDesignation>();
        private static readonly List<TreeId> s_lastExperimentalCleanupTreeSelections = new List<TreeId>();

        private readonly struct PlacedExperimentalDesignation
        {
            public Tile2i Origin { get; }
            public TerrainDesignationProto Proto { get; }

            public PlacedExperimentalDesignation(Tile2i origin, TerrainDesignationProto proto)
            {
                Origin = origin;
                Proto = proto;
            }
        }

        private sealed class AccessPropCleanupSnapshotDiagnostics
        {
            public int PropSamples;
            public int TreeSamples;
            public int EligibleOrigins;
            public int TreeCleanupOrigins;
            public int DenseDebrisCleanupOrigins;
            public int HardBlockedOrigins;
            public int TerrainRemovalPolicyOrigins;
            public readonly Dictionary<AccessPropBlockerKind, int> BlockedByKind =
                new Dictionary<AccessPropBlockerKind, int>();
            public readonly List<string> SampleDetails = new List<string>();
            public readonly List<string> EligibleOriginDetails = new List<string>();
            public readonly List<string> BlockedOriginDetails = new List<string>();

            public void CountBlocked(AccessPropBlockerKind kind)
            {
                if (BlockedByKind.TryGetValue(kind, out int count))
                    BlockedByKind[kind] = count + 1;
                else
                    BlockedByKind[kind] = 1;
            }

            public void RecordSample(Tile2i tile, Tile2i origin, AccessPropSample sample,
                AccessPropBlockerKind blockerKind)
            {
                if (SampleDetails.Count >= 16)
                    return;
                string kind = sample.IsTree ? "tree" : sample.IsDenseDebris ? "debris" : "prop";
                SampleDetails.Add(
                    $"{kind}:tile=({tile.X},{tile.Y}) origin=({origin.X},{origin.Y}) " +
                    $"key={sample.CleanupObjectKey} blocker={blockerKind}");
            }

            public void RecordOrigin(AccessPropCleanupInfo info)
            {
                List<string> target = info.IsEligible ? EligibleOriginDetails : BlockedOriginDetails;
                if (target.Count >= 24)
                    return;
                string samples = info.Samples.Count == 0
                    ? "none"
                    : string.Join(",", info.Samples.Take(8).Select(sample =>
                    {
                        string kind = sample.IsTree ? "T" : sample.IsDenseDebris ? "D" : "P";
                        return $"{kind}@({sample.Tile.X},{sample.Tile.Y})#{sample.CleanupObjectKey}";
                    }))
                    + (info.Samples.Count > 8 ? $",...+{info.Samples.Count - 8}" : string.Empty);
                target.Add(
                    $"origin=({info.Origin.X},{info.Origin.Y}) classes={info.Classes} " +
                    $"eligible={info.IsEligible} blocker={info.BlockerKind} samples=[{samples}]");
            }
        }

        private sealed class ProjectedDesignationDisturbance
        {
            public readonly HashSet<Tile2i> CutTiles = new HashSet<Tile2i>();
            public readonly HashSet<Tile2i> FillTiles = new HashSet<Tile2i>();
            public readonly Dictionary<Tile2i, float> CutSupportCeilings =
                new Dictionary<Tile2i, float>();
            public readonly Dictionary<Tile2i, float> FillSurfaceFloors =
                new Dictionary<Tile2i, float>();
            public int Count => CutTiles.Union(FillTiles).Count();
            public bool Contains(Tile2i tile)
                => CutTiles.Contains(tile) || FillTiles.Contains(tile);
            public void Add(AccessSideRayOperation operation, Tile2i tile)
            {
                if (operation == AccessSideRayOperation.Cut) CutTiles.Add(tile);
                else if (operation == AccessSideRayOperation.Fill) FillTiles.Add(tile);
            }
            public void AddHeight(
                AccessSideRayOperation operation, Tile2i tile, float projectedHeight)
            {
                if (operation == AccessSideRayOperation.Cut)
                {
                    if (!CutSupportCeilings.TryGetValue(tile, out float existing)
                        || projectedHeight < existing)
                        CutSupportCeilings[tile] = projectedHeight;
                }
                else if (operation == AccessSideRayOperation.Fill)
                {
                    if (!FillSurfaceFloors.TryGetValue(tile, out float existing)
                        || projectedHeight > existing)
                        FillSurfaceFloors[tile] = projectedHeight;
                }
            }
        }

        private static bool TryGetExperimentalOperation(TerrainDesignationProto proto, out bool isMining)
        {
            if (s_miningProto != null && proto == s_miningProto)
            {
                isMining = true;
                return true;
            }
            if (s_dumpingProto != null && proto == s_dumpingProto)
            {
                isMining = false;
                return true;
            }
            isMining = false;
            return false;
        }

        internal static bool TrySelectHandoffOperationForProfile(
            int profileCenter2,
            float groundHeight,
            out AccessHandoffOperation operation)
        {
            int groundHeight2 = ToHeight2(groundHeight);
            if (profileCenter2 == groundHeight2)
            {
                operation = AccessHandoffOperation.None;
                return false;
            }

            operation = profileCenter2 < groundHeight2
                ? AccessHandoffOperation.Mining
                : AccessHandoffOperation.Dumping;
            return true;
        }

        internal static bool TrySelectHandoffOperationForOrigin(
            int predecessorProfileCenter2,
            float predecessorGroundHeight,
            out AccessHandoffOperation operation)
            => TrySelectHandoffOperationForProfile(predecessorProfileCenter2, predecessorGroundHeight, out operation);

        internal static bool TrySelectDirectionalHandoffOperation(
            int connectedDeltaA,
            int connectedDeltaB,
            int handoffDeltaA,
            int handoffDeltaB,
            out AccessHandoffOperation operation)
        {
            if (handoffDeltaA == 0 && handoffDeltaB == 0)
            {
                operation = AccessHandoffOperation.Leveling;
                return true;
            }

            bool isMining =
                connectedDeltaA <= 0
                && connectedDeltaB <= 0
                && (connectedDeltaA < 0 || connectedDeltaB < 0)
                && handoffDeltaA >= 0
                && handoffDeltaB >= 0;
            if (isMining)
            {
                operation = AccessHandoffOperation.Mining;
                return true;
            }

            bool isDumping =
                connectedDeltaA >= 0
                && connectedDeltaB >= 0
                && (connectedDeltaA > 0 || connectedDeltaB > 0)
                && handoffDeltaA <= 0
                && handoffDeltaB <= 0;
            if (isDumping)
            {
                operation = AccessHandoffOperation.Dumping;
                return true;
            }

            operation = AccessHandoffOperation.None;
            return false;
        }

        private static bool TryBuildExperimentalAccessSnapshot(
            IAreaManagingTower tower,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights,
            TerrainManager terrMgr,
            bool isMining,
            bool allowsMixedWork,
            IReadOnlyCollection<Tile2i>? reachableFixedOrigins,
            out AccessSearchSnapshot snapshot,
            out string failureReason)
        {
            Stopwatch snapshotTimer = Stopwatch.StartNew();
            snapshot = null!;
            failureReason = string.Empty;
            if (!AccessPathSearch.ValidateCoreTransitions(out failureReason))
            {
                failureReason = "TransitionSelfTest: " + failureReason;
                return false;
            }
            if (s_desigManager == null || s_vehiclePathFindingManager == null)
            {
                failureReason = "PathfindingUnavailable";
                return false;
            }

            Tile2i boundsMin = tower.Area.BoundingBoxMin;
            Tile2i boundsMax = tower.Area.BoundingBoxMax;
            Tile2i towerCenter = tower is IEntityWithPosition positioned
                ? positioned.Position2f.Tile2i
                : new Tile2i((boundsMin.X + boundsMax.X) / 2, (boundsMin.Y + boundsMax.Y) / 2);
            Tile2i groundCaptureMin = new Tile2i(
                Math.Min(boundsMin.X, towerCenter.X) - RAMP_ACCESS_SEARCH_MARGIN_TILES,
                Math.Min(boundsMin.Y, towerCenter.Y) - RAMP_ACCESS_SEARCH_MARGIN_TILES);
            Tile2i groundCaptureMax = new Tile2i(
                Math.Max(boundsMax.X, towerCenter.X) + RAMP_ACCESS_SEARCH_MARGIN_TILES,
                Math.Max(boundsMax.Y, towerCenter.Y) + RAMP_ACCESS_SEARCH_MARGIN_TILES);
            Tile2i physicalTerrainMin = Tile2i.Zero;
            Tile2i physicalTerrainMax = new Tile2i(
                terrMgr.TerrainSize.X - 1,
                terrMgr.TerrainSize.Y - 1);
            groundCaptureMin = new Tile2i(
                Math.Max(physicalTerrainMin.X, groundCaptureMin.X
                    - AutoTerrainDesignationsMod.AccessProjectedRayMaxDistance
                    - AutoTerrainDesignationsMod.AccessProjectedRayEndBuffer),
                Math.Max(physicalTerrainMin.Y, groundCaptureMin.Y
                    - AutoTerrainDesignationsMod.AccessProjectedRayMaxDistance
                    - AutoTerrainDesignationsMod.AccessProjectedRayEndBuffer));
            groundCaptureMax = new Tile2i(
                Math.Min(physicalTerrainMax.X, groundCaptureMax.X
                    + AutoTerrainDesignationsMod.AccessProjectedRayMaxDistance
                    + AutoTerrainDesignationsMod.AccessProjectedRayEndBuffer),
                Math.Min(physicalTerrainMax.Y, groundCaptureMax.Y
                    + AutoTerrainDesignationsMod.AccessProjectedRayMaxDistance
                    + AutoTerrainDesignationsMod.AccessProjectedRayEndBuffer));

            var groundHeight2 = new Dictionary<Tile2i, int>();
            var preciseTerrainHeights = new Dictionary<Tile2i, float>();
            var terrainColumns = new Dictionary<Tile2i, AccessTerrainColumn>();
            var terrainCenterHeight2 = new Dictionary<Tile2i, int>();
            var oceanTiles = new HashSet<Tile2i>();
            var fixedProfiles = new Dictionary<Tile2i, AccessHeightProfile>();
            var designatedOrigins = new HashSet<Tile2i>();
            var rayDesignationOrigins = new HashSet<Tile2i>();
            var rayDesignations = new Dictionary<Tile2i, TerrainDesignation>();
            var groundExclusionReasons = new Dictionary<Tile2i, string>();

            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(boundsMin, boundsMax))
            {
                Tile2i origin = designation.OriginTileCoord;
                designatedOrigins.Add(origin);
                fixedProfiles[origin] = ProfileFromDesignation(designation);
            }
            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(
                groundCaptureMin, groundCaptureMax))
            {
                rayDesignationOrigins.Add(designation.OriginTileCoord);
                rayDesignations[designation.OriginTileCoord] = designation;
            }

            var workOrigins = new HashSet<Tile2i>();
            foreach (var pair in tileDepths)
            {
                Tile2i origin = pair.Key;
                workOrigins.Add(origin);
                int fallback = pair.Value;
                int nw = cornerHeights.TryGetValue(origin, out int value) ? value : fallback;
                int ne = cornerHeights.TryGetValue(origin + new RelTile2i(4, 0), out value) ? value : fallback;
                int se = cornerHeights.TryGetValue(origin + new RelTile2i(4, 4), out value) ? value : fallback;
                int sw = cornerHeights.TryGetValue(origin + new RelTile2i(0, 4), out value) ? value : fallback;
                fixedProfiles[origin] = new AccessHeightProfile(nw * 2, ne * 2, se * 2, sw * 2);
                designatedOrigins.Add(origin);
                rayDesignationOrigins.Add(origin);
            }

            int minHeight2 = int.MaxValue;
            int maxHeight2 = int.MinValue;
            for (int x = groundCaptureMin.X; x <= groundCaptureMax.X; x++)
            {
                for (int y = groundCaptureMin.Y; y <= groundCaptureMax.Y; y++)
                {
                    Tile2i tile = new Tile2i(x, y);
                    float preciseHeight = terrMgr.GetHeight(tile).Value.ToFloat();
                    int height2 = ToHeight2(preciseHeight);
                    groundHeight2[tile] = height2;
                    preciseTerrainHeights[tile] = preciseHeight;
                    if (terrMgr.IsOcean(tile)) oceanTiles.Add(tile);
                    minHeight2 = Math.Min(minHeight2, height2);
                    maxHeight2 = Math.Max(maxHeight2, height2);
                }
            }

            int firstOriginX = boundsMin.X & -4;
            int firstOriginY = boundsMin.Y & -4;
            for (int x = firstOriginX; x <= boundsMax.X; x += 4)
            {
                for (int y = firstOriginY; y <= boundsMax.Y; y += 4)
                {
                    Tile2i origin = new Tile2i(x, y);
                    if (!IsOriginInsideTower(tower, origin)) continue;
                    Tile2i center = origin + new RelTile2i(2, 2);
                    terrainCenterHeight2[origin] = groundHeight2.TryGetValue(center, out int h2)
                        ? h2
                        : ToHeight2(terrMgr.GetHeight(center).Value.ToFloat());
                }
            }
            for (int x = firstOriginX; x <= boundsMax.X + 4; x += 4)
            {
                for (int y = firstOriginY; y <= boundsMax.Y + 4; y += 4)
                {
                    Tile2i corner = new Tile2i(x, y);
                    if (!terrMgr.IsValidCoord(corner))
                        continue;
                    terrainColumns[corner] = CaptureAccessTerrainColumn(terrMgr, corner);
                }
            }
            ResolveAccessMaterialSlopes(
                tower,
                out float dumpingMaterialSlope,
                out float fallbackMiningSlope,
                out bool dumpingSlopeUsedFallback,
                out bool hasDumpingMaterial,
                out string materialSlopeDiagnostic);
            VehiclePathFindingParams pathParams =
                GetExcavatorPathFindingParamsForTower(tower, out string pathParamsSource);
            int vehicleClearance = Math.Max(1, ExtractVehicleClearance(pathParams).Value);
            int vehicleDisturbanceRadius =
                AccessPathSearch.GetVehicleDisturbanceRadius(vehicleClearance);
            ProjectedDesignationDisturbance projectedDesignationDisturbance =
                BuildProjectedDesignationDisturbedTiles(
                    rayDesignations,
                    preciseTerrainHeights,
                    terrainColumns,
                    oceanTiles,
                    physicalTerrainMin,
                    physicalTerrainMax,
                    dumpingMaterialSlope,
                    fallbackMiningSlope,
                    vehicleDisturbanceRadius);

            foreach (AccessHeightProfile profile in fixedProfiles.Values)
            {
                minHeight2 = Math.Min(minHeight2, Math.Min(Math.Min(profile.Nw2, profile.Ne2), Math.Min(profile.Se2, profile.Sw2)));
                maxHeight2 = Math.Max(maxHeight2, Math.Max(Math.Max(profile.Nw2, profile.Ne2), Math.Max(profile.Se2, profile.Sw2)));
            }

            float landslideRunPerHeight = AutoTerrainDesignationsMod.AccessLandslideRunPerHeight;
            var durabilityCorners = BuildDurabilityCorners(
                fixedProfiles,
                s_buildingFixedHeights2ByTile,
                rayDesignations,
                preciseTerrainHeights,
                terrainColumns,
                dumpingMaterialSlope,
                fallbackMiningSlope,
                landslideRunPerHeight);
            IPathabilityProvider provider = s_vehiclePathFindingManager.PathabilityProvider;
            // A generated V node is exactly one 4x4 designation wide.  The
            // profile's five samples are boundary points, not five traversible
            // tiles, so a five-wide Mega/T3 cannot use this graph yet.
            if (vehicleClearance > 4)
            {
                failureReason = "ExperimentalAccesswayWidthInsufficient";
                return false;
            }
            try { provider.UpdateChangedTiles(); } catch { }

            var groundNodes = new HashSet<Tile2i>();
            foreach (var pair in groundHeight2)
            {
                Tile2i tile = pair.Key;
                if (pair.Value < 2 && oceanTiles.Contains(tile))
                {
                    groundExclusionReasons[tile] = "OceanBelowMinimum";
                    continue;
                }
                Tile2i alignedOrigin = new Tile2i(tile.X & -4, tile.Y & -4);
                if (designatedOrigins.Contains(alignedOrigin))
                {
                    groundExclusionReasons[tile] = $"DesignatedOrigin@({alignedOrigin.X},{alignedOrigin.Y})";
                    continue;
                }
                if (projectedDesignationDisturbance.Contains(tile))
                {
                    groundExclusionReasons[tile] = "ProjectedDesignationWork";
                    continue;
                }
                if (provider.IsPathable(tile, pathParams.PathabilityQueryMask))
                    groundNodes.Add(tile);
                else
                    groundExclusionReasons[tile] = "NotPathable";
            }

            Dictionary<Tile2i, AccessPropCleanupInfo> propCleanupByOrigin =
                BuildAccessPropCleanupByOrigin(
                    tower,
                    terrMgr,
                    boundsMin,
                    boundsMax,
                    groundHeight2,
                    designatedOrigins,
                    oceanTiles,
                    projectedDesignationDisturbance,
                    ExtractVehicleClearance(pathParams),
                    out AccessPropCleanupSnapshotDiagnostics cleanupDiagnostics);
            var prospectiveHandoffCache =
                new Dictionary<string, IReadOnlyList<AccessGroundHandoff>>(StringComparer.Ordinal);

            if (!TryBuildTowerReachableGround(tower, boundsMin, boundsMax,
                groundNodes, provider, pathParams,
                out HashSet<Tile2i> towerReachableGround, out Tile2i groundStart))
            {
                failureReason = "NoTowerGround";
                return false;
            }
            if (towerReachableGround.Count == 0)
            {
                failureReason = "NoTowerReachableGround";
                return false;
            }
            int fullTowerGoalCount = towerReachableGround.Count;
            if (fullTowerGoalCount <= 16)
                LogTowerGroundFrontierDiagnostics(
                    groundStart,
                    towerReachableGround,
                    groundNodes,
                    groundExclusionReasons,
                    provider,
                    pathParams);
            towerReachableGround = SelectTowerRadialGroundGoals(
                towerCenter,
                towerReachableGround,
                maxSteps: 12,
                out string radialGoalDiagnostic);
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Tower Radial Goals] center={towerCenter} " +
                $"selected={towerReachableGround.Count} maxSteps=12 {radialGoalDiagnostic}");
            if (towerReachableGround.Count == 0)
            {
                failureReason = "NoTowerRadialGroundGoals";
                return false;
            }
            if (minHeight2 == int.MaxValue) { minHeight2 = 0; maxHeight2 = 0; }

            snapshot = new AccessSearchSnapshot(
                boundsMin,
                boundsMax,
                towerCenter,
                minHeight2 - 2,
                maxHeight2 + 2,
                isMining,
                allowsMixedWork,
                AutoTerrainDesignationsMod.ExperimentalAccessUseAStar,
                AutoTerrainDesignationsMod.AccessLandscapingCostDistanceScale,
                landslideRunPerHeight,
                groundHeight2,
                terrainCenterHeight2,
                fixedProfiles,
                workOrigins,
                groundNodes,
                towerReachableGround,
                s_buildingOccupiedTiles,
                oceanTiles,
                durabilityCorners,
                (origin, profile, predecessorOrigin, predecessorProfile) =>
                    BuildProspectiveWorkableHandoffs(
                        origin, profile, predecessorOrigin, predecessorProfile,
                        terrMgr, groundNodes, towerReachableGround,
                        propCleanupByOrigin, vehicleClearance, prospectiveHandoffCache),
                propCleanupByOrigin,
                preciseTerrainHeights,
                terrainColumns,
                physicalTerrainMin,
                physicalTerrainMax,
                dumpingMaterialSlope,
                fallbackMiningSlope,
                dumpingSlopeUsedFallback,
                hasDumpingMaterial,
                groundExclusionReasons: groundExclusionReasons,
                rayMiningDesignationOrigins: rayDesignations.Values
                    .Where(item => s_miningProto != null && item.Prototype == s_miningProto)
                    .Select(item => item.OriginTileCoord),
                rayDumpingDesignationOrigins: rayDesignations.Values
                    .Where(item => s_dumpingProto != null && item.Prototype == s_dumpingProto)
                    .Select(item => item.OriginTileCoord),
                rayLevelingDesignationOrigins: rayDesignations.Values
                    .Where(item => s_levelingProto != null && item.Prototype == s_levelingProto)
                    .Select(item => item.OriginTileCoord),
                projectedCutDisturbedTiles: projectedDesignationDisturbance.CutTiles,
                projectedFillDisturbedTiles: projectedDesignationDisturbance.FillTiles,
                projectedCutSupportCeilings: projectedDesignationDisturbance.CutSupportCeilings,
                projectedFillSurfaceFloors: projectedDesignationDisturbance.FillSurfaceFloors,
                vehicleClearanceRadius: vehicleDisturbanceRadius,
                avoidOcean: AccessAvoidOcean,
                avoidBuildings: AccessAvoidBuildings);
            snapshotTimer.Stop();
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Timing] phase=snapshot algorithm={(snapshot.UseAStar ? "A*" : "Dijkstra")} " +
                $"elapsedMs={snapshotTimer.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"goals={snapshot.GoalCount} fullTowerGoals={fullTowerGoalCount} towerGroundStart={groundStart} " +
                $"rayHeightSamples={preciseTerrainHeights.Count} rayMaterialColumns={terrainColumns.Count} " +
                $"dumpingSlope={dumpingMaterialSlope.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"fallbackMiningSlope={fallbackMiningSlope.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"hasDumpingMaterial={hasDumpingMaterial} " +
                $"avoidOcean={snapshot.AvoidOcean} avoidBuildings={snapshot.AvoidBuildings} " +
                $"materialSlopeSource={materialSlopeDiagnostic} " +
                $"landslideSources={snapshot.LandslideSourceCount} " +
                $"projectedDesignationBlockedTiles={projectedDesignationDisturbance.Count} " +
                $"pathParams={pathParamsSource}");
            LogAccessPropCleanupDiagnostics(cleanupDiagnostics);
            return true;
        }

        private static AccessTerrainColumn CaptureAccessTerrainColumn(
            TerrainManager terrainManager,
            Tile2i tile)
        {
            var layers = new List<AccessTerrainLayer>();
            float topHeight = terrainManager.GetHeight(tile).Value.ToFloat();
            TerrainLayerEnumerator enumerator =
                terrainManager.EnumerateLayers(terrainManager.GetTileIndex(tile));
            while (enumerator.MoveNext())
            {
                TerrainMaterialThicknessSlim layer = enumerator.Current;
                float thickness = layer.Thickness.Value.ToFloat();
                TerrainMaterialProto material = layer.SlimId.ToFull(terrainManager);
                float bottomHeight = topHeight - thickness;
                layers.Add(new AccessTerrainLayer(
                    topHeight,
                    bottomHeight,
                    GetNormalMaterialSlope(material),
                    material.Id.ToString()));
                topHeight = bottomHeight;
            }
            return new AccessTerrainColumn(layers);
        }

        private static void ResolveAccessMaterialSlopes(
            IAreaManagingTower tower,
            out float dumpingMaterialSlope,
            out float fallbackMiningSlope,
            out bool dumpingSlopeUsedFallback,
            out bool hasDumpingMaterial,
            out string diagnostic)
        {
            float fallbackDumpingSlope = float.MaxValue;
            fallbackMiningSlope = float.MaxValue;
            if (s_protosDb != null)
            {
                foreach (TerrainMaterialProto material in s_protosDb.All<TerrainMaterialProto>())
                {
                    fallbackDumpingSlope = Math.Min(
                        fallbackDumpingSlope,
                        material.GetApproxSlopeSteepness().ToFloat());
                    fallbackMiningSlope = Math.Min(
                        fallbackMiningSlope,
                        GetNormalMaterialSlope(material));
                }
            }
            if (fallbackDumpingSlope == float.MaxValue)
                fallbackDumpingSlope = 0.5f;
            if (fallbackMiningSlope == float.MaxValue)
                fallbackMiningSlope = 0.5f;

            dumpingMaterialSlope = float.MaxValue;
            int resolvedProducts = 0;
            if (TryGetTowerDumpableProducts(
                    tower,
                    out List<LooseProductProto> dumpableProducts,
                    out string error))
            {
                foreach (LooseProductProto product in dumpableProducts)
                {
                    if (!product.TerrainMaterial.HasValue)
                        continue;
                    dumpingMaterialSlope = Math.Min(
                        dumpingMaterialSlope,
                        product.TerrainMaterial.Value
                            .GetApproxSlopeSteepness().ToFloat());
                    resolvedProducts++;
                }
                diagnostic = resolvedProducts > 0
                    ? $"tower-rules:{resolvedProducts}"
                    : "blocked:no-terrain-products";
                hasDumpingMaterial = resolvedProducts > 0;
            }
            else
            {
                diagnostic = "fallback:" + error.Replace(' ', '-');
                hasDumpingMaterial = true;
            }

            dumpingSlopeUsedFallback = dumpingMaterialSlope == float.MaxValue;
            if (dumpingSlopeUsedFallback)
                dumpingMaterialSlope = fallbackDumpingSlope;
        }

        private static float GetNormalMaterialSlope(TerrainMaterialProto material)
            => (material.MinCollapseHeightDiff.Value
                + material.MaxCollapseHeightDiff.Value).ToFloat() / 3f;

        private static Dictionary<Tile2i, AccessPropCleanupInfo> BuildAccessPropCleanupByOrigin(
            IAreaManagingTower tower,
            TerrainManager terrMgr,
            Tile2i boundsMin,
            Tile2i boundsMax,
            IReadOnlyDictionary<Tile2i, int> groundHeight2,
            ISet<Tile2i> designatedOrigins,
            ISet<Tile2i> oceanTiles,
            ProjectedDesignationDisturbance projectedDesignationDisturbance,
            RelTile1i requiredClearance,
            out AccessPropCleanupSnapshotDiagnostics diagnostics)
        {
            diagnostics = new AccessPropCleanupSnapshotDiagnostics();
            var samplesByOrigin = new Dictionary<Tile2i, List<AccessPropSample>>();
            var blockersByOrigin = new Dictionary<Tile2i, AccessPropBlockerKind>();
            var buildingBlockedGroundTiles = new HashSet<Tile2i>();
            foreach (Tile2i occupiedTile in s_buildingOccupiedTiles)
                foreach (Tile2i blockedCenter in EnumerateBlockedCenterTilesForOccupiedTile(
                    occupiedTile, requiredClearance, boundsMin, boundsMax))
                    buildingBlockedGroundTiles.Add(blockedCenter);

            if (s_terrainPropsManager != null)
            {
                var area = new RectangleTerrainArea2i(
                    boundsMin,
                    new RelTile2i(boundsMax.X - boundsMin.X + 1, boundsMax.Y - boundsMin.Y + 1));
                var occupiedTiles = new Lyst<Tile2i>();
                foreach (TerrainPropData prop in s_terrainPropsManager.EnumeratePropsInArea(area))
                {
                    if (prop.Proto.DoesNotBlocksVehicles)
                        continue;

                    occupiedTiles.Clear();
                    prop.CalculateOccupiedTiles(terrMgr, occupiedTiles);
                    for (int i = 0; i < occupiedTiles.Count; i++)
                    {
                        Tile2i occupiedTile = occupiedTiles[i];
                        foreach (Tile2i tile in EnumerateBlockedCenterTilesForOccupiedTile(
                            occupiedTile, requiredClearance, boundsMin, boundsMax))
                        {
                            Tile2i origin = TerrainDesignation.GetOrigin(tile);
                            AccessPropSample sample = new AccessPropSample(
                                tile, isTree: false, isDenseDebris: true, isRemovable: true,
                                cleanupObjectKey: BuildPropCleanupKey(prop.Id));
                            AccessPropBlockerKind blocker = AddCleanupSample(
                                tower,
                                origin,
                                tile,
                                sample,
                                groundHeight2,
                                designatedOrigins,
                                oceanTiles,
                                buildingBlockedGroundTiles,
                                projectedDesignationDisturbance,
                                samplesByOrigin,
                                blockersByOrigin);
                            diagnostics.RecordSample(tile, origin, sample, blocker);
                            diagnostics.PropSamples++;
                        }
                    }
                }
            }

            if (s_treesManager != null)
            {
                foreach (TreeId treeId in s_treesManager.EnumerateTreesInArea(tower.Area))
                {
                    if (!s_treesManager.TryGetTree(treeId, out TreeData tree))
                        continue;

                    foreach (Tile2i tile in EnumerateBlockedCenterTilesForOccupiedTile(
                        tree.Id.Position.AsFull, requiredClearance, boundsMin, boundsMax))
                    {
                        Tile2i origin = TerrainDesignation.GetOrigin(tile);
                        AccessPropSample sample = new AccessPropSample(
                            tile, isTree: true, isDenseDebris: false, isRemovable: true,
                            cleanupObjectKey: BuildTreeCleanupKey(treeId));
                        AccessPropBlockerKind blocker = AddCleanupSample(
                            tower,
                            origin,
                            tile,
                            sample,
                            groundHeight2,
                            designatedOrigins,
                            oceanTiles,
                            buildingBlockedGroundTiles,
                            projectedDesignationDisturbance,
                            samplesByOrigin,
                            blockersByOrigin);
                        diagnostics.RecordSample(tile, origin, sample, blocker);
                        diagnostics.TreeSamples++;
                    }
                }
            }

            var cleanupByOrigin = new Dictionary<Tile2i, AccessPropCleanupInfo>();
            foreach (KeyValuePair<Tile2i, List<AccessPropSample>> pair in samplesByOrigin)
            {
                blockersByOrigin.TryGetValue(pair.Key, out AccessPropBlockerKind blockerKind);
                AccessPropCleanupInfo info = AccessPropCleanupPolicy.BuildOriginInfo(
                    pair.Key, pair.Value, blockerKind);
                cleanupByOrigin[pair.Key] = info;
                diagnostics.RecordOrigin(info);

                if (info.IsEligible)
                {
                    diagnostics.EligibleOrigins++;
                    if (info.HasTreeCleanup) diagnostics.TreeCleanupOrigins++;
                    if (info.HasDenseDebrisCleanup) diagnostics.DenseDebrisCleanupOrigins++;
                    if (info.UsesTerrainRemovalPolicy) diagnostics.TerrainRemovalPolicyOrigins++;
                }
                else if (info.BlockerKind != AccessPropBlockerKind.None)
                {
                    diagnostics.HardBlockedOrigins++;
                    diagnostics.CountBlocked(info.BlockerKind);
                }
            }

            return cleanupByOrigin;
        }

        private static AccessPropBlockerKind AddCleanupSample(
            IAreaManagingTower tower,
            Tile2i origin,
            Tile2i tile,
            AccessPropSample sample,
            IReadOnlyDictionary<Tile2i, int> groundHeight2,
            ISet<Tile2i> designatedOrigins,
            ISet<Tile2i> oceanTiles,
            ISet<Tile2i> buildingBlockedGroundTiles,
            ProjectedDesignationDisturbance projectedDesignationDisturbance,
            Dictionary<Tile2i, List<AccessPropSample>> samplesByOrigin,
            Dictionary<Tile2i, AccessPropBlockerKind> blockersByOrigin)
        {
            if (!samplesByOrigin.TryGetValue(origin, out List<AccessPropSample> samples))
            {
                samples = new List<AccessPropSample>();
                samplesByOrigin[origin] = samples;
            }
            samples.Add(sample);

            AccessPropBlockerKind blocker = GetCleanupBlockerKind(
                tower, origin, tile, groundHeight2, designatedOrigins,
                oceanTiles, buildingBlockedGroundTiles, projectedDesignationDisturbance);
            if (blocker != AccessPropBlockerKind.None
                && (!blockersByOrigin.TryGetValue(origin, out AccessPropBlockerKind existing)
                    || existing == AccessPropBlockerKind.None))
                blockersByOrigin[origin] = blocker;
            return blocker;
        }

        private static IEnumerable<Tile2i> EnumerateBlockedCenterTilesForOccupiedTile(
            Tile2i occupiedTile,
            RelTile1i requiredClearance,
            Tile2i boundsMin,
            Tile2i boundsMax)
        {
            int clearance = Math.Max(1, requiredClearance.Value);
            int radius = clearance;
            for (int y = occupiedTile.Y - radius; y <= occupiedTile.Y + radius; y++)
            {
                for (int x = occupiedTile.X - radius; x <= occupiedTile.X + radius; x++)
                {
                    Tile2i center = new Tile2i(x, y);
                    if (center.X < boundsMin.X || center.X > boundsMax.X
                        || center.Y < boundsMin.Y || center.Y > boundsMax.Y)
                        continue;
                    Tile2i corner = VehiclePathFindingParams.ConvertToCornerTileSpace(
                        center, requiredClearance);
                    if (occupiedTile.X >= corner.X && occupiedTile.X < corner.X + clearance
                        && occupiedTile.Y >= corner.Y && occupiedTile.Y < corner.Y + clearance)
                        yield return center;
                }
            }
        }

        private static string BuildTreeCleanupKey(TreeId treeId)
            => $"tree:{treeId.Position.X},{treeId.Position.Y}";

        private static string BuildPropCleanupKey(TerrainPropId propId)
            => $"prop:{propId.Position.X},{propId.Position.Y}";

        private static bool TryParsePropCleanupKey(string cleanupObjectKey, out TerrainPropId propId)
        {
            propId = TerrainPropId.Invalid;
            if (!cleanupObjectKey.StartsWith("prop:", StringComparison.Ordinal))
                return false;

            string[] parts = cleanupObjectKey.Substring("prop:".Length).Split(',');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                return false;

            propId = new TerrainPropId(x, y);
            return true;
        }

        private static RelTile1i ExtractVehicleClearance(VehiclePathFindingParams pathParams)
        {
            var mask = pathParams.PathabilityQueryMask;
            return ClearancePathabilityProvider.ExtractClearanceFromMask(ref mask);
        }

        private static AccessPropBlockerKind GetCleanupBlockerKind(
            IAreaManagingTower tower,
            Tile2i origin,
            Tile2i tile,
            IReadOnlyDictionary<Tile2i, int> groundHeight2,
            ISet<Tile2i> designatedOrigins,
            ISet<Tile2i> oceanTiles,
            ISet<Tile2i> buildingBlockedGroundTiles,
            ProjectedDesignationDisturbance projectedDesignationDisturbance)
        {
            if (!IsOriginInsideTower(tower, origin) || !tower.Area.ContainsTile(tile))
                return AccessPropBlockerKind.OutOfArea;
            if (designatedOrigins.Contains(origin))
                return AccessPropBlockerKind.ActiveTerrainDesignation;
            if (buildingBlockedGroundTiles.Contains(tile))
                return AccessPropBlockerKind.Building;
            if (!groundHeight2.TryGetValue(tile, out int height2))
                return AccessPropBlockerKind.OutOfArea;
            if (height2 < 2 && oceanTiles.Contains(tile))
                return AccessPropBlockerKind.Ocean;
            if (projectedDesignationDisturbance.Contains(tile))
                return AccessPropBlockerKind.Durability;
            return AccessPropBlockerKind.None;
        }

        private static void LogAccessPropCleanupDiagnostics(
            AccessPropCleanupSnapshotDiagnostics diagnostics)
        {
            if (diagnostics.PropSamples == 0
                && diagnostics.TreeSamples == 0
                && diagnostics.EligibleOrigins == 0
                && diagnostics.HardBlockedOrigins == 0)
                return;

            string blockers = diagnostics.BlockedByKind.Count == 0
                ? "none"
                : string.Join(",", diagnostics.BlockedByKind
                    .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}:{pair.Value}"));
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Cleanup] propSamples={diagnostics.PropSamples} " +
                $"treeSamples={diagnostics.TreeSamples} eligibleOrigins={diagnostics.EligibleOrigins} " +
                $"treeOrigins={diagnostics.TreeCleanupOrigins} denseDebrisOrigins={diagnostics.DenseDebrisCleanupOrigins} " +
                $"hardBlockedOrigins={diagnostics.HardBlockedOrigins} blockers=[{blockers}] " +
                $"terrainRemovalPolicyOrigins={diagnostics.TerrainRemovalPolicyOrigins}");
            if (diagnostics.PropSamples + diagnostics.TreeSamples > 2000
                || diagnostics.EligibleOrigins > 64
                || diagnostics.HardBlockedOrigins > 64)
            {
                LogExperimentalAccessDebug(
                    "[ATD Experimental Access Cleanup Details] suppressed for large snapshot");
                return;
            }
            if (diagnostics.SampleDetails.Count > 0)
                LogExperimentalAccessDebug(
                    $"[ATD Experimental Access Cleanup Samples] {string.Join("; ", diagnostics.SampleDetails)}");
            if (diagnostics.EligibleOriginDetails.Count > 0)
                LogExperimentalAccessDebug(
                    $"[ATD Experimental Access Cleanup Eligible Origins] {string.Join("; ", diagnostics.EligibleOriginDetails)}");
            if (diagnostics.BlockedOriginDetails.Count > 0)
                LogExperimentalAccessDebug(
                    $"[ATD Experimental Access Cleanup Blocked Origins] {string.Join("; ", diagnostics.BlockedOriginDetails)}");
        }

        private static AccessPathRequest BuildMergedGoalAccessRequest(
            AccessSearchSnapshot snapshot,
            AccessOriginCluster cluster,
            IEnumerable<Tile2i> fixedGoalOrigins,
            int requiredWidth,
            float maxCostLimit = float.MaxValue)
        {
            return new AccessPathRequest(
                $"merged-goals-cluster-{cluster.ClusterId}",
                snapshot,
                new AccessPathEndpoint(
                    AccessPathEndpointKind.FixedProfiles,
                    cluster.Origins.Select(origin => origin.Origin)),
                new AccessPathEndpoint(
                    fixedGoalOrigins,
                    snapshot.GoalGroundNodes),
                requiredWidth,
                AccessPathIntent.ConstructAccessway,
                maxCostLimit);
        }

        private sealed class ExperimentalAccessDryRunResult
        {
            public AccessSearchResult? Result;
        }

        internal static void SetUiRoot(UiRoot uiRoot)
        {
            s_uiRoot = uiRoot;
        }

        private static AccessSearchResult RunExperimentalAccessDryRun(
            AccessPathRequest request,
            AccessOriginCluster cluster)
        {
            AccessSearchSnapshot snapshot = request.Snapshot;
            Stopwatch searchTimer = Stopwatch.StartNew();
            AccessSearchResult result = AccessPathSearch.FindPath(request);
            searchTimer.Stop();
            RecordExperimentalAccessDryRun(request, cluster, snapshot, result, searchTimer.Elapsed, 1, searchTimer.Elapsed);
            return result;
        }

        private static IEnumerator RunExperimentalAccessDryRunSliced(
            AccessPathRequest request,
            AccessOriginCluster cluster,
            int clusterIndex,
            int clusterCount,
            ExperimentalAccessDryRunResult output)
        {
            AccessSearchSnapshot snapshot = request.Snapshot;
            Stopwatch searchTimer = Stopwatch.StartNew();
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Search Start] request={request.RequestId} cluster={cluster.ClusterId} " +
                $"{FormatAccessPathRequest(request)} cleanupOrigins={snapshot.EligibleCleanupOriginCount}");
            Stopwatch createSessionTimer = Stopwatch.StartNew();
            var session = AccessPathSearch.CreateSession(request);
            createSessionTimer.Stop();
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Search Session] request={request.RequestId} cluster={cluster.ClusterId} " +
                $"createMs={createSessionTimer.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"complete={session.IsComplete} pending={session.PendingNodes} visited={session.VisitedNodes}");
            int frames = 0;
            TimeSpan maxSlice = TimeSpan.Zero;
            int lastToastSecond = -1;
            int slowStepLogCount = 0;

            while (!session.IsComplete && !s_cancelExperimentalAccessSearch)
            {
                Stopwatch sliceTimer = Stopwatch.StartNew();
                do
                {
                    Stopwatch stepTimer = Stopwatch.StartNew();
                    session.Step(1);
                    stepTimer.Stop();
                    if (stepTimer.ElapsedMilliseconds >= 250 && slowStepLogCount < 8)
                    {
                        slowStepLogCount++;
                        LogExperimentalAccessDebug(
                            $"[ATD Experimental Access Search SlowStep] request={request.RequestId} " +
                            $"cluster={cluster.ClusterId} stepMs={stepTimer.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} " +
                            $"visited={session.VisitedNodes} pending={session.PendingNodes} " +
                            $"elapsedMs={searchTimer.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)}");
                    }
                }
                while (!session.IsComplete
                    && !s_cancelExperimentalAccessSearch
                    && sliceTimer.ElapsedMilliseconds < AutoTerrainDesignationsMod.AccessSearchFrameBudgetMs
                    && searchTimer.Elapsed.TotalSeconds < AutoTerrainDesignationsMod.AccessSearchTimeoutSeconds);
                sliceTimer.Stop();
                if (sliceTimer.Elapsed > maxSlice) maxSlice = sliceTimer.Elapsed;
                frames++;
                int elapsedSeconds = Math.Min(
                    AutoTerrainDesignationsMod.AccessSearchTimeoutSeconds,
                    (int)Math.Floor(searchTimer.Elapsed.TotalSeconds));
                if (elapsedSeconds != lastToastSecond)
                {
                    ShowTerrainAnalysisProgressToast(
                        clusterIndex,
                        clusterCount,
                        elapsedSeconds,
                        session.VisitedNodes,
                        session.PendingNodes);
                    lastToastSecond = elapsedSeconds;
                }
                if (!session.IsComplete)
                    yield return null;
                if (searchTimer.Elapsed.TotalSeconds >= AutoTerrainDesignationsMod.AccessSearchTimeoutSeconds
                    && !session.IsComplete)
                {
                    break;
                }
            }

            searchTimer.Stop();
            AccessSearchResult result = s_cancelExperimentalAccessSearch
                ? new AccessSearchResult(
                    false,
                    "SearchCancelled",
                    request.Start.Nodes.Count > 0 ? request.Start.Nodes[0] : default,
                    Array.Empty<AccessSearchNode>(),
                    0f,
                    session.VisitedNodes,
                    session.Rejections,
                    session.Diagnostics)
                : session.IsComplete
                ? session.Result
                : new AccessSearchResult(
                    false,
                    "SearchTimeLimit",
                    request.Start.Nodes.Count > 0 ? request.Start.Nodes[0] : default,
                    Array.Empty<AccessSearchNode>(),
                    0f,
                    session.VisitedNodes,
                    session.Rejections,
                    session.Diagnostics);
            output.Result = result;
            HideTerrainAnalysisProgressToast();
            RecordExperimentalAccessDryRun(request, cluster, snapshot, result, searchTimer.Elapsed, frames, maxSlice);
        }

        private static void ShowTerrainAnalysisProgressToast(
            int clusterIndex,
            int clusterCount,
            int elapsedSeconds,
            int visitedNodes,
            int pendingNodes)
        {
            if (s_uiRoot == null) return;
            try
            {
                int nodeLimit = AutoTerrainDesignationsMod.AccessMaxVisitedNodes;
                int timeLimit = AutoTerrainDesignationsMod.AccessSearchTimeoutSeconds;
                var notification = s_uiRoot.ToastNotifProvider.m_notification;
                notification.ShowGeneral(
                    new LocStrFormatted("[ATD] Terrain analysis in progress"),
                    showForever: true);
                notification.Body.SetChildren(
                    new Label(new LocStrFormatted(
                        $"[ATD] Terrain analysis — cluster {clusterIndex}/{clusterCount} · " +
                        $"visited {visitedNodes:N0}/{nodeLimit:N0} · queue {pendingNodes:N0} · " +
                        $"{elapsedSeconds}/{timeLimit}s"))
                        .FontSize(16),
                    new ButtonText(
                        Button.General,
                        new LocStrFormatted("Cancel"),
                        () => s_cancelExperimentalAccessSearch = true)
                        .MarginLeft(8.pt()));
            }
            catch
            {
            }
        }

        private static void HideTerrainAnalysisProgressToast()
        {
            try
            {
                s_uiRoot?.ToastNotifProvider.m_notification.Hide();
            }
            catch
            {
            }
        }

        private static void RecordExperimentalAccessDryRun(
            AccessPathRequest request,
            AccessOriginCluster cluster,
            AccessSearchSnapshot snapshot,
            AccessSearchResult result,
            TimeSpan elapsed,
            int frames,
            TimeSpan maxSlice)
        {
            LastExperimentalAccessSearch = result;
            string rejections = result.Rejections.Count == 0
                ? "none"
                : string.Join(",", result.Rejections
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}:{pair.Value}"));
            string reason = string.IsNullOrEmpty(result.FailureReason) ? "none" : result.FailureReason;
            string cost = result.Cost.ToString("0.##", CultureInfo.InvariantCulture);
            string searchMs = elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
            string maxSliceMs = maxSlice.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
            string landslideRun = snapshot.LandslideRunPerHeight.ToString("0.##", CultureInfo.InvariantCulture);
            AccessSearchDiagnostics diag = result.Diagnostics;
            double ticksToMs = 1000d / Stopwatch.Frequency;
            string diagnostics =
                $"expansions=[G:{diag.GroundExpansions},V:{diag.OriginExpansions}] " +
                $"ground=[checks:{diag.GroundSuccessorChecks},relax:{diag.GroundRelaxations},cleanupChecks:{diag.CleanupGroundSuccessorChecks},cleanupRelax:{diag.CleanupGroundRelaxations}] " +
                $"generated=[neighbors:{diag.OriginNeighborChecks},modes:{diag.GeneratedModeAttempts},g2vOrigins:{diag.GroundToGeneratedOriginChecks},g2vProfiles:{diag.GroundToGeneratedProfileAttempts},g2vNoHandoff:{diag.GroundToGeneratedHandoffFailures},relax:{diag.GeneratedRelaxations}] " +
                $"profile=[checks:{diag.GeneratedProfileFeasibleChecks},fail:{diag.GeneratedProfileFeasibleFailures},historyFail:{diag.GeneratedPathHistoryFailures}] " +
                $"sideRay=[checks:{diag.SideRayCostChecks},reject:{diag.SideRayCostRejections},samples:{diag.SideRayCostSamples},cacheHit:{diag.SideRayCacheHits},cacheMiss:{diag.SideRayCacheMisses},historyReuse:{diag.GeneratedHistoryCostReuses},historyRecalc:{diag.GeneratedHistoryCostRecalculations}] " +
                $"history=[created:{diag.GeneratedHistoryNodesCreated},maxDepth:{diag.GeneratedHistoryMaxDepth}] " +
                $"prop=[checks:{diag.PropCleanupChecks},hits:{diag.PropCleanupHits},reject:{diag.PropCleanupRejections}] " +
                $"fixed=[checks:{diag.FixedProfileSuccessorChecks},relax:{diag.FixedProfileRelaxations}] " +
                $"goals=[pops:{diag.GoalPops},rejected:{diag.GoalRejected},acceptedAt:{diag.GoalAcceptedAtVisited}] " +
                $"queue=[relax:{diag.QueueRelaxations},stale:{diag.QueueStalePops}] " +
                $"timingMs=[ground:{(diag.GroundExpansionTicks * ticksToMs).ToString("0.###", CultureInfo.InvariantCulture)}," +
                $"origin:{(diag.OriginExpansionTicks * ticksToMs).ToString("0.###", CultureInfo.InvariantCulture)}," +
                $"profile:{(diag.ProfileFeasibilityTicks * ticksToMs).ToString("0.###", CultureInfo.InvariantCulture)}," +
                $"handoff:{(diag.HandoffValidationTicks * ticksToMs).ToString("0.###", CultureInfo.InvariantCulture)}," +
                $"history:{(diag.PathHistoryTicks * ticksToMs).ToString("0.###", CultureInfo.InvariantCulture)}," +
                $"sideRay:{(diag.SideRayCostTicks * ticksToMs).ToString("0.###", CultureInfo.InvariantCulture)}," +
                $"prop:{(diag.PropCleanupTicks * ticksToMs).ToString("0.###", CultureInfo.InvariantCulture)}]";
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access] request={request.RequestId} " +
                $"{FormatAccessPathRequest(request)} cluster={cluster.ClusterId} " +
                $"algorithm={(snapshot.UseAStar ? "A*" : "Dijkstra")} " +
                $"success={result.Success} reason={reason} start=({result.StartOrigin.X},{result.StartOrigin.Y}) " +
                $"goals={request.Goal.Nodes.Count} reachedGoal={result.ReachedGoalKind} landslideRun={landslideRun} " +
                $"landslideSources={snapshot.LandslideSourceCount} cost={cost} " +
                $"visited={result.VisitedNodes} pathNodes={result.Path.Count} frames={frames} " +
                $"searchMs={searchMs} maxSliceMs={maxSliceMs} " +
                $"{diagnostics} " +
                $"rejections=[{rejections}]");
            if (result.Success)
            {
                LogExperimentalAccessDebug($"[ATD Experimental Access Path] cluster={cluster.ClusterId} {FormatExperimentalPath(result)}");
                LogExperimentalNonGoalGroundDiagnostics(snapshot, result, cluster.ClusterId);
                LogExperimentalSelectedVToGHandoffDiagnostics(snapshot, result, cluster.ClusterId);
                Stopwatch materializeTimer = Stopwatch.StartNew();
                AccessDesignationPlan plan = AccessPathMaterializer.Materialize(snapshot, result);
                materializeTimer.Stop();
                LastExperimentalAccessPlan = plan;
                string materializeMs = materializeTimer.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
                float selectedSideRayCost = result.LeftSideRayCost
                    + result.RightSideRayCost
                    + result.SideRayUnresolvedPenalty;
                float selectedCenterOnlyCost = result.Cost
                    - AccessPathSearch.SideRayWeight * selectedSideRayCost;
                LogExperimentalAccessDebug(
                    $"[ATD Experimental Access Plan] cluster={cluster.ClusterId} valid={plan.IsValid} " +
                    $"reason={(string.IsNullOrEmpty(plan.FailureReason) ? "none" : plan.FailureReason)} " +
                    $"designations={plan.Designations.Count} reused={plan.ReusedNodeCount} " +
                    $"groundNodes={plan.GroundNodeCount} cleanupOrigins={plan.CleanupOrigins.Count} " +
                    $"traversalCost={result.TraversalCost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"generatedWorkCost={result.GeneratedWorkCost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"generatedDirectCost={result.GeneratedDirectWorkCost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"leftRayCost={result.LeftSideRayCost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"rightRayCost={result.RightSideRayCost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"unresolvedRayPenalty={result.SideRayUnresolvedPenalty.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"raySamples={result.SideRaySampleCount} " +
                    $"selectedCenterOnlyCost={selectedCenterOnlyCost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"directWeight={AccessPathSearch.DirectWorkWeight.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"sideRayWeight={AccessPathSearch.SideRayWeight.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"maxRayCost={AutoTerrainDesignationsMod.AccessRayMaxCost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"unresolvedRayCap={AutoTerrainDesignationsMod.AccessRayUnresolvedPenalty.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"generatedFixedCost={result.GeneratedFixedCost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"treeCleanupCost={result.TreeCleanupCost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"denseDebrisCleanupCost={result.DenseDebrisCleanupCost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                    $"handoff=({plan.HandoffGround.X},{plan.HandoffGround.Y}) " +
                    $"handoffOperation={plan.HandoffOperation} materializeMs={materializeMs}");
                if (plan.IsValid)
                {
                    LogExperimentalAccessDebug($"[ATD Experimental Access Plan Tiles] cluster={cluster.ClusterId} {FormatExperimentalPlan(plan)}");
                    LogExperimentalCleanupRouteDiagnostics(snapshot, result, plan, cluster.ClusterId);
                }
            }
            else
            {
                LastExperimentalAccessPlan = null;
                if (result.Path.Count > 0)
                    LogExperimentalAccessDebug($"[ATD Experimental Access Rejected Path] cluster={cluster.ClusterId} {FormatExperimentalPath(result)}");
            }
        }

        private static void LogExperimentalNonGoalGroundDiagnostics(
            AccessSearchSnapshot snapshot,
            AccessSearchResult result,
            int clusterId)
        {
            var firstDetails = new List<string>();
            var tailDetails = new Queue<string>();
            int nonGoalGroundCount = 0;
            foreach (AccessSearchNode node in result.Path)
            {
                if (!node.IsGround || snapshot.IsGoalGroundNode(node.Position))
                    continue;
                nonGoalGroundCount++;
                Tile2i alignedOrigin = new Tile2i(node.Position.X & -4, node.Position.Y & -4);
                string detail =
                    $"G@({node.Position.X},{node.Position.Y},h={(node.Height2 / 2f).ToString("0.##", CultureInfo.InvariantCulture)})" +
                    $":origin=({alignedOrigin.X},{alignedOrigin.Y}) status={snapshot.DescribeGroundGoalStatus(node.Position)}";
                if (firstDetails.Count < 24)
                    firstDetails.Add(detail);
                tailDetails.Enqueue(detail);
                while (tailDetails.Count > 48)
                    tailDetails.Dequeue();
            }
            if (nonGoalGroundCount == 0)
                return;
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access NonGoalGround] cluster={clusterId} " +
                $"count={nonGoalGroundCount} first=[{string.Join("; ", firstDetails)}] " +
                $"tail=[{string.Join("; ", tailDetails)}]");
        }

        // This runs only after a route has been selected.  In particular it does
        // not add any work to the very hot handoff-candidate loop.  It gives us
        // enough information to distinguish a missing V->G transition because
        // the outward lanes are unavailable from one because the profile itself
        // cannot make a usable working-edge handoff.
        private static void LogExperimentalSelectedVToGHandoffDiagnostics(
            AccessSearchSnapshot snapshot,
            AccessSearchResult result,
            int clusterId)
        {
            TerrainManager? terrainManager = s_desigManager?.TerrainManager;
            if (terrainManager == null)
                return;
            var details = new List<string>();
            for (int index = 1; index < result.Path.Count; index++)
            {
                AccessSearchNode node = result.Path[index];
                AccessSearchNode predecessor = result.Path[index - 1];
                if (node.IsGround || node.Mode == AccessSearchMode.Existing
                    || !AccessHeightProfile.TryForMode(node.Mode, node.Height2,
                        out AccessHeightProfile profile))
                    continue;

                AccessHeightProfile predecessorProfile = predecessor.IsGround
                    || !AccessHeightProfile.TryForMode(predecessor.Mode,
                        predecessor.Height2, out AccessHeightProfile foundPredecessorProfile)
                    ? profile
                    : foundPredecessorProfile;
                IReadOnlyList<AccessGroundHandoff> handoffs = snapshot.GetWorkableHandoffs(
                    node.Position, profile, predecessor.Position, predecessorProfile);
                if (handoffs.Count > 0)
                {
                    details.Add(
                        $"V@({node.Position.X},{node.Position.Y}) exits=[" +
                        string.Join(",", handoffs.Select(handoff =>
                            $"({handoff.Tile.X},{handoff.Tile.Y}):{handoff.Operation}")) + "]");
                    continue;
                }

                if (!TryGetDirectionalHandoff(node.Position, profile,
                    predecessor.Position, terrainManager, 1, out int edge,
                    out AccessHandoffOperation operation, out _))
                    continue;
                RelTile2i outward = GetHandoffOutwardDirection(edge);
                var lanes = new List<string>();
                for (int offset = 1; offset < 4; offset++)
                {
                    int x = edge == 0 ? 0 : edge == 1 ? 4 : offset;
                    int y = edge == 2 ? 0 : edge == 3 ? 4 : offset;
                    if (outward.X > 0) x--;
                    if (outward.Y > 0) y--;
                    Tile2i outside = node.Position + new RelTile2i(x + outward.X, y + outward.Y);
                    Tile2i nextOutside = outside + outward;
                    lanes.Add(
                        $"({outside.X},{outside.Y})={snapshot.DescribeGroundGoalStatus(outside)}" +
                        $"/next={snapshot.DescribeGroundGoalStatus(nextOutside)}");
                }
                details.Add(
                    $"V@({node.Position.X},{node.Position.Y}) noExit op={operation} edge={edge} " +
                    $"lanes=[{string.Join(",", lanes)}]");
            }

            if (details.Count > 0)
                LogExperimentalAccessDebug(
                    $"[ATD Experimental Access Selected VToG] cluster={clusterId} " +
                    $"count={details.Count} details=[{string.Join("; ", details)}]");
        }

        private static string FormatAccessPathRequest(AccessPathRequest request)
        {
            return
                $"intent={request.Intent} width={request.RequiredWidth} " +
                $"start={request.Start.Kind}:{request.Start.Nodes.Count} " +
                $"goal={request.Goal.Kind}:{request.Goal.Nodes.Count}" +
                $"[fixed={request.Goal.FixedProfileNodes.Count},ground={request.Goal.GroundTileNodes.Count}] " +
                $"bounds=({request.BoundsMin.X},{request.BoundsMin.Y})..({request.BoundsMax.X},{request.BoundsMax.Y})";
        }

        private static IReadOnlyList<AccessGroundHandoff> BuildProspectiveWorkableHandoffs(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i predecessorOrigin,
            AccessHeightProfile predecessorProfile,
            TerrainManager terrMgr,
            HashSet<Tile2i> groundNodes,
            HashSet<Tile2i> goalGroundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByOrigin,
            int vehicleClearance,
            Dictionary<string, IReadOnlyList<AccessGroundHandoff>> handoffCache)
        {
            if (((profile.Nw2 | profile.Ne2 | profile.Se2 | profile.Sw2) & 1) != 0)
                return Array.Empty<AccessGroundHandoff>();

            if (!TryGetDirectionalHandoff(
                    origin, profile, predecessorOrigin, terrMgr, vehicleClearance,
                    out int handoffEdge, out AccessHandoffOperation operation,
                    out string directionalDiagnostic))
            {
                if (s_enableVerboseHandoffDiagnostics)
                    LogExistingHandoffDiagnostic(origin, predecessorOrigin,
                        directionalDiagnostic + " selected=None");
                return Array.Empty<AccessGroundHandoff>();
            }
            string cacheKey =
                origin.X.ToString(CultureInfo.InvariantCulture) + "," +
                origin.Y.ToString(CultureInfo.InvariantCulture) + "|" +
                profile.Nw2.ToString(CultureInfo.InvariantCulture) + "," +
                profile.Ne2.ToString(CultureInfo.InvariantCulture) + "," +
                profile.Se2.ToString(CultureInfo.InvariantCulture) + "," +
                profile.Sw2.ToString(CultureInfo.InvariantCulture) + "|" +
                predecessorOrigin.X.ToString(CultureInfo.InvariantCulture) + "," +
                predecessorOrigin.Y.ToString(CultureInfo.InvariantCulture) + "|" +
                operation;
            if (handoffCache.TryGetValue(cacheKey, out IReadOnlyList<AccessGroundHandoff> cached))
                return cached;

            var result = new List<AccessGroundHandoff>();
            var emitted = new HashSet<Tile2i>();
            List<string>? candidateDiagnostics = s_enableVerboseHandoffDiagnostics
                ? new List<string>()
                : null;
            int groundCandidateCount = 0;
            int connectedCandidateCount = 0;

            var data = new DesignationData(origin,
                new HeightTilesI(profile.Nw2 / 2),
                new HeightTilesI(profile.Ne2 / 2),
                new HeightTilesI(profile.Se2 / 2),
                new HeightTilesI(profile.Sw2 / 2));
            TerrainDesignationProto? proto = operation == AccessHandoffOperation.Mining
                ? s_miningProto
                : operation == AccessHandoffOperation.Dumping
                    ? s_dumpingProto
                    : operation == AccessHandoffOperation.Leveling
                        ? s_levelingProto
                        : null;
            if (proto == null)
            {
                handoffCache[cacheKey] = result.ToArray();
                return handoffCache[cacheKey];
            }

            RelTile2i outwardDirection = GetHandoffOutwardDirection(handoffEdge);
            var postWorkPathOutCache =
                new Dictionary<Tile2i, IReadOnlyList<Tile2i>?>();
            for (int offset = 1; offset < 4; offset++)
            {
                if (!IsClearanceValidHandoffLane(offset, vehicleClearance))
                    continue;

                int edgeX = handoffEdge == 0 ? 0 : handoffEdge == 1 ? 4 : offset;
                int edgeY = handoffEdge == 2 ? 0 : handoffEdge == 3 ? 4 : offset;
                int insideX = edgeX - (outwardDirection.X > 0 ? 1 : 0);
                int insideY = edgeY - (outwardDirection.Y > 0 ? 1 : 0);
                Tile2i insideEdgeTile = origin + new RelTile2i(insideX, insideY);
                Tile2i outsideTile = insideEdgeTile + outwardDirection;
                bool outsideGround = IsExperimentalAccessGroundOrCleanupNode(
                    groundNodes, propCleanupByOrigin, outsideTile);
                if (outsideGround)
                    groundCandidateCount++;
                float edgeTargetHeight = GetDesignationTargetHeightAt(
                    data, insideX, insideY).Value.ToFloat();
                bool meetsOutsideGround = Math.Abs(
                    edgeTargetHeight - terrMgr.GetHeight(outsideTile).Value.ToFloat()) <= 0.0001f;
                TryAddHandoff(
                    outsideTile,
                    outsideGround && meetsOutsideGround,
                    "outside",
                    new[] { outsideTile });

                // The final profile can cross natural ground before reaching
                // its G-facing edge.  A target-vehicle-pathable tile inside
                // that crossed footprint then provides the G/V bridge.
                for (int depth = 0; depth < 4; depth++)
                {
                    int tileX = handoffEdge == 0 ? depth : handoffEdge == 1 ? 3 - depth : offset;
                    int tileY = handoffEdge == 2 ? depth : handoffEdge == 3 ? 3 - depth : offset;
                    Tile2i interiorTile = origin + new RelTile2i(tileX, tileY);
                    bool interiorGround = IsExperimentalAccessGroundOrCleanupNode(
                        groundNodes, propCleanupByOrigin, interiorTile);
                    if (interiorGround)
                        groundCandidateCount++;
                    IReadOnlyList<Tile2i>? escapeTiles = interiorGround
                        && DoesProfileCrossGroundInTile(
                            data, terrMgr, handoffEdge, tileX, tileY)
                            ? GetPostWorkPathOut(interiorTile)
                            : null;
                    TryAddHandoff(
                        interiorTile,
                        escapeTiles != null,
                        "inside",
                        escapeTiles);
                }

                void TryAddHandoff(
                    Tile2i tile,
                    bool connected,
                    string location,
                    IReadOnlyList<Tile2i>? escapeTiles)
                {
                    if (candidateDiagnostics != null)
                        candidateDiagnostics.Add(
                            $"{location}=({tile.X},{tile.Y}):connected={connected}," +
                            $"goal={goalGroundNodes.Contains(tile)}");
                    if (!connected || !emitted.Add(tile))
                        return;
                    connectedCandidateCount++;
                    result.Add(new AccessGroundHandoff(tile, operation, escapeTiles));
                }

                IReadOnlyList<Tile2i>? GetPostWorkPathOut(Tile2i tile)
                {
                    if (postWorkPathOutCache.TryGetValue(
                        tile, out IReadOnlyList<Tile2i>? cached))
                        return cached;
                    IReadOnlyList<Tile2i>? path = FindPostWorkGroundPathOutOfHandoffCell(
                        origin, predecessorOrigin, data, terrMgr, operation, tile,
                        groundNodes, propCleanupByOrigin);
                    postWorkPathOutCache[tile] = path;
                    return path;
                }
            }
            if (candidateDiagnostics != null)
                LogExistingHandoffDiagnostic(origin, predecessorOrigin,
                    directionalDiagnostic
                    + " selected=" + operation
                    + " edge=" + handoffEdge.ToString(CultureInfo.InvariantCulture)
                    + " groundCandidates=" + groundCandidateCount.ToString(CultureInfo.InvariantCulture)
                    + " connectedCandidates=" + connectedCandidateCount.ToString(CultureInfo.InvariantCulture)
                    + " candidates=[" + string.Join(";", candidateDiagnostics) + "]");
            handoffCache[cacheKey] = result.ToArray();
            return handoffCache[cacheKey];
        }

        private static void LogExistingHandoffDiagnostic(
            Tile2i origin,
            Tile2i predecessorOrigin,
            string details)
        {
            if (!s_enableVerboseHandoffDiagnostics)
                return;
            if (s_desigManager == null)
                return;
            bool nearExistingDesignation = s_desigManager.GetDesignationAt(origin).HasValue;
            var directions = new[]
            {
                new RelTile2i(4, 0), new RelTile2i(-4, 0),
                new RelTile2i(0, 4), new RelTile2i(0, -4),
            };
            for (int i = 0; i < directions.Length && !nearExistingDesignation; i++)
                nearExistingDesignation = s_desigManager.GetDesignationAt(origin + directions[i]).HasValue;
            if (!nearExistingDesignation)
                return;
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Handoff Diagnostic] origin=({origin.X},{origin.Y}) " +
                $"predecessor=({predecessorOrigin.X},{predecessorOrigin.Y}) {details}");
        }

        internal static bool IsInteriorHandoffEdgeTile(
            int x,
            int y,
            int handoffEdge)
        {
            int offset = handoffEdge == 0 || handoffEdge == 1 ? y : x;
            return offset > 0 && offset < 4;
        }

        internal static bool IsClearanceValidHandoffLane(int offset, int vehicleClearance)
        {
            if (vehicleClearance > 4)
                return false;
            int sideMargin = Math.Min(2, Math.Max(0, (vehicleClearance - 1) / 2));
            return offset > 0 && offset < 4
                && offset >= sideMargin && offset < 4 - sideMargin;
        }

        private static bool DoesProfileCrossGroundInTile(
            DesignationData data,
            TerrainManager terrMgr,
            int handoffEdge,
            int tileX,
            int tileY)
        {
            int nextX = handoffEdge == 0 || handoffEdge == 1 ? tileX + 1 : tileX;
            int nextY = handoffEdge == 2 || handoffEdge == 3 ? tileY + 1 : tileY;
            float firstDelta = GetDesignationTargetHeightAt(data, tileX, tileY).Value.ToFloat()
                - terrMgr.GetHeight(data.OriginTile + new RelTile2i(tileX, tileY)).Value.ToFloat();
            float secondDelta = GetDesignationTargetHeightAt(data, nextX, nextY).Value.ToFloat()
                - terrMgr.GetHeight(data.OriginTile + new RelTile2i(nextX, nextY)).Value.ToFloat();
            int firstSign = CompareHeightDeltaToGround(firstDelta);
            int secondSign = CompareHeightDeltaToGround(secondDelta);
            return firstSign == 0 || secondSign == 0 || firstSign != secondSign;
        }

        private static IReadOnlyList<Tile2i>? FindPostWorkGroundPathOutOfHandoffCell(
            Tile2i origin,
            Tile2i adjacentVOrigin,
            DesignationData data,
            TerrainManager terrMgr,
            AccessHandoffOperation operation,
            Tile2i start,
            HashSet<Tile2i> groundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByOrigin)
        {
            if (!IsExperimentalAccessGroundOrCleanupNode(
                    groundNodes, propCleanupByOrigin, start)
                || IsHandoffWorkTile(origin, data, terrMgr, operation, start))
                return null;

            if (!IsInsideVFootprint(start, origin)
                && !IsInsideVFootprint(start, adjacentVOrigin))
                return new[] { start };

            var queue = new Queue<Tile2i>();
            var visited = new HashSet<Tile2i>();
            var parent = new Dictionary<Tile2i, Tile2i>();
            queue.Enqueue(start);
            visited.Add(start);
            RelTile2i[] directions =
            {
                new RelTile2i(-1, 0), new RelTile2i(1, 0),
                new RelTile2i(0, -1), new RelTile2i(0, 1),
            };
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                for (int index = 0; index < directions.Length; index++)
                {
                    Tile2i next = current + directions[index];
                    bool insideHandoffV = IsInsideVFootprint(next, origin);
                    bool insideAdjacentV = IsInsideVFootprint(next, adjacentVOrigin);
                    if (!IsExperimentalAccessGroundOrCleanupNode(
                            groundNodes, propCleanupByOrigin, next))
                        continue;
                    if (!insideHandoffV && !insideAdjacentV)
                    {
                        parent[next] = current;
                        var path = new List<Tile2i> { next };
                        Tile2i pathTile = next;
                        while (pathTile != start)
                        {
                            pathTile = parent[pathTile];
                            path.Add(pathTile);
                        }
                        path.Reverse();
                        return path;
                    }
                    if (insideAdjacentV)
                        continue;
                    if (IsHandoffWorkTile(origin, data, terrMgr, operation, next)
                        || !visited.Add(next))
                        continue;
                    parent[next] = current;
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        private static bool IsInsideVFootprint(Tile2i tile, Tile2i origin)
            => tile.X >= origin.X && tile.X < origin.X + 4
                && tile.Y >= origin.Y && tile.Y < origin.Y + 4;

        private static bool IsHandoffWorkTile(
            Tile2i origin,
            DesignationData data,
            TerrainManager terrMgr,
            AccessHandoffOperation operation,
            Tile2i tile)
        {
            int x = tile.X - origin.X;
            int y = tile.Y - origin.Y;
            if (x < 0 || x >= 4 || y < 0 || y >= 4)
                return false;
            float targetHeight = GetDesignationTargetHeightAt(data, x, y).Value.ToFloat();
            float groundHeight = terrMgr.GetHeight(tile).Value.ToFloat();
            const float epsilon = 0.0001f;
            return operation == AccessHandoffOperation.Mining
                ? targetHeight < groundHeight - epsilon
                : operation == AccessHandoffOperation.Dumping
                    ? targetHeight > groundHeight + epsilon
                    : Math.Abs(targetHeight - groundHeight) > epsilon;
        }

        private static RelTile2i GetHandoffOutwardDirection(int handoffEdge)
            => handoffEdge == 0
                ? new RelTile2i(-1, 0)
                : handoffEdge == 1
                    ? new RelTile2i(1, 0)
                    : handoffEdge == 2
                        ? new RelTile2i(0, -1)
                        : new RelTile2i(0, 1);

        private static bool TryGetDirectionalHandoff(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i predecessorPosition,
            TerrainManager terrMgr,
            int vehicleClearance,
            out int handoffEdge,
            out AccessHandoffOperation operation,
            out string diagnostic)
        {
            if (!TryGetConnectedAndHandoffCorners(
                    origin, predecessorPosition,
                    out int connectedA, out int connectedB,
                    out int handoffA, out int handoffB,
                    out handoffEdge))
            {
                operation = AccessHandoffOperation.None;
                diagnostic = "orientation=invalid";
                return false;
            }

            diagnostic = string.Empty;
            if (s_enableVerboseHandoffDiagnostics)
            {
                Tile2i[] corners =
                {
                    origin,
                    origin + new RelTile2i(4, 0),
                    origin + new RelTile2i(4, 4),
                    origin + new RelTile2i(0, 4),
                };
                int[] heights = { profile.Nw2, profile.Ne2, profile.Se2, profile.Sw2 };
                var heightDeltas = new float[corners.Length];
                for (int i = 0; i < corners.Length; i++)
                {
                    float groundHeight = terrMgr.GetHeight(corners[i]).Value.ToFloat();
                    heightDeltas[i] = heights[i] * 0.5f - groundHeight;
                }
                diagnostic =
                    "deltas=[" + string.Join(",", heightDeltas.Select(
                        delta => delta.ToString("0.###", CultureInfo.InvariantCulture))) + "]"
                    + " connectedCorners=" + connectedA + "," + connectedB
                    + " groundCorners=" + handoffA + "," + handoffB;
            }

            int connectedEdge = OppositeEdge(handoffEdge);
            int prospectiveHandoffEdge = handoffEdge;
            int[] connectedSigns = GetEdgeHeightSigns(
                origin, profile, connectedEdge, terrMgr,
                s_enableVerboseHandoffDiagnostics, out float[] connectedDeltas);
            int[] handoffSigns = GetEdgeHeightSigns(
                origin, profile, handoffEdge, terrMgr,
                s_enableVerboseHandoffDiagnostics, out float[] handoffDeltas);
            if (s_enableVerboseHandoffDiagnostics)
                diagnostic +=
                    " connectedEdgeDeltas=[" + FormatHeightDeltas(connectedDeltas) + "]"
                    + " groundEdgeDeltas=[" + FormatHeightDeltas(handoffDeltas) + "]";

            bool groundEdgeLevel = handoffSigns.All(sign => sign == 0);
            if (groundEdgeLevel)
            {
                operation = AccessHandoffOperation.Leveling;
                return true;
            }

            bool mining = connectedSigns.All(sign => sign <= 0)
                && connectedSigns.Any(sign => sign < 0)
                && HasVanillaWorkableExitLane(isMining: true);
            if (mining)
            {
                operation = AccessHandoffOperation.Mining;
                return true;
            }

            bool dumping = connectedSigns.All(sign => sign >= 0)
                && connectedSigns.Any(sign => sign > 0)
                && HasVanillaWorkableExitLane(isMining: false);
            if (dumping)
            {
                operation = AccessHandoffOperation.Dumping;
                return true;
            }

            operation = AccessHandoffOperation.None;
            return false;

            bool HasVanillaWorkableExitLane(bool isMining)
            {
                TerrainDesignationProto? proto = isMining ? s_miningProto : s_dumpingProto;
                if (proto == null || s_desigManager == null)
                    return false;
                var data = new DesignationData(origin,
                    new HeightTilesI(profile.Nw2 / 2),
                    new HeightTilesI(profile.Ne2 / 2),
                    new HeightTilesI(profile.Se2 / 2),
                    new HeightTilesI(profile.Sw2 / 2));
                for (int offset = 1; offset < 4; offset++)
                {
                    if (!IsClearanceValidHandoffLane(offset, vehicleClearance))
                        continue;
                    int x = prospectiveHandoffEdge == 0 ? 0
                        : prospectiveHandoffEdge == 1 ? 4 : offset;
                    int y = prospectiveHandoffEdge == 2 ? 0
                        : prospectiveHandoffEdge == 3 ? 4 : offset;
                    Tile2i tile = origin + new RelTile2i(x, y);
                    HeightTilesF target = GetDesignationTargetHeightAt(data, x, y);
                    bool upperEdge = x == 4 || y == 4;
                    bool fulfilled = isMining
                        ? proto.IsFulfilledMiningFn.HasValue
                            && proto.IsFulfilledMiningFn.Value(
                                s_desigManager, terrMgr.ExtendTileIndex(tile), target, upperEdge)
                        : proto.IsFulfilledDumpingFn.HasValue
                            && proto.IsFulfilledDumpingFn.Value(
                                s_desigManager, terrMgr.ExtendTileIndex(tile), target, upperEdge);
                    if (fulfilled)
                        return true;
                }
                return false;
            }
        }

        private static int[] GetEdgeHeightSigns(
            Tile2i origin,
            AccessHeightProfile profile,
            int edge,
            TerrainManager terrMgr,
            bool collectDeltas,
            out float[] deltas)
        {
            var data = new DesignationData(origin,
                new HeightTilesI(profile.Nw2 / 2),
                new HeightTilesI(profile.Ne2 / 2),
                new HeightTilesI(profile.Se2 / 2),
                new HeightTilesI(profile.Sw2 / 2));
            var signs = new int[5];
            deltas = collectDeltas ? new float[5] : Array.Empty<float>();
            for (int offset = 0; offset <= 4; offset++)
            {
                int x = edge == 0 ? 0 : edge == 1 ? 4 : offset;
                int y = edge == 2 ? 0 : edge == 3 ? 4 : offset;
                Tile2i tile = origin + new RelTile2i(x, y);
                float targetHeight = GetDesignationTargetHeightAt(data, x, y).Value.ToFloat();
                float groundHeight = terrMgr.GetHeight(tile).Value.ToFloat();
                float delta = targetHeight - groundHeight;
                if (collectDeltas)
                    deltas[offset] = delta;
                signs[offset] = CompareHeightDeltaToGround(delta);
            }
            return signs;
        }

        private static int OppositeEdge(int edge)
            => edge == 0 ? 1 : edge == 1 ? 0 : edge == 2 ? 3 : 2;

        private static string FormatHeightDeltas(IEnumerable<float> deltas)
            => string.Join(",", deltas.Select(
                delta => delta.ToString("0.###", CultureInfo.InvariantCulture)));

        private static int CompareProfileHeightToGround(int profileHeight2, float groundHeight)
            => CompareHeightDeltaToGround(profileHeight2 * 0.5f - groundHeight);

        private static int CompareHeightDeltaToGround(float delta)
        {
            const float exactLevelEpsilon = 0.0001f;
            return Math.Abs(delta) <= exactLevelEpsilon ? 0 : Math.Sign(delta);
        }

        private static bool TryGetConnectedAndHandoffCorners(
            Tile2i origin,
            Tile2i predecessorPosition,
            out int connectedA,
            out int connectedB,
            out int handoffA,
            out int handoffB,
            out int handoffEdge)
        {
            int dx = predecessorPosition.X - origin.X;
            int dy = predecessorPosition.Y - origin.Y;
            if (dx < 0 && predecessorPosition.X <= origin.X - 4
                && predecessorPosition.Y >= origin.Y && predecessorPosition.Y <= origin.Y + 4)
            {
                connectedA = 0; connectedB = 3; handoffA = 1; handoffB = 2; handoffEdge = 1;
                return true;
            }
            if (dx > 0 && predecessorPosition.X >= origin.X + 4
                && predecessorPosition.Y >= origin.Y && predecessorPosition.Y <= origin.Y + 4)
            {
                connectedA = 1; connectedB = 2; handoffA = 0; handoffB = 3; handoffEdge = 0;
                return true;
            }
            if (dy < 0 && predecessorPosition.Y <= origin.Y - 4
                && predecessorPosition.X >= origin.X && predecessorPosition.X <= origin.X + 4)
            {
                connectedA = 0; connectedB = 1; handoffA = 3; handoffB = 2; handoffEdge = 3;
                return true;
            }
            if (dy > 0 && predecessorPosition.Y >= origin.Y + 4
                && predecessorPosition.X >= origin.X && predecessorPosition.X <= origin.X + 4)
            {
                connectedA = 3; connectedB = 2; handoffA = 0; handoffB = 1; handoffEdge = 2;
                return true;
            }

            connectedA = connectedB = handoffA = handoffB = handoffEdge = -1;
            return false;
        }

        private static bool IsTileOnHandoffEdge(int x, int y, int handoffEdge)
        {
            return handoffEdge == 0 ? x == 0
                : handoffEdge == 1 ? x == 4
                : handoffEdge == 2 ? y == 0
                : handoffEdge == 3 && y == 4;
        }

        private static bool IsExperimentalAccessGroundOrCleanupNode(
            HashSet<Tile2i> groundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByOrigin,
            Tile2i tile)
        {
            if (groundNodes.Contains(tile))
                return true;
            return propCleanupByOrigin.TryGetValue(TerrainDesignation.GetOrigin(tile), out AccessPropCleanupInfo info)
                && info.IsEligible
                && (info.Samples.Count == 0 || info.Samples.Any(sample => sample.Tile == tile));
        }

        private static EvaluatedAccessCandidate? EvaluateExperimentalAccessCandidate(
            AccessSearchResult result,
            AccessDesignationPlan? plan,
            Tile2i towerPosition,
            TerrainManager terrMgr)
        {
            if (!result.Success || plan == null || !plan.IsValid || plan.Designations.Count == 0)
                return null;

            var rampTiles = new List<RampTilePlan>(plan.Designations.Count);
            foreach (AccessPlannedDesignation item in plan.Designations)
            {
                if (((item.Profile.Nw2 | item.Profile.Ne2 | item.Profile.Se2 | item.Profile.Sw2) & 1) != 0)
                    return null;
                rampTiles.Add(new RampTilePlan(item.Origin,
                    item.Profile.Nw2 / 2,
                    item.Profile.Ne2 / 2,
                    item.Profile.Se2 / 2,
                    item.Profile.Sw2 / 2));
            }

            Tile2i terminal = plan.GroundNodeCount > 0
                ? plan.HandoffGround
                : result.Path[result.Path.Count - 1].Position;
            int dx = towerPosition.X - terminal.X;
            int dy = towerPosition.Y - terminal.Y;
            return new EvaluatedAccessCandidate(
                terminal,
                isValid: true,
                isReachableNow: true,
                mouthDistance: dx * dx + dy * dy,
                materialMoved: CalculateUselessMaterialMoved(rampTiles, terrMgr),
                designationCount: plan.Designations.Count,
                stableOrder: int.MaxValue,
                sourceCandidate: new ExperimentalAccessCandidate(result, plan));
        }

        private static bool TryPlaceExperimentalAccessCandidate(
            AccessSearchSnapshot snapshot,
            TerrainDesignationProto rampProto,
            ExperimentalAccessCandidate candidate,
            IAreaManagingTower tower,
            List<Tile2i>? placedRampOrigins,
            HashSet<Tile2i>? reservedRampTiles,
            out Tile2i topRowTile,
            out string failureReason)
        {
            topRowTile = default;
            failureReason = string.Empty;
            ClearLastExperimentalCleanupMaterialization();
            if (s_desigManager == null)
            {
                failureReason = "DesignationManagerUnavailable";
                return false;
            }

            AccessDesignationPlan placementPlan = AccessPathMaterializer.Materialize(snapshot, candidate.SearchResult);
            if (!placementPlan.IsValid || placementPlan.Designations.Count == 0)
            {
                failureReason = placementPlan.IsValid ? "EmptyPlan" : placementPlan.FailureReason;
                return false;
            }

            var placedCleanupDesignations = new List<PlacedExperimentalDesignation>();
            if (!TryPlaceDenseDebrisCleanupDesignations(
                    placementPlan.CleanupOrigins,
                    tower,
                    reservedRampTiles,
                    placedCleanupDesignations,
                    out failureReason))
            {
                RollBackExperimentalDesignations(placedCleanupDesignations, tower, reservedRampTiles);
                return false;
            }

            var selectedCleanupTrees = new List<TreeId>();
            if (!TryMaterializeTreeCleanup(placementPlan.CleanupOrigins, selectedCleanupTrees, out failureReason))
            {
                RollBackTreeCleanupSelections(selectedCleanupTrees);
                RollBackExperimentalDesignations(placedCleanupDesignations, tower, reservedRampTiles);
                return false;
            }

            Tile2i terminalOrigin = default;
            bool hasGeneratedTerminal = TryGetGeneratedTerminal(
                candidate.SearchResult, out terminalOrigin);
            Dictionary<Tile2i, AccessHandoffOperation> generatedHandoffOperations =
                BuildGeneratedHandoffOperations(candidate.SearchResult);
            var placedNow = new List<PlacedExperimentalDesignation>(placementPlan.Designations.Count);
            int placementIndex = -1;
            foreach (AccessPlannedDesignation item in placementPlan.Designations)
            {
                placementIndex++;
                if (((item.Profile.Nw2 | item.Profile.Ne2 | item.Profile.Se2 | item.Profile.Sw2) & 1) != 0)
                {
                    failureReason = "HalfLevelCorner";
                    RollBackTreeCleanupSelections(selectedCleanupTrees);
                    RollBackExperimentalDesignations(placedCleanupDesignations, tower, reservedRampTiles);
                    RollBackExperimentalDesignations(placedNow, tower, reservedRampTiles);
                    return false;
                }
                Option<TerrainDesignation> existingDesignation =
                    s_desigManager.GetDesignationAt(item.Origin);
                if (existingDesignation.HasValue)
                {
                    failureReason = "DesignationAppeared";
                    TerrainDesignation existing = existingDesignation.Value;
                    LogExperimentalAccessDebug(
                        $"[ATD Experimental Access Placement Collision] " +
                        $"index={placementIndex}/{placementPlan.Designations.Count} " +
                        $"origin=({item.Origin.X},{item.Origin.Y}) mode={item.Mode} " +
                        $"profile=[{item.Profile.Nw2},{item.Profile.Ne2},{item.Profile.Se2},{item.Profile.Sw2}] " +
                        $"existingProto={existing.Prototype.Id.Value} fulfilled={existing.IsFulfilled} " +
                        $"snapshotFixed={snapshot.TryGetFixedProfile(item.Origin, out _)} " +
                        $"registeredAccessway={IsRegisteredGeneratedAccesswayOrigin(tower, item.Origin)} " +
                        $"reserved={reservedRampTiles?.Contains(item.Origin) == true} " +
                        $"cleanupOrigin={placementPlan.CleanupOrigins.Any(cleanup => cleanup.Origin == item.Origin)}");
                    RollBackTreeCleanupSelections(selectedCleanupTrees);
                    RollBackExperimentalDesignations(placedCleanupDesignations, tower, reservedRampTiles);
                    RollBackExperimentalDesignations(placedNow, tower, reservedRampTiles);
                    return false;
                }

                var data = new DesignationData(item.Origin,
                    new HeightTilesI(item.Profile.Nw2 / 2),
                    new HeightTilesI(item.Profile.Ne2 / 2),
                    new HeightTilesI(item.Profile.Se2 / 2),
                    new HeightTilesI(item.Profile.Sw2 / 2));
                TerrainDesignationProto itemProto = rampProto;
                AccessHandoffOperation itemHandoffOperation = AccessHandoffOperation.None;
                if (generatedHandoffOperations.TryGetValue(item.Origin, out AccessHandoffOperation mappedOperation))
                    itemHandoffOperation = mappedOperation;
                else if (hasGeneratedTerminal
                    && item.Origin == terminalOrigin)
                    itemHandoffOperation = placementPlan.HandoffOperation;
                if (itemHandoffOperation != AccessHandoffOperation.None)
                {
                    TerrainDesignationProto? terminalProto = itemHandoffOperation == AccessHandoffOperation.Mining
                        ? s_miningProto
                        : itemHandoffOperation == AccessHandoffOperation.Dumping
                            ? s_dumpingProto
                            : itemHandoffOperation == AccessHandoffOperation.Leveling
                                ? s_levelingProto
                                : null;
                    if (terminalProto == null)
                    {
                        failureReason = "MissingHandoffOperationProto";
                        RollBackTreeCleanupSelections(selectedCleanupTrees);
                        RollBackExperimentalDesignations(placedCleanupDesignations, tower, reservedRampTiles);
                        RollBackExperimentalDesignations(placedNow, tower, reservedRampTiles);
                        return false;
                    }
                    itemProto = terminalProto;
                }
                if (!s_desigManager.AddOrReplaceDesignation(itemProto, data))
                {
                    failureReason = "PlacementFailed";
                    RollBackTreeCleanupSelections(selectedCleanupTrees);
                    RollBackExperimentalDesignations(placedCleanupDesignations, tower, reservedRampTiles);
                    RollBackExperimentalDesignations(placedNow, tower, reservedRampTiles);
                    return false;
                }

                RegisterGeneratedDesignationOrigin(tower, item.Origin);
                placedNow.Add(new PlacedExperimentalDesignation(item.Origin, itemProto));
                s_designationOriginsInArea.Add(item.Origin);
                reservedRampTiles?.Add(item.Origin);
                if (itemProto != rampProto)
                    LogExperimentalAccessDebug(
                        $"[ATD Experimental Access Terminal] origin={item.Origin} proto={itemProto.Id.Value}");
            }

            placedRampOrigins?.AddRange(placedNow.Select(item => item.Origin));
            topRowTile = placementPlan.Designations[placementPlan.Designations.Count - 1].Origin;
            LastExperimentalAccessPlan = placementPlan;
            s_lastExperimentalCleanupDesignations.AddRange(placedCleanupDesignations);
            s_lastExperimentalCleanupTreeSelections.AddRange(selectedCleanupTrees);
            return true;
        }

        private static bool TryPlaceDenseDebrisCleanupDesignations(
            IReadOnlyList<AccessPropCleanupInfo> cleanupOrigins,
            IAreaManagingTower tower,
            HashSet<Tile2i>? reservedRampTiles,
            List<PlacedExperimentalDesignation> placedCleanupDesignations,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (cleanupOrigins.Count == 0)
                return true;

            int denseCleanupOrigins = cleanupOrigins.Count(info => info.HasDenseDebrisCleanup);
            if (denseCleanupOrigins == 0)
                return true;
            if (s_desigManager == null)
            {
                failureReason = "DesignationManagerUnavailable";
                return false;
            }
            if (s_miningProto == null)
            {
                failureReason = "MiningProtoUnavailable";
                return false;
            }

            var cleanupByObjectKey = new Dictionary<string, AccessPropCleanupInfo>(StringComparer.Ordinal);
            foreach (AccessPropCleanupInfo cleanup in cleanupOrigins)
            {
                if (!cleanup.HasDenseDebrisCleanup)
                    continue;
                foreach (AccessPropSample sample in cleanup.Samples)
                    if (sample.IsDenseDebris && !cleanupByObjectKey.ContainsKey(sample.CleanupObjectKey))
                        cleanupByObjectKey.Add(sample.CleanupObjectKey, cleanup);
            }

            TerrainManager terrMgr = s_desigManager.TerrainManager;
            var placedCleanupOrigins = new HashSet<Tile2i>();
            foreach (KeyValuePair<string, AccessPropCleanupInfo> pair in cleanupByObjectKey)
            {
                if (!TrySelectDenseDebrisCleanupOrigin(
                        tower, terrMgr, pair.Value, pair.Key, out Tile2i origin))
                {
                    failureReason = "DenseDebrisCleanupOriginUnavailable";
                    return false;
                }
                if (!placedCleanupOrigins.Add(origin))
                    continue;
                if (s_desigManager.GetDesignationAt(origin).HasValue)
                {
                    failureReason = "CleanupDesignationAppeared";
                    return false;
                }

                DesignationData data = BuildDenseDebrisCleanupDesignationData(terrMgr, origin);
                if (!s_desigManager.AddOrReplaceDesignation(s_miningProto, data))
                {
                    failureReason = "CleanupPlacementFailed";
                    return false;
                }

                RegisterGeneratedDesignationOrigin(tower, origin);
                placedCleanupDesignations.Add(new PlacedExperimentalDesignation(origin, s_miningProto));
                s_designationOriginsInArea.Add(origin);
                reservedRampTiles?.Add(origin);
            }

            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Cleanup] dense debris materialization origins={denseCleanupOrigins} " +
                $"props={cleanupByObjectKey.Count} designations={placedCleanupDesignations.Count}");
            return true;
        }

        private static bool TrySelectDenseDebrisCleanupOrigin(
            IAreaManagingTower tower,
            TerrainManager terrMgr,
            AccessPropCleanupInfo preferredCleanup,
            string cleanupObjectKey,
            out Tile2i origin)
        {
            origin = preferredCleanup.Origin;
            if (!TryParsePropCleanupKey(cleanupObjectKey, out TerrainPropId propId)
                || s_terrainPropsManager == null
                || !s_terrainPropsManager.TerrainProps.TryGetValue(propId, out TerrainPropData prop))
            {
                return false;
            }

            Tile2i propOrigin = TerrainDesignation.GetOrigin(propId.Position);
            if (IsOriginInsideTower(tower, propOrigin)
                && IsDesignatableTileFullyInsideArea(tower.Area, propOrigin))
            {
                origin = propOrigin;
                return true;
            }

            var occupiedTiles = new Lyst<Tile2i>();
            prop.CalculateOccupiedTiles(terrMgr, occupiedTiles);
            Tile2i fallbackOrigin = default;
            bool hasFallback = false;
            for (int i = 0; i < occupiedTiles.Count; i++)
            {
                Tile2i candidate = TerrainDesignation.GetOrigin(occupiedTiles[i]);
                if (!IsOriginInsideTower(tower, candidate)
                    || !IsDesignatableTileFullyInsideArea(tower.Area, candidate))
                    continue;
                if (candidate == preferredCleanup.Origin)
                {
                    origin = candidate;
                    return true;
                }
                if (!hasFallback)
                {
                    fallbackOrigin = candidate;
                    hasFallback = true;
                }
            }

            if (hasFallback)
            {
                origin = fallbackOrigin;
                return true;
            }
            return false;
        }

        private static DesignationData BuildDenseDebrisCleanupDesignationData(
            TerrainManager terrMgr,
            Tile2i origin)
        {
            int hNW = GetSurfaceHeight(terrMgr, origin) + 1;
            int hNE = GetSurfaceHeight(terrMgr, origin.AddX(4)) + 1;
            int hSE = GetSurfaceHeight(terrMgr, origin.AddXy(4)) + 1;
            int hSW = GetSurfaceHeight(terrMgr, origin.AddY(4)) + 1;
            return new DesignationData(origin,
                new HeightTilesI(hNW),
                new HeightTilesI(hNE),
                new HeightTilesI(hSE),
                new HeightTilesI(hSW));
        }

        private static bool TryMaterializeTreeCleanup(
            IReadOnlyList<AccessPropCleanupInfo> cleanupOrigins,
            List<TreeId> selectedTrees,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (cleanupOrigins.Count == 0)
                return true;

            int treeCleanupOrigins = cleanupOrigins.Count(info => info.HasTreeCleanup);
            if (treeCleanupOrigins == 0)
                return true;
            if (s_treesManager == null)
            {
                failureReason = "TreesManagerUnavailable";
                return false;
            }

            var treeCleanupKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (AccessPropCleanupInfo cleanup in cleanupOrigins)
            {
                if (!cleanup.HasTreeCleanup)
                    continue;
                foreach (AccessPropSample sample in cleanup.Samples)
                    if (sample.IsTree)
                        treeCleanupKeys.Add(sample.CleanupObjectKey);
            }

            foreach (KeyValuePair<TreeId, TreeData> pair in s_treesManager.Trees)
            {
                TreeId treeId = pair.Key;
                if (!treeCleanupKeys.Contains(BuildTreeCleanupKey(treeId)))
                    continue;
                if (s_treesManager.IsTreeSelected(treeId))
                    continue;

                s_treesManager.AddToHarvest(treeId);
                selectedTrees.Add(treeId);
            }

            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Cleanup] tree materialization origins={treeCleanupOrigins} " +
                $"newHarvestSelections={selectedTrees.Count}");
            return true;
        }

        private static void RollBackTreeCleanupSelections(IReadOnlyList<TreeId> selectedTrees)
        {
            if (s_treesManager == null || selectedTrees.Count == 0)
                return;

            for (int i = selectedTrees.Count - 1; i >= 0; i--)
            {
                TreeId treeId = selectedTrees[i];
                if (s_treesManager.IsTreeSelected(treeId))
                    s_treesManager.RemoveFromHarvest(treeId);
            }
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Cleanup] rolled back tree harvest selections={selectedTrees.Count}");
        }

        private static void RollBackLastExperimentalCleanupMaterialization(
            IAreaManagingTower tower,
            HashSet<Tile2i>? reservedRampTiles)
        {
            RollBackTreeCleanupSelections(s_lastExperimentalCleanupTreeSelections);
            RollBackExperimentalDesignations(s_lastExperimentalCleanupDesignations, tower, reservedRampTiles);
            ClearLastExperimentalCleanupMaterialization();
        }

        private static void ClearLastExperimentalCleanupMaterialization()
        {
            s_lastExperimentalCleanupTreeSelections.Clear();
            s_lastExperimentalCleanupDesignations.Clear();
        }

        private static void RollBackExperimentalDesignations(
            IReadOnlyList<PlacedExperimentalDesignation> designations,
            IAreaManagingTower tower,
            HashSet<Tile2i>? reservedRampTiles)
        {
            if (s_desigManager == null) return;
            foreach (PlacedExperimentalDesignation placed in designations)
            {
                Option<TerrainDesignation> designation = s_desigManager.GetDesignationAt(placed.Origin);
                if (designation.HasValue && designation.Value.Prototype == placed.Proto)
                    s_desigManager.RemoveDesignation(placed.Origin);
                s_designationOriginsInArea.Remove(placed.Origin);
                UnregisterGeneratedDesignationOrigin(tower, placed.Origin);
                reservedRampTiles?.Remove(placed.Origin);
            }
        }

        private static void RollBackExperimentalDesignations(
            IReadOnlyList<Tile2i> origins,
            IAreaManagingTower tower,
            TerrainDesignationProto rampProto,
            HashSet<Tile2i>? reservedRampTiles)
        {
            if (s_desigManager == null) return;
            foreach (Tile2i origin in origins)
            {
                Option<TerrainDesignation> designation = s_desigManager.GetDesignationAt(origin);
                if (designation.HasValue && IsAccesswayDesignationProto(designation.Value.Prototype, rampProto))
                    s_desigManager.RemoveDesignation(origin);
                s_designationOriginsInArea.Remove(origin);
                UnregisterGeneratedDesignationOrigin(tower, origin);
                reservedRampTiles?.Remove(origin);
            }
        }

        private static bool TryGetGeneratedTerminal(
            AccessSearchResult result,
            out Tile2i terminalOrigin)
        {
            terminalOrigin = default;
            if (result.Path.Count < 2) return false;

            // The path may continue through additional G nodes after the first handoff,
            // so walk backward from the end and use the last generated V node before
            // the terminal ground/existing suffix as the handoff target.
            for (int index = result.Path.Count - 1; index >= 0; index--)
            {
                AccessSearchNode node = result.Path[index];
                if (node.IsGround || node.Mode == AccessSearchMode.Existing)
                    continue;

                terminalOrigin = node.Position;
                return true;
            }

            return false;
        }

        private static Dictionary<Tile2i, AccessHandoffOperation> BuildGeneratedHandoffOperations(
            AccessSearchResult result)
        {
            var operations = new Dictionary<Tile2i, AccessHandoffOperation>();
            AccessSearchNode? previousGenerated = null;
            foreach (AccessSearchNode node in result.Path)
            {
                if (node.IsGround)
                {
                    if (previousGenerated.HasValue
                        && node.HandoffOperation != AccessHandoffOperation.None)
                        operations[previousGenerated.Value.Position] = node.HandoffOperation;
                    previousGenerated = null;
                    continue;
                }

                if (node.Mode != AccessSearchMode.Existing
                    && node.HandoffOperation != AccessHandoffOperation.None)
                {
                    operations[node.Position] = node.HandoffOperation;
                }
                previousGenerated = node.Mode == AccessSearchMode.Existing
                    ? (AccessSearchNode?)null
                    : node;
            }
            return operations;
        }

        private static string FormatExperimentalPath(AccessSearchResult result)
        {
            var parts = new List<string>(result.Path.Count + 1)
            {
                $"S@({result.StartOrigin.X},{result.StartOrigin.Y})"
            };
            foreach (AccessSearchNode node in result.Path)
            {
                string height = (node.Height2 / 2f).ToString("0.#", CultureInfo.InvariantCulture);
                string handoff = node.HandoffOperation != AccessHandoffOperation.None
                    ? $",op={node.HandoffOperation}"
                    : string.Empty;
                parts.Add($"{FormatSearchMode(node.Mode)}@({node.Position.X},{node.Position.Y},h={height}{handoff})");
            }
            return string.Join(" -> ", parts);
        }

        private static void LogExperimentalCleanupRouteDiagnostics(
            AccessSearchSnapshot snapshot,
            AccessSearchResult result,
            AccessDesignationPlan plan,
            int clusterId)
        {
            var cleanupOrigins = snapshot.PropCleanupOrigins
                .Where(info => info.IsEligible)
                .OrderBy(info => info.Origin.X)
                .ThenBy(info => info.Origin.Y)
                .Take(16)
                .ToList();
            if (cleanupOrigins.Count == 0)
                return;

            var details = new List<string>(cleanupOrigins.Count);
            foreach (AccessPropCleanupInfo info in cleanupOrigins)
            {
                bool exactGround = result.Path.Any(node =>
                    node.IsGround && TerrainDesignation.GetOrigin(node.Position) == info.Origin);
                bool exactGenerated = plan.Designations.Any(item => item.Origin == info.Origin);
                bool sampleCoveredByGenerated = info.Samples.Any(sample =>
                    plan.Designations.Any(item => IsTileInDesignationOrigin(sample.Tile, item.Origin)));

                string samples = info.Samples.Count == 0
                    ? "none"
                    : string.Join(",", info.Samples.Select(sample =>
                    {
                        string kind = sample.IsTree ? "T" : sample.IsDenseDebris ? "D" : "P";
                        return $"{kind}@({sample.Tile.X},{sample.Tile.Y})";
                    }));
                string nearPath = string.Join(",", result.Path
                    .Where(node => IsNearOrigin(node.Position, info.Origin, 8))
                    .Take(8)
                    .Select(node => $"{FormatSearchMode(node.Mode)}@({node.Position.X},{node.Position.Y})"));
                string nearPlan = string.Join(",", plan.Designations
                    .Where(item => IsNearOrigin(item.Origin, info.Origin, 8))
                    .Take(8)
                    .Select(item => $"{FormatSearchMode(item.Mode)}@({item.Origin.X},{item.Origin.Y})"));

                details.Add(
                    $"origin=({info.Origin.X},{info.Origin.Y}) classes={info.Classes} samples=[{samples}] " +
                    $"exactGround={exactGround} exactV={exactGenerated} sampleCoveredByV={sampleCoveredByGenerated} " +
                    $"nearPath=[{nearPath}] nearPlan=[{nearPlan}]");
            }

            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Cleanup Route] cluster={clusterId} {string.Join("; ", details)}");
        }

        private static bool IsTileInDesignationOrigin(Tile2i tile, Tile2i origin)
            => tile.X >= origin.X && tile.X <= origin.X + 3
                && tile.Y >= origin.Y && tile.Y <= origin.Y + 3;

        private static bool IsNearOrigin(Tile2i position, Tile2i origin, int distance)
            => Math.Abs(position.X - origin.X) <= distance
                && Math.Abs(position.Y - origin.Y) <= distance;

        private static string FormatSearchMode(AccessSearchMode mode)
        {
            switch (mode)
            {
                case AccessSearchMode.Ground: return "G";
                case AccessSearchMode.Flat: return "F";
                case AccessSearchMode.XPositive: return "X+";
                case AccessSearchMode.XNegative: return "X-";
                case AccessSearchMode.YPositive: return "Y+";
                case AccessSearchMode.YNegative: return "Y-";
                case AccessSearchMode.Existing: return "Existing";
                default: return mode.ToString();
            }
        }

        private static string FormatExperimentalPlan(AccessDesignationPlan plan)
        {
            return string.Join(" -> ", plan.Designations.Select(item =>
                $"{FormatSearchMode(item.Mode)}@({item.Origin.X},{item.Origin.Y})" +
                $"[{item.Profile.Nw2 / 2},{item.Profile.Ne2 / 2},{item.Profile.Se2 / 2},{item.Profile.Sw2 / 2}]"));
        }

        private static AccessHeightProfile ProfileFromDesignation(TerrainDesignation designation)
        {
            DesignationData data = designation.Data;
            return new AccessHeightProfile(
                data.OriginTargetHeight.Value * 2,
                data.PlusXTargetHeight.Value * 2,
                data.PlusXyTargetHeight.Value * 2,
                data.PlusYTargetHeight.Value * 2);
        }

        private static ProjectedDesignationDisturbance BuildProjectedDesignationDisturbedTiles(
            IReadOnlyDictionary<Tile2i, TerrainDesignation> designations,
            IReadOnlyDictionary<Tile2i, float> terrainHeights,
            IReadOnlyDictionary<Tile2i, AccessTerrainColumn> terrainColumns,
            ISet<Tile2i> oceanTiles,
            Tile2i physicalTerrainMin,
            Tile2i physicalTerrainMax,
            float dumpingMaterialSlope,
            float fallbackMiningSlope,
            int vehicleDisturbanceRadius)
        {
            var result = new ProjectedDesignationDisturbance();
            foreach (KeyValuePair<Tile2i, TerrainDesignation> pair in designations)
            {
                Tile2i origin = pair.Key;
                TerrainDesignation designation = pair.Value;
                AccessHeightProfile profile = ProfileFromDesignation(designation);
                AccessHandoffOperation workOperation =
                    s_miningProto != null && designation.Prototype == s_miningProto
                        ? AccessHandoffOperation.Mining
                        : s_dumpingProto != null && designation.Prototype == s_dumpingProto
                            ? AccessHandoffOperation.Dumping
                            : AccessHandoffOperation.Leveling;

                bool westExposed = IsBoundaryExposed(new Tile2i(-4, 0));
                bool eastExposed = IsBoundaryExposed(new Tile2i(4, 0));
                bool northExposed = IsBoundaryExposed(new Tile2i(0, -4));
                bool southExposed = IsBoundaryExposed(new Tile2i(0, 4));

                TraceBoundary(westExposed,
                    origin, profile.Nw2 / 2f,
                    origin + new RelTile2i(0, 4), profile.Sw2 / 2f,
                    new Tile2i(-1, 0));
                TraceBoundary(eastExposed,
                    origin + new RelTile2i(4, 0), profile.Ne2 / 2f,
                    origin + new RelTile2i(4, 4), profile.Se2 / 2f,
                    new Tile2i(1, 0));
                TraceBoundary(northExposed,
                    origin, profile.Nw2 / 2f,
                    origin + new RelTile2i(4, 0), profile.Ne2 / 2f,
                    new Tile2i(0, -1));
                TraceBoundary(southExposed,
                    origin + new RelTile2i(0, 4), profile.Sw2 / 2f,
                    origin + new RelTile2i(4, 4), profile.Se2 / 2f,
                    new Tile2i(0, 1));

                if (westExposed && northExposed)
                    TraceOutsideCorner(origin, profile.Nw2 / 2f, -1, -1);
                if (eastExposed && northExposed)
                    TraceOutsideCorner(
                        origin + new RelTile2i(4, 0), profile.Ne2 / 2f, 1, -1);
                if (eastExposed && southExposed)
                    TraceOutsideCorner(
                        origin + new RelTile2i(4, 4), profile.Se2 / 2f, 1, 1);
                if (westExposed && southExposed)
                    TraceOutsideCorner(
                        origin + new RelTile2i(0, 4), profile.Sw2 / 2f, -1, 1);

                bool IsBoundaryExposed(Tile2i neighborOffset)
                    => !designations.ContainsKey(new Tile2i(
                        origin.X + neighborOffset.X,
                        origin.Y + neighborOffset.Y));

                void TraceBoundary(
                    bool isExposed,
                    Tile2i firstCorner,
                    float firstHeight,
                    Tile2i secondCorner,
                    float secondHeight,
                    Tile2i direction)
                {
                    if (!isExposed)
                        return;
                    TraceCorner(firstCorner, firstHeight, direction);
                    TraceCorner(secondCorner, secondHeight, direction);
                }

                void TraceCorner(Tile2i corner, float plannedHeight, Tile2i direction)
                {
                    if (!TryResolveCornerRay(
                        corner, plannedHeight,
                        out AccessSideRayOperation operation,
                        out float materialSlope))
                        return;

                    int postTerminationSafetyMargin =
                        AutoTerrainDesignationsMod.AccessProjectedRayEndBuffer;
                    for (int distance = 1;
                        distance <= AutoTerrainDesignationsMod.AccessProjectedRayMaxDistance;
                        distance++)
                    {
                        Tile2i tile = new Tile2i(
                            corner.X + direction.X * distance,
                            corner.Y + direction.Y * distance);
                        if (tile.X < physicalTerrainMin.X || tile.X > physicalTerrainMax.X
                            || tile.Y < physicalTerrainMin.Y || tile.Y > physicalTerrainMax.Y
                            || !terrainHeights.TryGetValue(tile, out float sampledHeight))
                        {
                            MarkThrough(distance - 1);
                            return;
                        }
                        float rayHeight = operation == AccessSideRayOperation.Fill
                            ? plannedHeight - distance * materialSlope
                            : plannedHeight + distance * materialSlope;
                        float gap = operation == AccessSideRayOperation.Fill
                            ? rayHeight - sampledHeight
                            : sampledHeight - rayHeight;
                        if (gap <= 0f)
                        {
                            MarkThrough(distance + postTerminationSafetyMargin);
                            return;
                        }
                    }
                    // The projected slope did not resolve inside the normal ray limit.
                    MarkThrough(
                        AutoTerrainDesignationsMod.AccessProjectedRayMaxDistance
                        + postTerminationSafetyMargin);

                    void MarkThrough(int maxDistance)
                    {
                        maxDistance = Math.Min(
                            maxDistance,
                            AutoTerrainDesignationsMod.AccessProjectedRayMaxDistance
                                + AutoTerrainDesignationsMod.AccessProjectedRayEndBuffer);
                        for (int distance = 1; distance <= maxDistance; distance++)
                        {
                            Tile2i disturbed = new Tile2i(
                                corner.X + direction.X * distance,
                                corner.Y + direction.Y * distance);
                            float projectedHeight = operation == AccessSideRayOperation.Fill
                                ? plannedHeight - distance * materialSlope
                                : plannedHeight + distance * materialSlope;
                            AddProjected(operation, disturbed, projectedHeight);
                        }
                    }
                }

                void TraceOutsideCorner(
                    Tile2i corner,
                    float plannedHeight,
                    int outwardX,
                    int outwardY)
                {
                    if (!TryResolveCornerRay(
                        corner, plannedHeight,
                        out AccessSideRayOperation operation,
                        out float materialSlope))
                        return;

                    for (int dy = 1; dy <= AutoTerrainDesignationsMod.AccessProjectedRayMaxDistance; dy++)
                    {
                        for (int dx = 1; dx <= AutoTerrainDesignationsMod.AccessProjectedRayMaxDistance; dx++)
                        {
                            int slopeDistance = Math.Max(dx, dy);
                            Tile2i tile = new Tile2i(
                                corner.X + outwardX * dx,
                                corner.Y + outwardY * dy);
                            if (!terrainHeights.TryGetValue(tile, out float sampledHeight))
                                continue;
                            float projectedHeight = operation == AccessSideRayOperation.Fill
                                ? plannedHeight - slopeDistance * materialSlope
                                : plannedHeight + slopeDistance * materialSlope;
                            float gap = operation == AccessSideRayOperation.Fill
                                ? projectedHeight - sampledHeight
                                : sampledHeight - projectedHeight;
                            if (gap > 0f)
                                AddProjected(operation, tile, projectedHeight);
                        }
                    }
                }

                bool TryResolveCornerRay(
                    Tile2i corner,
                    float plannedHeight,
                    out AccessSideRayOperation operation,
                    out float materialSlope)
                {
                    operation = AccessSideRayOperation.None;
                    materialSlope = dumpingMaterialSlope;
                    if (!terrainHeights.TryGetValue(corner, out float terrainHeight))
                        return false;
                    const float epsilon = 0.0001f;
                    operation = plannedHeight > terrainHeight + epsilon
                        ? AccessSideRayOperation.Fill
                        : plannedHeight < terrainHeight - epsilon
                            ? AccessSideRayOperation.Cut
                            : AccessSideRayOperation.None;
                    if ((operation == AccessSideRayOperation.Fill
                            && workOperation == AccessHandoffOperation.Mining)
                        || (operation == AccessSideRayOperation.Cut
                            && workOperation == AccessHandoffOperation.Dumping)
                        || operation == AccessSideRayOperation.None)
                        return false;
                    if (operation == AccessSideRayOperation.Cut
                        && (!terrainColumns.TryGetValue(corner, out AccessTerrainColumn column)
                            || !column.TryGetNormalSlopeAt(
                                plannedHeight, out materialSlope, out _)))
                        materialSlope = fallbackMiningSlope;
                    materialSlope *= AutoTerrainDesignationsMod.AccessProjectedRaySlopeFactor;
                    return materialSlope > 0f;
                }

                void AddProjected(
                    AccessSideRayOperation operation, Tile2i tile, float projectedHeight)
                {
                    result.AddHeight(operation, tile, projectedHeight);
                    for (int dy = -vehicleDisturbanceRadius; dy <= vehicleDisturbanceRadius; dy++)
                    {
                        for (int dx = -vehicleDisturbanceRadius; dx <= vehicleDisturbanceRadius; dx++)
                        {
                            Tile2i blocked = new Tile2i(tile.X + dx, tile.Y + dy);
                            if (blocked.X >= physicalTerrainMin.X
                                && blocked.X <= physicalTerrainMax.X
                                && blocked.Y >= physicalTerrainMin.Y
                                && blocked.Y <= physicalTerrainMax.Y)
                                result.Add(operation, blocked);
                        }
                    }
                }
            }
            return result;
        }

        private static List<AccessDurabilityCorner> BuildDurabilityCorners(
            Dictionary<Tile2i, AccessHeightProfile> profiles,
            IReadOnlyDictionary<Tile2i, HashSet<int>> buildingFixedHeights2ByTile,
            IReadOnlyDictionary<Tile2i, TerrainDesignation> designations,
            IReadOnlyDictionary<Tile2i, float> terrainHeights,
            IReadOnlyDictionary<Tile2i, AccessTerrainColumn> terrainColumns,
            float dumpingMaterialSlope,
            float fallbackMiningSlope,
            float fallbackRunPerHeight)
        {
            var designationHeightsByPosition = new Dictionary<Tile2i, HashSet<int>>();
            var runByPositionAndHeight = new Dictionary<(Tile2i Position, int Height2), float>();
            foreach (KeyValuePair<Tile2i, AccessHeightProfile> pair in profiles)
            {
                pair.Value.AddWorldCorners(pair.Key, (position, height2) =>
                {
                    if (!designationHeightsByPosition.TryGetValue(position, out HashSet<int> heights))
                    {
                        heights = new HashSet<int>();
                        designationHeightsByPosition[position] = heights;
                    }
                    heights.Add(height2);
                    float run = GetMaterialRunPerHeight(pair.Key, position, height2);
                    var key = (position, height2);
                    if (!runByPositionAndHeight.TryGetValue(key, out float existingRun)
                        || run > existingRun)
                        runByPositionAndHeight[key] = run;
                });
            }

            var heightsByPosition = new Dictionary<Tile2i, HashSet<int>>();
            foreach (KeyValuePair<Tile2i, HashSet<int>> pair in designationHeightsByPosition)
            {
                // Four surrounding compatible origins make this a strictly interior
                // corner. Their stable profiles contain its exclusion envelope.
                if (pair.Value.Count == 1 && IsStrictlyInteriorDesignationCorner(pair.Key, profiles)) continue;
                heightsByPosition[pair.Key] = new HashSet<int>(pair.Value);
            }

            foreach (KeyValuePair<Tile2i, HashSet<int>> pair in buildingFixedHeights2ByTile)
            {
                if (!heightsByPosition.TryGetValue(pair.Key, out HashSet<int> heights))
                {
                    heights = new HashSet<int>();
                    heightsByPosition[pair.Key] = heights;
                }
                foreach (int height2 in pair.Value)
                {
                    heights.Add(height2);
                    var key = (pair.Key, height2);
                    if (!runByPositionAndHeight.TryGetValue(key, out float existingRun)
                        || fallbackRunPerHeight > existingRun)
                        runByPositionAndHeight[key] = fallbackRunPerHeight;
                }
            }

            return heightsByPosition
                .SelectMany(pair => pair.Value.Select(height2 => new AccessDurabilityCorner(
                    pair.Key,
                    height2,
                    runByPositionAndHeight.TryGetValue((pair.Key, height2), out float run)
                        ? run
                        : fallbackRunPerHeight)))
                .ToList();

            float GetMaterialRunPerHeight(
                Tile2i designationOrigin,
                Tile2i corner,
                int plannedHeight2)
            {
                if (!designations.TryGetValue(designationOrigin, out TerrainDesignation designation))
                    return fallbackRunPerHeight;
                float plannedHeight = plannedHeight2 / 2f;
                float materialSlope;
                if (s_dumpingProto != null && designation.Prototype == s_dumpingProto)
                {
                    materialSlope = dumpingMaterialSlope;
                }
                else if (s_levelingProto != null && designation.Prototype == s_levelingProto
                    && terrainHeights.TryGetValue(corner, out float terrainHeight)
                    && plannedHeight > terrainHeight + 0.0001f)
                {
                    materialSlope = dumpingMaterialSlope;
                }
                else if (!terrainColumns.TryGetValue(corner, out AccessTerrainColumn column)
                    || !column.TryGetNormalSlopeAt(
                        plannedHeight, out materialSlope, out _))
                {
                    materialSlope = fallbackMiningSlope;
                }
                float effectiveSlope = materialSlope
                    * AutoTerrainDesignationsMod.AccessProjectedRaySlopeFactor;
                return effectiveSlope > 0f
                    ? 1f / effectiveSlope
                    : fallbackRunPerHeight;
            }
        }

        private static bool IsStrictlyInteriorDesignationCorner(Tile2i corner,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> profiles)
        {
            bool hasNw = profiles.TryGetValue(corner + new RelTile2i(-4, -4), out AccessHeightProfile nw);
            bool hasNe = profiles.TryGetValue(corner + new RelTile2i(0, -4), out AccessHeightProfile ne);
            bool hasSw = profiles.TryGetValue(corner + new RelTile2i(-4, 0), out AccessHeightProfile sw);
            bool hasSe = profiles.TryGetValue(corner, out AccessHeightProfile se);

            int count = (hasNw ? 1 : 0) + (hasNe ? 1 : 0) + (hasSw ? 1 : 0) + (hasSe ? 1 : 0);
            if (count < 3) return false;

            if (count == 4)
            {
                return ProfilesShareEdge(nw, ne, new Tile2i(1, 0))
                    && ProfilesShareEdge(nw, sw, new Tile2i(0, 1))
                    && ProfilesShareEdge(ne, se, new Tile2i(0, 1))
                    && ProfilesShareEdge(sw, se, new Tile2i(1, 0));
            }

            if (!hasNw) // NE, SW, SE
            {
                return ProfilesShareEdge(ne, se, new Tile2i(0, 1))
                    && ProfilesShareEdge(sw, se, new Tile2i(1, 0));
            }
            if (!hasNe) // NW, SW, SE
            {
                return ProfilesShareEdge(nw, sw, new Tile2i(0, 1))
                    && ProfilesShareEdge(sw, se, new Tile2i(1, 0));
            }
            if (!hasSw) // NW, NE, SE
            {
                return ProfilesShareEdge(nw, ne, new Tile2i(1, 0))
                    && ProfilesShareEdge(ne, se, new Tile2i(0, 1));
            }
            // !hasSe // NW, NE, SW
            return ProfilesShareEdge(nw, ne, new Tile2i(1, 0))
                && ProfilesShareEdge(nw, sw, new Tile2i(0, 1));
        }

        private static bool ProfilesShareEdge(AccessHeightProfile first,
            AccessHeightProfile second, Tile2i direction)
        {
            first.GetEdge(direction, out int firstA, out int firstB);
            second.GetEdge(new Tile2i(-direction.X, -direction.Y), out int secondA, out int secondB);
            return firstA == secondA && firstB == secondB;
        }

        private static bool IsDurabilityBlocked(Tile2i position, int height2,
            IReadOnlyList<AccessDurabilityCorner> durabilityCorners,
            float landslideRunPerHeight,
            int vehicleClearanceRadius = 0)
        {
            foreach (AccessDurabilityCorner corner in durabilityCorners)
            {
                if (corner.BlocksVehicleFootprint(
                    position, height2, landslideRunPerHeight, vehicleClearanceRadius))
                    return true;
            }
            return false;
        }

        private static bool IsOriginInsideTower(IAreaManagingTower tower, Tile2i origin)
            => tower.Area.ContainsTile(origin)
                && tower.Area.ContainsTile(origin + new RelTile2i(3, 0))
                && tower.Area.ContainsTile(origin + new RelTile2i(0, 3))
                && tower.Area.ContainsTile(origin + new RelTile2i(3, 3));

        private static bool TryBuildTowerReachableGround(
            IAreaManagingTower tower,
            Tile2i boundsMin,
            Tile2i boundsMax,
            HashSet<Tile2i> groundNodes,
            IPathabilityProvider provider,
            VehiclePathFindingParams pathParams,
            out HashSet<Tile2i> reachedGround,
            out Tile2i start)
        {
            reachedGround = new HashSet<Tile2i>();
            Tile2i towerAccessPosition = GetTowerAccessPosition(tower, boundsMin, boundsMax);
            if (!TryFindNearestTowerGroundSeed(tower, groundNodes, provider, pathParams, towerAccessPosition, out start))
                return false;

            int minX = Math.Min(boundsMin.X, towerAccessPosition.X) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int minY = Math.Min(boundsMin.Y, towerAccessPosition.Y) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxX = Math.Max(boundsMax.X, towerAccessPosition.X) + RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxY = Math.Max(boundsMax.Y, towerAccessPosition.Y) + RAMP_ACCESS_SEARCH_MARGIN_TILES;

            var visited = new HashSet<Tile2i> { start };
            var queue = new Queue<Tile2i>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                if (groundNodes.Contains(current)) reachedGround.Add(current);
                foreach (RelTile2i direction in s_experimentalGroundDirections)
                {
                    Tile2i next = current + direction;
                    if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY
                        || visited.Contains(next))
                        continue;

                    bool insideManagedArea = tower.Area.ContainsTile(next);
                    if (insideManagedArea)
                    {
                        if (!groundNodes.Contains(next)) continue;
                    }
                    else if (!provider.IsPathable(next, pathParams.PathabilityQueryMask))
                    {
                        continue;
                    }

                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
            return true;
        }

        private static void LogTowerGroundFrontierDiagnostics(
            Tile2i start,
            HashSet<Tile2i> reachedGround,
            HashSet<Tile2i> groundNodes,
            Dictionary<Tile2i, string> groundExclusionReasons,
            IPathabilityProvider provider,
            VehiclePathFindingParams pathParams)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var samples = new List<string>();
            foreach (Tile2i current in reachedGround)
            {
                foreach (RelTile2i direction in s_experimentalGroundDirections)
                {
                    Tile2i next = current + direction;
                    if (reachedGround.Contains(next)) continue;

                    string reason;
                    if (groundNodes.Contains(next))
                        reason = "GroundNodeNotReached";
                    else if (groundExclusionReasons.TryGetValue(next, out string excluded))
                        reason = excluded.StartsWith("DesignatedOrigin@", StringComparison.Ordinal)
                            ? "DesignatedOrigin" : excluded;
                    else if (!provider.IsPathable(next, pathParams.PathabilityQueryMask))
                        reason = "NotPathableOutsideSnapshot";
                    else
                        reason = "OutsideSnapshotGroundCandidate";

                    counts.TryGetValue(reason, out int count);
                    counts[reason] = count + 1;
                    if (samples.Count < 16)
                        samples.Add($"({next.X},{next.Y}):{reason}");
                }
            }

            string summary = string.Join(",", counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{pair.Value}"));
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Tower Ground Frontier] start={start} reached={reachedGround.Count} " +
                $"frontier=[{summary}] samples=[{string.Join(";", samples)}]");
        }

        private static HashSet<Tile2i> SelectTowerRadialGroundGoals(
            Tile2i towerCenter,
            HashSet<Tile2i> towerReachableGround,
            int maxSteps,
            out string diagnostic)
        {
            var result = new HashSet<Tile2i>();
            var details = new List<string>();
            var directions = new[]
            {
                (Name: "E", Delta: new RelTile2i(1, 0)),
                (Name: "NE", Delta: new RelTile2i(1, 1)),
                (Name: "N", Delta: new RelTile2i(0, 1)),
                (Name: "NW", Delta: new RelTile2i(-1, 1)),
                (Name: "W", Delta: new RelTile2i(-1, 0)),
                (Name: "SW", Delta: new RelTile2i(-1, -1)),
                (Name: "S", Delta: new RelTile2i(0, -1)),
                (Name: "SE", Delta: new RelTile2i(1, -1)),
            };

            foreach (var direction in directions)
            {
                Tile2i? selected = null;
                int selectedStep = 0;
                for (int step = 1; step <= maxSteps; step++)
                {
                    Tile2i candidate = towerCenter + new RelTile2i(
                        direction.Delta.X * step,
                        direction.Delta.Y * step);
                    if (!towerReachableGround.Contains(candidate)) continue;
                    selected = candidate;
                    selectedStep = step;
                    result.Add(candidate);
                    break;
                }

                details.Add(selected.HasValue
                    ? $"{direction.Name}=({selected.Value.X},{selected.Value.Y})@{selectedStep}"
                    : $"{direction.Name}=none");
            }

            diagnostic = "directions=[" + string.Join(";", details) + "]";
            return result;
        }

        private static Tile2i GetTowerAccessPosition(
            IAreaManagingTower tower,
            Tile2i boundsMin,
            Tile2i boundsMax)
        {
            if (tower is MineTower mineTower)
                return mineTower.Prototype.Layout.Transform(
                    mineTower.Prototype.Area.Origin, mineTower.Transform);
            if (tower is ForestryTower forestryTower)
                return forestryTower.Prototype.Layout.Transform(
                    forestryTower.Prototype.Area.Origin, forestryTower.Transform);
            return GetTowerPosition(tower, boundsMin, boundsMax);
        }

        private static bool TryFindNearestTowerGroundSeed(
            IAreaManagingTower tower,
            HashSet<Tile2i> groundNodes,
            IPathabilityProvider provider,
            VehiclePathFindingParams pathParams,
            Tile2i origin,
            out Tile2i seed)
        {
            if (IsTowerGroundSeed(tower, groundNodes, provider, pathParams, origin))
            {
                seed = origin;
                return true;
            }

            for (int radius = 1; radius <= RAMP_ACCESS_SEARCH_MARGIN_TILES; radius++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    if (TryUseTowerGroundSeed(tower, groundNodes, provider, pathParams, origin + new RelTile2i(-radius, y), out seed)
                        || TryUseTowerGroundSeed(tower, groundNodes, provider, pathParams, origin + new RelTile2i(radius, y), out seed))
                        return true;
                }

                for (int x = -radius + 1; x < radius; x++)
                {
                    if (TryUseTowerGroundSeed(tower, groundNodes, provider, pathParams, origin + new RelTile2i(x, -radius), out seed)
                        || TryUseTowerGroundSeed(tower, groundNodes, provider, pathParams, origin + new RelTile2i(x, radius), out seed))
                        return true;
                }
            }

            seed = origin;
            return false;
        }

        private static bool TryUseTowerGroundSeed(
            IAreaManagingTower tower,
            HashSet<Tile2i> groundNodes,
            IPathabilityProvider provider,
            VehiclePathFindingParams pathParams,
            Tile2i candidate,
            out Tile2i seed)
        {
            if (IsTowerGroundSeed(tower, groundNodes, provider, pathParams, candidate))
            {
                seed = candidate;
                return true;
            }

            seed = candidate;
            return false;
        }

        private static bool IsTowerGroundSeed(
            IAreaManagingTower tower,
            HashSet<Tile2i> groundNodes,
            IPathabilityProvider provider,
            VehiclePathFindingParams pathParams,
            Tile2i candidate)
        {
            if (!provider.IsPathable(candidate, pathParams.PathabilityQueryMask))
                return false;

            if (tower.Area.ContainsTile(candidate) && !groundNodes.Contains(candidate))
                return false;

            // Static-entity access positions can contain an isolated pathable pocket that
            // vanilla goals use as a docking point. It is not a usable ground component
            // for an accessway: require at least one cardinal exit and otherwise continue
            // the seed search outward to the surrounding vehicle-reachable terrain.
            foreach (RelTile2i direction in s_experimentalGroundDirections)
            {
                Tile2i neighbour = candidate + direction;
                if (tower.Area.ContainsTile(neighbour))
                {
                    if (groundNodes.Contains(neighbour)) return true;
                }
                else if (provider.IsPathable(neighbour, pathParams.PathabilityQueryMask))
                {
                    return true;
                }
            }
            return false;
        }

        private static int ToHeight2(float height)
            => (int)Math.Round(height * 2f, MidpointRounding.AwayFromZero);
    }
}

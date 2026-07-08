using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.PathFinding;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.Terrain.Trees;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
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
        private const int EXPERIMENTAL_ACCESS_SEARCH_FRAME_BUDGET_MS = 30;
        private const int EXPERIMENTAL_ACCESS_SEARCH_TOTAL_LIMIT_SECONDS = 60;
        private const int EXPERIMENTAL_ACCESS_FIXED_NETWORK_GOAL_LIMIT_WITH_CLEANUP = 256;
        private static UiRoot? s_uiRoot;
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
            public int StubbedThresholdOrigins;
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
            operation = profileCenter2 / 2f < groundHeight
                ? AccessHandoffOperation.Mining
                : AccessHandoffOperation.Dumping;
            return true;
        }

        internal static bool TrySelectHandoffOperationForOrigin(
            int predecessorProfileCenter2,
            float predecessorGroundHeight,
            out AccessHandoffOperation operation)
            => TrySelectHandoffOperationForProfile(predecessorProfileCenter2, predecessorGroundHeight, out operation);

        private static bool TryBuildExperimentalAccessSnapshot(
            IAreaManagingTower tower,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights,
            TerrainManager terrMgr,
            bool isMining,
            bool allowsMixedWork,
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
            if (s_desigManager == null || s_vehiclePathFindingManager == null || s_excavatorPathFindingParams == null)
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

            var groundHeight2 = new Dictionary<Tile2i, int>();
            var terrainCenterHeight2 = new Dictionary<Tile2i, int>();
            var oceanTiles = new HashSet<Tile2i>();
            var fixedProfiles = new Dictionary<Tile2i, AccessHeightProfile>();
            var designatedOrigins = new HashSet<Tile2i>();

            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(boundsMin, boundsMax))
            {
                Tile2i origin = designation.OriginTileCoord;
                designatedOrigins.Add(origin);
                fixedProfiles[origin] = ProfileFromDesignation(designation);
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
            }

            int minHeight2 = int.MaxValue;
            int maxHeight2 = int.MinValue;
            for (int x = groundCaptureMin.X; x <= groundCaptureMax.X; x++)
            {
                for (int y = groundCaptureMin.Y; y <= groundCaptureMax.Y; y++)
                {
                    Tile2i tile = new Tile2i(x, y);
                    int height2 = ToHeight2(terrMgr.GetHeight(tile).Value.ToFloat());
                    groundHeight2[tile] = height2;
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

            foreach (AccessHeightProfile profile in fixedProfiles.Values)
            {
                minHeight2 = Math.Min(minHeight2, Math.Min(Math.Min(profile.Nw2, profile.Ne2), Math.Min(profile.Se2, profile.Sw2)));
                maxHeight2 = Math.Max(maxHeight2, Math.Max(Math.Max(profile.Nw2, profile.Ne2), Math.Max(profile.Se2, profile.Sw2)));
            }

            var durabilityCorners = BuildDurabilityCorners(fixedProfiles, s_buildingFixedHeights2ByTile);
            float landslideRunPerHeight = AutoTerrainDesignationsMod.AccessLandslideRunPerHeight;
            IPathabilityProvider provider = s_vehiclePathFindingManager.PathabilityProvider;
            VehiclePathFindingParams pathParams = s_excavatorPathFindingParams;
            try { provider.UpdateChangedTiles(); } catch { }

            var groundNodes = new HashSet<Tile2i>();
            foreach (var pair in groundHeight2)
            {
                Tile2i tile = pair.Key;
                if (pair.Value < 2 && oceanTiles.Contains(tile)) continue;
                Tile2i alignedOrigin = new Tile2i(tile.X & -4, tile.Y & -4);
                if (designatedOrigins.Contains(alignedOrigin)) continue;
                if (IsDurabilityBlocked(tile, pair.Value, durabilityCorners, landslideRunPerHeight)) continue;
                if (provider.IsPathable(tile, pathParams.PathabilityQueryMask)) groundNodes.Add(tile);
            }

            Dictionary<Tile2i, AccessPropCleanupInfo> propCleanupByOrigin =
                BuildAccessPropCleanupByOrigin(
                    tower,
                    terrMgr,
                    groundCaptureMin,
                    groundCaptureMax,
                    groundHeight2,
                    designatedOrigins,
                    oceanTiles,
                    durabilityCorners,
                    ExtractVehicleClearance(pathParams),
                    landslideRunPerHeight,
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
            towerReachableGround = AccessSearchSnapshot.BuildDiagonalGoalNodes(towerReachableGround);
            if (towerReachableGround.Count == 0)
            {
                failureReason = "NoDiagonalTowerGround";
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
                        terrMgr, groundNodes, propCleanupByOrigin, prospectiveHandoffCache),
                propCleanupByOrigin);
            snapshotTimer.Stop();
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Timing] phase=snapshot algorithm={(snapshot.UseAStar ? "A*" : "Dijkstra")} " +
                $"elapsedMs={snapshotTimer.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"goals={snapshot.GoalCount} fullTowerGoals={fullTowerGoalCount} towerGroundStart={groundStart} " +
                $"landslideSources={snapshot.LandslideSourceCount}");
            LogAccessPropCleanupDiagnostics(cleanupDiagnostics);
            return true;
        }

        private static Dictionary<Tile2i, AccessPropCleanupInfo> BuildAccessPropCleanupByOrigin(
            IAreaManagingTower tower,
            TerrainManager terrMgr,
            Tile2i boundsMin,
            Tile2i boundsMax,
            IReadOnlyDictionary<Tile2i, int> groundHeight2,
            ISet<Tile2i> designatedOrigins,
            ISet<Tile2i> oceanTiles,
            IReadOnlyList<AccessDurabilityCorner> durabilityCorners,
            RelTile1i requiredClearance,
            float landslideRunPerHeight,
            out AccessPropCleanupSnapshotDiagnostics diagnostics)
        {
            diagnostics = new AccessPropCleanupSnapshotDiagnostics();
            var samplesByOrigin = new Dictionary<Tile2i, List<AccessPropSample>>();
            var blockersByOrigin = new Dictionary<Tile2i, AccessPropBlockerKind>();

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
                                durabilityCorners,
                                landslideRunPerHeight,
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
                            durabilityCorners,
                            landslideRunPerHeight,
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
                    if (info.UsesStubbedTerrainRemovalThreshold) diagnostics.StubbedThresholdOrigins++;
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
            IReadOnlyList<AccessDurabilityCorner> durabilityCorners,
            float landslideRunPerHeight,
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
                oceanTiles, durabilityCorners, landslideRunPerHeight);
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
            IReadOnlyList<AccessDurabilityCorner> durabilityCorners,
            float landslideRunPerHeight)
        {
            if (!IsOriginInsideTower(tower, origin) || !tower.Area.ContainsTile(tile))
                return AccessPropBlockerKind.OutOfArea;
            if (designatedOrigins.Contains(origin))
                return AccessPropBlockerKind.ActiveTerrainDesignation;
            if (s_buildingOccupiedTiles.Contains(tile))
                return AccessPropBlockerKind.Building;
            if (!groundHeight2.TryGetValue(tile, out int height2))
                return AccessPropBlockerKind.OutOfArea;
            if (height2 < 2 && oceanTiles.Contains(tile))
                return AccessPropBlockerKind.Ocean;
            if (IsDurabilityBlocked(tile, height2, durabilityCorners, landslideRunPerHeight))
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
                $"stubbedThresholdOrigins={diagnostics.StubbedThresholdOrigins}");
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

        private static bool ShouldSkipFixedNetworkSearchForExperimentalAccess(
            AccessSearchSnapshot snapshot,
            int fixedGoalCount)
        {
            return fixedGoalCount > EXPERIMENTAL_ACCESS_FIXED_NETWORK_GOAL_LIMIT_WITH_CLEANUP
                && snapshot.PropCleanupOrigins.Any(info => info.IsEligible);
        }

        private static AccessPathRequest BuildTowerRootedAccessRequest(
            AccessSearchSnapshot snapshot,
            AccessOriginCluster cluster,
            int requiredWidth,
            float maxCostLimit = float.MaxValue)
        {
            return new AccessPathRequest(
                $"tower-rooted-cluster-{cluster.ClusterId}",
                snapshot,
                new AccessPathEndpoint(
                    AccessPathEndpointKind.FixedProfiles,
                    cluster.Origins.Select(origin => origin.Origin)),
                new AccessPathEndpoint(
                    AccessPathEndpointKind.GroundTiles,
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

        private static AccessPathRequest BuildFixedProfileAccessRequest(
            AccessSearchSnapshot snapshot,
            AccessOriginCluster cluster,
            IEnumerable<Tile2i> fixedGoalOrigins,
            int requiredWidth,
            float maxCostLimit = float.MaxValue)
        {
            return new AccessPathRequest(
                $"fixed-network-cluster-{cluster.ClusterId}",
                snapshot,
                new AccessPathEndpoint(
                    AccessPathEndpointKind.FixedProfiles,
                    cluster.Origins.Select(origin => origin.Origin)),
                new AccessPathEndpoint(
                    AccessPathEndpointKind.FixedProfiles,
                    fixedGoalOrigins),
                requiredWidth,
                AccessPathIntent.ConstructAccessway,
                maxCostLimit);
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

            while (!session.IsComplete)
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
                    && sliceTimer.ElapsedMilliseconds < EXPERIMENTAL_ACCESS_SEARCH_FRAME_BUDGET_MS
                    && searchTimer.Elapsed.TotalSeconds < EXPERIMENTAL_ACCESS_SEARCH_TOTAL_LIMIT_SECONDS);
                sliceTimer.Stop();
                if (sliceTimer.Elapsed > maxSlice) maxSlice = sliceTimer.Elapsed;
                frames++;
                int elapsedSeconds = Math.Min(
                    EXPERIMENTAL_ACCESS_SEARCH_TOTAL_LIMIT_SECONDS,
                    (int)Math.Floor(searchTimer.Elapsed.TotalSeconds));
                if (elapsedSeconds != lastToastSecond)
                {
                    ShowTerrainAnalysisProgressToast(clusterIndex, clusterCount, elapsedSeconds);
                    lastToastSecond = elapsedSeconds;
                }
                if (!session.IsComplete)
                    yield return null;
                if (searchTimer.Elapsed.TotalSeconds >= EXPERIMENTAL_ACCESS_SEARCH_TOTAL_LIMIT_SECONDS
                    && !session.IsComplete)
                {
                    break;
                }
            }

            searchTimer.Stop();
            AccessSearchResult result = session.IsComplete
                ? session.Result
                : new AccessSearchResult(
                    false,
                    "SearchTimeLimit",
                    request.Start.Nodes.Count > 0 ? request.Start.Nodes[0] : default,
                    Array.Empty<AccessSearchNode>(),
                    0f,
                    session.VisitedNodes,
                    session.Rejections);
            output.Result = result;
            RecordExperimentalAccessDryRun(request, cluster, snapshot, result, searchTimer.Elapsed, frames, maxSlice);
        }

        private static void ShowTerrainAnalysisProgressToast(int clusterIndex, int clusterCount, int elapsedSeconds)
        {
            if (s_uiRoot == null) return;
            try
            {
                s_uiRoot.ToastNotifProvider.ShowSuccess(
                    new LocStrFormatted(
                        $"[ATD] Terrain analysis in progress (cluster {clusterIndex}/{clusterCount}, {elapsedSeconds}s)"));
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
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access] request={request.RequestId} " +
                $"{FormatAccessPathRequest(request)} cluster={cluster.ClusterId} " +
                $"algorithm={(snapshot.UseAStar ? "A*" : "Dijkstra")} " +
                $"success={result.Success} reason={reason} start=({result.StartOrigin.X},{result.StartOrigin.Y}) " +
                $"goals={request.Goal.Nodes.Count} landslideRun={landslideRun} " +
                $"landslideSources={snapshot.LandslideSourceCount} cost={cost} " +
                $"visited={result.VisitedNodes} pathNodes={result.Path.Count} frames={frames} " +
                $"searchMs={searchMs} maxSliceMs={maxSliceMs} " +
                $"rejections=[{rejections}]");
            if (result.Success)
            {
                LogExperimentalAccessDebug($"[ATD Experimental Access Path] cluster={cluster.ClusterId} {FormatExperimentalPath(result)}");
                Stopwatch materializeTimer = Stopwatch.StartNew();
                AccessDesignationPlan plan = AccessPathMaterializer.Materialize(snapshot, result);
                materializeTimer.Stop();
                LastExperimentalAccessPlan = plan;
                string materializeMs = materializeTimer.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
                LogExperimentalAccessDebug($"[ATD Experimental Access Plan] cluster={cluster.ClusterId} valid={plan.IsValid} reason={(string.IsNullOrEmpty(plan.FailureReason) ? "none" : plan.FailureReason)} designations={plan.Designations.Count} reused={plan.ReusedNodeCount} groundNodes={plan.GroundNodeCount} cleanupOrigins={plan.CleanupOrigins.Count} traversalCost={result.TraversalCost.ToString("0.##", CultureInfo.InvariantCulture)} generatedWorkCost={result.GeneratedWorkCost.ToString("0.##", CultureInfo.InvariantCulture)} generatedFixedCost={result.GeneratedFixedCost.ToString("0.##", CultureInfo.InvariantCulture)} treeCleanupCost={result.TreeCleanupCost.ToString("0.##", CultureInfo.InvariantCulture)} denseDebrisCleanupCost={result.DenseDebrisCleanupCost.ToString("0.##", CultureInfo.InvariantCulture)} handoff=({plan.HandoffGround.X},{plan.HandoffGround.Y}) handoffOperation={plan.HandoffOperation} materializeMs={materializeMs}");
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

        private static string FormatAccessPathRequest(AccessPathRequest request)
        {
            return
                $"intent={request.Intent} width={request.RequiredWidth} " +
                $"start={request.Start.Kind}:{request.Start.Nodes.Count} " +
                $"goal={request.Goal.Kind}:{request.Goal.Nodes.Count} " +
                $"bounds=({request.BoundsMin.X},{request.BoundsMin.Y})..({request.BoundsMax.X},{request.BoundsMax.Y})";
        }

        private static IReadOnlyList<AccessGroundHandoff> BuildProspectiveWorkableHandoffs(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i predecessorOrigin,
            AccessHeightProfile predecessorProfile,
            TerrainManager terrMgr,
            HashSet<Tile2i> groundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByOrigin,
            Dictionary<string, IReadOnlyList<AccessGroundHandoff>> handoffCache)
        {
            if (((profile.Nw2 | profile.Ne2 | profile.Se2 | profile.Sw2) & 1) != 0)
                return Array.Empty<AccessGroundHandoff>();

            Tile2i referenceOrigin = predecessorOrigin != default && predecessorOrigin != origin
                ? predecessorOrigin
                : origin;
            Tile2i referenceCenter = referenceOrigin + new RelTile2i(2, 2);
            float referenceGroundCenter = terrMgr.GetHeight(referenceCenter).Value.ToFloat();
            int referenceProfileCenter2 = referenceOrigin == origin
                ? profile.Center2
                : predecessorProfile.Center2;
            if (!TrySelectHandoffOperationForOrigin(referenceProfileCenter2, referenceGroundCenter, out AccessHandoffOperation operation))
                return Array.Empty<AccessGroundHandoff>();
            string cacheKey =
                origin.X.ToString(CultureInfo.InvariantCulture) + "," +
                origin.Y.ToString(CultureInfo.InvariantCulture) + "|" +
                profile.Nw2.ToString(CultureInfo.InvariantCulture) + "," +
                profile.Ne2.ToString(CultureInfo.InvariantCulture) + "," +
                profile.Se2.ToString(CultureInfo.InvariantCulture) + "," +
                profile.Sw2.ToString(CultureInfo.InvariantCulture) + "|" +
                operation;
            if (handoffCache.TryGetValue(cacheKey, out IReadOnlyList<AccessGroundHandoff> cached))
                return cached;

            var result = new List<AccessGroundHandoff>();
            var emitted = new HashSet<Tile2i>();
            AddExactGroundHandoffs(
                origin, profile, groundNodes, propCleanupByOrigin, terrMgr, operation, result, emitted);

            var data = new DesignationData(origin,
                new HeightTilesI(profile.Nw2 / 2),
                new HeightTilesI(profile.Ne2 / 2),
                new HeightTilesI(profile.Se2 / 2),
                new HeightTilesI(profile.Sw2 / 2));
            TerrainDesignationProto? proto = operation == AccessHandoffOperation.Mining
                ? s_miningProto
                : s_dumpingProto;
            if (proto == null || !TryBuildProspectiveFulfilledBitmap(
                proto, terrMgr, data, operation, out uint fulfilledBitmap))
            {
                handoffCache[cacheKey] = result.ToArray();
                return handoffCache[cacheKey];
            }
            if ((fulfilledBitmap & READY_PERIMETER_MASK) == 0)
            {
                handoffCache[cacheKey] = result.ToArray();
                return handoffCache[cacheKey];
            }

            for (int y = 0; y <= 4; y++)
            {
                for (int x = 0; x <= 4; x++)
                {
                    if (x != 0 && x != 4 && y != 0 && y != 4) continue;
                    uint mask = GetDesignationMask(x, y);
                    if ((fulfilledBitmap & mask) == 0) continue;
                    Tile2i tile = origin + new RelTile2i(x, y);
                    // Handoff may enter any valid ground node; only the final
                    // search goal needs to be in the tower-reachable flood.
                    if (IsExperimentalAccessGroundOrCleanupNode(groundNodes, propCleanupByOrigin, tile)
                        && emitted.Add(tile))
                        result.Add(new AccessGroundHandoff(tile, operation));
                }
            }
            handoffCache[cacheKey] = result.ToArray();
            return handoffCache[cacheKey];
        }

        private static void AddExactGroundHandoffs(
            Tile2i origin,
            AccessHeightProfile profile,
            HashSet<Tile2i> groundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByOrigin,
            TerrainManager terrMgr,
            AccessHandoffOperation operation,
            List<AccessGroundHandoff> result,
            HashSet<Tile2i> emitted)
        {
            Tile2i center = origin + new RelTile2i(2, 2);
            bool centerMatches = IsExperimentalAccessGroundOrCleanupNode(
                    groundNodes, propCleanupByOrigin, center)
                && ToHeight2(terrMgr.GetHeight(center).Value.ToFloat()) == profile.Center2;

            Tile2i[] corners =
            {
                origin,
                origin + new RelTile2i(4, 0),
                origin + new RelTile2i(4, 4),
                origin + new RelTile2i(0, 4),
            };
            int[] heights = { profile.Nw2, profile.Ne2, profile.Se2, profile.Sw2 };
            var matchingCorners = new bool[corners.Length];
            int matchingCornerCount = 0;
            for (int i = 0; i < corners.Length; i++)
            {
                matchingCorners[i] = IsExperimentalAccessGroundOrCleanupNode(
                        groundNodes, propCleanupByOrigin, corners[i])
                    && ToHeight2(terrMgr.GetHeight(corners[i]).Value.ToFloat()) == heights[i];
                if (matchingCorners[i]) matchingCornerCount++;
            }

            if (!centerMatches && matchingCornerCount < 2) return;

            if (centerMatches && emitted.Add(center))
                result.Add(new AccessGroundHandoff(center, operation));
            for (int i = 0; i < corners.Length; i++)
                if (matchingCorners[i] && emitted.Add(corners[i]))
                    result.Add(new AccessGroundHandoff(corners[i], operation));
        }

        private static bool IsExperimentalAccessGroundOrCleanupNode(
            HashSet<Tile2i> groundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByOrigin,
            Tile2i tile)
        {
            if (groundNodes.Contains(tile))
                return true;
            return propCleanupByOrigin.TryGetValue(TerrainDesignation.GetOrigin(tile), out AccessPropCleanupInfo info)
                && info.IsEligible;
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
            foreach (AccessPlannedDesignation item in placementPlan.Designations)
            {
                if (((item.Profile.Nw2 | item.Profile.Ne2 | item.Profile.Se2 | item.Profile.Sw2) & 1) != 0)
                {
                    failureReason = "HalfLevelCorner";
                    RollBackTreeCleanupSelections(selectedCleanupTrees);
                    RollBackExperimentalDesignations(placedCleanupDesignations, tower, reservedRampTiles);
                    RollBackExperimentalDesignations(placedNow, tower, reservedRampTiles);
                    return false;
                }
                if (s_desigManager.GetDesignationAt(item.Origin).HasValue)
                {
                    failureReason = "DesignationAppeared";
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
                return IsOriginInsideTower(tower, origin)
                    && IsDesignatableTileFullyInsideArea(tower.Area, origin);
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
                string handoff = node.IsGround && node.HandoffOperation != AccessHandoffOperation.None
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

        private static List<AccessDurabilityCorner> BuildDurabilityCorners(
            Dictionary<Tile2i, AccessHeightProfile> profiles,
            IReadOnlyDictionary<Tile2i, HashSet<int>> buildingFixedHeights2ByTile)
        {
            var designationHeightsByPosition = new Dictionary<Tile2i, HashSet<int>>();
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
                    heights.Add(height2);
            }

            return heightsByPosition
                .SelectMany(pair => pair.Value.Select(height2 => new AccessDurabilityCorner(pair.Key, height2)))
                .ToList();
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
            float landslideRunPerHeight)
        {
            foreach (AccessDurabilityCorner corner in durabilityCorners)
            {
                if (corner.Blocks(position, height2, landslideRunPerHeight))
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
            Tile2i towerPosition = GetTowerPosition(tower, boundsMin, boundsMax);
            if (!TryFindNearestTowerGroundSeed(tower, groundNodes, provider, pathParams, towerPosition, out start))
                return false;

            int minX = Math.Min(boundsMin.X, towerPosition.X) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int minY = Math.Min(boundsMin.Y, towerPosition.Y) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxX = Math.Max(boundsMax.X, towerPosition.X) + RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxY = Math.Max(boundsMax.Y, towerPosition.Y) + RAMP_ACCESS_SEARCH_MARGIN_TILES;

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

            return !tower.Area.ContainsTile(candidate) || groundNodes.Contains(candidate);
        }

        private static int ToHeight2(float height)
            => (int)Math.Round(height * 2f, MidpointRounding.AwayFromZero);
    }
}

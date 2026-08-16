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
using Mafi.Core.Vehicles.Excavators;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using AutoTerrainDesignations.Access;
using AutoTerrainDesignations.Access.V2;

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
        private static bool s_enableVerboseHandoffDiagnostics
            => AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace);
        private static UiRoot? s_uiRoot;
        private const double TerrainAnalysisToastMinimumHideSeconds = 10d;
        private static bool s_terrainAnalysisToastHidden;
        private static double s_terrainAnalysisToastHiddenUntilSeconds;
        private static bool s_cancelExperimentalAccessSearch;
        private static readonly List<ATDPropRemovalRequestHandle> s_lastExperimentalPropRemovalRequests =
            new List<ATDPropRemovalRequestHandle>();
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

        private readonly struct PlannedExperimentalDesignation
        {
            public DesignationData Data { get; }
            public TerrainDesignationProto Proto { get; }

            public PlannedExperimentalDesignation(DesignationData data,
                TerrainDesignationProto proto)
            {
                Data = data;
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
                if (!AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                    return;
                if (SampleDetails.Count >= 16)
                    return;
                string kind = sample.IsTree ? "tree" : sample.IsDenseDebris ? "debris" : "prop";
                SampleDetails.Add(
                    $"{kind}:tile=({tile.X},{tile.Y}) origin=({origin.X},{origin.Y}) " +
                    $"key={sample.CleanupObjectKey} blocker={blockerKind}");
            }

            public void RecordOrigin(AccessPropCleanupInfo info)
            {
                if (!AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                    return;
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

        private sealed class ExperimentalAccessSnapshotBuildResult
        {
            public AccessSearchSnapshot? Snapshot { get; set; }
            public string FailureReason { get; set; } = string.Empty;
        }

        private sealed class ProjectedDesignationBuildResult
        {
            public ProjectedDesignationDisturbance? Disturbance { get; set; }
            public string FailureReason { get; set; } = string.Empty;
        }

        private sealed class ProjectedDesignationDisturbance
        {
            public readonly HashSet<Tile2i> CutTiles = new HashSet<Tile2i>();
            public readonly HashSet<Tile2i> FillTiles = new HashSet<Tile2i>();
            public readonly HashSet<Tile2i> CutSafetyTiles = new HashSet<Tile2i>();
            public readonly HashSet<Tile2i> FillSafetyTiles = new HashSet<Tile2i>();
            public readonly Dictionary<Tile2i, float> CutSupportCeilings =
                new Dictionary<Tile2i, float>();
            public readonly Dictionary<Tile2i, float> FillSurfaceFloors =
                new Dictionary<Tile2i, float>();
            public readonly Dictionary<Tile2i, HashSet<Tile2i>> CutSourcesByTile =
                new Dictionary<Tile2i, HashSet<Tile2i>>();
            public readonly Dictionary<Tile2i, HashSet<Tile2i>> FillSourcesByTile =
                new Dictionary<Tile2i, HashSet<Tile2i>>();
            public readonly Dictionary<Tile2i, HashSet<Tile2i>> CutSafetySourcesByTile =
                new Dictionary<Tile2i, HashSet<Tile2i>>();
            public readonly Dictionary<Tile2i, HashSet<Tile2i>> FillSafetySourcesByTile =
                new Dictionary<Tile2i, HashSet<Tile2i>>();
            public int Count => CutTiles.Union(FillTiles).Count();
            public int CutWorkCount => CutSupportCeilings.Count;
            public int FillWorkCount => FillSurfaceFloors.Count;
            public int CutSafetyCount => CutSafetyTiles.Count;
            public int FillSafetyCount => FillSafetyTiles.Count;
            public bool Contains(Tile2i tile)
                => CutTiles.Contains(tile) || FillTiles.Contains(tile);
            public bool TryGetWorkHeight(
                AccessSideRayOperation operation,
                Tile2i tile,
                out float height)
            {
                if (operation == AccessSideRayOperation.Cut)
                    return CutSupportCeilings.TryGetValue(tile, out height);
                if (operation == AccessSideRayOperation.Fill)
                    return FillSurfaceFloors.TryGetValue(tile, out height);
                height = 0f;
                return false;
            }
            public void Add(
                AccessSideRayOperation operation,
                Tile2i tile,
                Tile2i sourceOrigin,
                bool isSafetyOnly)
            {
                if (operation == AccessSideRayOperation.Cut)
                {
                    CutTiles.Add(tile);
                    AddSource(CutSourcesByTile, tile, sourceOrigin);
                    if (isSafetyOnly)
                    {
                        CutSafetyTiles.Add(tile);
                        AddSource(
                            CutSafetySourcesByTile, tile, sourceOrigin);
                    }
                }
                else if (operation == AccessSideRayOperation.Fill)
                {
                    FillTiles.Add(tile);
                    AddSource(FillSourcesByTile, tile, sourceOrigin);
                    if (isSafetyOnly)
                    {
                        FillSafetyTiles.Add(tile);
                        AddSource(
                            FillSafetySourcesByTile, tile, sourceOrigin);
                    }
                }

                static void AddSource(
                    Dictionary<Tile2i, HashSet<Tile2i>> target,
                    Tile2i disturbedTile,
                    Tile2i origin)
                {
                    if (!target.TryGetValue(
                            disturbedTile, out HashSet<Tile2i> sources))
                    {
                        sources = new HashSet<Tile2i>();
                        target.Add(disturbedTile, sources);
                    }
                    sources.Add(origin);
                }
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

        internal static bool TrySelectHandoffOperationFromEdge(
            IReadOnlyList<int> handoffEdgeSigns,
            out AccessHandoffOperation operation)
        {
            if (handoffEdgeSigns.Count == 0)
            {
                operation = AccessHandoffOperation.None;
                return false;
            }

            bool allAtOrBelow = handoffEdgeSigns.All(sign => sign <= 0);
            bool anyBelow = handoffEdgeSigns.Any(sign => sign < 0);
            if (allAtOrBelow && anyBelow)
            {
                operation = AccessHandoffOperation.Mining;
                return true;
            }

            if (handoffEdgeSigns.All(sign => sign >= 0))
            {
                operation = AccessHandoffOperation.Dumping;
                return true;
            }

            operation = AccessHandoffOperation.None;
            return false;
        }

        internal static bool IsHandoffOperationCompatibleWithProfileSigns(
            IReadOnlyList<int> profileWorkSigns,
            AccessHandoffOperation operation)
        {
            if (profileWorkSigns.Count == 0)
                return false;
            if (operation == AccessHandoffOperation.Mining)
                return profileWorkSigns.All(sign => sign <= 0);
            if (operation == AccessHandoffOperation.Dumping)
                return profileWorkSigns.All(sign => sign >= 0);
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
            IReadOnlyCollection<Tile2i>? groundGoalOverride,
            int generatedAreaMarginTiles,
            out AccessSearchSnapshot snapshot,
            out string failureReason)
        {
            var output = new ExperimentalAccessSnapshotBuildResult();
            IEnumerator routine = BuildExperimentalAccessSnapshot(
                tower,
                tileDepths,
                cornerHeights,
                terrMgr,
                isMining,
                allowsMixedWork,
                reachableFixedOrigins,
                groundGoalOverride,
                generatedAreaMarginTiles,
                output,
                sliceControl: null);
            while (routine.MoveNext()) { }

            snapshot = output.Snapshot!;
            failureReason = output.FailureReason;
            return snapshot != null;
        }

        private static IEnumerator BuildExperimentalAccessSnapshot(
            IAreaManagingTower tower,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights,
            TerrainManager terrMgr,
            bool isMining,
            bool allowsMixedWork,
            IReadOnlyCollection<Tile2i>? reachableFixedOrigins,
            IReadOnlyCollection<Tile2i>? groundGoalOverride,
            int generatedAreaMarginTiles,
            ExperimentalAccessSnapshotBuildResult output,
            ExperimentalAccessSliceControl? sliceControl)
        {
            Stopwatch snapshotTimer = Stopwatch.StartNew();
            AccessSearchSnapshot snapshot = null!;
            string failureReason = string.Empty;
            sliceControl?.ReportPhase("Capturing terrain");
            if (!AccessSearchFixtureGate.EnsureInitialized(out failureReason))
            {
                output.FailureReason = "AccessFixtureGate: " + failureReason;
                yield break;
            }
            if (s_desigManager == null || s_vehiclePathFindingManager == null)
            {
                output.FailureReason = "PathfindingUnavailable";
                yield break;
            }

            generatedAreaMarginTiles = Math.Max(0, generatedAreaMarginTiles);
            Tile2i towerBoundsMin = tower.Area.BoundingBoxMin;
            Tile2i towerBoundsMax = tower.Area.BoundingBoxMax;
            Tile2i boundsMin = new Tile2i(
                Math.Max(0, towerBoundsMin.X - generatedAreaMarginTiles),
                Math.Max(0, towerBoundsMin.Y - generatedAreaMarginTiles));
            Tile2i boundsMax = new Tile2i(
                generatedAreaMarginTiles > 0
                    ? Math.Min(terrMgr.TerrainSize.X - 4,
                        towerBoundsMax.X + generatedAreaMarginTiles)
                    : towerBoundsMax.X,
                generatedAreaMarginTiles > 0
                    ? Math.Min(terrMgr.TerrainSize.Y - 4,
                        towerBoundsMax.Y + generatedAreaMarginTiles)
                    : towerBoundsMax.Y);
            Tile2i towerCenter = tower is IEntityWithPosition positioned
                ? positioned.Position2f.Tile2i
                : new Tile2i(
                    (towerBoundsMin.X + towerBoundsMax.X) / 2,
                    (towerBoundsMin.Y + towerBoundsMax.Y) / 2);
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
                    - AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance
                    - AutoTerrainDesignationsMod.AccessRayEndBuffer),
                Math.Max(physicalTerrainMin.Y, groundCaptureMin.Y
                    - AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance
                    - AutoTerrainDesignationsMod.AccessRayEndBuffer));
            groundCaptureMax = new Tile2i(
                Math.Min(physicalTerrainMax.X, groundCaptureMax.X
                    + AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance
                    + AutoTerrainDesignationsMod.AccessRayEndBuffer),
                Math.Min(physicalTerrainMax.Y, groundCaptureMax.Y
                    + AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance
                    + AutoTerrainDesignationsMod.AccessRayEndBuffer));
            long captureDesignationRevision =
                CurrentTerrainDesignationRevision;

            var groundHeight2 = new Dictionary<Tile2i, int>();
            var preciseTerrainHeights = new Dictionary<Tile2i, float>();
            var terrainColumns = new Dictionary<Tile2i, AccessTerrainColumn>();
            var terrainCenterHeight2 = new Dictionary<Tile2i, int>();
            var oceanTiles = new HashSet<Tile2i>();
            var fixedProfiles = new Dictionary<Tile2i, AccessHeightProfile>();
            var designatedOrigins = new HashSet<Tile2i>();
            var rayDesignationOrigins = new HashSet<Tile2i>();
            var rayDesignations = new Dictionary<Tile2i, TerrainDesignation>();
            var projectedRayDesignations = new Dictionary<Tile2i, TerrainDesignation>();
            var groundExclusionReasons = new Dictionary<Tile2i, string>();

            Stopwatch phaseTimer = Stopwatch.StartNew();

            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(boundsMin, boundsMax))
            {
                Tile2i origin = designation.OriginTileCoord;
                designatedOrigins.Add(origin);
                fixedProfiles[origin] = ProfileFromDesignation(designation);
                if (sliceControl != null
                    && phaseTimer.ElapsedMilliseconds
                        >= sliceControl.SliceBudgetMilliseconds)
                {
                    if (sliceControl.CancellationRequested)
                    {
                        output.FailureReason = "SearchCancelled";
                        yield break;
                    }
                    phaseTimer.Restart();
                    yield return null;
                    phaseTimer.Restart();
                }
            }
            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(
                groundCaptureMin, groundCaptureMax))
            {
                rayDesignationOrigins.Add(designation.OriginTileCoord);
                rayDesignations[designation.OriginTileCoord] = designation;
                if (sliceControl != null
                    && phaseTimer.ElapsedMilliseconds
                        >= sliceControl.SliceBudgetMilliseconds)
                {
                    if (sliceControl.CancellationRequested)
                    {
                        output.FailureReason = "SearchCancelled";
                        yield break;
                    }
                    phaseTimer.Restart();
                    yield return null;
                    phaseTimer.Restart();
                }
            }
            // Projected rays have no artificial distance limit. Include every
            // designation on the physical map so a remote but sufficiently deep
            // cut cannot reach the search area without being represented.
            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(
                physicalTerrainMin, physicalTerrainMax))
            {
                projectedRayDesignations[designation.OriginTileCoord] = designation;
                if (sliceControl != null
                    && phaseTimer.ElapsedMilliseconds
                        >= sliceControl.SliceBudgetMilliseconds)
                {
                    if (sliceControl.CancellationRequested)
                    {
                        output.FailureReason = "SearchCancelled";
                        yield break;
                    }
                    phaseTimer.Restart();
                    yield return null;
                    phaseTimer.Restart();
                }
            }

            if (sliceControl != null)
            {
                if (sliceControl.CancellationRequested)
                {
                    output.FailureReason = "SearchCancelled";
                    yield break;
                }
                phaseTimer.Restart();
                yield return null;
                phaseTimer.Restart();
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
                if (sliceControl != null
                    && phaseTimer.ElapsedMilliseconds
                        >= sliceControl.SliceBudgetMilliseconds)
                {
                    if (sliceControl.CancellationRequested)
                    {
                        output.FailureReason = "SearchCancelled";
                        yield break;
                    }
                    phaseTimer.Restart();
                    yield return null;
                    phaseTimer.Restart();
                }
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

                    if (sliceControl != null
                        && phaseTimer.ElapsedMilliseconds
                            >= sliceControl.SliceBudgetMilliseconds)
                    {
                        if (sliceControl.CancellationRequested)
                        {
                            output.FailureReason = "SearchCancelled";
                            yield break;
                        }
                        phaseTimer.Restart();
                        yield return null;
                        phaseTimer.Restart();
                    }
                }
            }

            int firstOriginX = boundsMin.X & -4;
            int firstOriginY = boundsMin.Y & -4;
            for (int x = firstOriginX; x <= boundsMax.X; x += 4)
            {
                for (int y = firstOriginY; y <= boundsMax.Y; y += 4)
                {
                    Tile2i origin = new Tile2i(x, y);
                    if (!IsOriginInsideGeneratedArea(
                            tower, origin, generatedAreaMarginTiles))
                        continue;
                    Tile2i center = origin + new RelTile2i(2, 2);
                    terrainCenterHeight2[origin] = groundHeight2.TryGetValue(center, out int h2)
                        ? h2
                        : ToHeight2(terrMgr.GetHeight(center).Value.ToFloat());

                    if (sliceControl != null
                        && phaseTimer.ElapsedMilliseconds
                            >= sliceControl.SliceBudgetMilliseconds)
                    {
                        if (sliceControl.CancellationRequested)
                        {
                            output.FailureReason = "SearchCancelled";
                            yield break;
                        }
                        phaseTimer.Restart();
                        yield return null;
                        phaseTimer.Restart();
                    }
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

                    if (sliceControl != null
                        && phaseTimer.ElapsedMilliseconds
                            >= sliceControl.SliceBudgetMilliseconds)
                    {
                        if (sliceControl.CancellationRequested)
                        {
                            output.FailureReason = "SearchCancelled";
                            yield break;
                        }
                        phaseTimer.Restart();
                        yield return null;
                        phaseTimer.Restart();
                    }
                }
            }
            sliceControl?.ReportPhase("Projecting designations");
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
            var projectedDesignationBuild =
                new ProjectedDesignationBuildResult();
            IEnumerator projectedDesignationRoutine =
                BuildProjectedDesignationDisturbedTilesSliced(
                    projectedRayDesignations,
                    terrMgr,
                    preciseTerrainHeights,
                    terrainColumns,
                    groundCaptureMin,
                    groundCaptureMax,
                    physicalTerrainMin,
                    physicalTerrainMax,
                    dumpingMaterialSlope,
                    fallbackMiningSlope,
                    vehicleDisturbanceRadius,
                    projectedDesignationBuild,
                    sliceControl);
            while (projectedDesignationRoutine.MoveNext())
                yield return projectedDesignationRoutine.Current;
            ProjectedDesignationDisturbance projectedDesignationDisturbance =
                projectedDesignationBuild.Disturbance
                ?? new ProjectedDesignationDisturbance();
            string projectedRayFailure = projectedDesignationBuild.FailureReason;
            if (!string.IsNullOrEmpty(projectedRayFailure))
            {
                Log.Error("[ATD] Existing-work ray projection failed critically: "
                    + projectedRayFailure);
                output.FailureReason = projectedRayFailure;
                yield break;
            }

            if (sliceControl != null)
            {
                if (sliceControl.CancellationRequested)
                {
                    output.FailureReason = "SearchCancelled";
                    yield break;
                }
                phaseTimer.Restart();
                yield return null;
                phaseTimer.Restart();
            }

            sliceControl?.ReportPhase("Building navigation");

            foreach (AccessHeightProfile profile in fixedProfiles.Values)
            {
                minHeight2 = Math.Min(minHeight2, Math.Min(Math.Min(profile.Nw2, profile.Ne2), Math.Min(profile.Se2, profile.Sw2)));
                maxHeight2 = Math.Max(maxHeight2, Math.Max(Math.Max(profile.Nw2, profile.Ne2), Math.Max(profile.Se2, profile.Sw2)));
                if (sliceControl != null
                    && phaseTimer.ElapsedMilliseconds
                        >= sliceControl.SliceBudgetMilliseconds)
                {
                    if (sliceControl.CancellationRequested)
                    {
                        output.FailureReason = "SearchCancelled";
                        yield break;
                    }
                    phaseTimer.Restart();
                    yield return null;
                    phaseTimer.Restart();
                }
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
            RefreshPathabilityAndInvalidateReachability();
            VehiclePathFindingParams t1PathParams = VehiclePathFindingParams.DEFAULT;
            bool hasT1DiagnosticMask = vehicleClearance > 4
                && TryGetTierExcavatorPathFindingParams(
                    "T1", out t1PathParams);

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
                    groundExclusionReasons[tile] = hasT1DiagnosticMask
                        && provider.IsPathable(
                            tile, t1PathParams.PathabilityQueryMask)
                                ? "T1Only"
                                : "NotPathable";

                if (sliceControl != null
                    && phaseTimer.ElapsedMilliseconds
                        >= sliceControl.SliceBudgetMilliseconds)
                {
                    if (sliceControl.CancellationRequested)
                    {
                        output.FailureReason = "SearchCancelled";
                        yield break;
                    }
                    phaseTimer.Restart();
                    yield return null;
                    phaseTimer.Restart();
                }
            }

            Func<Tile2i, bool> isTerrainPathableWithoutBlockers =
                BuildTerrainOnlyPathabilityPredicate(
                    provider, pathParams.PathabilityQueryMask,
                    ExtractVehicleClearance(pathParams));
            var terrainPathableWithoutProps = new HashSet<Tile2i>();
            foreach (Tile2i tile in groundHeight2.Keys)
            {
                if (isTerrainPathableWithoutBlockers(tile))
                    terrainPathableWithoutProps.Add(tile);
                if (sliceControl != null
                    && phaseTimer.ElapsedMilliseconds
                        >= sliceControl.SliceBudgetMilliseconds)
                {
                    if (sliceControl.CancellationRequested)
                    {
                        output.FailureReason = "SearchCancelled";
                        yield break;
                    }
                    phaseTimer.Restart();
                    yield return null;
                    phaseTimer.Restart();
                }
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
                    isTerrainPathableWithoutBlockers,
                    out Dictionary<Tile2i, AccessPropCleanupInfo> propCleanupByTile,
                    out AccessPropCleanupSnapshotDiagnostics cleanupDiagnostics);
            if (sliceControl != null)
            {
                if (sliceControl.CancellationRequested)
                {
                    output.FailureReason = "SearchCancelled";
                    yield break;
                }
                phaseTimer.Restart();
                yield return null;
                phaseTimer.Restart();
            }
            var prospectiveHandoffCache =
                new Dictionary<string, IReadOnlyList<AccessGroundHandoff>>(StringComparer.Ordinal);
            var prospectiveHandoffSpanCache =
                new Dictionary<string, IReadOnlyList<AccessGroundHandoff>>(StringComparer.Ordinal);
            var prospectiveV2HandoffCache =
                new Dictionary<string, IReadOnlyList<AccessGroundHandoff>>(StringComparer.Ordinal);
            var prospectiveV2HandoffSpanCache =
                new Dictionary<string, IReadOnlyList<AccessGroundHandoff>>(StringComparer.Ordinal);

            HashSet<Tile2i> towerReachableGround;
            Tile2i groundStart;
            int fullTowerGoalCount;
            if (groundGoalOverride != null && groundGoalOverride.Count > 0)
            {
                towerReachableGround = groundGoalOverride
                    .Where(groundNodes.Contains)
                    .ToHashSet();
                if (towerReachableGround.Count == 0)
                {
                    output.FailureReason = "NoOverrideGroundGoalsInSnapshot";
                    yield break;
                }
                groundStart = towerReachableGround.First();
                fullTowerGoalCount = towerReachableGround.Count;
                LogExperimentalAccessDebug(
                    $"[ATD Access Ground Goal Override] " +
                    $"selected={towerReachableGround.Count} " +
                    $"goals=[{string.Join(",", towerReachableGround)}]");
            }
            else
            {
                if (!TryBuildTowerReachableGround(tower, towerBoundsMin, towerBoundsMax,
                    groundNodes, provider, pathParams,
                    out towerReachableGround, out groundStart))
                {
                    output.FailureReason = "NoTowerGround";
                    yield break;
                }
                if (towerReachableGround.Count == 0)
                {
                    output.FailureReason = "NoTowerReachableGround";
                    yield break;
                }
                fullTowerGoalCount = towerReachableGround.Count;
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
                    $"[ATD Access Tower Radial Goals] center={towerCenter} " +
                    $"selected={towerReachableGround.Count} maxSteps=12 {radialGoalDiagnostic}");
                if (towerReachableGround.Count == 0)
                {
                    output.FailureReason = "NoTowerRadialGroundGoals";
                    yield break;
                }
            }
            if (vehicleClearance > 4)
                LogV2GroundGraphDiagnostics(
                    vehicleClearance,
                    groundNodes,
                    fullTowerGoalCount,
                    towerReachableGround,
                    propCleanupByTile,
                    groundExclusionReasons);
            if (minHeight2 == int.MaxValue) { minHeight2 = 0; maxHeight2 = 0; }

            if (sliceControl != null)
            {
                if (sliceControl.CancellationRequested)
                {
                    output.FailureReason = "SearchCancelled";
                    yield break;
                }
                phaseTimer.Restart();
                yield return null;
                phaseTimer.Restart();
            }

            sliceControl?.ReportPhase("Finalizing snapshot");

            if (CurrentTerrainDesignationRevision != captureDesignationRevision
                && HasTerrainDesignationMutationSince(
                    captureDesignationRevision,
                    physicalTerrainMin,
                    physicalTerrainMax))
            {
                output.FailureReason = "CaptureRevisionChanged";
                yield break;
            }
            if (sliceControl?.CancellationRequested == true)
            {
                output.FailureReason = "SearchCancelled";
                yield break;
            }

            AccessUsefulHeightEnvelope? usefulHeightEnvelope = null;
            if (AutoTerrainDesignationsMod.ExperimentalAccessUsefulHeightEnvelope)
            {
                if (!AccessUsefulHeightEnvelope.ValidateSelfTest(
                        out string envelopeSelfTestFailure))
                {
                    s_log.Warning(
                        "[ATD Access Height Envelope] build=skipped v1Pruning=off v2Pruning=off "
                        + "reason=SelfTestFailed:" + envelopeSelfTestFailure);
                }
                else
                {
                    Stopwatch envelopeTimer = Stopwatch.StartNew();
                    bool envelopeBuilt = AccessUsefulHeightEnvelope.TryCreate(
                        preciseTerrainHeights,
                        oceanTiles,
                        fixedProfiles,
                        out usefulHeightEnvelope,
                        out string envelopeFailure,
                        v1LowerAllowance32: AutoTerrainDesignationsMod
                            .ExperimentalAccessV1HeightEnvelopeLowerAllowance32,
                        v2LowerAllowance32: AutoTerrainDesignationsMod
                            .ExperimentalAccessV2HeightEnvelopeLowerAllowance32,
                        v1UpperAllowance32: AutoTerrainDesignationsMod
                            .ExperimentalAccessV1HeightEnvelopeUpperAllowance32,
                        v2UpperAllowance32: AutoTerrainDesignationsMod
                            .ExperimentalAccessV2HeightEnvelopeUpperAllowance32);
                    if (!envelopeBuilt || usefulHeightEnvelope == null)
                    {
                        envelopeTimer.Stop();
                        s_log.Warning(
                            "[ATD Access Height Envelope] build=failed "
                            + "v1Pruning=off v2Pruning=off reason="
                            + (string.IsNullOrEmpty(envelopeFailure)
                                ? "NoEnvelopeReturned"
                                : envelopeFailure)
                            + " elapsedMs="
                            + envelopeTimer.Elapsed.TotalMilliseconds.ToString(
                                "0.##", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        envelopeTimer.Stop();
                        AccessUsefulHeightEnvelope completedEnvelope = usefulHeightEnvelope;
                        AccessUsefulHeightEnvelopeDiagnostics diagnostics =
                            completedEnvelope.Diagnostics;
                        LogExperimentalAccessDebug(
                            "[ATD Access Height Envelope] build=complete v1Pruning=on v2Pruning=on "
                            + "sourcePolicy=allFixedSnapshot+requestEndpointExtension "
                            + "endpointExtension=[v1Upper:"
                            + (completedEnvelope.V1UpperAllowance32 / 32d).ToString(
                                "0.#####", CultureInfo.InvariantCulture)
                            + ",v1Lower:"
                            + (completedEnvelope.V1LowerAllowance32 / 32d).ToString(
                                "0.#####", CultureInfo.InvariantCulture)
                            + ",v2Upper:"
                            + (completedEnvelope.V2UpperAllowance32 / 32d).ToString(
                                "0.#####", CultureInfo.InvariantCulture)
                            + ",v2Lower:"
                            + (completedEnvelope.V2LowerAllowance32 / 32d).ToString(
                                "0.#####", CultureInfo.InvariantCulture)
                            + "] "
                            + "elapsedMs="
                            + envelopeTimer.Elapsed.TotalMilliseconds.ToString(
                                "0.##", CultureInfo.InvariantCulture)
                            + " bounds=(" + completedEnvelope.Min.X.ToString(CultureInfo.InvariantCulture)
                            + "," + completedEnvelope.Min.Y.ToString(CultureInfo.InvariantCulture)
                            + ") size=" + diagnostics.Width.ToString(CultureInfo.InvariantCulture)
                            + "x" + diagnostics.Height.ToString(CultureInfo.InvariantCulture)
                            + " tiles=" + diagnostics.TileCount.ToString(CultureInfo.InvariantCulture)
                            + " memoryMiB="
                            + (diagnostics.ArrayBytes / (1024d * 1024d)).ToString(
                                "0.##", CultureInfo.InvariantCulture)
                            + " sources=[terrain:" + diagnostics.TerrainSourceCount.ToString(CultureInfo.InvariantCulture)
                            + ",oceanUpper:" + diagnostics.OceanUpperSourceCount.ToString(CultureInfo.InvariantCulture)
                            + ",fixedProfiles:" + diagnostics.FixedProfileCount.ToString(CultureInfo.InvariantCulture)
                            + ",fixedSamples:" + diagnostics.FixedProfileSampleCount.ToString(CultureInfo.InvariantCulture)
                            + "] band32=[min:" + diagnostics.MinimumBandWidth32.ToString(CultureInfo.InvariantCulture)
                            + ",avg:" + diagnostics.AverageBandWidth32.ToString("0.##", CultureInfo.InvariantCulture)
                            + ",max:" + diagnostics.MaximumBandWidth32.ToString(CultureInfo.InvariantCulture)
                            + "] missing=" + diagnostics.MissingBandCount.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            if (sliceControl != null)
            {
                if (sliceControl.CancellationRequested)
                {
                    output.FailureReason = "SearchCancelled";
                    yield break;
                }
                phaseTimer.Restart();
                yield return null;
                phaseTimer.Restart();
            }

            bool useAStar =
                AutoTerrainDesignationsMod.ExperimentalAccessUseAStar;
            AccessV1GroundGoalDistance? prebuiltV1GroundGoalDistance = null;
            float[]? prebuiltAnyGoalDistance = null;
            if (sliceControl != null && useAStar)
            {
                sliceControl.ReportPhase("Building V1 ground routes");
                var groundGoalBuild =
                    new AccessV1GroundGoalDistance.BuildSession(
                        groundNodes,
                        propCleanupByTile,
                        towerReachableGround,
                        takeOwnership: true);
                while (!groundGoalBuild.IsComplete)
                {
                    Stopwatch buildSlice = Stopwatch.StartNew();
                    do
                    {
                        groundGoalBuild.Advance(maxWorkItems: 64);
                    }
                    while (!groundGoalBuild.IsComplete
                        && !sliceControl.CancellationRequested
                        && buildSlice.ElapsedMilliseconds
                            < sliceControl.SliceBudgetMilliseconds);
                    if (sliceControl.CancellationRequested)
                    {
                        output.FailureReason = "SearchCancelled";
                        yield break;
                    }
                    yield return null;
                }
                prebuiltV1GroundGoalDistance = groundGoalBuild.Result;

                Tile2i goalDistanceMin = boundsMin;
                Tile2i goalDistanceMax = boundsMax;
                foreach (Tile2i tile in groundNodes)
                    ExpandGoalDistanceBounds(tile);
                foreach (KeyValuePair<Tile2i, AccessPropCleanupInfo> pair
                    in propCleanupByTile)
                {
                    if (pair.Value.IsEligible)
                        ExpandGoalDistanceBounds(pair.Key);
                }

                sliceControl.ReportPhase("Building V1 goal potential");
                var anyGoalBuild = new AccessGoalDistanceBuildSession(
                    goalDistanceMin,
                    goalDistanceMax,
                    towerReachableGround);
                while (!anyGoalBuild.IsComplete)
                {
                    Stopwatch buildSlice = Stopwatch.StartNew();
                    do
                    {
                        anyGoalBuild.Advance(maxWorkItems: 64);
                    }
                    while (!anyGoalBuild.IsComplete
                        && !sliceControl.CancellationRequested
                        && buildSlice.ElapsedMilliseconds
                            < sliceControl.SliceBudgetMilliseconds);
                    if (sliceControl.CancellationRequested)
                    {
                        output.FailureReason = "SearchCancelled";
                        yield break;
                    }
                    yield return null;
                }
                prebuiltAnyGoalDistance = anyGoalBuild.Result;

                void ExpandGoalDistanceBounds(Tile2i tile)
                {
                    goalDistanceMin = new Tile2i(
                        Math.Min(goalDistanceMin.X, tile.X),
                        Math.Min(goalDistanceMin.Y, tile.Y));
                    goalDistanceMax = new Tile2i(
                        Math.Max(goalDistanceMax.X, tile.X),
                        Math.Max(goalDistanceMax.Y, tile.Y));
                }
            }

            sliceControl?.ReportPhase("Constructing snapshot");
            snapshot = new AccessSearchSnapshot(
                boundsMin,
                boundsMax,
                towerCenter,
                minHeight2 - 2,
                maxHeight2 + 2,
                isMining,
                allowsMixedWork,
                useAStar,
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
                sliceControl != null
                    ? durabilityCorners.ToArray()
                    : durabilityCorners,
                (origin, profile, predecessorOrigin, predecessorProfile) =>
                    BuildProspectiveWorkableHandoffs(
                        origin, profile, predecessorOrigin, predecessorProfile,
                        terrMgr, groundNodes, towerReachableGround,
                        propCleanupByTile, terrainPathableWithoutProps,
                        vehicleClearance, prospectiveHandoffCache,
                        propCleanupByOrigin: propCleanupByOrigin),
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
                projectedCutSourcesByTile: projectedDesignationDisturbance.CutSourcesByTile,
                projectedFillSourcesByTile: projectedDesignationDisturbance.FillSourcesByTile,
                projectedCutSafetyTiles: projectedDesignationDisturbance.CutSafetyTiles,
                projectedFillSafetyTiles: projectedDesignationDisturbance.FillSafetyTiles,
                projectedCutSafetySourcesByTile: projectedDesignationDisturbance.CutSafetySourcesByTile,
                projectedFillSafetySourcesByTile: projectedDesignationDisturbance.FillSafetySourcesByTile,
                vehicleClearanceRadius: vehicleDisturbanceRadius,
                avoidOcean: AccessAvoidOcean,
                avoidBuildings: AccessAvoidBuildings,
                vehicleWidth: vehicleClearance,
                 workableHandoffSpans: cells => BuildProspectiveWorkableHandoffSpan(
                     cells, terrMgr, groundNodes, towerReachableGround,
                     propCleanupByTile, terrainPathableWithoutProps,
                     vehicleClearance,
                     validateEverySpanCell: true,
                     prospectiveHandoffSpanCache,
                     propCleanupByOrigin: propCleanupByOrigin),
                 propCleanupByTile: propCleanupByTile,
                 v2WorkableHandoffs:
                    (origin, profile, predecessorOrigin, predecessorProfile) =>
                        BuildProspectiveWorkableHandoffs(
                            origin, profile, predecessorOrigin, predecessorProfile,
                            terrMgr, groundNodes, towerReachableGround,
                            propCleanupByTile, terrainPathableWithoutProps,
                            0, prospectiveV2HandoffCache,
                            useV2CornerCrestRule: true,
                            propCleanupByOrigin: propCleanupByOrigin),
                 v2WorkableHandoffSpans: cells =>
                    BuildProspectiveWorkableHandoffSpan(
                        cells, terrMgr, groundNodes, towerReachableGround,
                        propCleanupByTile, terrainPathableWithoutProps, 0,
                        validateEverySpanCell: false,
                        prospectiveV2HandoffSpanCache,
                        useV2CornerCrestRule: true,
                        propCleanupByOrigin: propCleanupByOrigin),
                usefulHeightEnvelope: usefulHeightEnvelope,
                terrainPathableWithoutBlockers:
                    terrainPathableWithoutProps,
                takeOwnership: sliceControl != null,
                prebuiltV1GroundGoalDistance:
                    prebuiltV1GroundGoalDistance,
                prebuiltAnyGoalDistance: prebuiltAnyGoalDistance);
            snapshotTimer.Stop();
            output.Snapshot = snapshot;
            output.FailureReason = string.Empty;
            LogExperimentalAccessDebug(
                $"[ATD Access Timing] phase=snapshot algorithm={(snapshot.UseAStar ? "A*" : "Dijkstra")} " +
                $"elapsedMs={snapshotTimer.Elapsed.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"goals={snapshot.GoalCount} fullTowerGoals={fullTowerGoalCount} towerGroundStart={groundStart} " +
                $"v1GroundPotentialNodes={snapshot.V1GroundGoalDistance?.ReachableNodeCount ?? 0} " +
                $"rayHeightSamples={preciseTerrainHeights.Count} rayMaterialColumns={terrainColumns.Count} " +
                $"dumpingSlope={dumpingMaterialSlope.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"fallbackMiningSlope={fallbackMiningSlope.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"hasDumpingMaterial={hasDumpingMaterial} " +
                $"maxHandoffSpan={AccessPathSearch.GetMaxHandoffSpanLength(vehicleClearance)} " +
                $"rayConservatism={AutoTerrainDesignationsMod.AccessRaySlopeConservatism.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"rayBuffer={AutoTerrainDesignationsMod.AccessRayEndBuffer} " +
                $"candidateRayMaxDistance={AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance} " +
                $"avoidOcean={snapshot.AvoidOcean} avoidBuildings={snapshot.AvoidBuildings} " +
                $"materialSlopeSource={materialSlopeDiagnostic} " +
                $"landslideSources={snapshot.LandslideSourceCount} " +
                $"projectedDesignation=[blocked:{projectedDesignationDisturbance.Count}," +
                $"cutWork:{projectedDesignationDisturbance.CutWorkCount}," +
                $"fillWork:{projectedDesignationDisturbance.FillWorkCount}," +
                $"cutSafety:{projectedDesignationDisturbance.CutSafetyCount}," +
                $"fillSafety:{projectedDesignationDisturbance.FillSafetyCount}] " +
                $"pathParams={pathParamsSource} " +
                (sliceControl == null
                    ? "sliceStats=sync"
                    : sliceControl.FormatDiagnostics()));
            LogAccessPropCleanupDiagnostics(cleanupDiagnostics);
            if (sliceControl != null)
                yield return null;
            yield break;
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
                    GetCutMaterialSlope(material),
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
                        GetDumpMaterialSlope(material));
                    fallbackMiningSlope = Math.Min(
                        fallbackMiningSlope,
                        GetCutMaterialSlope(material));
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
                        GetDumpMaterialSlope(product.TerrainMaterial.Value));
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

        private static float GetCutMaterialSlope(TerrainMaterialProto material)
            // A cut ray predicts failure of the still-intact bank. Preserve the
            // empirical 2/3 conversion used by vanilla's approximate slope, but
            // expose the intact material's full collapse range as a shared
            // conservatism setting instead of forcing its disrupted resting slope.
            => InterpolateRaySlope(material);

        private static float GetDumpMaterialSlope(TerrainMaterialProto material)
            // Dumped terrain is loose, so use the disrupted variant when one exists.
            => InterpolateRaySlope(material.DisruptedMaterialProto.ValueOr(material));

        private static float InterpolateRaySlope(TerrainMaterialProto material)
        {
            return InterpolateRaySlopeBounds(
                material.MinCollapseHeightDiff.Value.ToFloat(),
                material.MaxCollapseHeightDiff.Value.ToFloat(),
                AutoTerrainDesignationsMod.AccessRaySlopeConservatism);
        }

        internal static float InterpolateRaySlopeBounds(
            float minCollapseHeightDiff,
            float maxCollapseHeightDiff,
            float conservatism)
        {
            float conservative = minCollapseHeightDiff * (2f / 3f);
            float aggressive = maxCollapseHeightDiff * (2f / 3f);
            conservatism = Math.Max(0f, Math.Min(1.5f, conservatism));
            return aggressive + (conservative - aggressive) * conservatism;
        }

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
            Func<Tile2i, bool> isTerrainPathableWithoutBlockers,
            out Dictionary<Tile2i, AccessPropCleanupInfo> cleanupByTile,
            out AccessPropCleanupSnapshotDiagnostics diagnostics)
        {
            diagnostics = new AccessPropCleanupSnapshotDiagnostics();
            var samplesByOrigin = new Dictionary<Tile2i, List<AccessPropSample>>();
            var blockersByOrigin = new Dictionary<Tile2i, AccessPropBlockerKind>();
            var samplesByTile = new Dictionary<Tile2i, List<AccessPropSample>>();
            var blockersByTile = new Dictionary<Tile2i, AccessPropBlockerKind>();
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
                    var eligibleCleanupOrigins = new HashSet<Tile2i>();
                    for (int i = 0; i < occupiedTiles.Count; i++)
                    {
                        Tile2i candidate = TerrainDesignation.GetOrigin(
                            occupiedTiles[i]);
                        if (IsDenseDebrisCleanupOriginStaticallyFree(
                                tower, candidate, designatedOrigins))
                            eligibleCleanupOrigins.Add(candidate);
                    }
                    Tile2i[] orderedCleanupOrigins = eligibleCleanupOrigins
                        .OrderBy(item => item.X)
                        .ThenBy(item => item.Y)
                        .ToArray();
                    for (int i = 0; i < occupiedTiles.Count; i++)
                    {
                        Tile2i occupiedTile = occupiedTiles[i];
                        foreach (Tile2i tile in EnumerateBlockedCenterTilesForOccupiedTile(
                            occupiedTile, requiredClearance, boundsMin, boundsMax))
                        {
                            Tile2i origin = TerrainDesignation.GetOrigin(tile);
                            AccessPropSample sample = new AccessPropSample(
                                tile, isTree: false, isDenseDebris: true,
                                isRemovable: orderedCleanupOrigins.Length > 0,
                                cleanupObjectKey: BuildPropCleanupKey(prop.Id),
                                eligibleCleanupOrigins: orderedCleanupOrigins,
                                dumpBurialProbeTile: prop.Position.Tile2i,
                                dumpBurialProbeOffsetX:
                                    prop.PositionWithinTile.X.ToFloat(),
                                dumpBurialProbeOffsetY:
                                    prop.PositionWithinTile.Y.ToFloat(),
                                placedHeight:
                                    prop.PlacedAtHeight.Value.ToFloat(),
                                dumpBurialThreshold:
                                    prop.Proto.DespawnBuriedThreshold
                                        .ScaledBy(prop.Scale).Value.ToFloat(),
                                denseDebrisPropId: prop.Id);
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
                                isTerrainPathableWithoutBlockers,
                                samplesByOrigin,
                                blockersByOrigin,
                                samplesByTile,
                                blockersByTile);
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
                            isTerrainPathableWithoutBlockers,
                            samplesByOrigin,
                            blockersByOrigin,
                            samplesByTile,
                            blockersByTile);
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

            cleanupByTile = new Dictionary<Tile2i, AccessPropCleanupInfo>();
            foreach (KeyValuePair<Tile2i, List<AccessPropSample>> pair in samplesByTile)
            {
                blockersByTile.TryGetValue(
                    pair.Key, out AccessPropBlockerKind blockerKind);
                cleanupByTile[pair.Key] = AccessPropCleanupPolicy.BuildOriginInfo(
                    TerrainDesignation.GetOrigin(pair.Key), pair.Value, blockerKind);
            }

            return cleanupByOrigin;
        }

        private static bool IsDenseDebrisCleanupOriginStaticallyFree(
            IAreaManagingTower tower,
            Tile2i origin,
            ISet<Tile2i> designatedOrigins)
        {
            if (!IsOriginInsideTower(tower, origin)
                || !IsDesignatableTileFullyInsideArea(tower.Area, origin)
                || designatedOrigins.Contains(origin)
                || DoesOriginOverlapBuilding(origin))
                return false;
            return true;
        }

        private static bool DoesOriginOverlapBuilding(Tile2i origin)
        {
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    if (s_buildingOccupiedTiles.Contains(
                            origin + new RelTile2i(x, y)))
                        return true;
            return false;
        }

        private static void LogV2GroundGraphDiagnostics(
            int vehicleClearance,
            IReadOnlyCollection<Tile2i> groundNodes,
            int towerReachableGroundCount,
            IReadOnlyCollection<Tile2i> towerGoals,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> cleanupByTile,
            IReadOnlyDictionary<Tile2i, string> exclusionReasons)
        {
            int eligibleCleanupTiles = 0;
            int blockedCleanupTiles = 0;
            var cleanupKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (AccessPropCleanupInfo info in cleanupByTile.Values)
            {
                if (info.IsEligible)
                {
                    eligibleCleanupTiles++;
                    for (int index = 0; index < info.Samples.Count; index++)
                        cleanupKeys.Add(info.Samples[index].CleanupObjectKey);
                }
                else if (info.BlockerKind != AccessPropBlockerKind.None)
                    blockedCleanupTiles++;
            }

            string exclusions = string.Join(",", exclusionReasons.Values
                .GroupBy(reason => reason.StartsWith(
                    "DesignatedOrigin@", StringComparison.Ordinal)
                        ? "DesignatedOrigin" : reason)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key}:{group.Count()}"));
            LogExperimentalAccessDebug(
                $"[ATD V2 Ground Graph] vehicleWidth={vehicleClearance} " +
                $"pathableCenters={groundNodes.Count} " +
                $"towerReachableCenters={towerReachableGroundCount} " +
                $"sparseTowerGoals={towerGoals.Count} " +
                $"cleanupEligibleCenters={eligibleCleanupTiles} " +
                $"cleanupBlockedCenters={blockedCleanupTiles} " +
                $"distinctCleanupObjects={cleanupKeys.Count} " +
                $"exclusions=[{exclusions}]");
        }

        private static bool TryGetTierExcavatorPathFindingParams(
            string tierToken,
            out VehiclePathFindingParams pathFindingParams)
        {
            if (s_protosDb != null)
            {
                foreach (ExcavatorProto proto in s_protosDb.All<ExcavatorProto>()
                    .OrderBy(candidate => candidate.Id.Value, StringComparer.Ordinal))
                {
                    if (proto.Id.Value.IndexOf(
                            tierToken, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    pathFindingParams = proto.PathFindingParams;
                    return true;
                }
            }
            pathFindingParams = VehiclePathFindingParams.DEFAULT;
            return false;
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
            Func<Tile2i, bool> isTerrainPathableWithoutBlockers,
            Dictionary<Tile2i, List<AccessPropSample>> samplesByOrigin,
            Dictionary<Tile2i, AccessPropBlockerKind> blockersByOrigin,
            Dictionary<Tile2i, List<AccessPropSample>> samplesByTile,
            Dictionary<Tile2i, AccessPropBlockerKind> blockersByTile)
        {
            if (!samplesByOrigin.TryGetValue(origin, out List<AccessPropSample> samples))
            {
                samples = new List<AccessPropSample>();
                samplesByOrigin[origin] = samples;
            }
            samples.Add(sample);
            if (!samplesByTile.TryGetValue(tile, out List<AccessPropSample> tileSamples))
            {
                tileSamples = new List<AccessPropSample>();
                samplesByTile[tile] = tileSamples;
            }
            tileSamples.Add(sample);

            AccessPropBlockerKind blocker = GetCleanupBlockerKind(
                tower, origin, tile, groundHeight2, designatedOrigins,
                oceanTiles, buildingBlockedGroundTiles, projectedDesignationDisturbance,
                isTerrainPathableWithoutBlockers);
            if (blocker != AccessPropBlockerKind.None
                && (!blockersByOrigin.TryGetValue(origin, out AccessPropBlockerKind existing)
                    || existing == AccessPropBlockerKind.None))
                blockersByOrigin[origin] = blocker;
            if (blocker != AccessPropBlockerKind.None
                && (!blockersByTile.TryGetValue(tile, out AccessPropBlockerKind tileExisting)
                    || tileExisting == AccessPropBlockerKind.None))
                blockersByTile[tile] = blocker;
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

        private static RelTile1i ExtractVehicleClearance(VehiclePathFindingParams pathParams)
        {
            var mask = pathParams.PathabilityQueryMask;
            return ClearancePathabilityProvider.ExtractClearanceFromMask(ref mask);
        }

        private static Func<Tile2i, bool> BuildTerrainOnlyPathabilityPredicate(
            IPathabilityProvider provider,
            Mafi.Numerics.UInt128 pathabilityMask,
            RelTile1i clearance)
        {
            // Each clearance column occupies ten mask bits. Bit zero is the
            // generic TerrainManager.BlocksVehicles flag used by props and
            // trees. Remove only that bit; retain the selected vehicle's slope,
            // height-clearance, ocean, and encoded footprint requirements.
            for (int x = 0; x < clearance.Value; x++)
                pathabilityMask &= ~(
                    Mafi.Numerics.UInt128.One << (x * 10));

            var cache = new Dictionary<Tile2i, bool>();
            return tile =>
            {
                if (!cache.TryGetValue(tile, out bool pathable))
                {
                    pathable = provider.IsPathable(tile, pathabilityMask);
                    cache.Add(tile, pathable);
                }
                return pathable;
            };
        }

        private static AccessPropBlockerKind GetCleanupBlockerKind(
            IAreaManagingTower tower,
            Tile2i origin,
            Tile2i tile,
            IReadOnlyDictionary<Tile2i, int> groundHeight2,
            ISet<Tile2i> designatedOrigins,
            ISet<Tile2i> oceanTiles,
            ISet<Tile2i> buildingBlockedGroundTiles,
            ProjectedDesignationDisturbance projectedDesignationDisturbance,
            Func<Tile2i, bool> isTerrainPathableWithoutBlockers)
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
            if (!isTerrainPathableWithoutBlockers(tile))
                return AccessPropBlockerKind.UnderlyingTerrain;
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
                $"[ATD Access Cleanup] propSamples={diagnostics.PropSamples} " +
                $"treeSamples={diagnostics.TreeSamples} eligibleOrigins={diagnostics.EligibleOrigins} " +
                $"treeOrigins={diagnostics.TreeCleanupOrigins} denseDebrisOrigins={diagnostics.DenseDebrisCleanupOrigins} " +
                $"hardBlockedOrigins={diagnostics.HardBlockedOrigins} blockers=[{blockers}] " +
                $"terrainRemovalPolicyOrigins={diagnostics.TerrainRemovalPolicyOrigins}");
            if (diagnostics.PropSamples + diagnostics.TreeSamples > 2000
                || diagnostics.EligibleOrigins > 64
                || diagnostics.HardBlockedOrigins > 64)
            {
                LogExperimentalAccessDebug(
                    "[ATD Access Cleanup Details] suppressed for large snapshot");
                return;
            }
            if (diagnostics.SampleDetails.Count > 0)
                LogExperimentalAccessTrace(
                    $"[ATD Access Cleanup Samples] {string.Join("; ", diagnostics.SampleDetails)}");
            if (diagnostics.EligibleOriginDetails.Count > 0)
                LogExperimentalAccessTrace(
                    $"[ATD Access Cleanup Eligible Origins] {string.Join("; ", diagnostics.EligibleOriginDetails)}");
            if (diagnostics.BlockedOriginDetails.Count > 0)
                LogExperimentalAccessTrace(
                    $"[ATD Access Cleanup Blocked Origins] {string.Join("; ", diagnostics.BlockedOriginDetails)}");
        }

        private static AccessPathRequest BuildMergedGoalAccessRequest(
            AccessSearchSnapshot snapshot,
            AccessOriginCluster cluster,
            IEnumerable<Tile2i> fixedGoalOrigins,
            float maxCostLimit = float.MaxValue,
            IEnumerable<Tile2i>? groundGoalOverride = null)
        {
            int requiredWidth = snapshot.VehicleWidth > 4 ? 2 : 1;
            List<Tile2i> fixedGoals = fixedGoalOrigins.Distinct().ToList();
            List<Tile2i> groundGoals = (groundGoalOverride
                    ?? snapshot.GoalGroundNodes)
                .Distinct()
                .ToList();
            AccessV2EndpointSet? v2Endpoints = requiredWidth == 2
                ? AccessV2FrontageDiscovery.Build(
                    snapshot,
                    cluster.Origins.Select(origin => origin.Origin))
                : null;
            if (v2Endpoints != null)
            {
                AccessV2FrontageDiagnostics diagnostics = v2Endpoints.Diagnostics;
                string rejections = diagnostics.Rejections.Count == 0
                    ? "none"
                    : string.Join(",", diagnostics.Rejections
                        .OrderByDescending(pair => pair.Value)
                        .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                        .Take(12)
                        .Select(pair => $"{pair.Key}:{pair.Value}"));
                string startSamples = string.Join(";", v2Endpoints.Starts.Take(8)
                    .Select(start =>
                        $"{start.State.Anchor}/{start.State.Axis}/{start.State.EntryDirection}" +
                        (start.IsSourceLaunch
                            ? $"/launch={start.LaunchSuccessor!.Next.Anchor}" +
                                $"/initialGenerated={start.InitialTransition?.Delta.Count ?? 0}" +
                                $"/successorGenerated={start.LaunchSuccessor.Delta.Count}"
                            : "/direct-fixture-start")));
                LogExperimentalAccessDebug(
                    $"[ATD V2 Frontages] seeds={diagnostics.SeedCount} " +
                    $"startTiers={v2Endpoints.StartTiers.Count} " +
                    $"starts={v2Endpoints.Starts.Count} " +
                    $"sourceLaunches={diagnostics.SourceLaunchCount} " +
                    $"directFixtureStarts={diagnostics.DirectFixtureStartCount} " +
                    $"rejections=[{rejections}] startSamples=[{startSamples}] " +
                    $"projectedGoals={fixedGoals.Count}");
            }
            return new AccessPathRequest(
                $"merged-goals-cluster-{cluster.ClusterId}",
                snapshot,
                new AccessPathEndpoint(
                    AccessPathEndpointKind.FixedProfiles,
                    cluster.Origins.Select(origin => origin.Origin)),
                new AccessPathEndpoint(
                    fixedGoals,
                    groundGoals),
                requiredWidth,
                AccessPathIntent.ConstructAccessway,
                maxCostLimit,
                v2Endpoints);
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
            ExperimentalAccessDryRunResult output,
            ExperimentalAccessSliceControl? sliceControl = null)
        {
            AccessSearchSnapshot snapshot = request.Snapshot;
            sliceControl?.ReportPhase("Preparing search request");
            if (sliceControl != null)
                yield return null;
            Stopwatch searchTimer = Stopwatch.StartNew();
            LogExperimentalAccessDebug(
                $"[ATD Access Search Start] request={request.RequestId} cluster={cluster.ClusterId} " +
                $"{FormatAccessPathRequest(request)} cleanupOrigins={snapshot.EligibleCleanupOriginCount}");
            sliceControl?.ReportPhase("Preparing search session");
            AccessPathSearch.AccessPathSearchSessionBuilder sessionBuilder =
                AccessPathSearch.CreateSessionBuilder(request);
            TimeSpan managedProcessingElapsed = TimeSpan.Zero;
            int frames = 0;
            TimeSpan maxSlice = TimeSpan.Zero;
            int lastToastSecond = -1;
            int slowStepLogCount = 0;
            int slowSliceLogCount = 0;

            bool IsCancellationRequested()
                => sliceControl?.CancellationRequested
                    ?? s_cancelExperimentalAccessSearch;

            while (!sessionBuilder.IsComplete
                && !IsCancellationRequested())
            {
                sliceControl?.ReportPhase(sessionBuilder.Phase);
                Stopwatch preparationSliceTimer = Stopwatch.StartNew();
                do
                {
                    sessionBuilder.Advance(maxWorkItems: 64);
                }
                while (!sessionBuilder.IsComplete
                    && !IsCancellationRequested()
                    && preparationSliceTimer.ElapsedMilliseconds
                        < (sliceControl?.SliceBudgetMilliseconds
                            ?? AutoTerrainDesignationsMod
                                .AccessSearchFrameBudgetMs)
                    && (sliceControl == null
                        ? searchTimer.Elapsed.TotalSeconds
                        : (managedProcessingElapsed
                            + preparationSliceTimer.Elapsed).TotalSeconds)
                        < AutoTerrainDesignationsMod
                            .AccessSearchTimeoutSeconds);
                preparationSliceTimer.Stop();
                if (sliceControl != null)
                    managedProcessingElapsed +=
                        preparationSliceTimer.Elapsed;
                if (preparationSliceTimer.Elapsed > maxSlice)
                    maxSlice = preparationSliceTimer.Elapsed;
                frames++;
                sliceControl?.ReportProgress(0, 0);
                if (!sessionBuilder.IsComplete
                    && !IsCancellationRequested()
                    && (sliceControl == null
                        ? searchTimer.Elapsed.TotalSeconds
                        : managedProcessingElapsed.TotalSeconds)
                        < AutoTerrainDesignationsMod
                            .AccessSearchTimeoutSeconds)
                    yield return null;
                if ((sliceControl == null
                        ? searchTimer.Elapsed.TotalSeconds
                        : managedProcessingElapsed.TotalSeconds)
                    >= AutoTerrainDesignationsMod
                        .AccessSearchTimeoutSeconds)
                    break;
            }
            AccessSearchSessionBuildDiagnostics sessionDiagnostics =
                sessionBuilder.Diagnostics;
            LogExperimentalAccessTrace(
                $"[ATD Access Search Preparation] "
                + $"request={request.RequestId} cluster={cluster.ClusterId} "
                + $"totalMs={sessionDiagnostics.TotalMilliseconds.ToString(
                    "0.##", CultureInfo.InvariantCulture)} "
                + $"requestEnvelopeMs={sessionDiagnostics.RequestHeightEnvelopeMilliseconds.ToString(
                    "0.##", CultureInfo.InvariantCulture)} "
                + $"requestGroundGraphMs={sessionDiagnostics.RequestGroundGraphMilliseconds.ToString(
                    "0.##", CultureInfo.InvariantCulture)} "
                + $"potentialFieldMs={sessionDiagnostics.PotentialFieldMilliseconds.ToString(
                    "0.##", CultureInfo.InvariantCulture)} "
                + $"v2SessionMs={sessionDiagnostics.V2SessionMilliseconds.ToString(
                    "0.##", CultureInfo.InvariantCulture)} "
                + $"v1GoalCollectionMs={sessionDiagnostics.V1GoalCollectionMilliseconds.ToString(
                    "0.##", CultureInfo.InvariantCulture)} "
                + $"v1GoalIndexMs={sessionDiagnostics.V1GoalIndexMilliseconds.ToString(
                    "0.##", CultureInfo.InvariantCulture)} "
                + $"v1InitialExpansionMs={sessionDiagnostics.V1InitialExpansionMilliseconds.ToString(
                    "0.##", CultureInfo.InvariantCulture)}");
            if (!sessionBuilder.IsComplete)
            {
                searchTimer.Stop();
                bool cancelledDuringPreparation =
                    IsCancellationRequested();
                AccessSearchResult preparationFailure =
                    new AccessSearchResult(
                        false,
                        cancelledDuringPreparation
                            ? "SearchCancelled"
                            : "SearchTimeLimit",
                        request.Start.Nodes.Count > 0
                            ? request.Start.Nodes[0]
                            : default,
                        Array.Empty<AccessSearchNode>(),
                        0f,
                        0,
                        new Dictionary<string, int>(
                            StringComparer.Ordinal));
                if (sliceControl == null)
                    HideTerrainAnalysisProgressToast();
                TimeSpan preparationElapsed = sliceControl == null
                    ? searchTimer.Elapsed
                    : managedProcessingElapsed;
                AccessDesignationPlan? preparationPlan =
                    RecordExperimentalAccessDryRun(
                        request,
                        cluster,
                        snapshot,
                        preparationFailure,
                        preparationElapsed,
                        frames,
                        maxSlice);
                if (sliceControl != null)
                {
                    LogExperimentalAccessDebug(
                        $"[ATD Access Slice Summary] "
                        + $"request={request.RequestId} "
                        + $"cluster={cluster.ClusterId} "
                        + sliceControl.FormatDiagnostics());
                }
                output.Complete(preparationFailure, preparationPlan);
                yield break;
            }
            AccessPathSearch.AccessPathSearchSession session =
                sessionBuilder.Session;
            if (sliceControl != null)
                yield return null;
            sliceControl?.ReportPhase("Searching");
            if (ShowExperimentalAccessSearchOverlay
                || ShowExperimentalAccessPotentialOverlay)
            {
                BeginExperimentalAccessSearchOverlay();
                if (ShowExperimentalAccessPotentialOverlay)
                    RecordExperimentalAccessPotential(
                        session.V2PotentialSamples);
                if (ShowExperimentalAccessSearchOverlay)
                    session.NodeExplored =
                        RecordExperimentalAccessSearchNode;
            }
            LogExperimentalAccessDebug(
                $"[ATD Access Search Session] request={request.RequestId} cluster={cluster.ClusterId} " +
                $"createMs={sessionDiagnostics.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"complete={session.IsComplete} pending={session.PendingNodes} visited={session.VisitedNodes}");

            while (!session.IsComplete && !IsCancellationRequested())
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
                            $"[ATD Access Search SlowStep] request={request.RequestId} " +
                            $"cluster={cluster.ClusterId} stepMs={stepTimer.Elapsed.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture)} " +
                            $"visited={session.VisitedNodes} pending={session.PendingNodes} " +
                            $"elapsedMs={(sliceControl == null
                                ? searchTimer.Elapsed
                                : managedProcessingElapsed + sliceTimer.Elapsed).TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture)}");
                    }
                }
                while (!session.IsComplete
                    && !IsCancellationRequested()
                    && sliceTimer.ElapsedMilliseconds < (sliceControl?.SliceBudgetMilliseconds
                        ?? AutoTerrainDesignationsMod.AccessSearchFrameBudgetMs)
                    && (sliceControl == null
                        ? searchTimer.Elapsed.TotalSeconds
                        : (managedProcessingElapsed + sliceTimer.Elapsed).TotalSeconds)
                        < AutoTerrainDesignationsMod.AccessSearchTimeoutSeconds);
                sliceTimer.Stop();
                if (sliceControl != null)
                    managedProcessingElapsed += sliceTimer.Elapsed;
                if (sliceTimer.Elapsed > maxSlice) maxSlice = sliceTimer.Elapsed;
                frames++;
                if (sliceControl != null
                    && sliceTimer.Elapsed.TotalMilliseconds
                        >= Math.Max(
                            25d,
                            sliceControl.SliceBudgetMilliseconds * 2d)
                    && slowSliceLogCount < 8)
                {
                    slowSliceLogCount++;
                    LogExperimentalAccessDebug(
                        $"[ATD Access SlowSlice] "
                        + $"request={request.RequestId} "
                        + $"cluster={cluster.ClusterId} "
                        + $"phase={sliceControl.Phase} "
                        + $"frame={frames} "
                        + $"budgetMs={sliceControl.SliceBudgetMilliseconds} "
                        + $"sliceMs={sliceTimer.Elapsed.TotalMilliseconds.ToString(
                            "0.##", CultureInfo.InvariantCulture)} "
                        + $"visited={session.VisitedNodes} "
                        + $"pending={session.PendingNodes}");
                }
                TimeSpan elapsed = sliceControl == null
                    ? searchTimer.Elapsed
                    : managedProcessingElapsed;
                int elapsedSeconds = Math.Min(
                    AutoTerrainDesignationsMod.AccessSearchTimeoutSeconds,
                    (int)Math.Floor(elapsed.TotalSeconds));
                sliceControl?.ReportProgress(
                    session.VisitedNodes,
                    session.PendingNodes);
                if (elapsedSeconds != lastToastSecond)
                {
                    if (sliceControl == null)
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
                if (elapsed.TotalSeconds
                        >= AutoTerrainDesignationsMod.AccessSearchTimeoutSeconds
                    && !session.IsComplete)
                {
                    break;
                }
            }

            searchTimer.Stop();
            AccessSearchResult result = IsCancellationRequested()
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
            if (sliceControl == null)
                HideTerrainAnalysisProgressToast();
            TimeSpan recordedElapsed = sliceControl == null
                ? searchTimer.Elapsed
                : managedProcessingElapsed;
            AccessDesignationPlan? plan = RecordExperimentalAccessDryRun(
                request, cluster, snapshot, result, recordedElapsed,
                frames, maxSlice);
            if (sliceControl != null)
            {
                LogExperimentalAccessDebug(
                    $"[ATD Access Slice Summary] "
                    + $"request={request.RequestId} cluster={cluster.ClusterId} "
                    + sliceControl.FormatDiagnostics());
            }
            output.Complete(result, plan);
        }

        private static void ShowTerrainAnalysisProgressToast(
            int clusterIndex,
            int clusterCount,
            int elapsedSeconds,
            int visitedNodes,
            int pendingNodes)
        {
            if (s_uiRoot == null || s_terrainAnalysisToastHidden) return;
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
                        $"[ATD] Finding path {clusterIndex}/{clusterCount}, " +
                        $"visited {visitedNodes:N0}/{nodeLimit:N0} · queue {pendingNodes:N0} · " +
                        $"{elapsedSeconds}/{timeLimit}s"))
                        .FontSize(16),
                    new ButtonText(
                        Button.General,
                        new LocStrFormatted("Cancel"),
                        () => s_cancelExperimentalAccessSearch = true)
                        .MarginLeft(8.pt()),
                    new ButtonText(
                        Button.General,
                        new LocStrFormatted("Hide"),
                        HideTerrainAnalysisProgressToastUntilComplete)
                        .MarginLeft(8.pt()));
            }
            catch
            {
            }
        }

        private static void HideTerrainAnalysisProgressToast()
        {
            TryResetTerrainAnalysisToastHidden();
            try
            {
                s_uiRoot?.ToastNotifProvider.m_notification.Hide();
            }
            catch
            {
            }
        }

        private static void HideTerrainAnalysisProgressToastUntilComplete()
        {
            HideTerrainAnalysisToastForCurrentSearch();
            try
            {
                s_uiRoot?.ToastNotifProvider.m_notification.Hide();
            }
            catch
            {
            }
        }

        private static void HideTerrainAnalysisToastForCurrentSearch()
        {
            s_terrainAnalysisToastHidden = true;
            s_terrainAnalysisToastHiddenUntilSeconds =
                GetTerrainAnalysisToastRealtimeSeconds()
                + TerrainAnalysisToastMinimumHideSeconds;
        }

        private static void TryResetTerrainAnalysisToastHidden()
        {
            if (GetTerrainAnalysisToastRealtimeSeconds()
                < s_terrainAnalysisToastHiddenUntilSeconds)
                return;

            s_terrainAnalysisToastHidden = false;
            s_terrainAnalysisToastHiddenUntilSeconds = 0d;
        }

        private static double GetTerrainAnalysisToastRealtimeSeconds()
            => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private static AccessDesignationPlan? RecordExperimentalAccessDryRun(
            AccessPathRequest request,
            AccessOriginCluster cluster,
            AccessSearchSnapshot snapshot,
            AccessSearchResult result,
            TimeSpan elapsed,
            int frames,
            TimeSpan maxSlice)
        {
            LastExperimentalAccessSearch = result;
            RecordV2PathabilityOverlay(snapshot, result);
            AccessSearchDiagnostics diag = result.Diagnostics;
            if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Info)
                && !AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Debug))
            {
                string conciseReason = string.IsNullOrEmpty(result.FailureReason)
                    ? "none"
                    : result.FailureReason;
                LogInfo(
                    $"[ATD Access] request={request.RequestId} cluster={cluster.ClusterId} " +
                    $"width={request.RequiredWidth} success={result.Success} reason={conciseReason} " +
                    $"visited={result.VisitedNodes} pathNodes={result.Path.Count} " +
                    $"searchMs={elapsed.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture)}");
            }
            if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Debug))
            {
                string rejections = result.Rejections.Count == 0
                ? "none"
                : string.Join(",", result.Rejections
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}:{pair.Value}"));
            string reason = string.IsNullOrEmpty(result.FailureReason) ? "none" : result.FailureReason;
            string cost = result.Cost.ToString("0.##", CultureInfo.InvariantCulture);
            string searchMs = elapsed.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture);
            string maxSliceMs = maxSlice.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture);
            string landslideRun = snapshot.LandslideRunPerHeight.ToString("0.##", CultureInfo.InvariantCulture);
                double ticksToMs = 1000d / Stopwatch.Frequency;
                string diagnostics =
                $"expansions=[G:{diag.GroundExpansions},V:{diag.OriginExpansions}] " +
                $"ground=[checks:{diag.GroundSuccessorChecks},relax:{diag.GroundRelaxations},cleanupChecks:{diag.CleanupGroundSuccessorChecks},cleanupRelax:{diag.CleanupGroundRelaxations},suffix:{diag.V1GroundSuffixSuccesses}/{diag.V1GroundSuffixAttempts},suffixFallback:{diag.V1GroundSuffixFallbacks},suffixSteps:{diag.V1GroundSuffixSteps}] " +
                $"generated=[neighbors:{diag.OriginNeighborChecks},modes:{diag.GeneratedModeAttempts},g2vOrigins:{diag.GroundToGeneratedOriginChecks},g2vProfiles:{diag.GroundToGeneratedProfileAttempts},g2vNoHandoff:{diag.GroundToGeneratedHandoffFailures},g2vDirectLevel:{diag.V1GroundToVDirectLevelingAccepts},relax:{diag.GeneratedRelaxations}] " +
                $"profile=[checks:{diag.GeneratedProfileFeasibleChecks},fail:{diag.GeneratedProfileFeasibleFailures},historyFail:{diag.GeneratedPathHistoryFailures}] " +
                $"hull=[checks:{diag.HeightEnvelopeChecks},above:{diag.HeightEnvelopeAboveRejections},below:{diag.HeightEnvelopeBelowRejections},missing:{diag.HeightEnvelopeMissingSamples}] " +
                $"sideRay=[checks:{diag.SideRayCostChecks},reject:{diag.SideRayCostRejections},samples:{diag.SideRayCostSamples},cacheHit:{diag.SideRayCacheHits},cacheMiss:{diag.SideRayCacheMisses},historyReuse:{diag.GeneratedHistoryCostReuses},historyRecalc:{diag.GeneratedHistoryCostRecalculations}] " +
                $"history=[created:{diag.GeneratedHistoryNodesCreated},maxDepth:{diag.GeneratedHistoryMaxDepth}] " +
                $"prop=[checks:{diag.PropCleanupChecks},hits:{diag.PropCleanupHits},reject:{diag.PropCleanupRejections}] " +
                $"fixed=[checks:{diag.FixedProfileSuccessorChecks},relax:{diag.FixedProfileRelaxations}] " +
                $"goals=[pops:{diag.GoalPops},rejected:{diag.GoalRejected},acceptedAt:{diag.GoalAcceptedAtVisited}] " +
                $"queue=[relax:{diag.QueueRelaxations},stale:{diag.QueueStalePops}] " +
                $"v1HandoffDominance=[success:{diag.V1HandoffDominanceSuccesses},prune:{diag.V1HandoffDominancePrunes}] " +
                $"timingMs=[ground:{(diag.GroundExpansionTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                $"origin:{(diag.OriginExpansionTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                $"profile:{(diag.ProfileFeasibilityTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                $"handoff:{(diag.HandoffValidationTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                $"history:{(diag.PathHistoryTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                $"sideRay:{(diag.SideRayCostTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                $"prop:{(diag.PropCleanupTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}]";
                if (request.RequiredWidth == 2)
                {
                    diagnostics +=
                    $" v2Expand=[G:{diag.V2GroundExpansions},V:{diag.V2BandExpansions}] " +
                    $"v2Potential=[generated:{diag.V2PotentialGeneratedNodes},fixed:{diag.V2PotentialFixedNodes}," +
                    $"escapeComponents:{diag.V2PotentialGroundComponents},buildMs:{(diag.V2PotentialBuildTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}] " +
                    $"v2StartTiers=[attempted:{diag.V2StartTiersAttempted},redundantSkipped:{diag.V2RedundantStartTiersSkipped},seedsSkipped:{diag.V2RedundantStartSeedsSkipped}] " +
                    $"v2LabelDominance=[early:{diag.V2EarlyLabelDominancePrunes},exact:{diag.V2ExactLabelDominancePrunes}] " +
                    $"[DEBUG-v2-frontier] v2ExpansionLabels=[first:{diag.V2LabelFirstExpansions},reopen:{diag.V2LabelReexpansions}," +
                    $"queueAgeAvg:{(diag.V2LabelFirstExpansions + diag.V2LabelReexpansions > 0 ? (double)diag.V2ExpansionQueueAgeTotal / (diag.V2LabelFirstExpansions + diag.V2LabelReexpansions) : 0d).ToString("0.##", CultureInfo.InvariantCulture)}," +
                    $"queueAgeMax:{diag.V2ExpansionQueueAgeMax},uniqueVCenters:{diag.V2UniqueExpansionCenters},centerAliases:{diag.V2CenterAliasedFirstExpansions}," +
                    $"initialV:{diag.V2InitialVExpansions},groundRelaunchedV:{diag.V2GroundRelaunchedVExpansions}] " +
                    $"[DEBUG-v2-frontier] v2ShallowV=[expanded:{diag.V2ShallowVExpansions},reopen:{diag.V2ShallowVReexpansions}," +
                    $"groundRelaunched:{diag.V2ShallowGroundRelaunchedVExpansions}," +
                    $"queueAgeAvg:{(diag.V2ShallowVExpansions > 0 ? (double)diag.V2ShallowVQueueAgeTotal / diag.V2ShallowVExpansions : 0d).ToString("0.##", CultureInfo.InvariantCulture)}," +
                    $"queueAgeMax:{diag.V2ShallowVQueueAgeMax}] " +
                    $"v2RayOverlay=[hit:{diag.V2RayOverlayCacheHits},miss:{diag.V2RayOverlayCacheMisses}," +
                    $"parentSteps:{diag.V2RayOverlayParentSteps},cacheEntries:{diag.V2RayOverlayCacheEntries}," +
                    $"maxRaw:{diag.V2RayOverlayMaxRawConstraints},maxCollapsed:{diag.V2RayOverlayMaxCollapsedEntries}] " +
                    $"v2Suffix=[attempts:{diag.V2GroundSuffixAttempts},success:{diag.V2GroundSuffixSuccesses}," +
                    $"fallback:{diag.V2GroundSuffixFallbacks},steps:{diag.V2GroundSuffixSteps}] " +
                    $"v2G2V=[calls:{diag.V2GroundToVCalls},areaReject:{diag.V2GroundToVTowerAreaRejects},seeds:{diag.V2GroundToVSeedCalls}," +
                    $"extensions:{diag.V2GroundToVSeedExtensions},anchors:{diag.V2GroundToVAnchorCandidates}," +
                    $"profiles:{diag.V2GroundToVProfileCandidates}," +
                    $"directLevel:{diag.V2GroundToVDirectLevelingAccepts}," +
                    $"rough:{diag.V2GroundToVRoughAccepts},cacheHit:{diag.V2GroundToVCacheHits}," +
                    $"cacheAdd:{diag.V2GroundToVCacheInsertions},face:{diag.V2GroundToVFaceChecks}," +
                    $"faceReject:{diag.V2GroundToVFaceRejects},steps:{diag.V2GroundToVBridgeSteps}," +
                    $"stepReject:{diag.V2GroundToVBridgeRejects},propReject:{diag.V2GroundToVPropRejects}] " +
                    $"v2Handoff=[evaluations:{diag.V2HandoffEvaluations},quick:{diag.V2QuickHandoffAccepts}," +
                    $"dominanceSuccess:{diag.V2HandoffDominanceSuccesses},dominancePrune:{diag.V2HandoffDominancePrunes}," +
                    $"pairs:{diag.V2HandoffPairChecks},mixedRejected:{diag.V2MixedLanePairRejects}," +
                    $"leveling:{diag.V2LevelingBridgeAccepts}," +
                    $"corridors:{diag.V2CorridorAttempts},centerChecks:{diag.V2CorridorCenterChecks}," +
                    $"bfsPops:{diag.V2CorridorBfsPops}] " +
                    $"v2TimingMs=[G:{(diag.V2GroundExpansionTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                    $"V:{(diag.V2BandExpansionTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                    $"suffix:{(diag.V2GroundSuffixTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                    $"g2v:{(diag.V2GroundToVTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                    $"transition:{(diag.V2TransitionEvaluationTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                    $"handoff:{(diag.V2HandoffEvaluationTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                    $"lane:{(diag.V2HandoffLaneEvaluationTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                    $"corridor:{(diag.V2CorridorTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}," +
                    $"localEscape:{(diag.V2LocalEscapeTicks * ticksToMs).ToString("0.##", CultureInfo.InvariantCulture)}]";
                }
                LogExperimentalAccessDebug(
                $"[ATD Access] request={request.RequestId} " +
                $"{FormatAccessPathRequest(request)} cluster={cluster.ClusterId} " +
                $"algorithm={(request.RequiredWidth == 2 ? AccessPathSearch.ShouldUseV2AStar(request) ? "A*" : "Dijkstra" : snapshot.UseAStar ? "A*" : "Dijkstra")} " +
                $"success={result.Success} reason={reason} start=({result.StartOrigin.X},{result.StartOrigin.Y}) " +
                $"goals={request.Goal.Nodes.Count} reachedGoal={result.ReachedGoalKind} landslideRun={landslideRun} " +
                $"landslideSources={snapshot.LandslideSourceCount} cost={cost} " +
                $"visited={result.VisitedNodes} pathNodes={result.Path.Count} frames={frames} " +
                $"searchMs={searchMs} maxSliceMs={maxSliceMs} " +
                $"{diagnostics} " +
                    $"rejections=[{rejections}]");
            }
            if (diag.StartSuccessorDetails.Count > 0)
                LogExperimentalAccessTrace(
                    $"[ATD Access Start Successors] cluster={cluster.ClusterId} " +
                    string.Join("; ", diag.StartSuccessorDetails));
            if (diag.FirstGeneratedHandoffDetails.Count > 0)
                LogExperimentalAccessTrace(
                    $"[ATD Access First Handoffs] cluster={cluster.ClusterId} " +
                    string.Join("; ", diag.FirstGeneratedHandoffDetails));
            if (diag.V2RouteHandoffDetails.Count > 0)
                LogExperimentalAccessTrace(
                    $"[ATD V2 Route Handoffs] cluster={cluster.ClusterId} " +
                    string.Join("; ", diag.V2RouteHandoffDetails));
            if (diag.V2GroundSuffixDetails.Count > 0)
                LogExperimentalAccessTrace(
                    $"[ATD V2 Ground Suffix] cluster={cluster.ClusterId} " +
                    string.Join("; ", diag.V2GroundSuffixDetails));
            if (diag.V2VPrimeAdapterDetails.Count > 0)
                LogExperimentalAccessDebug(
                    $"[ATD V2 VPrime Adapter] cluster={cluster.ClusterId} " +
                    string.Join("; ", diag.V2VPrimeAdapterDetails));
            if (!string.IsNullOrEmpty(diag.V2DryRunSummary))
                LogExperimentalAccessDebug(
                    $"[ATD V2 Search] cluster={cluster.ClusterId} " +
                    diag.V2DryRunSummary);
            if (!string.IsNullOrEmpty(diag.V2DryRunPath))
                LogExperimentalAccessTrace(
                    $"[ATD V2 Search Path] cluster={cluster.ClusterId} " +
                    diag.V2DryRunPath);
            AccessDesignationPlan? completedPlan = null;
            if (result.Success)
            {
                if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                    LogExperimentalAccessTrace($"[ATD Access Path] cluster={cluster.ClusterId} {FormatExperimentalPath(result)}");
                LogExperimentalNonGoalGroundDiagnostics(snapshot, result, cluster.ClusterId);
                LogExperimentalSelectedVToGHandoffDiagnostics(snapshot, result, cluster.ClusterId);
                long materializeStart = AtdDiagnostics.Timestamp();
                AccessDesignationPlan plan = AccessPathMaterializer.Materialize(snapshot, result);
                completedPlan = plan;
                LastExperimentalAccessPlan = plan;
                if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Debug))
                {
                    string materializeMs = (AtdDiagnostics.ElapsedSince(materializeStart)
                        * 1000d / Stopwatch.Frequency).ToString("0.##", CultureInfo.InvariantCulture);
                    float selectedSideRayCost = result.LeftSideRayCost
                        + result.RightSideRayCost
                        + result.SideRayUnresolvedPenalty;
                    float selectedCenterOnlyCost = result.Cost
                        - AccessPathSearch.SideRayWeight * selectedSideRayCost;
                    LogExperimentalAccessDebug(
                    $"[ATD Access Plan] cluster={cluster.ClusterId} valid={plan.IsValid} " +
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
                }
                if (plan.IsValid)
                {
                    if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                        LogExperimentalAccessTrace($"[ATD Access Plan Tiles] cluster={cluster.ClusterId} {FormatExperimentalPlan(plan)}");
                    LogExperimentalCleanupRouteDiagnostics(snapshot, result, plan, cluster.ClusterId);
                }
            }
            else
            {
                LastExperimentalAccessPlan = null;
                if (result.Path.Count > 0)
                    if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                        LogExperimentalAccessTrace($"[ATD Access Rejected Path] cluster={cluster.ClusterId} {FormatExperimentalPath(result)}");
            }
            return completedPlan;
        }

        private static void LogExperimentalNonGoalGroundDiagnostics(
            AccessSearchSnapshot snapshot,
            AccessSearchResult result,
            int clusterId)
        {
            if (!AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                return;
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
            LogExperimentalAccessTrace(
                $"[ATD Access NonGoalGround] cluster={clusterId} " +
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
            if (!AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                return;
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
                    out AccessHandoffOperation operation, out _, out _))
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

            foreach (AccessSearchNode node in result.Path)
                if (node.IsGround && node.HandoffSpanLength > 1)
                    details.Add(
                        $"multiCellHandoff=({node.Position.X},{node.Position.Y})" +
                        $":{node.HandoffOperation}:span={node.HandoffSpanLength}");

            if (details.Count > 0)
                LogExperimentalAccessTrace(
                    $"[ATD Access Selected VToG] cluster={clusterId} " +
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
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByTile,
            HashSet<Tile2i> terrainPathableWithoutProps,
            int vehicleClearance,
            Dictionary<string, IReadOnlyList<AccessGroundHandoff>> handoffCache,
            bool useV2CornerCrestRule = false,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo>?
                propCleanupByOrigin = null)
        {
            if (((profile.Nw2 | profile.Ne2 | profile.Se2 | profile.Sw2) & 1) != 0)
                return Array.Empty<AccessGroundHandoff>();

            int handoffEdge;
            AccessHandoffOperation operation;
            uint fulfilledBitmap;
            string directionalDiagnostic;
            bool selected = useV2CornerCrestRule
                ? TryGetV2DirectionalCornerCrestHandoff(
                    origin, profile, predecessorOrigin, terrMgr,
                    terrainPathableWithoutProps.Contains,
                    out handoffEdge, out operation, out fulfilledBitmap,
                    out directionalDiagnostic)
                : TryGetDirectionalHandoff(
                    origin, profile, predecessorOrigin, terrMgr,
                    vehicleClearance,
                    out handoffEdge, out operation, out fulfilledBitmap,
                    out directionalDiagnostic);
            if (!selected)
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

            for (int offset = 0; offset <= 4; offset++)
            {
                if (!IsClearanceValidHandoffLane(offset, vehicleClearance))
                    continue;

                GetHandoffLaneCoordinates(
                    handoffEdge, offset,
                    out int edgeX, out int edgeY,
                    out int firstRankX, out int firstRankY);
                Tile2i perimeterTile = origin + new RelTile2i(edgeX, edgeY);
                bool perimeterFulfilled =
                    (fulfilledBitmap & GetDesignationMask(edgeX, edgeY)) != 0;
                if (useV2CornerCrestRule)
                {
                    // The V2 crest proves rank one.  Operation-specific
                    // post-work pathability begins at rank two and is checked
                    // across the complete paired corridor by AccessV2Handoffs;
                    // pre-work G admission at this boundary sample would
                    // incorrectly reject a cell that its handoff operation
                    // makes usable.
                    TryAddHandoff(
                        perimeterTile,
                        perimeterFulfilled,
                        "v2-seam",
                        new[] { perimeterTile });
                    continue;
                }
                if (vehicleClearance > 0)
                {
                    // The seam establishes the first outward rank.  V1 may
                    // enter G only from either middle tile of rank two, so the
                    // normal ground search proves that the handoff actually
                    // has somewhere to go instead of accepting a lone seam
                    // contact.
                    if (!IsSecondRankMiddleLane(offset))
                        continue;
                    RelTile2i outward = GetHandoffOutwardDirection(handoffEdge);
                    Tile2i firstRankTile = origin + new RelTile2i(
                        firstRankX, firstRankY);
                    Tile2i secondRankTile = firstRankTile - outward;
                    Tile2i exteriorTile = perimeterTile + outward;
                    bool secondRankPathable = IsV1PostWorkHandoffTilePathable(
                        origin, profile, terrMgr, operation, secondRankTile,
                        groundNodes, propCleanupByTile,
                        terrainPathableWithoutProps);
                    bool exteriorGround = IsExperimentalAccessGroundOrCleanupCenter(
                        groundNodes, propCleanupByTile, exteriorTile);
                    if (exteriorGround)
                        groundCandidateCount++;
                    TryAddHandoff(
                        exteriorTile,
                        perimeterFulfilled && secondRankPathable && exteriorGround,
                        "rank2",
                        new[] { secondRankTile, firstRankTile, exteriorTile });
                    continue;
                }

                bool perimeterGround = IsPostWorkHandoffGroundCenter(
                    groundNodes, propCleanupByTile,
                    perimeterTile, operation);
                if (perimeterGround)
                    groundCandidateCount++;
                TryAddHandoff(
                    perimeterTile,
                    perimeterGround && perimeterFulfilled,
                    "perimeter",
                    new[] { perimeterTile });

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

        private static IReadOnlyList<AccessGroundHandoff> BuildProspectiveWorkableHandoffSpan(
            IReadOnlyList<AccessHandoffSpanCell> cells,
            TerrainManager terrMgr,
            HashSet<Tile2i> groundNodes,
            HashSet<Tile2i> goalGroundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByTile,
            HashSet<Tile2i> terrainPathableWithoutProps,
            int vehicleClearance,
            bool validateEverySpanCell,
            Dictionary<string, IReadOnlyList<AccessGroundHandoff>> handoffCache,
            bool useV2CornerCrestRule = false,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo>?
                propCleanupByOrigin = null)
        {
            if (cells.Count < 2)
                return Array.Empty<AccessGroundHandoff>();

            Tile2i direction = cells[0].EntryDirection;
            if (!IsCardinalDesignationStep(direction))
                return Array.Empty<AccessGroundHandoff>();
            for (int index = 1; index < cells.Count; index++)
                if (cells[index].EntryDirection != direction
                    || cells[index].Origin != new Tile2i(
                        cells[index - 1].Origin.X + direction.X,
                        cells[index - 1].Origin.Y + direction.Y))
                    return Array.Empty<AccessGroundHandoff>();

            string cacheKey = string.Join("|", cells.Select(cell =>
                $"{cell.Origin.X},{cell.Origin.Y}:" +
                $"{cell.Profile.Nw2},{cell.Profile.Ne2},{cell.Profile.Se2},{cell.Profile.Sw2}"));
            if (handoffCache.TryGetValue(cacheKey, out IReadOnlyList<AccessGroundHandoff> cached))
                return cached;

            AccessHandoffSpanCell last = cells[cells.Count - 1];
            Tile2i lastPredecessor = new Tile2i(
                last.Origin.X - direction.X,
                last.Origin.Y - direction.Y);
            if (!TryGetConnectedAndHandoffCorners(
                    last.Origin, lastPredecessor,
                    out _, out _, out _, out _, out int handoffEdge))
                return Array.Empty<AccessGroundHandoff>();
            int[] handoffSigns = GetEdgeHeightSigns(
                last.Origin, last.Profile, handoffEdge,
                terrMgr, collectDeltas: false, out _);
            bool useCornerSeamRule = vehicleClearance > 0;
            AccessHandoffOperation operation;
            uint fulfilledBitmap = 0;
            bool selected;
            if (useV2CornerCrestRule)
            {
                AccessHandoffSpanCell first = cells[0];
                selected = TrySelectV2CornerCrestHandoff(
                    first.Origin, first.Profile,
                    last.Origin, last.Profile,
                    handoffEdge, terrMgr,
                    terrainPathableWithoutProps.Contains,
                    out operation, out fulfilledBitmap, out _);
            }
            else if (useCornerSeamRule)
                selected = TrySelectCornerSeamHandoff(
                    last.Origin, last.Profile, handoffEdge, terrMgr,
                    out operation, out fulfilledBitmap);
            else
                selected = TrySelectHandoffOperationFromEdge(
                    handoffSigns, out operation);
            if (!selected
                || (operation != AccessHandoffOperation.Mining
                    && operation != AccessHandoffOperation.Dumping
                    && operation != AccessHandoffOperation.Leveling))
                return Array.Empty<AccessGroundHandoff>();
            // V1 materializes every cell in a terminal span with the same
            // mining/dumping proto. Validating only the outermost cell let the
            // placement fallback assign that proto to an earlier, incompatible
            // cell (or one with no work at all). V2 carries per-lane terminal
            // ownership and retains its existing span semantics.
            int validationStart = validateEverySpanCell ? 0 : cells.Count - 1;
            for (int index = validationStart; index < cells.Count; index++)
            {
                if (!useCornerSeamRule && !useV2CornerCrestRule)
                {
                    if (!HasVanillaWorkableDesignation(
                            cells[index].Origin, cells[index].Profile,
                            operation, terrMgr,
                            out uint cellFulfilledBitmap))
                        return Array.Empty<AccessGroundHandoff>();
                    if (index == cells.Count - 1)
                        fulfilledBitmap = cellFulfilledBitmap;
                }
            }

            var result = new List<AccessGroundHandoff>();
            var emitted = new HashSet<Tile2i>();
            for (int offset = 0; offset <= 4; offset++)
            {
                if (!IsClearanceValidHandoffLane(offset, vehicleClearance))
                    continue;
                GetHandoffLaneCoordinates(
                    handoffEdge, offset,
                    out int edgeX, out int edgeY,
                    out int firstRankX, out int firstRankY);
                Tile2i perimeter = last.Origin + new RelTile2i(edgeX, edgeY);
                bool fulfilled =
                    (fulfilledBitmap & GetDesignationMask(edgeX, edgeY)) != 0;
                if (useV2CornerCrestRule)
                {
                    TryAdd(perimeter,
                        fulfilled ? new[] { perimeter } : null);
                    continue;
                }
                if (vehicleClearance > 0)
                {
                    if (!IsSecondRankMiddleLane(offset))
                        continue;
                    RelTile2i outward = GetHandoffOutwardDirection(handoffEdge);
                    Tile2i firstRankTile = last.Origin + new RelTile2i(
                        firstRankX, firstRankY);
                    Tile2i secondRankTile = firstRankTile - outward;
                    Tile2i exteriorTile = perimeter + outward;
                    bool secondRankPathable = IsV1PostWorkHandoffTilePathable(
                        last.Origin, last.Profile, terrMgr, operation,
                        secondRankTile, groundNodes, propCleanupByTile,
                        terrainPathableWithoutProps);
                    TryAdd(exteriorTile,
                        fulfilled && secondRankPathable
                            && IsExperimentalAccessGroundOrCleanupCenter(
                                groundNodes, propCleanupByTile, exteriorTile)
                            ? new[] { secondRankTile, firstRankTile, exteriorTile }
                            : null);
                    continue;
                }
                TryAdd(perimeter,
                    fulfilled && IsPostWorkHandoffGroundCenter(
                        groundNodes, propCleanupByTile,
                        perimeter, operation)
                        ? new[] { perimeter }
                        : null);
            }

            handoffCache[cacheKey] = result.ToArray();
            return handoffCache[cacheKey];

            void TryAdd(Tile2i tile, IReadOnlyList<Tile2i>? escape)
            {
                if (escape == null || !emitted.Add(tile))
                    return;
                result.Add(new AccessGroundHandoff(
                    tile, operation, escape, cells.Count));
            }

        }

        private static bool IsCardinalDesignationStep(Tile2i direction)
            => (Math.Abs(direction.X) == 4 && direction.Y == 0)
                || (Math.Abs(direction.Y) == 4 && direction.X == 0);

        private static bool HasVanillaWorkableDesignation(
            Tile2i origin,
            AccessHeightProfile profile,
            AccessHandoffOperation operation,
            TerrainManager terrMgr,
            out uint fulfilledBitmap)
        {
            fulfilledBitmap = 0;
            TerrainDesignationProto? proto = operation == AccessHandoffOperation.Mining
                ? s_miningProto : operation == AccessHandoffOperation.Dumping ? s_dumpingProto : null;
            if (proto == null || s_desigManager == null)
                return false;
            var data = new DesignationData(origin,
                new HeightTilesI(profile.Nw2 / 2),
                new HeightTilesI(profile.Ne2 / 2),
                new HeightTilesI(profile.Se2 / 2),
                new HeightTilesI(profile.Sw2 / 2));
            // The selected operation describes the outward handoff edge, not
            // every sample in the profile.  Mixed work within the 4x4 body is
            // legal when vanilla declares the selected mining/dumping
            // designation fulfilled and ready.
            if (!TryBuildProspectiveFulfilledBitmap(
                    proto, terrMgr, data, operation, out fulfilledBitmap))
                return false;
            return fulfilledBitmap != ALL_DESIGNATION_TILES_MASK
                && (fulfilledBitmap & READY_PERIMETER_MASK) != 0;
        }

        private static IReadOnlyList<Tile2i>? FindPostWorkGroundPathOutOfHandoffSpan(
            IReadOnlyList<AccessHandoffSpanCell> cells,
            Tile2i adjacentVOrigin,
            Tile2i start,
            HashSet<Tile2i> groundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByOrigin)
        {
            if (!IsExperimentalAccessGroundOrCleanupNode(
                    groundNodes, propCleanupByOrigin, start))
                return null;

            // This is a post-work route.  A sample inside the terminal span may
            // currently require the very mining/dumping operation that creates
            // the handoff surface; rejecting it here made an interior crest
            // impossible to use.  It still has to be a captured G node, so
            // projected main-body work and all ordinary pathability exclusions
            // remain blocked.

            var queue = new Queue<Tile2i>();
            var visited = new HashSet<Tile2i> { start };
            var parent = new Dictionary<Tile2i, Tile2i>();
            queue.Enqueue(start);
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
                    bool insideSpan = cells.Any(cell => IsInsideVFootprint(next, cell.Origin));
                    bool insideAdjacent = IsInsideVFootprint(next, adjacentVOrigin);
                    if (!IsExperimentalAccessGroundOrCleanupNode(
                            groundNodes, propCleanupByOrigin, next))
                        continue;
                    if (!insideSpan && !insideAdjacent)
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
                    if (insideAdjacent || !visited.Add(next))
                        continue;
                    parent[next] = current;
                    queue.Enqueue(next);
                }
            }
            return null;
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
            LogExperimentalAccessTrace(
                    $"[ATD Access Handoff Diagnostic] origin=({origin.X},{origin.Y}) " +
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
            // V2 passes zero because its vehicle clearance is proved by the G
            // graph. Vanilla workability accepts every perimeter sample there;
            // excluding corners can hide the only continuous contact between
            // two adjacent V2 lanes. V1 retains its one-cell clearance rule.
            if (vehicleClearance <= 0)
                return offset >= 0 && offset <= 4;
            if (vehicleClearance > 4)
                return false;
            int sideMargin = Math.Min(2, Math.Max(0, (vehicleClearance - 1) / 2));
            return offset > 0 && offset < 4
                && offset >= sideMargin && offset < 4 - sideMargin;
        }

        private static bool IsSecondRankMiddleLane(int offset)
            => offset == 1 || offset == 2;

        private static bool IsV1PostWorkHandoffTilePathable(
            Tile2i origin,
            AccessHeightProfile profile,
            TerrainManager terrMgr,
            AccessHandoffOperation operation,
            Tile2i tile,
            HashSet<Tile2i> groundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByTile,
            HashSet<Tile2i> terrainPathableWithoutProps)
        {
            // The rank-two tile is within the terminal designation. Side-ray
            // fills never enter this overlay. Leveling produces a drivable
            // surface; mining may ignore props or use a genuine cut; dumping
            // needs normal (including removable-prop cleanup) pathability or
            // a genuine fill in this exact designation tile.
            if (operation == AccessHandoffOperation.Leveling)
                return true;
            if (operation == AccessHandoffOperation.Mining)
                return terrainPathableWithoutProps.Contains(tile)
                    || IsHandoffWorkTile(origin,
                        CreateDesignationData(origin, profile), terrMgr,
                        operation, tile);
            if (operation == AccessHandoffOperation.Dumping)
            {
                if (IsExperimentalAccessGroundOrCleanupCenter(
                        groundNodes, propCleanupByTile, tile))
                    return true;
                if (!IsHandoffWorkTile(origin,
                        CreateDesignationData(origin, profile), terrMgr,
                        operation, tile))
                    return false;
                if (!propCleanupByTile.TryGetValue(
                        tile, out AccessPropCleanupInfo cleanup))
                    return true;
                var checkedProps = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < cleanup.Samples.Count; index++)
                {
                    AccessPropSample sample = cleanup.Samples[index];
                    if (sample.IsDenseDebris
                        && checkedProps.Add(sample.CleanupObjectKey)
                        && !AccessSearchSnapshot.DoesDumpingBuryProp(
                            origin, profile, sample))
                        return false;
                }
                return true;
            }
            return false;
        }

        private static DesignationData CreateDesignationData(
            Tile2i origin,
            AccessHeightProfile profile)
            => new DesignationData(origin,
                new HeightTilesI(profile.Nw2 / 2),
                new HeightTilesI(profile.Ne2 / 2),
                new HeightTilesI(profile.Se2 / 2),
                new HeightTilesI(profile.Sw2 / 2));

        internal static void GetHandoffLaneCoordinates(
            int handoffEdge,
            int offset,
            out int edgeX,
            out int edgeY,
            out int insideX,
            out int insideY)
        {
            edgeX = handoffEdge == 0 ? 0 : handoffEdge == 1 ? 4 : offset;
            edgeY = handoffEdge == 2 ? 0 : handoffEdge == 3 ? 4 : offset;
            RelTile2i outward = GetHandoffOutwardDirection(handoffEdge);
            insideX = edgeX - (outward.X > 0 ? 1 : 0);
            insideY = edgeY - (outward.Y > 0 ? 1 : 0);
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
                    groundNodes, propCleanupByOrigin, start))
                return null;

            // The terminal designation is evaluated after its own work has
            // completed.  Its crossed samples are therefore part of the new
            // drivable surface, provided the underlying snapshot already admits
            // them as G nodes.  Main-body projected work remains excluded when
            // groundNodes is built.

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
                    if (!visited.Add(next))
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

        private static bool HasDenseDebrisAtHandoffOrigin(
            Tile2i origin,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo>?
                propCleanupByOrigin)
            => propCleanupByOrigin != null
                && propCleanupByOrigin.TryGetValue(
                    origin, out AccessPropCleanupInfo info)
                && info.HasDenseDebrisCleanup;

        private static bool TryGetV2DirectionalCornerCrestHandoff(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i predecessorPosition,
            TerrainManager terrMgr,
            Func<Tile2i, bool> bridgeTilePathable,
            out int handoffEdge,
            out AccessHandoffOperation operation,
            out uint fulfilledBitmap,
            out string diagnostic)
        {
            fulfilledBitmap = 0;
            if (!TryGetConnectedAndHandoffCorners(
                    origin, predecessorPosition,
                    out _, out _, out _, out _, out handoffEdge))
            {
                operation = AccessHandoffOperation.None;
                diagnostic = "orientation=invalid";
                return false;
            }

            return TrySelectV2CornerCrestHandoff(
                origin, profile, origin, profile,
                handoffEdge, terrMgr, bridgeTilePathable,
                out operation, out fulfilledBitmap, out diagnostic);
        }

        // V2 does not use vanilla's operation-specific fulfilled bitmap. A
        // leveling handoff requires a smooth, target-compatible G-facing edge
        // with at least one mask-pathable bridge tile. Mining and dumping use
        // the corner-crest crossing rule. The G graph and width-two pairing
        // subsequently prove the usable exit for non-leveling operations.
        private static bool TrySelectV2CornerCrestHandoff(
            Tile2i incomingOrigin,
            AccessHeightProfile incomingProfile,
            Tile2i outgoingOrigin,
            AccessHeightProfile outgoingProfile,
            int handoffEdge,
            TerrainManager terrMgr,
            Func<Tile2i, bool> bridgeTilePathable,
            out AccessHandoffOperation operation,
            out uint fulfilledBitmap,
            out string diagnostic)
        {
            int[] incomingEdgeSigns = GetEdgeHeightSigns(
                incomingOrigin, incomingProfile, OppositeEdge(handoffEdge),
                terrMgr, collectDeltas: false, out _);
            int[] outgoingEdgeSigns = GetEdgeHeightSigns(
                outgoingOrigin, outgoingProfile, handoffEdge,
                terrMgr, collectDeltas: false, out _);
            int[] incomingCorners =
            {
                incomingEdgeSigns[0],
                incomingEdgeSigns[incomingEdgeSigns.Length - 1],
            };
            int[] outgoingCorners =
            {
                outgoingEdgeSigns[0],
                outgoingEdgeSigns[outgoingEdgeSigns.Length - 1],
            };
            diagnostic =
                "v2CornerCrest incoming=["
                + string.Join(",", incomingCorners)
                + "] outgoing=["
                + string.Join(",", outgoingCorners) + "]";

            uint smoothFaceMask = 0;
            bool smoothLeveling = IsLevelingHandoffFaceCompatible(
                outgoingOrigin, outgoingProfile,
                handoffEdge, terrMgr);
            if (smoothLeveling)
            {
                for (int offset = 0;
                    offset < outgoingEdgeSigns.Length;
                    offset++)
                {
                    GetHandoffLaneCoordinates(
                        handoffEdge, offset,
                        out int x, out int y, out _, out _);
                    Tile2i bridge = outgoingOrigin + new RelTile2i(x, y);
                    if (bridgeTilePathable(bridge))
                        smoothFaceMask |= GetDesignationMask(x, y);
                }
            }

            if (!TrySelectV2CornerCrestOperation(
                    incomingCorners, outgoingCorners,
                    smoothFaceMask != 0, out operation))
            {
                fulfilledBitmap = 0;
                return false;
            }

            if (operation == AccessHandoffOperation.Leveling)
            {
                fulfilledBitmap = smoothFaceMask;
                diagnostic += " smoothLeveling=true";
            }
            else
                fulfilledBitmap = BuildHandoffEdgeMask(handoffEdge);
            return true;
        }

        // Leveling is preferred only when the complete G-facing terrain edge
        // is smooth, target-compatible, and contains a pathable bridge sample.
        // An isolated level sample on rough ground is merely the crossing
        // point of a mining or dumping crest; it must not turn the entire
        // terminal into a leveling handoff.
        internal static bool TrySelectV2CornerCrestOperation(
            IReadOnlyList<int> incomingCorners,
            IReadOnlyList<int> outgoingCorners,
            bool smoothLevelingAvailable,
            out AccessHandoffOperation operation)
        {
            if (smoothLevelingAvailable)
            {
                operation = AccessHandoffOperation.Leveling;
                return true;
            }

            bool mining = incomingCorners.All(sign => sign <= 0)
                && incomingCorners.Any(sign => sign < 0)
                && outgoingCorners.All(sign => sign >= 0);
            if (mining)
            {
                operation = AccessHandoffOperation.Mining;
                return true;
            }

            bool dumping = incomingCorners.All(sign => sign >= 0)
                && incomingCorners.Any(sign => sign > 0)
                && outgoingCorners.All(sign => sign <= 0);
            if (dumping)
            {
                operation = AccessHandoffOperation.Dumping;
                return true;
            }

            operation = AccessHandoffOperation.None;
            return false;
        }

        private static bool TryGetDirectionalHandoff(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i predecessorPosition,
            TerrainManager terrMgr,
            int vehicleClearance,
            out int handoffEdge,
            out AccessHandoffOperation operation,
            out uint fulfilledBitmap,
            out string diagnostic)
        {
            fulfilledBitmap = 0;
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
                        delta => delta.ToString("0.##", CultureInfo.InvariantCulture))) + "]"
                    + " connectedCorners=" + connectedA + "," + connectedB
                    + " groundCorners=" + handoffA + "," + handoffB;
            }

            int[] handoffSigns = GetEdgeHeightSigns(
                origin, profile, handoffEdge, terrMgr,
                s_enableVerboseHandoffDiagnostics, out float[] handoffDeltas);
            if (s_enableVerboseHandoffDiagnostics)
                diagnostic +=
                    " groundEdgeDeltas=[" + FormatHeightDeltas(handoffDeltas) + "]";

            if (vehicleClearance > 0)
                return TrySelectCornerSeamHandoff(
                    origin, profile, handoffEdge, terrMgr,
                    out operation, out fulfilledBitmap);

            if (handoffSigns.All(sign => sign == 0)
                && IsProfileExactTerrain(origin, profile, terrMgr))
            {
                operation = AccessHandoffOperation.None;
                fulfilledBitmap = ALL_DESIGNATION_TILES_MASK;
                return true;
            }

            if (TrySelectHandoffOperationFromEdge(handoffSigns, out operation))
            {
                if (operation == AccessHandoffOperation.Mining)
                    return HasVanillaWorkableDesignation(
                        origin, profile, operation, terrMgr,
                        out fulfilledBitmap);
                if (operation == AccessHandoffOperation.Dumping)
                    return HasVanillaWorkableDesignation(
                        origin, profile, operation, terrMgr,
                        out fulfilledBitmap);
            }

            operation = AccessHandoffOperation.None;
            return false;
        }

        // Temporary V1 experiment: use the former seam invariant in place of
        // vanilla's prospective fulfilled bitmap. The incoming and outgoing
        // profile edges must be level, or lie on opposite sides of terrain.
        private static bool TrySelectCornerSeamHandoff(
            Tile2i origin,
            AccessHeightProfile profile,
            int handoffEdge,
            TerrainManager terrMgr,
            out AccessHandoffOperation operation,
            out uint fulfilledBitmap)
        {
            int[] incomingSigns = GetEdgeHeightSigns(
                origin, profile, OppositeEdge(handoffEdge), terrMgr,
                collectDeltas: false, out _);
            int[] outgoingSigns = GetEdgeHeightSigns(
                origin, profile, handoffEdge, terrMgr,
                collectDeltas: false, out _);

            // A level G-facing terrain edge has a legitimate leveling handoff
            // even when it is vertically offset from the candidate V face.
            // Leveling is also the appropriate prop-clearing operation.
            if (IsLevelingHandoffFaceCompatible(
                    origin, profile, handoffEdge, terrMgr))
            {
                operation = AccessHandoffOperation.Leveling;
                fulfilledBitmap = BuildHandoffEdgeMask(handoffEdge);
                return true;
            }

            bool mining = incomingSigns.All(sign => sign <= 0)
                && incomingSigns.Any(sign => sign < 0)
                && outgoingSigns.All(sign => sign >= 0);
            if (mining)
            {
                operation = AccessHandoffOperation.Mining;
                fulfilledBitmap = BuildHandoffEdgeMask(handoffEdge);
                return true;
            }

            bool dumping = incomingSigns.All(sign => sign >= 0)
                && incomingSigns.Any(sign => sign > 0)
                && outgoingSigns.All(sign => sign <= 0);
            if (dumping)
            {
                operation = AccessHandoffOperation.Dumping;
                fulfilledBitmap = BuildHandoffEdgeMask(handoffEdge);
                return true;
            }

            operation = AccessHandoffOperation.None;
            fulfilledBitmap = 0;
            return false;
        }

        private static uint BuildHandoffEdgeMask(int handoffEdge)
        {
            uint mask = 0;
            for (int offset = 0; offset <= 4; offset++)
            {
                GetHandoffLaneCoordinates(
                    handoffEdge, offset,
                    out int x, out int y, out _, out _);
                mask |= GetDesignationMask(x, y);
            }
            return mask;
        }

        private static bool IsProfileExactTerrain(
            Tile2i origin,
            AccessHeightProfile profile,
            TerrainManager terrMgr)
        {
            for (int y = 0; y <= 4; y++)
                for (int x = 0; x <= 4; x++)
                {
                    Tile2i tile = origin + new RelTile2i(x, y);
                    int groundHeight2 = ToHeight2(
                        terrMgr.GetHeight(tile).Value.ToFloat());
                    if (profile.GetHeight2NumeratorAt(x, y)
                        != groundHeight2 * 16)
                        return false;
                }
            return true;
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

        private static float[] GetEdgeTerrainHeights(
            Tile2i origin,
            int edge,
            TerrainManager terrMgr)
        {
            var heights = new float[5];
            for (int offset = 0; offset <= 4; offset++)
            {
                int x = edge == 0 ? 0 : edge == 1 ? 4 : offset;
                int y = edge == 2 ? 0 : edge == 3 ? 4 : offset;
                Tile2i tile = origin + new RelTile2i(x, y);
                heights[offset] = terrMgr.GetHeight(tile).Value.ToFloat();
            }
            return heights;
        }

        private static bool IsLevelingHandoffFaceCompatible(
            Tile2i origin,
            AccessHeightProfile profile,
            int edge,
            TerrainManager terrMgr)
        {
            float[] groundHeights = GetEdgeTerrainHeights(
                origin, edge, terrMgr);
            if (!AccessPathSearch.IsSmoothLevelHandoffFace(groundHeights))
                return false;

            float groundLevel = groundHeights[0];
            var data = CreateDesignationData(origin, profile);
            for (int offset = 0; offset <= 4; offset++)
            {
                GetHandoffLaneCoordinates(
                    edge, offset,
                    out int x, out int y, out _, out _);
                float targetHeight = GetDesignationTargetHeightAt(
                    data, x, y).Value.ToFloat();
                if (!AccessPathSearch.IsLevelingHandoffHeightCompatible(
                        targetHeight, groundLevel))
                    return false;
            }
            return true;
        }

        private static int OppositeEdge(int edge)
            => edge == 0 ? 1 : edge == 1 ? 0 : edge == 2 ? 3 : 2;

        private static string FormatHeightDeltas(IEnumerable<float> deltas)
            => string.Join(",", deltas.Select(
                delta => delta.ToString("0.##", CultureInfo.InvariantCulture)));

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

        internal static bool IsExperimentalAccessGroundOrCleanupCenter(
            HashSet<Tile2i> groundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByTile,
            Tile2i center)
        {
            if (groundNodes.Contains(center))
                return true;
            return propCleanupByTile.TryGetValue(
                    center, out AccessPropCleanupInfo info)
                && info.IsEligible;
        }

        // A mining or leveling handoff may provisionally enter a removable
        // non-tree prop tile when generated-V cleanup can service it. Vanilla
        // terrain work does not remove that prop; materialization must submit
        // it to the prop-removal manager before the route is considered live.
        private static bool IsPostWorkHandoffGroundCenter(
            HashSet<Tile2i> groundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> propCleanupByTile,
            Tile2i center,
            AccessHandoffOperation operation)
        {
            if (IsExperimentalAccessGroundOrCleanupCenter(
                    groundNodes, propCleanupByTile, center))
                return true;
            if (operation != AccessHandoffOperation.Mining
                && operation != AccessHandoffOperation.Leveling)
                return false;
            return propCleanupByTile.TryGetValue(
                    center, out AccessPropCleanupInfo info)
                && info.IsEligibleWithinGeneratedV;
        }

        private static EvaluatedAccessCandidate? EvaluateExperimentalAccessCandidate(
            AccessSearchResult result,
            AccessDesignationPlan? plan,
            Tile2i towerPosition,
            TerrainManager terrMgr)
        {
            if (!result.Success || plan == null || !plan.IsValid
                || (plan.Designations.Count == 0
                    && plan.CleanupOrigins.Count == 0))
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
                : result.V2Route != null
                    ? AccessPathSearch.GetV2CanonicalCenter(
                        result.V2Route.States[
                            result.V2Route.States.Count - 1])
                    : result.Path[result.Path.Count - 1].Position;
            int dx = towerPosition.X - terminal.X;
            int dy = towerPosition.Y - terminal.Y;
            return new EvaluatedAccessCandidate(
                terminal,
                isValid: true,
                isReachableNow: true,
                mouthDistance: dx * dx + dy * dy,
                materialMoved: CalculateUselessMaterialMoved(rampTiles, terrMgr),
                designationCount: plan.Designations.Count
                    + plan.CleanupOrigins.Count,
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

            AccessDesignationPlan placementPlan = candidate.Plan;
            if (!placementPlan.IsValid
                || (placementPlan.Designations.Count == 0
                    && placementPlan.CleanupOrigins.Count == 0))
            {
                failureReason = placementPlan.IsValid
                    ? "EmptyPlan" : placementPlan.FailureReason;
                return false;
            }

            Tile2i terminalOrigin = default;
            bool hasGeneratedTerminal = TryGetGeneratedTerminal(
                candidate.SearchResult, out terminalOrigin);
            Dictionary<Tile2i, AccessHandoffOperation> generatedHandoffOperations =
                placementPlan.HandoffOperationsByOrigin.ToDictionary(
                    pair => pair.Key, pair => pair.Value);
            var plannedPlacements = new Dictionary<Tile2i, PlannedExperimentalDesignation>();
            foreach (AccessPlannedDesignation item in placementPlan.Designations)
            {
                if (((item.Profile.Nw2 | item.Profile.Ne2 | item.Profile.Se2
                        | item.Profile.Sw2) & 1) != 0)
                {
                    failureReason = "HalfLevelCorner";
                    return false;
                }
                TerrainDesignationProto itemProto = rampProto;
                AccessHandoffOperation operation = AccessHandoffOperation.None;
                if (generatedHandoffOperations.TryGetValue(item.Origin,
                        out AccessHandoffOperation mappedOperation))
                    operation = mappedOperation;
                else if (hasGeneratedTerminal && item.Origin == terminalOrigin)
                    operation = placementPlan.HandoffOperation;
                if (operation != AccessHandoffOperation.None)
                {
                    TerrainDesignationProto? terminalProto = operation == AccessHandoffOperation.Mining
                        ? s_miningProto
                        : operation == AccessHandoffOperation.Dumping
                            ? s_dumpingProto
                            : operation == AccessHandoffOperation.Leveling
                                ? s_levelingProto
                                : null;
                    if (terminalProto == null)
                    {
                        failureReason = "MissingHandoffOperationProto";
                        return false;
                    }
                    itemProto = terminalProto;
                }
                var data = new DesignationData(item.Origin,
                    new HeightTilesI(item.Profile.Nw2 / 2),
                    new HeightTilesI(item.Profile.Ne2 / 2),
                    new HeightTilesI(item.Profile.Se2 / 2),
                    new HeightTilesI(item.Profile.Sw2 / 2));
                plannedPlacements[item.Origin] =
                    new PlannedExperimentalDesignation(data, itemProto);
            }

            var placedCleanupRequests = new List<ATDPropRemovalRequestHandle>();
            var placedNow = new List<PlacedExperimentalDesignation>(
                placementPlan.Designations.Count);
            var preplacedCleanupOrigins = new HashSet<Tile2i>();
            if (!TryPlaceDenseDebrisCleanupDesignations(
                    placementPlan.CleanupOrigins,
                    plannedPlacements,
                    tower,
                    reservedRampTiles,
                    placedCleanupRequests,
                    placedNow,
                    preplacedCleanupOrigins,
                    out failureReason))
            {
                RollBackPropRemovalRequests(placedCleanupRequests, reservedRampTiles);
                RollBackExperimentalDesignations(placedNow, tower, reservedRampTiles);
                return false;
            }

            var selectedCleanupTrees = new List<TreeId>();
            if (!TryMaterializeTreeCleanup(placementPlan.CleanupOrigins, selectedCleanupTrees, out failureReason))
            {
                RollBackTreeCleanupSelections(selectedCleanupTrees);
                RollBackPropRemovalRequests(placedCleanupRequests, reservedRampTiles);
                RollBackExperimentalDesignations(placedNow, tower, reservedRampTiles);
                return false;
            }
            if (AccessHarvestDisruptedTrees
                && !TryMaterializeDisruptedTreeHarvests(
                    snapshot, candidate.SearchResult, selectedCleanupTrees, out failureReason))
            {
                RollBackTreeCleanupSelections(selectedCleanupTrees);
                RollBackPropRemovalRequests(placedCleanupRequests, reservedRampTiles);
                RollBackExperimentalDesignations(placedNow, tower, reservedRampTiles);
                return false;
            }
            int placementIndex = -1;
            foreach (AccessPlannedDesignation item in placementPlan.Designations)
            {
                placementIndex++;
                if (preplacedCleanupOrigins.Contains(item.Origin))
                    continue;
                Option<TerrainDesignation> existingDesignation =
                    s_desigManager.GetDesignationAt(item.Origin);
                if (existingDesignation.HasValue)
                {
                    failureReason = "DesignationAppeared";
                    TerrainDesignation existing = existingDesignation.Value;
                    LogExperimentalAccessDebug(
                        $"[ATD Access Placement Collision] " +
                        $"index={placementIndex}/{placementPlan.Designations.Count} " +
                        $"origin=({item.Origin.X},{item.Origin.Y}) mode={item.Mode} " +
                        $"profile=[{item.Profile.Nw2},{item.Profile.Ne2},{item.Profile.Se2},{item.Profile.Sw2}] " +
                        $"existingProto={existing.Prototype.Id.Value} fulfilled={existing.IsFulfilled} " +
                        $"snapshotFixed={snapshot.TryGetFixedProfile(item.Origin, out _)} " +
                        $"registeredAccessway={IsRegisteredGeneratedAccesswayOrigin(tower, item.Origin)} " +
                        $"reserved={reservedRampTiles?.Contains(item.Origin) == true} " +
                        $"cleanupOrigin={placementPlan.CleanupOrigins.Any(cleanup => cleanup.Origin == item.Origin)}");
                    RollBackTreeCleanupSelections(selectedCleanupTrees);
                    RollBackPropRemovalRequests(placedCleanupRequests, reservedRampTiles);
                    RollBackExperimentalDesignations(placedNow, tower, reservedRampTiles);
                    return false;
                }

                PlannedExperimentalDesignation planned = plannedPlacements[item.Origin];
                if (!s_desigManager.AddOrReplaceDesignation(planned.Proto, planned.Data))
                {
                    failureReason = "PlacementFailed";
                    RollBackTreeCleanupSelections(selectedCleanupTrees);
                    RollBackPropRemovalRequests(placedCleanupRequests, reservedRampTiles);
                    RollBackExperimentalDesignations(placedNow, tower, reservedRampTiles);
                    return false;
                }

                RegisterGeneratedDesignationOrigin(tower, item.Origin);
                placedNow.Add(new PlacedExperimentalDesignation(item.Origin, planned.Proto));
                s_designationOriginsInArea.Add(item.Origin);
                reservedRampTiles?.Add(item.Origin);
                if (planned.Proto != rampProto)
                    LogExperimentalAccessDebug(
                        $"[ATD Access Terminal] origin={item.Origin} proto={planned.Proto.Id.Value}");
            }

            placedRampOrigins?.AddRange(placedNow.Select(item => item.Origin));
            topRowTile = placementPlan.Designations.Count > 0
                ? placementPlan.Designations[placementPlan.Designations.Count - 1].Origin
                : placementPlan.HandoffGround;
            LastExperimentalAccessPlan = placementPlan;
            s_lastExperimentalPropRemovalRequests.AddRange(placedCleanupRequests);
            foreach (ATDPropRemovalRequestHandle request in placedCleanupRequests)
                TrackAccesswayPropRemovalRequest(tower, request);
            s_lastExperimentalCleanupTreeSelections.AddRange(selectedCleanupTrees);
            RegisterGeneratedHarvestTreePositions(tower, selectedCleanupTrees);
            return true;
        }

        private static bool ValidatePlacedV2Provider(
            AccessSearchResult result,
            AccessDesignationPlan plan,
            TerrainDesignationProto accesswayProto,
            TerrainManager terrMgr,
            out string reason)
        {
            reason = string.Empty;
            if (result.V2Route == null
                || result.V2Route.States.Count == 0
                || s_desigManager == null)
            {
                reason = "MissingRouteOrManager";
                return false;
            }
            if (result.V2Route.Handoff != null
                && plan.GroundNodeCount == 0)
            {
                reason = "MissingGroundHandoff";
                return false;
            }

            foreach (AccessPlannedDesignation item in plan.Designations)
            {
                Option<TerrainDesignation> placed =
                    s_desigManager.GetDesignationAt(item.Origin);
                if (!placed.HasValue)
                {
                    reason = $"MissingDesignation@{item.Origin}";
                    return false;
                }
                TerrainDesignationProto expected = accesswayProto;
                if (plan.HandoffOperationsByOrigin.TryGetValue(
                        item.Origin, out AccessHandoffOperation operation))
                {
                    expected = operation == AccessHandoffOperation.Mining
                        ? s_miningProto ?? accesswayProto
                        : operation == AccessHandoffOperation.Dumping
                            ? s_dumpingProto ?? accesswayProto
                            : operation == AccessHandoffOperation.Leveling
                                ? s_levelingProto ?? accesswayProto
                                : accesswayProto;
                }
                if (placed.Value.Prototype != expected)
                {
                    reason = $"ProtoMismatch@{item.Origin}";
                    return false;
                }
                int nw = GetDesignationTargetHeightRounded(
                    placed.Value, 0, 0) * 2;
                int ne = GetDesignationTargetHeightRounded(
                    placed.Value, 4, 0) * 2;
                int se = GetDesignationTargetHeightRounded(
                    placed.Value, 4, 4) * 2;
                int sw = GetDesignationTargetHeightRounded(
                    placed.Value, 0, 4) * 2;
                if (nw != item.Profile.Nw2
                    || ne != item.Profile.Ne2
                    || se != item.Profile.Se2
                    || sw != item.Profile.Sw2)
                {
                    reason = $"ProfileMismatch@{item.Origin}";
                    return false;
                }
            }

            var placedHandoffs = new List<(
                AccessV2HandoffCandidate Handoff,
                bool IsGroundToV,
                AccessV2BandState State)>();
            if (result.V2Route.RouteSteps.Count > 0)
            {
                for (int stepIndex = 0;
                    stepIndex < result.V2Route.RouteSteps.Count;
                    stepIndex++)
                {
                    AccessV2RouteStep step =
                        result.V2Route.RouteSteps[stepIndex];
                    if (step.Handoff == null)
                        continue;
                    bool isGroundToV = stepIndex > 0
                        && result.V2Route.RouteSteps[stepIndex - 1].IsGround
                        && !step.IsGround;
                    placedHandoffs.Add(
                        (step.Handoff, isGroundToV, step.State));
                }
            }
            else if (result.V2Route.Handoff != null)
            {
                placedHandoffs.Add((
                    result.V2Route.Handoff,
                    false,
                    result.V2Route.States[
                        result.V2Route.States.Count - 1]));
            }
            for (int index = 0; index < placedHandoffs.Count; index++)
            {
                AccessV2HandoffCandidate handoff =
                    placedHandoffs[index].Handoff;
                if (placedHandoffs[index].IsGroundToV)
                {
                    if (!AccessV2Handoffs.TryValidatePlacedGroundToVBridge(
                        placedHandoffs[index].State,
                        handoff,
                        result.V2Route.VehicleWidth,
                        (Tile2i tile, out float height) =>
                            TryGetLiveGroundToVPostWorkHeight(
                                placedHandoffs[index].State,
                                handoff.Lane0Operation,
                                tile,
                                out height),
                        _ => false,
                        out string bridgeReason))
                    {
                        reason = "LiveGroundToVBridgeMismatch:"
                            + bridgeReason;
                        return false;
                    }
                    continue;
                }
                if (handoff.IsQuickPath
                    && handoff.SpanLength == 1
                    && handoff.Lane0Operation
                        == AccessHandoffOperation.Leveling
                    && handoff.Lane1Operation
                        == AccessHandoffOperation.Leveling
                    && handoff.CleanupKeys.Count == 0)
                    continue;
                if (!ValidateLiveLane(
                        handoff.Lane0TerminalOrigins,
                        handoff.Lane0Operation,
                        handoff.Lane0Contact,
                        handoff.GroundEntryCenters,
                        handoff.ExitDirection,
                        lane: 0,
                        requiresCrest:
                            !placedHandoffs[index].IsGroundToV
                            && handoff.Lane0RequiresCrest,
                        out string laneReason)
                    || !ValidateLiveLane(
                        handoff.Lane1TerminalOrigins,
                        handoff.Lane1Operation,
                        handoff.Lane1Contact,
                        handoff.GroundEntryCenters,
                        handoff.ExitDirection,
                        lane: 1,
                        requiresCrest:
                            !placedHandoffs[index].IsGroundToV
                            && handoff.Lane1RequiresCrest,
                        out laneReason))
                {
                    reason = laneReason;
                    return false;
                }
            }

            reason = "ValidatedProfilesAndMegaSeam";
            return true;

            bool ValidateLiveLane(
                IReadOnlyList<Tile2i> terminalOrigins,
                AccessHandoffOperation operation,
                Tile2i contact,
                IReadOnlyList<Tile2i> groundEntries,
                Tile2i exitDirection,
                int lane,
                bool requiresCrest,
                out string laneReason)
            {
                laneReason = string.Empty;
                if (terminalOrigins.Count == 0)
                {
                    laneReason = $"MissingHandoffLaneOrigin:{lane}";
                    return false;
                }

                Tile2i terminalOrigin = terminalOrigins[terminalOrigins.Count - 1];
                Tile2i incomingOrigin = terminalOrigins[0];
                if (!TryGetV2RouteProfile(
                        result.V2Route!, incomingOrigin,
                        out AccessHeightProfile incomingProfile)
                    || !TryGetV2RouteProfile(
                        result.V2Route, terminalOrigin,
                        out AccessHeightProfile terminalProfile)
                    || !TryGetConnectedAndHandoffCorners(
                        terminalOrigin,
                        new Tile2i(
                            terminalOrigin.X - exitDirection.X,
                            terminalOrigin.Y - exitDirection.Y),
                        out _, out _, out _, out _, out int handoffEdge))
                {
                    laneReason =
                        $"LiveHandoffCornerCrestMismatch:{operation}:lane={lane}" +
                        $"@{incomingOrigin}..{terminalOrigin}";
                    return false;
                }

                bool levelingCompanion =
                    operation == AccessHandoffOperation.Leveling
                    && !groundEntries.Contains(contact);
                if (requiresCrest && !levelingCompanion
                    && (!TrySelectV2CornerCrestHandoff(
                        incomingOrigin, incomingProfile,
                        terminalOrigin, terminalProfile,
                        handoffEdge, terrMgr,
                        tile => tile == contact,
                        out AccessHandoffOperation selectedOperation,
                        out _, out _)
                    || selectedOperation != operation))
                {
                    laneReason =
                        $"LiveHandoffCornerCrestMismatch:{operation}:lane={lane}" +
                        $"@{incomingOrigin}..{terminalOrigin}";
                    return false;
                }

                var plannedTerrainOrigins = new HashSet<Tile2i>(
                    plan.Designations.Select(item => item.Origin));
                for (int originIndex = 0;
                    originIndex < terminalOrigins.Count;
                    originIndex++)
                {
                    Tile2i origin = terminalOrigins[originIndex];
                    if (!plannedTerrainOrigins.Contains(origin))
                        continue;
                    Option<TerrainDesignation> placed =
                        s_desigManager!.GetDesignationAt(origin);
                    if (!placed.HasValue)
                    {
                        laneReason =
                            $"MissingHandoffLaneDesignation:{lane}@{origin}";
                        return false;
                    }

                    bool ready = operation == AccessHandoffOperation.Mining
                        ? placed.Value.IsReadyToMineNonAmphibious()
                        : operation == AccessHandoffOperation.Dumping
                            ? placed.Value.IsReadyToDumpNonAmphibious()
                            : operation == AccessHandoffOperation.Leveling
                                && (placed.Value.IsReadyToMineNonAmphibious()
                                    || placed.Value.IsReadyToDumpNonAmphibious());
                    if (!ready)
                    {
                        laneReason =
                            $"LiveHandoffNotReady:{operation}:lane={lane}@{origin}";
                        return false;
                    }
                }

                int relativeX = contact.X - terminalOrigin.X;
                int relativeY = contact.Y - terminalOrigin.Y;
                bool onSelectedEdge = relativeX >= 0 && relativeX <= 4
                    && relativeY >= 0 && relativeY <= 4
                    && (exitDirection.X < 0 ? relativeX == 0
                        : exitDirection.X > 0 ? relativeX == 4
                        : exitDirection.Y < 0 ? relativeY == 0
                        : exitDirection.Y > 0 && relativeY == 4);
                if (!onSelectedEdge)
                {
                    laneReason =
                        $"LiveHandoffContactWrongEdge:{operation}:lane={lane}" +
                        $"@{terminalOrigin}:contact={contact}" +
                        $":exit={exitDirection}";
                    return false;
                }
                return true;
            }

            bool TryGetLiveGroundToVPostWorkHeight(
                AccessV2BandState state,
                AccessHandoffOperation operation,
                Tile2i tile,
                out float height)
            {
                float natural =
                    terrMgr.GetHeight(tile).Value.ToFloat();
                return AccessV2Handoffs
                    .TryResolvePlacedGroundToVPostWorkHeight(
                        state,
                        operation,
                        tile,
                        natural,
                        TryGetProjectedDesignationHeight,
                        out height);
            }

            float? TryGetProjectedDesignationHeight(Tile2i tile)
            {
                int baseX = (int)Math.Floor(tile.X / 4.0) * 4;
                int baseY = (int)Math.Floor(tile.Y / 4.0) * 4;
                int minX = tile.X == baseX ? baseX - 4 : baseX;
                int minY = tile.Y == baseY ? baseY - 4 : baseY;
                float? resolved = null;
                for (int originX = minX;
                    originX <= baseX;
                    originX += 4)
                {
                    for (int originY = minY;
                        originY <= baseY;
                        originY += 4)
                    {
                        var origin = new Tile2i(originX, originY);
                        Option<TerrainDesignation> designation =
                            s_desigManager!.GetDesignationAt(origin);
                        if (!designation.HasValue
                            || !IsTerrainWorkDesignationProto(
                                designation.Value.Prototype))
                            continue;
                        float candidate = GetDesignationTargetHeightAt(
                                designation.Value.Data,
                                tile.X - originX,
                                tile.Y - originY)
                            .Value.ToFloat();
                        if (resolved.HasValue
                            && Math.Abs(resolved.Value - candidate) > 0.0001f)
                            return null;
                        resolved = candidate;
                    }
                }
                return resolved;
            }
        }

        private static bool TryGetV2RouteProfile(
            AccessV2RouteData route,
            Tile2i origin,
            out AccessHeightProfile profile)
        {
            if (route.GeneratedProfiles.TryGetValue(origin, out profile))
                return true;
            for (int stateIndex = 0;
                stateIndex < route.States.Count;
                stateIndex++)
            {
                AccessV2BandState state = route.States[stateIndex];
                for (int lane = 0; lane < 2; lane++)
                    if (state.GetLaneOrigin(lane) == origin)
                    {
                        profile = state.GetLane(lane).Profile;
                        return true;
                    }
            }
            profile = default;
            return false;
        }

        private static bool TryPlaceDenseDebrisCleanupDesignations(
            IReadOnlyList<AccessPropCleanupInfo> cleanupOrigins,
            IReadOnlyDictionary<Tile2i, PlannedExperimentalDesignation> plannedPlacements,
            IAreaManagingTower tower,
            HashSet<Tile2i>? reservedRampTiles,
            List<ATDPropRemovalRequestHandle> placedCleanupRequests,
            List<PlacedExperimentalDesignation> placedTerrainDesignations,
            ISet<Tile2i> preplacedCleanupOrigins,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (cleanupOrigins.Count == 0)
                return true;

            int denseCleanupOrigins = cleanupOrigins.Count(info => info.HasDenseDebrisCleanup);
            if (denseCleanupOrigins == 0)
                return true;
            if (s_desigManager == null || PropRemovalManager == null)
            {
                failureReason = "DesignationManagerUnavailable";
                return false;
            }
            var cleanupOriginsByProp =
                new Dictionary<TerrainPropId, HashSet<Tile2i>>();
            var cleanupSampleByProp =
                new Dictionary<TerrainPropId, AccessPropSample>();
            foreach (AccessPropCleanupInfo cleanup in cleanupOrigins)
            {
                if (!cleanup.HasDenseDebrisCleanup)
                    continue;
                foreach (AccessPropSample sample in cleanup.Samples)
                {
                    if (!sample.IsDenseDebris)
                        continue;
                    if (!sample.DenseDebrisPropId.HasValue)
                    {
                        failureReason = "DenseDebrisPropIdUnavailable";
                        return false;
                    }
                    TerrainPropId propId = sample.DenseDebrisPropId.Value;
                    cleanupSampleByProp[propId] = sample;
                    if (!cleanupOriginsByProp.TryGetValue(
                            propId,
                            out HashSet<Tile2i> approvedOrigins))
                    {
                        approvedOrigins = new HashSet<Tile2i>();
                        cleanupOriginsByProp.Add(propId, approvedOrigins);
                    }
                    approvedOrigins.UnionWith(
                        sample.EligibleCleanupOrigins);
                }
            }

            var placedCleanupOrigins = new HashSet<Tile2i>();
            var plannedTerrainWorkOrigins = new HashSet<Tile2i>(
                plannedPlacements.Keys);
            int preplacedTerrainWork = 0;
            int buriedProps = 0;
            int handledByDefaultOperation = 0;
            foreach (KeyValuePair<TerrainPropId, HashSet<Tile2i>> pair
                in cleanupOriginsByProp)
            {
                if (!cleanupSampleByProp.TryGetValue(
                        pair.Key, out AccessPropSample sample)
                    || s_terrainPropsManager == null
                    || !s_terrainPropsManager.TerrainProps.TryGetValue(
                        pair.Key, out TerrainPropData liveProp))
                    continue;
                float currentCover = s_desigManager.TerrainManager
                    .GetHeight(liveProp.Position).Value.ToFloat()
                    - liveProp.PlacedAtHeight.Value.ToFloat();
                float burialThreshold = liveProp.Proto.DespawnBuriedThreshold
                    .ScaledBy(liveProp.Scale).Value.ToFloat();
                if (currentCover > burialThreshold + 0.0001f)
                {
                    buriedProps++;
                    continue;
                }
                Tile2i propOrigin = TerrainDesignation.GetOrigin(
                    liveProp.Position.Tile2i);
                if (!TrySelectDenseDebrisCleanupOrigin(
                        tower,
                        pair.Value,
                        plannedTerrainWorkOrigins,
                        placedCleanupOrigins,
                        reservedRampTiles,
                        propOrigin,
                        out Tile2i origin))
                {
                    failureReason = "DenseDebrisCleanupOriginUnavailable";
                    return false;
                }

                bool defaultOperationRemoves = false;
                if (plannedPlacements.TryGetValue(origin,
                        out PlannedExperimentalDesignation defaultPlanned))
                {
                    AccessHandoffOperation defaultOperation =
                        defaultPlanned.Proto == s_miningProto
                            ? AccessHandoffOperation.Mining
                            : defaultPlanned.Proto == s_dumpingProto
                                ? AccessHandoffOperation.Dumping
                                : defaultPlanned.Proto == s_levelingProto
                                    ? AccessHandoffOperation.Leveling
                                    : AccessHandoffOperation.None;
                    defaultOperationRemoves =
                        AccessPropCleanupPolicy
                            .PlannedOperationRemovesNonTreeProp(
                                defaultOperation, defaultPlanned.Data,
                                sample);
                }

                QuickRemoveDebrisPolicy policy =
                    AccessQuickRemoveDebrisPolicy;
                if (!AccessPropCleanupPolicy
                    .TryGetNonBuriedPropRemovalStrategy(
                        policy, defaultOperationRemoves,
                        out bool quickRemove))
                {
                    handledByDefaultOperation++;
                    continue;
                }
                if (!quickRemove && origin != propOrigin)
                {
                    if (!pair.Value.Contains(propOrigin)
                        || !TrySelectDenseDebrisCleanupOrigin(
                            tower,
                            new[] { propOrigin },
                            plannedTerrainWorkOrigins,
                            placedCleanupOrigins,
                            reservedRampTiles,
                            propOrigin,
                            out origin))
                    {
                        failureReason =
                            "DenseDebrisTerrainOriginUnavailable";
                        return false;
                    }
                }
                bool firstRequestAtOrigin = placedCleanupOrigins.Add(origin);
                if (firstRequestAtOrigin
                    && plannedPlacements.TryGetValue(origin,
                        out PlannedExperimentalDesignation planned))
                {
                    if (s_desigManager.GetDesignationAt(origin).HasValue
                        || !s_desigManager.AddOrReplaceDesignation(
                            planned.Proto, planned.Data))
                    {
                        failureReason = "CleanupOriginalPlacementFailed";
                        return false;
                    }
                    RegisterGeneratedDesignationOrigin(tower, origin);
                    placedTerrainDesignations.Add(
                        new PlacedExperimentalDesignation(origin, planned.Proto));
                    preplacedCleanupOrigins.Add(origin);
                    s_designationOriginsInArea.Add(origin);
                    reservedRampTiles?.Add(origin);
                    preplacedTerrainWork++;
                }
                ATDPropRemovalRequestHandle request = PropRemovalManager.RequestRemoval(
                    pair.Key, origin,
                    $"accessway:{origin.X},{origin.Y}",
                    quickRemove);
                if (request.IsCompleted
                    && request.Result.Outcome != ATDPropRemovalOutcome.Removed
                    && request.Result.Outcome != ATDPropRemovalOutcome.AlreadyAbsent)
                {
                    // Prop cleanup is assistance, not a placement prerequisite.
                    // If the manager cannot perform the requested quick removal
                    // or landscaping, retain the accessway so the player can
                    // remove the prop manually.
                    LogExperimentalAccessDebug(
                        $"[ATD Access Cleanup] request={request.RequestId} " +
                        $"origin={request.Origin} outcome={request.Result.Outcome} " +
                        "accesswayRetained=true manualRemovalRequired=true");
                }
                if (request.IsCompleted
                    && request.Result.Outcome == ATDPropRemovalOutcome.AlreadyAbsent)
                    continue;

                request.OnCompleted(result =>
                {
                    if (result.Outcome == ATDPropRemovalOutcome.Removed
                        && !result.OriginalDesignationRestored)
                        return;
                    if (plannedPlacements.ContainsKey(request.Origin))
                    {
                        if (result.Outcome != ATDPropRemovalOutcome.Removed
                            && result.Outcome != ATDPropRemovalOutcome.AlreadyAbsent)
                            LogExperimentalAccessDebug(
                                $"[ATD Access Cleanup] request={result.RequestId} " +
                                $"origin={result.Origin} outcome={result.Outcome} " +
                                "accesswayRetained=true manualRemovalRequired=true");
                        return;
                    }
                    s_designationOriginsInArea.Remove(request.Origin);
                    reservedRampTiles?.Remove(request.Origin);
                });
                placedCleanupRequests.Add(request);
                s_designationOriginsInArea.Add(origin);
                reservedRampTiles?.Add(origin);
            }

            LogExperimentalAccessDebug(
                $"[ATD Access Cleanup] dense debris materialization origins={denseCleanupOrigins} " +
                $"props={cleanupOriginsByProp.Count} " +
                $"requests={placedCleanupRequests.Count} " +
                $"buried={buriedProps} defaultHandled={handledByDefaultOperation} " +
                $"preplacedTerrainWork={preplacedTerrainWork}");
            return true;
        }

        private static bool TrySelectDenseDebrisCleanupOrigin(
            IAreaManagingTower tower,
            IEnumerable<Tile2i> approvedOrigins,
            ISet<Tile2i> plannedTerrainWorkOrigins,
            ISet<Tile2i> placedCleanupOrigins,
            ISet<Tile2i>? reservedRampTiles,
            Tile2i preferredPropOrigin,
            out Tile2i origin)
        {
            origin = default;
            foreach (Tile2i candidate in approvedOrigins
                .OrderBy(item => plannedTerrainWorkOrigins.Contains(item)
                    && item == preferredPropOrigin ? 0
                    : plannedTerrainWorkOrigins.Contains(item) ? 1
                    : item == preferredPropOrigin ? 2 : 3)
                .ThenBy(item => item.X)
                .ThenBy(item => item.Y))
            {
                if (!IsOriginInsideTower(tower, candidate)
                    || !IsDesignatableTileFullyInsideArea(tower.Area, candidate)
                    || DoesOriginOverlapBuilding(candidate))
                    continue;
                if (placedCleanupOrigins.Contains(candidate))
                {
                    origin = candidate;
                    return true;
                }
                if (plannedTerrainWorkOrigins.Contains(candidate))
                {
                    origin = candidate;
                    return true;
                }
                if (reservedRampTiles?.Contains(candidate) == true
                    || s_desigManager?.GetDesignationAt(candidate).HasValue == true)
                    continue;
                origin = candidate;
                return true;
            }
            return false;
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
                $"[ATD Access Cleanup] tree materialization origins={treeCleanupOrigins} " +
                $"newHarvestSelections={selectedTrees.Count}");
            return true;
        }

        private static bool TryMaterializeDisruptedTreeHarvests(
            AccessSearchSnapshot snapshot,
            AccessSearchResult result,
            List<TreeId> selectedTrees,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (s_treesManager == null)
            {
                failureReason = "TreesManagerUnavailable";
                return false;
            }

            IReadOnlyCollection<Tile2i> disturbedTiles =
                AccessPathSearch.BuildFinalGeneratedDisturbedTiles(snapshot, result);
            if (disturbedTiles.Count == 0)
                return true;
            var disturbed = disturbedTiles as HashSet<Tile2i>
                ?? new HashSet<Tile2i>(disturbedTiles);
            var alreadySelectedNow = new HashSet<TreeId>(selectedTrees);
            int added = 0;
            foreach (KeyValuePair<TreeId, TreeData> pair in s_treesManager.Trees)
            {
                TreeId treeId = pair.Key;
                if (!disturbed.Contains(treeId.Position.AsFull)
                    || alreadySelectedNow.Contains(treeId)
                    || s_treesManager.IsTreeSelected(treeId))
                    continue;
                s_treesManager.AddToHarvest(treeId);
                selectedTrees.Add(treeId);
                alreadySelectedNow.Add(treeId);
                added++;
            }
            LogExperimentalAccessDebug(
                $"[ATD Access Cleanup] disruptedTiles={disturbed.Count} " +
                $"additionalTreeHarvestSelections={added}");
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
                $"[ATD Access Cleanup] rolled back tree harvest selections={selectedTrees.Count}");
        }

        private static void RollBackLastExperimentalCleanupMaterialization(
            IAreaManagingTower tower,
            HashSet<Tile2i>? reservedRampTiles)
        {
            RollBackTreeCleanupSelections(s_lastExperimentalCleanupTreeSelections);
            RollBackPropRemovalRequests(s_lastExperimentalPropRemovalRequests, reservedRampTiles);
            ClearLastExperimentalCleanupMaterialization();
        }

        private static void ClearLastExperimentalCleanupMaterialization()
        {
            s_lastExperimentalCleanupTreeSelections.Clear();
            s_lastExperimentalPropRemovalRequests.Clear();
        }

        private static bool HasPendingExperimentalPropRemovalRequests()
            => s_lastExperimentalPropRemovalRequests.Any(request => !request.IsCompleted);

        private static void RollBackPropRemovalRequests(
            IReadOnlyList<ATDPropRemovalRequestHandle> requests,
            HashSet<Tile2i>? reservedRampTiles)
        {
            if (PropRemovalManager == null)
                return;
            foreach (ATDPropRemovalRequestHandle request in requests)
            {
                PropRemovalManager.Cancel(request);
                s_designationOriginsInArea.Remove(request.Origin);
                reservedRampTiles?.Remove(request.Origin);
            }
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

        private static string FormatExperimentalPath(AccessSearchResult result)
        {
            if (result.V2Route != null)
                return string.Join(" -> ",
                    result.V2Route.States.Select(state => state.ToString()));
            var parts = new List<string>(result.Path.Count + 1)
            {
                $"S@({result.StartOrigin.X},{result.StartOrigin.Y})"
            };
            foreach (AccessSearchNode node in result.Path)
            {
                string height = (node.Height2 / 2f).ToString("0.#", CultureInfo.InvariantCulture);
                string handoff = node.HandoffOperation != AccessHandoffOperation.None
                    ? $",op={node.HandoffOperation},span={Math.Max(1, node.HandoffSpanLength)}"
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
            if (!AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                return;
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

            LogExperimentalAccessTrace(
                $"[ATD Access Cleanup Route] cluster={clusterId} {string.Join("; ", details)}");
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
                case AccessSearchMode.VPrime: return "V'";
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
            TerrainManager terrMgr,
            IReadOnlyDictionary<Tile2i, float> terrainHeights,
            IReadOnlyDictionary<Tile2i, AccessTerrainColumn> terrainColumns,
            Tile2i relevantTerrainMin,
            Tile2i relevantTerrainMax,
            Tile2i physicalTerrainMin,
            Tile2i physicalTerrainMax,
            float dumpingMaterialSlope,
            float fallbackMiningSlope,
            int vehicleDisturbanceRadius,
            out string failureReason)
        {
            var output = new ProjectedDesignationBuildResult();
            IEnumerator routine = BuildProjectedDesignationDisturbedTilesSliced(
                designations,
                terrMgr,
                terrainHeights,
                terrainColumns,
                relevantTerrainMin,
                relevantTerrainMax,
                physicalTerrainMin,
                physicalTerrainMax,
                dumpingMaterialSlope,
                fallbackMiningSlope,
                vehicleDisturbanceRadius,
                output,
                sliceControl: null);
            while (routine.MoveNext()) { }
            failureReason = output.FailureReason;
            return output.Disturbance
                ?? new ProjectedDesignationDisturbance();
        }

        private static IEnumerator BuildProjectedDesignationDisturbedTilesSliced(
            IReadOnlyDictionary<Tile2i, TerrainDesignation> designations,
            TerrainManager terrMgr,
            IReadOnlyDictionary<Tile2i, float> terrainHeights,
            IReadOnlyDictionary<Tile2i, AccessTerrainColumn> terrainColumns,
            Tile2i relevantTerrainMin,
            Tile2i relevantTerrainMax,
            Tile2i physicalTerrainMin,
            Tile2i physicalTerrainMax,
            float dumpingMaterialSlope,
            float fallbackMiningSlope,
            int vehicleDisturbanceRadius,
            ProjectedDesignationBuildResult output,
            ExperimentalAccessSliceControl? sliceControl)
        {
            string criticalFailure = string.Empty;
            var result = new ProjectedDesignationDisturbance();
            Stopwatch phaseTimer = Stopwatch.StartNew();
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

                if (!TraceBoundary(westExposed,
                    origin, profile.Nw2 / 2f,
                    origin + new RelTile2i(0, 4), profile.Sw2 / 2f,
                    new Tile2i(-1, 0)))
                { output.FailureReason = criticalFailure; output.Disturbance = result; yield break; }
                if (!TraceBoundary(eastExposed,
                    origin + new RelTile2i(4, 0), profile.Ne2 / 2f,
                    origin + new RelTile2i(4, 4), profile.Se2 / 2f,
                    new Tile2i(1, 0)))
                { output.FailureReason = criticalFailure; output.Disturbance = result; yield break; }
                if (!TraceBoundary(northExposed,
                    origin, profile.Nw2 / 2f,
                    origin + new RelTile2i(4, 0), profile.Ne2 / 2f,
                    new Tile2i(0, -1)))
                { output.FailureReason = criticalFailure; output.Disturbance = result; yield break; }
                if (!TraceBoundary(southExposed,
                    origin + new RelTile2i(0, 4), profile.Sw2 / 2f,
                    origin + new RelTile2i(4, 4), profile.Se2 / 2f,
                    new Tile2i(0, 1)))
                { output.FailureReason = criticalFailure; output.Disturbance = result; yield break; }

                if (sliceControl != null
                    && phaseTimer.ElapsedMilliseconds
                        >= sliceControl.SliceBudgetMilliseconds)
                {
                    if (sliceControl.CancellationRequested)
                    {
                        output.FailureReason = "SearchCancelled";
                        output.Disturbance = result;
                        yield break;
                    }
                    phaseTimer.Restart();
                    yield return null;
                    phaseTimer.Restart();
                }

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

                bool TraceBoundary(
                    bool isExposed,
                    Tile2i firstCorner,
                    float firstHeight,
                    Tile2i secondCorner,
                    float secondHeight,
                    Tile2i direction)
                {
                    if (!isExposed)
                        return true;
                    return TraceCorner(firstCorner, firstHeight, direction)
                        && TraceCorner(secondCorner, secondHeight, direction);
                }

                bool TraceCorner(Tile2i corner, float plannedHeight, Tile2i direction)
                {
                    int relevantMinX = relevantTerrainMin.X - vehicleDisturbanceRadius;
                    int relevantMaxX = relevantTerrainMax.X + vehicleDisturbanceRadius;
                    int relevantMinY = relevantTerrainMin.Y - vehicleDisturbanceRadius;
                    int relevantMaxY = relevantTerrainMax.Y + vehicleDisturbanceRadius;
                    bool canReachRelevant = direction.X != 0
                        ? corner.Y >= relevantMinY && corner.Y <= relevantMaxY
                            && (direction.X > 0
                                ? corner.X <= relevantMaxX
                                : corner.X >= relevantMinX)
                        : corner.X >= relevantMinX && corner.X <= relevantMaxX
                            && (direction.Y > 0
                                ? corner.Y <= relevantMaxY
                                : corner.Y >= relevantMinY);
                    if (!canReachRelevant)
                        return true;
                    if (!TryResolveCornerRay(
                        corner, plannedHeight,
                        out AccessSideRayOperation operation,
                        out float materialSlope))
                        return true;

                    int postTerminationSafetyMargin =
                        AutoTerrainDesignationsMod.AccessRayEndBuffer;
                    int physicalDistance = direction.X < 0
                        ? corner.X - physicalTerrainMin.X
                        : direction.X > 0
                            ? physicalTerrainMax.X - corner.X
                            : direction.Y < 0
                                ? corner.Y - physicalTerrainMin.Y
                                : physicalTerrainMax.Y - corner.Y;
                    for (int distance = 1; distance <= physicalDistance; distance++)
                    {
                        Tile2i tile = new Tile2i(
                            corner.X + direction.X * distance,
                            corner.Y + direction.Y * distance);
                        float sampledHeight = terrMgr.GetHeight(tile).Value.ToFloat();
                        // Immutable FV rays share the same projected-ground
                        // semantics as generated rays. An equal or stronger
                        // same-sort surface resolves this ray; a deeper cut or
                        // higher fill continues from that projected surface.
                        if (result.TryGetWorkHeight(
                                operation, tile,
                                out float projectedGroundHeight))
                            sampledHeight = operation
                                == AccessSideRayOperation.Cut
                                    ? Math.Min(
                                        sampledHeight,
                                        projectedGroundHeight)
                                    : Math.Max(
                                        sampledHeight,
                                        projectedGroundHeight);
                        float rayHeight = operation == AccessSideRayOperation.Fill
                            ? plannedHeight - distance * materialSlope
                            : plannedHeight + distance * materialSlope;
                        float gap = operation == AccessSideRayOperation.Fill
                            ? rayHeight - sampledHeight
                            : sampledHeight - rayHeight;
                        bool hasPassedTerrain = gap <= 0f;
                        bool hasReachedDryCutHeight =
                            operation != AccessSideRayOperation.Cut
                            || rayHeight >= 1f;
                        if (gap > 0f)
                            AddProjected(operation, tile, rayHeight);
                        else if (!hasReachedDryCutHeight)
                            AddSafety(operation, tile);
                        if (hasPassedTerrain && hasReachedDryCutHeight)
                        {
                            int safetyEnd = Math.Min(
                                physicalDistance,
                                distance + postTerminationSafetyMargin);
                            for (int safetyDistance = distance;
                                safetyDistance <= safetyEnd;
                                safetyDistance++)
                                AddSafety(
                                    operation,
                                    new Tile2i(
                                        corner.X + direction.X * safetyDistance,
                                        corner.Y + direction.Y * safetyDistance));
                            return true;
                        }
                    }
                    criticalFailure =
                        "ProjectedRayUnresolvedAtMapEdge:" + operation
                        + "@(" + corner.X.ToString(CultureInfo.InvariantCulture)
                        + "," + corner.Y.ToString(CultureInfo.InvariantCulture) + ")"
                        + " dir=(" + direction.X.ToString(CultureInfo.InvariantCulture)
                        + "," + direction.Y.ToString(CultureInfo.InvariantCulture) + ")";
                    return false;

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

                    int scanMinX = Math.Max(
                        physicalTerrainMin.X,
                        relevantTerrainMin.X - vehicleDisturbanceRadius);
                    int scanMaxX = Math.Min(
                        physicalTerrainMax.X,
                        relevantTerrainMax.X + vehicleDisturbanceRadius);
                    int scanMinY = Math.Max(
                        physicalTerrainMin.Y,
                        relevantTerrainMin.Y - vehicleDisturbanceRadius);
                    int scanMaxY = Math.Min(
                        physicalTerrainMax.Y,
                        relevantTerrainMax.Y + vehicleDisturbanceRadius);
                    int firstX = outwardX > 0
                        ? Math.Max(scanMinX, corner.X + 1)
                        : scanMinX;
                    int lastX = outwardX > 0
                        ? scanMaxX
                        : Math.Min(scanMaxX, corner.X - 1);
                    int firstY = outwardY > 0
                        ? Math.Max(scanMinY, corner.Y + 1)
                        : scanMinY;
                    int lastY = outwardY > 0
                        ? scanMaxY
                        : Math.Min(scanMaxY, corner.Y - 1);
                    if (firstX > lastX || firstY > lastY)
                        return;
                    for (int y = firstY; y <= lastY; y++)
                    {
                        int dy = (y - corner.Y) * outwardY;
                        for (int x = firstX; x <= lastX; x++)
                        {
                            int dx = (x - corner.X) * outwardX;
                            int slopeDistance = Math.Max(dx, dy);
                            Tile2i tile = new Tile2i(x, y);
                            float sampledHeight = terrMgr.GetHeight(tile).Value.ToFloat();
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
                    if (!terrMgr.IsValidCoord(corner))
                        return false;
                    float terrainHeight = terrainHeights.TryGetValue(corner, out float capturedHeight)
                        ? capturedHeight
                        : terrMgr.GetHeight(corner).Value.ToFloat();
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
                    if (operation == AccessSideRayOperation.Cut)
                    {
                        AccessTerrainColumn column =
                            terrainColumns.TryGetValue(corner, out AccessTerrainColumn capturedColumn)
                                ? capturedColumn
                                : CaptureAccessTerrainColumn(terrMgr, corner);
                        if (!column.TryGetNormalSlopeAt(
                                plannedHeight, out materialSlope, out _))
                            materialSlope = fallbackMiningSlope;
                    }
                    return materialSlope > 0f;
                }

                void AddProjected(
                    AccessSideRayOperation operation, Tile2i tile, float projectedHeight)
                {
                    if (tile.X < relevantTerrainMin.X - vehicleDisturbanceRadius
                        || tile.X > relevantTerrainMax.X + vehicleDisturbanceRadius
                        || tile.Y < relevantTerrainMin.Y - vehicleDisturbanceRadius
                        || tile.Y > relevantTerrainMax.Y + vehicleDisturbanceRadius)
                        return;
                    AddDisturbance(
                        operation, tile,
                        isSafetyOnly: false,
                        projectedHeight);
                }

                void AddSafety(
                    AccessSideRayOperation operation, Tile2i tile)
                {
                    if (tile.X < relevantTerrainMin.X - vehicleDisturbanceRadius
                        || tile.X > relevantTerrainMax.X + vehicleDisturbanceRadius
                        || tile.Y < relevantTerrainMin.Y - vehicleDisturbanceRadius
                        || tile.Y > relevantTerrainMax.Y + vehicleDisturbanceRadius)
                        return;
                    AddDisturbance(
                        operation, tile,
                        isSafetyOnly: true,
                        projectedHeight: 0f);
                }

                void AddDisturbance(
                    AccessSideRayOperation operation, Tile2i tile,
                    bool isSafetyOnly,
                    float projectedHeight)
                {
                    for (int dy = -vehicleDisturbanceRadius; dy <= vehicleDisturbanceRadius; dy++)
                    {
                        for (int dx = -vehicleDisturbanceRadius; dx <= vehicleDisturbanceRadius; dx++)
                        {
                            Tile2i blocked = new Tile2i(tile.X + dx, tile.Y + dy);
                            if (blocked.X >= relevantTerrainMin.X
                                && blocked.X <= relevantTerrainMax.X
                                && blocked.Y >= relevantTerrainMin.Y
                                && blocked.Y <= relevantTerrainMax.Y)
                            {
                                if (!isSafetyOnly)
                                    result.AddHeight(
                                        operation, blocked,
                                        projectedHeight);
                                result.Add(
                                    operation, blocked, origin,
                                    isSafetyOnly);
                            }
                        }
                    }
                }
            }
            output.Disturbance = result;
            output.FailureReason = string.Empty;
            yield break;

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
                return materialSlope > 0f
                    ? 1f / materialSlope
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

        private static bool IsOriginInsideGeneratedArea(
            IAreaManagingTower tower,
            Tile2i origin,
            int marginTiles)
        {
            if (marginTiles <= 0)
                return IsOriginInsideTower(tower, origin);
            return IsTileWithinTowerAreaMargin(tower, origin, marginTiles)
                && IsTileWithinTowerAreaMargin(
                    tower, origin + new RelTile2i(3, 0), marginTiles)
                && IsTileWithinTowerAreaMargin(
                    tower, origin + new RelTile2i(0, 3), marginTiles)
                && IsTileWithinTowerAreaMargin(
                    tower, origin + new RelTile2i(3, 3), marginTiles);
        }

        private static bool IsTileWithinTowerAreaMargin(
            IAreaManagingTower tower,
            Tile2i tile,
            int marginTiles)
        {
            if (tower.Area.ContainsTile(tile))
                return true;
            for (int dx = -marginTiles; dx <= marginTiles; dx++)
            {
                int remaining = marginTiles - Math.Abs(dx);
                for (int dy = -remaining; dy <= remaining; dy++)
                    if (tower.Area.ContainsTile(
                            tile + new RelTile2i(dx, dy)))
                        return true;
            }
            return false;
        }

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
                $"[ATD Access Tower Ground Frontier] start={start} reached={reachedGround.Count} " +
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

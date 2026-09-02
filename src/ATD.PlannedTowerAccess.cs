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
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities.Static;
using Mafi.Core.PathFinding;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using AutoTerrainDesignations.Access;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private const int PLANNED_TOWER_APPROACH_RADIUS = 12;
        private const float PLANNED_TOWER_TERRAIN_EPSILON = 0.26f;

        private sealed class PlannedTowerAccessResult
        {
            public bool MarkerFound;
            public bool Connected;
            public bool RequestCancelled;
        }

        private sealed class PlannedTowerApproach
        {
            public MineTower Ghost { get; }
            public Tile2i AccessTile { get; }
            public IReadOnlyDictionary<Tile2i, AccessHeightProfile> Profiles { get; }

            public PlannedTowerApproach(
                MineTower ghost,
                Tile2i accessTile,
                IReadOnlyDictionary<Tile2i, AccessHeightProfile> profiles)
            {
                Ghost = ghost;
                AccessTile = accessTile;
                Profiles = profiles;
            }
        }

        private sealed class PlannedTowerCandidate
        {
            public PlannedTowerApproach Approach { get; }
            public AccessSearchSnapshot Snapshot { get; }
            public AccessSearchResult SearchResult { get; }
            public AccessDesignationPlan Plan { get; }
            public AccessReplayMemoryEvidence? MemoryEvidence { get; }

            public PlannedTowerCandidate(
                PlannedTowerApproach approach,
                AccessSearchSnapshot snapshot,
                AccessSearchResult searchResult,
                AccessDesignationPlan plan,
                AccessReplayMemoryEvidence? memoryEvidence)
            {
                Approach = approach;
                Snapshot = snapshot;
                SearchResult = searchResult;
                Plan = plan;
                MemoryEvidence = memoryEvidence;
            }
        }

        private static IEnumerator TryConnectToPlannedMiningTowerGhostCoroutine(
            IAreaManagingTower tower,
            TerrainManager terrMgr,
            ATDTowerSettings towerSettings,
            bool generateRamps,
            PlannedTowerAccessResult result,
            ExperimentalAccessSliceControl? sliceControl = null)
        {
            result.MarkerFound = false;
            result.Connected = false;
            result.RequestCancelled = false;
            if (s_entitiesManager == null)
                yield break;

            BuildBuildingOccupiedTiles(tower, forceRefresh: true);
            List<MineTower> ghosts = FindUnstartedMiningTowerGhostsInArea(tower);
            if (ghosts.Count == 0)
                yield break;
            result.MarkerFound = true;

            if (sliceControl == null
                && s_createDesignationsOperationActive)
            {
                var managedResult = new PlannedTowerAccessResult();
                var completion =
                    new CreateDesignationsAccessRequestCompletion();
                string workFingerprint = "planned-tower|revision="
                    + CurrentTerrainDesignationRevision
                    + "|ghosts="
                    + BuildPlannedTowerGhostFingerprint(tower)
                    + "|width=" + towerSettings.RampWidth
                    + "|clearance=" + towerSettings.VehicleClearance;
                IEnumerator requestRoutine =
                    AwaitCreateDesignationsAccessRequest(
                        tower,
                        towerSettings,
                        BuildCreateDesignationsAccessOwnerKey(
                            tower, "planned-tower"),
                        workFingerprint,
                        managedSlice =>
                            RunCreateDesignationsAccessRampWithDebugGate(
                                managedSlice,
                                TryConnectToPlannedMiningTowerGhostCoroutine(
                                    tower,
                                    terrMgr,
                                    towerSettings,
                                    generateRamps,
                                    managedResult,
                                    managedSlice)),
                        () => ATDAccesswayRequestResult.Succeeded(
                            new PlannedTowerManagedResult(managedResult)),
                        completion,
                        ATDAccesswayRequestKind.PlannedTower);
                while (requestRoutine.MoveNext())
                    yield return requestRoutine.Current;

                if (completion.Snapshot.State
                        == ATDAccesswayRequestState.Succeeded
                    && completion.Snapshot.Result?.Payload
                        is PlannedTowerManagedResult payload)
                {
                    result.MarkerFound = payload.AccessResult.MarkerFound;
                    result.Connected = payload.AccessResult.Connected;
                    result.RequestCancelled =
                        payload.AccessResult.RequestCancelled;
                }
                else
                {
                    result.RequestCancelled = true;
                    LogExperimentalAccessDebug(
                        "[ATD Planned Tower Access] request ended "
                        + $"state={completion.Snapshot.State} "
                        + $"reason={completion.Snapshot.Result?.Reason ?? "unknown"}");
                }
                yield break;
            }

            if (!generateRamps
                || (towerSettings.RampWidth != 1 && towerSettings.RampWidth != 2)
                || s_miningProto == null
                || s_levelingProto == null
                || s_vehiclePathFindingManager == null)
            {
                LogExperimentalAccessDebug(
                    $"[ATD Planned Tower Access] ghosts={ghosts.Count} marker claimed AUTO request, " +
                    "but path generation is unavailable");
                yield break;
            }

            TerrainDesignationProto levelingProto = s_levelingProto;

            VehiclePathFindingParams pathParams =
                GetExcavatorPathFindingParamsForTower(tower, out _);
            IPathabilityProvider pathability =
                s_vehiclePathFindingManager.PathabilityProvider;
            int requiredWidth = ExtractVehicleClearance(pathParams).Value > 4
                ? 2
                : 1;
            if (!(tower is MineTower homeTower))
            {
                LogExperimentalAccessDebug(
                    "[ATD Planned Tower Access] active tower is not a mine tower; " +
                    "AUTO request remains claimed by ghost marker");
                yield break;
            }
            IReadOnlyList<Tile2i> ghostGroundGoals =
                BuildPlannedTowerGhostGroundGoals(tower);
            if (ghostGroundGoals.Count == 0)
            {
                LogExperimentalAccessDebug(
                    "[ATD Planned Tower Access] eligible ghost ground goals=0; " +
                    "AUTO request remains claimed by ghost marker");
                yield break;
            }
            List<PlannedTowerApproach> approaches =
                BuildPlannedTowerApproaches(
                    tower, new[] { homeTower }, terrMgr, pathability, pathParams,
                    requiredWidth,
                    allowExactNaturalSourcesOutsideArea: true);
            if (approaches.Count == 0)
            {
                LogExperimentalAccessDebug(
                    $"[ATD Planned Tower Access] ghosts={ghosts.Count} " +
                    "eligibleApproaches=0; AUTO request remains claimed by ghost marker");
                yield break;
            }

            var mergedProfiles = new Dictionary<Tile2i, AccessHeightProfile>();
            foreach (PlannedTowerApproach approach in approaches)
            {
                foreach (KeyValuePair<Tile2i, AccessHeightProfile> pair
                    in approach.Profiles)
                    mergedProfiles[pair.Key] = pair.Value;
            }

            var workDepths = new Dict<Tile2i, int>();
            var cornerHeights = new Dict<Tile2i, int>();
            bool profileConflict = false;
            foreach (KeyValuePair<Tile2i, AccessHeightProfile> pair
                in mergedProfiles)
            {
                AccessHeightProfile profile = pair.Value;
                workDepths[pair.Key] = profile.Center2 / 2;
                AddCorner(pair.Key, profile.Nw2 / 2);
                AddCorner(pair.Key + new RelTile2i(4, 0), profile.Ne2 / 2);
                AddCorner(pair.Key + new RelTile2i(4, 4), profile.Se2 / 2);
                AddCorner(pair.Key + new RelTile2i(0, 4), profile.Sw2 / 2);
            }
            if (profileConflict)
            {
                LogExperimentalAccessDebug(
                    "[ATD Planned Tower Access] merged approach profiles conflict; " +
                    "AUTO request remains claimed by ghost marker");
                yield break;
            }

            bool useWorkerThread = UseWorkerThread;
            var snapshotBuild = new ExperimentalAccessSnapshotBuildResult();
            IEnumerator snapshotPreparation = BuildExperimentalAccessSnapshot(
                tower, workDepths, cornerHeights, terrMgr,
                isMining: true, allowsMixedWork: true,
                reachableFixedOrigins: null,
                groundGoalOverride: ghostGroundGoals,
                generatedAreaMarginTiles: 0,
                snapshotBuild,
                sliceControl,
                createWorkspace: !useWorkerThread);
            while (snapshotPreparation.MoveNext())
                yield return snapshotPreparation.Current;

            if (sliceControl?.CancellationRequested ?? false)
                yield break;

            AccessSearchSnapshot? snapshot = snapshotBuild.Snapshot;
            string snapshotFailure = snapshotBuild.FailureReason;
            if (snapshot == null)
            {
                LogExperimentalAccessDebug(
                    $"[ATD Planned Tower Access] snapshotFailed={snapshotFailure}");
                yield break;
            }

            var intent = new GenericWorkIntent("planned-mining-tower-ghost");
            var origins = mergedProfiles.Keys
                .Select(origin => new AccessWorkOrigin(
                    origin, intent, false,
                    AccessWorkOriginKind.ExternalTerrainWorkEndpoint))
                .ToList();
            var cluster = new AccessOriginCluster(
                1, origins, new[] { intent });
            AccessPathRequest request = BuildMergedGoalAccessRequest(
                snapshot, cluster,
                fixedGoalOrigins: Array.Empty<Tile2i>(),
                groundGoalOverride: ghostGroundGoals);
            var dryRun = new ExperimentalAccessDryRunResult();
            IEnumerator search = RunExperimentalAccessDryRunConfigured(
                request,
                snapshotBuild.Workspace,
                cluster,
                0,
                1,
                dryRun,
                sliceControl,
                useWorkerThread);
            while (search.MoveNext())
                yield return search.Current;
            if (sliceControl?.CancellationRequested
                ?? s_cancelExperimentalAccessSearch)
                yield break;

            AccessSearchResult? searchResult = dryRun.SearchResult;
            AccessDesignationPlan? plan = dryRun.Plan;
            PlannedTowerApproach? selectedSourceApproach = searchResult == null
                ? null
                : approaches.FirstOrDefault(item =>
                    item.Profiles.ContainsKey(searchResult.StartOrigin));
            Tile2i reachedGoal = searchResult?.V2Route?.GroundPath.LastOrDefault()
                ?? (searchResult != null && searchResult.Path.Count > 0
                    ? searchResult.Path[searchResult.Path.Count - 1].Position
                    : default);
            MineTower? selectedGhost = searchResult == null
                ? null
                : ghosts.OrderBy(ghost => DistanceSquared(
                    GetTowerAccessPosition(
                        ghost,
                        tower.Area.BoundingBoxMin,
                        tower.Area.BoundingBoxMax),
                    reachedGoal))
                    .FirstOrDefault();
            PlannedTowerApproach? selectedApproach =
                selectedSourceApproach == null || selectedGhost == null
                    ? null
                    : new PlannedTowerApproach(
                        selectedGhost,
                        GetTowerAccessPosition(
                            selectedGhost,
                            tower.Area.BoundingBoxMin,
                            tower.Area.BoundingBoxMax),
                        selectedSourceApproach.Profiles);
            PlannedTowerCandidate? best = searchResult != null
                && searchResult.Success
                && plan != null
                && plan.IsValid
                && selectedApproach != null
                    ? new PlannedTowerCandidate(
                        selectedApproach, snapshot, searchResult, plan,
                        snapshotBuild.MemoryEvidence)
                    : null;
            if (best != null)
            {
                LogExperimentalAccessDebug(
                    $"[ATD Planned Tower Access] ghost={best.Approach.Ghost.Id} " +
                    $"homeStarts={mergedProfiles.Count} reachedGhostGoal={reachedGoal}; " +
                    "committing route");
            }

            void AddCorner(Tile2i corner, int height)
            {
                if (cornerHeights.TryGetValue(corner, out int existing)
                    && existing != height)
                {
                    profileConflict = true;
                    return;
                }
                cornerHeights[corner] = height;
            }

            if (best == null)
            {
                LogExperimentalAccessDebug(
                    $"[ATD Planned Tower Access] ghosts={ghosts.Count} " +
                    $"approaches={approaches.Count} reachable=0; " +
                    "AUTO request remains claimed by ghost marker");
                yield break;
            }

            if (!IsUnstartedMiningTowerGhost(best.Approach.Ghost))
            {
                LogExperimentalAccessDebug(
                    $"[ATD Planned Tower Access] selected ghost={best.Approach.Ghost.Id} " +
                    "changed construction state during search; AUTO ore scan suppressed");
                yield break;
            }

            if (best.Plan.Designations.Count == 0
                && best.Plan.CleanupOrigins.Count == 0)
            {
                LastExperimentalAccessSearch = best.SearchResult;
                LastExperimentalAccessPlan = best.Plan;
                SetTowerLastRampOutcome(tower, RampPlacementOutcome.Crested);
                result.Connected = true;
                LogExperimentalAccessDebug(
                    $"[ATD Planned Tower Access] ghost={best.Approach.Ghost.Id} " +
                    $"access={best.Approach.AccessTile} selected=existing-route");
                yield break;
            }

            var placedOrigins = new List<Tile2i>();
            var candidate = new ExperimentalAccessCandidate(
                best.SearchResult, best.Plan, request, dryRun.ReplayTiming,
                best.MemoryEvidence);
            if (!TryPlaceExperimentalAccessCandidate(
                    best.Snapshot, levelingProto, candidate, tower,
                    placedOrigins, reservedRampTiles: null,
                    out Tile2i topRowTile,
                    out string placementFailure))
            {
                LogExperimentalAccessDebug(
                    $"[ATD Planned Tower Access] ghost={best.Approach.Ghost.Id} " +
                    $"placementFailed={placementFailure}; AUTO ore scan suppressed");
                yield break;
            }

            AccessDesignationPlan placedPlan = LastExperimentalAccessPlan
                ?? best.Plan;
            ATDPropRemovalRequestHandle[] replayCleanupRequests =
                SnapshotLastExperimentalPropRemovalRequests();
            string validationReason = "NotV2";
            bool pendingPropRemoval = replayCleanupRequests.Any(
                request => !request.IsCompleted);
            if (pendingPropRemoval)
                validationReason = "AcceptedPendingPropRemoval";
            bool valid = pendingPropRemoval
                || best.SearchResult.V2Route == null
                || ValidatePlacedV2Provider(
                    best.SearchResult, placedPlan, levelingProto, terrMgr,
                    out validationReason);
            bool ghostStillUnstarted =
                IsUnstartedMiningTowerGhost(best.Approach.Ghost);
            if (!valid || !ghostStillUnstarted)
            {
                RollBackExperimentalDesignations(
                    placedOrigins, tower, levelingProto,
                    reservedRampTiles: null);
                RollBackLastExperimentalCleanupMaterialization(
                    tower, reservedRampTiles: null);
                LogExperimentalAccessDebug(
                    $"[ATD Planned Tower Access] ghost={best.Approach.Ghost.Id} " +
                    $"post-placement validation failed reason={validationReason} " +
                    $"ghostStillUnstarted={ghostStillUnstarted}; rolled back; " +
                    "AUTO ore scan suppressed");
                yield break;
            }

            RegisterGeneratedAccesswayOrigins(tower, placedOrigins);
            AccessReplayCaptureOperation? replayCapture =
                AccessSearchReplayRecorder.BeginRecordAccepted(
                    candidate, "planned-tower-access");
            if (replayCapture != null)
            {
                bool ValidateReplayLive(out string reason)
                {
                    if (!IsUnstartedMiningTowerGhost(best.Approach.Ghost))
                    {
                        reason = "Planned mining tower started before replay acceptance.";
                        return false;
                    }
                    if (best.SearchResult.V2Route == null)
                    {
                        reason = "Non-V2 accepted route";
                        return true;
                    }
                    return ValidatePlacedV2Provider(
                        best.SearchResult,
                        placedPlan,
                        levelingProto,
                        terrMgr,
                        out reason);
                }
                IEnumerator replayCompletion =
                    CompleteReplayCaptureAfterLiveAcceptance(
                        replayCapture,
                        replayCleanupRequests,
                        ValidateReplayLive,
                        sliceControl);
                while (replayCompletion.MoveNext())
                    yield return replayCompletion.Current;
            }
            ClearLastExperimentalCleanupMaterialization();
            LastExperimentalAccessSearch = best.SearchResult;
            SetTowerLastRampOutcome(tower, RampPlacementOutcome.Crested);
            result.Connected = true;
            LogExperimentalAccessDebug(
                $"[ATD Planned Tower Access] ghost={best.Approach.Ghost.Id} " +
                $"access={best.Approach.AccessTile} cost={best.SearchResult.Cost:0.##} " +
                $"designations={placedOrigins.Count} top={topRowTile}");
        }

        private static List<MineTower> FindUnstartedMiningTowerGhostsInArea(
            IAreaManagingTower tower)
        {
            var result = new List<MineTower>();
            if (s_entitiesManager == null)
                return result;
            foreach (MineTower candidate in
                s_entitiesManager.GetAllEntitiesOfType<MineTower>())
            {
                if (ReferenceEquals(candidate, tower)
                    || !IsUnstartedMiningTowerGhost(candidate))
                    continue;
                Tile2i access = GetTowerAccessPosition(
                    candidate,
                    tower.Area.BoundingBoxMin,
                    tower.Area.BoundingBoxMax);
                if (tower.Area.ContainsTile(access))
                    result.Add(candidate);
            }
            return result;
        }

        private static string BuildPlannedTowerGhostFingerprint(
            IAreaManagingTower tower)
        {
            return string.Join(",", FindUnstartedMiningTowerGhostsInArea(tower)
                .Select(ghost =>
                {
                    Tile2i access = GetTowerAccessPosition(
                        ghost,
                        tower.Area.BoundingBoxMin,
                        tower.Area.BoundingBoxMax);
                    return $"{ghost.Id}@{access.X}:{access.Y}";
                })
                .OrderBy(value => value, StringComparer.Ordinal));
        }

        private static IReadOnlyList<Tile2i> BuildPlannedTowerGhostGroundGoals(
            IAreaManagingTower tower)
        {
            if (s_vehiclePathFindingManager == null)
                return Array.Empty<Tile2i>();
            List<MineTower> ghosts = FindUnstartedMiningTowerGhostsInArea(tower);
            if (ghosts.Count == 0)
                return Array.Empty<Tile2i>();
            VehiclePathFindingParams pathParams =
                GetExcavatorPathFindingParamsForTower(tower, out _);
            IPathabilityProvider pathability =
                s_vehiclePathFindingManager.PathabilityProvider;
            List<Tile2i> goals = ghosts
                .Select(ghost =>
                {
                    Tile2i access = GetTowerAccessPosition(
                        ghost,
                        tower.Area.BoundingBoxMin,
                        tower.Area.BoundingBoxMax);
                    return FindNearestPathableTile(
                        access, pathability, pathParams);
                })
                .Where(goal => tower.Area.ContainsTile(goal)
                    && pathability.IsPathable(
                        goal, pathParams.PathabilityQueryMask))
                .Distinct()
                .ToList();
            if (goals.Count > 0)
            {
                LogExperimentalAccessDebug(
                    $"[ATD Planned Tower Access] using ghost ground goals " +
                    $"instead of active-tower goals: [{string.Join(",", goals)}]");
            }
            return goals;
        }

        private static bool IsUnstartedMiningTowerGhost(MineTower tower)
        {
            if (tower.IsDestroyed || tower.IsConstructed
                || tower.ConstructionState == ConstructionState.Invalid
                || tower.ConstructionState == ConstructionState.PendingDeconstruction
                || tower.ConstructionState == ConstructionState.InDeconstruction)
                return false;
            Option<IEntityConstructionProgress> progress =
                tower.ConstructionProgress;
            return progress.HasValue
                && !progress.Value.IsDeconstruction
                && !progress.Value.IsUpgrade
                && progress.Value.CurrentSteps == 0;
        }

        private static List<PlannedTowerApproach> BuildPlannedTowerApproaches(
            IAreaManagingTower tower,
            IReadOnlyList<MineTower> ghosts,
            TerrainManager terrMgr,
            IPathabilityProvider pathability,
            VehiclePathFindingParams pathParams,
            int requiredWidth,
            bool allowExactNaturalSourcesOutsideArea = false)
        {
            var approaches = new List<PlannedTowerApproach>();
            foreach (MineTower ghost in ghosts)
            {
                Tile2i access = GetTowerAccessPosition(
                    ghost, tower.Area.BoundingBoxMin,
                    tower.Area.BoundingBoxMax);
                Tile2i seed = FindNearestPathableTile(
                    access, pathability, pathParams);
                HashSet<Tile2i> reachableApproachGround =
                    BuildReachableApproachGround(
                        seed, pathability, pathParams);
                var profiles = new Dictionary<Tile2i, AccessHeightProfile>();
                int firstX = (seed.X - PLANNED_TOWER_APPROACH_RADIUS) & -4;
                int firstY = (seed.Y - PLANNED_TOWER_APPROACH_RADIUS) & -4;
                int lastX = (seed.X + PLANNED_TOWER_APPROACH_RADIUS) & -4;
                int lastY = (seed.Y + PLANNED_TOWER_APPROACH_RADIUS) & -4;
                for (int x = firstX; x <= lastX; x += 4)
                {
                    for (int y = firstY; y <= lastY; y += 4)
                    {
                        Tile2i origin = new Tile2i(x, y);
                        if (TryBuildNaturalApproachProfile(
                                tower, origin, seed, terrMgr,
                                pathability, pathParams,
                                reachableApproachGround,
                                allowExactNaturalSourcesOutsideArea,
                                out AccessHeightProfile profile))
                            profiles[origin] = profile;
                    }
                }

                List<Tile2i> ordered = profiles.Keys
                    .OrderBy(origin => DistanceSquared(
                        origin + new RelTile2i(2, 2), seed))
                    .ThenBy(origin => origin.X)
                    .ThenBy(origin => origin.Y)
                    .Take(8)
                    .ToList();
                if (requiredWidth <= 1)
                {
                    foreach (Tile2i origin in ordered.Take(2))
                        approaches.Add(new PlannedTowerApproach(
                            ghost, access,
                            new Dictionary<Tile2i, AccessHeightProfile>
                            {
                                [origin] = profiles[origin],
                            }));
                    continue;
                }

                var pairedApproaches = new List<PlannedTowerApproach>();
                var seenPairs = new HashSet<string>(StringComparer.Ordinal);
                foreach (Tile2i origin in ordered)
                {
                    foreach (RelTile2i delta in new[]
                    {
                        new RelTile2i(4, 0),
                        new RelTile2i(0, 4),
                    })
                    {
                        Tile2i adjacent = origin + delta;
                        if (!profiles.TryGetValue(
                                adjacent, out AccessHeightProfile adjacentProfile))
                            continue;
                        string key = $"{origin.X},{origin.Y}:{adjacent.X},{adjacent.Y}";
                        if (!seenPairs.Add(key))
                            continue;
                        pairedApproaches.Add(new PlannedTowerApproach(
                            ghost, access,
                            new Dictionary<Tile2i, AccessHeightProfile>
                            {
                                [origin] = profiles[origin],
                                [adjacent] = adjacentProfile,
                            }));
                    }
                }
                approaches.AddRange(pairedApproaches
                    .OrderBy(item => item.Profiles.Keys.Min(origin =>
                        DistanceSquared(origin + new RelTile2i(2, 2), seed)))
                    .Take(2));
            }
            return approaches;
        }

        private static bool TryBuildNaturalApproachProfile(
            IAreaManagingTower tower,
            Tile2i origin,
            Tile2i approachSeed,
            TerrainManager terrMgr,
            IPathabilityProvider pathability,
            VehiclePathFindingParams pathParams,
            HashSet<Tile2i> reachableApproachGround,
            bool allowOutsideArea,
            out AccessHeightProfile profile)
        {
            profile = default;
            if ((!allowOutsideArea && !IsOriginInsideTower(tower, origin))
                || DistanceSquared(origin + new RelTile2i(2, 2), approachSeed)
                    > PLANNED_TOWER_APPROACH_RADIUS
                        * PLANNED_TOWER_APPROACH_RADIUS)
                return false;
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    Tile2i tile = origin + new RelTile2i(x, y);
                    if (s_buildingOccupiedTiles.Contains(tile)
                        || !reachableApproachGround.Contains(tile)
                        || !pathability.IsPathable(
                            tile, pathParams.PathabilityQueryMask))
                        return false;
                }
            }

            if (!TryRoundedCorner(origin, out int nw)
                || !TryRoundedCorner(origin + new RelTile2i(4, 0), out int ne)
                || !TryRoundedCorner(origin + new RelTile2i(4, 4), out int se)
                || !TryRoundedCorner(origin + new RelTile2i(0, 4), out int sw))
                return false;
            var candidate = new AccessHeightProfile(
                nw * 2, ne * 2, se * 2, sw * 2);
            bool supported = false;
            foreach (AccessSearchMode mode in new[]
            {
                AccessSearchMode.Flat,
                AccessSearchMode.XPositive,
                AccessSearchMode.XNegative,
                AccessSearchMode.YPositive,
                AccessSearchMode.YNegative,
            })
            {
                if (AccessHeightProfile.TryForMode(
                        mode, candidate.Center2,
                        out AccessHeightProfile expected)
                    && expected.Nw2 == candidate.Nw2
                    && expected.Ne2 == candidate.Ne2
                    && expected.Se2 == candidate.Se2
                    && expected.Sw2 == candidate.Sw2)
                {
                    supported = true;
                    break;
                }
            }
            if (!supported)
                return false;

            for (int x = 0; x <= 4; x++)
            {
                for (int y = 0; y <= 4; y++)
                {
                    Tile2i tile = origin + new RelTile2i(x, y);
                    float actual = terrMgr.GetHeight(tile).Value.ToFloat();
                    float expected = candidate.GetHeight2NumeratorAt(x, y) / 32f;
                    if (Math.Abs(actual - expected)
                        > PLANNED_TOWER_TERRAIN_EPSILON)
                        return false;
                }
            }
            profile = candidate;
            return true;

            bool TryRoundedCorner(Tile2i tile, out int rounded)
            {
                rounded = 0;
                if (!terrMgr.IsValidCoord(tile))
                    return false;
                float height = terrMgr.GetHeight(tile).Value.ToFloat();
                rounded = (int)Math.Round(
                    height, MidpointRounding.AwayFromZero);
                return Math.Abs(height - rounded)
                    <= PLANNED_TOWER_TERRAIN_EPSILON;
            }
        }

        private static HashSet<Tile2i> BuildReachableApproachGround(
            Tile2i seed,
            IPathabilityProvider pathability,
            VehiclePathFindingParams pathParams)
        {
            var reached = new HashSet<Tile2i>();
            if (!pathability.IsPathable(
                    seed, pathParams.PathabilityQueryMask))
                return reached;
            int radius = PLANNED_TOWER_APPROACH_RADIUS + 4;
            var queue = new Queue<Tile2i>();
            reached.Add(seed);
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                foreach (RelTile2i direction in
                    s_experimentalGroundDirections)
                {
                    Tile2i next = current + direction;
                    if (Math.Abs(next.X - seed.X) > radius
                        || Math.Abs(next.Y - seed.Y) > radius
                        || reached.Contains(next)
                        || !pathability.IsPathable(
                            next, pathParams.PathabilityQueryMask))
                        continue;
                    reached.Add(next);
                    queue.Enqueue(next);
                }
            }
            return reached;
        }

        private static Tile2i FindNearestPathableTile(
            Tile2i access,
            IPathabilityProvider pathability,
            VehiclePathFindingParams pathParams)
        {
            if (pathability.IsPathable(
                    access, pathParams.PathabilityQueryMask))
                return access;
            for (int radius = 1;
                radius <= PLANNED_TOWER_APPROACH_RADIUS;
                radius++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    Tile2i left = access + new RelTile2i(-radius, y);
                    if (pathability.IsPathable(
                            left, pathParams.PathabilityQueryMask))
                        return left;
                    Tile2i right = access + new RelTile2i(radius, y);
                    if (pathability.IsPathable(
                            right, pathParams.PathabilityQueryMask))
                        return right;
                }
                for (int x = -radius + 1; x < radius; x++)
                {
                    Tile2i bottom = access + new RelTile2i(x, -radius);
                    if (pathability.IsPathable(
                            bottom, pathParams.PathabilityQueryMask))
                        return bottom;
                    Tile2i top = access + new RelTile2i(x, radius);
                    if (pathability.IsPathable(
                            top, pathParams.PathabilityQueryMask))
                        return top;
                }
            }
            return access;
        }

        private static int DistanceSquared(Tile2i first, Tile2i second)
        {
            int dx = first.X - second.X;
            int dy = first.Y - second.Y;
            return dx * dx + dy * dy;
        }
    }
}

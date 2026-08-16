// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farming Access Ramps
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AutoTerrainDesignations.Access;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.PathFinding;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private const int FARMING_ACCESS_SEARCH_MARGIN_TILES = 96;
        private const int MAX_FARMING_ACCESS_SEARCH_TILES = 250000;
        private const int FARMING_ACCESS_RECHECK_TICKS = 10;
        private const int FARMING_ACCESS_MEDIUM_WORK_THRESHOLD = 250;
        private const int FARMING_ACCESS_LARGE_WORK_THRESHOLD = 1000;
        private const int FARMING_ACCESS_MEDIUM_RECHECK_TICKS = 30;
        private const int FARMING_ACCESS_LARGE_RECHECK_TICKS = 90;

        private sealed class FarmingAccessCluster
        {
            public int DebugId { get; set; }
            public List<TerrainDesignation> Designations { get; } = new List<TerrainDesignation>();
            public HashSet<Tile2i> Origins { get; } = new HashSet<Tile2i>();
            public bool NeedsAccess { get; set; }
            public bool HasAccess { get; set; }

            public Tile2i Anchor => Designations.Count > 0
                ? Designations[0].OriginTileCoord
                : default;

            public int Count => Designations.Count;

            public void Add(TerrainDesignation designation)
            {
                Designations.Add(designation);
                Origins.Add(designation.OriginTileCoord);
            }
        }

        private sealed class FarmingManagedAccessResult
        {
            public RampPlacementOutcome Outcome { get; }
            public Tile2i TopTile { get; }
            public IReadOnlyList<Tile2i> PlacedOrigins { get; }

            public FarmingManagedAccessResult(
                RampPlacementOutcome outcome,
                Tile2i topTile,
                IReadOnlyList<Tile2i> placedOrigins)
            {
                Outcome = outcome;
                TopTile = topTile;
                PlacedOrigins = placedOrigins;
            }
        }

        private static bool EnsureFarmingAccessForCurrentPhase(
            IAreaManagingTower tower,
            FarmingPreparationSession session,
            bool isFilling)
        {
            session.LastAccessRampDetail = string.Empty;
            AccessFailureRetryState failureRetry = GetFarmingAccessRetryState(
                session,
                isFilling);

            if (s_desigManager == null)
                return true;

            TerrainDesignationProto? defaultRampProto = isFilling ? s_dumpingProto : s_miningProto;
            if (defaultRampProto == null)
            {
                failureRetry.Clear();
                session.LastAccessRampDetail = "Access ramp skipped: ramp designation proto unavailable.";
                return false;
            }

            List<TerrainDesignation> currentWork =
                CollectCurrentFarmingAccessWork(session, isFilling);

            if (currentWork.Count == 0)
            {
                CancelFarmingAccessRequest(
                    session, isFilling, "OwnerWorkCompleted");
                failureRetry.Clear();
                if (isFilling && HasQueuedFarmingFillingOrigins(session))
                    return true;

                int removed = RemoveOwnedFarmingAccessRamps(session, isFilling);
                if (removed > 0)
                {
                    string cleanupMode = isFilling ? "dumping" : "excavation";
                    session.LastAccessRampDetail = $"Removed {removed} stale {cleanupMode} access ramp designation(s).";
                }

                return true;
            }

            // Rim alignment designations were placed this tick but the terrain they target has not
            // been raised yet. The BFS uses actual terrain pathability, so it cannot see the future
            // path through the rim and may route a filling ramp in the wrong direction (e.g. into
            // the sea on the cliff side). Wait for the rim to be built before placing any ramp.
            if (isFilling && session.RimAlignmentOrigins.Count > 0)
            {
                failureRetry.Clear();
                session.LastAccessRampDetail =
                    "Filling access: waiting for rim alignment designations to be built before placing ramps.";
                return false;
            }

            string workKey = BuildFarmingAccessWorkKey(currentWork, isFilling);
            var towerSettings = GetOrCreateTowerSettings(tower);
            string failureFingerprint = BuildFarmingAccessFailureFingerprint(
                tower,
                workKey,
                towerSettings.RampWidth,
                towerSettings.VehicleClearance);
            double nowSeconds = GetFarmingAccessRealtimeSeconds();
            AccessFailureRetryDecision retryDecision = failureRetry.Evaluate(
                failureFingerprint,
                nowSeconds,
                s_farmingAutomationTickIndex);
            if (!retryDecision.ShouldAttempt)
            {
                string waitMode = isFilling ? "dumping" : "excavation";
                session.LastAccessRampDetail =
                    $"Access ramp search for {waitMode} is waiting after a failed attempt; "
                    + $"retry eligible in {System.Math.Ceiling(retryDecision.RetryAfterSeconds):0} second(s) "
                    + $"or after the minimum grace when relevant work changes.";
                SetFarmingAccessCache(
                    session,
                    workKey,
                    ready: false,
                    session.LastAccessRampDetail);
                return false;
            }

            bool retryingFailedObligation = failureRetry.HasFailure;
            if (!retryingFailedObligation
                && TryUseCachedFarmingAccessResult(session, workKey, currentWork.Count, out bool cachedReady))
                return cachedReady;

            Stopwatch accessSw = Stopwatch.StartNew();
            if (!TryFindInaccessibleFarmingAccessClusters(tower, currentWork, isFilling, out List<FarmingAccessCluster> inaccessibleClusters))
            {
                failureRetry.Clear();
                accessSw.Stop();
                LogFarmingPerfIfSlow(session, tower, "access check", accessSw.ElapsedMilliseconds, $"mode={(isFilling ? "filling" : "preparation")}, work={currentWork.Count}, inaccessible=unknown");
                SetFarmingAccessCache(session, workKey, ready: true, string.Empty);
                return true;
            }
            accessSw.Stop();

            // Merge spatially adjacent inaccessible clusters into super-clusters so that one
            // ramp serves the entire contiguous group. Without this, each sub-cluster would
            // get its own ramp, which often points into an adjacent cluster's footprint — an
            // area also being prepared — rather than to a stable external surface.
            inaccessibleClusters = MergeAdjacentInaccessibleClusters(inaccessibleClusters);

            // Order clusters greedily by Manhattan distance from cluster anchor to tower's
            // bounding-box center, ascending. Non-selected clusters remain fixed navigation
            // context: a route may enter or cross them, but they are not source obligations
            // for the selected cluster's request.
            Tile2i towerCenterForOrdering = new Tile2i(
                (tower.Area.BoundingBoxMin.X + tower.Area.BoundingBoxMax.X) / 2,
                (tower.Area.BoundingBoxMin.Y + tower.Area.BoundingBoxMax.Y) / 2);
            inaccessibleClusters.Sort((a, b) =>
            {
                int da = System.Math.Abs(a.Anchor.X - towerCenterForOrdering.X) + System.Math.Abs(a.Anchor.Y - towerCenterForOrdering.Y);
                int db = System.Math.Abs(b.Anchor.X - towerCenterForOrdering.X) + System.Math.Abs(b.Anchor.Y - towerCenterForOrdering.Y);
                return da.CompareTo(db);
            });

            int inaccessibleCount = inaccessibleClusters.Sum(cluster => cluster.Count);
            LogFarmingPerfIfSlow(session, tower, "access check", accessSw.ElapsedMilliseconds, $"mode={(isFilling ? "filling" : "preparation")}, work={currentWork.Count}, inaccessible={inaccessibleCount}, clusters={inaccessibleClusters.Count}");
            LogDebug($"[ATD Farming Access] mode={(isFilling ? "filling" : "preparation")} work={currentWork.Count} inaccessibleClusters={inaccessibleClusters.Count} inaccessibleOrigins={inaccessibleCount} (after adjacency merge).");

            if (inaccessibleClusters.Count == 0)
            {
                failureRetry.Clear();
                LogDebug($"[ATD Farming Access] mode={(isFilling ? "filling" : "preparation")} all clusters have access.");
                // Proactively remove any stale filling ramps now that the fill area is accessible.
                if (isFilling)
                    RemoveOwnedFarmingAccessRamps(session, isFilling: true);
                SetFarmingAccessCache(session, workKey, ready: true, string.Empty);
                return true;
            }

            if (towerSettings.RampWidth <= 0)
            {
                failureRetry.Clear();
                session.LastAccessRampDetail = $"Access ramp needed for {inaccessibleCount} origin(s), but ramp generation is disabled.";
                SetFarmingAccessCache(session, workKey, ready: false, session.LastAccessRampDetail);
                return false;
            }

            // Reserve this session's active work-phase origins so ramps don't overwrite designations
            // currently being prepared. Hidden origins (ReadyForFilling/Done) are intentionally NOT
            // reserved — ramps must be allowed to pass through already-completed tiles to reach an
            // inaccessible cluster that is surrounded by finished neighbours.
            // All origins from other sessions are reserved regardless of phase to prevent ramps from
            // corrupting another session's farming tracking.
            var reservedRampTiles = new HashSet<Tile2i>(
                session.Origins
                    .Where(kvp => IsFarmingAccessWorkPhase(kvp.Value.Phase, isFilling))
                    .Select(kvp => kvp.Key));
            foreach (Tile2i rimOrigin in session.RimAlignmentOrigins)
                reservedRampTiles.Add(rimOrigin);
            foreach (Tile2i cleanupOrigin in session.FutureRimDebrisCleanupOrigins)
                reservedRampTiles.Add(cleanupOrigin);
            foreach (FarmingPreparationSession otherSession in s_farmingPreparationSessions.Values)
            {
                if (otherSession == session)
                    continue;
                foreach (Tile2i otherOrigin in otherSession.Origins.Keys)
                    reservedRampTiles.Add(otherOrigin);
            }

            string mode = isFilling ? "dumping" : "excavation";
            HashSet<Tile2i> ownedRamps = GetOwnedFarmingAccessRamps(session, isFilling);
            // A generated accessway is expected to be unreachable while its terrain work is
            // still pending. Retain every compatible designation until the game fulfills or
            // replaces it; current-terrain BFS cannot invalidate a projected plan.
            bool retiredOwnedAccessway = PruneInactiveOwnedRamps(
                ownedRamps, isFilling);
            // Also reserve ramps already placed in previous ticks so we never double-stack.
            foreach (Tile2i existingRamp in ownedRamps)
                reservedRampTiles.Add(existingRamp);

            var hasPendingOwnedAccessway = new List<bool>(
                inaccessibleClusters.Count);
            foreach (FarmingAccessCluster cluster in inaccessibleClusters)
            {
                TerrainDesignationProto? clusterRampProto =
                    GetFarmingAccessRampProtoForCluster(
                        cluster.Designations,
                        isFilling,
                        defaultRampProto);
                hasPendingOwnedAccessway.Add(
                    clusterRampProto != null
                    && AttachSurfaceAlreadyHasOwnedRamp(
                            cluster.Designations,
                            ownedRamps,
                            clusterRampProto));
            }
            int nextClusterIndex = SelectNextFarmingAccessClusterIndex(
                hasPendingOwnedAccessway);
            var projectedProviderGoalOrigins = new HashSet<Tile2i>();
            foreach (int clusterIndex in SelectProjectedProviderGoalClusterIndices(
                hasPendingOwnedAccessway, nextClusterIndex))
            {
                foreach (TerrainDesignation designation
                    in inaccessibleClusters[clusterIndex].Designations)
                {
                    projectedProviderGoalOrigins.Add(
                        designation.OriginTileCoord);
                }
            }
            if (nextClusterIndex < 0 && ownedRamps.Count > 0)
            {
                session.LastAccessRampDetail =
                    $"Accessway terrain work is pending at {ownedRamps.Count} designation(s); "
                    + "every inaccessible cluster already has a pending provider.";
                SetFarmingAccessCache(
                    session,
                    workKey,
                    ready: false,
                    session.LastAccessRampDetail);
                return false;
            }

            if (retiredOwnedAccessway)
            {
                failureRetry.RecordFailure(
                    failureFingerprint,
                    GetFarmingAccessRealtimeSeconds(),
                    s_farmingAutomationTickIndex);
                session.LastAccessRampDetail =
                    "The previous accessway is no longer pending but the farming work "
                    + "is still inaccessible; waiting before replanning.";
                SetFarmingAccessCache(
                    session,
                    workKey,
                    ready: false,
                    session.LastAccessRampDetail);
                return false;
            }

            if (s_accesswayManager == null)
            {
                session.LastAccessRampDetail =
                    "Accessway manager is unavailable; farming access fails closed.";
                SetFarmingAccessCache(
                    session,
                    workKey,
                    ready: false,
                    session.LastAccessRampDetail);
                return false;
            }

            return EnsureManagedFarmingAccess(
                tower,
                session,
                isFilling,
                workKey,
                failureFingerprint,
                failureRetry,
                inaccessibleClusters,
                nextClusterIndex,
                inaccessibleCount,
                reservedRampTiles,
                ownedRamps,
                projectedProviderGoalOrigins,
                towerSettings);
        }

        private static bool EnsureManagedFarmingAccess(
            IAreaManagingTower tower,
            FarmingPreparationSession session,
            bool isFilling,
            string workKey,
            string failureFingerprint,
            AccessFailureRetryState failureRetry,
            List<FarmingAccessCluster> inaccessibleClusters,
            int nextClusterIndex,
            int inaccessibleCount,
            HashSet<Tile2i> reservedRampTiles,
            HashSet<Tile2i> ownedRamps,
            HashSet<Tile2i> projectedProviderGoalOrigins,
            ATDTowerSettings towerSettings)
        {
            string mode = isFilling ? "dumping" : "excavation";
            string requestFingerprint = BuildFarmingAccessRampRequestKey(
                    inaccessibleClusters,
                    isFilling,
                    towerSettings.VehicleClearance)
                + "|r=" + (reservedRampTiles.Count / 50);
            ATDAccesswayRequestHandle? existing = isFilling
                ? session.FillingAccessRequest
                : session.PreparationAccessRequest;
            if (existing != null)
            {
                ATDAccesswayHandleSnapshot snapshot =
                    ReadAccesswayRequest(existing);
                if (!snapshot.IsTerminal
                    && string.Equals(
                        existing.WorkFingerprint,
                        requestFingerprint,
                        System.StringComparison.Ordinal))
                {
                    session.LastAccessRampDetail =
                        $"Managed accessway search for {mode} is {snapshot.State.ToString().ToLowerInvariant()}; "
                        + $"visited {snapshot.VisitedNodes:N0}, queue {snapshot.PendingNodes:N0}.";
                    SetFarmingAccessCache(
                        session,
                        workKey,
                        ready: false,
                        session.LastAccessRampDetail);
                    return false;
                }

                if (snapshot.IsTerminal)
                {
                    SetFarmingAccessRequest(session, isFilling, null);
                    FarmingManagedAccessResult? payload =
                        snapshot.Result?.Payload as FarmingManagedAccessResult;
                    if (snapshot.State == ATDAccesswayRequestState.Succeeded
                        && payload != null)
                    {
                        AdoptTerminalFarmingAccessOwnership(
                            snapshot, ownedRamps);
                        session.LastAccessRampRequestKey = requestFingerprint;
                        if (payload.PlacedOrigins.Count > 0)
                        {
                            failureRetry.Clear();
                            session.LastAccessRampDetail =
                                $"Managed accessway placed for {inaccessibleCount} unreachable {mode} "
                                + $"origin(s): {payload.Outcome} at "
                                + $"({payload.TopTile.X},{payload.TopTile.Y}); "
                                + $"pending terrain designations={payload.PlacedOrigins.Count}.";
                        }
                        else
                        {
                            failureRetry.RecordFailure(
                                failureFingerprint,
                                GetFarmingAccessRealtimeSeconds(),
                                s_farmingAutomationTickIndex);
                            session.LastAccessRampDetail =
                                "Managed access found an existing projected provider; "
                                + "waiting for its terrain work before re-evaluation.";
                        }
                        LogInfo(
                            $"[ATD Access Manager] id={existing.RequestId} "
                            + $"owner={existing.OwnerKey} state=succeeded "
                            + $"placed={payload.PlacedOrigins.Count} "
                            + $"processingMs={snapshot.ProcessingMilliseconds:0.##}");

                        if (ShouldContinueFarmingAccessAfterTerminalSuccess(
                                payload.PlacedOrigins.Count,
                                inaccessibleClusters.Count))
                        {
                            ClearFarmingAccessCache(session);
                            LogDebug(
                                "[ATD Farming Access] Provider placed; immediately "
                                + "re-evaluating remaining inaccessible clusters.");
                            return EnsureFarmingAccessForCurrentPhase(
                                tower, session, isFilling);
                        }
                    }
                    else
                    {
                        string reason = snapshot.Result?.Reason
                            ?? snapshot.State.ToString();
                        bool stoppedByUser = snapshot.State
                                == ATDAccesswayRequestState.Cancelled
                            && string.Equals(
                                reason,
                                "UserCancelled",
                                System.StringComparison.Ordinal);
                        if (stoppedByUser)
                        {
                            SetFarmingAccessSuppressedByUser(
                                session, isFilling, suppressed: true);
                            failureRetry.Clear();
                            session.LastAccessRampDetail =
                                $"Automatic farming access for {mode} was stopped by the user; "
                                + "disable and re-enable farming automation to resume it.";
                        }
                        else
                        {
                            failureRetry.RecordFailure(
                                failureFingerprint,
                                GetFarmingAccessRealtimeSeconds(),
                                s_farmingAutomationTickIndex);
                            session.LastAccessRampDetail =
                                $"Managed accessway search for {mode} ended: {reason}; "
                                + "waiting before retry.";
                        }
                        LogInfo(
                            $"[ATD Access Manager] id={existing.RequestId} "
                            + $"owner={existing.OwnerKey} state={snapshot.State} "
                            + $"reason={reason} "
                            + $"processingMs={snapshot.ProcessingMilliseconds:0.##}");
                    }
                    SetFarmingAccessCache(
                        session,
                        workKey,
                        ready: false,
                        session.LastAccessRampDetail);
                    return false;
                }
            }

            if (IsFarmingAccessSuppressedByUser(session, isFilling))
            {
                session.LastAccessRampDetail =
                    $"Automatic farming access for {mode} is stopped; "
                    + "disable and re-enable farming automation to resume it.";
                SetFarmingAccessCache(
                    session,
                    workKey,
                    ready: false,
                    session.LastAccessRampDetail);
                return false;
            }

            if (!AutoTerrainDesignationsMod.TurningRampsEnabled)
            {
                session.LastAccessRampDetail =
                    "Managed farming access requires Turning ramps; "
                    + "legacy generation is not used by the accessway manager.";
                SetFarmingAccessCache(
                    session,
                    workKey,
                    ready: false,
                    session.LastAccessRampDetail);
                return false;
            }
            if (!TryGetTowerEntityId(tower, out var towerId))
            {
                session.LastAccessRampDetail =
                    "Managed farming access cannot identify its tower owner.";
                SetFarmingAccessCache(
                    session,
                    workKey,
                    ready: false,
                    session.LastAccessRampDetail);
                return false;
            }

            FarmingAccessCluster cluster =
                inaccessibleClusters[nextClusterIndex];
            TerrainDesignationProto? defaultRampProto = isFilling
                ? s_dumpingProto
                : s_miningProto;
            TerrainDesignationProto? clusterRampProto = defaultRampProto == null
                ? null
                : GetFarmingAccessRampProtoForCluster(
                    cluster.Designations,
                    isFilling,
                    defaultRampProto);
            if (clusterRampProto == null || s_desigManager == null)
            {
                session.LastAccessRampDetail =
                    "Managed farming access cannot resolve its terrain-work prototype.";
                SetFarmingAccessCache(
                    session,
                    workKey,
                    ready: false,
                    session.LastAccessRampDetail);
                return false;
            }

            var attachDesignations = new List<TerrainDesignation>(
                cluster.Designations);
            var tileDepths = new Dict<Tile2i, int>();
            var cornerHeights = new Dict<Tile2i, int>();
            foreach (TerrainDesignation designation in cluster.Designations)
                AddFarmingRampPlanTile(
                    designation, tileDepths, cornerHeights);
            if (!isFilling
                && s_dumpingProto != null
                && clusterRampProto == s_dumpingProto)
            {
                AddConnectedPreparationShouldersToRampPlan(
                    session,
                    cluster.Designations,
                    tileDepths,
                    cornerHeights,
                    attachDesignations);
            }

            int requestedRampWidth = towerSettings.RampWidth;
            if (GetTowerVehicleClearance(tower)
                == AccessVehicleClearanceMode.Auto)
            {
                VehiclePathFindingParams pathParams =
                    GetExcavatorPathFindingParamsForTower(tower, out _);
                if (GetVehicleClearance(pathParams) >= 3)
                    requestedRampWidth = System.Math.Max(
                        requestedRampWidth, 2);
            }
            int configuredRampWidth = cluster.Count < requestedRampWidth
                ? 1
                : requestedRampWidth;
            var placedOrigins = new List<Tile2i>();
            var rampResult = new RampGenerationResult();
            var reservedSnapshot = new HashSet<Tile2i>(reservedRampTiles);
            var contextOnlyTerrainWorkOrigins = new HashSet<Tile2i>(
                session.Origins
                    .Where(pair => IsFarmingAccessWorkPhase(
                        pair.Value.Phase, isFilling))
                    .Select(pair => pair.Key));
            contextOnlyTerrainWorkOrigins.ExceptWith(cluster.Origins);
            string ownerKey = (isFilling ? "farm-fill/tower:" : "farm-prep/tower:")
                + towerId;
            int requestWorldGeneration = CurrentWorldGeneration;
            int expectedRampWidth = towerSettings.RampWidth;
            AccessVehicleClearanceMode expectedClearanceMode =
                towerSettings.VehicleClearance;
            int expectedPlanningSettings =
                AutoTerrainDesignationsMod.AccessPlanningSettingsFingerprint;
            Tile2i expectedAreaMin = tower.Area.BoundingBoxMin;
            Tile2i expectedAreaMax = tower.Area.BoundingBoxMax;
            int validationWatchMargin = FARMING_ACCESS_SEARCH_MARGIN_TILES
                + AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance
                + AutoTerrainDesignationsMod.AccessRayEndBuffer;
            Tile2i validationWatchMin = new Tile2i(
                expectedAreaMin.X - validationWatchMargin,
                expectedAreaMin.Y - validationWatchMargin);
            Tile2i validationWatchMax = new Tile2i(
                expectedAreaMax.X + validationWatchMargin,
                expectedAreaMax.Y + validationWatchMargin);
            long validatedDesignationRevision =
                CurrentTerrainDesignationRevision;
            ATDAccesswayValidationResult ValidateLiveRequest()
            {
                if (!IsWorldGenerationActive(requestWorldGeneration))
                    return ATDAccesswayValidationResult.OwnerGone(
                        "WorldGenerationChanged");
                if (!TryGetTowerEntityId(tower, out var liveTowerId)
                    || liveTowerId != towerId
                    || !s_farmingPreparationSessions.TryGetValue(
                        towerId, out FarmingPreparationSession liveSession)
                    || !ReferenceEquals(liveSession, session)
                    || !session.Enabled)
                    return ATDAccesswayValidationResult.OwnerGone(
                        "FarmingOwnerGone");

                ATDTowerSettings liveSettings =
                    GetOrCreateTowerSettings(tower);
                if (!AutoTerrainDesignationsMod.TurningRampsEnabled
                    || AutoTerrainDesignationsMod
                        .AccessPlanningSettingsFingerprint
                        != expectedPlanningSettings
                    || liveSettings.RampWidth != expectedRampWidth
                    || liveSettings.VehicleClearance
                        != expectedClearanceMode
                    || tower.Area.BoundingBoxMin != expectedAreaMin
                    || tower.Area.BoundingBoxMax != expectedAreaMax)
                    return ATDAccesswayValidationResult.Stale(
                        "AccessModeChanged");

                long liveRevision = CurrentTerrainDesignationRevision;
                if (liveRevision == validatedDesignationRevision)
                    return ATDAccesswayValidationResult.Current();
                bool relevantDesignationChanged =
                    HasTerrainDesignationMutationSince(
                        validatedDesignationRevision,
                        validationWatchMin,
                        validationWatchMax);
                validatedDesignationRevision = liveRevision;
                if (!relevantDesignationChanged)
                    return ATDAccesswayValidationResult.Current();

                List<TerrainDesignation> liveWork =
                    CollectCurrentFarmingAccessWork(session, isFilling);
                if (liveWork.Count == 0)
                    return ATDAccesswayValidationResult.OwnerGone(
                        "OwnerWorkCompleted");
                string liveWorkKey = BuildFarmingAccessWorkKey(
                    liveWork, isFilling);
                string liveFingerprint =
                    BuildFarmingAccessFailureFingerprint(
                        tower,
                        liveWorkKey,
                        liveSettings.RampWidth,
                        liveSettings.VehicleClearance);
                if (!string.Equals(
                        liveFingerprint,
                        failureFingerprint,
                        System.StringComparison.Ordinal))
                    return ATDAccesswayValidationResult.Stale(
                        "FarmingWorkChanged");
                return ATDAccesswayValidationResult.Stale(
                    "NearbyDesignationChanged");
            }
            var request = new ATDAccesswayRequest(
                ownerKey,
                requestFingerprint,
                isFilling
                    ? ATDAccesswayRequestKind.FarmingFilling
                    : ATDAccesswayRequestKind.FarmingPreparation,
                ATDAccesswayPriority.Derived,
                () => new ATDAccesswayCoroutineWork(
                    sliceControl => CreateAccessRampCoroutine(
                        tower,
                        tileDepths,
                        cornerHeights,
                        s_desigManager.TerrainManager,
                        configuredRampWidth,
                        clusterRampProto,
                        placedOrigins,
                        reservedSnapshot,
                        useLocalSurfaceReference: isFilling
                            || (s_dumpingProto != null
                                && clusterRampProto == s_dumpingProto),
                        allowExistingPlannedRampShortcut: false,
                        result: rampResult,
                        contextOnlyTerrainWorkOrigins:
                            contextOnlyTerrainWorkOrigins,
                        projectedProviderGoalOrigins:
                            projectedProviderGoalOrigins,
                        emitNoCandidateWarnings: false,
                        newPlannerOnly: true,
                        sliceControl: sliceControl),
                    () =>
                    {
                        var payload = new FarmingManagedAccessResult(
                            rampResult.Outcome,
                            rampResult.TopRowTile,
                            placedOrigins.ToArray());
                        return rampResult.Outcome == RampPlacementOutcome.Crested
                                || rampResult.Outcome
                                    == RampPlacementOutcome.Truncated
                            ? ATDAccesswayRequestResult.Succeeded(payload)
                            : ATDAccesswayRequestResult.Failed(
                                rampResult.Outcome.ToString(), payload);
                    },
                    GetManagedAccesswaySliceBudgetMilliseconds),
                ValidateLiveRequest);
            ATDAccesswayRequestHandle handle = EnqueueAccesswayRequest(request);
            SetFarmingAccessRequest(session, isFilling, handle);
            session.LastAccessRampDetail =
                $"Managed accessway search queued for {cluster.Count} {mode} origin(s).";
            SetFarmingAccessCache(
                session,
                workKey,
                ready: false,
                session.LastAccessRampDetail);
            return false;
        }

        private static void SetFarmingAccessRequest(
            FarmingPreparationSession session,
            bool isFilling,
            ATDAccesswayRequestHandle? handle)
        {
            if (isFilling)
                session.FillingAccessRequest = handle;
            else
                session.PreparationAccessRequest = handle;
        }

        private static List<TerrainDesignation>
            CollectCurrentFarmingAccessWork(
                FarmingPreparationSession session,
                bool isFilling)
        {
            var currentWork = new List<TerrainDesignation>();
            if (s_desigManager == null)
                return currentWork;

            foreach (FarmingOriginSession originState
                in session.Origins.Values)
            {
                if (!IsFarmingAccessWorkPhase(
                        originState.Phase, isFilling))
                    continue;
                var currentDesignation = s_desigManager.GetDesignationAt(
                    originState.Origin);
                if (currentDesignation.HasValue
                    && IsFarmingAccessDesignationForCurrentPhase(
                        currentDesignation.Value,
                        originState,
                        isFilling))
                    currentWork.Add(currentDesignation.Value);
            }
            return currentWork;
        }

        private static int AdoptTerminalFarmingAccessOwnership(
            ATDAccesswayHandleSnapshot snapshot,
            HashSet<Tile2i> ownedOrigins)
        {
            if (snapshot.State != ATDAccesswayRequestState.Succeeded
                || !(snapshot.Result?.Payload
                    is FarmingManagedAccessResult payload))
                return 0;

            int adopted = 0;
            foreach (Tile2i origin in payload.PlacedOrigins)
                if (ownedOrigins.Add(origin))
                    adopted++;
            return adopted;
        }

        private static void CancelFarmingAccessRequest(
            FarmingPreparationSession session,
            bool isFilling,
            string reason)
        {
            ATDAccesswayRequestHandle? handle = isFilling
                ? session.FillingAccessRequest
                : session.PreparationAccessRequest;
            CancelAccesswayRequest(handle, reason);
            if (handle != null)
            {
                ATDAccesswayHandleSnapshot snapshot =
                    ReadAccesswayRequest(handle);
                int adopted = AdoptTerminalFarmingAccessOwnership(
                    snapshot,
                    GetOwnedFarmingAccessRamps(session, isFilling));
                if (adopted > 0)
                {
                    LogExperimentalAccessDebug(
                        $"[ATD Farming Access] lifecycle={reason} "
                        + $"adoptedCommittedOrigins={adopted} "
                        + $"phase={(isFilling ? "filling" : "preparation")}");
                }
            }
            SetFarmingAccessRequest(session, isFilling, null);
        }

        private static void CancelAllFarmingAccessRequests(
            FarmingPreparationSession session,
            string reason)
        {
            CancelFarmingAccessRequest(session, isFilling: false, reason);
            CancelFarmingAccessRequest(session, isFilling: true, reason);
        }

        private static AccessFailureRetryState GetFarmingAccessRetryState(
            FarmingPreparationSession session,
            bool isFilling)
            => isFilling
                ? session.FillingAccessRetry
                : session.PreparationAccessRetry;

        private static bool IsFarmingAccessSuppressedByUser(
            FarmingPreparationSession session,
            bool isFilling)
            => isFilling
                ? session.FillingAccessSuppressedByUser
                : session.PreparationAccessSuppressedByUser;

        private static void SetFarmingAccessSuppressedByUser(
            FarmingPreparationSession session,
            bool isFilling,
            bool suppressed)
        {
            if (isFilling)
                session.FillingAccessSuppressedByUser = suppressed;
            else
                session.PreparationAccessSuppressedByUser = suppressed;
        }

        private static double GetFarmingAccessRealtimeSeconds()
            => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private static string BuildFarmingAccessFailureFingerprint(
            IAreaManagingTower tower,
            string workKey,
            int rampWidth,
            AccessVehicleClearanceMode accesswayMode)
        {
            Tile2i min = tower.Area.BoundingBoxMin;
            Tile2i max = tower.Area.BoundingBoxMax;
            return workKey
                + "|width=" + rampWidth
                + "|mode=" + (int)accesswayMode
                + "|area=" + min.X + "," + min.Y + ":" + max.X + "," + max.Y;
        }

        private static string BuildFarmingAccessWorkKey(
            List<TerrainDesignation> currentWork,
            bool isFilling)
        {
            var sb = new StringBuilder();
            sb.Append(isFilling ? "fill" : "prep");
            foreach (TerrainDesignation designation in currentWork
                .OrderBy(designation => designation.OriginTileCoord.Y)
                .ThenBy(designation => designation.OriginTileCoord.X))
            {
                DesignationData data = designation.Data;
                sb.Append('|')
                    .Append(designation.Prototype.Id.Value).Append('@')
                    .Append(data.OriginTile.X).Append(',').Append(data.OriginTile.Y)
                    .Append(':')
                    .Append(data.OriginTargetHeight.Value).Append(',')
                    .Append(data.PlusXTargetHeight.Value).Append(',')
                    .Append(data.PlusXyTargetHeight.Value).Append(',')
                    .Append(data.PlusYTargetHeight.Value);
            }

            return sb.ToString();
        }

        private static bool TryUseCachedFarmingAccessResult(
            FarmingPreparationSession session,
            string workKey,
            int workCount,
            out bool ready)
        {
            ready = true;
            if (session.LastAccessCheckWorkKey != workKey)
                return false;

            int ticksSinceCheck = s_farmingAutomationTickIndex - session.LastAccessCheckTick;
            int recheckTicks = GetFarmingAccessRecheckTicks(workCount);
            if (ticksSinceCheck < 0 || ticksSinceCheck >= recheckTicks)
                return false;

            ready = session.LastAccessCheckReady;
            session.LastAccessRampDetail = session.LastAccessCheckDetail;
            return true;
        }

        private static int GetFarmingAccessRecheckTicks(int workCount)
        {
            if (workCount >= FARMING_ACCESS_LARGE_WORK_THRESHOLD)
                return FARMING_ACCESS_LARGE_RECHECK_TICKS;
            if (workCount >= FARMING_ACCESS_MEDIUM_WORK_THRESHOLD)
                return FARMING_ACCESS_MEDIUM_RECHECK_TICKS;
            return FARMING_ACCESS_RECHECK_TICKS;
        }

        private static void SetFarmingAccessCache(
            FarmingPreparationSession session,
            string workKey,
            bool ready,
            string detail)
        {
            session.LastAccessCheckWorkKey = workKey;
            session.LastAccessCheckReady = ready;
            session.LastAccessCheckDetail = detail;
            session.LastAccessCheckTick = s_farmingAutomationTickIndex;
        }

        private static void ClearFarmingAccessCache(FarmingPreparationSession session)
        {
            session.LastAccessCheckWorkKey = string.Empty;
            session.LastAccessCheckReady = true;
            session.LastAccessCheckDetail = string.Empty;
            session.LastAccessCheckTick = int.MinValue;
        }

        private static string BuildFarmingAccessRampRequestKey(
            List<FarmingAccessCluster> inaccessibleClusters,
            bool isFilling,
            AccessVehicleClearanceMode accesswayMode)
        {
            var sb = new StringBuilder();
            sb.Append(isFilling ? "fill" : "prep")
                .Append("|mode=").Append((int)accesswayMode);
            foreach (TerrainDesignation designation in inaccessibleClusters
                .SelectMany(cluster => cluster.Designations)
                .OrderBy(designation => designation.OriginTileCoord.Y)
                .ThenBy(designation => designation.OriginTileCoord.X))
            {
                DesignationData data = designation.Data;
                sb.Append('|')
                    .Append(designation.Prototype.Id.Value).Append('@')
                    .Append(data.OriginTile.X).Append(',').Append(data.OriginTile.Y)
                    .Append(':')
                    .Append(data.OriginTargetHeight.Value).Append(',')
                    .Append(data.PlusXTargetHeight.Value).Append(',')
                    .Append(data.PlusXyTargetHeight.Value).Append(',')
                    .Append(data.PlusYTargetHeight.Value);
            }

            return sb.ToString();
        }

        private static List<FarmingAccessCluster> BuildFarmingAccessClusters(
            List<TerrainDesignation> designations)
        {
            var clusters = new List<FarmingAccessCluster>();
            var remaining = new HashSet<int>();
            for (int i = 0; i < designations.Count; i++)
                remaining.Add(i);

            while (remaining.Count > 0)
            {
                var cluster = new FarmingAccessCluster();
                var queue = new Queue<int>();
                int seed = -1;
                foreach (int i in remaining) { seed = i; break; }
                remaining.Remove(seed);
                queue.Enqueue(seed);
                cluster.Add(designations[seed]);

                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    var toExpand = new List<int>();
                    foreach (int other in remaining)
                    {
                        if (AreFarmingDesignationsConnectedByNonRedEdge(designations[idx], designations[other]))
                            toExpand.Add(other);
                    }

                    foreach (int other in toExpand)
                    {
                        remaining.Remove(other);
                        queue.Enqueue(other);
                        cluster.Add(designations[other]);
                    }
                }

                cluster.DebugId = clusters.Count + 1;
                clusters.Add(cluster);
            }

            LogDebug($"[ATD Farming Access] built {clusters.Count} non-red cluster(s): {FormatFarmingAccessClusterList(clusters)}");
            return clusters;
        }

        private static bool AreFarmingDesignationsConnectedByNonRedEdge(
            TerrainDesignation first,
            TerrainDesignation second)
        {
            Tile2i a = first.OriginTileCoord;
            Tile2i b = second.OriginTileCoord;
            int dx = b.X - a.X;
            int dy = b.Y - a.Y;

            if (dx == 4 && dy == 0)
            {
                return first.Data.PlusXTargetHeight == second.Data.OriginTargetHeight
                    && first.Data.PlusXyTargetHeight == second.Data.PlusYTargetHeight;
            }

            if (dx == -4 && dy == 0)
            {
                return first.Data.OriginTargetHeight == second.Data.PlusXTargetHeight
                    && first.Data.PlusYTargetHeight == second.Data.PlusXyTargetHeight;
            }

            if (dx == 0 && dy == 4)
            {
                return first.Data.PlusYTargetHeight == second.Data.OriginTargetHeight
                    && first.Data.PlusXyTargetHeight == second.Data.PlusXTargetHeight;
            }

            if (dx == 0 && dy == -4)
            {
                return first.Data.OriginTargetHeight == second.Data.PlusYTargetHeight
                    && first.Data.PlusXTargetHeight == second.Data.PlusXyTargetHeight;
            }

            return false;
        }

        private static bool IsFarmingAccessWorkPhase(FarmingOriginPhase phase, bool isFilling)
        {
            if (isFilling)
                return phase == FarmingOriginPhase.Filling;

            return phase == FarmingOriginPhase.AnalysisLeveling
                || phase == FarmingOriginPhase.Preparing;
        }

        private static bool IsFarmingAccessDesignationForCurrentPhase(
            TerrainDesignation designation,
            FarmingOriginSession originState,
            bool isFilling)
        {
            if (!isFilling)
                return IsLevelingDesignation(designation)
                    || (originState.Phase == FarmingOriginPhase.Preparing && IsDumpingDesignation(designation));

            return IsDumpingDesignation(designation);
        }

        private static TerrainDesignationProto? GetFarmingAccessRampProtoForCluster(
            List<TerrainDesignation> cluster,
            bool isFilling,
            TerrainDesignationProto defaultRampProto)
        {
            if (isFilling)
                return s_dumpingProto;

            foreach (TerrainDesignation designation in cluster)
            {
                if (IsDumpingDesignation(designation))
                    return s_dumpingProto;
            }

            return defaultRampProto;
        }

        private static void AddFarmingRampPlanTile(
            TerrainDesignation designation,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights)
        {
            DesignationData data = designation.Data;
            tileDepths[data.OriginTile] = data.OriginTargetHeight.Value
                .Min(data.PlusXTargetHeight.Value)
                .Min(data.PlusXyTargetHeight.Value)
                .Min(data.PlusYTargetHeight.Value);
            cornerHeights[data.OriginTile] = data.OriginTargetHeight.Value;
            cornerHeights[data.PlusXTileCoord] = data.PlusXTargetHeight.Value;
            cornerHeights[data.PlusXyTileCoord] = data.PlusXyTargetHeight.Value;
            cornerHeights[data.PlusYTileCoord] = data.PlusYTargetHeight.Value;
        }

        private static void AddConnectedPreparationShouldersToRampPlan(
            FarmingPreparationSession session,
            List<TerrainDesignation> cluster,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights,
            List<TerrainDesignation> attachDesignations)
        {
            if (s_desigManager == null || session.PreparationShoulderOrigins.Count == 0)
                return;

            var queue = new Queue<Tile2i>();
            var seen = new HashSet<Tile2i>();
            foreach (TerrainDesignation designation in cluster)
            {
                Tile2i origin = designation.OriginTileCoord;
                if (seen.Add(origin))
                    queue.Enqueue(origin);
            }

            int added = 0;
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                foreach (Tile2i direction in s_cardinalDirections)
                {
                    Tile2i neighbor = Offset(current, direction);
                    if (!seen.Add(neighbor))
                        continue;

                    if (!session.PreparationShoulderOrigins.Contains(neighbor))
                        continue;

                    var shoulder = s_desigManager.GetDesignationAt(neighbor);
                    if (!shoulder.HasValue || !IsDumpingDesignation(shoulder.Value))
                        continue;

                    AddFarmingRampPlanTile(shoulder.Value, tileDepths, cornerHeights);
                    attachDesignations.Add(shoulder.Value);
                    queue.Enqueue(neighbor);
                    added++;
                }
            }

            if (added > 0)
                LogDebug($"Farming dumping-prep access: included {added} connected preparation shoulder designation(s) in ramp planning.");
        }

        private static bool AttachSurfaceAlreadyHasOwnedRamp(
            List<TerrainDesignation> attachDesignations,
            HashSet<Tile2i> ownedRamps,
            TerrainDesignationProto rampProto)
        {
            if (s_desigManager == null || ownedRamps.Count == 0)
                return false;

            TerrainDesignationProto accesswayProto = s_levelingProto ?? rampProto;

            foreach (TerrainDesignation attachDesignation in attachDesignations)
            {
                Tile2i origin = attachDesignation.OriginTileCoord;
                foreach (NeighborCoord dir in NeighborCoord.All4Neighbors)
                {
                    Tile2i neighbor = origin + new RelTile2i(dir.Dx * 4, dir.Dy * 4);
                    if (!ownedRamps.Contains(neighbor))
                        continue;

                    Option<TerrainDesignation> existing = s_desigManager.GetDesignationAt(neighbor);
                    if (existing.HasValue
                        && IsAccesswayDesignationProto(existing.Value.Prototype, accesswayProto)
                        && attachDesignation.IsSnappedTowards(dir))
                        return true;
                }
            }

            return false;
        }

        private static HashSet<Tile2i> GetOwnedFarmingAccessRamps(FarmingPreparationSession session, bool isFilling)
        {
            return isFilling
                ? session.FillingAccessRampOrigins
                : session.PreparationAccessRampOrigins;
        }

        private static int SelectNextFarmingAccessClusterIndex(
            IReadOnlyList<bool> hasPendingOwnedAccessway)
        {
            for (int index = 0;
                index < hasPendingOwnedAccessway.Count;
                index++)
            {
                if (!hasPendingOwnedAccessway[index])
                    return index;
            }
            return -1;
        }

        private static bool ShouldContinueFarmingAccessAfterTerminalSuccess(
            int placedOriginCount,
            int inaccessibleClusterCount)
            => placedOriginCount > 0 && inaccessibleClusterCount > 1;

        private static IEnumerable<int> SelectProjectedProviderGoalClusterIndices(
            IReadOnlyList<bool> hasPendingOwnedAccessway,
            int selectedClusterIndex)
        {
            for (int index = 0;
                index < hasPendingOwnedAccessway.Count;
                index++)
            {
                if (index != selectedClusterIndex
                    && hasPendingOwnedAccessway[index])
                {
                    yield return index;
                }
            }
        }

        private enum FarmingAccesswayOwnershipState
        {
            Pending,
            Retired
        }

        private static FarmingAccesswayOwnershipState ClassifyFarmingAccesswayOwnership(
            bool designationExists,
            bool hasCompatiblePrototype)
            => designationExists && hasCompatiblePrototype
                ? FarmingAccesswayOwnershipState.Pending
                : FarmingAccesswayOwnershipState.Retired;

        /// <summary>
        /// Drops tracking only after the generated designation has been fulfilled, removed, or
        /// replaced. A compatible designation remains pending regardless of current-terrain
        /// reachability because its projected terrain does not exist yet.
        /// </summary>
        private static bool PruneInactiveOwnedRamps(
            HashSet<Tile2i> ownedRamps, bool isFilling)
        {
            if (s_desigManager == null || ownedRamps.Count == 0) return false;

            TerrainDesignationProto? rampProto = isFilling ? s_dumpingProto : s_miningProto;
            if (rampProto == null) return false;
            TerrainDesignationProto accesswayProto = s_levelingProto ?? rampProto;

            bool retiredAny = false;
            foreach (Tile2i origin in ownedRamps.ToList())
            {
                Option<TerrainDesignation> desig = s_desigManager.GetDesignationAt(origin);
                FarmingAccesswayOwnershipState state =
                    ClassifyFarmingAccesswayOwnership(
                        desig.HasValue,
                        desig.HasValue
                            && IsAccesswayDesignationProto(
                                desig.Value.Prototype, accesswayProto));
                if (state == FarmingAccesswayOwnershipState.Retired)
                {
                    ownedRamps.Remove(origin);
                    retiredAny = true;
                    LogDebug(
                        $"[ATD Farming Access] Retired completed or replaced owned "
                        + $"accessway origin at ({origin.X},{origin.Y}).");
                }
            }
            return retiredAny && ownedRamps.Count == 0;
        }

        internal static bool ValidateFarmingAccesswayOwnershipFixtures(
            out string failure)
        {
            if (!ShouldContinueFarmingAccessAfterTerminalSuccess(
                    placedOriginCount: 8,
                    inaccessibleClusterCount: 2)
                || ShouldContinueFarmingAccessAfterTerminalSuccess(
                    placedOriginCount: 0,
                    inaccessibleClusterCount: 2)
                || ShouldContinueFarmingAccessAfterTerminalSuccess(
                    placedOriginCount: 8,
                    inaccessibleClusterCount: 1))
            {
                failure =
                    "A successful provider placement did not immediately continue to the next inaccessible farming cluster.";
                return false;
            }

            int[] projectedProviderClusters =
                SelectProjectedProviderGoalClusterIndices(
                    new[] { false, true, true },
                    selectedClusterIndex: 0).ToArray();
            if (!projectedProviderClusters.SequenceEqual(new[] { 1, 2 }))
            {
                failure =
                    "Pending accessways from previously served farming clusters were not exposed as projected provider goals.";
                return false;
            }

            if (SelectNextFarmingAccessClusterIndex(
                    new[] { true, false }) != 1)
            {
                failure =
                    "A pending accessway for the first farming cluster blocked the next unserved cluster.";
                return false;
            }
            if (SelectNextFarmingAccessClusterIndex(
                    new[] { true, true }) != -1
                || SelectNextFarmingAccessClusterIndex(
                    new[] { false, false }) != 0)
            {
                failure =
                    "Farming cluster selection did not wait only when every cluster already had a pending accessway.";
                return false;
            }

            if (ShouldCaptureFlatFarmingDesignation(
                    alreadyTracked: false,
                    rimAlignmentOwned: false,
                    accesswayOwned: true))
            {
                failure =
                    "A generated accessway leveling origin was reclassified as a farming intent.";
                return false;
            }
            if (ShouldAddExistingTerrainWorkEndpoint(
                    alreadyRequested: false,
                    contextOnly: true,
                    generatedAccesswayOwned: false,
                    fulfilled: false,
                    insideTower: true,
                    terrainWorkPrototype: true))
            {
                failure =
                    "A non-selected farming cluster was promoted from fixed provider context to a new access obligation.";
                return false;
            }

            if (ClassifyFarmingAccesswayOwnership(
                    designationExists: true,
                    hasCompatiblePrototype: true)
                != FarmingAccesswayOwnershipState.Pending)
            {
                failure =
                    "An unfinished compatible accessway must remain pending even before its projected terrain is reachable.";
                return false;
            }
            if (ClassifyFarmingAccesswayOwnership(
                    designationExists: false,
                    hasCompatiblePrototype: false)
                != FarmingAccesswayOwnershipState.Retired
                || ClassifyFarmingAccesswayOwnership(
                    designationExists: true,
                    hasCompatiblePrototype: false)
                != FarmingAccesswayOwnershipState.Retired)
            {
                failure =
                    "Only fulfilled, removed, or replaced accessway designations may retire ownership.";
                return false;
            }

            var terminalOrigins = new[]
            {
                new Tile2i(4, 8),
                new Tile2i(8, 8),
                new Tile2i(4, 12),
                new Tile2i(8, 12),
            };
            var terminalPayload = new FarmingManagedAccessResult(
                RampPlacementOutcome.Crested,
                terminalOrigins[0],
                terminalOrigins);
            var terminalSnapshot = new ATDAccesswayHandleSnapshot(
                ATDAccesswayRequestState.Succeeded,
                ATDAccesswayRequestResult.Succeeded(terminalPayload),
                visitedNodes: 100,
                pendingNodes: 0,
                processingMilliseconds: 12d);
            var adoptedOrigins = new HashSet<Tile2i>();
            int adopted = AdoptTerminalFarmingAccessOwnership(
                terminalSnapshot, adoptedOrigins);
            if (adopted != terminalOrigins.Length
                || !adoptedOrigins.SetEquals(terminalOrigins))
            {
                failure =
                    "A terminal managed accessway must transfer every committed origin to farming ownership before lifecycle cleanup.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static int RemoveOwnedFarmingAccessRamps(FarmingPreparationSession session, bool isFilling)
        {
            if (s_desigManager == null)
                return 0;

            TerrainDesignationProto? rampProto = isFilling ? s_dumpingProto : s_miningProto;
            if (rampProto == null)
                return 0;
            TerrainDesignationProto accesswayProto = s_levelingProto ?? rampProto;

            HashSet<Tile2i> ownedRamps = GetOwnedFarmingAccessRamps(session, isFilling);
            int removed = 0;
            foreach (Tile2i origin in ownedRamps.ToList())
            {
                var currentDesignation = s_desigManager.GetDesignationAt(origin);
                if (currentDesignation.HasValue
                    && IsAccesswayDesignationProto(currentDesignation.Value.Prototype, accesswayProto))
                {
                    s_desigManager.RemoveDesignation(origin);
                    removed++;
                }

                ownedRamps.Remove(origin);
            }

            if (removed > 0)
                session.LastAccessRampRequestKey = string.Empty;

            ClearFarmingAccessCache(session);
            return removed;
        }

        private static bool TryFindInaccessibleFarmingAccessClusters(
            IAreaManagingTower tower,
            List<TerrainDesignation> designations,
            bool isFilling,
            out List<FarmingAccessCluster> inaccessibleClusters)
        {
            inaccessibleClusters = new List<FarmingAccessCluster>();

            if (designations.Count == 0)
                return true;

            if (s_vehiclePathFindingManager == null || s_excavatorPathFindingParams == null)
            {
                Log.Warning("[ATD] Farming access check skipped because vehicle pathfinding is unavailable.");
                return false;
            }

            IPathabilityProvider pathabilityProvider = s_vehiclePathFindingManager.PathabilityProvider;
            VehiclePathFindingParams pfParams = s_excavatorPathFindingParams;

            RefreshPathabilityAndInvalidateReachability();

            Tile2i bbMin = tower.Area.BoundingBoxMin;
            Tile2i bbMax = tower.Area.BoundingBoxMax;
            Tile2i towerPosition = GetTowerPosition(tower, bbMin, bbMax);
            if (!TryFindNearestPathableTile(pathabilityProvider, pfParams, towerPosition, out Tile2i start))
            {
                inaccessibleClusters.AddRange(BuildFarmingAccessClusters(designations));
                LogDebug($"[ATD Farming Access] no pathable start near tower; treating all clusters inaccessible: {FormatFarmingAccessClusterList(inaccessibleClusters)}");
                return true;
            }

            List<FarmingAccessCluster> clusters = BuildFarmingAccessClusters(designations);
            var clusterByOrigin = new Dictionary<Tile2i, FarmingAccessCluster>(designations.Count);
            foreach (FarmingAccessCluster cluster in clusters)
            {
                cluster.NeedsAccess = true;
                foreach (Tile2i origin in cluster.Origins)
                    clusterByOrigin[origin] = cluster;
            }

            var targetTilesByOrigin = new Dictionary<Tile2i, HashSet<Tile2i>>();
            var originsByTargetTile = new Dictionary<Tile2i, List<Tile2i>>();
            var designationsByOrigin = new Dictionary<Tile2i, TerrainDesignation>(designations.Count);
            int notReadyCount = 0;
            foreach (TerrainDesignation designation in designations)
            {
                if (!IsFarmingDesignationReadyForVehicleWork(designation, isFilling))
                {
                    notReadyCount++;
                    continue;
                }

                HashSet<Tile2i> targets = BuildFarmingAccessTargetTiles(designation.OriginTileCoord, pathabilityProvider, pfParams);
                if (targets.Count > 0)
                {
                    targetTilesByOrigin[designation.OriginTileCoord] = targets;
                    designationsByOrigin[designation.OriginTileCoord] = designation;
                    foreach (Tile2i target in targets)
                    {
                        if (!originsByTargetTile.TryGetValue(target, out List<Tile2i> origins))
                        {
                            origins = new List<Tile2i>();
                            originsByTargetTile[target] = origins;
                        }

                        origins.Add(designation.OriginTileCoord);
                    }
                }
                else
                {
                    LogDebug($"[ATD Farming Access] origin=({designation.OriginTileCoord.X},{designation.OriginTileCoord.Y}) has no adjacent pathable target tiles.");
                }
            }

            if (notReadyCount > 0)
                LogDebug($"[ATD Farming Access] skipped {notReadyCount} designation(s) not ready for {(isFilling ? "filling" : "preparation")} vehicle work.");

            // If no designation passed the vanilla readiness check (IsReadyToMineNonAmphibious /
            // IsReadyToDumpNonAmphibious), vanilla will not assign any vehicle to this cluster
            // regardless of whether the perimeter tiles are physically reachable. Treat it as
            // inaccessible so a ramp is placed and excavation can actually begin.

            if (targetTilesByOrigin.Count == 0)
            {
                inaccessibleClusters.AddRange(clusters.Where(cluster => cluster.NeedsAccess));
                LogDebug($"[ATD Farming Access] no target tiles for any cluster; inaccessible={FormatFarmingAccessClusterList(inaccessibleClusters)}");
                return true;
            }

            int minX = towerPosition.X - FARMING_ACCESS_SEARCH_MARGIN_TILES;
            int minY = towerPosition.Y - FARMING_ACCESS_SEARCH_MARGIN_TILES;
            int maxX = towerPosition.X + FARMING_ACCESS_SEARCH_MARGIN_TILES;
            int maxY = towerPosition.Y + FARMING_ACCESS_SEARCH_MARGIN_TILES;
            foreach (TerrainDesignation designation in designations)
            {
                Tile2i origin = designation.OriginTileCoord;
                minX = minX.Min(origin.X - FARMING_ACCESS_SEARCH_MARGIN_TILES);
                minY = minY.Min(origin.Y - FARMING_ACCESS_SEARCH_MARGIN_TILES);
                maxX = maxX.Max(origin.X + 3 + FARMING_ACCESS_SEARCH_MARGIN_TILES);
                maxY = maxY.Max(origin.Y + 3 + FARMING_ACCESS_SEARCH_MARGIN_TILES);
            }

            var visited = new HashSet<Tile2i>();
            var queue = new Queue<Tile2i>();
            visited.Add(start);
            queue.Enqueue(start);

            var reachableOrigins = new HashSet<Tile2i>();
            while (queue.Count > 0 && visited.Count < MAX_FARMING_ACCESS_SEARCH_TILES)
            {
                Tile2i current = queue.Dequeue();

                if (originsByTargetTile.TryGetValue(current, out List<Tile2i> reachedTargets))
                {
                    foreach (Tile2i reachedOrigin in reachedTargets)
                    {
                        reachableOrigins.Add(reachedOrigin);
                        if (clusterByOrigin.TryGetValue(reachedOrigin, out FarmingAccessCluster cluster))
                        {
                            if (!cluster.HasAccess)
                                LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} reached by path target at origin=({reachedOrigin.X},{reachedOrigin.Y}).");
                            cluster.HasAccess = true;
                        }
                    }
                }

                if (reachableOrigins.Count == targetTilesByOrigin.Count)
                    break;

                foreach (RelTile2i direction in s_rampAccessSearchDirections)
                {
                    Tile2i next = current + direction;
                    if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY)
                        continue;
                    if (visited.Contains(next))
                        continue;
                    if (!pathabilityProvider.IsPathable(next, pfParams.PathabilityQueryMask))
                        continue;

                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            // Propagate reachability through designation-adjacency with matching target heights
            // ("non-red" edges). If designation A is BFS-reachable and shares a compatible edge
            // with neighbouring farming designation B, B is also reachable — a vehicle working
            // on A can cross the matching-height edge into B once A is fulfilled.
            var spreadQueue = new Queue<TerrainDesignation>();
            foreach (TerrainDesignation d in designationsByOrigin.Values)
                if (reachableOrigins.Contains(d.OriginTileCoord))
                    spreadQueue.Enqueue(d);

            while (spreadQueue.Count > 0)
            {
                TerrainDesignation curr = spreadQueue.Dequeue();
                DesignationData cd = curr.Data;
                Tile2i o = curr.OriginTileCoord;
                TerrainDesignation nbr;

                // East (+4, 0): curr.PlusX == nbr.Origin AND curr.PlusXy == nbr.PlusY
                if (designationsByOrigin.TryGetValue(o + new RelTile2i(4, 0), out nbr)
                    && !reachableOrigins.Contains(nbr.OriginTileCoord)
                    && cd.PlusXTargetHeight == nbr.Data.OriginTargetHeight
                    && cd.PlusXyTargetHeight == nbr.Data.PlusYTargetHeight)
                { MarkFarmingAccessOriginReachable(nbr.OriginTileCoord, clusterByOrigin, reachableOrigins); spreadQueue.Enqueue(nbr); }

                // West (-4, 0): curr.Origin == nbr.PlusX AND curr.PlusY == nbr.PlusXy
                if (designationsByOrigin.TryGetValue(o + new RelTile2i(-4, 0), out nbr)
                    && !reachableOrigins.Contains(nbr.OriginTileCoord)
                    && cd.OriginTargetHeight == nbr.Data.PlusXTargetHeight
                    && cd.PlusYTargetHeight == nbr.Data.PlusXyTargetHeight)
                { MarkFarmingAccessOriginReachable(nbr.OriginTileCoord, clusterByOrigin, reachableOrigins); spreadQueue.Enqueue(nbr); }

                // PlusY (+4, 0): curr.PlusY == nbr.Origin AND curr.PlusXy == nbr.PlusX
                if (designationsByOrigin.TryGetValue(o + new RelTile2i(0, 4), out nbr)
                    && !reachableOrigins.Contains(nbr.OriginTileCoord)
                    && cd.PlusYTargetHeight == nbr.Data.OriginTargetHeight
                    && cd.PlusXyTargetHeight == nbr.Data.PlusXTargetHeight)
                { MarkFarmingAccessOriginReachable(nbr.OriginTileCoord, clusterByOrigin, reachableOrigins); spreadQueue.Enqueue(nbr); }

                // MinusY (0, -4): curr.Origin == nbr.PlusY AND curr.PlusX == nbr.PlusXy
                if (designationsByOrigin.TryGetValue(o + new RelTile2i(0, -4), out nbr)
                    && !reachableOrigins.Contains(nbr.OriginTileCoord)
                    && cd.OriginTargetHeight == nbr.Data.PlusYTargetHeight
                    && cd.PlusXTargetHeight == nbr.Data.PlusXyTargetHeight)
                { MarkFarmingAccessOriginReachable(nbr.OriginTileCoord, clusterByOrigin, reachableOrigins); spreadQueue.Enqueue(nbr); }
            }

            inaccessibleClusters.AddRange(clusters.Where(cluster => cluster.NeedsAccess && !cluster.HasAccess));
            LogDebug($"[ATD Farming Access] reachability result reachableOrigins={reachableOrigins.Count}/{targetTilesByOrigin.Count}, visited={visited.Count}, inaccessible={FormatFarmingAccessClusterList(inaccessibleClusters)}");

            return true;
        }

        private static void MarkFarmingAccessOriginReachable(
            Tile2i origin,
            Dictionary<Tile2i, FarmingAccessCluster> clusterByOrigin,
            HashSet<Tile2i> reachableOrigins)
        {
            reachableOrigins.Add(origin);
            if (clusterByOrigin.TryGetValue(origin, out FarmingAccessCluster cluster))
            {
                if (!cluster.HasAccess)
                    LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} reached through non-red edge at origin=({origin.X},{origin.Y}).");
                cluster.HasAccess = true;
            }
        }

        /// <summary>
        /// Merges spatially adjacent inaccessible clusters into super-clusters.
        /// Two clusters are adjacent if any designation origin in one is exactly 4 tiles
        /// (one designation width) from any designation origin in the other. The merged
        /// super-cluster's full footprint is used for ramp generation, which causes the
        /// ramp candidate search to exit at the true outer boundary of the group rather
        /// than pointing inward toward a neighbour that is also being prepared.
        /// </summary>
        private static List<FarmingAccessCluster> MergeAdjacentInaccessibleClusters(
            List<FarmingAccessCluster> inaccessibleClusters)
        {
            if (inaccessibleClusters.Count <= 1)
                return inaccessibleClusters;

            // Union-Find: parent[i] is the representative of cluster i.
            int[] parent = new int[inaccessibleClusters.Count];
            for (int i = 0; i < parent.Length; i++)
                parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }

            // Build a lookup: origin tile → cluster index.
            var indexByOrigin = new Dictionary<Tile2i, int>();
            for (int i = 0; i < inaccessibleClusters.Count; i++)
                foreach (Tile2i origin in inaccessibleClusters[i].Origins)
                    indexByOrigin[origin] = i;

            // Merge clusters that share a designation boundary (4 tiles apart, cardinal).
            var designationOffsets = new RelTile2i[]
            {
                new RelTile2i(4, 0), new RelTile2i(-4, 0),
                new RelTile2i(0, 4), new RelTile2i(0, -4)
            };

            for (int i = 0; i < inaccessibleClusters.Count; i++)
            {
                foreach (Tile2i origin in inaccessibleClusters[i].Origins)
                {
                    foreach (RelTile2i offset in designationOffsets)
                    {
                        if (indexByOrigin.TryGetValue(origin + offset, out int j) && j != i)
                            Union(i, j);
                    }
                }
            }

            // Build merged super-clusters, preserving insertion order of the first member.
            var merged = new Dictionary<int, FarmingAccessCluster>();
            var mergedOrder = new List<int>();
            for (int i = 0; i < inaccessibleClusters.Count; i++)
            {
                int root = Find(i);
                if (!merged.TryGetValue(root, out FarmingAccessCluster mc))
                {
                    mc = new FarmingAccessCluster { DebugId = inaccessibleClusters[root].DebugId, NeedsAccess = true };
                    merged[root] = mc;
                    mergedOrder.Add(root);
                }
                foreach (TerrainDesignation d in inaccessibleClusters[i].Designations)
                    mc.Add(d);
            }

            if (merged.Count < inaccessibleClusters.Count)
                LogDebug($"[ATD Farming Access] merged {inaccessibleClusters.Count} adjacent inaccessible clusters into {merged.Count} super-cluster(s).");

            return mergedOrder.Select(root => merged[root]).ToList();
        }

        private static string FormatFarmingAccessClusterList(IEnumerable<FarmingAccessCluster> clusters)
        {
            var parts = clusters
                .OrderBy(cluster => cluster.DebugId)
                .Select(FormatFarmingAccessClusterSummary)
                .ToList();
            return parts.Count == 0 ? "none" : string.Join("; ", parts);
        }

        private static string FormatFarmingAccessClusterSummary(FarmingAccessCluster cluster)
        {
            Tile2i anchor = cluster.Anchor;
            string origins = string.Join(
                " ",
                cluster.Origins
                    .OrderBy(origin => origin.Y)
                    .ThenBy(origin => origin.X)
                    .Take(6)
                    .Select(origin => $"({origin.X},{origin.Y})"));
            if (cluster.Origins.Count > 6)
                origins += $" ...+{cluster.Origins.Count - 6}";

            return $"#{cluster.DebugId}@({anchor.X},{anchor.Y}) count={cluster.Count} needs={cluster.NeedsAccess} has={cluster.HasAccess} [{origins}]";
        }

        private static bool IsFarmingDesignationReadyForVehicleWork(TerrainDesignation designation, bool isFilling)
        {
            // Mirror the vanilla job assignment gates exactly:
            //   Filling pass  → trucks use TryFindClosestReadyToDump  → IsReadyToDumpNonAmphibious()
            //   Prep pass     → excavators use TryFindClosestReadyToMine → IsReadyToMineNonAmphibious()
            // A LevelDesignator has both mining and dumping fulfillment functions,
            // so the isFilling flag selects which vanilla gate to match.
            return isFilling
                ? designation.IsReadyToDumpNonAmphibious()
                : designation.IsReadyToMineNonAmphibious();
        }

        private static HashSet<Tile2i> BuildFarmingAccessTargetTiles(
            Tile2i origin,
            IPathabilityProvider pathabilityProvider,
            VehiclePathFindingParams pfParams)
        {
            var targets = new HashSet<Tile2i>();
            for (int y = -1; y <= 4; y++)
            {
                for (int x = -1; x <= 4; x++)
                {
                    bool isPerimeter = x == -1 || x == 4 || y == -1 || y == 4;
                    if (!isPerimeter)
                        continue;

                    Tile2i tile = origin + new RelTile2i(x, y);
                    if (pathabilityProvider.IsPathable(tile, pfParams.PathabilityQueryMask))
                        targets.Add(tile);
                }
            }

            return targets;
        }
    }
}

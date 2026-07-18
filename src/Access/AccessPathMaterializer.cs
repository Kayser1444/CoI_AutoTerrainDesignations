using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using AutoTerrainDesignations.Access.V2;

namespace AutoTerrainDesignations.Access
{
    internal static class AccessPathMaterializer
    {
        public static AccessDesignationPlan Materialize(AccessSearchSnapshot snapshot, AccessSearchResult result)
        {
            if (!result.Success) return AccessDesignationPlan.Invalid("SearchFailed", result.StartOrigin);
            if (result.V2Route != null)
                return MaterializeV2(snapshot, result);
            if (result.Path.Count == 0) return AccessDesignationPlan.Invalid("EmptyPath", result.StartOrigin);
            if (!snapshot.TryGetFixedProfile(result.StartOrigin, out AccessHeightProfile previousProfile))
                return AccessDesignationPlan.Invalid("MissingStartProfile", result.StartOrigin);

            var designations = new List<AccessPlannedDesignation>();
            var generatedByOrigin = new Dictionary<Tile2i, AccessPlannedDesignation>();
            var cornerHeights = new Dictionary<Tile2i, int>();
            var cleanupByOrigin = new Dictionary<Tile2i, AccessPropCleanupInfo>();
            var cleanupOriginsCoveredByGeneratedV = new HashSet<Tile2i>();
            var handoffOperationsByOrigin =
                new Dictionary<Tile2i, AccessHandoffOperation>();
            Dictionary<int, AccessHandoffOperation> terminalOperationByPathIndex =
                BuildTerminalOperationByPathIndex(result);
            Tile2i previousPosition = result.StartOrigin;
            var previousNode = new AccessSearchNode(
                result.StartOrigin, previousProfile.Center2, AccessSearchMode.Existing);
            bool previousWasGround = false;
            int reusedNodes = 0;
            int groundNodes = 0;
            Tile2i handoffGround = default;
            AccessHandoffOperation handoffOperation = AccessHandoffOperation.None;

            for (int pathIndex = 0; pathIndex < result.Path.Count; pathIndex++)
            {
                AccessSearchNode node = result.Path[pathIndex];
                if (node.IsGround)
                {
                    // A mining/leveling V-to-G handoff may enter a removable
                    // non-tree prop tile.  The selected handoff operation
                    // clears it before this ground node is used, even though
                    // it is not ordinary pre-work G/cleanup terrain.
                    bool postWorkHandoffGround =
                        (node.HandoffOperation == AccessHandoffOperation.Mining
                            || node.HandoffOperation == AccessHandoffOperation.Leveling)
                        && snapshot.HasRemovableNonTreePropAtTile(node.Position);
                    if (!snapshot.IsGroundOrCleanupNode(node.Position)
                        && !postWorkHandoffGround)
                        return Invalid("PlanGroundUnavailable", result, designations, reusedNodes, groundNodes);
                    if (snapshot.TryGetRequiredCleanupInfoForTile(
                        node.Position, out AccessPropCleanupInfo cleanupInfo))
                    {
                        bool previousGeneratedSameOrigin = !previousWasGround
                            && previousNode.Mode != AccessSearchMode.Existing
                            && previousNode.Position == cleanupInfo.Origin;
                        if (previousGeneratedSameOrigin)
                        {
                            cleanupOriginsCoveredByGeneratedV.Add(cleanupInfo.Origin);
                            MergeCleanupInfo(cleanupByOrigin, cleanupInfo);
                        }
                        else if (cleanupOriginsCoveredByGeneratedV.Contains(cleanupInfo.Origin))
                        {
                            MergeCleanupInfo(cleanupByOrigin, cleanupInfo);
                        }
                        else
                        {
                            MergeCleanupInfo(cleanupByOrigin, cleanupInfo);
                        }
                    }
                    if (previousWasGround)
                    {
                        if (!IsValidGroundStep(
                                snapshot, previousPosition, node.Position))
                            return Invalid("PlanGroundDiscontinuity", result, designations, reusedNodes, groundNodes);
                    }
                    else
                    {
                        bool validHandoff;
                        if (node.HandoffSpanLength > 1)
                        {
                            validHandoff = TryBuildHandoffSpan(
                                    result, pathIndex, snapshot,
                                    node.HandoffSpanLength,
                                    out List<AccessHandoffSpanCell> span)
                                && snapshot.GetWorkableHandoffSpans(span).Any(candidate =>
                                    candidate.Tile == node.Position
                                    && candidate.Operation == node.HandoffOperation
                                    && candidate.SpanLength == node.HandoffSpanLength);
                        }
                        else
                        {
                            FindPredecessorProfile(result, pathIndex, snapshot,
                                out Tile2i predPosition,
                                out AccessHeightProfile predProfile);
                            validHandoff = AccessPathSearch.ContainsHandoff(
                                snapshot, previousPosition, previousProfile,
                                predPosition, predProfile,
                                node.Position, node.HandoffOperation);
                        }
                        if (!validHandoff)
                        {
                            return Invalid("PlanVToGHandoff", result, designations, reusedNodes, groundNodes);
                        }

                        // The search has already attached the selected operation
                        // to every V cell in a terminal span.  Keep that exact
                        // ownership for placement instead of inferring a span from
                        // the last materialized V node after exact-terrain cells
                        // have been omitted.
                        if (node.HandoffOperation != AccessHandoffOperation.None)
                        {
                            int firstSpanIndex = Math.Max(0,
                                pathIndex - Math.Max(1, node.HandoffSpanLength));
                            for (int spanIndex = firstSpanIndex;
                                spanIndex < pathIndex;
                                spanIndex++)
                            {
                                AccessSearchNode spanNode = result.Path[spanIndex];
                                if (!spanNode.IsGround
                                    && spanNode.Mode != AccessSearchMode.Existing)
                                    handoffOperationsByOrigin[spanNode.Position] =
                                        node.HandoffOperation;
                            }
                        }
                    }

                    previousWasGround = true;
                    previousPosition = node.Position;
                    previousNode = node;
                    handoffGround = node.Position;
                    handoffOperation = node.HandoffOperation;
                    groundNodes++;
                    continue;
                }

                if (!AccessPathSearch.TryGetProfile(snapshot, node, out AccessHeightProfile profile))
                    return Invalid("PlanMissingProfile", result, designations, reusedNodes, groundNodes);

                AccessHandoffOperation terminalOperation =
                    terminalOperationByPathIndex.TryGetValue(
                        pathIndex, out AccessHandoffOperation mappedOperation)
                        ? mappedOperation
                        : node.HandoffOperation;

                Tile2i stepDirection = default;
                if (previousWasGround)
                {
                    if (!AccessPathSearch.TryGetGroundToGeneratedHandoff(
                        snapshot, node.Position, profile, previousPosition,
                        out AccessHandoffOperation operation,
                        out Tile2i entryDirection)
                        || operation != node.HandoffOperation
                        || entryDirection != node.EntryDirection)
                    {
                        return Invalid("PlanGToVHandoff", result, designations, reusedNodes, groundNodes);
                    }
                }
                else
                {
                    stepDirection = new Tile2i(
                        node.Position.X - previousPosition.X,
                        node.Position.Y - previousPosition.Y);
                    if (!IsOriginStep(stepDirection)
                        || !AccessPathSearch.EdgesMatch(previousProfile, profile, stepDirection))
                        return Invalid("PlanEdgeMismatch", result, designations, reusedNodes, groundNodes);
                    if (previousNode.HandoffOperation != AccessHandoffOperation.None
                        && pathIndex >= 2
                        && result.Path[pathIndex - 2].IsGround
                        && !AccessPathSearch.IsGroundToGeneratedContinuation(
                            snapshot, previousNode.Position, previousProfile,
                            result.Path[pathIndex - 2].Position,
                            previousNode.HandoffOperation, node.Position))
                    {
                        return Invalid("PlanGToVHandoffDirection", result,
                            designations, reusedNodes, groundNodes);
                    }
                }

                if (node.Mode == AccessSearchMode.Existing)
                {
                    if (!snapshot.TryGetFixedProfile(node.Position, out _))
                        return Invalid("PlanExistingMissing", result, designations, reusedNodes, groundNodes);
                    reusedNodes++;
                }
                else
                {
                    if (!AccessPathSearch.IsGeneratedProfileFeasible(
                        snapshot, node.Position, profile, previousNode, stepDirection,
                        out string reason))
                        return Invalid("Plan" + reason, result, designations, reusedNodes, groundNodes);
                    bool hasTerrainDelta = ProfileHasTerrainDelta(
                        snapshot, node.Position, profile);
                    if (terminalOperation != AccessHandoffOperation.None)
                        handoffOperationsByOrigin[node.Position] = terminalOperation;
                    if (snapshot.TryGetPropCleanupInfo(
                            node.Position, out AccessPropCleanupInfo generatedCleanup)
                        && generatedCleanup.IsEligibleWithinGeneratedV)
                    {
                        AccessPropCleanupInfo approvedGeneratedCleanup =
                            generatedCleanup.BlockerKind == AccessPropBlockerKind.Durability
                                ? AccessPropCleanupPolicy.BuildOriginInfo(
                                    generatedCleanup.Origin,
                                    generatedCleanup.Samples,
                                    usesTerrainRemovalPolicy:
                                        generatedCleanup.UsesTerrainRemovalPolicy)
                                : generatedCleanup;
                        MergeCleanupInfo(cleanupByOrigin,
                            approvedGeneratedCleanup);
                    }

                    // V-space is needed for accurate elevation-aware
                    // feasibility, but an exact-terrain V cell has no work to
                    // materialize. Any removable prop it covers is emitted as
                    // explicit cleanup above instead of a no-op leveling proto.
                    if (!hasTerrainDelta)
                    {
                        previousWasGround = false;
                        previousPosition = node.Position;
                        previousProfile = profile;
                        previousNode = node;
                        continue;
                    }

                    var planned = new AccessPlannedDesignation(node.Position, node.Mode, profile);
                    if (generatedByOrigin.TryGetValue(node.Position, out AccessPlannedDesignation existing))
                    {
                        if (!ProfilesEqual(existing.Profile, profile))
                            return Invalid("PlanDuplicateConflict", result, designations, reusedNodes, groundNodes);
                        return Invalid("PlanDuplicateOrigin", result, designations, reusedNodes, groundNodes);
                    }
                    else
                    {
                        // Nonconsecutive side/diagonal contact is legal when every
                        // shared corner agrees. Compact flat-landed turns require it.
                        bool cornerMismatch = false;
                        profile.AddWorldCorners(node.Position, (corner, height2) =>
                        {
                            if (cornerHeights.TryGetValue(corner, out int oldHeight2) && oldHeight2 != height2)
                                cornerMismatch = true;
                            else
                                cornerHeights[corner] = height2;
                        });
                        if (cornerMismatch)
                            return Invalid("PlanCornerFight", result, designations, reusedNodes, groundNodes);

                        generatedByOrigin[node.Position] = planned;
                        designations.Add(planned);
                    }
                }

                previousWasGround = false;
                previousPosition = node.Position;
                previousProfile = profile;
                previousNode = node;
            }

            AccessSearchNode end = result.Path[result.Path.Count - 1];
            if (end.IsGround && !snapshot.IsGoalGroundNode(end.Position))
                return Invalid("PlanGoalMissing", result, designations, reusedNodes, groundNodes);
            if (!end.IsGround && end.Mode != AccessSearchMode.Existing)
                return Invalid("PlanGoalMissing", result, designations, reusedNodes, groundNodes);

            return new AccessDesignationPlan(true, string.Empty, result.StartOrigin, handoffGround,
                handoffOperation,
                designations, reusedNodes, groundNodes,
                new List<AccessPropCleanupInfo>(cleanupByOrigin.Values),
                handoffOperationsByOrigin);
        }

        private static AccessDesignationPlan MaterializeV2(
            AccessSearchSnapshot snapshot,
            AccessSearchResult result)
        {
            AccessV2RouteData route = result.V2Route!;
            if (!AccessV2Replay.TryReplay(
                    snapshot, route,
                    out _,
                    out IReadOnlyList<AccessV2OriginProfile> ordered,
                    out AccessV2HandoffCandidate? handoff,
                    out string replayReason))
                return AccessDesignationPlan.Invalid(
                    replayReason, result.StartOrigin);

            var designations = new List<AccessPlannedDesignation>();
            var cleanupByOrigin = new Dictionary<Tile2i, AccessPropCleanupInfo>();
            var terrainWorkOrigins = new HashSet<Tile2i>();
            var corners = new Dictionary<Tile2i, int>();
            for (int index = 0; index < ordered.Count; index++)
            {
                AccessV2OriginProfile item = ordered[index];
                bool cornerMismatch = false;
                item.Profile.AddWorldCorners(item.Origin, (corner, height2) =>
                {
                    if (corners.TryGetValue(corner, out int old)
                        && old != height2)
                        cornerMismatch = true;
                    else
                        corners[corner] = height2;
                });
                if (cornerMismatch)
                    return AccessDesignationPlan.Invalid(
                        "V2PlanCornerFight", result.StartOrigin);

                bool hasTerrainDelta = ProfileHasTerrainDelta(
                    snapshot, item.Origin, item.Profile);
                if (snapshot.TryGetPropCleanupInfo(
                        item.Origin, out AccessPropCleanupInfo generatedCleanup)
                    && generatedCleanup.IsEligibleWithinGeneratedV)
                {
                    AccessPropCleanupInfo approved =
                        generatedCleanup.BlockerKind == AccessPropBlockerKind.Durability
                            ? AccessPropCleanupPolicy.BuildOriginInfo(
                                generatedCleanup.Origin,
                                generatedCleanup.Samples,
                                usesTerrainRemovalPolicy:
                                    generatedCleanup.UsesTerrainRemovalPolicy)
                            : generatedCleanup;
                    MergeCleanupInfo(cleanupByOrigin, approved);
                }
                if (!hasTerrainDelta) continue;
                if (!AccessV2BandProfile.TryGetProfileMode(
                        item.Profile, out AccessSearchMode mode))
                    return AccessDesignationPlan.Invalid(
                        "V2PlanProfileMode", result.StartOrigin);
                terrainWorkOrigins.Add(item.Origin);
                designations.Add(new AccessPlannedDesignation(
                    item.Origin, mode, item.Profile));
            }

            var operations = new Dictionary<Tile2i, AccessHandoffOperation>();
            Tile2i handoffGround = default;
            AccessHandoffOperation commonOperation = AccessHandoffOperation.None;
            int groundNodes = 0;
            IReadOnlyList<AccessV2HandoffCandidate> handoffs =
                route.RouteSteps.Count > 0
                    ? route.RouteSteps
                        .Where(step => step.Handoff != null)
                        .Select(step => step.Handoff!)
                        .ToArray()
                    : handoff != null
                        ? new[] { handoff }
                        : Array.Empty<AccessV2HandoffCandidate>();
            if (handoffs.Count > 0)
            {
                AccessV2HandoffCandidate terminalHandoff =
                    handoffs[handoffs.Count - 1];
                handoffGround = terminalHandoff.Lane0Contact;
                commonOperation = terminalHandoff.Lane0Operation
                    == terminalHandoff.Lane1Operation
                    ? terminalHandoff.Lane0Operation
                    : AccessHandoffOperation.Leveling;
                var replayGroundCenters = new HashSet<Tile2i>();
                replayGroundCenters.UnionWith(route.GroundPath);
                groundNodes = replayGroundCenters.Count;
                for (int handoffIndex = 0;
                    handoffIndex < handoffs.Count;
                    handoffIndex++)
                {
                    AccessV2HandoffCandidate routeHandoff =
                        handoffs[handoffIndex];
                    replayGroundCenters.UnionWith(
                        routeHandoff.EscapeCenters);
                    AddOperations(
                        routeHandoff.Lane0TerminalOrigins,
                        routeHandoff.Lane0Operation);
                    AddOperations(
                        routeHandoff.Lane1TerminalOrigins,
                        routeHandoff.Lane1Operation);
                }
                groundNodes = replayGroundCenters.Count;
                foreach (Tile2i center in replayGroundCenters)
                {
                    if (!snapshot.TryGetRequiredCleanupInfoForTile(
                            center, out AccessPropCleanupInfo cleanup))
                        continue;
                    if (terrainWorkOrigins.Contains(cleanup.Origin))
                    {
                        MergeCleanupInfo(cleanupByOrigin, cleanup);
                    }
                    else
                    {
                        MergeCleanupInfo(cleanupByOrigin, cleanup);
                    }
                }
            }

            var fixedOrigins = new HashSet<Tile2i>();
            foreach (AccessV2BandState state in route.States)
                for (int lane = 0; lane < 2; lane++)
                {
                    Tile2i origin = state.GetLaneOrigin(lane);
                    if (!route.GeneratedProfiles.ContainsKey(origin))
                        fixedOrigins.Add(origin);
                }
            return new AccessDesignationPlan(
                true, string.Empty, result.StartOrigin,
                handoffGround, commonOperation,
                designations, fixedOrigins.Count, groundNodes,
                new List<AccessPropCleanupInfo>(cleanupByOrigin.Values),
                operations);

            void AddOperations(
                IReadOnlyList<Tile2i> origins,
                AccessHandoffOperation operation)
            {
                for (int index = 0; index < origins.Count; index++)
                    if (route.GeneratedProfiles.ContainsKey(origins[index]))
                        operations[origins[index]] = operation;
            }
        }

        private static bool ProfileHasTerrainDelta(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile)
        {
            for (int y = 0; y <= 4; y++)
                for (int x = 0; x <= 4; x++)
                {
                    Tile2i tile = origin + new RelTile2i(x, y);
                    if (!snapshot.TryGetGroundHeight2(tile, out int terrainHeight2))
                        return true;
                    if (profile.GetHeight2NumeratorAt(x, y)
                        != terrainHeight2 * 16)
                        return true;
                }
            return false;
        }

        private static void MergeCleanupInfo(
            Dictionary<Tile2i, AccessPropCleanupInfo> cleanupByOrigin,
            AccessPropCleanupInfo cleanupInfo)
        {
            if (!cleanupByOrigin.TryGetValue(cleanupInfo.Origin, out AccessPropCleanupInfo existing))
            {
                cleanupByOrigin.Add(cleanupInfo.Origin, cleanupInfo);
                return;
            }

            var samples = new List<AccessPropSample>(existing.Samples.Count + cleanupInfo.Samples.Count);
            samples.AddRange(existing.Samples);
            samples.AddRange(cleanupInfo.Samples);
            cleanupByOrigin[cleanupInfo.Origin] = AccessPropCleanupPolicy.BuildOriginInfo(
                cleanupInfo.Origin,
                samples,
                usesTerrainRemovalPolicy:
                    existing.UsesTerrainRemovalPolicy
                    || cleanupInfo.UsesTerrainRemovalPolicy);
        }

        private static bool TryBuildTreeOnlyCleanupInfo(
            AccessPropCleanupInfo cleanupInfo,
            out AccessPropCleanupInfo treeCleanupInfo)
        {
            if (!cleanupInfo.HasTreeCleanup)
            {
                treeCleanupInfo = AccessPropCleanupInfo.Clear(cleanupInfo.Origin);
                return false;
            }

            var treeSamples = new List<AccessPropSample>();
            foreach (AccessPropSample sample in cleanupInfo.Samples)
                if (sample.IsTree)
                    treeSamples.Add(sample);

            if (treeSamples.Count == 0)
                treeCleanupInfo = new AccessPropCleanupInfo(
                    cleanupInfo.Origin,
                    AccessPropCleanupClass.Tree,
                    AccessPropBlockerKind.None,
                    cleanupInfo.UsesTerrainRemovalPolicy);
            else
                treeCleanupInfo = AccessPropCleanupPolicy.BuildOriginInfo(
                    cleanupInfo.Origin,
                    treeSamples,
                    usesTerrainRemovalPolicy:
                        cleanupInfo.UsesTerrainRemovalPolicy);
            return treeCleanupInfo.IsEligible;
        }

        private static bool TryBuildDumpingCleanupInfo(
            AccessPropCleanupInfo cleanupInfo,
            Tile2i origin,
            AccessHeightProfile profile,
            out AccessPropCleanupInfo dumpingCleanupInfo)
        {
            if (cleanupInfo.Samples.Count == 0)
            {
                dumpingCleanupInfo = cleanupInfo;
                return cleanupInfo.IsEligible;
            }

            AccessPropSample[] retained = cleanupInfo.Samples
                .Where(sample => sample.IsTree
                    || sample.IsDenseDebris
                        && !AccessSearchSnapshot.DoesDumpingBuryProp(
                            origin, profile, sample))
                .ToArray();
            if (retained.Length == 0)
            {
                dumpingCleanupInfo = AccessPropCleanupInfo.Clear(
                    cleanupInfo.Origin);
                return false;
            }
            dumpingCleanupInfo = AccessPropCleanupPolicy.BuildOriginInfo(
                cleanupInfo.Origin, retained,
                usesTerrainRemovalPolicy:
                    cleanupInfo.UsesTerrainRemovalPolicy);
            return dumpingCleanupInfo.IsEligible;
        }

        private static AccessDesignationPlan Invalid(string reason, AccessSearchResult result,
            IReadOnlyList<AccessPlannedDesignation> designations, int reusedNodes, int groundNodes)
            => new AccessDesignationPlan(false, reason, result.StartOrigin, default,
                AccessHandoffOperation.None,
                designations, reusedNodes, groundNodes);

        private static bool IsOriginStep(Tile2i direction)
            => (Math.Abs(direction.X) == 4 && direction.Y == 0)
                || (Math.Abs(direction.Y) == 4 && direction.X == 0);

        private static bool ProfilesEqual(AccessHeightProfile left, AccessHeightProfile right)
            => left.Nw2 == right.Nw2 && left.Ne2 == right.Ne2
                && left.Se2 == right.Se2 && left.Sw2 == right.Sw2;

        private static int Manhattan(Tile2i left, Tile2i right)
            => Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);

        private static bool IsValidGroundStep(
            AccessSearchSnapshot snapshot,
            Tile2i from,
            Tile2i to)
        {
            int dx = Math.Abs(to.X - from.X);
            int dy = Math.Abs(to.Y - from.Y);
            if (dx + dy == 1) return true;
            if (dx != 1 || dy != 1) return false;

            // V1 search admits a diagonal only after both cardinal side
            // corridors have passed the same ordinary-ground test. Replay the
            // static part here so a valid diagonal route is not rejected as a
            // materialization discontinuity after it reaches a tower goal.
            return snapshot.IsGroundNode(new Tile2i(to.X, from.Y))
                && snapshot.IsGroundNode(new Tile2i(from.X, to.Y));
        }

        private static void FindPredecessorProfile(
            AccessSearchResult result,
            int currentIndex,
            AccessSearchSnapshot snapshot,
            out Tile2i predPosition,
            out AccessHeightProfile predProfile)
        {
            predPosition = result.StartOrigin;
            snapshot.TryGetFixedProfile(result.StartOrigin, out predProfile);

            int handoffIndex = currentIndex - 1;
            int predecessorIndex = handoffIndex - 1;
            if (predecessorIndex < 0)
                return;

            AccessSearchNode predecessor = result.Path[predecessorIndex];
            if (!predecessor.IsGround
                && AccessPathSearch.TryGetProfile(snapshot, predecessor, out AccessHeightProfile profile))
            {
                predPosition = predecessor.Position;
                predProfile = profile;
            }
        }

        private static bool TryBuildHandoffSpan(
            AccessSearchResult result,
            int groundPathIndex,
            AccessSearchSnapshot snapshot,
            int spanLength,
            out List<AccessHandoffSpanCell> span)
        {
            span = new List<AccessHandoffSpanCell>(spanLength);
            int firstIndex = groundPathIndex - spanLength;
            if (firstIndex < 0)
                return false;
            Tile2i direction = default;
            for (int index = firstIndex; index < groundPathIndex; index++)
            {
                AccessSearchNode node = result.Path[index];
                if (node.IsGround || node.Mode == AccessSearchMode.Existing
                    || !AccessPathSearch.TryGetProfile(
                        snapshot, node, out AccessHeightProfile profile))
                    return false;
                if (index == firstIndex)
                    direction = node.EntryDirection;
                else if (node.EntryDirection != direction
                    || node.Position != new Tile2i(
                        result.Path[index - 1].Position.X + direction.X,
                        result.Path[index - 1].Position.Y + direction.Y))
                    return false;
                span.Add(new AccessHandoffSpanCell(
                    node.Position, profile, node.EntryDirection));
            }
            return true;
        }

        private static Dictionary<int, AccessHandoffOperation>
            BuildTerminalOperationByPathIndex(AccessSearchResult result)
        {
            var operations = new Dictionary<int, AccessHandoffOperation>();
            for (int index = 0; index < result.Path.Count; index++)
            {
                AccessSearchNode node = result.Path[index];
                if (!node.IsGround && node.Mode != AccessSearchMode.Existing
                    && node.HandoffOperation != AccessHandoffOperation.None)
                    operations[index] = node.HandoffOperation;
                if (!node.IsGround || node.HandoffOperation == AccessHandoffOperation.None)
                    continue;
                int spanLength = Math.Max(1, node.HandoffSpanLength);
                for (int spanIndex = Math.Max(0, index - spanLength);
                    spanIndex < index;
                    spanIndex++)
                    if (!result.Path[spanIndex].IsGround
                        && result.Path[spanIndex].Mode != AccessSearchMode.Existing)
                        operations[spanIndex] = node.HandoffOperation;
            }
            return operations;
        }
    }
}

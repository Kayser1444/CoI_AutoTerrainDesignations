using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    internal static class AccessPathMaterializer
    {
        public static AccessDesignationPlan Materialize(AccessSearchSnapshot snapshot, AccessSearchResult result)
        {
            if (!result.Success) return AccessDesignationPlan.Invalid("SearchFailed", result.StartOrigin);
            if (result.Path.Count == 0) return AccessDesignationPlan.Invalid("EmptyPath", result.StartOrigin);
            if (!snapshot.TryGetFixedProfile(result.StartOrigin, out AccessHeightProfile previousProfile))
                return AccessDesignationPlan.Invalid("MissingStartProfile", result.StartOrigin);

            var designations = new List<AccessPlannedDesignation>();
            var generatedByOrigin = new Dictionary<Tile2i, AccessPlannedDesignation>();
            var cornerHeights = new Dictionary<Tile2i, int>();
            var cleanupByOrigin = new Dictionary<Tile2i, AccessPropCleanupInfo>();
            var cleanupOriginsCoveredByGeneratedV = new HashSet<Tile2i>();
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
                    if (!snapshot.IsGroundOrCleanupNode(node.Position))
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
                            if (TryBuildTreeOnlyCleanupInfo(cleanupInfo, out AccessPropCleanupInfo treeCleanup))
                                MergeCleanupInfo(cleanupByOrigin, treeCleanup);
                        }
                        else if (cleanupOriginsCoveredByGeneratedV.Contains(cleanupInfo.Origin))
                        {
                            if (TryBuildTreeOnlyCleanupInfo(cleanupInfo, out AccessPropCleanupInfo treeCleanup))
                                MergeCleanupInfo(cleanupByOrigin, treeCleanup);
                        }
                        else
                        {
                            MergeCleanupInfo(cleanupByOrigin, cleanupInfo);
                        }
                    }
                    if (previousWasGround)
                    {
                        if (Manhattan(previousPosition, node.Position) != 1)
                            return Invalid("PlanGroundDiscontinuity", result, designations, reusedNodes, groundNodes);
                    }
                    else
                    {
                        FindPredecessorProfile(result, pathIndex, snapshot, out Tile2i predPosition, out AccessHeightProfile predProfile);
                        if (!AccessPathSearch.ContainsHandoff(
                            snapshot, previousPosition, previousProfile,
                            predPosition, predProfile,
                            node.Position, node.HandoffOperation))
                        {
                            return Invalid("PlanVToGHandoff", result, designations, reusedNodes, groundNodes);
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
                    if (snapshot.TryGetPropCleanupInfo(node.Position, out AccessPropCleanupInfo generatedCleanup)
                        && TryBuildTreeOnlyCleanupInfo(generatedCleanup, out AccessPropCleanupInfo treeCleanup))
                        MergeCleanupInfo(cleanupByOrigin, treeCleanup);

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
                new List<AccessPropCleanupInfo>(cleanupByOrigin.Values));
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
    }
}

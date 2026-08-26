using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    /// <summary>
    /// Bounded terminal-form evaluator. It deliberately owns the whole
    /// terminal attempt: operation derivation, at most four rank states, the
    /// fixed forward frontage, and the dense ground proof. The old recursive
    /// staggered fallback is not called from this module.
    /// </summary>
    internal static class AccessV2TerminalEvaluator
    {
        internal const int MaxRanks = 4;
        internal const int MaxShapes = 40;

        public static AccessV2TerminalResult Evaluate(
            in AccessV2TerminalRequest request)
        {
            if (request.Straight.Kind != AccessV2TransitionKind.Straight)
                return AccessV2TerminalResult.NotApplicable(
                    "TerminalRequiresStraight");
            if (IsCancelled(request))
                return AccessV2TerminalResult.Cancelled(
                    "SearchCancelled");
            if (!TryDeriveOperation(
                    request, out AccessHandoffOperation operation,
                    out AccessV2TerminalCrestState lane0Crest,
                    out AccessV2TerminalCrestState lane1Crest,
                    out string operationReason))
                return AccessV2TerminalResult.NotApplicable(operationReason);
            int evaluatedBranches = 0;
            int evaluatedFrontages = 0;
            int maxRankEvaluated = 0;

            AccessV2Transition initial = WithOperation(
                request.Straight, operation);
            if (!TryApply(
                    request, request.Predecessor, request.History,
                    request.PredecessorCost, initial, operation,
                    out TerminalBranch branch, out string initialReason))
            {
                if (initialReason == "SearchCancelled")
                    return AccessV2TerminalResult.Cancelled(
                        "SearchCancelled");
                return new AccessV2TerminalResult(
                    AccessV2TerminalStatus.NoHandoff,
                    Array.Empty<AccessV2TerminalCandidate>(),
                    initialReason,
                    evaluatedBranches: 0,
                    evaluatedFrontages: 0,
                    maxRank: 1);
            }
            branch.SetCrests(lane0Crest, lane1Crest);

            var frontier = new List<TerminalBranch>(1) { branch };
            for (int rank = 1; rank <= MaxRanks; rank++)
            {
                evaluatedBranches += frontier.Count;
                maxRankEvaluated = rank;
                var successes = new List<AccessV2TerminalCandidate>();
                for (int index = 0; index < frontier.Count; index++)
                {
                    if (IsCancelled(request))
                        return new AccessV2TerminalResult(
                            AccessV2TerminalStatus.Cancelled,
                            Array.Empty<AccessV2TerminalCandidate>(),
                            "SearchCancelled",
                            evaluatedBranches,
                            evaluatedFrontages,
                            maxRankEvaluated);
                    if (EvaluateForwardFrontage(
                            request, frontier[index], operation, successes,
                            out bool frontageEvaluated))
                        return new AccessV2TerminalResult(
                            AccessV2TerminalStatus.Cancelled,
                            Array.Empty<AccessV2TerminalCandidate>(),
                            "SearchCancelled",
                            evaluatedBranches,
                            evaluatedFrontages,
                            maxRankEvaluated);
                    if (frontageEvaluated)
                        evaluatedFrontages++;
                }
                if (successes.Count > 0)
                    return new AccessV2TerminalResult(
                        AccessV2TerminalStatus.Success,
                        SelectNondominated(successes),
                        evaluatedBranches: evaluatedBranches,
                        evaluatedFrontages: evaluatedFrontages,
                        maxRank: maxRankEvaluated);
                if (rank == MaxRanks)
                    break;

                if (frontier.All(item => item.FrozenLanes == 3))
                    break;

                var next = new List<TerminalBranch>(
                    Math.Min(MaxShapes, frontier.Count * 3));
                for (int index = 0; index < frontier.Count; index++)
                {
                    if (IsCancelled(request))
                        return new AccessV2TerminalResult(
                            AccessV2TerminalStatus.Cancelled,
                            Array.Empty<AccessV2TerminalCandidate>(),
                            "SearchCancelled",
                            evaluatedBranches,
                            evaluatedFrontages,
                            maxRankEvaluated);
                    IReadOnlyList<AccessV2Transition> transitions =
                        AccessV2Geometry.EnumerateStraight(
                            frontier[index].State);
                    for (int transitionIndex = 0;
                        transitionIndex < transitions.Count;
                        transitionIndex++)
                    {
                        AccessV2Transition transition = transitions[
                            transitionIndex];
                        if (next.Count >= MaxShapes)
                            break;
                        byte activeLanes = (byte)(~frontier[index].FrozenLanes & 3);
                        var delta = new List<AccessV2OriginProfile>(2);
                        if ((activeLanes & 1) != 0)
                            delta.Add(transition.Next.GetLane(0));
                        if ((activeLanes & 2) != 0)
                            delta.Add(transition.Next.GetLane(1));
                        var extensionTransition = new AccessV2Transition(
                            transition.Kind,
                            transition.Next,
                            delta,
                            transition.LocalContextOrigins,
                            transition.OldDirectionTurnRays,
                            operation,
                            transition.ScoreOnlyGeneratedExteriorRays);
                        if (!TryApply(
                                request,
                                frontier[index].State,
                                frontier[index].History,
                                frontier[index].Cost,
                                extensionTransition,
                                operation,
                                out TerminalBranch child,
                                out string childReason))
                        {
                            if (childReason == "SearchCancelled")
                                return new AccessV2TerminalResult(
                                    AccessV2TerminalStatus.Cancelled,
                                    Array.Empty<AccessV2TerminalCandidate>(),
                                    "SearchCancelled",
                                    evaluatedBranches,
                                    evaluatedFrontages,
                                    maxRankEvaluated);
                            continue;
                        }
                        if (!TryAdvanceCrests(
                                request, frontier[index], child, operation))
                            continue;
                        child.Append(frontier[index]);
                        next.Add(child);
                    }
                }
                frontier = next;
                if (frontier.Count == 0)
                    break;
            }
            return new AccessV2TerminalResult(
                AccessV2TerminalStatus.NoHandoff,
                Array.Empty<AccessV2TerminalCandidate>(),
                "NoTerminalHandoffWithinFourRanks",
                evaluatedBranches,
                evaluatedFrontages,
                maxRankEvaluated);
        }

        private static bool TryDeriveOperation(
            in AccessV2TerminalRequest request,
            out AccessHandoffOperation operation,
            out AccessV2TerminalCrestState lane0Crest,
            out AccessV2TerminalCrestState lane1Crest,
            out string reason)
        {
            AccessV2TerminalCrestEvidence evidence = request.CrestEvaluator(
                request.Straight.Next, 3,
                AccessHandoffOperation.None);
            operation = evidence.Operation;
            lane0Crest = evidence.Lane0;
            lane1Crest = evidence.Lane1;
            reason = evidence.Reason;
            return evidence.IsApplicable;
        }

        private static bool TryAdvanceCrests(
            in AccessV2TerminalRequest request,
            TerminalBranch parent,
            TerminalBranch child,
            AccessHandoffOperation operation)
        {
            int childRank = parent.States.Count + 1;
            byte activeLanes = (byte)(~parent.FrozenLanes & 3);
            AccessV2TerminalCrestEvidence evidence = request.CrestEvaluator(
                child.State, activeLanes, operation);
            if (!evidence.IsApplicable || evidence.Operation != operation)
                return false;
            for (int lane = 0; lane < 2; lane++)
            {
                if (parent.IsFrozen(lane))
                {
                    child.SetCrest(
                        lane, AccessV2TerminalCrestState.Full,
                        parent.GetFreezeRank(lane));
                    continue;
                }
                AccessV2TerminalCrestState crest = lane == 0
                    ? evidence.Lane0 : evidence.Lane1;
                if (parent.GetCrest(lane)
                        == AccessV2TerminalCrestState.Partial
                    && crest == AccessV2TerminalCrestState.Uncrested)
                    return false;
                child.SetCrest(
                    lane, crest,
                    crest == AccessV2TerminalCrestState.Full
                        ? childRank : 0);
            }
            return true;
        }

        private static IReadOnlyList<AccessGroundHandoff>
            GetFrontageHandoffs(
                TerminalBranch branch,
                int lane,
                AccessHandoffOperation operation)
        {
            if (branch.GetCrest(lane)
                != AccessV2TerminalCrestState.Full)
                return Array.Empty<AccessGroundHandoff>();
            int stateIndex = branch.IsFrozen(lane)
                ? branch.GetFreezeRank(lane) - 1
                : branch.States.Count - 1;
            AccessV2BandState state = branch.States[stateIndex];
            Tile2i origin = state.GetLaneOrigin(lane);
            var contacts = new AccessGroundHandoff[5];
            for (int offset = 0; offset < contacts.Length; offset++)
            {
                Tile2i tile = state.EntryDirection.X != 0
                    ? origin + new RelTile2i(
                        state.EntryDirection.X > 0 ? 4 : 0, offset)
                    : origin + new RelTile2i(
                        offset, state.EntryDirection.Y > 0 ? 4 : 0);
                contacts[offset] = new AccessGroundHandoff(tile, operation);
            }
            return contacts;
        }

        private static IReadOnlyList<AccessGroundHandoff>
            BuildCompanionHandoffs(
                TerminalBranch branch,
                int lane,
                IReadOnlyList<AccessGroundHandoff> source,
                AccessHandoffOperation operation)
        {
            int stateIndex = branch.IsFrozen(lane)
                ? branch.GetFreezeRank(lane) - 1
                : branch.States.Count - 1;
            AccessV2BandState state = branch.States[stateIndex];
            Tile2i origin = state.GetLaneOrigin(lane);
            var result = new AccessGroundHandoff[source.Count];
            for (int index = 0; index < source.Count; index++)
                result[index] = new AccessGroundHandoff(
                    GetCompanionContact(
                        origin, state.EntryDirection, source[index].Tile),
                    operation,
                    source[index].EscapeTiles,
                    source[index].SpanLength);
            return result;
        }

        private static IReadOnlyList<Tile2i> BuildLaneOrigins(
            TerminalBranch branch,
            int lane)
        {
            int count = branch.IsFrozen(lane)
                ? branch.GetFreezeRank(lane)
                : branch.States.Count;
            var result = new Tile2i[count];
            for (int index = 0; index < count; index++)
                result[index] = branch.States[index].GetLaneOrigin(lane);
            return result;
        }

        /// <returns>
        /// True when cancellation was observed. Candidates accumulated before
        /// cancellation are deliberately discarded by the caller.
        /// </returns>
        private static bool EvaluateForwardFrontage(
            in AccessV2TerminalRequest request,
            TerminalBranch branch,
            AccessHandoffOperation operation,
            ICollection<AccessV2TerminalCandidate> results,
            out bool evaluated)
        {
            evaluated = false;
            AccessV2BandState first = branch.States[0];
            if (branch.GetCrest(0) != AccessV2TerminalCrestState.Full
                && branch.GetCrest(1) != AccessV2TerminalCrestState.Full)
                return false;
            evaluated = true;
            IReadOnlyList<AccessGroundHandoff> lane0Handoffs =
                GetFrontageHandoffs(branch, 0, operation);
            IReadOnlyList<AccessGroundHandoff> lane1Handoffs =
                GetFrontageHandoffs(branch, 1, operation);
            if (lane0Handoffs.Count == 0)
                lane0Handoffs = BuildCompanionHandoffs(
                    branch, 0, lane1Handoffs, operation);
            if (lane1Handoffs.Count == 0)
                lane1Handoffs = BuildCompanionHandoffs(
                    branch, 1, lane0Handoffs, operation);

            IReadOnlyList<Tile2i> lane0Origins =
                BuildLaneOrigins(branch, 0);
            IReadOnlyList<Tile2i> lane1Origins =
                BuildLaneOrigins(branch, 1);
            if (IsCancelled(request))
                return true;
            bool escapeBuilt = TryBuildDenseEscape(
                request, first, branch.States.Count,
                lane0Origins, lane1Origins, operation,
                branch.History,
                out IReadOnlyList<Tile2i> escape,
                out Tile2i entry,
                out IReadOnlyCollection<string> cleanupKeys,
                out float cleanupCost);
            if (IsCancelled(request))
                return true;
            if (!escapeBuilt
                || !TrySelectCompatibilityPair(
                    lane0Handoffs, lane1Handoffs,
                    out AccessGroundHandoff lane0,
                    out AccessGroundHandoff lane1))
                return false;
            var frontage = new AccessV2TerminalFrontage(
                isForward: true,
                isInnerNotch: false,
                first.Axis,
                first.EntryDirection,
                0,
                1,
                0,
                0);
            AccessV2TerminalRankDelta[] rankDeltas =
                BuildRankDeltas(branch);
            int extensionLane = branch.FrozenLanes == 1
                ? 1
                : branch.FrozenLanes == 2 ? 0 : -1;
            var compatibility = new AccessV2HandoffCandidate(
                first.EntryDirection,
                branch.States.Count,
                lane0, lane1,
                lane0Origins, lane1Origins,
                escape,
                new[] { entry },
                cleanupKeys,
                cleanupCost,
                centerSpokeCost: request.CenterSpokeCost,
                isStaggeredExtension: true,
                nonCrestLane: extensionLane,
                isBoundedTerminal: true,
                terminalRanks: rankDeltas,
                terminalFrontage: frontage);
            results.Add(new AccessV2TerminalCandidate(
                operation,
                branch.States.Count,
                rankDeltas,
                frontage,
                entry,
                Math.Max(0, escape.Count - 1),
                branch.TraversalCost,
                branch.GeneratedWorkCost,
                branch.GeneratedFixedCost,
                branch.DirectWorkCost,
                branch.ExteriorRayCost,
                branch.CleanupCost + cleanupCost,
                cleanupKeys,
                compatibility,
                branch.Transitions.ToArray(),
                branch.Evaluations.ToArray()));
            return false;
        }

        private static bool TrySelectCompatibilityPair(
            IReadOnlyList<AccessGroundHandoff> first,
            IReadOnlyList<AccessGroundHandoff> second,
            out AccessGroundHandoff selectedFirst,
            out AccessGroundHandoff selectedSecond)
        {
            selectedFirst = default;
            selectedSecond = default;
            bool found = false;
            for (int firstIndex = 0; firstIndex < first.Count; firstIndex++)
                for (int secondIndex = 0;
                    secondIndex < second.Count;
                    secondIndex++)
                {
                    AccessGroundHandoff candidateFirst = first[firstIndex];
                    AccessGroundHandoff candidateSecond = second[secondIndex];
                    if (!found
                        || CompareContacts(
                            candidateFirst, candidateSecond,
                            selectedFirst, selectedSecond) < 0)
                    {
                        selectedFirst = candidateFirst;
                        selectedSecond = candidateSecond;
                        found = true;
                    }
                }
            return found;
        }

        private static int CompareContacts(
            AccessGroundHandoff leftFirst,
            AccessGroundHandoff leftSecond,
            AccessGroundHandoff rightFirst,
            AccessGroundHandoff rightSecond)
        {
            int comparison = leftFirst.Tile.X.CompareTo(rightFirst.Tile.X);
            if (comparison != 0) return comparison;
            comparison = leftFirst.Tile.Y.CompareTo(rightFirst.Tile.Y);
            if (comparison != 0) return comparison;
            comparison = leftSecond.Tile.X.CompareTo(rightSecond.Tile.X);
            return comparison != 0
                ? comparison
                : leftSecond.Tile.Y.CompareTo(rightSecond.Tile.Y);
        }

        private static bool TryBuildDenseEscape(
            in AccessV2TerminalRequest request,
            AccessV2BandState first,
            int terminalRanks,
            IReadOnlyList<Tile2i> lane0Origins,
            IReadOnlyList<Tile2i> lane1Origins,
            AccessHandoffOperation operation,
            AccessV2History history,
            out IReadOnlyList<Tile2i> path,
            out Tile2i entry,
            out IReadOnlyCollection<string> cleanupKeys,
            out float cleanupCost)
        {
            path = Array.Empty<Tile2i>();
            entry = default;
            cleanupKeys = Array.Empty<string>();
            cleanupCost = 0f;
            int ranks = Math.Max(1, Math.Min(MaxRanks, terminalRanks));
            ulong pathable = 0UL;
            ulong goals = 0UL;
            Tile2i[] centers = new Tile2i[AccessV2TerminalMask.MaxCells];
            IReadOnlyCollection<Tile2i> clearingOrigins = lane0Origins
                .Concat(lane1Origins).Distinct().ToArray();
            int rows = AccessV2TerminalMask.RankRowCount(ranks);
            for (int row = 0; row < rows; row++)
                for (int file = 0; file < AccessV2TerminalMask.Files; file++)
                {
                    int bit = row * AccessV2TerminalMask.Files + file;
                    Tile2i center = GetCenter(first, row, file);
                    centers[bit] = center;
                    Tile2i owner = FindOwner(
                        center, lane0Origins, lane1Origins);
                    bool ground = request.Ground.IsTraversable(center, history);
                    bool postWorkPathable = request.PostWorkCenterValidator(
                        owner, operation, center, history,
                        clearingOrigins);
                    if (!postWorkPathable)
                        continue;
                    pathable |= 1UL << bit;
                    if (ground
                        && (request.ProjectedCenterValidator == null
                            || request.ProjectedCenterValidator(
                                center, history))
                        && (request.GroundEntryValidator == null
                            || request.GroundEntryValidator(
                                center, clearingOrigins, history)))
                        goals |= 1UL << bit;
                }
            AccessV2TerminalProof proof =
                AccessV2TerminalProofHelper.FindMinimumCardinalProof(
                    new AccessV2TerminalMask(pathable, ranks),
                    new AccessV2TerminalMask(
                        pathable & AccessV2TerminalMask.Row(rows - 1, ranks).Bits,
                        ranks),
                    new AccessV2TerminalMask(goals, ranks));
            if (!proof.Success)
                return false;
            List<Tile2i>? bestPath = null;
            IReadOnlyCollection<string>? bestKeys = null;
            float bestCleanupCost = float.PositiveInfinity;
            for (int goalBit = 0;
                goalBit < AccessV2TerminalMask.MaxCells;
                goalBit++)
            {
                if ((proof.GoalBits & (1UL << goalBit)) == 0UL
                    || !TryExtractPath(
                        pathable,
                        AccessV2TerminalMask.Row(rows - 1, ranks).Bits,
                        goalBit,
                        centers,
                        out List<Tile2i> extracted))
                    continue;
                Tile2i candidateEntry = extracted[extracted.Count - 1];
                if (!request.Ground.TryValidateLocalEscape(
                        new[] { candidateEntry }, history,
                        request.CleanupCostScale,
                        out IReadOnlyCollection<string> candidateKeys,
                        out float candidateCleanupCost))
                    continue;
                if (candidateCleanupCost < bestCleanupCost
                    || (Math.Abs(candidateCleanupCost - bestCleanupCost)
                        <= 0.0001f
                        && (bestPath == null
                            || candidateEntry.X < bestPath[bestPath.Count - 1].X
                            || (candidateEntry.X
                                    == bestPath[bestPath.Count - 1].X
                                && candidateEntry.Y
                                    < bestPath[bestPath.Count - 1].Y))))
                {
                    bestPath = extracted;
                    bestKeys = candidateKeys;
                    bestCleanupCost = candidateCleanupCost;
                    entry = candidateEntry;
                }
            }
            if (bestPath == null || bestKeys == null)
                return false;
            path = bestPath;
            cleanupKeys = bestKeys;
            cleanupCost = bestCleanupCost;
            return true;
        }

        private static bool TryExtractPath(
            ulong pathable,
            ulong starts,
            int goalBit,
            IReadOnlyList<Tile2i> centers,
            out List<Tile2i> path)
        {
            path = new List<Tile2i>();
            int[] queue = new int[AccessV2TerminalMask.MaxCells];
            int[] parent = new int[AccessV2TerminalMask.MaxCells];
            for (int index = 0; index < parent.Length; index++)
                parent[index] = -2;
            int head = 0;
            int tail = 0;
            ulong startBits = starts & pathable;
            for (int bit = 0; bit < AccessV2TerminalMask.MaxCells; bit++)
                if ((startBits & (1UL << bit)) != 0UL)
                {
                    queue[tail++] = bit;
                    parent[bit] = -1;
                }
            while (head < tail && parent[goalBit] == -2)
            {
                int bit = queue[head++];
                ulong neighbors = AccessV2TerminalProofHelper.ExpandCardinal(
                    1UL << bit,
                    AccessV2TerminalMask.MaxRanks) & pathable;
                for (int next = 0;
                    next < AccessV2TerminalMask.MaxCells;
                    next++)
                    if ((neighbors & (1UL << next)) != 0UL
                        && parent[next] == -2)
                    {
                        parent[next] = bit;
                        queue[tail++] = next;
                    }
            }
            if (parent[goalBit] == -2)
                return false;
            for (int bit = goalBit; bit >= 0; bit = parent[bit])
            {
                path.Add(centers[bit]);
                if (parent[bit] < 0)
                    break;
            }
            path.Reverse();
            return path.Count > 0;
        }

        private static AccessV2TerminalRankDelta[] BuildRankDeltas(
            TerminalBranch branch)
        {
            IReadOnlyList<AccessV2BandState> states = branch.States;
            var result = new AccessV2TerminalRankDelta[states.Count];
            int lane0FreezeRank = branch.GetFreezeRank(0);
            int lane1FreezeRank = branch.GetFreezeRank(1);
            for (int index = 0; index < states.Count; index++)
            {
                int rank = index + 1;
                AccessV2BandState state = states[index];
                AccessV2BandProfile.TryGetProfileMode(
                    state.Band.Lane0, out AccessSearchMode mode);
                byte frozenLanes = 0;
                if (lane0FreezeRank > 0 && lane0FreezeRank <= rank)
                    frozenLanes |= 1;
                if (lane1FreezeRank > 0 && lane1FreezeRank <= rank)
                    frozenLanes |= 2;
                result[index] = new AccessV2TerminalRankDelta(
                    rank,
                    mode,
                    lane0FreezeRank > 0 && lane0FreezeRank < rank
                        ? states[lane0FreezeRank - 1].GetLane(0)
                        : state.GetLane(0),
                    lane1FreezeRank > 0 && lane1FreezeRank < rank
                        ? states[lane1FreezeRank - 1].GetLane(1)
                        : state.GetLane(1),
                    frozenLanes,
                    newlyExposedFrontages: 1);
            }
            return result;
        }

        internal static IReadOnlyList<AccessV2TerminalCandidate>
            SelectNondominated(
                IReadOnlyList<AccessV2TerminalCandidate> candidates)
        {
            var selected = new List<AccessV2TerminalCandidate>();
            for (int index = 0; index < candidates.Count; index++)
            {
                AccessV2TerminalCandidate candidate = candidates[index];
                bool dominated = false;
                for (int otherIndex = 0;
                    otherIndex < candidates.Count;
                    otherIndex++)
                {
                    if (index == otherIndex)
                        continue;
                    AccessV2TerminalCandidate other = candidates[otherIndex];
                    if (other.GroundEntry != candidate.GroundEntry
                        || other.TotalCost > candidate.TotalCost + 0.0001f
                        || !other.CleanupKeys.All(
                            key => candidate.CleanupKeys.Contains(key)))
                        continue;
                    if (other.TotalCost < candidate.TotalCost - 0.0001f
                        || other.CleanupKeys.Count < candidate.CleanupKeys.Count)
                    {
                        dominated = true;
                        break;
                    }
                }
                if (!dominated)
                    selected.Add(candidate);
            }
            return selected;
        }

        private static bool TryApply(
            in AccessV2TerminalRequest request,
            AccessV2BandState predecessor,
            AccessV2History history,
            float predecessorCost,
            AccessV2Transition transition,
            AccessHandoffOperation operation,
            out TerminalBranch branch,
            out string reason)
        {
            branch = null!;
            reason = string.Empty;
            if (!AccessV2Geometry.IsInsideBounds(
                    transition, request.BoundsMin, request.BoundsMax))
            {
                reason = "HorizontalBounds";
                return false;
            }
            if (request.TransitionValidator != null
                && !request.TransitionValidator(transition))
            {
                reason = "UsefulHeightEnvelope";
                return false;
            }
            if (!history.TryValidateApply(
                    transition, out string historyReason))
            {
                reason = historyReason;
                return false;
            }
            if (IsCancelled(request))
            {
                reason = "SearchCancelled";
                return false;
            }
            AccessV2TransitionEvaluation evaluation =
                request.TransitionEvaluator(
                    predecessor,
                    transition,
                    history,
                    request.ConnectedFixedOrigin,
                    operation);
            if (!evaluation.IsValid)
            {
                reason = evaluation.RejectionReason;
                return false;
            }
            float cost = predecessorCost + evaluation.TotalCost;
            if (cost > request.MaxCost)
            {
                reason = "CostLimitExceeded";
                return false;
            }
            var states = new List<AccessV2BandState>(1)
            {
                transition.Next,
            };
            var transitions = new List<AccessV2Transition>(1)
            {
                transition,
            };
            var evaluations = new List<AccessV2TransitionEvaluation>(1)
            {
                evaluation,
            };
            branch = new TerminalBranch(
                transition.Next,
                history.ApplyValidated(
                    transition,
                    evaluation.RayConstraints,
                    evaluation.CleanupKeys,
                    request.SafetyExemptionProvider?.Invoke(
                        predecessor)
                        ?? Array.Empty<Tile2i>()),
                cost,
                evaluation,
                states,
                transitions,
                evaluations);
            return true;
        }

        private static AccessV2Transition WithOperation(
            AccessV2Transition transition,
            AccessHandoffOperation operation)
            => transition.WorkOperation == operation
                ? transition
                : new AccessV2Transition(
                    transition.Kind,
                    transition.Next,
                    transition.Delta,
                    transition.LocalContextOrigins,
                    transition.OldDirectionTurnRays,
                    operation,
                    transition.ScoreOnlyGeneratedExteriorRays);

        private static bool IsCancelled(in AccessV2TerminalRequest request)
            => request.SliceBudget?.CancellationRequested == true;

        private static bool IsTerrainOperation(AccessHandoffOperation operation)
            => operation == AccessHandoffOperation.Mining
                || operation == AccessHandoffOperation.Dumping;

        private static Tile2i GetCenter(
            AccessV2BandState first,
            int row,
            int file)
        {
            bool travelsX = first.Axis == AccessV2TravelAxis.X;
            int sign = travelsX
                ? Math.Sign(first.EntryDirection.X)
                : Math.Sign(first.EntryDirection.Y);
            int longitudinalOffset = sign > 0 ? row + 1 : 3 - row;
            int transverseMin = travelsX
                ? Math.Min(first.GetLaneOrigin(0).Y,
                    first.GetLaneOrigin(1).Y)
                : Math.Min(first.GetLaneOrigin(0).X,
                    first.GetLaneOrigin(1).X);
            int longitudinal = travelsX
                ? first.Anchor.X + longitudinalOffset
                : first.Anchor.Y + longitudinalOffset;
            int transverse = transverseMin + file + 2;
            return travelsX
                ? new Tile2i(longitudinal, transverse)
                : new Tile2i(transverse, longitudinal);
        }

        private static Tile2i FindOwner(
            Tile2i center,
            IReadOnlyList<Tile2i> lane0Origins,
            IReadOnlyList<Tile2i> lane1Origins)
        {
            for (int index = 0; index < lane0Origins.Count; index++)
                if (IsInsideOrigin(center, lane0Origins[index]))
                    return lane0Origins[index];
            for (int index = 0; index < lane1Origins.Count; index++)
                if (IsInsideOrigin(center, lane1Origins[index]))
                    return lane1Origins[index];
            return lane0Origins.Count > 0
                ? lane0Origins[lane0Origins.Count - 1]
                : lane1Origins[lane1Origins.Count - 1];
        }

        private static bool IsInsideOrigin(Tile2i tile, Tile2i origin)
            => tile.X >= origin.X && tile.X < origin.X + 4
                && tile.Y >= origin.Y && tile.Y < origin.Y + 4;

        private static Tile2i GetCompanionContact(
            Tile2i origin,
            Tile2i direction,
            Tile2i contact)
        {
            bool travelsX = direction.X != 0;
            int transverse = travelsX ? contact.Y : contact.X;
            int originTransverse = travelsX ? origin.Y : origin.X;
            int clamped = Math.Max(originTransverse,
                Math.Min(originTransverse + 4, transverse));
            return travelsX
                ? new Tile2i(contact.X, clamped)
                : new Tile2i(clamped, contact.Y);
        }

        private sealed class TerminalBranch
        {
            public AccessV2BandState State { get; }
            public AccessV2History History { get; }
            public float Cost { get; }
            public float TraversalCost { get; private set; }
            public float GeneratedWorkCost { get; private set; }
            public float GeneratedFixedCost { get; private set; }
            public float DirectWorkCost { get; private set; }
            public float ExteriorRayCost { get; private set; }
            public float CleanupCost { get; private set; }
            public List<AccessV2BandState> States { get; }
            public List<AccessV2Transition> Transitions { get; }
            public List<AccessV2TransitionEvaluation> Evaluations { get; }
            private AccessV2TerminalCrestState m_lane0Crest;
            private AccessV2TerminalCrestState m_lane1Crest;
            private int m_lane0FreezeRank;
            private int m_lane1FreezeRank;
            public byte FrozenLanes
                => (byte)((m_lane0FreezeRank > 0 ? 1 : 0)
                    | (m_lane1FreezeRank > 0 ? 2 : 0));

            public TerminalBranch(
                AccessV2BandState state,
                AccessV2History history,
                float cost,
                AccessV2TransitionEvaluation evaluation,
                List<AccessV2BandState> states,
                List<AccessV2Transition> transitions,
                List<AccessV2TransitionEvaluation> evaluations)
            {
                State = state;
                History = history;
                Cost = cost;
                States = states;
                Transitions = transitions;
                Evaluations = evaluations;
                Add(evaluation);
            }

            public void SetCrests(
                AccessV2TerminalCrestState lane0,
                AccessV2TerminalCrestState lane1)
            {
                SetCrest(0, lane0,
                    lane0 == AccessV2TerminalCrestState.Full ? 1 : 0);
                SetCrest(1, lane1,
                    lane1 == AccessV2TerminalCrestState.Full ? 1 : 0);
            }

            public void SetCrest(
                int lane,
                AccessV2TerminalCrestState crest,
                int freezeRank)
            {
                if (lane == 0)
                {
                    m_lane0Crest = crest;
                    m_lane0FreezeRank = freezeRank;
                }
                else
                {
                    m_lane1Crest = crest;
                    m_lane1FreezeRank = freezeRank;
                }
            }

            public AccessV2TerminalCrestState GetCrest(int lane)
                => lane == 0 ? m_lane0Crest : m_lane1Crest;

            public bool IsFrozen(int lane)
                => GetFreezeRank(lane) > 0;

            public int GetFreezeRank(int lane)
                => lane == 0 ? m_lane0FreezeRank : m_lane1FreezeRank;

            private void Add(AccessV2TransitionEvaluation evaluation)
            {
                TraversalCost += evaluation.TraversalCost;
                GeneratedWorkCost += evaluation.GeneratedWorkCost;
                GeneratedFixedCost += evaluation.GeneratedFixedCost;
                DirectWorkCost += evaluation.DirectWorkCost;
                ExteriorRayCost += evaluation.ExteriorRayCost;
                CleanupCost += evaluation.CleanupCost;
            }

            public void Append(TerminalBranch parent)
            {
                States.InsertRange(0, parent.States);
                Transitions.InsertRange(0, parent.Transitions);
                Evaluations.InsertRange(0, parent.Evaluations);
                TraversalCost += parent.TraversalCost;
                GeneratedWorkCost += parent.GeneratedWorkCost;
                GeneratedFixedCost += parent.GeneratedFixedCost;
                DirectWorkCost += parent.DirectWorkCost;
                ExteriorRayCost += parent.ExteriorRayCost;
                CleanupCost += parent.CleanupCost;
            }
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal static class AccessV2Replay
    {
        public static bool TryReplay(
            AccessSearchSnapshot snapshot,
            AccessSearchWorkspace workspace,
            AccessV2RouteData route,
            out AccessV2History history,
            out IReadOnlyList<AccessV2OriginProfile> orderedGenerated,
            out AccessV2HandoffCandidate? replayedHandoff,
            out string reason)
        {
            history = AccessV2History.Empty;
            replayedHandoff = null;
            var ordered = new List<AccessV2OriginProfile>();
            orderedGenerated = ordered;
            if (route.RouteSteps.Count > 0)
                return TryReplaySegmented(
                    snapshot, workspace, route, ref history, ordered,
                    out replayedHandoff, out reason);
            if (route.States.Count == 0)
            {
                reason = "V2ReplayEmptyStates";
                return false;
            }

            AccessV2BandState first = route.States[0];
            var initialDelta = new List<AccessV2OriginProfile>();
            var initialContext = new List<Tile2i>();
            Tile2i? connectedFixed = null;
            for (int lane = 0; lane < 2; lane++)
            {
                AccessV2OriginProfile item = first.GetLane(lane);
                if (route.GeneratedProfiles.TryGetValue(
                        item.Origin, out AccessHeightProfile generatedProfile))
                {
                    if (!ProfilesEqual(item.Profile, generatedProfile))
                    {
                        reason = "V2ReplayInitialProfileMismatch";
                        return false;
                    }
                    initialDelta.Add(item);
                }
                else
                {
                    initialContext.Add(item.Origin);
                    if (!connectedFixed.HasValue) connectedFixed = item.Origin;
                }
            }
            if (initialDelta.Count > 0)
            {
                var initial = new AccessV2Transition(
                    AccessV2TransitionKind.SourceLaunch,
                    first,
                    initialDelta,
                    initialContext,
                    scoreOnlyGeneratedExteriorRays: true);
                if (!Apply(
                        snapshot, null, initial, connectedFixed,
                        ref history, ordered, out reason))
                    return false;
            }

            for (int index = 1; index < route.States.Count; index++)
            {
                AccessV2BandState previous = route.States[index - 1];
                AccessV2BandState next = route.States[index];
                if (!TryInferTransition(
                        route.States, index, previous, next, history,
                        out AccessV2Transition transition))
                {
                    reason = "V2ReplayTransitionMissing";
                    return false;
                }
                for (int deltaIndex = 0;
                    deltaIndex < transition.Delta.Count;
                    deltaIndex++)
                {
                    AccessV2OriginProfile item = transition.Delta[deltaIndex];
                    if (!route.GeneratedProfiles.TryGetValue(
                            item.Origin, out AccessHeightProfile generatedProfile)
                        || !ProfilesEqual(item.Profile, generatedProfile))
                    {
                        reason = "V2ReplayDeltaMismatch";
                        return false;
                    }
                }
                if (!Apply(
                        snapshot, previous, transition, null,
                        ref history, ordered, out reason))
                    return false;
            }

            IReadOnlyDictionary<Tile2i, AccessHeightProfile> flattened =
                history.Flatten();
            if (flattened.Count != route.GeneratedProfiles.Count)
            {
                reason = "V2ReplayGeneratedCountMismatch";
                return false;
            }
            foreach (KeyValuePair<Tile2i, AccessHeightProfile> pair
                in route.GeneratedProfiles)
            {
                if (!flattened.TryGetValue(
                        pair.Key, out AccessHeightProfile replayed)
                    || !ProfilesEqual(pair.Value, replayed))
                {
                    reason = "V2ReplayGeneratedProfileMismatch";
                    return false;
                }
            }

            if (route.Handoff != null)
            {
                IReadOnlyList<AccessV2BandState> recent = route.States
                    .Reverse()
                    .Take(AccessV2Handoffs.MaxSpanLength)
                    .ToArray();
                replayedHandoff = AccessPathSearch.EvaluateV2Handoffs(
                        snapshot, workspace, recent, history)
                    .FirstOrDefault(candidate => HandoffsEqual(
                        candidate, route.Handoff));
                if (replayedHandoff == null)
                {
                    reason = "V2ReplayHandoffMismatch";
                    return false;
                }
                if (!ReplayGroundPath(
                        snapshot, route.GroundPath,
                        route.Handoff, history,
                        route.TerminalGoalCenters, out reason))
                    return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool TryReplaySegmented(
            AccessSearchSnapshot snapshot,
            AccessSearchWorkspace workspace,
            AccessV2RouteData route,
            ref AccessV2History history,
            ICollection<AccessV2OriginProfile> ordered,
            out AccessV2HandoffCandidate? replayedHandoff,
            out string reason)
        {
            replayedHandoff = null;
            if (route.RouteSteps.Count == 0
                || route.RouteSteps[0].IsGround)
            {
                reason = "V2ReplayInvalidFirstStep";
                return false;
            }

            AccessV2RouteStep firstStep = route.RouteSteps[0];
            AccessV2BandState first = firstStep.State;
            var initialDelta = new List<AccessV2OriginProfile>();
            var initialContext = new List<Tile2i>();
            Tile2i? connectedFixed = null;
            for (int lane = 0; lane < 2; lane++)
            {
                AccessV2OriginProfile item = first.GetLane(lane);
                if (route.GeneratedProfiles.TryGetValue(
                        item.Origin, out AccessHeightProfile generated))
                {
                    if (!ProfilesEqual(item.Profile, generated))
                    {
                        reason = "V2ReplayInitialProfileMismatch";
                        return false;
                    }
                    initialDelta.Add(item);
                }
                else
                {
                    initialContext.Add(item.Origin);
                    if (!connectedFixed.HasValue) connectedFixed = item.Origin;
                }
            }
            if (initialDelta.Count > 0)
            {
                var initial = new AccessV2Transition(
                    AccessV2TransitionKind.SourceLaunch,
                    first, initialDelta, initialContext,
                    scoreOnlyGeneratedExteriorRays: true);
                if (!Apply(
                        snapshot, null, initial, connectedFixed,
                        ref history, ordered, out reason))
                    return false;
            }

            for (int index = 1; index < route.RouteSteps.Count; index++)
            {
                AccessV2RouteStep previous = route.RouteSteps[index - 1];
                AccessV2RouteStep step = route.RouteSteps[index];
                if (!step.IsGround)
                {
                    if (step.Transition == null)
                    {
                        reason = "V2ReplayTransitionMissing";
                        return false;
                    }
                    AccessV2BandState? current = previous.IsGround
                        ? (AccessV2BandState?)null
                        : previous.State;
                    Tile2i? transitionConnectedFixed = null;
                    if (step.Transition.ScoreOnlyGeneratedExteriorRays
                        && current.HasValue)
                    {
                        for (int lane = 0; lane < 2; lane++)
                        {
                            Tile2i origin =
                                current.Value.GetLaneOrigin(lane);
                            if (!route.GeneratedProfiles.ContainsKey(origin))
                            {
                                transitionConnectedFixed = origin;
                                break;
                            }
                        }
                    }
                    if (!Apply(
                            snapshot, current, step.Transition,
                            transitionConnectedFixed,
                            ref history, ordered, out reason))
                        return false;

                    if (previous.IsGround)
                    {
                        if (step.Handoff == null)
                        {
                            reason = "V2ReplayGroundToVSeamMissing";
                            return false;
                        }
                        AccessV2HandoffCandidate recorded = step.Handoff;
                        Tile2i groundCenter = previous.GroundCenter!.Value;
                        if (recorded.SpanLength != 1)
                        {
                            reason = "V2ReplayGroundToVSpanInvalid";
                            return false;
                        }
                        AccessV2HandoffCandidate? replayed =
                            IsDirectLevelingBridge(recorded, groundCenter)
                                && AccessV2Handoffs.TryCreateDirectLevelingBridge(
                                    step.State, groundCenter,
                                    recorded.CenterSpokeCost,
                                    out AccessV2HandoffCandidate direct)
                                ? direct
                                : AccessPathSearch.EvaluateV2GroundToVHandoff(
                                    snapshot, workspace, step.State, groundCenter,
                                    recorded.Lane0Operation, history);
                        if (replayed == null
                            || !HandoffsEqual(replayed, recorded))
                        {
                            reason = "V2ReplayGroundToVSeamMismatch";
                            return false;
                        }
                        history = history.ApplyCleanupKeys(
                            replayed.CleanupKeys);
                    }
                    continue;
                }

                Tile2i center = step.GroundCenter!.Value;
                if (!previous.IsGround)
                {
                    if (step.IsProjectedGroundEntry)
                    {
                        AccessV2GroundGraph? projectedGraph =
                            snapshot.V2GroundGraph;
                        AccessV2History entryHistory = history;
                        if (step.Handoff != null
                            || projectedGraph == null
                            || !IsProjectedGroundEntryValid(
                                previous.State, center,
                                origin => snapshot.TryGetFixedProfile(
                                    origin,
                                    out AccessHeightProfile fixedProfile)
                                        ? fixedProfile
                                        : (AccessHeightProfile?)null,
                                projectedGraph.IsProjectedFixedGround,
                                tile => snapshot
                                    .IsProjectedV2CenterPathable(
                                        tile, entryHistory)))
                        {
                            reason =
                                "V2ReplayProjectedGroundEntryMismatch";
                            return false;
                        }
                        continue;
                    }
                    if (step.Handoff == null)
                    {
                        reason = "V2ReplayVToGroundSeamMissing";
                        return false;
                    }
                    var recent = new List<AccessV2BandState>();
                    for (int recentIndex = index - 1;
                        recentIndex >= 0
                            && recent.Count < AccessV2Handoffs.MaxSpanLength;
                        recentIndex--)
                    {
                        AccessV2RouteStep recentStep = route.RouteSteps[recentIndex];
                        if (recentStep.IsGround) break;
                        recent.Add(recentStep.State);
                        }
                    AccessV2History historyAtHandoff = history;
                    IReadOnlyList<AccessV2HandoffCandidate> replayCandidates;
                    if (step.Handoff.IsBoundedTerminal)
                    {
                        AccessV2GroundGraph? graph = snapshot.V2GroundGraph;
                        if (!TryValidateBoundedTerminalMetadata(
                                recent,
                                step.Handoff,
                                out string terminalMetadataReason)
                            || graph == null
                            || !graph.TryValidateLocalEscape(
                                step.Handoff.GroundEntryCenters,
                                historyAtHandoff,
                                snapshot.LandscapingCostDistanceScale,
                                out _, out _))
                        {
                            reason = "V2ReplayBoundedTerminalProofMismatch:" +
                                terminalMetadataReason;
                            return false;
                        }
                        replayCandidates = new[] { step.Handoff };
                    }
                    else if (step.Handoff.IsStaggeredExtension)
                    {
                        int extensionLane = step.Handoff.NonCrestLane;
                        replayCandidates =
                            AccessPathSearch.EvaluateV2StaggeredHandoffs(
                                snapshot,
                                workspace,
                                recent.Take(step.Handoff.SpanLength)
                                    .Reverse().ToArray(),
                                extensionLane,
                                step.Handoff.Lane0Operation,
                                historyAtHandoff);
                    }
                    else
                        replayCandidates = AccessPathSearch.EvaluateV2Handoffs(
                            snapshot, workspace, recent, historyAtHandoff);
                    replayedHandoff = replayCandidates
                        .FirstOrDefault(candidate =>
                            HandoffsEqual(candidate, step.Handoff)
                            && candidate.GroundEntryCenters.Contains(center));
                    if (replayedHandoff == null)
                    {
                        reason = "V2ReplayHandoffMismatch";
                        return false;
                    }
                    history = history.ApplyCleanupKeys(
                        replayedHandoff.CleanupKeys);
                }
                else
                {
                    Tile2i from = previous.GroundCenter!.Value;
                    AccessV2GroundGraph? graph = snapshot.V2GroundGraph;
                    IReadOnlyList<Tile2i> swept =
                        AccessV2GroundGraph.GetSweptCenters(from, center);
                    AccessV2History replayHistory = history;
                    if (graph == null || !graph.CanTraverse(from, center)
                        || swept.Any(item =>
                            !snapshot.IsProjectedV2CenterPathable(
                                item, replayHistory))
                        || !graph.TryValidateLocalEscape(
                            swept, history,
                            snapshot.LandscapingCostDistanceScale,
                            out _, out _))
                    {
                        reason = "V2ReplayGroundCorridor";
                        return false;
                    }
                }
            }

            IReadOnlyDictionary<Tile2i, AccessHeightProfile> flattened =
                history.Flatten();
            if (flattened.Count != route.GeneratedProfiles.Count
                || route.GeneratedProfiles.Any(pair =>
                    !flattened.TryGetValue(pair.Key, out AccessHeightProfile value)
                    || !ProfilesEqual(pair.Value, value)))
            {
                reason = "V2ReplayGeneratedProfileMismatch";
                return false;
            }
            AccessV2RouteStep last = route.RouteSteps[
                route.RouteSteps.Count - 1];
            if (last.IsGround
                && (snapshot.V2GroundGraph == null
                    || (!snapshot.V2GroundGraph.IsGoal(
                            last.GroundCenter!.Value)
                        && !route.TerminalGoalCenters.Contains(
                            last.GroundCenter.Value))))
            {
                reason = "V2ReplayGroundGoal";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        internal static bool IsProjectedGroundEntryValid(
            AccessV2BandState fixedState,
            Tile2i groundCenter,
            Func<Tile2i, AccessHeightProfile?> fixedProfileProvider,
            Func<Tile2i, bool> projectedGroundValidator,
            Func<Tile2i, bool> pathabilityValidator)
        {
            if (groundCenter
                    != AccessV2PotentialField.GetCanonicalCenter(fixedState)
                || !projectedGroundValidator(groundCenter)
                || !pathabilityValidator(groundCenter))
                return false;
            for (int lane = 0; lane < 2; lane++)
            {
                AccessV2OriginProfile item = fixedState.GetLane(lane);
                AccessHeightProfile? fixedProfile =
                    fixedProfileProvider(item.Origin);
                if (!fixedProfile.HasValue
                    || !ProfilesEqual(
                        fixedProfile.Value, item.Profile))
                    return false;
            }
            return true;
        }

        private static bool TryValidateBoundedTerminalMetadata(
            IReadOnlyList<AccessV2BandState> recentNewestFirst,
            AccessV2HandoffCandidate handoff,
            out string reason)
        {
            if (handoff.TerminalRanks.Count == 0
                || recentNewestFirst.Count < handoff.TerminalRanks.Count)
            {
                reason = "RankDataMissing";
                return false;
            }
            if (handoff.TerminalFrontage.OutwardDirection
                != recentNewestFirst[0].EntryDirection)
            {
                reason = "FrontageDirection";
                return false;
            }
            for (int index = 0;
                index < handoff.TerminalRanks.Count;
                index++)
            {
                AccessV2TerminalRankDelta delta =
                    handoff.TerminalRanks[index];
                AccessV2BandState state = recentNewestFirst[
                    handoff.TerminalRanks.Count - index - 1];
                if (delta.Rank != index + 1
                    || !RankLaneMatches(
                        handoff.TerminalRanks, delta, state, 0)
                    || !RankLaneMatches(
                        handoff.TerminalRanks, delta, state, 1))
                {
                    reason = "RankDelta" + (index + 1);
                    return false;
                }
            }
            reason = string.Empty;
            return true;

            bool RankLaneMatches(
                IReadOnlyList<AccessV2TerminalRankDelta> ranks,
                AccessV2TerminalRankDelta rankDelta,
                AccessV2BandState routeState,
                int lane)
            {
                AccessV2OriginProfile actual = lane == 0
                    ? rankDelta.Lane0
                    : rankDelta.Lane1;
                bool frozen = (rankDelta.FrozenLanes & (1 << lane)) != 0;
                AccessV2OriginProfile expected = frozen
                    ? (lane == 0 ? ranks[0].Lane0 : ranks[0].Lane1)
                    : routeState.GetLane(lane);
                return actual.Origin == expected.Origin
                    && ProfilesEqual(actual.Profile, expected.Profile);
            }
        }

        private static bool IsDirectLevelingBridge(
            AccessV2HandoffCandidate handoff,
            Tile2i? groundCenter)
            => groundCenter.HasValue
                && handoff.IsQuickPath
                && handoff.SpanLength == 1
                && handoff.Lane0Operation == AccessHandoffOperation.Leveling
                && handoff.Lane1Operation == AccessHandoffOperation.Leveling
                && handoff.GroundEntryCenters.Contains(groundCenter.Value)
                && handoff.CleanupKeys.Count == 0;

        private static bool ReplayGroundPath(
            AccessSearchSnapshot snapshot,
            IReadOnlyList<Tile2i> groundPath,
            AccessV2HandoffCandidate handoff,
            AccessV2History history,
            IReadOnlyCollection<Tile2i> terminalGoalCenters,
            out string reason)
        {
            AccessV2GroundGraph? graph = snapshot.V2GroundGraph;
            if (graph == null || groundPath.Count == 0
                || !handoff.GroundEntryCenters.Contains(groundPath[0]))
            {
                reason = "V2ReplayGroundEntry";
                return false;
            }
            for (int index = 0; index < groundPath.Count; index++)
            {
                Tile2i current = groundPath[index];
                IReadOnlyList<Tile2i> swept = index == 0
                    ? new[] { current }
                    : AccessV2GroundGraph.GetSweptCenters(
                        groundPath[index - 1], current);
                if (index > 0
                    && !graph.CanTraverse(groundPath[index - 1], current))
                {
                    reason = "V2ReplayGroundEdge";
                    return false;
                }
                if (swept.Any(center =>
                        !snapshot.IsProjectedV2CenterPathable(center, history))
                    || !graph.TryValidateLocalEscape(
                        swept, history,
                        snapshot.LandscapingCostDistanceScale,
                        out _, out _))
                {
                    reason = "V2ReplayGroundCorridor";
                    return false;
                }
            }
            if (!graph.IsGoal(groundPath[groundPath.Count - 1])
                && !terminalGoalCenters.Contains(
                    groundPath[groundPath.Count - 1]))
            {
                reason = "V2ReplayGroundGoal";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private static bool Apply(
            AccessSearchSnapshot snapshot,
            AccessV2BandState? current,
            AccessV2Transition transition,
            Tile2i? connectedFixed,
            ref AccessV2History history,
            ICollection<AccessV2OriginProfile> ordered,
            out string reason)
        {
            if (!history.TryApply(transition, out _, out reason))
                return false;
            AccessV2TransitionEvaluation evaluation =
                AccessPathSearch.EvaluateV2Transition(
                    snapshot, current, transition,
                    history, connectedFixed,
                    transition.WorkOperation);
            if (!evaluation.IsValid)
            {
                reason = evaluation.RejectionReason;
                return false;
            }
            var snapshotSafetyExemptOrigins = new HashSet<Tile2i>();
            if (connectedFixed.HasValue)
                snapshotSafetyExemptOrigins.Add(connectedFixed.Value);
            if (current.HasValue)
            {
                for (int lane = 0; lane < 2; lane++)
                {
                    Tile2i origin = current.Value.GetLaneOrigin(lane);
                    if (snapshot.TryGetFixedProfile(origin, out _))
                        snapshotSafetyExemptOrigins.Add(origin);
                }
            }
            history = history.ApplyValidated(
                transition,
                evaluation.RayConstraints,
                evaluation.CleanupKeys,
                snapshotSafetyExemptOrigins);
            for (int index = 0; index < transition.Delta.Count; index++)
                ordered.Add(transition.Delta[index]);
            return true;
        }

        private static bool TryInferTransition(
            IReadOnlyList<AccessV2BandState> states,
            int index,
            AccessV2BandState previous,
            AccessV2BandState next,
            AccessV2History history,
            out AccessV2Transition transition)
        {
            foreach (AccessV2Transition candidate in
                AccessV2Geometry.EnumerateStraight(previous))
            {
                if (candidate.Next.Equals(next))
                {
                    transition = candidate;
                    return true;
                }
            }
            for (int sign = -1; sign <= 1; sign += 2)
            {
                if (AccessV2Geometry.TryStrafe(
                        previous, sign,
                        out AccessV2Transition strafe, out _)
                    && strafe.Next.Equals(next))
                {
                    transition = strafe;
                    return true;
                }
                AccessV2Transition? turn = null;
                for (int predecessorIndex = index - 2;
                    predecessorIndex >= 0;
                    predecessorIndex--)
                {
                    AccessV2BandState candidatePredecessor =
                        states[predecessorIndex];
                    if (candidatePredecessor.Axis != previous.Axis
                        || candidatePredecessor.EntryDirection
                            != previous.EntryDirection
                        || candidatePredecessor.Anchor !=
                            AccessV2Geometry.Subtract(
                                previous.Anchor, previous.EntryDirection))
                        continue;
                    if (AccessV2Geometry.TryTurn(
                            candidatePredecessor, previous, sign,
                            out AccessV2Transition candidateTurn, out _))
                    {
                        turn = candidateTurn;
                        break;
                    }
                }
                if (turn == null
                    && AccessV2Geometry.TryTurn(
                        previous, history, sign,
                        out AccessV2Transition historyTurn, out _))
                    turn = historyTurn;
                if (turn != null
                    && turn.Next.Equals(next))
                {
                    transition = turn;
                    return true;
                }
            }
            transition = null!;
            return false;
        }

        private static bool HandoffsEqual(
            AccessV2HandoffCandidate left,
            AccessV2HandoffCandidate right)
            => left.ExitDirection == right.ExitDirection
                && left.SpanLength == right.SpanLength
                && left.Lane0Operation == right.Lane0Operation
                    && left.Lane1Operation == right.Lane1Operation
                    && left.IsStaggeredExtension
                        == right.IsStaggeredExtension
                    && left.IsBoundedTerminal
                        == right.IsBoundedTerminal
                    && left.NonCrestLane == right.NonCrestLane
                && left.Lane0Contact == right.Lane0Contact
                && left.Lane1Contact == right.Lane1Contact
                && left.Lane0TerminalOrigins.SequenceEqual(
                    right.Lane0TerminalOrigins)
                && left.Lane1TerminalOrigins.SequenceEqual(
                    right.Lane1TerminalOrigins);

        private static bool ProfilesEqual(
            AccessHeightProfile left,
            AccessHeightProfile right)
            => left.Nw2 == right.Nw2
                && left.Ne2 == right.Ne2
                && left.Se2 == right.Se2
                && left.Sw2 == right.Sw2;
    }
}

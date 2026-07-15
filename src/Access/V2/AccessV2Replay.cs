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
                    AccessV2TransitionKind.Strafe,
                    first,
                    initialDelta,
                    initialContext);
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
                        route.States, index, previous, next,
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
                        snapshot, recent, history)
                    .FirstOrDefault(candidate => HandoffsEqual(
                        candidate, route.Handoff));
                if (replayedHandoff == null)
                {
                    reason = "V2ReplayHandoffMismatch";
                    return false;
                }
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
                    history, connectedFixed);
            if (!evaluation.IsValid)
            {
                reason = evaluation.RejectionReason;
                return false;
            }
            if (!history.TryApply(
                    transition.Delta,
                    transition.LocalContextOrigins,
                    evaluation.RayConstraints,
                    evaluation.CleanupKeys,
                    out AccessV2History next,
                    out reason))
                return false;
            history = next;
            for (int index = 0; index < transition.Delta.Count; index++)
                ordered.Add(transition.Delta[index]);
            return true;
        }

        private static bool TryInferTransition(
            IReadOnlyList<AccessV2BandState> states,
            int index,
            AccessV2BandState previous,
            AccessV2BandState next,
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
                if (index >= 2
                    && AccessV2Geometry.TryTurn(
                        states[index - 2], previous, sign,
                        out AccessV2Transition turn, out _)
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

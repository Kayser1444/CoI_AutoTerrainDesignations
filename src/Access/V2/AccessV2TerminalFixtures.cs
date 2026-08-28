using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal static class AccessV2TerminalFixtures
    {
        internal static bool ValidateAll(out string failure)
        {
            if (!ValidateCardinalTurn(out failure))
                return false;
            if (!ValidateBlockedMask(out failure))
                return false;
            if (!ValidateMinimumGoalDistance(out failure))
                return false;
            if (!ValidateFullFourRankMask(out failure))
                return false;
            if (!ValidateCleanupDominance(out failure))
                return false;
            if (!ValidateCrestClassifier(out failure))
                return false;
            if (!ValidateBoundedRankOne(out failure))
                return false;
            failure = string.Empty;
            return true;
        }

        private static bool ValidateCardinalTurn(out string failure)
        {
            AccessV2TerminalMask pathable =
                AccessV2TerminalMask.Single(0, 0)
                .Or(AccessV2TerminalMask.Single(0, 1))
                .Or(AccessV2TerminalMask.Single(1, 1))
                .Or(AccessV2TerminalMask.Single(1, 2));
            AccessV2TerminalProof proof =
                AccessV2TerminalProofHelper.FindMinimumCardinalProof(
                    pathable,
                    AccessV2TerminalMask.Single(0, 0),
                    AccessV2TerminalMask.Single(1, 2));
            if (!proof.Success || proof.Distance != 3)
            {
                failure = $"Cardinal mask turn proof expected distance 3, got " +
                    $"success={proof.Success} distance={proof.Distance}";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool ValidateBlockedMask(out string failure)
        {
            AccessV2TerminalMask pathable =
                AccessV2TerminalMask.Single(0, 0)
                .Or(AccessV2TerminalMask.Single(0, 1))
                .Or(AccessV2TerminalMask.Single(1, 0));
            AccessV2TerminalProof proof =
                AccessV2TerminalProofHelper.FindMinimumCardinalProof(
                    pathable,
                    AccessV2TerminalMask.Single(0, 0),
                    AccessV2TerminalMask.Single(3, 3));
            if (proof.Success || proof.Distance != -1)
            {
                failure = "Blocked cardinal mask unexpectedly reached goal";
                return false;
            }
            AccessV2TerminalMask wrappedPath =
                AccessV2TerminalMask.Single(0, 3)
                .Or(AccessV2TerminalMask.Single(1, 0));
            AccessV2TerminalProof wrappedProof =
                AccessV2TerminalProofHelper.FindMinimumCardinalProof(
                    wrappedPath,
                    AccessV2TerminalMask.Single(0, 3),
                    AccessV2TerminalMask.Single(1, 0));
            if (wrappedProof.Success)
            {
                failure = "Cardinal mask incorrectly wrapped across file rows";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool ValidateMinimumGoalDistance(out string failure)
        {
            AccessV2TerminalMask pathable =
                AccessV2TerminalMask.Row(0, 4)
                .Or(AccessV2TerminalMask.Row(1, 4))
                .Or(AccessV2TerminalMask.Row(2, 4));
            AccessV2TerminalProof proof =
                AccessV2TerminalProofHelper.FindMinimumCardinalProof(
                    pathable,
                    AccessV2TerminalMask.Single(0, 0),
                    AccessV2TerminalMask.Single(2, 3)
                        .Or(AccessV2TerminalMask.Single(2, 0)));
            if (!proof.Success || proof.Distance != 2
                || (proof.GoalBits
                    & AccessV2TerminalMask.Single(2, 0).Bits) == 0UL)
            {
                failure = "Cardinal proof did not retain the minimum-distance goal";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool ValidateFullFourRankMask(out string failure)
        {
            AccessV2TerminalMask farCorner =
                AccessV2TerminalMask.Single(15, 3);
            if (!farCorner.Contains(15, 3)
                || AccessV2TerminalMask.GridMask(4) != ulong.MaxValue)
            {
                failure = "Four terminal ranks must expose all sixteen center rows";
                return false;
            }
            AccessV2TerminalMask pathable = new AccessV2TerminalMask(
                ulong.MaxValue,
                4);
            AccessV2TerminalProof proof =
                AccessV2TerminalProofHelper.FindMinimumCardinalProof(
                    pathable,
                    AccessV2TerminalMask.Single(0, 0),
                    farCorner);
            if (!proof.Success || proof.Distance != 18)
            {
                failure = "Four-rank proof did not traverse the full 16x4 mask"
                    + $": success={proof.Success} distance={proof.Distance}";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool ValidateCleanupDominance(out string failure)
        {
            Tile2i entry = new Tile2i(8, 8);
            AccessV2TerminalCandidate dominated = Candidate(
                2f, new[] { "cleanup:a", "cleanup:b" });
            AccessV2TerminalCandidate cleanupA = Candidate(
                1f, new[] { "cleanup:a" });
            AccessV2TerminalCandidate cleanupC = Candidate(
                1f, new[] { "cleanup:c" });
            IReadOnlyList<AccessV2TerminalCandidate> selected =
                AccessV2TerminalEvaluator.SelectNondominated(
                    new[] { dominated, cleanupA, cleanupC });
            if (selected.Count != 2
                || !ReferenceEquals(selected[0], cleanupA)
                || !ReferenceEquals(selected[1], cleanupC))
            {
                failure = "Terminal dominance must remove a more expensive cleanup superset while retaining incomparable cleanup sets";
                return false;
            }
            failure = string.Empty;
            return true;

            AccessV2TerminalCandidate Candidate(
                float cleanupCost,
                IReadOnlyCollection<string> cleanupKeys)
                => new AccessV2TerminalCandidate(
                    AccessHandoffOperation.Mining,
                    1,
                    Array.Empty<AccessV2TerminalRankDelta>(),
                    default,
                    entry,
                    0,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    cleanupCost,
                    cleanupKeys);
        }

        private static bool ValidateBoundedRankOne(out string failure)
        {
            if (!AccessHeightProfile.TryForMode(
                    AccessSearchMode.Flat, 0,
                    out AccessHeightProfile flat)
                || !AccessV2BandProfile.TryCreateEnabled(
                    AccessV2TravelAxis.X, flat, flat,
                    out AccessV2BandProfile band, out _))
            {
                failure = "Could not create rank-one terminal fixture band";
                return false;
            }
            var predecessor = new AccessV2BandState(
                new Tile2i(4, 4), band, new Tile2i(4, 0));
            if (!AccessV2Geometry.TryStraight(
                    predecessor,
                    out AccessV2Transition straight,
                    out string straightReason))
            {
                failure = "Could not create rank-one terminal fixture transition: "
                    + straightReason;
                return false;
            }
            var groundTiles = new List<Tile2i>();
            for (int y = 0; y <= 32; y++)
                for (int x = 0; x <= 32; x++)
                    groundTiles.Add(new Tile2i(x, y));
            var ground = new AccessV2GroundGraph(
                groundTiles,
                new[] { new Tile2i(32, 16) },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            bool fullCrest = true;
            bool freezeAtRankTwo = false;
            bool crestApplicable = true;
            int singleHandoffCalls = 0;
            int transitionCalls = 0;
            IReadOnlyList<AccessGroundHandoff> Single(
                Tile2i origin,
                AccessHeightProfile profile,
                Tile2i previousOrigin,
                AccessHeightProfile previousProfile)
            {
                singleHandoffCalls++;
                return origin.Y == straight.Next.GetLaneOrigin(0).Y
                    ? BuildMiningContacts(origin, 5)
                    : Array.Empty<AccessGroundHandoff>();
            }
            IReadOnlyList<AccessGroundHandoff> Span(
                IReadOnlyList<AccessHandoffSpanCell> cells)
                => new[]
                {
                    new AccessGroundHandoff(
                        cells[cells.Count - 1].Origin + new RelTile2i(4, 2),
                        AccessHandoffOperation.Mining,
                        spanLength: cells.Count),
                };
            AccessV2TransitionEvaluation Transition(
                AccessV2BandState? current,
                AccessV2Transition transition,
                AccessV2History history,
                Tile2i? fixedOrigin,
                AccessHandoffOperation operation)
            {
                transitionCalls++;
                return new AccessV2TransitionEvaluation(
                    true, string.Empty, 1f, transition.Delta.Count, 0f);
            }
            int centerClassifications = 0;
            var request = new AccessV2TerminalRequest(
                predecessor,
                straight,
                AccessV2History.Empty,
                0f,
                null,
                new[] { straight.Next, predecessor },
                ground,
                Single,
                Span,
                (next, activeLanes, expectedOperation) =>
                    crestApplicable
                        ? new AccessV2TerminalCrestEvidence(
                            AccessHandoffOperation.Mining,
                            freezeAtRankTwo
                                ? (next.Anchor == straight.Next.Anchor
                                    ? AccessV2TerminalCrestState.Partial
                                    : AccessV2TerminalCrestState.Full)
                                : fullCrest
                                    ? AccessV2TerminalCrestState.Full
                                    : AccessV2TerminalCrestState.Partial,
                            AccessV2TerminalCrestState.Uncrested,
                            "fixture", 0)
                        : new AccessV2TerminalCrestEvidence(
                            AccessHandoffOperation.None,
                            AccessV2TerminalCrestState.Uncrested,
                            AccessV2TerminalCrestState.Uncrested,
                            "NoLeadingEdgeCrest", 0),
                Transition,
                (origin, operation, center, history, origins) =>
                {
                    centerClassifications++;
                    return true;
                },
                (center, origins, history) => true,
                null,
                null,
                Tile2i.Zero,
                new Tile2i(32, 32),
                cleanupCostScale: 1f,
                centerSpokeCost: 2f,
                maxCost: float.MaxValue,
                vehicleWidth: 5);
            AccessV2TerminalResult result =
                AccessV2TerminalEvaluator.Evaluate(in request);
            if (!result.Succeeded
                || result.Candidates.Count != 1
                || result.Candidates[0].RankCount != 1
                || result.Candidates[0].CompatibilityHandoff == null
                || !result.Candidates[0].CompatibilityHandoff!
                    .IsBoundedTerminal
                || result.EvaluatedBranches != 1
                || result.MaxRank != 1
                || centerClassifications != 16)
            {
                failure = "Bounded rank-one terminal evaluator did not emit one bounded candidate"
                    + $": status={result.Status} candidates={result.Candidates.Count}"
                    + $" branches={result.EvaluatedBranches} rank={result.MaxRank}"
                    + $" centerClassifications={centerClassifications}";
                return false;
            }
            freezeAtRankTwo = true;
            AccessV2TerminalResult rankTwoResult =
                AccessV2TerminalEvaluator.Evaluate(in request);
            AccessV2TerminalCandidate? rankTwoCandidate = rankTwoResult.Candidates
                .FirstOrDefault(candidate => candidate.RankCount == 2);
            AccessV2HandoffCandidate? rankTwoHandoff =
                rankTwoCandidate?.CompatibilityHandoff;
            AccessV2BandState[] recentNewestFirst = rankTwoCandidate == null
                ? Array.Empty<AccessV2BandState>()
                : rankTwoCandidate.Transitions
                    .Select(transition => transition.Next)
                    .Reverse()
                    .ToArray();
            string rankTwoReason = "CandidateMissing";
            bool rankTwoMetadataValid = rankTwoHandoff != null
                && AccessV2Replay.TryValidateBoundedTerminalMetadata(
                    recentNewestFirst,
                    rankTwoHandoff,
                    out rankTwoReason);
            if (rankTwoCandidate == null
                || rankTwoHandoff == null
                || !rankTwoMetadataValid)
            {
                failure = "A lane first frozen at rank two must retain replay-compatible rank metadata"
                    + $": status={rankTwoResult.Status}"
                    + $" candidates={rankTwoResult.Candidates.Count}"
                    + $" reason={rankTwoReason}";
                return false;
            }
            freezeAtRankTwo = false;
            fullCrest = false;
            centerClassifications = 0;
            singleHandoffCalls = 0;
            AccessV2TerminalResult partialResult =
                AccessV2TerminalEvaluator.Evaluate(in request);
            if (partialResult.Status != AccessV2TerminalStatus.NoHandoff
                || partialResult.EvaluatedBranches != 40
                || partialResult.MaxRank != 4
                || centerClassifications != 0
                || singleHandoffCalls != 0)
            {
                failure = "Partial-only terminal edges must extend without running an exit proof"
                    + $": status={partialResult.Status}"
                    + $" branches={partialResult.EvaluatedBranches}"
                    + $" rank={partialResult.MaxRank}"
                    + $" centerClassifications={centerClassifications}"
                    + $" singleHandoffCalls={singleHandoffCalls}";
                return false;
            }
            crestApplicable = false;
            singleHandoffCalls = 0;
            transitionCalls = 0;
            AccessV2TerminalResult rejected =
                AccessV2TerminalEvaluator.Evaluate(in request);
            if (rejected.Status != AccessV2TerminalStatus.NotApplicable
                || singleHandoffCalls != 0
                || transitionCalls != 0
                || centerClassifications != 0)
            {
                failure = "Uncrested trigger rejection must not materialize handoffs or evaluate transitions"
                    + $": status={rejected.Status}"
                    + $" single={singleHandoffCalls}"
                    + $" transitions={transitionCalls}"
                    + $" centers={centerClassifications}";
                return false;
            }
            failure = string.Empty;
            return true;

            IReadOnlyList<AccessGroundHandoff> BuildMiningContacts(
                Tile2i origin,
                int count)
            {
                var contacts = new AccessGroundHandoff[count];
                for (int offset = 0; offset < contacts.Length; offset++)
                    contacts[offset] = new AccessGroundHandoff(
                        origin + new RelTile2i(4, offset),
                        AccessHandoffOperation.Mining);
                return contacts;
            }
        }

        private static bool ValidateCrestClassifier(out string failure)
        {
            if (!AccessHeightProfile.TryForMode(
                    AccessSearchMode.Flat, 0,
                    out AccessHeightProfile flat)
                || !AccessV2BandProfile.TryCreateEnabled(
                    AccessV2TravelAxis.X, flat, flat,
                    out AccessV2BandProfile band, out _))
            {
                failure = "Could not create terminal crest fixture band";
                return false;
            }
            var state = new AccessV2BandState(
                new Tile2i(8, 8), band, new Tile2i(4, 0));
            Dictionary<Tile2i, float> terrain = Terrain(1f, 1f);
            AccessV2TerminalCrestEvidence uncrested =
                AccessV2TerminalCrestClassifier.Classify(
                    state, 3, AccessHandoffOperation.None, terrain);
            if (uncrested.IsApplicable
                || uncrested.Reason != "NoLeadingEdgeCrest"
                || uncrested.TerrainReads != 18)
            {
                failure = "Uncrested two-lane frontage must reject after eighteen unique rank-one samples"
                    + $": applicable={uncrested.IsApplicable}"
                    + $" reason={uncrested.Reason}"
                    + $" reads={uncrested.TerrainReads}";
                return false;
            }

            terrain = Terrain(1f, 1f);
            SetLaneLeading(terrain, state, 0, -1f, offsets: 2);
            AccessV2TerminalCrestEvidence partial =
                AccessV2TerminalCrestClassifier.Classify(
                    state, 3, AccessHandoffOperation.None, terrain);
            if (!partial.IsApplicable
                || partial.Operation != AccessHandoffOperation.Mining
                || partial.Lane0 != AccessV2TerminalCrestState.Partial
                || partial.TerrainReads != 18)
            {
                failure = "Mixed leading samples must classify a partial mining crest"
                    + $": operation={partial.Operation} lane0={partial.Lane0}"
                    + $" reads={partial.TerrainReads}";
                return false;
            }

            terrain = Terrain(1f, 1f);
            SetLaneLeading(terrain, state, 0, -1f, offsets: 5);
            AccessV2TerminalCrestEvidence full =
                AccessV2TerminalCrestClassifier.Classify(
                    state, 3, AccessHandoffOperation.None, terrain);
            if (!full.IsApplicable
                || full.Lane0 != AccessV2TerminalCrestState.Full)
            {
                failure = "All leading samples across terrain must classify a full mining crest"
                    + $": operation={full.Operation} lane0={full.Lane0}";
                return false;
            }

            terrain = Terrain(1f, 1f);
            SetLaneIncoming(terrain, state, 1, -1f);
            Tile2i lane0Origin = state.GetLaneOrigin(0);
            Tile2i lane1Origin = state.GetLaneOrigin(1);
            terrain[new Tile2i(
                lane0Origin.X,
                Math.Max(lane0Origin.Y, lane1Origin.Y))] = 0f;
            AccessV2TerminalCrestEvidence mixed =
                AccessV2TerminalCrestClassifier.Classify(
                    state, 3, AccessHandoffOperation.None, terrain);
            if (mixed.IsApplicable
                || mixed.Reason != "MixedLeadingEdgeOperations"
                || mixed.TerrainReads != 9)
            {
                failure = "Opposing lane operations must reject before leading-edge sampling"
                    + $": reason={mixed.Reason} reads={mixed.TerrainReads}";
                return false;
            }
            failure = string.Empty;
            return true;

            Dictionary<Tile2i, float> Terrain(
                float incoming,
                float leading)
            {
                var result = new Dictionary<Tile2i, float>();
                for (int lane = 0; lane < 2; lane++)
                {
                    Tile2i origin = state.GetLaneOrigin(lane);
                    for (int offset = 0; offset <= 4; offset++)
                    {
                        result[origin + new RelTile2i(0, offset)] = incoming;
                        result[origin + new RelTile2i(4, offset)] = leading;
                    }
                }
                return result;
            }

            void SetLaneLeading(
                IDictionary<Tile2i, float> values,
                AccessV2BandState bandState,
                int lane,
                float height,
                int offsets)
            {
                Tile2i origin = bandState.GetLaneOrigin(lane);
                for (int offset = 0; offset < offsets; offset++)
                    values[origin + new RelTile2i(4, offset)] = height;
            }

            void SetLaneIncoming(
                IDictionary<Tile2i, float> values,
                AccessV2BandState bandState,
                int lane,
                float height)
            {
                Tile2i origin = bandState.GetLaneOrigin(lane);
                for (int offset = 0; offset <= 4; offset++)
                    values[origin + new RelTile2i(0, offset)] = height;
            }
        }

        private static AccessV2TerminalMask Or(
            this AccessV2TerminalMask left,
            AccessV2TerminalMask right)
            => new AccessV2TerminalMask(
                left.Bits | right.Bits,
                Math.Max(left.Ranks, right.Ranks));
    }
}

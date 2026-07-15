using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal sealed class AccessV2HandoffCandidate
    {
        public Tile2i ExitDirection { get; }
        public int SpanLength { get; }
        public AccessHandoffOperation Lane0Operation { get; }
        public AccessHandoffOperation Lane1Operation { get; }
        public Tile2i Lane0Contact { get; }
        public Tile2i Lane1Contact { get; }
        public IReadOnlyList<Tile2i> Lane0TerminalOrigins { get; }
        public IReadOnlyList<Tile2i> Lane1TerminalOrigins { get; }
        public IReadOnlyList<Tile2i> EscapeCenters { get; }
        public IReadOnlyList<Tile2i> GroundEntryCenters { get; }
        public IReadOnlyCollection<string> CleanupKeys { get; }
        public float CleanupCost { get; }
        public bool IsQuickPath { get; }
        public float CenterSpokeCost { get; }
        public float TotalCost => CenterSpokeCost + CleanupCost;

        public AccessV2HandoffCandidate(
            Tile2i exitDirection,
            int spanLength,
            AccessGroundHandoff lane0,
            AccessGroundHandoff lane1,
            IReadOnlyList<Tile2i> lane0TerminalOrigins,
            IReadOnlyList<Tile2i> lane1TerminalOrigins,
            IReadOnlyList<Tile2i> escapeCenters,
            IReadOnlyList<Tile2i> groundEntryCenters,
            IReadOnlyCollection<string> cleanupKeys,
            float cleanupCost,
            bool isQuickPath = false,
            float centerSpokeCost = 2f)
        {
            ExitDirection = exitDirection;
            SpanLength = spanLength;
            Lane0Operation = lane0.Operation;
            Lane1Operation = lane1.Operation;
            Lane0Contact = lane0.Tile;
            Lane1Contact = lane1.Tile;
            Lane0TerminalOrigins = lane0TerminalOrigins;
            Lane1TerminalOrigins = lane1TerminalOrigins;
            EscapeCenters = escapeCenters;
            GroundEntryCenters = groundEntryCenters;
            CleanupKeys = cleanupKeys;
            CleanupCost = cleanupCost;
            IsQuickPath = isQuickPath;
            CenterSpokeCost = Math.Max(2f, centerSpokeCost);
        }

        public override string ToString()
            => $"exit=({ExitDirection.X},{ExitDirection.Y}) " +
                $"span={SpanLength} ops={Lane0Operation}/{Lane1Operation} " +
                $"contacts=({Lane0Contact.X},{Lane0Contact.Y})/" +
                $"({Lane1Contact.X},{Lane1Contact.Y}) " +
                $"escapeCenters={EscapeCenters.Count} " +
                $"entries={GroundEntryCenters.Count} " +
                $"quick={IsQuickPath}";
    }

    internal delegate IReadOnlyList<AccessGroundHandoff>
        AccessV2SingleLaneHandoffEvaluator(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i predecessorOrigin,
            AccessHeightProfile predecessorProfile);

    internal delegate IReadOnlyList<AccessGroundHandoff>
        AccessV2LaneSpanHandoffEvaluator(
            IReadOnlyList<AccessHandoffSpanCell> cells);

    internal static class AccessV2Handoffs
    {
        public const int MaxSpanLength = 3;

        public static IReadOnlyList<AccessV2HandoffCandidate> Evaluate(
            IReadOnlyList<AccessV2BandState> recentNewestFirst,
            AccessV2History history,
            AccessV2GroundGraph ground,
            AccessV2SingleLaneHandoffEvaluator singleEvaluator,
            AccessV2LaneSpanHandoffEvaluator spanEvaluator,
            float cleanupCostScale = 1f,
            Func<Tile2i, AccessV2History, bool>? projectedCenterValidator = null,
            Func<Tile2i, AccessV2History, bool>? projectedCenterOverlapsWork = null,
            int vehicleWidth = 0,
            float centerSpokeCost = 2f)
        {
            var result = new List<AccessV2HandoffCandidate>();
            if (recentNewestFirst.Count == 0) return result;
            AccessV2BandState current = recentNewestFirst[0];

            IReadOnlyList<AccessGroundHandoff> firstLane0 =
                EvaluateForwardLane(
                    recentNewestFirst, 1, 0,
                    singleEvaluator, spanEvaluator);
            IReadOnlyList<AccessGroundHandoff> firstLane1 =
                EvaluateForwardLane(
                    recentNewestFirst, 1, 1,
                    singleEvaluator, spanEvaluator);
            AccessV2HandoffCandidate? quick = TryBuildQuickForwardHandoff(
                current, history, ground,
                firstLane0, firstLane1, vehicleWidth,
                cleanupCostScale, projectedCenterValidator,
                centerSpokeCost);
            if (quick != null)
                return new[] { quick };

            int available = CountRecentStraightRows(recentNewestFirst);
            for (int span = 1;
                span <= Math.Min(MaxSpanLength, available);
                span++)
            {
                IReadOnlyList<AccessGroundHandoff> lane0 = span == 1
                    ? firstLane0
                    : EvaluateForwardLane(
                        recentNewestFirst, span, 0,
                        singleEvaluator, spanEvaluator);
                IReadOnlyList<AccessGroundHandoff> lane1 = span == 1
                    ? firstLane1
                    : EvaluateForwardLane(
                        recentNewestFirst, span, 1,
                        singleEvaluator, spanEvaluator);
                IReadOnlyList<Tile2i> lane0Origins = GetForwardOrigins(
                    recentNewestFirst, span, 0);
                IReadOnlyList<Tile2i> lane1Origins = GetForwardOrigins(
                    recentNewestFirst, span, 1);
                AddPairs(
                    current.EntryDirection, span,
                    lane0, lane1, lane0Origins, lane1Origins,
                    history, ground,
                    cleanupCostScale, projectedCenterValidator,
                    projectedCenterOverlapsWork,
                    centerSpokeCost, result);
            }

            if (available >= 2)
            {
                Tile2i transverse = AccessV2BandProfile.GetLaneDirection(
                    current.Axis);
                AddLateral(-1, laneIndex: 0);
                AddLateral(1, laneIndex: 1);

                void AddLateral(int sign, int laneIndex)
                {
                    AccessV2BandState newest = recentNewestFirst[0];
                    AccessV2BandState older = recentNewestFirst[1];
                    int innerLane = laneIndex == 0 ? 1 : 0;
                    IReadOnlyList<AccessGroundHandoff> first = singleEvaluator(
                        newest.GetLaneOrigin(laneIndex), newest.GetLane(laneIndex).Profile,
                        newest.GetLaneOrigin(innerLane), newest.GetLane(innerLane).Profile);
                    IReadOnlyList<AccessGroundHandoff> second = singleEvaluator(
                        older.GetLaneOrigin(laneIndex), older.GetLane(laneIndex).Profile,
                        older.GetLaneOrigin(innerLane), older.GetLane(innerLane).Profile);
                    AddPairs(
                        AccessV2Geometry.Scale(transverse, sign), 1,
                        first, second,
                        new[] { newest.GetLaneOrigin(laneIndex) },
                        new[] { older.GetLaneOrigin(laneIndex) },
                        history, ground,
                        cleanupCostScale, projectedCenterValidator,
                        projectedCenterOverlapsWork,
                        centerSpokeCost, result);
                }
            }

            return result
                .OrderBy(item => item.TotalCost)
                .ThenBy(item => item.SpanLength)
                .ThenBy(item => item.ExitDirection.X)
                .ThenBy(item => item.ExitDirection.Y)
                .ThenBy(item => item.Lane0Contact.X)
                .ThenBy(item => item.Lane0Contact.Y)
                .ToArray();
        }

        /// <summary>
        /// Accepts the common flat-ground seam from the already captured
        /// vehicle-center graph.  A green graph center already includes the
        /// resolved vehicle's complete vanilla mask and all snapshot blockers.
        /// Candidate-history rays are the only additional natural-ground
        /// blocker needed here.  The generated band behind the boundary is an
        /// accepted V surface, so only the outward half of the vehicle mask is
        /// checked against those disturbance rays.
        /// </summary>
        private static AccessV2HandoffCandidate? TryBuildQuickForwardHandoff(
            AccessV2BandState current,
            AccessV2History history,
            AccessV2GroundGraph ground,
            IReadOnlyList<AccessGroundHandoff> lane0,
            IReadOnlyList<AccessGroundHandoff> lane1,
            int vehicleWidth,
            float cleanupCostScale,
            Func<Tile2i, AccessV2History, bool>? projectedCenterValidator,
            float centerSpokeCost)
        {
            if (vehicleWidth <= 0 || lane0.Count == 0 || lane1.Count == 0)
                return null;
            if (!TrySelectForward(lane0, current, out AccessGroundHandoff left)
                || !TrySelectForward(
                    lane1, current, out AccessGroundHandoff right))
                return null;

            int radius = Math.Max(0, vehicleWidth / 2);
            int minOffset = radius;
            int maxOffset = 8 - radius;
            if (minOffset > maxOffset)
                return null;
            IReadOnlyCollection<Tile2i> rayTiles = history.CollectRayTiles();
            Tile2i? selected = null;
            for (int offset = minOffset; offset <= maxOffset; offset++)
            {
                Tile2i center = GetForwardCenter(current, offset);
                if (!ground.IsTraversable(center)
                    || (projectedCenterValidator != null
                        && !projectedCenterValidator(center, history))
                    || HasOutwardHistoryBlocker(
                        center, current, radius, history, rayTiles))
                    continue;
                selected = center;
                break;
            }
            if (!selected.HasValue)
                return null;
            if (!ground.TryValidateLocalEscape(
                    new[] { selected.Value }, history, cleanupCostScale,
                    out IReadOnlyCollection<string> cleanupKeys,
                    out float cleanupCost))
                return null;

            return new AccessV2HandoffCandidate(
                current.EntryDirection, 1,
                left, right,
                new[] { current.GetLaneOrigin(0) },
                new[] { current.GetLaneOrigin(1) },
                new[] { selected.Value },
                new[] { selected.Value },
                cleanupKeys, cleanupCost,
                isQuickPath: true,
                centerSpokeCost: centerSpokeCost);
        }

        private static bool TrySelectForward(
            IReadOnlyList<AccessGroundHandoff> candidates,
            AccessV2BandState state,
            out AccessGroundHandoff selected)
        {
            for (int index = 0; index < candidates.Count; index++)
                if (IsAheadOfBand(candidates[index].Tile, state))
                {
                    selected = candidates[index];
                    return true;
                }
            selected = default;
            return false;
        }

        private static bool IsAheadOfBand(
            Tile2i tile,
            AccessV2BandState state)
            => state.EntryDirection.X > 0
                ? tile.X >= state.Anchor.X + 4
                : state.EntryDirection.X < 0
                    ? tile.X < state.Anchor.X
                    : state.EntryDirection.Y > 0
                        ? tile.Y >= state.Anchor.Y + 4
                        : tile.Y < state.Anchor.Y;

        private static Tile2i GetForwardCenter(
            AccessV2BandState state,
            int transverseOffset)
            => state.Axis == AccessV2TravelAxis.X
                ? new Tile2i(
                    state.EntryDirection.X > 0
                        ? state.Anchor.X + 4
                        : state.Anchor.X - 1,
                    state.Anchor.Y + transverseOffset)
                : new Tile2i(
                    state.Anchor.X + transverseOffset,
                    state.EntryDirection.Y > 0
                        ? state.Anchor.Y + 4
                        : state.Anchor.Y - 1);

        private static bool HasOutwardHistoryBlocker(
            Tile2i center,
            AccessV2BandState state,
            int radius,
            AccessV2History history,
            IReadOnlyCollection<Tile2i> rayTiles)
        {
            var raySet = rayTiles as HashSet<Tile2i>
                ?? new HashSet<Tile2i>(rayTiles);
            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    Tile2i tile = center + new RelTile2i(x, y);
                    bool outward = state.EntryDirection.X > 0
                        ? tile.X >= state.Anchor.X + 4
                        : state.EntryDirection.X < 0
                            ? tile.X < state.Anchor.X
                            : state.EntryDirection.Y > 0
                                ? tile.Y >= state.Anchor.Y + 4
                                : tile.Y < state.Anchor.Y;
                    if (outward
                        && (raySet.Contains(tile)
                            || history.ContainsGeneratedTile(tile)))
                        return true;
                }
            return false;
        }

        private static IReadOnlyList<AccessGroundHandoff> EvaluateForwardLane(
            IReadOnlyList<AccessV2BandState> recentNewestFirst,
            int span,
            int lane,
            AccessV2SingleLaneHandoffEvaluator singleEvaluator,
            AccessV2LaneSpanHandoffEvaluator spanEvaluator)
        {
            AccessV2BandState newest = recentNewestFirst[0];
            if (span == 1)
            {
                Tile2i origin = newest.GetLaneOrigin(lane);
                AccessHeightProfile profile = newest.GetLane(lane).Profile;
                Tile2i predecessor = AccessV2Geometry.Add(
                    origin, AccessV2Geometry.Scale(newest.EntryDirection, -1));
                AccessHeightProfile predecessorProfile = recentNewestFirst.Count > 1
                    ? recentNewestFirst[1].GetLane(lane).Profile
                    : profile;
                return singleEvaluator(
                    origin, profile, predecessor, predecessorProfile);
            }

            var cells = new List<AccessHandoffSpanCell>(span);
            for (int index = span - 1; index >= 0; index--)
            {
                AccessV2BandState state = recentNewestFirst[index];
                cells.Add(new AccessHandoffSpanCell(
                    state.GetLaneOrigin(lane),
                    state.GetLane(lane).Profile,
                    state.EntryDirection));
            }
            return spanEvaluator(cells);
        }

        private static int CountRecentStraightRows(
            IReadOnlyList<AccessV2BandState> recentNewestFirst)
        {
            AccessV2BandState newest = recentNewestFirst[0];
            int count = 1;
            for (int index = 1;
                index < recentNewestFirst.Count && count < MaxSpanLength;
                index++)
            {
                AccessV2BandState newer = recentNewestFirst[index - 1];
                AccessV2BandState older = recentNewestFirst[index];
                if (older.Axis != newest.Axis
                    || older.EntryDirection != newest.EntryDirection
                    || newer.Anchor != AccessV2Geometry.Add(
                        older.Anchor, newest.EntryDirection))
                    break;
                count++;
            }
            return count;
        }

        private static IReadOnlyList<Tile2i> GetForwardOrigins(
            IReadOnlyList<AccessV2BandState> recentNewestFirst,
            int span,
            int lane)
        {
            var origins = new List<Tile2i>(span);
            for (int index = span - 1; index >= 0; index--)
                origins.Add(recentNewestFirst[index].GetLaneOrigin(lane));
            return origins;
        }

        private static void AddPairs(
            Tile2i exitDirection,
            int spanLength,
            IReadOnlyList<AccessGroundHandoff> lane0,
            IReadOnlyList<AccessGroundHandoff> lane1,
            IReadOnlyList<Tile2i> lane0TerminalOrigins,
            IReadOnlyList<Tile2i> lane1TerminalOrigins,
            AccessV2History history,
            AccessV2GroundGraph ground,
            float cleanupCostScale,
            Func<Tile2i, AccessV2History, bool>? projectedCenterValidator,
            Func<Tile2i, AccessV2History, bool>? projectedCenterOverlapsWork,
            float centerSpokeCost,
            ICollection<AccessV2HandoffCandidate> result)
        {
            for (int leftIndex = 0; leftIndex < lane0.Count; leftIndex++)
            {
                AccessGroundHandoff left = lane0[leftIndex];
                for (int rightIndex = 0; rightIndex < lane1.Count; rightIndex++)
                {
                    AccessGroundHandoff right = lane1[rightIndex];
                    if (!TryBuildCompleteEscape(
                            left, exitDirection, history, ground,
                            projectedCenterValidator,
                            projectedCenterOverlapsWork,
                            out IReadOnlyList<Tile2i> leftEscape)
                        || !TryBuildCompleteEscape(
                            right, exitDirection, history, ground,
                            projectedCenterValidator,
                            projectedCenterOverlapsWork,
                            out IReadOnlyList<Tile2i> rightEscape))
                        continue;
                    var centers = new HashSet<Tile2i>(leftEscape);
                    centers.UnionWith(rightEscape);
                    if (!ground.TryValidateLocalEscape(
                            centers, history, cleanupCostScale,
                            out IReadOnlyCollection<string> cleanupKeys,
                            out float cleanupCost))
                        continue;
                    result.Add(new AccessV2HandoffCandidate(
                        exitDirection, spanLength,
                        left, right,
                        lane0TerminalOrigins,
                        lane1TerminalOrigins,
                        centers.OrderBy(item => item.X)
                            .ThenBy(item => item.Y).ToArray(),
                        new[]
                        {
                            leftEscape[leftEscape.Count - 1],
                            rightEscape[rightEscape.Count - 1],
                        }.Distinct().ToArray(),
                        cleanupKeys, cleanupCost,
                        centerSpokeCost: centerSpokeCost));
                }
            }
        }

        private static bool TryBuildCompleteEscape(
            AccessGroundHandoff handoff,
            Tile2i exitDirection,
            AccessV2History history,
            AccessV2GroundGraph ground,
            Func<Tile2i, AccessV2History, bool>? projectedCenterValidator,
            Func<Tile2i, AccessV2History, bool>? projectedCenterOverlapsWork,
            out IReadOnlyList<Tile2i> escape)
        {
            const int maxAdditionalDistance = 4;
            var prefix = handoff.EscapeTiles.Count > 0
                ? new List<Tile2i>(handoff.EscapeTiles)
                : new List<Tile2i> { handoff.Tile };
            if (!prefix.Contains(handoff.Tile))
                prefix.Insert(0, handoff.Tile);
            for (int index = 0; index < prefix.Count; index++)
            {
                Tile2i center = prefix[index];
                if (!ground.IsTraversable(center)
                    || (projectedCenterValidator != null
                        && !projectedCenterValidator(center, history)))
                {
                    escape = Array.Empty<Tile2i>();
                    return false;
                }
            }

            Tile2i start = prefix[prefix.Count - 1];
            if (projectedCenterOverlapsWork == null
                || !projectedCenterOverlapsWork(start, history))
            {
                escape = prefix;
                return true;
            }

            RelTile2i continuation;
            if (prefix.Count >= 2)
            {
                Tile2i previous = prefix[prefix.Count - 2];
                continuation = new RelTile2i(
                    Math.Sign(start.X - previous.X),
                    Math.Sign(start.Y - previous.Y));
            }
            else
            {
                continuation = new RelTile2i(
                    Math.Sign(exitDirection.X),
                    Math.Sign(exitDirection.Y));
            }
            Tile2i current = start;
            for (int step = 0;
                step < maxAdditionalDistance
                    && projectedCenterOverlapsWork(current, history);
                step++)
            {
                Tile2i next = current + continuation;
                if (!ground.CanTraverse(current, next)
                    || (projectedCenterValidator != null
                        && !projectedCenterValidator(next, history)))
                {
                    escape = Array.Empty<Tile2i>();
                    return false;
                }
                prefix.Add(next);
                current = next;
            }
            if (projectedCenterOverlapsWork(current, history))
            {
                escape = Array.Empty<Tile2i>();
                return false;
            }
            escape = prefix;
            return true;
        }
    }
}

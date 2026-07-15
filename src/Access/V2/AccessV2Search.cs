using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal readonly struct AccessV2TransitionEvaluation
    {
        public bool IsValid { get; }
        public string RejectionReason { get; }
        public float TraversalCost { get; }
        public float GeneratedWorkCost { get; }
        public float DirectWorkCost { get; }
        public float GeneratedFixedCost { get; }
        public float ExteriorRayCost { get; }
        public float CleanupCost { get; }
        public IReadOnlyList<AccessRayHeightConstraint> RayConstraints { get; }
        public IReadOnlyCollection<string> CleanupKeys { get; }
        public float TotalCost => TraversalCost + GeneratedWorkCost + CleanupCost;

        public AccessV2TransitionEvaluation(
            bool isValid,
            string rejectionReason,
            float traversalCost,
            float generatedWorkCost,
            float cleanupCost,
            IReadOnlyList<AccessRayHeightConstraint>? rayConstraints = null,
            IReadOnlyCollection<string>? cleanupKeys = null,
            float directWorkCost = 0f,
            float generatedFixedCost = 0f,
            float exteriorRayCost = 0f)
        {
            IsValid = isValid;
            RejectionReason = rejectionReason ?? string.Empty;
            TraversalCost = traversalCost;
            GeneratedWorkCost = generatedWorkCost;
            DirectWorkCost = directWorkCost;
            GeneratedFixedCost = generatedFixedCost;
            ExteriorRayCost = exteriorRayCost;
            CleanupCost = cleanupCost;
            RayConstraints = rayConstraints ?? Array.Empty<AccessRayHeightConstraint>();
            CleanupKeys = cleanupKeys ?? Array.Empty<string>();
        }

        public static AccessV2TransitionEvaluation Reject(string reason)
            => new AccessV2TransitionEvaluation(
                false, reason, 0f, 0f, 0f);
    }

    internal delegate AccessV2TransitionEvaluation AccessV2TransitionEvaluator(
        AccessV2BandState? current,
        AccessV2Transition transition,
        AccessV2History history,
        Tile2i? connectedFixedOrigin);

    internal delegate IReadOnlyList<AccessV2HandoffCandidate>
        AccessV2HandoffEvaluator(
            IReadOnlyList<AccessV2BandState> recentNewestFirst,
            AccessV2History history);

    /// <summary>
    /// Returns an admissible remaining-cost estimate for a non-terminal V2
    /// band state. The accepted ground-goal heuristic uses the canonical band
    /// center; fixed-frontage searches deliberately leave this unset.
    /// </summary>
    internal delegate float AccessV2HeuristicEvaluator(
        AccessV2BandState state);

    internal sealed class AccessV2SearchResult
    {
        public bool Success { get; }
        public string FailureReason { get; }
        public IReadOnlyList<AccessV2BandState> States { get; }
        public IReadOnlyDictionary<Tile2i, AccessHeightProfile> GeneratedProfiles { get; }
        public float Cost { get; }
        public float TraversalCost { get; }
        public float GeneratedWorkCost { get; }
        public float DirectWorkCost { get; }
        public float GeneratedFixedCost { get; }
        public float ExteriorRayCost { get; }
        public float CleanupCost { get; }
        public AccessV2HandoffCandidate? Handoff { get; }
        public IReadOnlyList<Tile2i> GroundPath { get; }
        public int StraightTransitions { get; }
        public int StrafeTransitions { get; }
        public int TurnTransitions { get; }
        public int Visited { get; }
        public int Pending { get; }
        public int MaxHistoryOrigins { get; }
        public int MaxRayConstraints { get; }
        public IReadOnlyDictionary<string, int> Rejections { get; }
        public bool UsedAStar { get; }
        public int HandoffEvaluations { get; }
        public int QuickHandoffAccepts { get; }

        public AccessV2SearchResult(
            bool success,
            string failureReason,
            IReadOnlyList<AccessV2BandState> states,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> generatedProfiles,
            float cost,
            float traversalCost,
            float generatedWorkCost,
            float directWorkCost,
            float generatedFixedCost,
            float exteriorRayCost,
            float cleanupCost,
            AccessV2HandoffCandidate? handoff,
            IReadOnlyList<Tile2i> groundPath,
            int straightTransitions,
            int strafeTransitions,
            int turnTransitions,
            int visited,
            int pending,
            int maxHistoryOrigins,
            int maxRayConstraints,
            IReadOnlyDictionary<string, int> rejections,
            bool usedAStar,
            int handoffEvaluations,
            int quickHandoffAccepts)
        {
            Success = success;
            FailureReason = failureReason ?? string.Empty;
            States = states;
            GeneratedProfiles = generatedProfiles;
            Cost = cost;
            TraversalCost = traversalCost;
            GeneratedWorkCost = generatedWorkCost;
            DirectWorkCost = directWorkCost;
            GeneratedFixedCost = generatedFixedCost;
            ExteriorRayCost = exteriorRayCost;
            CleanupCost = cleanupCost;
            Handoff = handoff;
            GroundPath = groundPath;
            StraightTransitions = straightTransitions;
            StrafeTransitions = strafeTransitions;
            TurnTransitions = turnTransitions;
            Visited = visited;
            Pending = pending;
            MaxHistoryOrigins = maxHistoryOrigins;
            MaxRayConstraints = maxRayConstraints;
            Rejections = rejections;
            UsedAStar = usedAStar;
            HandoffEvaluations = handoffEvaluations;
            QuickHandoffAccepts = quickHandoffAccepts;
        }
    }

    internal sealed class AccessV2RouteData
    {
        public IReadOnlyList<AccessV2BandState> States { get; }
        public IReadOnlyDictionary<Tile2i, AccessHeightProfile> GeneratedProfiles { get; }
        public AccessV2HandoffCandidate? Handoff { get; }
        public IReadOnlyList<Tile2i> GroundPath { get; }

        public AccessV2RouteData(
            IReadOnlyList<AccessV2BandState> states,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> generatedProfiles,
            AccessV2HandoffCandidate? handoff,
            IReadOnlyList<Tile2i> groundPath)
        {
            States = states;
            GeneratedProfiles = generatedProfiles;
            Handoff = handoff;
            GroundPath = groundPath;
        }
    }

    internal sealed class AccessV2SearchSession
    {
        private readonly Tile2i m_boundsMin;
        private readonly Tile2i m_boundsMax;
        private readonly IReadOnlyList<AccessV2FixedFrontage> m_goals;
        private readonly AccessV2TransitionEvaluator m_evaluator;
        private readonly AccessV2HandoffEvaluator? m_handoffEvaluator;
        private readonly AccessV2HeuristicEvaluator? m_heuristicEvaluator;
        private readonly AccessV2GroundGraph? m_groundGraph;
        private readonly Func<Tile2i, AccessV2History, bool>? m_groundValidator;
        private readonly float m_cleanupCostScale;
        private readonly int m_maxVisited;
        private readonly float m_maxCost;
        private readonly SortedDictionary<float, Queue<SearchNode>> m_queue =
            new SortedDictionary<float, Queue<SearchNode>>();
        private readonly Dictionary<SearchKey, float> m_best =
            new Dictionary<SearchKey, float>();
        private readonly Dictionary<string, int> m_rejections =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private int m_queueCount;
        private int m_visited;
        private int m_maxHistoryOrigins;
        private int m_maxRayConstraints;
        private int m_handoffEvaluations;
        private int m_quickHandoffAccepts;

        public bool IsComplete { get; private set; }
        public int Visited => m_visited;
        public int Pending => m_queueCount;
        public AccessV2SearchResult Result { get; private set; }

        public AccessV2SearchSession(
            AccessV2EndpointSet endpoints,
            Tile2i boundsMin,
            Tile2i boundsMax,
            AccessV2TransitionEvaluator evaluator,
            int maxVisited,
            float maxCost,
            AccessV2HandoffEvaluator? handoffEvaluator = null,
            AccessV2HeuristicEvaluator? heuristicEvaluator = null,
            AccessV2GroundGraph? groundGraph = null,
            Func<Tile2i, AccessV2History, bool>? groundValidator = null,
            float cleanupCostScale = 1f)
        {
            m_boundsMin = boundsMin;
            m_boundsMax = boundsMax;
            m_goals = endpoints.FixedGoals;
            m_evaluator = evaluator;
            m_handoffEvaluator = handoffEvaluator;
            m_heuristicEvaluator = heuristicEvaluator;
            m_groundGraph = groundGraph;
            m_groundValidator = groundValidator;
            m_cleanupCostScale = cleanupCostScale;
            m_maxVisited = Math.Max(1, maxVisited);
            m_maxCost = maxCost;
            Result = Failed("SearchNotComplete");

            if (endpoints.Starts.Count == 0)
            {
                CompleteFailure("NoWidth2StartCompanion");
                return;
            }
            if (m_goals.Count == 0 && m_handoffEvaluator == null)
            {
                CompleteFailure("V2NoFixedFrontageGoal");
                return;
            }

            foreach (AccessV2StartFrontage start in endpoints.Starts)
                AddStart(start);
            if (m_queueCount == 0)
                CompleteFailure("V2NoFeasibleStart");
        }

        public int Step(int maxVisitedThisStep)
        {
            if (IsComplete) return 0;
            int budget = Math.Max(1, maxVisitedThisStep);
            int visitedAtStart = m_visited;
            while (m_queueCount > 0
                && m_visited < m_maxVisited
                && m_visited - visitedAtStart < budget)
            {
                SearchNode current = Pop();
                var currentKey = new SearchKey(current);
                if (!m_best.TryGetValue(currentKey, out float best)
                    || current.Cost > best + 0.0001f)
                    continue;
                if (current.Cost > m_maxCost)
                {
                    CompleteFailure("CostLimitExceeded");
                    break;
                }

                m_visited++;
                if (current.GroundCenter.HasValue)
                {
                    if (m_groundGraph != null
                        && m_groundGraph.IsGoal(current.GroundCenter.Value))
                    {
                        CompleteSuccess(current);
                        break;
                    }
                    ExpandGround(current);
                    continue;
                }
                if (TryMatchGoal(current.State))
                {
                    CompleteSuccess(current);
                    break;
                }

                EnqueueHandoffGoals(current);
                Expand(current);
            }

            if (!IsComplete && m_queueCount == 0)
                CompleteFailure("NoPath");
            else if (!IsComplete && m_visited >= m_maxVisited)
                CompleteFailure("VisitedLimit");
            return m_visited - visitedAtStart;
        }

        private void AddStart(AccessV2StartFrontage start)
        {
            AccessV2History history = AccessV2History.Empty;
            float cost = 0f;
            float traversalCost = 0f;
            float generatedWorkCost = 0f;
            float directWorkCost = 0f;
            float generatedFixedCost = 0f;
            float exteriorRayCost = 0f;
            float cleanupCost = 0f;
            if (start.HasSyntheticCompanion)
            {
                Tile2i synthetic = start.SyntheticCompanionOrigin.GetValueOrDefault();
                int lane = start.State.GetLaneOrigin(0) == synthetic ? 0 : 1;
                var transition = new AccessV2Transition(
                    AccessV2TransitionKind.Strafe,
                    start.State,
                    new[] { start.State.GetLane(lane) },
                    new[] { start.FixedSeedOrigin });
                if (!history.TryApply(
                        transition, out _, out string geometryReason))
                {
                    Reject(geometryReason);
                    return;
                }
                AccessV2TransitionEvaluation evaluation = m_evaluator(
                    null, transition, history, start.FixedSeedOrigin);
                if (!evaluation.IsValid)
                {
                    Reject(evaluation.RejectionReason);
                    return;
                }
                if (!history.TryApply(
                        transition.Delta,
                        transition.LocalContextOrigins,
                        evaluation.RayConstraints,
                        evaluation.CleanupKeys,
                        out history,
                        out geometryReason))
                {
                    Reject(geometryReason);
                    return;
                }
                cost = evaluation.TotalCost;
                traversalCost = evaluation.TraversalCost;
                generatedWorkCost = evaluation.GeneratedWorkCost;
                directWorkCost = evaluation.DirectWorkCost;
                generatedFixedCost = evaluation.GeneratedFixedCost;
                exteriorRayCost = evaluation.ExteriorRayCost;
                cleanupCost = evaluation.CleanupCost;
            }

            var node = new SearchNode(
                start.State, history, cost,
                traversalCost,
                generatedWorkCost,
                directWorkCost,
                generatedFixedCost,
                exteriorRayCost,
                cleanupCost,
                null, null, null);
            Enqueue(node);
        }

        private void Expand(SearchNode current)
        {
            foreach (AccessV2Transition transition in
                AccessV2Geometry.EnumerateStraight(current.State))
                TryRelax(current, transition);

            for (int sign = -1; sign <= 1; sign += 2)
            {
                if (AccessV2Geometry.TryStrafe(
                        current.State, sign,
                        out AccessV2Transition strafe, out string strafeReason))
                    TryRelax(current, strafe);
                else
                    Reject(strafeReason);

                if (current.Parent != null)
                {
                    if (AccessV2Geometry.TryTurn(
                            current.Parent.State, current.State, sign,
                            out AccessV2Transition turn, out string turnReason))
                        TryRelax(current, turn);
                    else
                        Reject(turnReason);
                }
            }
        }

        private void TryRelax(
            SearchNode current,
            AccessV2Transition transition)
        {
            if (!AccessV2Geometry.IsInsideBounds(
                    transition.Next, m_boundsMin, m_boundsMax))
            {
                Reject("HorizontalBounds");
                return;
            }
            if (!current.History.TryApply(
                    transition, out _, out string historyReason))
            {
                Reject(historyReason);
                return;
            }

            AccessV2TransitionEvaluation evaluation = m_evaluator(
                current.State, transition, current.History, null);
            if (!evaluation.IsValid)
            {
                Reject(evaluation.RejectionReason);
                return;
            }
            if (!current.History.TryApply(
                    transition.Delta,
                    transition.LocalContextOrigins,
                    evaluation.RayConstraints,
                    evaluation.CleanupKeys,
                    out AccessV2History nextHistory,
                    out historyReason))
            {
                Reject(historyReason);
                return;
            }

            float nextCost = current.Cost + evaluation.TotalCost;
            if (nextCost > m_maxCost)
            {
                Reject("CostLimitExceeded");
                return;
            }
            var next = new SearchNode(
                transition.Next, nextHistory, nextCost,
                current.TraversalCost + evaluation.TraversalCost,
                current.GeneratedWorkCost + evaluation.GeneratedWorkCost,
                current.DirectWorkCost + evaluation.DirectWorkCost,
                current.GeneratedFixedCost + evaluation.GeneratedFixedCost,
                current.ExteriorRayCost + evaluation.ExteriorRayCost,
                current.CleanupCost + evaluation.CleanupCost,
                current, transition, null);
            Enqueue(next);
        }

        private void EnqueueHandoffGoals(SearchNode current)
        {
            if (m_handoffEvaluator == null) return;
            var recent = new List<AccessV2BandState>(
                AccessV2Handoffs.MaxSpanLength);
            for (SearchNode? node = current;
                node != null && recent.Count < AccessV2Handoffs.MaxSpanLength;
                node = node.Parent)
            {
                if (node.Handoff != null) continue;
                recent.Add(node.State);
            }
            IReadOnlyList<AccessV2HandoffCandidate> candidates =
                m_handoffEvaluator(recent, current.History);
            m_handoffEvaluations++;
            if (candidates.Any(item => item.IsQuickPath))
                m_quickHandoffAccepts++;
            for (int index = 0; index < candidates.Count; index++)
            {
                AccessV2HandoffCandidate handoff = candidates[index];
                float cost = current.Cost + handoff.TotalCost;
                if (cost > m_maxCost) continue;
                AccessV2History handoffHistory =
                    current.History.ApplyCleanupKeys(handoff.CleanupKeys);
                for (int entryIndex = 0;
                    entryIndex < handoff.GroundEntryCenters.Count;
                    entryIndex++)
                {
                    Tile2i entry = handoff.GroundEntryCenters[entryIndex];
                    Enqueue(new SearchNode(
                        current.State, handoffHistory, cost,
                        current.TraversalCost + handoff.CenterSpokeCost,
                        current.GeneratedWorkCost,
                        current.DirectWorkCost,
                        current.GeneratedFixedCost,
                        current.ExteriorRayCost,
                        current.CleanupCost + handoff.CleanupCost,
                        current, null, handoff, entry));
                }
            }
        }

        private void ExpandGround(SearchNode current)
        {
            if (m_groundGraph == null || !current.GroundCenter.HasValue)
                return;
            Tile2i from = current.GroundCenter.Value;
            RelTile2i[] directions =
            {
                new RelTile2i(1, 0), new RelTile2i(-1, 0),
                new RelTile2i(0, 1), new RelTile2i(0, -1),
            };
            for (int index = 0; index < directions.Length; index++)
            {
                Tile2i next = from + directions[index];
                if (!m_groundGraph.CanTraverse(from, next)
                    || (m_groundValidator != null
                        && !m_groundValidator(next, current.History)))
                    continue;
                if (!m_groundGraph.TryValidateLocalEscape(
                        new[] { next }, current.History,
                        m_cleanupCostScale,
                        out IReadOnlyCollection<string> cleanupKeys,
                        out float cleanupCost))
                    continue;
                AccessV2History nextHistory =
                    current.History.ApplyCleanupKeys(cleanupKeys);
                float nextCost = current.Cost + 1f + cleanupCost;
                if (nextCost > m_maxCost) continue;
                Enqueue(new SearchNode(
                    current.State, nextHistory, nextCost,
                    current.TraversalCost + 1f,
                    current.GeneratedWorkCost,
                    current.DirectWorkCost,
                    current.GeneratedFixedCost,
                    current.ExteriorRayCost,
                    current.CleanupCost + cleanupCost,
                    current, null, current.Handoff, next));
            }
        }

        private void Enqueue(SearchNode node)
        {
            var key = new SearchKey(node);
            if (m_best.TryGetValue(key, out float old)
                && old <= node.Cost + 0.0001f)
                return;
            m_best[key] = node.Cost;
            float heuristic = 0f;
            if (m_heuristicEvaluator != null)
            {
                if (node.GroundCenter.HasValue && m_groundGraph != null
                    && m_groundGraph.TryGetGoalDistance(
                        node.GroundCenter.Value, out int groundDistance))
                    heuristic = groundDistance;
                else if (!node.GroundCenter.HasValue)
                    heuristic = Math.Max(0f, m_heuristicEvaluator(node.State));
            }
            float queuePriority = node.Cost + heuristic;
            if (!m_queue.TryGetValue(
                    queuePriority, out Queue<SearchNode> bucket))
            {
                bucket = new Queue<SearchNode>();
                m_queue.Add(queuePriority, bucket);
            }
            bucket.Enqueue(node);
            m_queueCount++;
            m_maxHistoryOrigins = Math.Max(
                m_maxHistoryOrigins, node.History.OriginCount);
            m_maxRayConstraints = Math.Max(
                m_maxRayConstraints, node.History.RayConstraintCount);
        }

        private SearchNode Pop()
        {
            KeyValuePair<float, Queue<SearchNode>> first = m_queue.First();
            SearchNode node = first.Value.Dequeue();
            if (first.Value.Count == 0) m_queue.Remove(first.Key);
            m_queueCount--;
            return node;
        }

        private bool TryMatchGoal(AccessV2BandState state)
        {
            for (int index = 0; index < m_goals.Count; index++)
            {
                AccessV2FixedFrontage goal = m_goals[index];
                if (state.Axis != goal.State.Axis
                    || state.EntryDirection != goal.State.EntryDirection
                    || state.Anchor != AccessV2Geometry.Add(
                        goal.State.Anchor, goal.ExposedDirection))
                    continue;
                bool lane0 = AccessPathSearch.EdgesMatch(
                    state.Band.Lane0, goal.State.Band.Lane0,
                    state.EntryDirection);
                bool lane1 = AccessPathSearch.EdgesMatch(
                    state.Band.Lane1, goal.State.Band.Lane1,
                    state.EntryDirection);
                if (lane0 && lane1) return true;
            }
            return false;
        }

        private void CompleteSuccess(SearchNode goal)
        {
            var reverse = new List<AccessV2BandState>();
            int straight = 0, strafe = 0, turn = 0;
            var groundReverse = new List<Tile2i>();
            SearchNode? routeGoal = goal;
            while (routeGoal != null && routeGoal.GroundCenter.HasValue)
            {
                groundReverse.Add(routeGoal.GroundCenter.Value);
                routeGoal = routeGoal.Parent;
            }
            for (SearchNode? node = routeGoal; node != null; node = node.Parent)
            {
                reverse.Add(node.State);
                if (node.Transition?.Kind == AccessV2TransitionKind.Straight)
                    straight++;
                else if (node.Transition?.Kind == AccessV2TransitionKind.Strafe)
                    strafe++;
                else if (node.Transition?.Kind == AccessV2TransitionKind.Turn)
                    turn++;
            }
            reverse.Reverse();
            groundReverse.Reverse();
            Result = new AccessV2SearchResult(
                true, string.Empty, reverse,
                goal.History.Flatten(), goal.Cost,
                goal.TraversalCost,
                goal.GeneratedWorkCost,
                goal.DirectWorkCost,
                goal.GeneratedFixedCost,
                goal.ExteriorRayCost,
                goal.CleanupCost,
                goal.Handoff,
                groundReverse,
                straight, strafe, turn,
                m_visited, m_queueCount,
                m_maxHistoryOrigins, m_maxRayConstraints,
                new Dictionary<string, int>(m_rejections),
                m_heuristicEvaluator != null,
                m_handoffEvaluations,
                m_quickHandoffAccepts);
            IsComplete = true;
        }

        private void CompleteFailure(string reason)
        {
            Result = Failed(reason);
            IsComplete = true;
        }

        private AccessV2SearchResult Failed(string reason)
            => new AccessV2SearchResult(
                false, reason,
                Array.Empty<AccessV2BandState>(),
                new Dictionary<Tile2i, AccessHeightProfile>(),
                0f, 0f, 0f, 0f, 0f, 0f, 0f, null,
                Array.Empty<Tile2i>(),
                0, 0, 0,
                m_visited, m_queueCount,
                m_maxHistoryOrigins, m_maxRayConstraints,
                new Dictionary<string, int>(m_rejections),
                m_heuristicEvaluator != null,
                m_handoffEvaluations,
                m_quickHandoffAccepts);

        private void Reject(string reason)
        {
            string key = string.IsNullOrEmpty(reason) ? "Unknown" : reason;
            m_rejections.TryGetValue(key, out int count);
            m_rejections[key] = count + 1;
        }

        private sealed class SearchNode
        {
            public AccessV2BandState State { get; }
            public AccessV2History History { get; }
            public float Cost { get; }
            public float TraversalCost { get; }
            public float GeneratedWorkCost { get; }
            public float DirectWorkCost { get; }
            public float GeneratedFixedCost { get; }
            public float ExteriorRayCost { get; }
            public float CleanupCost { get; }
            public SearchNode? Parent { get; }
            public AccessV2Transition? Transition { get; }
            public AccessV2HandoffCandidate? Handoff { get; }
            public Tile2i? GroundCenter { get; }

            public SearchNode(
                AccessV2BandState state,
                AccessV2History history,
                float cost,
                float traversalCost,
                float generatedWorkCost,
                float directWorkCost,
                float generatedFixedCost,
                float exteriorRayCost,
                float cleanupCost,
                SearchNode? parent,
                AccessV2Transition? transition,
                AccessV2HandoffCandidate? handoff,
                Tile2i? groundCenter = null)
            {
                State = state;
                History = history;
                Cost = cost;
                TraversalCost = traversalCost;
                GeneratedWorkCost = generatedWorkCost;
                DirectWorkCost = directWorkCost;
                GeneratedFixedCost = generatedFixedCost;
                ExteriorRayCost = exteriorRayCost;
                CleanupCost = cleanupCost;
                Parent = parent;
                Transition = transition;
                Handoff = handoff;
                GroundCenter = groundCenter;
            }
        }

        private readonly struct SearchKey : IEquatable<SearchKey>
        {
            private readonly AccessV2BandState m_state;
            private readonly int m_historySignature;
            private readonly int m_originCount;
            private readonly int m_rayCount;
            private readonly int m_cleanupCount;
            private readonly Tile2i? m_groundCenter;

            public SearchKey(AccessV2BandState state, AccessV2History history)
            {
                m_state = state;
                m_historySignature = history.Signature;
                m_originCount = history.OriginCount;
                m_rayCount = history.RayConstraintCount;
                m_cleanupCount = history.CleanupKeyCount;
                m_groundCenter = null;
            }

            public SearchKey(SearchNode node)
                : this(node.State, node.History)
            {
                m_groundCenter = node.GroundCenter;
            }

            public bool Equals(SearchKey other)
                => m_state.Equals(other.m_state)
                    && m_historySignature == other.m_historySignature
                    && m_originCount == other.m_originCount
                    && m_rayCount == other.m_rayCount
                    && m_cleanupCount == other.m_cleanupCount
                    && m_groundCenter == other.m_groundCenter;

            public override bool Equals(object? obj)
                => obj is SearchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = m_state.GetHashCode();
                    hash = (hash * 397) ^ m_historySignature;
                    hash = (hash * 397) ^ m_originCount;
                    hash = (hash * 397) ^ m_rayCount;
                    hash = (hash * 397) ^ m_cleanupCount;
                    hash = (hash * 397) ^ m_groundCenter.GetHashCode();
                    return hash;
                }
            }
        }
    }
}

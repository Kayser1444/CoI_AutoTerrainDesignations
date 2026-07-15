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

    internal sealed class AccessV2RouteStep
    {
        public AccessV2BandState State { get; }
        public AccessV2Transition? Transition { get; }
        public AccessV2HandoffCandidate? Handoff { get; }
        public Tile2i? GroundCenter { get; }

        public bool IsGround => GroundCenter.HasValue;

        public AccessV2RouteStep(
            AccessV2BandState state,
            AccessV2Transition? transition,
            AccessV2HandoffCandidate? handoff,
            Tile2i? groundCenter)
        {
            State = state;
            Transition = transition;
            Handoff = handoff;
            GroundCenter = groundCenter;
        }
    }

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
        public IReadOnlyList<AccessV2RouteStep> RouteSteps { get; }
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
            IReadOnlyList<AccessV2RouteStep> routeSteps,
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
            RouteSteps = routeSteps;
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
        public IReadOnlyList<AccessV2RouteStep> RouteSteps { get; }

        public AccessV2RouteData(
            IReadOnlyList<AccessV2BandState> states,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> generatedProfiles,
            AccessV2HandoffCandidate? handoff,
            IReadOnlyList<Tile2i> groundPath,
            IReadOnlyList<AccessV2RouteStep>? routeSteps = null)
        {
            States = states;
            GeneratedProfiles = generatedProfiles;
            Handoff = handoff;
            GroundPath = groundPath;
            RouteSteps = routeSteps ?? Array.Empty<AccessV2RouteStep>();
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
        private readonly AccessV2PotentialField? m_potentialField;
        private readonly AccessV2GroundEscapePotentialField?
            m_groundEscapePotentialField;
        private readonly Func<Tile2i, int?>? m_groundHeightProvider;
        private readonly Func<Tile2i, int>? m_terrainCenterHeightProvider;
        private readonly AccessV2GroundGraph? m_groundGraph;
        private readonly Func<Tile2i, AccessV2History, bool>? m_groundValidator;
        private readonly float m_cleanupCostScale;
        private readonly int m_maxVisited;
        private readonly float m_maxCost;
        private readonly SortedDictionary<SearchPriority, Queue<SearchNode>> m_queue =
            new SortedDictionary<SearchPriority, Queue<SearchNode>>();
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
        // Diagnostic-only hook used by the experimental search overlay.
        internal Action<Tile2i, int, bool, int?>? NodeExplored { get; set; }
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
            float cleanupCostScale = 1f,
            AccessV2PotentialField? potentialField = null,
            Func<Tile2i, int?>? groundHeightProvider = null,
            Func<Tile2i, int>? terrainCenterHeightProvider = null,
            float groundToVMinimumGeneratedCost = 0f)
        {
            m_boundsMin = boundsMin;
            m_boundsMax = boundsMax;
            m_goals = endpoints.FixedGoals;
            m_evaluator = evaluator;
            m_handoffEvaluator = handoffEvaluator;
            m_heuristicEvaluator = heuristicEvaluator;
            m_potentialField = potentialField;
            m_groundHeightProvider = groundHeightProvider;
            m_terrainCenterHeightProvider = terrainCenterHeightProvider;
            m_groundGraph = groundGraph;
            m_groundEscapePotentialField = potentialField != null
                && groundGraph != null
                    ? new AccessV2GroundEscapePotentialField(
                        groundGraph, potentialField,
                        groundToVMinimumGeneratedCost)
                    : null;
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
                Tile2i exploredCenter = current.GroundCenter
                    ?? AccessV2PotentialField.GetCanonicalCenter(current.State);
                int exploredHeight2 = current.GroundCenter.HasValue
                    ? m_groundHeightProvider?.Invoke(exploredCenter) ?? 0
                    : current.State.Band.Lane0.Center2;
                NodeExplored?.Invoke(
                    exploredCenter,
                    exploredHeight2,
                    current.GroundCenter.HasValue,
                    m_groundHeightProvider?.Invoke(exploredCenter));
                if (current.IsFixedGoalTerminal)
                {
                    CompleteSuccess(current);
                    break;
                }
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
                if (TryMatchGoal(current.State, out AccessV2FixedFrontage? fixedGoal))
                    EnqueueFixedGoal(current, fixedGoal!);

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
            // A freshly entered V band has just proved this same seam in the
            // opposite direction. Returning immediately is strictly dominated.
            if (current.Parent?.GroundCenter.HasValue == true) return;
            var recent = new List<AccessV2BandState>(
                AccessV2Handoffs.MaxSpanLength);
            for (SearchNode? node = current;
                node != null && recent.Count < AccessV2Handoffs.MaxSpanLength;
                node = node.Parent)
            {
                if (node.GroundCenter.HasValue) break;
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
                new RelTile2i(1, 1), new RelTile2i(1, -1),
                new RelTile2i(-1, 1), new RelTile2i(-1, -1),
            };
            int incomingX = 0;
            int incomingY = 0;
            if (current.Parent?.GroundCenter is Tile2i previousCenter)
            {
                incomingX = Math.Sign(from.X - previousCenter.X);
                incomingY = Math.Sign(from.Y - previousCenter.Y);
            }
            for (int index = 0; index < directions.Length; index++)
            {
                if (incomingX * directions[index].X
                    + incomingY * directions[index].Y < 0)
                    continue;
                Tile2i next = from + directions[index];
                if (!m_groundGraph.CanTraverse(from, next))
                    continue;
                IReadOnlyList<Tile2i> sweptCenters =
                    AccessV2GroundGraph.GetSweptCenters(from, next);
                if (m_groundValidator != null
                    && sweptCenters.Any(center =>
                        !m_groundValidator(center, current.History)))
                    continue;
                if (!m_groundGraph.TryValidateLocalEscape(
                        sweptCenters, current.History,
                        m_cleanupCostScale,
                        out IReadOnlyCollection<string> cleanupKeys,
                        out float cleanupCost))
                    continue;
                AccessV2History nextHistory =
                    current.History.ApplyCleanupKeys(cleanupKeys);
                float stepCost = AccessV2GroundGraph.GetStepCost(from, next);
                float nextCost = current.Cost + stepCost + cleanupCost;
                if (nextCost > m_maxCost) continue;
                Enqueue(new SearchNode(
                    current.State, nextHistory, nextCost,
                    current.TraversalCost + stepCost,
                    current.GeneratedWorkCost,
                    current.DirectWorkCost,
                    current.GeneratedFixedCost,
                    current.ExteriorRayCost,
                    current.CleanupCost + cleanupCost,
                    current, null, null, next));
            }

            ExpandGroundToV(current);
        }

        private void ExpandGroundToV(SearchNode current)
        {
            if (m_groundGraph == null
                || m_handoffEvaluator == null
                || m_terrainCenterHeightProvider == null
                || !current.GroundCenter.HasValue)
                return;

            Tile2i ground = current.GroundCenter.Value;
            Tile2i[] travelDirections =
            {
                new Tile2i(4, 0), new Tile2i(-4, 0),
                new Tile2i(0, 4), new Tile2i(0, -4),
            };
            var emitted = new HashSet<AccessV2BandState>();
            for (int directionIndex = 0;
                directionIndex < travelDirections.Length;
                directionIndex++)
            {
                Tile2i travel = travelDirections[directionIndex];
                AccessV2TravelAxis axis = travel.X != 0
                    ? AccessV2TravelAxis.X
                    : AccessV2TravelAxis.Y;
                foreach (Tile2i anchor in CandidateBandAnchors(ground, axis))
                {
                    int centerDistance = Manhattan(
                        ground,
                        axis == AccessV2TravelAxis.X
                            ? anchor + new RelTile2i(2, 4)
                            : anchor + new RelTile2i(4, 2));
                    if (centerDistance > 2) continue;

                    int baseHeight2 = m_terrainCenterHeightProvider(anchor);
                    AccessSearchMode positive = axis == AccessV2TravelAxis.X
                        ? AccessSearchMode.XPositive
                        : AccessSearchMode.YPositive;
                    AccessSearchMode negative = axis == AccessV2TravelAxis.X
                        ? AccessSearchMode.XNegative
                        : AccessSearchMode.YNegative;
                    AccessSearchMode[] modes =
                    {
                        AccessSearchMode.Flat, positive, negative,
                    };
                    for (int modeIndex = 0; modeIndex < modes.Length; modeIndex++)
                    for (int delta = -3; delta <= 3; delta++)
                    {
                        if (!AccessHeightProfile.TryForMode(
                                modes[modeIndex], baseHeight2 + delta,
                                out AccessHeightProfile profile)
                            || !AccessV2BandProfile.TryCreateEnabled(
                                axis, profile, profile,
                                out AccessV2BandProfile band, out _))
                            continue;
                        var state = new AccessV2BandState(anchor, band, travel);
                        if (!emitted.Add(state)
                            || !AccessV2Geometry.IsInsideBounds(
                                state, m_boundsMin, m_boundsMax))
                            continue;

                        var transition = new AccessV2Transition(
                            AccessV2TransitionKind.Straight,
                            state,
                            new[] { state.GetLane(0), state.GetLane(1) },
                            Array.Empty<Tile2i>());
                        if (!current.History.TryApply(
                                transition, out _, out string historyReason))
                        {
                            Reject(historyReason);
                            continue;
                        }
                        AccessV2TransitionEvaluation evaluation = m_evaluator(
                            null, transition, current.History, null);
                        if (!evaluation.IsValid)
                        {
                            Reject(evaluation.RejectionReason);
                            continue;
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
                            continue;
                        }

                        // The same physical band is viewed toward the G side
                        // solely for symmetric seam validation. Search travel
                        // continues in 'travel', away from the component.
                        var reverseState = new AccessV2BandState(
                            anchor, band,
                            new Tile2i(-travel.X, -travel.Y));
                        IReadOnlyList<AccessV2HandoffCandidate> seams =
                            m_handoffEvaluator(
                                new[] { reverseState }, nextHistory);
                        m_handoffEvaluations++;
                        if (seams.Any(item => item.IsQuickPath))
                            m_quickHandoffAccepts++;
                        AccessV2HandoffCandidate? seam = seams
                            .Where(candidate =>
                                candidate.GroundEntryCenters.Contains(ground))
                            .OrderBy(candidate => candidate.TotalCost)
                            .FirstOrDefault();
                        if (seam == null) continue;

                        float nextCost = current.Cost
                            + evaluation.TotalCost + seam.TotalCost;
                        if (nextCost > m_maxCost) continue;
                        nextHistory = nextHistory.ApplyCleanupKeys(
                            seam.CleanupKeys);
                        Enqueue(new SearchNode(
                            state, nextHistory, nextCost,
                            current.TraversalCost
                                + evaluation.TraversalCost
                                + seam.CenterSpokeCost,
                            current.GeneratedWorkCost
                                + evaluation.GeneratedWorkCost,
                            current.DirectWorkCost + evaluation.DirectWorkCost,
                            current.GeneratedFixedCost
                                + evaluation.GeneratedFixedCost,
                            current.ExteriorRayCost + evaluation.ExteriorRayCost,
                            current.CleanupCost
                                + evaluation.CleanupCost + seam.CleanupCost,
                            current, transition, seam));
                    }
                }
            }
        }

        private static IEnumerable<Tile2i> CandidateBandAnchors(
            Tile2i ground,
            AccessV2TravelAxis axis)
        {
            int baseX = ground.X & -4;
            int baseY = ground.Y & -4;
            var emitted = new HashSet<Tile2i>();
            for (int dx = -4; dx <= 0; dx += 4)
            for (int dy = -4; dy <= 0; dy += 4)
            {
                Tile2i origin = new Tile2i(baseX + dx, baseY + dy);
                Tile2i lane = AccessV2BandProfile.GetLaneDirection(axis);
                Tile2i[] anchors =
                {
                    origin,
                    new Tile2i(origin.X - lane.X, origin.Y - lane.Y),
                };
                for (int index = 0; index < anchors.Length; index++)
                    if (emitted.Add(anchors[index]))
                        yield return anchors[index];
            }
        }

        private static int Manhattan(Tile2i left, Tile2i right)
            => Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);

        private void Enqueue(SearchNode node)
        {
            var key = new SearchKey(node);
            if (m_best.TryGetValue(key, out float old)
                && old <= node.Cost + 0.0001f)
                return;
            m_best[key] = node.Cost;
            float heuristic = 0f;
            if (m_potentialField != null || m_heuristicEvaluator != null)
            {
                if (node.GroundCenter.HasValue && m_groundGraph != null
                    && m_groundGraph.TryGetGoalDistance(
                        node.GroundCenter.Value, out float groundDistance))
                    heuristic = groundDistance;
                else if (node.GroundCenter.HasValue
                    && m_groundEscapePotentialField != null)
                    heuristic = Math.Max(0f,
                        m_groundEscapePotentialField.GetPotential(
                            node.GroundCenter.Value));
                else if (!node.GroundCenter.HasValue
                    && !node.IsFixedGoalTerminal)
                    heuristic = m_potentialField != null
                        ? Math.Max(0f, m_potentialField.GetPotential(node.State))
                        : Math.Max(0f, m_heuristicEvaluator!(node.State));
            }
            var queuePriority = new SearchPriority(
                node.Cost + heuristic, heuristic);
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
            KeyValuePair<SearchPriority, Queue<SearchNode>> first = m_queue.First();
            SearchNode node = first.Value.Dequeue();
            if (first.Value.Count == 0) m_queue.Remove(first.Key);
            m_queueCount--;
            return node;
        }

        private bool TryMatchGoal(
            AccessV2BandState state,
            out AccessV2FixedFrontage? matched)
        {
            matched = null;
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
                if (lane0 && lane1
                    && (matched == null
                        || goal.TerminalCost < matched.TerminalCost))
                    matched = goal;
            }
            return matched != null;
        }

        private void EnqueueFixedGoal(
            SearchNode current,
            AccessV2FixedFrontage goal)
        {
            float cost = current.Cost + goal.TerminalCost;
            if (cost > m_maxCost) return;
            Enqueue(new SearchNode(
                current.State, current.History, cost,
                current.TraversalCost + goal.TerminalCost,
                current.GeneratedWorkCost,
                current.DirectWorkCost,
                current.GeneratedFixedCost,
                current.ExteriorRayCost,
                current.CleanupCost,
                current, null, null,
                groundCenter: null,
                isFixedGoalTerminal: true));
        }

        private void CompleteSuccess(SearchNode goal)
        {
            var reverse = new List<AccessV2BandState>();
            int straight = 0, strafe = 0, turn = 0;
            var groundReverse = new List<Tile2i>();
            var stepReverse = new List<AccessV2RouteStep>();
            SearchNode? routeGoal = goal.IsFixedGoalTerminal
                ? goal.Parent
                : goal;
            for (SearchNode? node = routeGoal; node != null; node = node.Parent)
            {
                stepReverse.Add(new AccessV2RouteStep(
                    node.State, node.Transition,
                    node.Handoff, node.GroundCenter));
                if (node.GroundCenter.HasValue)
                    groundReverse.Add(node.GroundCenter.Value);
                else
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
            stepReverse.Reverse();
            AccessV2HandoffCandidate? terminalHandoff = stepReverse
                .Where(step => step.IsGround && step.Handoff != null)
                .Select(step => step.Handoff)
                .LastOrDefault();
            Result = new AccessV2SearchResult(
                true, string.Empty, reverse,
                goal.History.Flatten(), goal.Cost,
                goal.TraversalCost,
                goal.GeneratedWorkCost,
                goal.DirectWorkCost,
                goal.GeneratedFixedCost,
                goal.ExteriorRayCost,
                goal.CleanupCost,
                terminalHandoff,
                groundReverse,
                stepReverse,
                straight, strafe, turn,
                m_visited, m_queueCount,
                m_maxHistoryOrigins, m_maxRayConstraints,
                new Dictionary<string, int>(m_rejections),
                m_potentialField != null || m_heuristicEvaluator != null,
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
                Array.Empty<AccessV2RouteStep>(),
                0, 0, 0,
                m_visited, m_queueCount,
                m_maxHistoryOrigins, m_maxRayConstraints,
                new Dictionary<string, int>(m_rejections),
                m_potentialField != null || m_heuristicEvaluator != null,
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
            public bool IsFixedGoalTerminal { get; }

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
                Tile2i? groundCenter = null,
                bool isFixedGoalTerminal = false)
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
                IsFixedGoalTerminal = isFixedGoalTerminal;
            }
        }

        private readonly struct SearchPriority : IComparable<SearchPriority>
        {
            private readonly float m_total;
            private readonly float m_heuristic;

            public SearchPriority(float total, float heuristic)
            {
                m_total = total;
                m_heuristic = heuristic;
            }

            public int CompareTo(SearchPriority other)
            {
                int total = m_total.CompareTo(other.m_total);
                return total != 0
                    ? total
                    : m_heuristic.CompareTo(other.m_heuristic);
            }
        }

        private readonly struct SearchKey : IEquatable<SearchKey>
        {
            private readonly AccessV2BandState m_state;
            private readonly Tile2i? m_groundCenter;
            private readonly bool m_isFixedGoalTerminal;

            public SearchKey(SearchNode node)
            {
                m_groundCenter = node.GroundCenter;
                m_isFixedGoalTerminal = node.IsFixedGoalTerminal;
                // Match V1's label dominance. The cheapest arrival owns the
                // history used for later feasibility checks; history is not a
                // second state dimension for either G centers or V bands.
                m_state = node.GroundCenter.HasValue
                    ? default
                    : node.State;
            }

            public bool Equals(SearchKey other)
                => m_state.Equals(other.m_state)
                    && m_groundCenter == other.m_groundCenter
                    && m_isFixedGoalTerminal == other.m_isFixedGoalTerminal;

            public override bool Equals(object? obj)
                => obj is SearchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = m_state.GetHashCode();
                    hash = (hash * 397) ^ m_groundCenter.GetHashCode();
                    hash = (hash * 397) ^ m_isFixedGoalTerminal.GetHashCode();
                    return hash;
                }
            }
        }
    }
}

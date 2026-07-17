using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public bool RequiresGroundTransition { get; }
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
            float exteriorRayCost = 0f,
            bool requiresGroundTransition = false)
        {
            IsValid = isValid;
            RejectionReason = rejectionReason ?? string.Empty;
            TraversalCost = traversalCost;
            GeneratedWorkCost = generatedWorkCost;
            DirectWorkCost = directWorkCost;
            GeneratedFixedCost = generatedFixedCost;
            ExteriorRayCost = exteriorRayCost;
            CleanupCost = cleanupCost;
            RequiresGroundTransition = requiresGroundTransition;
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
            AccessV2History history,
            Tile2i? requiredGroundEntry);

    internal delegate AccessV2HandoffCandidate?
        AccessV2GroundToVHandoffEvaluator(
            AccessV2BandState state,
            Tile2i groundEntry,
            AccessHandoffOperation operation,
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
        private readonly AccessV2GroundToVHandoffEvaluator?
            m_groundToVHandoffEvaluator;
        private readonly AccessV2HeuristicEvaluator? m_heuristicEvaluator;
        private readonly AccessV2PotentialField? m_potentialField;
        private readonly AccessV2GroundEscapePotentialField?
            m_groundEscapePotentialField;
        private readonly Func<Tile2i, int?>? m_groundHeightProvider;
        private readonly Func<Tile2i, int>? m_terrainCenterHeightProvider;
        private readonly Func<Tile2i, float?>? m_preciseTerrainHeightProvider;
        private readonly Func<Tile2i, bool>? m_generatedOriginValidator;
        private readonly AccessV2GroundGraph? m_groundGraph;
        private readonly AccessUsefulHeightEnvelope? m_usefulHeightEnvelope;
        private readonly AccessSearchDiagnostics m_diagnostics;
        private readonly Func<Tile2i, AccessV2History, bool>? m_groundValidator;
        private readonly float m_cleanupCostScale;
        private readonly float m_groundToVCenterSpokeCost;
        private readonly int m_maxVisited;
        private readonly float m_maxCost;
        private readonly SortedDictionary<SearchPriority, Queue<SearchNode>> m_queue =
            new SortedDictionary<SearchPriority, Queue<SearchNode>>();
        private readonly Dictionary<SearchKey, float> m_best =
            new Dictionary<SearchKey, float>();
        private readonly HashSet<AccessV2BandState> m_groundToVAccepted =
            new HashSet<AccessV2BandState>();
        private readonly AccessV2HandoffDominanceCache m_handoffDominance =
            new AccessV2HandoffDominanceCache();
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
        internal Dictionary<string, int> LiveRejections => m_rejections;
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
            float groundToVMinimumGeneratedCost = 0f,
            AccessUsefulHeightEnvelope? usefulHeightEnvelope = null,
            Func<Tile2i, bool>? generatedOriginValidator = null,
            AccessSearchDiagnostics? diagnostics = null,
            Func<Tile2i, float?>? preciseTerrainHeightProvider = null,
            float groundToVCenterSpokeCost = 2f,
            AccessV2GroundToVHandoffEvaluator? groundToVHandoffEvaluator = null)
        {
            m_boundsMin = boundsMin;
            m_boundsMax = boundsMax;
            m_goals = endpoints.FixedGoals;
            m_evaluator = evaluator;
            m_handoffEvaluator = handoffEvaluator;
            m_groundToVHandoffEvaluator = groundToVHandoffEvaluator;
            m_heuristicEvaluator = heuristicEvaluator;
            m_potentialField = potentialField;
            m_groundHeightProvider = groundHeightProvider;
            m_terrainCenterHeightProvider = terrainCenterHeightProvider;
            m_preciseTerrainHeightProvider = preciseTerrainHeightProvider;
            m_generatedOriginValidator = generatedOriginValidator;
            m_groundGraph = groundGraph;
            m_usefulHeightEnvelope = usefulHeightEnvelope;
            m_diagnostics = diagnostics ?? new AccessSearchDiagnostics();
            m_groundEscapePotentialField = potentialField != null
                && groundGraph != null
                    ? new AccessV2GroundEscapePotentialField(
                        groundGraph, potentialField,
                        groundToVMinimumGeneratedCost)
                    : null;
            m_groundValidator = groundValidator;
            m_cleanupCostScale = cleanupCostScale;
            m_groundToVCenterSpokeCost = groundToVCenterSpokeCost;
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
                    long suffixStart = Stopwatch.GetTimestamp();
                    if (TryCompleteGroundSuffix(current, out SearchNode? terminal))
                    {
                        m_diagnostics.V2GroundSuffixTicks +=
                            Stopwatch.GetTimestamp() - suffixStart;
                        CompleteSuccess(terminal!);
                        break;
                    }
                    m_diagnostics.V2GroundSuffixTicks +=
                        Stopwatch.GetTimestamp() - suffixStart;
                    m_diagnostics.V2GroundExpansions++;
                    long groundStart = Stopwatch.GetTimestamp();
                    ExpandGround(current);
                    m_diagnostics.V2GroundExpansionTicks +=
                        Stopwatch.GetTimestamp() - groundStart;
                    continue;
                }
                m_diagnostics.V2BandExpansions++;
                long bandStart = Stopwatch.GetTimestamp();
                if (TryMatchGoal(current.State, out AccessV2FixedFrontage? fixedGoal))
                    EnqueueFixedGoal(current, fixedGoal!);

                EnqueueHandoffGoals(current);
                if (!current.RequiresGroundTransition)
                    Expand(current);
                m_diagnostics.V2BandExpansionTicks +=
                    Stopwatch.GetTimestamp() - bandStart;
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
                if (!IsTransitionWithinUsefulHeightEnvelope(
                        m_usefulHeightEnvelope, transition,
                        out string envelopeRejection))
                {
                    Reject(envelopeRejection);
                    return;
                }
                if (!history.TryApply(
                        transition, out _, out string geometryReason))
                {
                    Reject(geometryReason);
                    return;
                }
                long evaluationStart = Stopwatch.GetTimestamp();
                AccessV2TransitionEvaluation evaluation = m_evaluator(
                    null, transition, history, start.FixedSeedOrigin);
                m_diagnostics.V2TransitionEvaluationTicks +=
                    Stopwatch.GetTimestamp() - evaluationStart;
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
                AccessV2Transition? turn = null;
                string turnReason = string.Empty;
                SearchNode? predecessor = current.Parent;
                bool hasVPredecessor = predecessor != null
                    && !predecessor.GroundCenter.HasValue;
                if (hasVPredecessor
                    && AccessV2Geometry.TryTurn(
                        predecessor!.State, current.State, sign,
                        out AccessV2Transition candidateTurn,
                        out turnReason))
                    turn = candidateTurn;
                else if (hasVPredecessor)
                    Reject(turnReason);

                // A flat strafe and the corresponding turn path can emit the
                // same terrain plan. Keep one canonical representation so
                // incremental ray cost and blockage cannot differ by graph path.
                if (!hasVPredecessor)
                    Reject("StrafeRequiresPredecessorSlice");
                else if (turn != null && current.State.Band.IsCompletelyFlat)
                    Reject("FlatStrafeDominatedByTurn");
                else if (!TryGetStrafePredecessorProfile(
                    current, sign,
                    out AccessHeightProfile predecessorProfile))
                    Reject("StrafePredecessorProfileMissing");
                else if (AccessV2Geometry.TryStrafe(
                    current.State, sign, predecessorProfile,
                    out AccessV2Transition strafe, out string strafeReason))
                    TryRelax(current, strafe);
                else
                    Reject(strafeReason);

                if (turn != null)
                {
                    TryRelax(current, turn);
                }
            }
        }

        private static bool TryGetStrafePredecessorProfile(
            SearchNode current,
            int transverseSign,
            out AccessHeightProfile profile)
        {
            int retainedLane = transverseSign < 0 ? 0 : 1;
            Tile2i retainedOrigin = AccessV2Geometry.Subtract(
                current.State.GetLaneOrigin(retainedLane),
                current.State.EntryDirection);
            if (current.History.TryGetProfile(retainedOrigin, out profile))
                return true;

            SearchNode? predecessor = current.Parent;
            if (predecessor == null || predecessor.GroundCenter.HasValue)
            {
                profile = default;
                return false;
            }
            for (int lane = 0; lane < 2; lane++)
            {
                if (predecessor.State.GetLaneOrigin(lane) == retainedOrigin)
                {
                    profile = predecessor.State.Band.GetLane(lane);
                    return true;
                }
            }
            profile = default;
            return false;
        }

        private void TryRelax(
            SearchNode current,
            AccessV2Transition transition)
        {
            if (!AccessV2Geometry.IsInsideBounds(
                    transition, m_boundsMin, m_boundsMax))
            {
                Reject("HorizontalBounds");
                return;
            }
            if (!IsTransitionWithinUsefulHeightEnvelope(
                    m_usefulHeightEnvelope, transition,
                    out string envelopeRejection))
            {
                Reject(envelopeRejection);
                return;
            }
            if (!current.History.TryApply(
                    transition, out _, out string historyReason))
            {
                Reject(historyReason);
                return;
            }

            long evaluationStart = Stopwatch.GetTimestamp();
            AccessV2TransitionEvaluation evaluation = m_evaluator(
                current.State, transition, current.History, null);
            m_diagnostics.V2TransitionEvaluationTicks +=
                Stopwatch.GetTimestamp() - evaluationStart;
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
                current, transition, null,
                requiresGroundTransition:
                    evaluation.RequiresGroundTransition);
            Enqueue(next);
        }

        /// <summary>
        /// V2 straight and strafe transitions introduce profile centers at new
        /// locations. Turns only reorient an already admitted flat landing, so
        /// testing their delta would be redundant.
        /// </summary>
        internal static bool IsTransitionWithinUsefulHeightEnvelope(
            AccessUsefulHeightEnvelope? envelope,
            AccessV2Transition transition,
            out string rejection)
        {
            if (envelope == null || transition.Kind == AccessV2TransitionKind.Turn)
            {
                rejection = string.Empty;
                return true;
            }

            for (int index = 0; index < transition.Delta.Count; index++)
            {
                AccessV2OriginProfile introduced = transition.Delta[index];
                Tile2i center = introduced.Origin + new RelTile2i(2, 2);
                if (!envelope.IsV2CenterHeightUseful(
                        center, checked(introduced.Profile.Center2 * 16),
                        out rejection))
                    return false;
            }

            rejection = string.Empty;
            return true;
        }

        private void EnqueueHandoffGoals(SearchNode current)
        {
            if (m_handoffEvaluator == null) return;
            // A freshly entered V band has proved its entry seam in the
            // opposite direction. Returning to that same G center is strictly
            // dominated, but an exit through the band's opposite face is a
            // valid one-brush V bridge and must remain available.
            Tile2i? enteredFromGround = null;
            for (SearchNode? entry = current;
                entry?.Parent != null;
                entry = entry.Parent)
            {
                if (!entry.Parent.GroundCenter.HasValue)
                    continue;
                enteredFromGround = entry.Parent.GroundCenter;
                break;
            }
            var recent = new List<AccessV2BandState>(
                AccessV2Handoffs.MaxSpanLength);
            for (SearchNode? node = current;
                node != null && recent.Count < AccessV2Handoffs.MaxSpanLength;
                node = node.Parent)
            {
                if (node.GroundCenter.HasValue) break;
                recent.Add(node.State);
            }
            long handoffStart = Stopwatch.GetTimestamp();
            IReadOnlyList<AccessV2HandoffCandidate> candidates =
                m_handoffEvaluator(recent, current.History, null);
            m_diagnostics.V2HandoffEvaluationTicks +=
                Stopwatch.GetTimestamp() - handoffStart;
            m_handoffEvaluations++;
            m_diagnostics.V2HandoffEvaluations++;
            if (candidates.Any(item => item.IsQuickPath))
            {
                m_quickHandoffAccepts++;
                m_diagnostics.V2QuickHandoffAccepts++;
            }
            for (int index = 0; index < candidates.Count; index++)
            {
                AccessV2HandoffCandidate handoff = candidates[index];
                if (enteredFromGround.HasValue
                    && handoff.GroundEntryCenters.Contains(
                        enteredFromGround.Value))
                    continue;
                float cost = current.Cost + handoff.TotalCost;
                if (cost > m_maxCost) continue;
                if (m_handoffDominance.IsDominated(
                        current, handoff, cost))
                {
                    m_diagnostics.V2HandoffDominancePrunes++;
                    continue;
                }
                AccessV2History handoffHistory =
                    current.History.ApplyCleanupKeys(handoff.CleanupKeys);
                if (handoff.GroundEntryCenters.Count > 0
                    && m_handoffDominance.RecordSuccess(
                        current, handoff, cost))
                    m_diagnostics.V2HandoffDominanceSuccesses++;
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
                long localEscapeStart = Stopwatch.GetTimestamp();
                bool localEscapeValid = m_groundGraph.TryValidateLocalEscape(
                        sweptCenters, current.History,
                        m_cleanupCostScale,
                        out IReadOnlyCollection<string> cleanupKeys,
                        out float cleanupCost);
                m_diagnostics.V2LocalEscapeTicks +=
                    Stopwatch.GetTimestamp() - localEscapeStart;
                if (!localEscapeValid)
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

            m_diagnostics.V2GroundToVCalls++;
            long groundToVStart = Stopwatch.GetTimestamp();
            ExpandGroundToV(current);
            m_diagnostics.V2GroundToVTicks +=
                Stopwatch.GetTimestamp() - groundToVStart;
        }

        private bool TryCompleteGroundSuffix(
            SearchNode current,
            out SearchNode? terminal)
        {
            terminal = null;
            if (m_groundGraph == null
                || (m_potentialField == null && m_heuristicEvaluator == null)
                || !current.GroundCenter.HasValue
                || !m_groundGraph.TryGetGoalDistance(
                    current.GroundCenter.Value, out float distance)
                || distance <= 0f)
                return false;

            m_diagnostics.V2GroundSuffixAttempts++;
            SearchNode cursor = current;
            int maxSteps = Math.Max(1, m_groundGraph.GroundNodeCount
                + m_groundGraph.CleanupNodeCount);
            RelTile2i[] directions =
            {
                new RelTile2i(1, 0), new RelTile2i(-1, 0),
                new RelTile2i(0, 1), new RelTile2i(0, -1),
                new RelTile2i(1, 1), new RelTile2i(1, -1),
                new RelTile2i(-1, 1), new RelTile2i(-1, -1),
            };

            for (int stepIndex = 0; stepIndex < maxSteps; stepIndex++)
            {
                Tile2i from = cursor.GroundCenter!.Value;
                if (m_groundGraph.IsGoal(from))
                {
                    m_diagnostics.V2GroundSuffixSuccesses++;
                    m_diagnostics.V2GroundSuffixSteps += stepIndex;
                    terminal = cursor;
                    return true;
                }
                if (!m_groundGraph.TryGetGoalDistance(from, out distance))
                    break;

                SearchNode? nextNode = null;
                for (int directionIndex = 0;
                    directionIndex < directions.Length;
                    directionIndex++)
                {
                    Tile2i next = from + directions[directionIndex];
                    if (!m_groundGraph.TryGetGoalDistance(
                            next, out float nextDistance))
                        continue;
                    float stepCost = AccessV2GroundGraph.GetStepCost(from, next);
                    if (Math.Abs(distance - stepCost - nextDistance) > 0.001f
                        || !m_groundGraph.CanTraverse(from, next))
                        continue;

                    IReadOnlyList<Tile2i> sweptCenters =
                        AccessV2GroundGraph.GetSweptCenters(from, next);
                    if (m_groundValidator != null
                        && sweptCenters.Any(center =>
                            !m_groundValidator(center, cursor.History)))
                        continue;
                    long localEscapeStart = Stopwatch.GetTimestamp();
                    bool localEscapeValid = m_groundGraph.TryValidateLocalEscape(
                        sweptCenters, cursor.History,
                        m_cleanupCostScale,
                        out IReadOnlyCollection<string> cleanupKeys,
                        out float cleanupCost);
                    m_diagnostics.V2LocalEscapeTicks +=
                        Stopwatch.GetTimestamp() - localEscapeStart;
                    if (!localEscapeValid)
                        continue;

                    float nextCost = cursor.Cost + stepCost + cleanupCost;
                    if (nextCost > m_maxCost)
                        continue;
                    nextNode = new SearchNode(
                        cursor.State,
                        cursor.History.ApplyCleanupKeys(cleanupKeys),
                        nextCost,
                        cursor.TraversalCost + stepCost,
                        cursor.GeneratedWorkCost,
                        cursor.DirectWorkCost,
                        cursor.GeneratedFixedCost,
                        cursor.ExteriorRayCost,
                        cursor.CleanupCost + cleanupCost,
                        cursor, null, null, next);
                    break;
                }

                if (nextNode == null)
                    break;
                cursor = nextNode;
            }

            m_diagnostics.V2GroundSuffixFallbacks++;
            return false;
        }

        private void ExpandGroundToV(SearchNode current)
        {
            if (m_groundGraph == null
                || m_groundToVHandoffEvaluator == null
                || m_terrainCenterHeightProvider == null
                || !current.GroundCenter.HasValue)
                return;

            Tile2i ground = current.GroundCenter.Value;
            Tile2i[] travelDirections =
            {
                new Tile2i(4, 0), new Tile2i(-4, 0),
                new Tile2i(0, 4), new Tile2i(0, -4),
            };
            for (int directionIndex = 0;
                directionIndex < travelDirections.Length;
                directionIndex++)
            {
                Tile2i travel = travelDirections[directionIndex];
                AccessV2TravelAxis axis = travel.X != 0
                    ? AccessV2TravelAxis.X
                    : AccessV2TravelAxis.Y;
                Tile2i anchor = GetGroundToVBandAnchor(ground, travel);
                m_diagnostics.V2GroundToVAnchorCandidates++;

                // This is the in-tower-area gate. G remains freely traversable
                // outside the generated-origin domain, but no reverse handoff
                // work may be considered there.
                if (!AreGroundToVBandOriginsEligible(
                        anchor, axis, m_generatedOriginValidator))
                {
                    m_diagnostics.V2GroundToVTowerAreaRejects++;
                    continue;
                }

                float terrainHeight = m_preciseTerrainHeightProvider?.Invoke(
                    ground) ?? m_terrainCenterHeightProvider(ground) / 2f;

                // The leveling shortcut is valid only at the shared cell edge.
                // Rough candidates are deliberately evaluated from every G.
                if (CanUseDirectGroundToVLevelingBridge(ground, travel)
                    && Math.Abs(terrainHeight - Math.Round(terrainHeight))
                        <= 0.0001f)
                {
                    foreach (GroundToVProfileCandidate candidate in
                        EnumerateDirectLevelingProfiles(
                            (int)Math.Round(terrainHeight), axis, travel))
                        TryEmitGroundToV(
                            current, ground, anchor, axis, travel,
                            candidate, directLeveling: true);
                }

                foreach (GroundToVProfileCandidate candidate in
                    EnumerateGroundToVProfiles(terrainHeight, axis, travel))
                    TryEmitGroundToV(
                        current, ground, anchor, axis, travel,
                        candidate, directLeveling: false);
            }
        }

        private bool TryEmitGroundToV(
            SearchNode groundNode,
            Tile2i groundCenter,
            Tile2i anchor,
            AccessV2TravelAxis axis,
            Tile2i travel,
            GroundToVProfileCandidate candidate,
            bool directLeveling)
        {
            if (m_groundToVHandoffEvaluator == null)
                return false;
            m_diagnostics.V2GroundToVSeedCalls++;
            m_diagnostics.V2GroundToVProfileCandidates++;
            if (!AccessV2BandProfile.TryCreateEnabled(
                    axis, candidate.Profile, candidate.Profile,
                    out AccessV2BandProfile band, out _))
                return false;
            var state = new AccessV2BandState(anchor, band, travel);
            if (m_groundToVAccepted.Contains(state))
            {
                m_diagnostics.V2GroundToVCacheHits++;
                return true;
            }
            if (!AccessV2Geometry.IsInsideBounds(
                    state, m_boundsMin, m_boundsMax))
                return false;
            var transition = new AccessV2Transition(
                AccessV2TransitionKind.Straight,
                state,
                new[] { state.GetLane(0), state.GetLane(1) },
                Array.Empty<Tile2i>());
            if (!IsTransitionWithinUsefulHeightEnvelope(
                    m_usefulHeightEnvelope, transition,
                    out string envelopeRejection))
            {
                Reject(envelopeRejection);
                return false;
            }
            if (!groundNode.History.TryApply(
                    transition, out _, out string historyReason))
            {
                Reject(historyReason);
                return false;
            }
            m_diagnostics.V2GroundToVSeedExtensions++;
            long evaluationStart = Stopwatch.GetTimestamp();
            AccessV2TransitionEvaluation evaluation = m_evaluator(
                null, transition, groundNode.History, null);
            m_diagnostics.V2TransitionEvaluationTicks +=
                Stopwatch.GetTimestamp() - evaluationStart;
            if (!evaluation.IsValid || evaluation.RequiresGroundTransition)
            {
                if (!evaluation.IsValid)
                    Reject(evaluation.RejectionReason);
                return false;
            }
            if (!groundNode.History.TryApply(
                    transition.Delta,
                    transition.LocalContextOrigins,
                    evaluation.RayConstraints,
                    evaluation.CleanupKeys,
                    out AccessV2History nextHistory,
                    out historyReason))
            {
                Reject(historyReason);
                return false;
            }

            AccessV2HandoffCandidate? seam;
            if (directLeveling)
            {
                seam = AccessV2Handoffs.TryCreateDirectLevelingBridge(
                    state, groundCenter, m_groundToVCenterSpokeCost,
                    out AccessV2HandoffCandidate directSeam)
                        ? directSeam
                        : null;
            }
            else
            {
                long handoffStart = Stopwatch.GetTimestamp();
                seam = m_groundToVHandoffEvaluator(
                    state, groundCenter, candidate.ExpectedOperation,
                    nextHistory);
                m_diagnostics.V2HandoffEvaluationTicks +=
                    Stopwatch.GetTimestamp() - handoffStart;
                m_handoffEvaluations++;
                m_diagnostics.V2HandoffEvaluations++;
            }
            if (seam == null)
                return false;

            float cost = groundNode.Cost + seam.TotalCost
                + evaluation.TotalCost;
            if (cost > m_maxCost)
                return false;
            var node = new SearchNode(
                state, nextHistory.ApplyCleanupKeys(seam.CleanupKeys), cost,
                groundNode.TraversalCost + seam.CenterSpokeCost
                    + evaluation.TraversalCost,
                groundNode.GeneratedWorkCost + evaluation.GeneratedWorkCost,
                groundNode.DirectWorkCost + evaluation.DirectWorkCost,
                groundNode.GeneratedFixedCost + evaluation.GeneratedFixedCost,
                groundNode.ExteriorRayCost + evaluation.ExteriorRayCost,
                groundNode.CleanupCost + seam.CleanupCost
                    + evaluation.CleanupCost,
                groundNode, transition, seam);
            Enqueue(node);
            m_groundToVAccepted.Add(state);
            m_diagnostics.V2GroundToVCacheInsertions++;
            if (directLeveling)
            {
                m_quickHandoffAccepts++;
                m_diagnostics.V2QuickHandoffAccepts++;
                m_diagnostics.V2GroundToVDirectLevelingAccepts++;
            }
            else
                m_diagnostics.V2GroundToVRoughAccepts++;
            return true;
        }

        /// <summary>
        /// The direct leveling shortcut starts from a captured vehicle center
        /// on a canonical four-tile grid edge. Rough G-to-V candidates do not
        /// use this restriction and are tested from every reached G.
        /// </summary>
        internal static bool CanUseDirectGroundToVLevelingBridge(
            Tile2i ground,
            Tile2i travelDirection)
        {
            if (travelDirection.X == -4 && travelDirection.Y == 0)
                return (ground.X & 3) == 0;
            if (travelDirection.X == 4 && travelDirection.Y == 0)
                return (ground.X & 3) == 0;
            if (travelDirection.X == 0 && travelDirection.Y == -4)
                return (ground.Y & 3) == 0;
            if (travelDirection.X == 0 && travelDirection.Y == 4)
                return (ground.Y & 3) == 0;
            return false;
        }

        internal static bool AreGroundToVBandOriginsEligible(
            Tile2i anchor,
            AccessV2TravelAxis axis,
            Func<Tile2i, bool>? originValidator)
        {
            if (originValidator == null)
                return true;
            Tile2i companion = AccessV2Geometry.Add(
                anchor, AccessV2BandProfile.GetLaneDirection(axis));
            return originValidator(anchor) && originValidator(companion);
        }

        internal static Tile2i GetGroundToVBandAnchor(
            Tile2i ground,
            Tile2i travelDirection)
        {
            AccessV2TravelAxis axis = travelDirection.X != 0
                ? AccessV2TravelAxis.X
                : AccessV2TravelAxis.Y;
            int transverseResidue = axis == AccessV2TravelAxis.X
                ? ground.Y & 3
                : ground.X & 3;
            bool companionOnNegativeSide = transverseResidue <= 1;
            if (axis == AccessV2TravelAxis.X)
                return new Tile2i(
                    travelDirection.X > 0
                        ? ground.X & -4
                        : (ground.X - 1) & -4,
                    (ground.Y & -4) - (companionOnNegativeSide ? 4 : 0));
            return new Tile2i(
                (ground.X & -4) - (companionOnNegativeSide ? 4 : 0),
                travelDirection.Y > 0
                    ? ground.Y & -4
                    : (ground.Y - 1) & -4);
        }

        internal readonly struct GroundToVProfileCandidate
        {
            public AccessHeightProfile Profile { get; }
            public AccessHandoffOperation ExpectedOperation { get; }

            public GroundToVProfileCandidate(
                AccessHeightProfile profile,
                AccessHandoffOperation expectedOperation)
            {
                Profile = profile;
                ExpectedOperation = expectedOperation;
            }
        }

        internal static IEnumerable<GroundToVProfileCandidate>
            EnumerateGroundToVProfiles(
                float terrainHeight,
                AccessV2TravelAxis axis,
                Tile2i travelDirection)
        {
            int miningLevel = (int)Math.Ceiling(terrainHeight);
            foreach (AccessSearchMode mode in TravelModes(axis, travelDirection))
                if (global::AutoTerrainDesignations.Access.AccessPathSearch
                        .TryProfileAtEntryLevel(
                        mode, miningLevel, travelDirection,
                        out AccessHeightProfile profile))
                    yield return new GroundToVProfileCandidate(
                        profile, AccessHandoffOperation.Mining);

            int dumpingLevel = (int)Math.Floor(terrainHeight);
            foreach (AccessSearchMode mode in TravelModes(axis, travelDirection))
                if (global::AutoTerrainDesignations.Access.AccessPathSearch
                        .TryProfileAtEntryLevel(
                        mode, dumpingLevel, travelDirection,
                        out AccessHeightProfile profile))
                    yield return new GroundToVProfileCandidate(
                        profile, AccessHandoffOperation.Dumping);
        }

        internal static IEnumerable<GroundToVProfileCandidate>
            EnumerateDirectLevelingProfiles(
                int entryLevel,
                AccessV2TravelAxis axis,
                Tile2i travelDirection)
        {
            foreach (AccessSearchMode mode in TravelModes(axis, travelDirection))
                if (global::AutoTerrainDesignations.Access.AccessPathSearch
                        .TryProfileAtEntryLevel(
                        mode, entryLevel, travelDirection,
                        out AccessHeightProfile profile))
                    yield return new GroundToVProfileCandidate(
                        profile, AccessHandoffOperation.Leveling);
        }

        private static IEnumerable<AccessSearchMode> TravelModes(
            AccessV2TravelAxis axis,
            Tile2i travelDirection)
        {
            AccessSearchMode up = axis == AccessV2TravelAxis.X
                ? (travelDirection.X > 0
                    ? AccessSearchMode.XPositive
                    : AccessSearchMode.XNegative)
                : (travelDirection.Y > 0
                    ? AccessSearchMode.YPositive
                    : AccessSearchMode.YNegative);
            AccessSearchMode down = axis == AccessV2TravelAxis.X
                ? (travelDirection.X > 0
                    ? AccessSearchMode.XNegative
                    : AccessSearchMode.XPositive)
                : (travelDirection.Y > 0
                    ? AccessSearchMode.YNegative
                    : AccessSearchMode.YPositive);
            yield return AccessSearchMode.Flat;
            yield return up;
            yield return down;
        }

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
            public bool RequiresGroundTransition { get; }

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
                bool isFixedGoalTerminal = false,
                bool requiresGroundTransition = false)
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
                RequiresGroundTransition = requiresGroundTransition;
            }
        }

        /// <summary>
        /// Cost-dominance only: a successful shallow cut/fill does not prove
        /// another profile pathable. Once both candidates independently name
        /// the same handoff geometry, however, the strictly more aggressive
        /// and no-cheaper sibling cannot improve the route.
        /// </summary>
        private sealed class AccessV2HandoffDominanceCache
        {
            private readonly List<Success> m_successes = new List<Success>();

            public bool IsDominated(
                SearchNode current,
                AccessV2HandoffCandidate candidate,
                float cost)
            {
                if (!TryDescribe(
                        current, candidate,
                        out AccessV2BandState parent,
                        out int rank))
                    return false;
                for (int index = 0; index < m_successes.Count; index++)
                {
                    Success success = m_successes[index];
                    if (success.Parent.Equals(parent)
                        && success.Rank < rank
                        && success.Cost <= cost + 0.0001f
                        && SameGeometry(success.Candidate, candidate))
                        return true;
                }
                return false;
            }

            public bool RecordSuccess(
                SearchNode current,
                AccessV2HandoffCandidate candidate,
                float cost)
            {
                if (!TryDescribe(
                        current, candidate,
                        out AccessV2BandState parent,
                        out int rank))
                    return false;
                for (int index = 0; index < m_successes.Count; index++)
                {
                    Success success = m_successes[index];
                    if (!success.Parent.Equals(parent)
                        || !SameGeometry(success.Candidate, candidate))
                        continue;
                    if (success.Rank <= rank
                        && success.Cost <= cost + 0.0001f)
                        return false;
                }
                m_successes.Add(new Success(parent, candidate, rank, cost));
                return true;
            }

            private static bool TryDescribe(
                SearchNode current,
                AccessV2HandoffCandidate candidate,
                out AccessV2BandState parent,
                out int rank)
            {
                parent = default;
                rank = 0;
                if (current.Parent == null
                    || current.Parent.GroundCenter.HasValue
                    || current.Transition?.Kind
                        != AccessV2TransitionKind.Straight
                    || candidate.Lane0Operation
                        != candidate.Lane1Operation
                    || !AccessV2BandProfile.TryGetProfileMode(
                        current.State.Band.Lane0,
                        out AccessSearchMode lane0Mode)
                    || !AccessV2BandProfile.TryGetProfileMode(
                        current.State.Band.Lane1,
                        out AccessSearchMode lane1Mode)
                    || lane0Mode != lane1Mode
                    || !global::AutoTerrainDesignations.Access.AccessPathSearch
                        .TryGetHandoffAggressionRank(
                            lane0Mode, current.State.EntryDirection,
                            candidate.Lane0Operation, out rank))
                    return false;
                parent = current.Parent.State;
                return true;
            }

            private static bool SameGeometry(
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
                        right.Lane1TerminalOrigins)
                    && left.EscapeCenters.SequenceEqual(right.EscapeCenters)
                    && left.GroundEntryCenters.SequenceEqual(
                        right.GroundEntryCenters)
                    && Math.Abs(left.CenterSpokeCost
                        - right.CenterSpokeCost) <= 0.0001f;

            private readonly struct Success
            {
                public AccessV2BandState Parent { get; }
                public AccessV2HandoffCandidate Candidate { get; }
                public int Rank { get; }
                public float Cost { get; }

                public Success(
                    AccessV2BandState parent,
                    AccessV2HandoffCandidate candidate,
                    int rank,
                    float cost)
                {
                    Parent = parent;
                    Candidate = candidate;
                    Rank = rank;
                    Cost = cost;
                }
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

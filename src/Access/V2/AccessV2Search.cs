using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

    internal delegate AccessV2TransitionEvaluation
        AccessV2TerminalTransitionEvaluator(
            AccessV2BandState? current,
            AccessV2Transition transition,
            AccessV2History history,
            Tile2i? connectedFixedOrigin,
            AccessHandoffOperation operation);

    internal delegate AccessV2TerminalExtensionRequest
        AccessV2TerminalExtensionOperationEvaluator(
            IReadOnlyList<AccessV2BandState> recentNewestFirst);

    internal delegate IReadOnlyList<AccessV2HandoffCandidate>
        AccessV2StaggeredHandoffEvaluator(
            IReadOnlyList<AccessV2BandState> terminalOldestFirst,
            int extensionLane,
            AccessHandoffOperation operation,
            AccessV2History history);

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
        private readonly AccessV2TerminalTransitionEvaluator?
            m_terminalTransitionEvaluator;
        private readonly AccessV2StaggeredHandoffEvaluator?
            m_staggeredHandoffEvaluator;
        private readonly AccessV2HandoffEvaluator? m_handoffEvaluator;
        private readonly AccessV2TerminalExtensionOperationEvaluator?
            m_terminalExtensionOperationEvaluator;
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
            AccessV2GroundToVHandoffEvaluator? groundToVHandoffEvaluator = null,
            AccessV2TerminalExtensionOperationEvaluator?
                terminalExtensionOperationEvaluator = null,
            AccessV2TerminalTransitionEvaluator?
                terminalTransitionEvaluator = null,
            AccessV2StaggeredHandoffEvaluator?
                staggeredHandoffEvaluator = null)
        {
            m_boundsMin = boundsMin;
            m_boundsMax = boundsMax;
            m_goals = endpoints.FixedGoals;
            m_evaluator = evaluator;
            m_terminalTransitionEvaluator = terminalTransitionEvaluator;
            m_staggeredHandoffEvaluator = staggeredHandoffEvaluator;
            m_handoffEvaluator = handoffEvaluator;
            m_terminalExtensionOperationEvaluator =
                terminalExtensionOperationEvaluator;
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
                    long suffixStart = AtdDiagnostics.Timestamp();
                    if (TryCompleteGroundSuffix(current, out SearchNode? terminal))
                    {
                        m_diagnostics.V2GroundSuffixTicks +=
                            AtdDiagnostics.ElapsedSince(suffixStart);
                        CompleteSuccess(terminal!);
                        break;
                    }
                    m_diagnostics.V2GroundSuffixTicks +=
                        AtdDiagnostics.ElapsedSince(suffixStart);
                    m_diagnostics.V2GroundExpansions++;
                    long groundStart = AtdDiagnostics.Timestamp();
                    ExpandGround(current);
                    m_diagnostics.V2GroundExpansionTicks +=
                        AtdDiagnostics.ElapsedSince(groundStart);
                    continue;
                }
                m_diagnostics.V2BandExpansions++;
                long bandStart = AtdDiagnostics.Timestamp();
                if (TryMatchGoal(current.State, out AccessV2FixedFrontage? fixedGoal))
                    EnqueueFixedGoal(current, fixedGoal!);

                EnqueueHandoffGoals(current);
                if (!current.RequiresGroundTransition)
                    Expand(current);
                m_diagnostics.V2BandExpansionTicks +=
                    AtdDiagnostics.ElapsedSince(bandStart);
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
                if (!history.TryValidateApply(
                        transition.Delta,
                        transition.LocalContextOrigins,
                        out string geometryReason))
                {
                    Reject(geometryReason);
                    return;
                }
                long evaluationStart = AtdDiagnostics.Timestamp();
                AccessV2TransitionEvaluation evaluation = m_evaluator(
                    null, transition, history, start.FixedSeedOrigin);
                m_diagnostics.V2TransitionEvaluationTicks +=
                    AtdDiagnostics.ElapsedSince(evaluationStart);
                if (!evaluation.IsValid)
                {
                    Reject("StartSourceMegaSeam:" +
                        evaluation.RejectionReason);
                    return;
                }
                history = history.ApplyValidated(
                    transition.Delta,
                    evaluation.RayConstraints,
                    evaluation.CleanupKeys);
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

            // A turn is an orientation-only transition. Its next state may
            // either terminate here or take exactly one ramp step; flat and
            // strafe successors would recreate the paths the turn policy is
            // intended to eliminate.
            if (current.State.IsTurnPending)
                return;

            for (int sign = -1; sign <= 1; sign += 2)
            {
                AccessV2Transition? turn = null;
                string turnReason = string.Empty;
                if (TryFindTurnPredecessor(
                        current, out AccessV2BandState predecessor))
                {
                    if (AccessV2Geometry.TryTurn(
                            predecessor, current.State, sign,
                            out AccessV2Transition candidateTurn,
                            out turnReason))
                        turn = candidateTurn;
                    else
                        Reject(turnReason);
                }
                else if (AccessV2Geometry.TryTurn(
                        current.State, current.History, sign,
                        out AccessV2Transition historyTurn,
                        out turnReason))
                {
                    turn = historyTurn;
                }
                else
                {
                    Reject(turnReason);
                }

                // A flat strafe and the corresponding turn path can emit the
                // same terrain plan. Keep one canonical representation so
                // incremental ray cost and blockage cannot differ by graph path.
                if (turn != null && current.State.Band.IsCompletelyFlat)
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

        private static bool TryFindTurnPredecessor(
            SearchNode current,
            out AccessV2BandState predecessor)
        {
            Tile2i expectedAnchor = AccessV2Geometry.Subtract(
                current.State.Anchor, current.State.EntryDirection);
            for (SearchNode? node = current.Parent;
                node != null && !node.GroundCenter.HasValue;
                node = node.Parent)
            {
                if (node.State.Axis == current.State.Axis
                    && node.State.EntryDirection == current.State.EntryDirection
                    && node.State.Anchor == expectedAnchor)
                {
                    predecessor = node.State;
                    return true;
                }
            }
            predecessor = default;
            return false;
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
            bool traceStartSuccessor = current.Parent == null
                && AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace);
            void Trace(string outcome)
            {
                if (!traceStartSuccessor) return;
                AccessV2BandProfile band = transition.Next.Band;
                AccessV2BandProfile.TryGetProfileMode(
                    band.Lane0, out AccessSearchMode mode);
                m_diagnostics.RecordStartSuccessor(
                    $"v2 from={current.State.Anchor} " +
                    $"entry={current.State.EntryDirection} " +
                    $"next={transition.Next.Anchor} kind={transition.Kind} " +
                    $"mode={mode} band={band.Kind} " +
                    $"lane0={FormatProfile2(band.Lane0)} " +
                    $"lane1={FormatProfile2(band.Lane1)} " +
                    $"outcome={outcome}");
            }

            if (!AccessV2Geometry.IsInsideBounds(
                    transition, m_boundsMin, m_boundsMax))
            {
                Reject("HorizontalBounds");
                Trace("reject:HorizontalBounds");
                return;
            }
            if (!IsTransitionWithinUsefulHeightEnvelope(
                    m_usefulHeightEnvelope, transition,
                    out string envelopeRejection))
            {
                Reject(envelopeRejection);
                Trace("reject:" + envelopeRejection);
                return;
            }
            float traversalLowerBound = GetTransitionTraversalCost(
                current.State, transition.Next);
            if (IsVBandCostKnownNoWorse(
                    transition.Next,
                    current.Cost + traversalLowerBound))
            {
                m_diagnostics.V2EarlyLabelDominancePrunes++;
                Trace("prune:EarlyLabelDominance");
                return;
            }
            if (!current.History.TryValidateApply(
                    transition, out string historyReason))
            {
                Reject(historyReason);
                Trace("reject:" + historyReason);
                return;
            }

            long evaluationStart = AtdDiagnostics.Timestamp();
            AccessV2TransitionEvaluation evaluation = m_evaluator(
                current.State, transition, current.History, null);
            m_diagnostics.V2TransitionEvaluationTicks +=
                AtdDiagnostics.ElapsedSince(evaluationStart);
            if (!evaluation.IsValid)
            {
                Reject(evaluation.RejectionReason);
                Trace("reject:" + evaluation.RejectionReason);
                return;
            }
            float nextCost = current.Cost + evaluation.TotalCost;
            if (nextCost > m_maxCost)
            {
                Reject("CostLimitExceeded");
                Trace("reject:CostLimitExceeded " +
                    $"nextCost={FormatCost(nextCost)}");
                return;
            }
            if (IsVBandCostKnownNoWorse(transition.Next, nextCost))
            {
                m_diagnostics.V2ExactLabelDominancePrunes++;
                Trace("prune:ExactLabelDominance " +
                    $"step={FormatCost(evaluation.TotalCost)} " +
                    $"nextCost={FormatCost(nextCost)}");
                return;
            }
            AccessV2History nextHistory = current.History.ApplyValidated(
                transition.Delta,
                evaluation.RayConstraints,
                evaluation.CleanupKeys);
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
            Trace("accepted " +
                $"step={FormatCost(evaluation.TotalCost)} " +
                $"travel={FormatCost(evaluation.TraversalCost)} " +
                $"direct={FormatCost(evaluation.DirectWorkCost)} " +
                $"fixed={FormatCost(evaluation.GeneratedFixedCost)} " +
                $"rays={FormatCost(evaluation.ExteriorRayCost)} " +
                $"cleanup={FormatCost(evaluation.CleanupCost)} " +
                $"nextCost={FormatCost(nextCost)} " +
                $"requiresG={evaluation.RequiresGroundTransition}");
        }

        private static string FormatProfile2(AccessHeightProfile profile)
            => $"[{profile.Nw2},{profile.Ne2},{profile.Se2},{profile.Sw2}]/2";

        private static string FormatCost(float cost)
            => cost.ToString("0.##", CultureInfo.InvariantCulture);

        private bool IsVBandCostKnownNoWorse(
            AccessV2BandState state,
            float candidateCost)
            => m_best.TryGetValue(
                    new SearchKey(state), out float knownCost)
                && knownCost <= candidateCost + 0.0001f;

        private static float GetTransitionTraversalCost(
            AccessV2BandState current,
            AccessV2BandState next)
        {
            Tile2i from = AccessV2PotentialField.GetCanonicalCenter(current);
            Tile2i to = AccessV2PotentialField.GetCanonicalCenter(next);
            return Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y);
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
            long handoffStart = AtdDiagnostics.Timestamp();
            IReadOnlyList<AccessV2HandoffCandidate> candidates =
                m_handoffEvaluator(recent, current.History, null);
            m_diagnostics.V2HandoffEvaluationTicks +=
                AtdDiagnostics.ElapsedSince(handoffStart);
            m_diagnostics.RecordV2RouteHandoff(
                $"anchor={current.State.Anchor} entry={current.State.EntryDirection} " +
                $"band={current.State.Band.Kind} pathCost={FormatCost(current.Cost)} " +
                $"candidates={candidates.Count}" +
                (candidates.Count == 0
                    ? " outcome=no-compatible-ground-seam"
                    : " options=[" + string.Join(", ", candidates.Select(
                        candidate => candidate +
                            $" entryCenters=[{string.Join(",", candidate.GroundEntryCenters)}] " +
                            $"totalCost={FormatCost(candidate.TotalCost)}")) + "]"));
            if (candidates.Count > 0)
                m_diagnostics.RecordV2HandoffTrace(
                    current.State.Anchor,
                    candidates.SelectMany(candidate => candidate.GroundEntryCenters));
            if (current.Parent != null
                && current.Parent.Parent == null
                && current.Transition != null
                && AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
            {
                m_diagnostics.RecordStartSuccessor(
                    $"v2-handoff at={current.State.Anchor} " +
                    $"band={current.State.Band.Kind} " +
                    $"candidates={candidates.Count}" +
                    (candidates.Count == 0
                        ? " outcome=no-compatible-ground-seam"
                        : " options=[" + string.Join(", ", candidates.Select(
                            candidate => candidate +
                                $" cost={FormatCost(candidate.TotalCost)}")) +
                            "]"));
            }
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

            if (candidates.Count == 0)
                EnqueueSameTypeTerminalExtensions(current, recent);
        }

        private void EnqueueSameTypeTerminalExtensions(
            SearchNode current,
            IReadOnlyList<AccessV2BandState> recentNewestFirst)
        {
            if (m_terminalExtensionOperationEvaluator == null
                || m_terminalTransitionEvaluator == null
                || m_staggeredHandoffEvaluator == null
                || m_handoffEvaluator == null
                || current.Parent == null
                || current.Parent.GroundCenter.HasValue
                || current.Transition == null
                || current.Transition.Kind != AccessV2TransitionKind.Straight)
                return;
            AccessV2TerminalExtensionRequest request =
                m_terminalExtensionOperationEvaluator(recentNewestFirst);
            if (!request.IsValid)
                return;
            TraceTerminal($"request anchor={current.State.Anchor} " +
                $"op={request.Operation} extensionLane={request.ExtensionLane}");
            AccessHandoffOperation operation = request.Operation;

            SearchNode baseNode = current.Parent;
            if (!TryApplyTerminalTransition(
                    baseNode, current.Transition, operation,
                    out SearchNode correctedCurrent))
                return;
            EnqueueSpecialHandoffs(
                correctedCurrent,
                m_staggeredHandoffEvaluator(
                    new[] { correctedCurrent.State },
                    request.ExtensionLane, operation,
                    correctedCurrent.History));
            Extend(correctedCurrent,
                new List<AccessV2BandState> { correctedCurrent.State });

            void Extend(
                SearchNode cursor,
                List<AccessV2BandState> terminalStates)
            {
                if (terminalStates.Count >= AccessV2Handoffs.MaxSpanLength)
                    return;
                foreach (AccessV2Transition fullTransition in
                    AccessV2Geometry.EnumerateStraight(cursor.State))
                {
                    if (!IsTerminalExtensionMode(fullTransition.Next))
                        continue;
                    var transition = new AccessV2Transition(
                        AccessV2TransitionKind.Straight,
                        fullTransition.Next,
                        new[]
                        {
                            fullTransition.Next.GetLane(
                                request.ExtensionLane),
                        },
                        fullTransition.LocalContextOrigins,
                        workOperation: operation);
                    if (!TryApplyTerminalTransition(
                            cursor, transition, operation,
                            out SearchNode extension))
                        continue;

                    terminalStates.Add(extension.State);
                    IReadOnlyList<AccessV2HandoffCandidate> extensionHandoffs =
                        m_staggeredHandoffEvaluator(
                            terminalStates, request.ExtensionLane,
                            operation, extension.History);
                    bool accepted = EnqueueSpecialHandoffs(
                        extension, extensionHandoffs);
                    if (!accepted && !extension.RequiresGroundTransition)
                        Extend(extension, terminalStates);
                    terminalStates.RemoveAt(terminalStates.Count - 1);
                }
            }

            bool EnqueueSpecialHandoffs(
                SearchNode parent,
                IReadOnlyList<AccessV2HandoffCandidate> handoffs)
            {
                bool accepted = false;
                for (int index = 0; index < handoffs.Count; index++)
                {
                    AccessV2HandoffCandidate handoff = handoffs[index];
                    if (handoff.Lane0Operation != operation
                        || handoff.Lane1Operation != operation)
                        continue;
                    accepted = true;
                    EnqueueTerminalGround(parent, handoff);
                }
                TraceTerminal($"handoffs anchor={parent.State.Anchor} " +
                    $"count={handoffs.Count} accepted={accepted}");
                return accepted;
            }

            bool TryApplyTerminalTransition(
                SearchNode parent,
                AccessV2Transition transition,
                AccessHandoffOperation terminalOperation,
                out SearchNode node)
            {
                node = null!;
                if (!AccessV2Geometry.IsInsideBounds(
                        transition, m_boundsMin, m_boundsMax))
                {
                    TraceTerminal($"transition anchor={transition.Next.Anchor} reject=bounds");
                    return false;
                }
                if (!IsTransitionWithinUsefulHeightEnvelope(
                        m_usefulHeightEnvelope, transition,
                        out string envelopeReason))
                {
                    TraceTerminal($"transition anchor={transition.Next.Anchor} " +
                        $"reject={envelopeReason}");
                    return false;
                }
                if (!parent.History.TryValidateApply(
                        transition.Delta,
                        transition.LocalContextOrigins,
                        out string historyReason))
                {
                    TraceTerminal($"transition anchor={transition.Next.Anchor} " +
                        $"reject={historyReason}");
                    return false;
                }
                var terminalTransition = transition.WorkOperation ==
                        terminalOperation
                    ? transition
                    : new AccessV2Transition(
                        transition.Kind, transition.Next,
                        transition.Delta,
                        transition.LocalContextOrigins,
                        transition.OldDirectionTurnRays,
                        terminalOperation);
                AccessV2TransitionEvaluation evaluation =
                    m_terminalTransitionEvaluator(
                    parent.State, terminalTransition,
                    parent.History, null,
                    terminalOperation);
                if (!evaluation.IsValid)
                {
                    TraceTerminal($"transition anchor={transition.Next.Anchor} " +
                        $"op={terminalOperation} reject={evaluation.RejectionReason}");
                    return false;
                }
                float cost = parent.Cost + evaluation.TotalCost;
                if (cost > m_maxCost)
                {
                    TraceTerminal($"transition anchor={transition.Next.Anchor} reject=max-cost");
                    return false;
                }
                AccessV2History history = parent.History.ApplyValidated(
                    terminalTransition.Delta,
                    evaluation.RayConstraints,
                    evaluation.CleanupKeys);
                node = new SearchNode(
                    terminalTransition.Next, history, cost,
                    parent.TraversalCost + evaluation.TraversalCost,
                    parent.GeneratedWorkCost + evaluation.GeneratedWorkCost,
                    parent.DirectWorkCost + evaluation.DirectWorkCost,
                    parent.GeneratedFixedCost + evaluation.GeneratedFixedCost,
                    parent.ExteriorRayCost + evaluation.ExteriorRayCost,
                    parent.CleanupCost + evaluation.CleanupCost,
                    parent, terminalTransition, null,
                    requiresGroundTransition:
                        evaluation.RequiresGroundTransition);
                return true;
            }

            void TraceTerminal(string message)
            {
                if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                    m_diagnostics.RecordFirstGeneratedHandoff(
                        "v2-terminal " + message);
            }

            void EnqueueTerminalGround(
                SearchNode parent,
                AccessV2HandoffCandidate handoff)
            {
                float cost = parent.Cost + handoff.TotalCost;
                if (cost > m_maxCost)
                    return;
                AccessV2History history =
                    parent.History.ApplyCleanupKeys(handoff.CleanupKeys);
                for (int index = 0;
                    index < handoff.GroundEntryCenters.Count;
                    index++)
                {
                    Tile2i entry = handoff.GroundEntryCenters[index];
                    Enqueue(new SearchNode(
                        parent.State, history, cost,
                        parent.TraversalCost + handoff.CenterSpokeCost,
                        parent.GeneratedWorkCost,
                        parent.DirectWorkCost,
                        parent.GeneratedFixedCost,
                        parent.ExteriorRayCost,
                        parent.CleanupCost + handoff.CleanupCost,
                        parent, null, handoff, entry));
                }
            }

            bool IsTerminalExtensionMode(AccessV2BandState state)
            {
                if (!AccessV2BandProfile.TryGetProfileMode(
                        state.Band.Lane0, out AccessSearchMode mode))
                    return false;
                if (mode == AccessSearchMode.Flat)
                    return true;
                AccessSearchMode rising = state.Axis
                    == AccessV2TravelAxis.X
                        ? state.EntryDirection.X > 0
                            ? AccessSearchMode.XPositive
                            : AccessSearchMode.XNegative
                        : state.EntryDirection.Y > 0
                            ? AccessSearchMode.YPositive
                            : AccessSearchMode.YNegative;
                return mode == rising;
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
                long localEscapeStart = AtdDiagnostics.Timestamp();
                bool localEscapeValid = m_groundGraph.TryValidateLocalEscape(
                        sweptCenters, current.History,
                        m_cleanupCostScale,
                        out IReadOnlyCollection<string> cleanupKeys,
                        out float cleanupCost);
                m_diagnostics.V2LocalEscapeTicks +=
                    AtdDiagnostics.ElapsedSince(localEscapeStart);
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
            long groundToVStart = AtdDiagnostics.Timestamp();
            ExpandGroundToV(current);
            m_diagnostics.V2GroundToVTicks +=
                AtdDiagnostics.ElapsedSince(groundToVStart);
        }

        private bool TryCompleteGroundSuffix(
            SearchNode current,
            out SearchNode? terminal)
        {
            terminal = null;
            string prefix = $"entry={current.GroundCenter.GetValueOrDefault()} " +
                $"fromAnchor={current.State.Anchor} pathCost={FormatCost(current.Cost)}";
            if (m_groundGraph == null
                || (m_potentialField == null && m_heuristicEvaluator == null)
                || !current.GroundCenter.HasValue)
            {
                m_diagnostics.RecordV2GroundSuffix(prefix + " outcome=unavailable");
                return false;
            }
            if (!m_groundGraph.TryGetGoalDistance(
                    current.GroundCenter.Value, out float distance)
                || distance <= 0f)
            {
                m_diagnostics.RecordV2GroundSuffix(prefix + " outcome=no-goal-distance");
                return false;
            }

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
                    m_diagnostics.RecordV2GroundSuffix(
                        prefix + $" outcome=success distance={FormatCost(distance)} " +
                        $"steps={stepIndex}");
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
                    long localEscapeStart = AtdDiagnostics.Timestamp();
                    bool localEscapeValid = m_groundGraph.TryValidateLocalEscape(
                        sweptCenters, cursor.History,
                        m_cleanupCostScale,
                        out IReadOnlyCollection<string> cleanupKeys,
                        out float cleanupCost);
                    m_diagnostics.V2LocalEscapeTicks +=
                        AtdDiagnostics.ElapsedSince(localEscapeStart);
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
            m_diagnostics.RecordV2GroundSuffix(
                prefix + $" outcome=fallback remainingDistance={FormatCost(distance)}");
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
            long evaluationStart = AtdDiagnostics.Timestamp();
            AccessV2TransitionEvaluation evaluation = m_evaluator(
                null, transition, groundNode.History, null);
            m_diagnostics.V2TransitionEvaluationTicks +=
                AtdDiagnostics.ElapsedSince(evaluationStart);
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
                long handoffStart = AtdDiagnostics.Timestamp();
                seam = m_groundToVHandoffEvaluator(
                    state, groundCenter, candidate.ExpectedOperation,
                    nextHistory);
                m_diagnostics.V2HandoffEvaluationTicks +=
                    AtdDiagnostics.ElapsedSince(handoffStart);
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
                    && left.NonCrestLane == right.NonCrestLane
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

            public SearchKey(AccessV2BandState state)
            {
                m_state = state;
                m_groundCenter = null;
                m_isFixedGoalTerminal = false;
            }

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

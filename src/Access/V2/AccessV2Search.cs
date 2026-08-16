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
        public bool IsProjectedGroundEntry { get; }

        public bool IsGround => GroundCenter.HasValue;

        public AccessV2RouteStep(
            AccessV2BandState state,
            AccessV2Transition? transition,
            AccessV2HandoffCandidate? handoff,
            Tile2i? groundCenter,
            bool isProjectedGroundEntry = false)
        {
            State = state;
            Transition = transition;
            Handoff = handoff;
            GroundCenter = groundCenter;
            IsProjectedGroundEntry = isProjectedGroundEntry;
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
        public IReadOnlyCollection<Tile2i> TerminalGoalCenters { get; }
        public int VehicleWidth { get; }

        public AccessV2RouteData(
            IReadOnlyList<AccessV2BandState> states,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> generatedProfiles,
            AccessV2HandoffCandidate? handoff,
            IReadOnlyList<Tile2i> groundPath,
            IReadOnlyList<AccessV2RouteStep>? routeSteps = null,
            int vehicleWidth = 5,
            IReadOnlyCollection<Tile2i>? terminalGoalCenters = null)
        {
            States = states;
            GeneratedProfiles = generatedProfiles;
            Handoff = handoff;
            GroundPath = groundPath;
            RouteSteps = routeSteps ?? Array.Empty<AccessV2RouteStep>();
            VehicleWidth = Math.Max(1, vehicleWidth);
            TerminalGoalCenters = terminalGoalCenters
                ?? Array.Empty<Tile2i>();
        }
    }

    internal sealed class AccessV2SearchSession
    {
        private const int FixedNavigationPortalRadius = 8;

        private static readonly Tile2i[] s_groundToVTravelDirections =
        {
            new Tile2i(4, 0), new Tile2i(-4, 0),
            new Tile2i(0, 4), new Tile2i(0, -4),
        };

        private readonly Tile2i m_boundsMin;
        private readonly Tile2i m_boundsMax;
        private readonly IReadOnlyList<IReadOnlyList<AccessV2StartFrontage>>
            m_startTiers;
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
        private readonly Func<Tile2i, bool>? m_generatedVPrimeOriginValidator;
        private readonly Func<Tile2i, AccessHeightProfile?>?
            m_fixedProfileProvider;
        private readonly AccessV2GroundGraph? m_groundGraph;
        private readonly AccessV2FixedNavigationGraph?
            m_fixedNavigationGraph;
        private readonly HashSet<int>? m_groundSourceViableComponents;
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
        private readonly HashSet<SearchKey> m_exploredStartTierKeys =
            new HashSet<SearchKey>();
        private readonly HashSet<SearchKey> m_expandedKeys =
            new HashSet<SearchKey>();
        private readonly Dictionary<Tile2i, int> m_expandedVKeysByCenter =
            new Dictionary<Tile2i, int>();
        private AccessV2HandoffDominanceCache m_handoffDominance =
            new AccessV2HandoffDominanceCache();
        private readonly Dictionary<string, int> m_rejections =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private int m_queueCount;
        private int m_startTierIndex = -1;
        private bool m_startTierHitCostLimit;
        private int m_visited;
        private int m_maxHistoryOrigins;
        private int m_maxRayConstraints;
        private int m_handoffEvaluations;
        private int m_quickHandoffAccepts;

        public bool IsComplete { get; private set; }
        public int Visited => m_visited;
        public int Pending => m_queueCount;
        public int VehicleWidth { get; }
        internal Dictionary<string, int> LiveRejections => m_rejections;
        // Diagnostic-only hook used by the access search overlay.
        internal Action<Tile2i, int, bool, int?>? NodeExplored { get; set; }
        internal IReadOnlyList<AccessV2PotentialSample> PotentialSamples
            => m_potentialField?.GetDiagnosticSamples()
                ?? Array.Empty<AccessV2PotentialSample>();
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
                staggeredHandoffEvaluator = null,
            Func<Tile2i, AccessHeightProfile?>?
                fixedProfileProvider = null,
            AccessV2FixedNavigationGraph?
                fixedNavigationGraph = null,
            Func<Tile2i, bool>? generatedVPrimeOriginValidator = null,
            int vehicleWidth = 5)
        {
            m_boundsMin = boundsMin;
            m_boundsMax = boundsMax;
            m_startTiers = endpoints.StartTiers;
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
            m_generatedVPrimeOriginValidator =
                generatedVPrimeOriginValidator;
            m_fixedProfileProvider = fixedProfileProvider;
            m_groundGraph = groundGraph;
            m_fixedNavigationGraph = fixedNavigationGraph;
            m_groundSourceViableComponents =
                groundGraph != null
                && m_generatedOriginValidator != null
                    ? groundGraph.CollectGoalOrExitComponents(
                        tile => CanExitGroundComponentToV(
                            tile, m_generatedOriginValidator,
                            m_fixedProfileProvider))
                    : null;
            m_usefulHeightEnvelope = usefulHeightEnvelope;
            m_diagnostics = diagnostics ?? new AccessSearchDiagnostics();
            m_groundEscapePotentialField = potentialField != null
                && groundGraph != null
                    ? new AccessV2GroundEscapePotentialField(
                        groundGraph, potentialField,
                        tile => CanExitGroundComponentToV(
                            tile, m_generatedOriginValidator,
                            m_fixedProfileProvider))
                    : null;
            m_groundValidator = groundValidator;
            m_cleanupCostScale = cleanupCostScale;
            m_groundToVCenterSpokeCost = groundToVCenterSpokeCost;
            VehicleWidth = Math.Max(1, vehicleWidth);
            m_maxVisited = Math.Max(1, maxVisited);
            m_maxCost = maxCost;
            Result = Failed("SearchNotComplete");

            if (m_startTiers.Count == 0
                || m_startTiers.All(tier => tier.Count == 0))
            {
                CompleteFailure("NoWidth2StartCompanion");
                return;
            }
            if (m_handoffEvaluator == null)
            {
                CompleteFailure("V2GroundHandoffMissing");
                return;
            }

            if (!TryAdvanceStartTier())
                CompleteFailure(m_startTierHitCostLimit
                    ? "CostLimitExceeded"
                    : "V2NoFeasibleStart");
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
                m_exploredStartTierKeys.Add(currentKey);
                if (current.Cost > m_maxCost)
                {
                    CompleteFailure("CostLimitExceeded");
                    break;
                }

                Tile2i exploredCenter = current.GroundCenter
                    ?? AccessV2PotentialField.GetCanonicalCenter(current.State);
                int queueAge = Math.Max(
                    0, m_visited - current.EnqueuedAtVisited);
                bool firstExpansion = m_expandedKeys.Add(currentKey);
                if (firstExpansion)
                    m_diagnostics.V2LabelFirstExpansions++;
                else
                    m_diagnostics.V2LabelReexpansions++;
                m_diagnostics.V2ExpansionQueueAgeTotal += queueAge;
                m_diagnostics.V2ExpansionQueueAgeMax = Math.Max(
                    m_diagnostics.V2ExpansionQueueAgeMax, queueAge);

                int? exploredGroundHeight2 =
                    m_groundHeightProvider?.Invoke(exploredCenter);
                int exploredHeight2 = current.GroundCenter.HasValue
                    ? exploredGroundHeight2 ?? 0
                    : current.State.Band.Lane0.Center2;
                if (!current.GroundCenter.HasValue)
                {
                    if (current.IsGroundRelaunchedV)
                        m_diagnostics.V2GroundRelaunchedVExpansions++;
                    else
                        m_diagnostics.V2InitialVExpansions++;

                    if (firstExpansion)
                    {
                        m_expandedVKeysByCenter.TryGetValue(
                            exploredCenter, out int centerKeyCount);
                        if (centerKeyCount > 0)
                            m_diagnostics.V2CenterAliasedFirstExpansions++;
                        m_expandedVKeysByCenter[exploredCenter] =
                            centerKeyCount + 1;
                        m_diagnostics.V2UniqueExpansionCenters =
                            m_expandedVKeysByCenter.Count;
                    }

                    int depth2 = exploredGroundHeight2.HasValue
                        ? exploredGroundHeight2.Value - exploredHeight2
                        : 0;
                    if (depth2 > 0 && depth2 <= 10)
                    {
                        m_diagnostics.V2ShallowVExpansions++;
                        if (!firstExpansion)
                            m_diagnostics.V2ShallowVReexpansions++;
                        if (current.IsGroundRelaunchedV)
                            m_diagnostics.V2ShallowGroundRelaunchedVExpansions++;
                        m_diagnostics.V2ShallowVQueueAgeTotal += queueAge;
                        m_diagnostics.V2ShallowVQueueAgeMax = Math.Max(
                            m_diagnostics.V2ShallowVQueueAgeMax, queueAge);
                    }
                }

                m_visited++;
                NodeExplored?.Invoke(
                    exploredCenter,
                    exploredHeight2,
                    current.GroundCenter.HasValue,
                    exploredGroundHeight2);
                if (current.GroundCenter.HasValue)
                {
                    if (m_groundGraph != null
                        && m_groundGraph.IsGoal(current.GroundCenter.Value))
                    {
                        CompleteSuccess(current);
                        break;
                    }
                    long suffixStart = AtdDiagnostics.Timestamp();
                    if (!current.FixedNavigationAxis.HasValue
                        && !current.FixedNavigationPortalRoot.HasValue
                        && TryCompleteGroundSuffix(
                            current, out SearchNode? terminal))
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
                if (current.IsGroundToVAdapter)
                {
                    ExpandGroundToVAdapter(current);
                    m_diagnostics.V2BandExpansionTicks +=
                        AtdDiagnostics.ElapsedSince(bandStart);
                    continue;
                }
                EnqueueHandoffGoals(current);
                if (!current.RequiresGroundTransition)
                    Expand(current);
                m_diagnostics.V2BandExpansionTicks +=
                    AtdDiagnostics.ElapsedSince(bandStart);
            }

            if (!IsComplete && m_queueCount == 0)
            {
                if (m_startTierHitCostLimit)
                    CompleteFailure("CostLimitExceeded");
                else if (!TryAdvanceStartTier())
                    CompleteFailure(m_startTierHitCostLimit
                        ? "CostLimitExceeded"
                        : "NoPath");
            }
            else if (!IsComplete && m_visited >= m_maxVisited)
                CompleteFailure("VisitedLimit");
            return m_visited - visitedAtStart;
        }

        private bool TryAdvanceStartTier()
        {
            if (m_startTierIndex >= 0 && m_startTierHitCostLimit)
                return false;
            while (++m_startTierIndex < m_startTiers.Count)
            {
                m_diagnostics.V2StartTiersAttempted++;
                m_startTierHitCostLimit = false;
                m_queue.Clear();
                m_queueCount = 0;
                m_best.Clear();
                m_handoffDominance =
                    new AccessV2HandoffDominanceCache();
                int redundantSeedsBefore =
                    m_diagnostics.V2RedundantStartSeedsSkipped;
                IReadOnlyList<AccessV2StartFrontage> tier =
                    m_startTiers[m_startTierIndex];
                for (int index = 0; index < tier.Count; index++)
                    AddStart(tier[index]);
                if (m_queueCount > 0)
                    return true;
                if (m_diagnostics.V2RedundantStartSeedsSkipped
                    > redundantSeedsBefore)
                    m_diagnostics.V2RedundantStartTiersSkipped++;
                if (m_startTierHitCostLimit)
                    return false;
            }
            return false;
        }

        private void AddStart(AccessV2StartFrontage start)
        {
            if (start.IsSourceLaunch)
            {
                AddSourceLaunch(start);
                return;
            }

            AccessV2History history = AccessV2History.Empty;
            float cost = 0f;
            float traversalCost = 0f;
            float generatedWorkCost = 0f;
            float directWorkCost = 0f;
            float generatedFixedCost = 0f;
            float exteriorRayCost = 0f;
            float cleanupCost = 0f;

            var node = new SearchNode(
                start.State, history, cost,
                traversalCost,
                generatedWorkCost,
                directWorkCost,
                generatedFixedCost,
                exteriorRayCost,
                cleanupCost,
                null, null, null);
            Enqueue(node, isStartTierSeed: true);

            // A complete fixed two-lane source is already a physical Mega
            // frontage in projected-target space. Seed its canonical center
            // directly into G so an existing designation chain can provide
            // the entire route without generating replacement V work.
            if (m_groundGraph != null)
            {
                Tile2i center =
                    AccessV2PotentialField.GetCanonicalCenter(start.State);
                if (m_groundGraph.IsProjectedFixedGround(center)
                    && (m_groundSourceViableComponents == null
                        || m_groundGraph.IsInComponent(
                            center,
                            m_groundSourceViableComponents))
                    && (m_groundValidator == null
                        || m_groundValidator(center, history)))
                    Enqueue(new SearchNode(
                        start.State, history, cost,
                        traversalCost,
                        generatedWorkCost,
                        directWorkCost,
                        generatedFixedCost,
                        exteriorRayCost,
                        cleanupCost,
                        node, null, null,
                        groundCenter: center,
                        isProjectedGroundEntry: true,
                        fixedNavigationAxis:
                            GetFixedNavigationAxis(
                                start.State, center)),
                        isStartTierSeed: true);
            }
        }

        private void AddSourceLaunch(AccessV2StartFrontage start)
        {
            AccessV2History history = AccessV2History.Empty;
            float cost = 0f;
            float traversalCost = 0f;
            float generatedWorkCost = 0f;
            float directWorkCost = 0f;
            float generatedFixedCost = 0f;
            float exteriorRayCost = 0f;
            float cleanupCost = 0f;

            if (start.InitialTransition != null)
            {
                AccessV2Transition initial = start.InitialTransition;
                if (!IsTransitionWithinUsefulHeightEnvelope(
                        m_usefulHeightEnvelope, initial,
                        out string envelopeRejection))
                {
                    Reject(envelopeRejection);
                    return;
                }
                if (!history.TryValidateApply(
                        initial, out string geometryReason))
                {
                    Reject(geometryReason);
                    return;
                }
                long evaluationStart = AtdDiagnostics.Timestamp();
                AccessV2TransitionEvaluation evaluation = m_evaluator(
                    null, initial, history, start.FixedSeedOrigin);
                m_diagnostics.V2TransitionEvaluationTicks +=
                    AtdDiagnostics.ElapsedSince(evaluationStart);
                if (!evaluation.IsValid)
                {
                    Reject("SourceLaunchInitial:" +
                        evaluation.RejectionReason);
                    return;
                }
                history = history.ApplyValidated(
                    initial,
                    evaluation.RayConstraints,
                    evaluation.CleanupKeys,
                    GetFixedSafetyExemptOrigins(
                        start.State, start.FixedSeedOrigin));
                cost = evaluation.TotalCost;
                traversalCost = evaluation.TraversalCost;
                generatedWorkCost = evaluation.GeneratedWorkCost;
                directWorkCost = evaluation.DirectWorkCost;
                generatedFixedCost = evaluation.GeneratedFixedCost;
                exteriorRayCost = evaluation.ExteriorRayCost;
                cleanupCost = evaluation.CleanupCost;
            }

            var initialNode = new SearchNode(
                start.State, history, cost,
                traversalCost,
                generatedWorkCost,
                directWorkCost,
                generatedFixedCost,
                exteriorRayCost,
                cleanupCost,
                null, null, null);

            AccessV2Transition successor = start.LaunchSuccessor!;
            if (!AccessV2Geometry.IsInsideBounds(
                    successor, m_boundsMin, m_boundsMax))
            {
                Reject("HorizontalBounds");
                return;
            }
            if (!IsTransitionWithinUsefulHeightEnvelope(
                    m_usefulHeightEnvelope, successor,
                    out string successorEnvelopeRejection))
            {
                Reject(successorEnvelopeRejection);
                return;
            }
            if (successor.Delta.Count > 0
                && !history.TryValidateApply(
                    successor, out string successorGeometryReason))
            {
                Reject(successorGeometryReason);
                return;
            }

            if (start.InitialTransition == null
                && successor.Delta.Count == 0)
            {
                if (m_groundGraph == null)
                {
                    Reject("SourceLaunchFixedGroundGraphMissing");
                    return;
                }
                Tile2i center =
                    AccessV2PotentialField.GetCanonicalCenter(start.State);
                bool projected =
                    m_groundGraph.IsProjectedFixedGround(center);
                bool viable = m_groundSourceViableComponents == null
                    || m_groundGraph.IsInComponent(
                        center,
                        m_groundSourceViableComponents);
                bool pathable = m_groundValidator == null
                    || m_groundValidator(center, history);
                if (!projected || !viable || !pathable)
                {
                    Reject(!projected
                        ? "SourceLaunchProjectedGroundMissing"
                        : !viable
                            ? "SourceLaunchProjectedComponentDead"
                            : "SourceLaunchProjectedCenterBlocked");
                    return;
                }
                Enqueue(new SearchNode(
                    start.State, history, cost,
                    traversalCost,
                    generatedWorkCost,
                    directWorkCost,
                    generatedFixedCost,
                    exteriorRayCost,
                    cleanupCost,
                    initialNode, null, null,
                    groundCenter: center,
                    isProjectedGroundEntry: true,
                    fixedNavigationAxis:
                        GetFixedNavigationAxis(
                            start.State, center)),
                    isStartTierSeed: true);
                return;
            }

            long successorEvaluationStart = AtdDiagnostics.Timestamp();
            AccessV2TransitionEvaluation successorEvaluation = m_evaluator(
                start.State, successor, history, start.FixedSeedOrigin);
            m_diagnostics.V2TransitionEvaluationTicks +=
                AtdDiagnostics.ElapsedSince(successorEvaluationStart);
            if (!successorEvaluation.IsValid)
            {
                Reject("SourceLaunchSuccessor:" +
                    successorEvaluation.RejectionReason);
                return;
            }

            float nextCost = cost + successorEvaluation.TotalCost;
            if (nextCost > m_maxCost)
            {
                m_startTierHitCostLimit = true;
                Reject("CostLimitExceeded");
                return;
            }
            AccessV2History nextHistory = history.ApplyValidated(
                successor,
                successorEvaluation.RayConstraints,
                successorEvaluation.CleanupKeys,
                GetFixedSafetyExemptOrigins(
                    start.State, start.FixedSeedOrigin));
            Enqueue(new SearchNode(
                successor.Next, nextHistory, nextCost,
                traversalCost + successorEvaluation.TraversalCost,
                generatedWorkCost + successorEvaluation.GeneratedWorkCost,
                directWorkCost + successorEvaluation.DirectWorkCost,
                generatedFixedCost + successorEvaluation.GeneratedFixedCost,
                exteriorRayCost + successorEvaluation.ExteriorRayCost,
                cleanupCost + successorEvaluation.CleanupCost,
                initialNode, successor, null,
                requiresGroundTransition:
                    successorEvaluation.RequiresGroundTransition),
                isStartTierSeed: true);
        }

        private AccessV2TravelAxis? GetFixedNavigationAxis(
            AccessV2BandState state,
            Tile2i center)
            => m_fixedNavigationGraph != null
                && m_fixedNavigationGraph.ContainsNode(
                    state.Axis, center)
                    ? state.Axis
                    : (AccessV2TravelAxis?)null;

        private IReadOnlyCollection<Tile2i> GetFixedSafetyExemptOrigins(
            AccessV2BandState state,
            Tile2i? fixedSeedOrigin = null)
        {
            var result = new HashSet<Tile2i>();
            if (fixedSeedOrigin.HasValue)
                result.Add(fixedSeedOrigin.Value);
            for (int lane = 0; lane < 2; lane++)
            {
                Tile2i origin = state.GetLaneOrigin(lane);
                if (m_fixedProfileProvider?.Invoke(origin).HasValue == true)
                    result.Add(origin);
            }
            return result;
        }

        private IReadOnlyList<AccessV2TravelAxis>
            GetFixedNavigationEntryAxes(Tile2i center)
            => m_fixedNavigationGraph != null
                ? m_fixedNavigationGraph.GetNodeAxes(center)
                : Array.Empty<AccessV2TravelAxis>();

        private Tile2i? GetFixedNavigationPortalRoot(
            Tile2i center)
            => m_groundGraph != null
                && m_groundGraph.IsProjectedFixedGround(center)
                    ? center
                    : (Tile2i?)null;

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
            bool traceVPrimeAdapter =
                current.IsGroundToVAdapter
                && IsVPrimeBand(current.State.Band);
            void Trace(string outcome)
            {
                if (traceVPrimeAdapter)
                    m_diagnostics.RecordV2VPrimeAdapter(
                        $"relax from={current.State.Anchor} " +
                        $"entry={current.State.EntryDirection} " +
                        $"g={FormatCost(current.Cost)} " +
                        $"next={transition.Next.Anchor} " +
                        $"lane0={FormatProfile2(
                            transition.Next.Band.Lane0)} " +
                        $"lane1={FormatProfile2(
                            transition.Next.Band.Lane1)} " +
                        $"outcome={outcome}");
                if (!traceStartSuccessor)
                    return;
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
                    current.Cost + traversalLowerBound,
                    current.History
                        .WillRequireStrictSelfDisruptionChecks(
                            transition.Next.EntryDirection),
                    GetPotentialOwnerForV(
                        current, transition.Next, transition)))
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
            Tile2i? connectedFixedOrigin = null;
            if (transition.ScoreOnlyGeneratedExteriorRays)
            {
                for (int lane = 0; lane < 2; lane++)
                {
                    Tile2i origin = current.State.GetLaneOrigin(lane);
                    if (m_fixedProfileProvider?.Invoke(origin).HasValue == true)
                    {
                        connectedFixedOrigin = origin;
                        break;
                    }
                }
            }
            AccessV2TransitionEvaluation evaluation = m_evaluator(
                current.State, transition, current.History,
                connectedFixedOrigin);
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
                m_startTierHitCostLimit = true;
                Reject("CostLimitExceeded");
                Trace("reject:CostLimitExceeded " +
                    $"nextCost={FormatCost(nextCost)}");
                return;
            }
            if (IsVBandCostKnownNoWorse(
                    transition.Next, nextCost,
                    current.History
                        .WillRequireStrictSelfDisruptionChecks(
                            transition.Next.EntryDirection),
                    GetPotentialOwnerForV(
                        current, transition.Next, transition)))
            {
                m_diagnostics.V2ExactLabelDominancePrunes++;
                Trace("prune:ExactLabelDominance " +
                    $"step={FormatCost(evaluation.TotalCost)} " +
                    $"nextCost={FormatCost(nextCost)}");
                return;
            }
            AccessV2History nextHistory = current.History.ApplyValidated(
                transition,
                evaluation.RayConstraints,
                evaluation.CleanupKeys,
                GetFixedSafetyExemptOrigins(
                    current.State, connectedFixedOrigin));
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
                    evaluation.RequiresGroundTransition,
                isGroundToVAdapter:
                    transition.Next.Band.Kind
                        == AccessV2BandProfileKind
                            .MechanicallyValidDeferred);
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
            float candidateCost,
            bool strictSelfDisruption,
            AccessV2PotentialOwner potentialOwner)
            => m_best.TryGetValue(
                    new SearchKey(
                        state, strictSelfDisruption, potentialOwner),
                    out float knownCost)
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
                if (cost > m_maxCost)
                {
                    m_startTierHitCostLimit = true;
                    continue;
                }
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
                    if (m_groundGraph != null)
                    {
                        AccessV2PotentialOwner handoffOwner =
                            current.PotentialOwner.Advance(
                                m_groundGraph,
                                AccessV2PotentialField.GetCanonicalCenter(
                                    current.State),
                                entry);
                        if (!handoffOwner.CanReturnTo(
                                m_groundGraph, entry))
                        {
                            Reject("SameComponentReturnBeforeVCommitment");
                            continue;
                        }
                    }
                    IReadOnlyList<AccessV2TravelAxis> axes =
                        GetFixedNavigationEntryAxes(entry);
                    if (axes.Count == 0)
                        EnqueueEntry(null);
                    else
                        for (int axisIndex = 0;
                            axisIndex < axes.Count;
                            axisIndex++)
                            EnqueueEntry(axes[axisIndex]);

                    void EnqueueEntry(
                        AccessV2TravelAxis? axis)
                        => Enqueue(new SearchNode(
                            current.State, handoffHistory, cost,
                            current.TraversalCost
                                + handoff.CenterSpokeCost,
                            current.GeneratedWorkCost,
                            current.DirectWorkCost,
                            current.GeneratedFixedCost,
                            current.ExteriorRayCost,
                            current.CleanupCost
                                + handoff.CleanupCost,
                            current, null, handoff, entry,
                            fixedNavigationAxis: axis,
                            fixedNavigationPortalRoot:
                                axis.HasValue
                                    ? null
                                    : GetFixedNavigationPortalRoot(
                                        entry)));
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
                    m_startTierHitCostLimit = true;
                    TraceTerminal($"transition anchor={transition.Next.Anchor} reject=max-cost");
                    return false;
                }
                AccessV2History history = parent.History.ApplyValidated(
                    terminalTransition,
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
                {
                    m_startTierHitCostLimit = true;
                    return;
                }
                AccessV2History history =
                    parent.History.ApplyCleanupKeys(handoff.CleanupKeys);
                for (int index = 0;
                    index < handoff.GroundEntryCenters.Count;
                    index++)
                {
                    Tile2i entry = handoff.GroundEntryCenters[index];
                    IReadOnlyList<AccessV2TravelAxis> axes =
                        GetFixedNavigationEntryAxes(entry);
                    if (axes.Count == 0)
                        EnqueueEntry(null);
                    else
                        for (int axisIndex = 0;
                            axisIndex < axes.Count;
                            axisIndex++)
                            EnqueueEntry(axes[axisIndex]);

                    void EnqueueEntry(
                        AccessV2TravelAxis? axis)
                        => Enqueue(new SearchNode(
                            parent.State, history, cost,
                            parent.TraversalCost
                                + handoff.CenterSpokeCost,
                            parent.GeneratedWorkCost,
                            parent.DirectWorkCost,
                            parent.GeneratedFixedCost,
                            parent.ExteriorRayCost,
                            parent.CleanupCost
                                + handoff.CleanupCost,
                            parent, null, handoff, entry,
                            fixedNavigationAxis: axis,
                            fixedNavigationPortalRoot:
                                axis.HasValue
                                    ? null
                                    : GetFixedNavigationPortalRoot(
                                        entry)));
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
            var sparseDirections = new HashSet<Tile2i>();
            if (m_fixedNavigationGraph != null
                && current.FixedNavigationAxis.HasValue)
            {
                IReadOnlyList<AccessV2FixedNavigationMove> moves =
                    m_fixedNavigationGraph.EnumerateMoves(
                        current.FixedNavigationAxis.Value, from);
                for (int moveIndex = 0;
                    moveIndex < moves.Count;
                    moveIndex++)
                {
                    AccessV2FixedNavigationMove move = moves[moveIndex];
                    IReadOnlyList<Tile2i> path = move.ExactCenterPath;
                    if (path.Count < 2
                        || path[0] != from
                        || (m_groundValidator != null
                            && path.Skip(1).Any(center =>
                                !m_groundValidator(
                                    center, current.History))))
                        continue;
                    sparseDirections.Add(new Tile2i(
                        Math.Sign(move.Center.X - from.X),
                        Math.Sign(move.Center.Y - from.Y)));
                    SearchNode cursor = current;
                    for (int pathIndex = 1;
                        pathIndex < path.Count;
                        pathIndex++)
                    {
                        Tile2i center = path[pathIndex];
                        float stepCost =
                            AccessV2GroundGraph.GetStepCost(
                                path[pathIndex - 1], center);
                        cursor = new SearchNode(
                            current.State,
                            current.History,
                            cursor.Cost + stepCost,
                            cursor.TraversalCost + stepCost,
                            current.GeneratedWorkCost,
                            current.DirectWorkCost,
                            current.GeneratedFixedCost,
                            current.ExteriorRayCost,
                            current.CleanupCost,
                            cursor, null, null,
                            groundCenter: center,
                            fixedNavigationAxis:
                                pathIndex == path.Count - 1
                                    ? move.Axis
                                    : current.FixedNavigationAxis);
                    }
                    if (cursor.Cost <= m_maxCost)
                        Enqueue(cursor);
                    else
                        m_startTierHitCostLimit = true;
                }

                IReadOnlyList<AccessV2FixedNavigationPortal> portals =
                    m_fixedNavigationGraph.EnumerateExitPortals(
                        current.FixedNavigationAxis.Value, from);
                for (int portalIndex = 0;
                    portalIndex < portals.Count;
                    portalIndex++)
                {
                    AccessV2FixedNavigationPortal portal =
                        portals[portalIndex];
                    IReadOnlyList<Tile2i> path =
                        portal.ExactCenterPath;
                    var requiredCenters = new HashSet<Tile2i>();
                    for (int pathIndex = 1;
                        pathIndex < path.Count;
                        pathIndex++)
                        requiredCenters.UnionWith(
                            AccessV2GroundGraph.GetSweptCenters(
                                path[pathIndex - 1],
                                path[pathIndex]));
                    if (m_groundValidator != null
                        && requiredCenters.Any(center =>
                            !m_groundValidator(
                                center, current.History)))
                        continue;
                    if (!m_groundGraph.TryValidateLocalEscape(
                            requiredCenters,
                            current.History,
                            m_cleanupCostScale,
                            out IReadOnlyCollection<string> cleanupKeys,
                            out float cleanupCost))
                        continue;
                    AccessV2History portalHistory =
                        current.History.ApplyCleanupKeys(cleanupKeys);
                    SearchNode cursor = current;
                    for (int pathIndex = 1;
                        pathIndex < path.Count;
                        pathIndex++)
                    {
                        Tile2i center = path[pathIndex];
                        float stepCost =
                            AccessV2GroundGraph.GetStepCost(
                                path[pathIndex - 1], center);
                        bool isLast =
                            pathIndex == path.Count - 1;
                        cursor = new SearchNode(
                            current.State,
                            portalHistory,
                            cursor.Cost + stepCost
                                + (isLast ? cleanupCost : 0f),
                            cursor.TraversalCost + stepCost,
                            current.GeneratedWorkCost,
                            current.DirectWorkCost,
                            current.GeneratedFixedCost,
                            current.ExteriorRayCost,
                            cursor.CleanupCost
                                + (isLast ? cleanupCost : 0f),
                            cursor, null, null,
                            groundCenter: center,
                            fixedNavigationAxis:
                                isLast
                                    ? null
                                    : current.FixedNavigationAxis);
                    }
                    if (cursor.Cost <= m_maxCost)
                        Enqueue(cursor);
                    else
                        m_startTierHitCostLimit = true;
                }
            }
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
                if (sparseDirections.Contains(new Tile2i(
                        directions[index].X,
                        directions[index].Y)))
                    continue;
                if (incomingX * directions[index].X
                    + incomingY * directions[index].Y < 0)
                    continue;
                Tile2i next = from + directions[index];
                if (!m_groundGraph.CanTraverse(from, next))
                    continue;
                bool nextIsProjected =
                    m_groundGraph.IsProjectedFixedGround(next);
                if (current.FixedNavigationAxis.HasValue
                    && nextIsProjected)
                    continue;
                Tile2i? portalRoot =
                    current.FixedNavigationPortalRoot;
                if (nextIsProjected
                    && (portalRoot.HasValue
                        || !m_groundGraph.IsProjectedFixedGround(from)))
                {
                    portalRoot ??= from;
                    if (Math.Max(
                            Math.Abs(next.X - portalRoot.Value.X),
                            Math.Abs(next.Y - portalRoot.Value.Y))
                        > FixedNavigationPortalRadius)
                        continue;
                }
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
                if (nextCost > m_maxCost)
                {
                    m_startTierHitCostLimit = true;
                    continue;
                }
                IReadOnlyList<AccessV2TravelAxis> nodeAxes =
                    m_fixedNavigationGraph != null
                        && nextIsProjected
                            ? m_fixedNavigationGraph.GetNodeAxes(next)
                            : Array.Empty<AccessV2TravelAxis>();
                if (nodeAxes.Count > 0)
                {
                    for (int axisIndex = 0;
                        axisIndex < nodeAxes.Count;
                        axisIndex++)
                        EnqueueGroundStep(
                            nodeAxes[axisIndex], null);
                }
                else
                    EnqueueGroundStep(
                        nextIsProjected
                            ? current.FixedNavigationAxis
                            : null,
                        nextIsProjected ? portalRoot : null);

                void EnqueueGroundStep(
                    AccessV2TravelAxis? fixedNavigationAxis,
                    Tile2i? fixedNavigationPortalRoot)
                    => Enqueue(new SearchNode(
                        current.State, nextHistory, nextCost,
                        current.TraversalCost + stepCost,
                        current.GeneratedWorkCost,
                        current.DirectWorkCost,
                        current.GeneratedFixedCost,
                        current.ExteriorRayCost,
                        current.CleanupCost + cleanupCost,
                        current, null, null, next,
                        fixedNavigationAxis:
                            fixedNavigationAxis,
                        fixedNavigationPortalRoot:
                            fixedNavigationPortalRoot));
            }

            if (HasCanonicalGroundToVLaunchPosition(from))
            {
                m_diagnostics.V2GroundToVCalls++;
                long groundToVStart = AtdDiagnostics.Timestamp();
                ExpandGroundToV(current);
                m_diagnostics.V2GroundToVTicks +=
                    AtdDiagnostics.ElapsedSince(groundToVStart);
            }
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
                    {
                        m_startTierHitCostLimit = true;
                        continue;
                    }
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
            for (int directionIndex = 0;
                directionIndex < s_groundToVTravelDirections.Length;
                directionIndex++)
            {
                Tile2i travel =
                    s_groundToVTravelDirections[directionIndex];
                if (!CanUseCanonicalGroundToVLaunchPosition(
                        ground, travel))
                    continue;
                AccessV2TravelAxis axis = travel.X != 0
                    ? AccessV2TravelAxis.X
                    : AccessV2TravelAxis.Y;
                foreach (Tile2i anchor in
                    EnumerateGroundToVBandAnchors(
                        ground, travel,
                        includeOutwardFringe:
                            m_groundGraph.IsProjectedFixedGround(ground)))
                {
                Tile2i companion = AccessV2Geometry.Add(
                    anchor,
                    AccessV2BandProfile.GetLaneDirection(axis));
                m_diagnostics.V2GroundToVAnchorCandidates++;

                // This is the in-tower-area gate. G remains freely traversable
                // outside the generated-origin domain, but no reverse handoff
                // work may be considered there.
                bool directBandResolvable =
                    AreGroundToVBandOriginsResolvable(
                        anchor, axis,
                        m_generatedOriginValidator,
                        origin => m_fixedProfileProvider?.Invoke(
                            origin).HasValue == true);
                bool hasFixedAdapter =
                    TryCreateFixedGroundToVAdapter(
                        anchor, axis, travel,
                        m_fixedProfileProvider,
                        out AccessV2Transition fixedAdapter)
                    && HasGeneratedGroundToVSuccessor(
                        fixedAdapter.Next,
                        m_fixedProfileProvider,
                        m_generatedOriginValidator);
                if (!directBandResolvable && !hasFixedAdapter)
                {
                    m_diagnostics.V2GroundToVTowerAreaRejects++;
                    continue;
                }

                if (hasFixedAdapter)
                    TryEmitFixedGroundAdapter(
                        current, ground, fixedAdapter);

                if (!directBandResolvable)
                    continue;

                float terrainHeight = m_preciseTerrainHeightProvider?.Invoke(
                    ground) ?? m_terrainCenterHeightProvider(ground) / 2f;

                if (Math.Abs(terrainHeight - Math.Round(terrainHeight))
                    <= 0.0001f)
                {
                    foreach (GroundToVProfileCandidate candidate in
                        EnumerateDirectLevelingProfiles(
                            (int)Math.Round(terrainHeight), axis, travel))
                        TryEmitGroundToV(
                            current, ground, anchor, axis, travel,
                            candidate, directLeveling: true);
                }

                GroundToVProfileCandidate[] profileCandidates =
                    EnumerateGroundToVBandProfiles(
                        anchor, terrainHeight, axis, travel,
                        m_fixedProfileProvider,
                        m_generatedVPrimeOriginValidator)
                    .ToArray();
                bool catalog0 =
                    m_generatedVPrimeOriginValidator?.Invoke(anchor) == true;
                bool catalog1 =
                    m_generatedVPrimeOriginValidator?.Invoke(companion) == true;
                if ((catalog0 || catalog1)
                    && !profileCandidates.Any(candidate =>
                        AccessV2BandProfile.IsCanonicalVPrime(
                            candidate.Lane0)
                        || AccessV2BandProfile.IsCanonicalVPrime(
                            candidate.Lane1)))
                    m_diagnostics.RecordV2VPrimeAdapter(
                        $"no-vprime ground={ground} anchor={anchor} " +
                        $"entry={travel} catalog={catalog0}/{catalog1} " +
                        $"profiles={profileCandidates.Length} " +
                        $"fixed={FormatFixedConstraintContext(
                            anchor, companion)}");
                foreach (GroundToVProfileCandidate candidate in
                    profileCandidates)
                    TryEmitGroundToV(
                        current, ground, anchor, axis, travel,
                        candidate, directLeveling: false);
                }
            }

            string FormatFixedConstraintContext(
                Tile2i anchor, Tile2i companion)
            {
                if (m_fixedProfileProvider == null)
                    return "none";
                var origins = new HashSet<Tile2i>();
                foreach (Tile2i lane in new[] { anchor, companion })
                {
                    for (int dy = -4; dy <= 4; dy += 4)
                        for (int dx = -4; dx <= 4; dx += 4)
                            origins.Add(new Tile2i(
                                lane.X + dx, lane.Y + dy));
                }
                return string.Join(
                    ",",
                    origins.OrderBy(origin => origin.X)
                        .ThenBy(origin => origin.Y)
                        .Select(origin =>
                        {
                            AccessHeightProfile? profile =
                                m_fixedProfileProvider(origin);
                            return profile.HasValue
                                ? $"{origin}:{FormatProfile2(profile.Value)}"
                                : null;
                        })
                        .Where(item => item != null));
            }
        }

        private bool TryEmitFixedGroundAdapter(
            SearchNode groundNode,
            Tile2i groundCenter,
            AccessV2Transition adapter)
        {
            if (m_groundToVHandoffEvaluator == null
                || !AccessV2Geometry.IsInsideBounds(
                    adapter.Next, m_boundsMin, m_boundsMax))
                return false;
            if (IsVBandCostKnownNoWorse(
                    adapter.Next, groundNode.Cost,
                    strictSelfDisruption: false,
                    GetPotentialOwnerForV(
                        groundNode, adapter.Next, adapter)))
            {
                m_diagnostics.V2GroundToVCacheHits++;
                return true;
            }

            m_diagnostics.V2GroundToVSeedCalls++;
            AccessV2HandoffCandidate? seam =
                m_groundToVHandoffEvaluator(
                    adapter.Next, groundCenter,
                    AccessHandoffOperation.Leveling,
                    groundNode.History);
            m_handoffEvaluations++;
            m_diagnostics.V2HandoffEvaluations++;
            if (seam == null)
                return false;

            float cost = groundNode.Cost + seam.TotalCost;
            if (cost > m_maxCost)
            {
                m_startTierHitCostLimit = true;
                return false;
            }
            AccessV2History history = groundNode.History
                .ResetDirectionScope()
                .ApplyCleanupKeys(seam.CleanupKeys);
            bool enqueued = Enqueue(new SearchNode(
                adapter.Next, history, cost,
                groundNode.TraversalCost + seam.CenterSpokeCost,
                groundNode.GeneratedWorkCost,
                groundNode.DirectWorkCost,
                groundNode.GeneratedFixedCost,
                groundNode.ExteriorRayCost,
                groundNode.CleanupCost + seam.CleanupCost,
                groundNode, adapter, seam,
                isGroundToVAdapter: true));
            if (!enqueued)
                return true;
            m_diagnostics.V2GroundToVSeedExtensions++;
            m_diagnostics.V2GroundToVCacheInsertions++;
            return true;
        }

        private void ExpandGroundToVAdapter(SearchNode adapterNode)
        {
            bool isVPrime = IsVPrimeBand(adapterNode.State.Band);
            int resolvedCount = 0;
            foreach (AccessV2Transition candidate in
                AccessV2Geometry.EnumerateStraight(adapterNode.State))
            {
                if (!TryResolveGroundToVTransition(
                        candidate,
                        m_fixedProfileProvider,
                        m_generatedOriginValidator,
                        out AccessV2Transition resolved))
                    continue;
                resolvedCount++;
                if (isVPrime)
                    m_diagnostics.RecordV2VPrimeAdapter(
                        $"expand anchor={adapterNode.State.Anchor} " +
                        $"entry={adapterNode.State.EntryDirection} " +
                        $"g={FormatCost(adapterNode.Cost)} " +
                        $"successor={resolved.Next.Anchor} " +
                        $"delta={resolved.Delta.Count}");
                TryRelax(adapterNode, resolved);
            }
            if (adapterNode.Parent?.IsGroundToVAdapter != true)
            {
                foreach (AccessV2Transition candidate in
                    EnumerateVPrimeAdapterExtensions(
                        adapterNode.State))
                {
                    if (!TryResolveGroundToVTransition(
                            candidate,
                            m_fixedProfileProvider,
                            m_generatedOriginValidator,
                            out AccessV2Transition resolved))
                        continue;
                    resolvedCount++;
                    if (isVPrime)
                        m_diagnostics.RecordV2VPrimeAdapter(
                            $"expand-pair anchor={adapterNode.State.Anchor} " +
                            $"entry={adapterNode.State.EntryDirection} " +
                            $"g={FormatCost(adapterNode.Cost)} " +
                            $"successor={resolved.Next.Anchor} " +
                            $"lane0={FormatProfile2(
                                resolved.Next.Band.Lane0)} " +
                            $"lane1={FormatProfile2(
                                resolved.Next.Band.Lane1)}");
                    TryRelax(adapterNode, resolved);
                }
            }
            if (isVPrime && resolvedCount == 0)
                m_diagnostics.RecordV2VPrimeAdapter(
                    $"expand anchor={adapterNode.State.Anchor} " +
                    $"entry={adapterNode.State.EntryDirection} " +
                    $"g={FormatCost(adapterNode.Cost)} successors=0");
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
            AccessV2History segmentHistory =
                groundNode.History.ResetDirectionScope();
            bool isVPrime = AccessV2BandProfile.IsCanonicalVPrime(
                    candidate.Lane0)
                || AccessV2BandProfile.IsCanonicalVPrime(
                    candidate.Lane1);
            m_diagnostics.V2GroundToVSeedCalls++;
            m_diagnostics.V2GroundToVProfileCandidates++;
            if (!AccessV2BandProfile.TryCreate(
                    axis, candidate.Lane0, candidate.Lane1,
                    includeDeferred: true,
                    out AccessV2BandProfile band, out _))
                return false;
            var state = new AccessV2BandState(anchor, band, travel);
            if (IsVBandCostKnownNoWorse(
                    state, groundNode.Cost,
                    strictSelfDisruption: false,
                    GetPotentialOwnerForV(
                        groundNode, state, transition: null)))
            {
                m_diagnostics.V2GroundToVCacheHits++;
                return true;
            }
            if (!AccessV2Geometry.IsInsideBounds(
                    state, m_boundsMin, m_boundsMax))
                return false;
            if (!TryResolveGroundToVTransition(
                    state, m_fixedProfileProvider,
                    m_generatedOriginValidator,
                    out AccessV2Transition transition))
            {
                if (isVPrime)
                    m_diagnostics.RecordV2VPrimeAdapter(
                        $"reject anchor={anchor} stage=resolve");
                return false;
            }
            if (!IsTransitionWithinUsefulHeightEnvelope(
                    m_usefulHeightEnvelope, transition,
                    out string envelopeRejection))
            {
                Reject(envelopeRejection);
                return false;
            }
            if (!segmentHistory.TryApply(
                    transition, out _, out string historyReason))
            {
                Reject(historyReason);
                return false;
            }
            m_diagnostics.V2GroundToVSeedExtensions++;
            long evaluationStart = AtdDiagnostics.Timestamp();
            AccessV2TransitionEvaluation evaluation = m_evaluator(
                null, transition, segmentHistory,
                transition.LocalContextOrigins.Count > 0
                    ? (Tile2i?)transition.LocalContextOrigins.First()
                    : null);
            m_diagnostics.V2TransitionEvaluationTicks +=
                AtdDiagnostics.ElapsedSince(evaluationStart);
            if (!evaluation.IsValid || evaluation.RequiresGroundTransition)
            {
                if (!evaluation.IsValid)
                    Reject(evaluation.RejectionReason);
                if (isVPrime)
                    m_diagnostics.RecordV2VPrimeAdapter(
                        $"reject anchor={anchor} stage=evaluate reason=" +
                        (evaluation.IsValid
                            ? "RequiresGroundTransition"
                            : evaluation.RejectionReason));
                return false;
            }
            AccessV2History nextHistory = segmentHistory.ApplyValidated(
                transition,
                evaluation.RayConstraints,
                evaluation.CleanupKeys);

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
            {
                if (isVPrime)
                    m_diagnostics.RecordV2VPrimeAdapter(
                        $"reject anchor={anchor} stage=seam");
                return false;
            }

            float cost = groundNode.Cost + seam.TotalCost
                + evaluation.TotalCost;
            if (cost > m_maxCost)
            {
                m_startTierHitCostLimit = true;
                return false;
            }
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
                groundNode, transition, seam,
                isGroundToVAdapter:
                    band.Kind
                        == AccessV2BandProfileKind.MechanicallyValidDeferred);
            bool enqueued = Enqueue(node);
            if (!enqueued)
                return true;
            if (isVPrime)
            {
                float heuristic = m_potentialField != null
                    ? Math.Max(
                        0f, m_potentialField.GetPotential(state))
                    : m_heuristicEvaluator != null
                        ? Math.Max(0f, m_heuristicEvaluator(state))
                        : 0f;
                m_diagnostics.RecordV2VPrimeAdapter(
                    $"accepted ground={groundCenter} anchor={anchor} " +
                    $"entry={travel} g={FormatCost(cost)} " +
                    $"h={FormatCost(heuristic)} " +
                    $"f={FormatCost(cost + heuristic)} " +
                    $"delta={transition.Delta.Count} " +
                    $"seam={FormatCost(seam.TotalCost)} " +
                    $"lane0={FormatProfile2(candidate.Lane0)} " +
                    $"lane1={FormatProfile2(candidate.Lane1)}");
            }
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

        private static bool IsVPrimeBand(AccessV2BandProfile band)
            => AccessV2BandProfile.IsCanonicalVPrime(band.Lane0)
                || AccessV2BandProfile.IsCanonicalVPrime(band.Lane1);

        /// <summary>
        /// A G-to-V handoff starts only from a captured vehicle center on the
        /// canonical four-tile grid edge for its travel direction. Within that
        /// restricted launch set, leveling, rough work, fixed adapters, and
        /// V-prime adapters remain independently eligible.
        /// </summary>
        internal static bool CanUseCanonicalGroundToVLaunchPosition(
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

        private static bool HasCanonicalGroundToVLaunchPosition(
            Tile2i ground)
            => (ground.X & 3) == 0 || (ground.Y & 3) == 0;

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

        internal static bool CanLaunchGroundToV(
            Tile2i ground,
            Func<Tile2i, bool>? originValidator)
            => CanLaunchGroundToV(
                ground, originValidator,
                fixedOriginValidator: null);

        internal static bool CanLaunchGroundToV(
            Tile2i ground,
            Func<Tile2i, bool>? generatedOriginValidator,
            Func<Tile2i, bool>? fixedOriginValidator)
        {
            for (int directionIndex = 0;
                directionIndex < s_groundToVTravelDirections.Length;
                directionIndex++)
            {
                Tile2i travel =
                    s_groundToVTravelDirections[directionIndex];
                if (!CanUseCanonicalGroundToVLaunchPosition(
                        ground, travel))
                    continue;
                AccessV2TravelAxis axis = travel.X != 0
                    ? AccessV2TravelAxis.X
                    : AccessV2TravelAxis.Y;
                Tile2i anchor = GetGroundToVBandAnchor(
                    ground, travel);
                if (AreGroundToVBandOriginsResolvable(
                        anchor, axis,
                        generatedOriginValidator,
                        fixedOriginValidator))
                    return true;
            }
            return false;
        }

        internal static bool CanExitGroundComponentToV(
            Tile2i ground,
            Func<Tile2i, bool>? generatedOriginValidator,
            Func<Tile2i, AccessHeightProfile?>? fixedProfileProvider)
        {
            for (int directionIndex = 0;
                directionIndex < s_groundToVTravelDirections.Length;
                directionIndex++)
            {
                Tile2i travel =
                    s_groundToVTravelDirections[directionIndex];
                if (!CanUseCanonicalGroundToVLaunchPosition(
                        ground, travel))
                    continue;
                AccessV2TravelAxis axis = travel.X != 0
                    ? AccessV2TravelAxis.X
                    : AccessV2TravelAxis.Y;
                Tile2i containingAnchor =
                    GetGroundToVBandAnchor(ground, travel);
                Tile2i containingCompanion = AccessV2Geometry.Add(
                    containingAnchor,
                    AccessV2BandProfile.GetLaneDirection(axis));
                bool includeOutwardFringe =
                    fixedProfileProvider?.Invoke(
                        containingAnchor).HasValue == true
                    || fixedProfileProvider?.Invoke(
                        containingCompanion).HasValue == true;
                foreach (Tile2i anchor in
                    EnumerateGroundToVBandAnchors(
                        ground, travel, includeOutwardFringe))
                {
                    if (AreGroundToVBandOriginsResolvable(
                            anchor, axis,
                            generatedOriginValidator,
                            origin => fixedProfileProvider?.Invoke(
                                origin).HasValue == true))
                        return true;
                    if (TryCreateFixedGroundToVAdapter(
                            anchor, axis, travel,
                            fixedProfileProvider,
                            out AccessV2Transition adapter)
                        && HasGeneratedGroundToVSuccessor(
                            adapter.Next,
                            fixedProfileProvider,
                            generatedOriginValidator))
                        return true;
                }
            }
            return false;
        }

        internal static bool TryCreateFixedGroundToVAdapter(
            Tile2i anchor,
            AccessV2TravelAxis axis,
            Tile2i travel,
            Func<Tile2i, AccessHeightProfile?>? fixedProfileProvider,
            out AccessV2Transition adapter)
        {
            Tile2i companion = AccessV2Geometry.Add(
                anchor, AccessV2BandProfile.GetLaneDirection(axis));
            AccessHeightProfile? lane0 =
                fixedProfileProvider?.Invoke(anchor);
            AccessHeightProfile? lane1 =
                fixedProfileProvider?.Invoke(companion);
            if (!lane0.HasValue
                || !lane1.HasValue
                || !AccessV2BandProfile.TryCreateEnabled(
                    axis, lane0.Value, lane1.Value,
                    out AccessV2BandProfile band, out _))
            {
                adapter = null!;
                return false;
            }
            var state = new AccessV2BandState(anchor, band, travel);
            adapter = new AccessV2Transition(
                AccessV2TransitionKind.ProjectedGroundAdapter,
                state,
                Array.Empty<AccessV2OriginProfile>(),
                new[] { anchor, companion },
                scoreOnlyGeneratedExteriorRays: true);
            return true;
        }

        internal static bool HasGeneratedGroundToVSuccessor(
            AccessV2BandState adapter,
            Func<Tile2i, AccessHeightProfile?>? fixedProfileProvider,
            Func<Tile2i, bool>? generatedOriginValidator)
            => AccessV2Geometry.EnumerateStraight(adapter)
                .Any(candidate => TryResolveGroundToVTransition(
                    candidate.Next,
                    fixedProfileProvider,
                    generatedOriginValidator,
                    out _));

        internal static bool AreGroundToVBandOriginsResolvable(
            Tile2i anchor,
            AccessV2TravelAxis axis,
            Func<Tile2i, bool>? generatedOriginValidator,
            Func<Tile2i, bool>? fixedOriginValidator)
        {
            Tile2i companion = AccessV2Geometry.Add(
                anchor, AccessV2BandProfile.GetLaneDirection(axis));
            bool generated0 = generatedOriginValidator == null
                || generatedOriginValidator(anchor);
            bool generated1 = generatedOriginValidator == null
                || generatedOriginValidator(companion);
            bool fixed0 = fixedOriginValidator?.Invoke(anchor) == true;
            bool fixed1 = fixedOriginValidator?.Invoke(companion) == true;
            return (generated0 || fixed0)
                && (generated1 || fixed1)
                && (generated0 || generated1);
        }

        internal static bool TryResolveGroundToVTransition(
            AccessV2Transition candidate,
            Func<Tile2i, AccessHeightProfile?>? fixedProfileProvider,
            Func<Tile2i, bool>? generatedOriginValidator,
            out AccessV2Transition transition)
        {
            if (!TryResolveGroundToVTransition(
                    candidate.Next,
                    fixedProfileProvider,
                    generatedOriginValidator,
                    out AccessV2Transition resolved))
            {
                transition = null!;
                return false;
            }

            // Resolution decides which lanes are generated versus reused.
            // Keep the predecessor footprint declared by the geometry
            // candidate: history contact with that footprint is local to this
            // atomic adapter continuation, not a nonlocal route collision.
            var localContext = new HashSet<Tile2i>(
                resolved.LocalContextOrigins);
            localContext.UnionWith(candidate.LocalContextOrigins);
            transition = new AccessV2Transition(
                candidate.Kind,
                resolved.Next,
                resolved.Delta,
                localContext,
                candidate.OldDirectionTurnRays,
                candidate.WorkOperation,
                candidate.ScoreOnlyGeneratedExteriorRays
                    || resolved.ScoreOnlyGeneratedExteriorRays);
            return true;
        }

        internal static bool TryResolveGroundToVTransition(
            AccessV2BandState state,
            Func<Tile2i, AccessHeightProfile?>? fixedProfileProvider,
            Func<Tile2i, bool>? generatedOriginValidator,
            out AccessV2Transition transition)
        {
            var generated = new List<AccessV2OriginProfile>(2);
            var fixedContext = new List<Tile2i>(2);
            for (int lane = 0; lane < 2; lane++)
            {
                AccessV2OriginProfile item = state.GetLane(lane);
                AccessHeightProfile? fixedProfile =
                    fixedProfileProvider?.Invoke(item.Origin);
                if (fixedProfile.HasValue)
                {
                    if (!ProfilesEqual(
                            fixedProfile.Value, item.Profile))
                    {
                        transition = null!;
                        return false;
                    }
                    fixedContext.Add(item.Origin);
                    continue;
                }
                if (generatedOriginValidator != null
                    && !generatedOriginValidator(item.Origin))
                {
                    transition = null!;
                    return false;
                }
                generated.Add(item);
            }
            if (generated.Count == 0)
            {
                transition = null!;
                return false;
            }
            transition = new AccessV2Transition(
                AccessV2TransitionKind.Straight,
                state,
                generated,
                fixedContext,
                scoreOnlyGeneratedExteriorRays: true);
            return true;
        }

        private static bool ProfilesEqual(
            AccessHeightProfile left,
            AccessHeightProfile right)
            => left.Nw2 == right.Nw2
                && left.Ne2 == right.Ne2
                && left.Se2 == right.Se2
                && left.Sw2 == right.Sw2;

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

        internal static IEnumerable<Tile2i>
            EnumerateGroundToVBandAnchors(
                Tile2i ground,
                Tile2i travelDirection,
                bool includeOutwardFringe)
        {
            Tile2i containingAnchor = GetGroundToVBandAnchor(
                ground, travelDirection);
            yield return containingAnchor;
            if (includeOutwardFringe)
                yield return AccessV2Geometry.Add(
                    containingAnchor, travelDirection);
        }

        internal readonly struct GroundToVProfileCandidate
        {
            public AccessHeightProfile Lane0 { get; }
            public AccessHeightProfile Lane1 { get; }
            public AccessHeightProfile Profile => Lane0;
            public AccessHandoffOperation ExpectedOperation { get; }

            public GroundToVProfileCandidate(
                AccessHeightProfile profile,
                AccessHandoffOperation expectedOperation)
                : this(profile, profile, expectedOperation)
            {
            }

            public GroundToVProfileCandidate(
                AccessHeightProfile lane0,
                AccessHeightProfile lane1,
                AccessHandoffOperation expectedOperation)
            {
                Lane0 = lane0;
                Lane1 = lane1;
                ExpectedOperation = expectedOperation;
            }
        }

        internal static IEnumerable<GroundToVProfileCandidate>
            EnumerateGroundToVBandProfiles(
                Tile2i anchor,
                float terrainHeight,
                AccessV2TravelAxis axis,
                Tile2i travelDirection,
                Func<Tile2i, AccessHeightProfile?>? fixedProfileProvider,
                Func<Tile2i, bool>? generatedVPrimeOriginValidator)
        {
            Tile2i companion = AccessV2Geometry.Add(
                anchor, AccessV2BandProfile.GetLaneDirection(axis));
            foreach (GroundToVProfileCandidate ordinary in
                EnumerateGroundToVProfiles(
                    terrainHeight, axis, travelDirection))
            {
                var emitted = new HashSet<AccessV2BandProfile>();
                IReadOnlyList<AccessHeightProfile> lane0 =
                    EnumerateLaneProfiles(
                        anchor, ordinary.Profile,
                        fixedProfileProvider,
                        generatedVPrimeOriginValidator);
                IReadOnlyList<AccessHeightProfile> lane1 =
                    EnumerateLaneProfiles(
                        companion, ordinary.Profile,
                        fixedProfileProvider,
                        generatedVPrimeOriginValidator);
                for (int lane0Index = 0;
                    lane0Index < lane0.Count;
                    lane0Index++)
                {
                    for (int lane1Index = 0;
                        lane1Index < lane1.Count;
                        lane1Index++)
                    {
                        if (!AccessV2BandProfile.TryCreate(
                                axis,
                                lane0[lane0Index],
                                lane1[lane1Index],
                                includeDeferred: true,
                                out AccessV2BandProfile band,
                                out _)
                            || !emitted.Add(band))
                            continue;
                        yield return new GroundToVProfileCandidate(
                            band.Lane0,
                            band.Lane1,
                            ordinary.ExpectedOperation);
                    }
                }
            }
        }

        internal static IEnumerable<AccessV2Transition>
            EnumerateVPrimeAdapterExtensions(
                AccessV2BandState current)
        {
            if (!IsVPrimeBand(current.Band))
                yield break;

            IReadOnlyList<AccessHeightProfile> lane0 =
                EnumerateAdapterSuccessorProfiles(
                    current.Band.Lane0,
                    current.EntryDirection);
            IReadOnlyList<AccessHeightProfile> lane1 =
                EnumerateAdapterSuccessorProfiles(
                    current.Band.Lane1,
                    current.EntryDirection);
            var emitted = new HashSet<AccessV2BandProfile>();
            Tile2i nextAnchor = AccessV2Geometry.Add(
                current.Anchor, current.EntryDirection);
            for (int lane0Index = 0;
                lane0Index < lane0.Count;
                lane0Index++)
            {
                for (int lane1Index = 0;
                    lane1Index < lane1.Count;
                    lane1Index++)
                {
                    if (!AccessV2BandProfile.TryCreate(
                            current.Axis,
                            lane0[lane0Index],
                            lane1[lane1Index],
                            includeDeferred: true,
                            out AccessV2BandProfile band,
                            out _)
                        || band.Kind
                            != AccessV2BandProfileKind
                                .MechanicallyValidDeferred
                        || !IsVPrimeBand(band)
                        || !emitted.Add(band))
                        continue;
                    var next = new AccessV2BandState(
                        nextAnchor, band,
                        current.EntryDirection);
                    if (AccessV2Geometry
                            .EnumerateStraight(next).Count == 0)
                        continue;
                    yield return new AccessV2Transition(
                        AccessV2TransitionKind.Straight,
                        next,
                        new[]
                        {
                            next.GetLane(0),
                            next.GetLane(1),
                        },
                        new[]
                        {
                            current.GetLaneOrigin(0),
                            current.GetLaneOrigin(1),
                        },
                        scoreOnlyGeneratedExteriorRays: true);
                }
            }
        }

        private static IReadOnlyList<AccessHeightProfile>
            EnumerateAdapterSuccessorProfiles(
                AccessHeightProfile current,
                Tile2i travelDirection)
        {
            var result = new List<AccessHeightProfile>();
            var emitted = new HashSet<AccessHeightProfile>();
            AccessSearchMode positive =
                travelDirection.X > 0
                    ? AccessSearchMode.XPositive
                    : travelDirection.X < 0
                        ? AccessSearchMode.XNegative
                        : travelDirection.Y > 0
                            ? AccessSearchMode.YPositive
                            : AccessSearchMode.YNegative;
            AccessSearchMode negative =
                travelDirection.X > 0
                    ? AccessSearchMode.XNegative
                    : travelDirection.X < 0
                        ? AccessSearchMode.XPositive
                        : travelDirection.Y > 0
                            ? AccessSearchMode.YNegative
                            : AccessSearchMode.YPositive;
            foreach (AccessSearchMode mode in new[]
            {
                AccessSearchMode.Flat,
                positive,
                negative,
            })
            {
                if (AccessPathSearch.TrySolveSuccessor(
                        current, travelDirection, mode,
                        out AccessHeightProfile ordinary)
                    && emitted.Add(ordinary))
                    result.Add(ordinary);
            }

            current.GetEdge(
                travelDirection,
                out int firstHeight2,
                out int secondHeight2);
            var baseHeights2 = new HashSet<int>
            {
                firstHeight2,
                secondHeight2,
                firstHeight2 - 2,
                firstHeight2 + 2,
                secondHeight2 - 2,
                secondHeight2 + 2,
            };
            foreach (int baseHeight2 in baseHeights2)
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        int[] heights =
                        {
                            baseHeight2,
                            baseHeight2,
                            baseHeight2,
                            baseHeight2,
                        };
                        heights[corner] += sign * 2;
                        var candidate = new AccessHeightProfile(
                            heights[0], heights[1],
                            heights[2], heights[3]);
                        if (AccessV2BandProfile.IsCanonicalVPrime(
                                candidate)
                            && AccessPathSearch.EdgesMatch(
                                current, candidate,
                                travelDirection)
                            && emitted.Add(candidate))
                            result.Add(candidate);
                    }
                }
            }
            return result;
        }

        private static IReadOnlyList<AccessHeightProfile>
            EnumerateLaneProfiles(
                Tile2i origin,
                AccessHeightProfile ordinary,
                Func<Tile2i, AccessHeightProfile?>? fixedProfileProvider,
                Func<Tile2i, bool>? generatedVPrimeOriginValidator)
        {
            AccessHeightProfile? fixedProfile =
                fixedProfileProvider?.Invoke(origin);
            if (fixedProfile.HasValue)
                return new[] { fixedProfile.Value };

            var result = new List<AccessHeightProfile>();
            if (MatchesFixedNeighborCorners(
                    origin, ordinary, fixedProfileProvider))
                result.Add(ordinary);
            if (generatedVPrimeOriginValidator?.Invoke(origin) != true)
                return result;

            var baseHeights2 = new HashSet<int>
            {
                ordinary.Nw2,
                ordinary.Ne2,
                ordinary.Se2,
                ordinary.Sw2,
            };
            if (fixedProfileProvider != null)
            {
                for (int dy = -4; dy <= 4; dy += 4)
                {
                    for (int dx = -4; dx <= 4; dx += 4)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        AccessHeightProfile? neighbor =
                            fixedProfileProvider(new Tile2i(
                                origin.X + dx, origin.Y + dy));
                        if (!neighbor.HasValue)
                            continue;
                        baseHeights2.Add(neighbor.Value.Nw2);
                        baseHeights2.Add(neighbor.Value.Ne2);
                        baseHeights2.Add(neighbor.Value.Se2);
                        baseHeights2.Add(neighbor.Value.Sw2);
                    }
                }
            }
            var emitted = new HashSet<AccessHeightProfile>();
            foreach (int baseHeight2 in baseHeights2)
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        int[] heights =
                        {
                            baseHeight2,
                            baseHeight2,
                            baseHeight2,
                            baseHeight2,
                        };
                        heights[corner] += sign * 2;
                        var candidate = new AccessHeightProfile(
                            heights[0], heights[1],
                            heights[2], heights[3]);
                        if (emitted.Add(candidate)
                            && MatchesFixedNeighborCorners(
                                origin, candidate,
                                fixedProfileProvider))
                            result.Add(candidate);
                    }
                }
            }
            return result;
        }

        private static bool MatchesFixedNeighborCorners(
            Tile2i origin,
            AccessHeightProfile candidate,
            Func<Tile2i, AccessHeightProfile?>? fixedProfileProvider)
        {
            if (fixedProfileProvider == null)
                return true;
            var candidateCorners = new Dictionary<Tile2i, int>();
            candidate.AddWorldCorners(
                origin,
                (corner, height2) =>
                    candidateCorners[corner] = height2);
            for (int dy = -4; dy <= 4; dy += 4)
            {
                for (int dx = -4; dx <= 4; dx += 4)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    Tile2i neighbor = new Tile2i(
                        origin.X + dx, origin.Y + dy);
                    AccessHeightProfile? fixedProfile =
                        fixedProfileProvider(neighbor);
                    if (!fixedProfile.HasValue)
                        continue;
                    bool mismatch = false;
                    fixedProfile.Value.AddWorldCorners(
                        neighbor,
                        (corner, height2) =>
                        {
                            if (candidateCorners.TryGetValue(
                                    corner, out int ownHeight2)
                                && ownHeight2 != height2)
                                mismatch = true;
                        });
                    if (mismatch)
                        return false;
                }
            }
            return true;
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

        private bool Enqueue(
            SearchNode node,
            bool isStartTierSeed = false)
        {
            node.PotentialOwner = GetPotentialOwnerForV(
                node.Parent, node.State, node.Transition);
            var key = new SearchKey(node);
            if (isStartTierSeed
                && m_startTierIndex > 0
                && m_exploredStartTierKeys.Contains(key))
            {
                m_diagnostics.V2RedundantStartSeedsSkipped++;
                return false;
            }
            if (m_best.TryGetValue(key, out float old)
                && old <= node.Cost + 0.0001f)
                return false;
            m_best[key] = node.Cost;
            node.EnqueuedAtVisited = m_visited;
            float heuristic = 0f;
            if (m_potentialField != null || m_heuristicEvaluator != null)
            {
                if (node.GroundCenter.HasValue && m_groundGraph != null
                    && m_groundGraph.TryGetGoalDistance(
                        node.GroundCenter.Value, out float groundDistance))
                    heuristic = groundDistance;
                else if (node.GroundCenter.HasValue
                    && m_groundEscapePotentialField != null)
                {
                    heuristic = Math.Max(0f,
                        m_groundEscapePotentialField.GetPotential(
                            node.GroundCenter.Value));
                    m_diagnostics.V2PotentialGroundComponents =
                        m_groundEscapePotentialField.BuiltComponentCount;
                }
                else if (!node.GroundCenter.HasValue)
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
            return true;
        }

        private AccessV2PotentialOwner GetPotentialOwnerForV(
            SearchNode? parent,
            AccessV2BandState state,
            AccessV2Transition? transition)
        {
            if (m_groundGraph == null || parent == null)
                return AccessV2PotentialOwner.Global;

            AccessV2PotentialOwner owner;
            Tile2i from;
            if (parent.GroundCenter.HasValue)
            {
                from = parent.GroundCenter.Value;
                owner = AccessV2PotentialOwner.FromGround(
                    m_groundGraph, from);
            }
            else
            {
                from = AccessV2PotentialField.GetCanonicalCenter(
                    parent.State);
                owner = parent.PotentialOwner;
            }

            // A turn reorients one already admitted flat landing; its change
            // of canonical band center is not new longitudinal V progress.
            if (transition?.Kind == AccessV2TransitionKind.Turn)
                return owner;
            return owner.Advance(
                m_groundGraph, from,
                AccessV2PotentialField.GetCanonicalCenter(state));
        }

        private SearchNode Pop()
        {
            KeyValuePair<SearchPriority, Queue<SearchNode>> first = m_queue.First();
            SearchNode node = first.Value.Dequeue();
            if (first.Value.Count == 0) m_queue.Remove(first.Key);
            m_queueCount--;
            return node;
        }

        private void CompleteSuccess(SearchNode goal)
        {
            var reverse = new List<AccessV2BandState>();
            int straight = 0, strafe = 0, turn = 0;
            var groundReverse = new List<Tile2i>();
            var stepReverse = new List<AccessV2RouteStep>();
            for (SearchNode? node = goal; node != null; node = node.Parent)
            {
                stepReverse.Add(new AccessV2RouteStep(
                    node.State, node.Transition,
                    node.Handoff, node.GroundCenter,
                    node.IsProjectedGroundEntry));
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
            public AccessV2TravelAxis? FixedNavigationAxis { get; }
            public Tile2i? FixedNavigationPortalRoot { get; }
            public bool RequiresGroundTransition { get; }
            public bool IsGroundToVAdapter { get; }
            public bool IsProjectedGroundEntry { get; }
            public bool IsGroundRelaunchedV { get; }
            public AccessV2PotentialOwner PotentialOwner { get; set; }
            public int EnqueuedAtVisited { get; set; }

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
                bool requiresGroundTransition = false,
                bool isGroundToVAdapter = false,
                bool isProjectedGroundEntry = false,
                AccessV2TravelAxis? fixedNavigationAxis = null,
                Tile2i? fixedNavigationPortalRoot = null)
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
                FixedNavigationAxis = fixedNavigationAxis;
                FixedNavigationPortalRoot =
                    fixedNavigationPortalRoot;
                RequiresGroundTransition = requiresGroundTransition;
                IsGroundToVAdapter = isGroundToVAdapter;
                IsProjectedGroundEntry = isProjectedGroundEntry;
                PotentialOwner = AccessV2PotentialOwner.Global;
                IsGroundRelaunchedV = !groundCenter.HasValue
                    && (parent?.GroundCenter.HasValue == true
                        || parent?.IsGroundRelaunchedV == true);
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
            private readonly AccessV2TravelAxis? m_fixedNavigationAxis;
            private readonly Tile2i? m_fixedNavigationPortalRoot;
            private readonly bool m_isGroundToVAdapter;
            private readonly bool m_strictSelfDisruption;
            private readonly AccessV2PotentialOwner m_potentialOwner;

            public SearchKey(
                AccessV2BandState state,
                bool strictSelfDisruption,
                AccessV2PotentialOwner potentialOwner)
            {
                m_state = state;
                m_groundCenter = null;
                m_fixedNavigationAxis = null;
                m_fixedNavigationPortalRoot = null;
                m_isGroundToVAdapter = false;
                m_strictSelfDisruption = strictSelfDisruption;
                m_potentialOwner = potentialOwner;
            }

            public SearchKey(SearchNode node)
            {
                m_groundCenter = node.GroundCenter;
                m_fixedNavigationAxis = node.FixedNavigationAxis;
                m_fixedNavigationPortalRoot =
                    node.FixedNavigationPortalRoot;
                m_isGroundToVAdapter = node.IsGroundToVAdapter;
                m_strictSelfDisruption = !node.GroundCenter.HasValue
                    && node.History.RequiresStrictSelfDisruptionChecks;
                m_potentialOwner = node.GroundCenter.HasValue
                    ? AccessV2PotentialOwner.Global
                    : node.PotentialOwner;
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
                    && m_fixedNavigationAxis == other.m_fixedNavigationAxis
                    && m_fixedNavigationPortalRoot
                        == other.m_fixedNavigationPortalRoot
                    && m_isGroundToVAdapter == other.m_isGroundToVAdapter
                    && m_strictSelfDisruption
                        == other.m_strictSelfDisruption
                    && m_potentialOwner.Equals(other.m_potentialOwner);

            public override bool Equals(object? obj)
                => obj is SearchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = m_state.GetHashCode();
                    hash = (hash * 397) ^ m_groundCenter.GetHashCode();
                    hash = (hash * 397)
                        ^ m_fixedNavigationAxis.GetHashCode();
                    hash = (hash * 397)
                        ^ m_fixedNavigationPortalRoot.GetHashCode();
                    hash = (hash * 397) ^ m_isGroundToVAdapter.GetHashCode();
                    hash = (hash * 397)
                        ^ m_strictSelfDisruption.GetHashCode();
                    hash = (hash * 397) ^ m_potentialOwner.GetHashCode();
                    return hash;
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal readonly struct AccessV2ExpansionTrace
    {
        public int Ordinal { get; }
        public Tile2i Center { get; }
        public int Height2 { get; }
        public int? GroundHeight2 { get; }
        public bool IsGround { get; }
        public bool FirstExpansion { get; }
        public int QueueAge { get; }
        public int EnqueuedAtVisited { get; }
        public float Cost { get; }
        public bool IsGroundRelaunchedV { get; }
        public AccessV2TravelAxis? Axis { get; }
        public Tile2i? EntryDirection { get; }
        public bool HasHandoff { get; }
        public Tile2i? FixedNavigationPortalRoot { get; }
        public bool IsGroundToVAdapter { get; }
        public bool IsProjectedGroundEntry { get; }
        public int HistorySignature { get; }
        public int HistoryOrigins { get; }
        public int HistoryRayConstraints { get; }
        public int HistoryCleanupKeys { get; }
        public bool PotentialOwnerIsGlobal { get; }
        public int PotentialOwnerId { get; }
        public Tile2i? GroundLaunchCenter { get; }
        public int? GroundLaunchComponent { get; }
        public Tile2i? PotentialMergeCenter { get; }
        public int? GroundComponent { get; }
        public float? OrdinaryGroundBestCost { get; }
        public int LaunchHistoryOrigins { get; }
        public int LaunchHistoryRayConstraints { get; }
        public int LaunchHistoryCleanupKeys { get; }
        public int LabelKeyHash { get; }

        public AccessV2ExpansionTrace(
            int ordinal,
            Tile2i center,
            int height2,
            int? groundHeight2,
            bool isGround,
            bool firstExpansion,
            int queueAge,
            int enqueuedAtVisited,
            float cost,
            bool isGroundRelaunchedV,
            AccessV2TravelAxis? axis,
            Tile2i? entryDirection,
            bool hasHandoff,
            Tile2i? fixedNavigationPortalRoot,
            bool isGroundToVAdapter,
            bool isProjectedGroundEntry,
            int historySignature,
            int historyOrigins,
            int historyRayConstraints,
            int historyCleanupKeys,
            bool potentialOwnerIsGlobal,
            int potentialOwnerId,
            Tile2i? groundLaunchCenter,
            int? groundLaunchComponent,
            Tile2i? potentialMergeCenter,
            int? groundComponent,
            float? ordinaryGroundBestCost,
            int launchHistoryOrigins,
            int launchHistoryRayConstraints,
            int launchHistoryCleanupKeys,
            int labelKeyHash)
        {
            Ordinal = ordinal;
            Center = center;
            Height2 = height2;
            GroundHeight2 = groundHeight2;
            IsGround = isGround;
            FirstExpansion = firstExpansion;
            QueueAge = queueAge;
            EnqueuedAtVisited = enqueuedAtVisited;
            Cost = cost;
            IsGroundRelaunchedV = isGroundRelaunchedV;
            Axis = axis;
            EntryDirection = entryDirection;
            HasHandoff = hasHandoff;
            FixedNavigationPortalRoot = fixedNavigationPortalRoot;
            IsGroundToVAdapter = isGroundToVAdapter;
            IsProjectedGroundEntry = isProjectedGroundEntry;
            HistorySignature = historySignature;
            HistoryOrigins = historyOrigins;
            HistoryRayConstraints = historyRayConstraints;
            HistoryCleanupKeys = historyCleanupKeys;
            PotentialOwnerIsGlobal = potentialOwnerIsGlobal;
            PotentialOwnerId = potentialOwnerId;
            GroundLaunchCenter = groundLaunchCenter;
            GroundLaunchComponent = groundLaunchComponent;
            PotentialMergeCenter = potentialMergeCenter;
            GroundComponent = groundComponent;
            OrdinaryGroundBestCost = ordinaryGroundBestCost;
            LaunchHistoryOrigins = launchHistoryOrigins;
            LaunchHistoryRayConstraints = launchHistoryRayConstraints;
            LaunchHistoryCleanupKeys = launchHistoryCleanupKeys;
            LabelKeyHash = labelKeyHash;
        }
    }

    internal readonly struct AccessV2GroundExpansionOutcomeTrace
    {
        public int Ordinal { get; }
        public bool HasHandoff { get; }
        public bool GoalAtPop { get; }
        public bool SuffixAttempted { get; }
        public bool SuffixSucceeded { get; }
        public int GroundEnqueueAttempts { get; }
        public int GroundEnqueueAccepted { get; }
        public int VEnqueueAttempts { get; }
        public int VEnqueueAccepted { get; }

        public AccessV2GroundExpansionOutcomeTrace(
            int ordinal,
            bool hasHandoff,
            bool goalAtPop,
            bool suffixAttempted,
            bool suffixSucceeded,
            int groundEnqueueAttempts,
            int groundEnqueueAccepted,
            int vEnqueueAttempts,
            int vEnqueueAccepted)
        {
            Ordinal = ordinal;
            HasHandoff = hasHandoff;
            GoalAtPop = goalAtPop;
            SuffixAttempted = suffixAttempted;
            SuffixSucceeded = suffixSucceeded;
            GroundEnqueueAttempts = groundEnqueueAttempts;
            GroundEnqueueAccepted = groundEnqueueAccepted;
            VEnqueueAttempts = vEnqueueAttempts;
            VEnqueueAccepted = vEnqueueAccepted;
        }
    }

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

        private static readonly RelTile2i[] s_groundDirections =
        {
            new RelTile2i(1, 0), new RelTile2i(-1, 0),
            new RelTile2i(0, 1), new RelTile2i(0, -1),
            new RelTile2i(1, 1), new RelTile2i(1, -1),
            new RelTile2i(-1, 1), new RelTile2i(-1, -1),
        };

        private readonly Tile2i m_boundsMin;
        private readonly Tile2i m_boundsMax;
        private readonly IReadOnlyList<IReadOnlyList<AccessV2StartFrontage>>
            m_startTiers;
        private readonly AccessV2TransitionEvaluator m_evaluator;
        private readonly AccessV2TerminalTransitionEvaluator?
            m_terminalTransitionEvaluator;
        private readonly AccessV2SingleLaneHandoffEvaluator?
            m_terminalSingleLaneHandoffEvaluator;
        private readonly AccessV2LaneSpanHandoffEvaluator?
            m_terminalLaneSpanHandoffEvaluator;
        private readonly AccessV2TerminalCrestEvaluator?
            m_terminalCrestEvaluator;
        private readonly AccessV2HandoffCenterEvaluator?
            m_terminalCenterEvaluator;
        private readonly AccessV2HandoffGroundEntryEvaluator?
            m_terminalGroundEntryEvaluator;
        private readonly Func<Tile2i, AccessV2History, bool>?
            m_terminalProjectedCenterValidator;
        private readonly Func<AccessV2Transition, bool>?
            m_terminalTransitionValidator;
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
        private readonly bool m_evaluateDirectGroundReplacementDominance;
        private readonly int m_maxVisited;
        private readonly float m_maxCost;
        private readonly SortedDictionary<SearchPriority, Queue<SearchNode>> m_queue =
            new SortedDictionary<SearchPriority, Queue<SearchNode>>();
        private bool m_groundToVEnqueued;
        private readonly Dictionary<SearchKey, float> m_best =
            new Dictionary<SearchKey, float>();
        private readonly Dictionary<Tile2i, SearchNode>
            m_bestGroundNodesByCenter =
                new Dictionary<Tile2i, SearchNode>();
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
        private IAccessV2SearchContinuation? m_continuation;
        private ContinuationResumeStage m_continuationResumeStage;
        private GroundExpansionTraceState? m_activeGroundExpansionTrace;
        private Dictionary<SearchNode, ExpansionTraceProvenance>?
            m_expansionTraceProvenance;
        private bool m_emittingOrdinaryGroundReplacement;

        public bool IsComplete { get; private set; }
        public int Visited => m_visited;
        public int Pending => m_queueCount;
        public int VehicleWidth { get; }
        internal string Phase => m_continuation?.Phase ?? "V2 frontier";
        internal Dictionary<string, int> LiveRejections => m_rejections;
        // Diagnostic-only hook used by the access search overlay.
        internal Action<Tile2i, int, bool, int?>? NodeExplored { get; set; }
        internal Action<AccessV2ExpansionTrace>? ExpansionTraced { get; set; }
        internal Action<AccessV2GroundExpansionOutcomeTrace>?
            GroundExpansionOutcomeTraced { get; set; }
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
            AccessV2TerminalTransitionEvaluator?
                terminalTransitionEvaluator = null,
            AccessV2SingleLaneHandoffEvaluator?
                terminalSingleLaneHandoffEvaluator = null,
            AccessV2LaneSpanHandoffEvaluator?
                terminalLaneSpanHandoffEvaluator = null,
            AccessV2HandoffCenterEvaluator?
                terminalCenterEvaluator = null,
            AccessV2HandoffGroundEntryEvaluator?
                terminalGroundEntryEvaluator = null,
            Func<Tile2i, AccessV2History, bool>?
                terminalProjectedCenterValidator = null,
            Func<AccessV2Transition, bool>?
                terminalTransitionValidator = null,
            Func<Tile2i, AccessHeightProfile?>?
                fixedProfileProvider = null,
            AccessV2FixedNavigationGraph?
                fixedNavigationGraph = null,
            Func<Tile2i, bool>? generatedVPrimeOriginValidator = null,
            int vehicleWidth = 5,
            AccessV2TerminalCrestEvaluator? terminalCrestEvaluator = null,
            bool evaluateDirectGroundReplacementDominance = false)
        {
            m_boundsMin = boundsMin;
            m_boundsMax = boundsMax;
            m_startTiers = endpoints.StartTiers;
            m_evaluator = evaluator;
            m_terminalTransitionEvaluator = terminalTransitionEvaluator;
            m_terminalSingleLaneHandoffEvaluator =
                terminalSingleLaneHandoffEvaluator;
            m_terminalLaneSpanHandoffEvaluator =
                terminalLaneSpanHandoffEvaluator;
            m_terminalCrestEvaluator = terminalCrestEvaluator;
            m_terminalCenterEvaluator = terminalCenterEvaluator;
            m_terminalGroundEntryEvaluator = terminalGroundEntryEvaluator;
            m_terminalProjectedCenterValidator =
                terminalProjectedCenterValidator;
            m_terminalTransitionValidator = terminalTransitionValidator;
            m_handoffEvaluator = handoffEvaluator;
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
            m_evaluateDirectGroundReplacementDominance =
                evaluateDirectGroundReplacementDominance;
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
            => Step(maxVisitedThisStep, null);

        public int Step(
            int maxVisitedThisStep,
            AccessSearchSliceBudget? sliceBudget)
        {
            if (IsComplete) return 0;
            int budget = Math.Max(1, maxVisitedThisStep);
            int visitedAtStart = m_visited;
            while (true)
            {
                if (m_continuation != null)
                {
                    ContinuationAdvanceResult continuationResult =
                        m_continuation.Advance(sliceBudget);
                    if (continuationResult.Outcome
                        == ContinuationOutcome.Yielded)
                        return m_visited - visitedAtStart;

                    IAccessV2SearchContinuation completedContinuation =
                        m_continuation;
                    m_continuation = null;
                    ContinuationResumeStage resumeStage =
                        m_continuationResumeStage;
                    m_continuationResumeStage =
                        ContinuationResumeStage.None;

                    if (continuationResult.Outcome
                        == ContinuationOutcome.Cancelled)
                    {
                        CompleteActiveGroundExpansionTrace();
                        CompleteFailure("SearchCancelled");
                        break;
                    }
                    if (continuationResult.Outcome
                        == ContinuationOutcome.Succeeded)
                    {
                        if (resumeStage
                            == ContinuationResumeStage.ExpandGround
                            && m_activeGroundExpansionTrace != null)
                            m_activeGroundExpansionTrace.SuffixSucceeded = true;
                        CompleteActiveGroundExpansionTrace();
                        CompleteSuccess(continuationResult.Terminal!);
                        break;
                    }
                    if (resumeStage
                        == ContinuationResumeStage.ExpandGround)
                    {
                        m_continuation =
                            new GroundExpansionContinuation(
                                this,
                                completedContinuation.Current);
                        continue;
                    }
                    if (completedContinuation
                        is GroundExpansionContinuation)
                        CompleteActiveGroundExpansionTrace();
                    if (resumeStage
                        == ContinuationResumeStage.ExpandBand)
                    {
                        if (sliceBudget?.CancellationRequested == true)
                        {
                            CompleteFailure("SearchCancelled");
                            break;
                        }
                        if (sliceBudget?.IsExpired == true)
                        {
                            m_continuation =
                                new BandExpansionContinuation(
                                    this,
                                    completedContinuation.Current);
                            continue;
                        }
                        if (!completedContinuation.Current
                                .RequiresGroundTransition)
                        {
                            m_continuation =
                                new BandExpansionContinuation(
                                    this,
                                    completedContinuation.Current);
                        }
                        continue;
                    }
                    continue;
                }

                if (sliceBudget?.CancellationRequested == true)
                {
                    CompleteFailure("SearchCancelled");
                    break;
                }
                if (sliceBudget?.IsExpired == true)
                    break;
                if (m_queueCount == 0
                    || m_visited >= m_maxVisited
                    || m_visited - visitedAtStart >= budget)
                    break;

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
                if (ExpansionTraced != null)
                {
                    int? exploredGroundComponent = null;
                    float? ordinaryGroundBestCost = null;
                    if (current.GroundCenter.HasValue
                        && m_groundGraph != null
                        && m_groundGraph.TryGetComponentId(
                            current.GroundCenter.Value,
                            out int foundGroundComponent))
                        exploredGroundComponent = foundGroundComponent;
                    if (current.GroundCenter.HasValue
                        && current.Handoff != null
                        && m_best.TryGetValue(
                            new SearchKey(
                                current.GroundCenter.Value,
                                current.FixedNavigationAxis,
                                current.FixedNavigationPortalRoot),
                            out float foundOrdinaryGroundCost))
                        ordinaryGroundBestCost = foundOrdinaryGroundCost;
                    ExpansionTraceProvenance provenance = default;
                    m_expansionTraceProvenance?.TryGetValue(
                        current, out provenance);
                    ExpansionTraced.Invoke(new AccessV2ExpansionTrace(
                        m_visited,
                        exploredCenter,
                        exploredHeight2,
                        exploredGroundHeight2,
                        current.GroundCenter.HasValue,
                        firstExpansion,
                        queueAge,
                        current.EnqueuedAtVisited,
                        current.Cost,
                        current.IsGroundRelaunchedV,
                        current.GroundCenter.HasValue
                            ? current.FixedNavigationAxis
                            : current.State.Axis,
                        current.GroundCenter.HasValue
                            ? null
                            : current.State.EntryDirection,
                        current.Handoff != null,
                        current.FixedNavigationPortalRoot,
                        current.IsGroundToVAdapter,
                        current.IsProjectedGroundEntry,
                        current.History.Signature,
                        current.History.OriginCount,
                        current.History.RayConstraintCount,
                        current.History.CleanupKeyCount,
                        current.PotentialOwner.IsGlobal,
                        current.PotentialOwner.GetHashCode(),
                        provenance.GroundLaunchCenter,
                        current.PotentialOwner.SourceGroundComponent,
                        provenance.PotentialMergeCenter,
                        exploredGroundComponent,
                        ordinaryGroundBestCost,
                        provenance.LaunchHistoryOrigins,
                        provenance.LaunchHistoryRayConstraints,
                        provenance.LaunchHistoryCleanupKeys,
                        currentKey.GetHashCode()));
                }
                if (NodeExplored != null)
                {
                    long nodeCallbackStart = AtdDiagnostics.Timestamp();
                    NodeExplored(
                        exploredCenter,
                        exploredHeight2,
                        current.GroundCenter.HasValue,
                        exploredGroundHeight2);
                    m_diagnostics.RecordV2MaxNodeCallback(
                        AtdDiagnostics.ElapsedSince(nodeCallbackStart),
                        $"center={exploredCenter} " +
                        $"ground={current.GroundCenter.HasValue} " +
                        $"visited={m_visited}");
                }
                if (current.GroundCenter.HasValue)
                {
                    if (GroundExpansionOutcomeTraced != null)
                        m_activeGroundExpansionTrace =
                            new GroundExpansionTraceState(
                                m_visited,
                                current.Handoff != null);
                    if (m_groundGraph != null
                        && m_groundGraph.IsGoal(current.GroundCenter.Value))
                    {
                        if (m_activeGroundExpansionTrace != null)
                            m_activeGroundExpansionTrace.GoalAtPop = true;
                        CompleteActiveGroundExpansionTrace();
                        CompleteSuccess(current);
                        break;
                    }
                    if (!current.FixedNavigationAxis.HasValue
                        && !current.FixedNavigationPortalRoot.HasValue)
                    {
                        GroundSuffixContinuation? suffix =
                            CreateGroundSuffixContinuation(current);
                        if (suffix != null)
                        {
                            if (m_activeGroundExpansionTrace != null)
                                m_activeGroundExpansionTrace.SuffixAttempted =
                                    true;
                            m_continuation = suffix;
                            m_continuationResumeStage =
                                ContinuationResumeStage.ExpandGround;
                            continue;
                        }
                    }
                    m_continuation =
                        new GroundExpansionContinuation(this, current);
                    continue;
                }
                m_diagnostics.V2BandExpansions++;
                long bandStart = AtdDiagnostics.Timestamp();
                if (current.IsGroundToVAdapter)
                {
                    ExpandGroundToVAdapter(current, sliceBudget);
                    long elapsed = AtdDiagnostics.ElapsedSince(bandStart);
                    m_diagnostics.V2BandExpansionTicks += elapsed;
                    m_diagnostics.RecordV2MaxBandSetup(
                        elapsed,
                        $"source=ground-to-v-adapter " +
                        $"anchor={current.State.Anchor} " +
                        $"entry={current.State.EntryDirection} " +
                        $"visited={m_visited} pending={m_queueCount}");
                    continue;
                }
                HandoffEnumerationContinuation? handoff =
                    CreateHandoffEnumerationContinuation(current);
                long bandElapsed = AtdDiagnostics.ElapsedSince(bandStart);
                m_diagnostics.V2BandExpansionTicks += bandElapsed;
                m_diagnostics.RecordV2MaxBandSetup(
                    bandElapsed,
                    $"source=frontier-setup " +
                    $"anchor={current.State.Anchor} " +
                    $"entry={current.State.EntryDirection} " +
                    $"band={current.State.Band.Kind} " +
                    $"candidates={(handoff == null ? 0 : 1)} " +
                    $"visited={m_visited} pending={m_queueCount}");
                if (handoff != null)
                {
                    m_continuation = handoff;
                    m_continuationResumeStage =
                        ContinuationResumeStage.ExpandBand;
                    continue;
                }
                if (sliceBudget?.CancellationRequested == true)
                {
                    CompleteFailure("SearchCancelled");
                    break;
                }
                if (sliceBudget?.IsExpired == true)
                {
                    m_continuation =
                        new BandExpansionContinuation(this, current);
                    continue;
                }
                if (!current.RequiresGroundTransition)
                {
                    m_continuation =
                        new BandExpansionContinuation(this, current);
                }
            }

            if (!IsComplete
                && m_queueCount == 0
                && sliceBudget?.CancellationRequested != true
                && sliceBudget?.IsExpired != true)
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
                m_bestGroundNodesByCenter.Clear();
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
                RecordTransitionEvaluationTiming(
                    evaluationStart, null, initial, "initial");
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

            if (successor.Kind == AccessV2TransitionKind.Straight)
                EvaluateAndEnqueueTerminal(
                    initialNode, successor, history,
                    start.FixedSeedOrigin);

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
            RecordTransitionEvaluationTiming(
                successorEvaluationStart,
                start.State,
                successor,
                "successor");
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
            AccessV2Transition transition,
            AccessSearchSliceBudget? sliceBudget = null)
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
            if (!current.History.TryValidateApply(
                    transition, out string historyReason))
            {
                Reject(historyReason);
                Trace("reject:" + historyReason);
                return;
            }

            // Terminal mining/dumping is an independent successor of the
            // same unpriced straight. It must be attempted before ordinary
            // label dominance or ordinary leveling feasibility can suppress
            // the route; ordinary V remains evaluated immediately after.
            if (transition.Kind == AccessV2TransitionKind.Straight)
            {
                Tile2i? terminalFixedOrigin = null;
                if (transition.ScoreOnlyGeneratedExteriorRays)
                    for (int lane = 0; lane < 2; lane++)
                    {
                        Tile2i origin = current.State.GetLaneOrigin(lane);
                        if (m_fixedProfileProvider?.Invoke(origin).HasValue
                            == true)
                        {
                            terminalFixedOrigin = origin;
                            break;
                        }
                    }
                EvaluateAndEnqueueTerminal(
                    current, transition, current.History,
                    terminalFixedOrigin, sliceBudget);
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
            RecordTransitionEvaluationTiming(
                evaluationStart,
                current.State,
                transition,
                "frontier");
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

        private void EvaluateAndEnqueueTerminal(
            SearchNode predecessor,
            AccessV2Transition straight,
            AccessV2History history,
            Tile2i? connectedFixedOrigin = null,
            AccessSearchSliceBudget? sliceBudget = null)
        {
            if (m_groundGraph == null
                || m_terminalSingleLaneHandoffEvaluator == null
                || m_terminalLaneSpanHandoffEvaluator == null
                || m_terminalCrestEvaluator == null
                || m_terminalTransitionEvaluator == null
                || m_terminalCenterEvaluator == null)
                return;
            var recent = new List<AccessV2BandState>(2)
            {
                straight.Next,
                predecessor.State,
            };
            var request = new AccessV2TerminalRequest(
                predecessor.State,
                straight,
                history,
                predecessor.Cost,
                connectedFixedOrigin,
                recent,
                m_groundGraph,
                m_terminalSingleLaneHandoffEvaluator,
                m_terminalLaneSpanHandoffEvaluator,
                m_terminalCrestEvaluator,
                m_terminalTransitionEvaluator,
                m_terminalCenterEvaluator,
                m_terminalGroundEntryEvaluator,
                m_terminalProjectedCenterValidator,
                m_generatedOriginValidator,
                m_boundsMin,
                m_boundsMax,
                m_cleanupCostScale,
                m_groundToVCenterSpokeCost,
                m_maxCost,
                VehicleWidth,
                sliceBudget,
                m_terminalTransitionValidator,
                m_diagnostics,
                state => GetFixedSafetyExemptOrigins(
                    state, connectedFixedOrigin));
            long started = AtdDiagnostics.Timestamp();
            AccessV2TerminalResult result =
                AccessV2TerminalEvaluator.Evaluate(in request);
            long elapsed = AtdDiagnostics.ElapsedSince(started);
            m_diagnostics.RecordV2TerminalEvaluation(
                elapsed,
                result.Status,
                result.EvaluatedBranches,
                result.EvaluatedFrontages,
                result.MaxRank);
            m_diagnostics.RecordV2MaxTerminalExtension(
                elapsed,
                $"anchor={straight.Next.Anchor} " +
                $"entry={straight.Next.EntryDirection} " +
                $"status={result.Status} reason={result.Reason} " +
                $"candidates={result.Candidates.Count} " +
                $"branches={result.EvaluatedBranches} " +
                $"eligibleFrontages={result.EvaluatedFrontages} " +
                $"rank={result.MaxRank}");
            if (result.Status != AccessV2TerminalStatus.Success)
                return;
            for (int index = 0; index < result.Candidates.Count; index++)
            {
                AccessV2TerminalCandidate candidate = result.Candidates[index];
                if (candidate.CompatibilityHandoff == null
                    || candidate.Transitions.Count == 0
                    || candidate.Transitions.Count
                        != candidate.Evaluations.Count)
                    continue;
                SearchNode cursor = predecessor;
                for (int step = 0;
                    step < candidate.Transitions.Count;
                    step++)
                {
                    AccessV2Transition transition = candidate.Transitions[step];
                    AccessV2TransitionEvaluation evaluation =
                        candidate.Evaluations[step];
                    AccessV2History nextHistory =
                        cursor.History.ApplyValidated(
                            transition,
                            evaluation.RayConstraints,
                            evaluation.CleanupKeys,
                            GetFixedSafetyExemptOrigins(
                                cursor.State, connectedFixedOrigin));
                    cursor = new SearchNode(
                        transition.Next,
                        nextHistory,
                        cursor.Cost + evaluation.TotalCost,
                        cursor.TraversalCost + evaluation.TraversalCost,
                        cursor.GeneratedWorkCost
                            + evaluation.GeneratedWorkCost,
                        cursor.DirectWorkCost + evaluation.DirectWorkCost,
                        cursor.GeneratedFixedCost
                            + evaluation.GeneratedFixedCost,
                        cursor.ExteriorRayCost + evaluation.ExteriorRayCost,
                        cursor.CleanupCost + evaluation.CleanupCost,
                        cursor,
                        transition,
                        null,
                        requiresGroundTransition:
                            evaluation.RequiresGroundTransition);
                }
                EnqueueTerminalGround(
                    cursor, candidate.CompatibilityHandoff);
            }
        }

        private void EnqueueTerminalGround(
            SearchNode parent,
            AccessV2HandoffCandidate handoff)
        {
            float cost = parent.Cost + handoff.TotalCost;
            if (cost > m_maxCost)
            {
                m_startTierHitCostLimit = true;
                return;
            }
            AccessV2History history = parent.History.ApplyCleanupKeys(
                handoff.CleanupKeys);
            for (int index = 0;
                index < handoff.GroundEntryCenters.Count;
                index++)
            {
                Tile2i entry = handoff.GroundEntryCenters[index];
                IReadOnlyList<AccessV2TravelAxis> axes =
                    GetFixedNavigationEntryAxes(entry);
                if (axes.Count == 0)
                    EnqueueTerminalEntry(null);
                else
                    for (int axisIndex = 0; axisIndex < axes.Count; axisIndex++)
                        EnqueueTerminalEntry(axes[axisIndex]);

                void EnqueueTerminalEntry(AccessV2TravelAxis? axis)
                {
                    Tile2i? portalRoot = axis.HasValue
                        ? null
                        : GetFixedNavigationPortalRoot(entry);
                    if (ShouldPruneHistoryQualifiedGroundEntry(
                            entry, cost, history, axis, portalRoot))
                        return;
                    Enqueue(new SearchNode(
                        parent.State,
                        history,
                        cost,
                        parent.TraversalCost + handoff.CenterSpokeCost,
                        parent.GeneratedWorkCost,
                        parent.DirectWorkCost,
                        parent.GeneratedFixedCost,
                        parent.ExteriorRayCost,
                        parent.CleanupCost + handoff.CleanupCost,
                        parent,
                        null,
                        handoff,
                        entry,
                        fixedNavigationAxis: axis,
                        fixedNavigationPortalRoot: portalRoot));
                }
            }
        }

        private void RecordTransitionEvaluationTiming(
            long started,
            AccessV2BandState? current,
            AccessV2Transition transition,
            string source)
        {
            long elapsed = AtdDiagnostics.ElapsedSince(started);
            m_diagnostics.V2TransitionEvaluationTicks += elapsed;
            if (elapsed > m_diagnostics.V2MaxTransitionEvaluationTicks)
            {
                string currentAnchor = current.HasValue
                    ? current.Value.Anchor.ToString()
                    : "none";
                m_diagnostics.RecordV2MaxTransitionEvaluation(
                    elapsed,
                    $"source={source} current={currentAnchor} " +
                    $"next={transition.Next.Anchor} " +
                    $"kind={transition.Kind} delta={transition.Delta.Count}");
            }
        }

        private void RecordHandoffEvaluationTiming(
            long started,
            string source,
            AccessV2BandState state,
            int recentCount,
            int candidateCount)
        {
            long elapsed = AtdDiagnostics.ElapsedSince(started);
            m_diagnostics.V2HandoffEvaluationTicks += elapsed;
            if (elapsed > m_diagnostics.V2MaxHandoffEvaluationTicks)
            {
                m_diagnostics.RecordV2MaxHandoffEvaluation(
                    elapsed,
                    $"source={source} anchor={state.Anchor} " +
                    $"entry={state.EntryDirection} band={state.Band.Kind} " +
                    $"recent={recentCount} candidates={candidateCount}");
            }
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

        private HandoffEnumerationContinuation?
            CreateHandoffEnumerationContinuation(SearchNode current)
        {
            if (m_handoffEvaluator == null)
                return null;

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
                node != null
                    && recent.Count < AccessV2Handoffs.MaxSpanLength;
                node = node.Parent)
            {
                if (node.GroundCenter.HasValue)
                    break;
                recent.Add(node.State);
            }

            long handoffStart = AtdDiagnostics.Timestamp();
            IReadOnlyList<AccessV2HandoffCandidate> candidates =
                m_handoffEvaluator(recent, current.History, null);
            RecordHandoffEvaluationTiming(
                handoffStart,
                "frontier",
                current.State,
                recent.Count,
                candidates.Count);
            m_diagnostics.RecordV2RouteHandoff(
                $"anchor={current.State.Anchor} " +
                $"entry={current.State.EntryDirection} " +
                $"band={current.State.Band.Kind} " +
                $"pathCost={FormatCost(current.Cost)} " +
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
                    candidates.SelectMany(candidate =>
                        candidate.GroundEntryCenters));
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
            if (candidates.Count == 0)
                return null;
            return new HandoffEnumerationContinuation(
                this, current, enteredFromGround, candidates);
        }

        private GroundSuffixContinuation?
            CreateGroundSuffixContinuation(SearchNode current)
        {
            string prefix =
                $"entry={current.GroundCenter.GetValueOrDefault()} " +
                $"fromAnchor={current.State.Anchor} " +
                $"pathCost={FormatCost(current.Cost)}";
            if (m_groundGraph == null
                || (m_potentialField == null && m_heuristicEvaluator == null)
                || !current.GroundCenter.HasValue)
            {
                m_diagnostics.RecordV2GroundSuffix(
                    prefix + " outcome=unavailable");
                return null;
            }
            if (!m_groundGraph.TryGetGoalDistance(
                    current.GroundCenter.Value, out float distance)
                || distance <= 0f)
            {
                m_diagnostics.RecordV2GroundSuffix(
                    prefix + " outcome=no-goal-distance");
                return null;
            }
            return new GroundSuffixContinuation(
                this, current, distance, prefix);
        }

        private enum ContinuationOutcome
        {
            Yielded,
            Completed,
            Succeeded,
            Cancelled,
        }

        private enum ContinuationResumeStage
        {
            None,
            ExpandGround,
            ExpandBand,
        }

        private readonly struct ContinuationAdvanceResult
        {
            public ContinuationOutcome Outcome { get; }
            public SearchNode? Terminal { get; }

            private ContinuationAdvanceResult(
                ContinuationOutcome outcome,
                SearchNode? terminal)
            {
                Outcome = outcome;
                Terminal = terminal;
            }

            public static ContinuationAdvanceResult Yielded()
                => new ContinuationAdvanceResult(
                    ContinuationOutcome.Yielded, null);

            public static ContinuationAdvanceResult Completed()
                => new ContinuationAdvanceResult(
                    ContinuationOutcome.Completed, null);

            public static ContinuationAdvanceResult Succeeded(
                SearchNode terminal)
                => new ContinuationAdvanceResult(
                    ContinuationOutcome.Succeeded, terminal);

            public static ContinuationAdvanceResult Cancelled()
                => new ContinuationAdvanceResult(
                    ContinuationOutcome.Cancelled, null);
        }

        /// <summary>
        /// Owns one resumable expansion. The session keeps exactly one
        /// instance so a yielded operation always resumes before queue work.
        /// </summary>
        private interface IAccessV2SearchContinuation
        {
            SearchNode Current { get; }
            string Phase { get; }

            ContinuationAdvanceResult Advance(
                AccessSearchSliceBudget? budget);
        }

        private sealed class GroundSuffixContinuation
            : IAccessV2SearchContinuation
        {
            private readonly AccessV2SearchSession m_owner;
            private readonly SearchNode m_current;
            private readonly string m_prefix;
            private readonly int m_maxSteps;
            private SearchNode m_cursor;
            private float m_distance;
            private int m_stepIndex;
            private int m_directionIndex;

            public SearchNode Current => m_current;
            public string Phase => "Ground suffix";

            public GroundSuffixContinuation(
                AccessV2SearchSession owner,
                SearchNode current,
                float distance,
                string prefix)
            {
                m_owner = owner;
                m_current = current;
                m_cursor = current;
                m_distance = distance;
                m_prefix = prefix;
                m_maxSteps = Math.Max(
                    1,
                    owner.m_groundGraph!.GroundNodeCount
                        + owner.m_groundGraph.CleanupNodeCount);
                owner.m_diagnostics.V2GroundSuffixAttempts++;
            }

            public ContinuationAdvanceResult Advance(
                AccessSearchSliceBudget? budget)
            {
                long started = AtdDiagnostics.Timestamp();

                ContinuationAdvanceResult Finish(
                    ContinuationAdvanceResult result)
                {
                    m_owner.m_diagnostics.V2GroundSuffixTicks +=
                        AtdDiagnostics.ElapsedSince(started);
                    return result;
                }

                while (m_stepIndex < m_maxSteps)
                {
                    if (budget?.CancellationRequested == true)
                        return Finish(
                            ContinuationAdvanceResult.Cancelled());
                    if (budget?.IsExpired == true)
                        return Finish(
                            ContinuationAdvanceResult.Yielded());

                    Tile2i from = m_cursor.GroundCenter!.Value;
                    if (m_owner.m_groundGraph!.IsGoal(from))
                    {
                        m_owner.m_diagnostics.V2GroundSuffixSuccesses++;
                        m_owner.m_diagnostics.V2GroundSuffixSteps +=
                            m_stepIndex;
                        m_owner.m_diagnostics.RecordV2GroundSuffix(
                            m_prefix +
                            $" outcome=success distance={
                                FormatCost(m_distance)} steps={m_stepIndex}");
                        return Finish(
                            ContinuationAdvanceResult.Succeeded(m_cursor));
                    }
                    if (!m_owner.m_groundGraph.TryGetGoalDistance(
                            from, out m_distance))
                        break;

                    SearchNode? nextNode = null;
                    while (m_directionIndex < s_groundDirections.Length)
                    {
                        if (budget?.CancellationRequested == true)
                            return Finish(
                                ContinuationAdvanceResult.Cancelled());
                        if (budget?.IsExpired == true)
                            return Finish(
                                ContinuationAdvanceResult.Yielded());

                        RelTile2i direction =
                            s_groundDirections[m_directionIndex++];
                        Tile2i next = from + direction;
                        if (!m_owner.m_groundGraph.TryGetGoalDistance(
                                next, out float nextDistance))
                            continue;
                        float stepCost =
                            AccessV2GroundGraph.GetStepCost(from, next);
                        if (Math.Abs(
                                m_distance - stepCost - nextDistance)
                            > 0.001f
                            || !m_owner.m_groundGraph.CanTraverse(
                                from, next))
                            continue;

                        IReadOnlyList<Tile2i> sweptCenters =
                            AccessV2GroundGraph.GetSweptCenters(
                                from, next);
                        if (m_owner.m_groundValidator != null
                            && sweptCenters.Any(center =>
                                !m_owner.m_groundValidator(
                                    center, m_cursor.History)))
                            continue;
                        long localEscapeStart =
                            AtdDiagnostics.Timestamp();
                        bool localEscapeValid =
                            m_owner.m_groundGraph.TryValidateLocalEscape(
                                sweptCenters,
                                m_cursor.History,
                                m_owner.m_cleanupCostScale,
                                out IReadOnlyCollection<string>
                                    cleanupKeys,
                                out float cleanupCost);
                        m_owner.m_diagnostics.V2LocalEscapeTicks +=
                            AtdDiagnostics.ElapsedSince(
                                localEscapeStart);
                        if (!localEscapeValid)
                            continue;

                        float nextCost = m_cursor.Cost
                            + stepCost + cleanupCost;
                        if (nextCost > m_owner.m_maxCost)
                        {
                            m_owner.m_startTierHitCostLimit = true;
                            continue;
                        }
                        nextNode = new SearchNode(
                            m_cursor.State,
                            m_cursor.History.ApplyCleanupKeys(
                                cleanupKeys),
                            nextCost,
                            m_cursor.TraversalCost + stepCost,
                            m_cursor.GeneratedWorkCost,
                            m_cursor.DirectWorkCost,
                            m_cursor.GeneratedFixedCost,
                            m_cursor.ExteriorRayCost,
                            m_cursor.CleanupCost + cleanupCost,
                            m_cursor,
                            null,
                            null,
                            next);
                        break;
                    }

                    if (nextNode == null)
                        break;
                    m_cursor = nextNode;
                    m_stepIndex++;
                    m_directionIndex = 0;
                }

                m_owner.m_diagnostics.V2GroundSuffixFallbacks++;
                m_owner.m_diagnostics.RecordV2GroundSuffix(
                    m_prefix +
                    $" outcome=fallback remainingDistance={
                        FormatCost(m_distance)}");
                return Finish(
                    ContinuationAdvanceResult.Completed());
            }
        }

        /// <summary>
        /// Resumes fixed-navigation paths, portals, and ground neighbors one
        /// data-dependent item at a time.
        /// </summary>
        private sealed class GroundExpansionContinuation
            : IAccessV2SearchContinuation
        {
            private readonly AccessV2SearchSession m_owner;
            private readonly SearchNode m_current;
            private readonly Tile2i m_from;
            private readonly HashSet<Tile2i> m_sparseDirections =
                new HashSet<Tile2i>();
            private readonly int m_incomingX;
            private readonly int m_incomingY;
            private readonly bool m_hasFixedNavigation;
            private GroundExpansionStage m_stage;
            private IReadOnlyList<AccessV2FixedNavigationMove>? m_moves;
            private int m_moveIndex;
            private AccessV2FixedNavigationMove m_move;
            private IReadOnlyList<Tile2i>? m_movePath;
            private int m_movePathIndex;
            private SearchNode? m_moveCursor;
            private IReadOnlyList<AccessV2FixedNavigationPortal>? m_portals;
            private int m_portalIndex;
            private AccessV2FixedNavigationPortal m_portal;
            private IReadOnlyList<Tile2i>? m_portalPath;
            private int m_portalPathIndex;
            private HashSet<Tile2i>? m_portalRequiredCenterSet;
            private IReadOnlyList<Tile2i>? m_portalRequiredCenters;
            private int m_portalRequiredCenterIndex;
            private SearchNode? m_portalCursor;
            private AccessV2History? m_portalHistory;
            private float m_portalCleanupCost;
            private int m_neighborIndex;
            private int m_neighborAxisIndex;
            private IReadOnlyList<AccessV2TravelAxis>? m_neighborAxes;
            private Tile2i m_neighborNext;
            private AccessV2History? m_neighborHistory;
            private float m_neighborStepCost;
            private float m_neighborCleanupCost;
            private Tile2i? m_neighborPortalRoot;
            private AccessV2TravelAxis? m_neighborFixedNavigationAxis;

            private enum GroundExpansionStage
            {
                LoadMove,
                ValidateMovePath,
                BuildMovePath,
                LoadPortal,
                CollectPortalPath,
                ValidatePortalCenters,
                BuildPortalPath,
                PrepareNeighbor,
                EnqueueNeighborAxes,
                GroundToV,
                Complete,
            }

            public string Phase
                => "Ground " + m_stage;

            public SearchNode Current => m_current;

            public GroundExpansionContinuation(
                AccessV2SearchSession owner,
                SearchNode current)
            {
                m_owner = owner;
                m_current = current;
                m_from = current.GroundCenter.GetValueOrDefault();
                if (current.Parent?.GroundCenter is Tile2i previousCenter)
                {
                    m_incomingX = Math.Sign(
                        m_from.X - previousCenter.X);
                    m_incomingY = Math.Sign(
                        m_from.Y - previousCenter.Y);
                }
                m_hasFixedNavigation = owner.m_fixedNavigationGraph != null
                    && current.FixedNavigationAxis.HasValue;
                m_stage = owner.m_groundGraph == null
                    ? GroundExpansionStage.Complete
                    : !m_hasFixedNavigation
                        ? GroundExpansionStage.PrepareNeighbor
                        : GroundExpansionStage.LoadMove;
                owner.m_diagnostics.V2GroundExpansions++;
            }

            public ContinuationAdvanceResult Advance(
                AccessSearchSliceBudget? budget)
            {
                long started = AtdDiagnostics.Timestamp();

                ContinuationAdvanceResult Finish(
                    ContinuationAdvanceResult result)
                {
                    m_owner.m_diagnostics.V2GroundExpansionTicks +=
                        AtdDiagnostics.ElapsedSince(started);
                    return result;
                }

                while (true)
                {
                    if (budget?.CancellationRequested == true)
                        return Finish(
                            ContinuationAdvanceResult.Cancelled());
                    if (budget?.IsExpired == true)
                        return Finish(
                            ContinuationAdvanceResult.Yielded());

                    switch (m_stage)
                    {
                        case GroundExpansionStage.LoadMove:
                            if (m_moves == null)
                                m_moves =
                                    m_owner.m_fixedNavigationGraph!.EnumerateMoves(
                                        m_current.FixedNavigationAxis!.Value,
                                        m_from);
                            if (m_moveIndex >= m_moves!.Count)
                            {
                                m_stage =
                                    GroundExpansionStage.LoadPortal;
                                continue;
                            }
                            m_move = m_moves[m_moveIndex];
                            m_movePath = m_move.ExactCenterPath;
                            if (m_movePath.Count < 2
                                || m_movePath[0] != m_from)
                            {
                                m_moveIndex++;
                                continue;
                            }
                            m_movePathIndex = 1;
                            m_stage =
                                GroundExpansionStage.ValidateMovePath;
                            continue;

                        case GroundExpansionStage.ValidateMovePath:
                            if (m_movePathIndex < m_movePath!.Count)
                            {
                                Tile2i center =
                                    m_movePath[m_movePathIndex++];
                                if (m_owner.m_groundValidator != null
                                    && !m_owner.m_groundValidator(
                                        center, m_current.History))
                                {
                                    m_moveIndex++;
                                    m_stage =
                                        GroundExpansionStage.LoadMove;
                                    continue;
                                }
                                continue;
                            }
                            m_sparseDirections.Add(new Tile2i(
                                Math.Sign(m_move.Center.X - m_from.X),
                                Math.Sign(m_move.Center.Y - m_from.Y)));
                            m_moveCursor = m_current;
                            m_movePathIndex = 1;
                            m_stage =
                                GroundExpansionStage.BuildMovePath;
                            continue;

                        case GroundExpansionStage.BuildMovePath:
                            if (m_movePathIndex < m_movePath!.Count)
                            {
                                Tile2i center =
                                    m_movePath[m_movePathIndex];
                                float stepCost =
                                    AccessV2GroundGraph.GetStepCost(
                                        m_movePath[m_movePathIndex - 1],
                                        center);
                                m_moveCursor = new SearchNode(
                                    m_current.State,
                                    m_current.History,
                                    m_moveCursor!.Cost + stepCost,
                                    m_moveCursor.TraversalCost + stepCost,
                                    m_current.GeneratedWorkCost,
                                    m_current.DirectWorkCost,
                                    m_current.GeneratedFixedCost,
                                    m_current.ExteriorRayCost,
                                    m_current.CleanupCost,
                                    m_moveCursor,
                                    null,
                                    null,
                                    groundCenter: center,
                                    fixedNavigationAxis:
                                        m_movePathIndex
                                            == m_movePath.Count - 1
                                            ? m_move.Axis
                                            : m_current.FixedNavigationAxis);
                                m_movePathIndex++;
                                continue;
                            }
                            if (m_moveCursor!.Cost <= m_owner.m_maxCost)
                                m_owner.Enqueue(m_moveCursor);
                            else
                                m_owner.m_startTierHitCostLimit = true;
                            m_moveIndex++;
                            m_stage = GroundExpansionStage.LoadMove;
                            continue;

                        case GroundExpansionStage.LoadPortal:
                            if (m_portals == null)
                                m_portals = m_owner.m_fixedNavigationGraph
                                    !.EnumerateExitPortals(
                                        m_current.FixedNavigationAxis!.Value,
                                        m_from);
                            if (m_portalIndex >= m_portals.Count)
                            {
                                m_stage =
                                    GroundExpansionStage.PrepareNeighbor;
                                continue;
                            }
                            m_portal = m_portals[m_portalIndex];
                            m_portalPath = m_portal.ExactCenterPath;
                            if (m_portalPath.Count < 2)
                            {
                                m_portalIndex++;
                                continue;
                            }
                            m_portalRequiredCenterSet =
                                new HashSet<Tile2i>();
                            m_portalPathIndex = 1;
                            m_stage =
                                GroundExpansionStage.CollectPortalPath;
                            continue;

                        case GroundExpansionStage.CollectPortalPath:
                            if (m_portalPathIndex < m_portalPath!.Count)
                            {
                                m_portalRequiredCenterSet!.UnionWith(
                                    AccessV2GroundGraph.GetSweptCenters(
                                        m_portalPath[m_portalPathIndex - 1],
                                        m_portalPath[m_portalPathIndex]));
                                m_portalPathIndex++;
                                continue;
                            }
                            m_portalRequiredCenters =
                                m_portalRequiredCenterSet.ToArray();
                            m_portalRequiredCenterIndex = 0;
                            m_stage =
                                GroundExpansionStage.ValidatePortalCenters;
                            continue;

                        case GroundExpansionStage.ValidatePortalCenters:
                            if (m_portalRequiredCenterIndex
                                < m_portalRequiredCenters!.Count)
                            {
                                Tile2i center =
                                    m_portalRequiredCenters[
                                        m_portalRequiredCenterIndex++];
                                if (m_owner.m_groundValidator != null
                                    && !m_owner.m_groundValidator(
                                        center, m_current.History))
                                {
                                    m_portalIndex++;
                                    m_stage =
                                        GroundExpansionStage.LoadPortal;
                                    continue;
                                }
                                continue;
                            }
                            if (!m_owner.m_groundGraph!.TryValidateLocalEscape(
                                    m_portalRequiredCenters,
                                    m_current.History,
                                    m_owner.m_cleanupCostScale,
                                    out IReadOnlyCollection<string>
                                        cleanupKeys,
                                    out m_portalCleanupCost))
                            {
                                m_portalIndex++;
                                m_stage =
                                    GroundExpansionStage.LoadPortal;
                                continue;
                            }
                            m_portalHistory = m_current.History
                                .ApplyCleanupKeys(cleanupKeys);
                            m_portalCursor = m_current;
                            m_portalPathIndex = 1;
                            m_stage =
                                GroundExpansionStage.BuildPortalPath;
                            continue;

                        case GroundExpansionStage.BuildPortalPath:
                            if (m_portalPathIndex < m_portalPath!.Count)
                            {
                                Tile2i center =
                                    m_portalPath[m_portalPathIndex];
                                float stepCost =
                                    AccessV2GroundGraph.GetStepCost(
                                        m_portalPath[m_portalPathIndex - 1],
                                        center);
                                bool isLast = m_portalPathIndex
                                    == m_portalPath.Count - 1;
                                m_portalCursor = new SearchNode(
                                    m_current.State,
                                    m_portalHistory!,
                                    m_portalCursor!.Cost + stepCost
                                        + (isLast
                                            ? m_portalCleanupCost
                                            : 0f),
                                    m_portalCursor.TraversalCost + stepCost,
                                    m_current.GeneratedWorkCost,
                                    m_current.DirectWorkCost,
                                    m_current.GeneratedFixedCost,
                                    m_current.ExteriorRayCost,
                                    m_portalCursor.CleanupCost
                                        + (isLast
                                            ? m_portalCleanupCost
                                            : 0f),
                                    m_portalCursor,
                                    null,
                                    null,
                                    groundCenter: center,
                                    fixedNavigationAxis:
                                        isLast
                                            ? null
                                            : m_current.FixedNavigationAxis);
                                m_portalPathIndex++;
                                continue;
                            }
                            if (m_portalCursor!.Cost
                                <= m_owner.m_maxCost)
                                m_owner.Enqueue(m_portalCursor);
                            else
                                m_owner.m_startTierHitCostLimit = true;
                            m_portalIndex++;
                            m_stage = GroundExpansionStage.LoadPortal;
                            continue;

                        case GroundExpansionStage.PrepareNeighbor:
                            if (m_neighborIndex >= s_groundDirections.Length)
                            {
                                m_stage = GroundExpansionStage.GroundToV;
                                continue;
                            }
                            RelTile2i direction =
                                s_groundDirections[m_neighborIndex];
                            if (m_sparseDirections.Contains(new Tile2i(
                                    direction.X, direction.Y))
                                || m_incomingX * direction.X
                                    + m_incomingY * direction.Y < 0)
                            {
                                m_neighborIndex++;
                                continue;
                            }
                            Tile2i next = m_from + direction;
                            if (!m_owner.m_groundGraph!.CanTraverse(
                                    m_from, next))
                            {
                                m_neighborIndex++;
                                continue;
                            }
                            bool nextIsProjected =
                                m_owner.m_groundGraph.IsProjectedFixedGround(
                                    next);
                            if (m_current.FixedNavigationAxis.HasValue
                                && nextIsProjected)
                            {
                                m_neighborIndex++;
                                continue;
                            }
                            Tile2i? portalRoot =
                                m_current.FixedNavigationPortalRoot;
                            if (nextIsProjected
                                && (portalRoot.HasValue
                                    || !m_owner.m_groundGraph
                                        .IsProjectedFixedGround(m_from)))
                            {
                                portalRoot ??= m_from;
                                if (Math.Max(
                                        Math.Abs(next.X - portalRoot.Value.X),
                                        Math.Abs(next.Y - portalRoot.Value.Y))
                                    > FixedNavigationPortalRadius)
                                {
                                    m_neighborIndex++;
                                    continue;
                                }
                            }
                            m_neighborNext = next;
                            m_neighborPortalRoot = nextIsProjected
                                ? portalRoot
                                : null;
                            m_neighborFixedNavigationAxis = nextIsProjected
                                ? m_current.FixedNavigationAxis
                                : null;
                            m_neighborStepCost =
                                AccessV2GroundGraph.GetStepCost(
                                    m_from, next);
                            m_neighborAxes = m_owner.m_fixedNavigationGraph
                                    != null && nextIsProjected
                                ? m_owner.m_fixedNavigationGraph.GetNodeAxes(
                                    next)
                                : Array.Empty<AccessV2TravelAxis>();
                            if (m_current.Handoff != null
                                && m_owner
                                    .AreAllGroundSuccessorsOptimisticallyDominated(
                                        next,
                                        m_neighborAxes,
                                        m_neighborFixedNavigationAxis,
                                        m_neighborPortalRoot,
                                        m_current.Cost
                                            + m_neighborStepCost))
                            {
                                m_neighborIndex++;
                                continue;
                            }
                            IReadOnlyList<Tile2i> sweptCenters =
                                AccessV2GroundGraph.GetSweptCenters(
                                    m_from, next);
                            if (m_owner.m_groundValidator != null
                                && sweptCenters.Any(center =>
                                    !m_owner.m_groundValidator(
                                        center, m_current.History)))
                            {
                                m_neighborIndex++;
                                continue;
                            }
                            long localEscapeStart =
                                AtdDiagnostics.Timestamp();
                            bool localEscapeValid =
                                m_owner.m_groundGraph.TryValidateLocalEscape(
                                    sweptCenters,
                                    m_current.History,
                                    m_owner.m_cleanupCostScale,
                                    out IReadOnlyCollection<string>
                                        neighborCleanupKeys,
                                    out m_neighborCleanupCost);
                            m_owner.m_diagnostics.V2LocalEscapeTicks +=
                                AtdDiagnostics.ElapsedSince(
                                    localEscapeStart);
                            if (!localEscapeValid)
                            {
                                m_neighborIndex++;
                                continue;
                            }
                            m_neighborHistory = m_current.History
                                .ApplyCleanupKeys(neighborCleanupKeys);
                            float nextCost = m_current.Cost
                                + m_neighborStepCost
                                + m_neighborCleanupCost;
                            if (nextCost > m_owner.m_maxCost)
                            {
                                m_owner.m_startTierHitCostLimit = true;
                                m_neighborIndex++;
                                continue;
                            }
                            m_neighborAxisIndex = 0;
                            m_stage =
                                GroundExpansionStage.EnqueueNeighborAxes;
                            continue;

                        case GroundExpansionStage.EnqueueNeighborAxes:
                            if (m_neighborAxes!.Count == 0)
                            {
                                EnqueueNeighbor(
                                    m_neighborFixedNavigationAxis,
                                    m_neighborPortalRoot);
                                m_neighborIndex++;
                                m_stage =
                                    GroundExpansionStage.PrepareNeighbor;
                                continue;
                            }
                            if (m_neighborAxisIndex < m_neighborAxes.Count)
                            {
                                EnqueueNeighbor(
                                    m_neighborAxes[
                                        m_neighborAxisIndex++],
                                    null);
                                continue;
                            }
                            m_neighborIndex++;
                            m_stage =
                                GroundExpansionStage.PrepareNeighbor;
                            continue;

                        case GroundExpansionStage.GroundToV:
                            if (HasCanonicalGroundToVLaunchPosition(
                                    m_from))
                            {
                                m_owner.m_diagnostics.V2GroundToVCalls++;
                                long groundToVStart =
                                    AtdDiagnostics.Timestamp();
                                m_owner.ExpandGroundToV(m_current);
                                m_owner.m_diagnostics.V2GroundToVTicks +=
                                    AtdDiagnostics.ElapsedSince(
                                        groundToVStart);
                            }
                            m_stage = GroundExpansionStage.Complete;
                            continue;

                        case GroundExpansionStage.Complete:
                            return Finish(
                                ContinuationAdvanceResult.Completed());
                    }
                }

                void EnqueueNeighbor(
                    AccessV2TravelAxis? fixedNavigationAxis,
                    Tile2i? fixedNavigationPortalRoot)
                {
                    float nextCost = m_current.Cost
                        + m_neighborStepCost
                        + m_neighborCleanupCost;
                    m_owner.Enqueue(new SearchNode(
                        m_current.State,
                        m_neighborHistory!,
                        nextCost,
                        m_current.TraversalCost
                            + m_neighborStepCost,
                        m_current.GeneratedWorkCost,
                        m_current.DirectWorkCost,
                        m_current.GeneratedFixedCost,
                        m_current.ExteriorRayCost,
                        m_current.CleanupCost
                            + m_neighborCleanupCost,
                        m_current,
                        null,
                        null,
                        m_neighborNext,
                        fixedNavigationAxis:
                            fixedNavigationAxis,
                        fixedNavigationPortalRoot:
                            fixedNavigationPortalRoot));
                }
            }
        }

        /// <summary>
        /// Resumes candidate filtering and ground-entry/axis enumeration after
        /// the lane candidate evaluator has returned its immutable list.
        /// </summary>
        private sealed class HandoffEnumerationContinuation
            : IAccessV2SearchContinuation
        {
            private readonly AccessV2SearchSession m_owner;
            private readonly SearchNode m_current;
            private readonly Tile2i? m_enteredFromGround;
            private readonly IReadOnlyList<AccessV2HandoffCandidate>
                m_candidates;
            private int m_candidateIndex;
            private AccessV2HandoffCandidate? m_handoff;
            private float m_cost;
            private AccessV2History? m_handoffHistory;
            private int m_entryIndex;
            private Tile2i m_entry;
            private IReadOnlyList<AccessV2TravelAxis>? m_axes;
            private int m_axisIndex;

            public SearchNode Current => m_current;
            public string Phase => "Handoff entries";

            public HandoffEnumerationContinuation(
                AccessV2SearchSession owner,
                SearchNode current,
                Tile2i? enteredFromGround,
                IReadOnlyList<AccessV2HandoffCandidate> candidates)
            {
                m_owner = owner;
                m_current = current;
                m_enteredFromGround = enteredFromGround;
                m_candidates = candidates;
            }

            public ContinuationAdvanceResult Advance(
                AccessSearchSliceBudget? budget)
            {
                long started = AtdDiagnostics.Timestamp();

                ContinuationAdvanceResult Finish(
                    ContinuationAdvanceResult result)
                {
                    long elapsed = AtdDiagnostics.ElapsedSince(started);
                    m_owner.m_diagnostics.V2BandExpansionTicks += elapsed;
                    if (elapsed
                        > m_owner.m_diagnostics.V2MaxHandoffContinuationTicks)
                    {
                        m_owner.m_diagnostics
                            .RecordV2MaxHandoffContinuation(
                                elapsed,
                                $"anchor={m_current.State.Anchor} " +
                                $"entry={m_current.State.EntryDirection} " +
                                $"candidates={m_candidates.Count} " +
                                $"candidateIndex={m_candidateIndex} " +
                                $"entryIndex={m_entryIndex}");
                    }
                    return result;
                }

                while (true)
                {
                    if (budget?.CancellationRequested == true)
                        return Finish(
                            ContinuationAdvanceResult.Cancelled());
                    if (budget?.IsExpired == true)
                        return Finish(
                            ContinuationAdvanceResult.Yielded());

                    if (m_handoff == null)
                    {
                        if (m_candidateIndex >= m_candidates.Count)
                            return Finish(
                                ContinuationAdvanceResult.Completed());
                        m_handoff = m_candidates[m_candidateIndex];
                        if (m_enteredFromGround.HasValue
                            && m_handoff.GroundEntryCenters.Contains(
                                m_enteredFromGround.Value))
                        {
                            m_candidateIndex++;
                            m_handoff = null;
                            continue;
                        }
                        m_cost = m_current.Cost
                            + m_handoff.TotalCost;
                        if (m_cost > m_owner.m_maxCost)
                        {
                            m_owner.m_startTierHitCostLimit = true;
                            m_candidateIndex++;
                            m_handoff = null;
                            continue;
                        }
                        if (m_owner.m_handoffDominance.IsDominated(
                                m_current, m_handoff, m_cost))
                        {
                            m_owner.m_diagnostics
                                .V2HandoffDominancePrunes++;
                            m_candidateIndex++;
                            m_handoff = null;
                            continue;
                        }
                        m_handoffHistory = m_current.History
                            .ApplyCleanupKeys(m_handoff.CleanupKeys);
                        if (m_handoff.GroundEntryCenters.Count > 0
                            && m_owner.m_handoffDominance.RecordSuccess(
                                m_current, m_handoff, m_cost))
                            m_owner.m_diagnostics
                                .V2HandoffDominanceSuccesses++;
                        m_entryIndex = 0;
                    }

                    if (m_entryIndex >= m_handoff.GroundEntryCenters.Count)
                    {
                        m_candidateIndex++;
                        m_handoff = null;
                        continue;
                    }

                    m_entry = m_handoff.GroundEntryCenters[
                        m_entryIndex];
                    if (m_owner.m_groundGraph != null)
                    {
                        AccessV2PotentialOwner handoffOwner =
                            m_current.PotentialOwner.Advance(
                                m_owner.m_groundGraph,
                                AccessV2PotentialField.GetCanonicalCenter(
                                    m_current.State),
                                m_entry);
                        if (!handoffOwner.CanReturnTo(
                                m_owner.m_groundGraph, m_entry))
                        {
                            m_owner.Reject(
                                "SameComponentReturnBeforeVCommitment");
                            m_entryIndex++;
                            continue;
                        }
                    }
                    m_axes = m_owner.GetFixedNavigationEntryAxes(m_entry);
                    m_axisIndex = 0;
                    if (m_axes.Count == 0)
                    {
                        EnqueueEntry(null);
                        m_entryIndex++;
                        continue;
                    }
                    if (m_axisIndex < m_axes.Count)
                    {
                        EnqueueEntry(m_axes[m_axisIndex++]);
                        continue;
                    }
                    m_entryIndex++;
                }

                void EnqueueEntry(AccessV2TravelAxis? axis)
                {
                    Tile2i? portalRoot = axis.HasValue
                        ? null
                        : m_owner.GetFixedNavigationPortalRoot(m_entry);
                    if (m_owner.ShouldPruneHistoryQualifiedGroundEntry(
                            m_entry, m_cost, m_handoffHistory!,
                            axis, portalRoot))
                        return;
                    m_owner.Enqueue(new SearchNode(
                        m_current.State,
                        m_handoffHistory!,
                        m_cost,
                        m_current.TraversalCost
                            + m_handoff!.CenterSpokeCost,
                        m_current.GeneratedWorkCost,
                        m_current.DirectWorkCost,
                        m_current.GeneratedFixedCost,
                        m_current.ExteriorRayCost,
                        m_current.CleanupCost
                            + m_handoff.CleanupCost,
                        m_current,
                        null,
                        m_handoff,
                        m_entry,
                        fixedNavigationAxis: axis,
                        fixedNavigationPortalRoot: portalRoot));
                }
            }
        }

        /// <summary>
        /// Expands one V-band at transition-item boundaries. A complete band
        /// expansion used to be one atomic operation, even when the transition
        /// evaluator or predecessor search did substantial data-dependent
        /// work. The continuation keeps the exact expansion order while
        /// allowing the shared slice deadline to yield between straight,
        /// strafe, and turn items.
        /// </summary>
        private sealed class BandExpansionContinuation
            : IAccessV2SearchContinuation
        {
            private readonly AccessV2SearchSession m_owner;
            private readonly SearchNode m_current;
            private readonly IReadOnlyList<AccessV2Transition>
                m_straightTransitions;
            private int m_straightIndex;
            private int m_turnSign = -1;

            public SearchNode Current => m_current;
            public string Phase => "V2 frontier";

            public BandExpansionContinuation(
                AccessV2SearchSession owner,
                SearchNode current)
            {
                m_owner = owner;
                m_current = current;
                m_straightTransitions =
                    AccessV2Geometry.EnumerateStraight(current.State)
                        .ToArray();
            }

            public ContinuationAdvanceResult Advance(
                AccessSearchSliceBudget? budget)
            {
                long started = AtdDiagnostics.Timestamp();

                ContinuationAdvanceResult Finish(
                    ContinuationAdvanceResult result)
                {
                    long elapsed = AtdDiagnostics.ElapsedSince(started);
                    m_owner.m_diagnostics.V2BandExpansionTicks += elapsed;
                    if (elapsed
                        > m_owner.m_diagnostics
                            .V2MaxFrontierContinuationTicks)
                    {
                        m_owner.m_diagnostics
                            .RecordV2MaxFrontierContinuation(
                                elapsed,
                                $"anchor={m_current.State.Anchor} " +
                                $"entry={m_current.State.EntryDirection} " +
                                $"straightIndex={m_straightIndex}/" +
                                $"{m_straightTransitions.Count} " +
                                $"turnSign={m_turnSign}");
                    }
                    return result;
                }

                while (true)
                {
                    if (budget?.CancellationRequested == true)
                        return Finish(
                            ContinuationAdvanceResult.Cancelled());
                    if (budget?.IsExpired == true)
                        return Finish(
                            ContinuationAdvanceResult.Yielded());

                    if (m_straightIndex < m_straightTransitions.Count)
                    {
                        m_owner.TryRelax(
                            m_current,
                            m_straightTransitions[m_straightIndex++],
                            budget);
                        continue;
                    }

                    // A turn-pending band has no strafe/turn successors. This
                    // is the same early return as Expand(), after all
                    // straight successors have been attempted.
                    if (m_current.State.IsTurnPending
                        || m_turnSign > 1)
                        return Finish(
                            ContinuationAdvanceResult.Completed());

                    int sign = m_turnSign;
                    m_turnSign += 2;
                    AccessV2Transition? turn = null;
                    string turnReason = string.Empty;
                    if (TryFindTurnPredecessor(
                            m_current,
                            out AccessV2BandState predecessor))
                    {
                        if (AccessV2Geometry.TryTurn(
                                predecessor,
                                m_current.State,
                                sign,
                                out AccessV2Transition candidateTurn,
                                out turnReason))
                            turn = candidateTurn;
                        else
                            m_owner.Reject(turnReason);
                    }
                    else if (AccessV2Geometry.TryTurn(
                                m_current.State,
                                m_current.History,
                                sign,
                                out AccessV2Transition historyTurn,
                                out turnReason))
                    {
                        turn = historyTurn;
                    }
                    else
                    {
                        m_owner.Reject(turnReason);
                    }

                    // Preserve Expand()'s canonical flat-strafe suppression
                    // and the exact strafe-before-turn ordering.
                    if (turn != null
                        && m_current.State.Band.IsCompletelyFlat)
                    {
                        m_owner.Reject("FlatStrafeDominatedByTurn");
                    }
                    else if (!TryGetStrafePredecessorProfile(
                                m_current,
                                sign,
                                out AccessHeightProfile predecessorProfile))
                    {
                        m_owner.Reject("StrafePredecessorProfileMissing");
                    }
                    else if (AccessV2Geometry.TryStrafe(
                                m_current.State,
                                sign,
                                predecessorProfile,
                                out AccessV2Transition strafe,
                                out string strafeReason))
                    {
                        m_owner.TryRelax(m_current, strafe, budget);
                    }
                    else
                    {
                        m_owner.Reject(strafeReason);
                    }

                    if (turn != null)
                        m_owner.TryRelax(m_current, turn, budget);
                }
            }
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
            long handoffStart = AtdDiagnostics.Timestamp();
            AccessV2HandoffCandidate? seam =
                m_groundToVHandoffEvaluator(
                    adapter.Next, groundCenter,
                    AccessHandoffOperation.Leveling,
                    groundNode.History);
            RecordHandoffEvaluationTiming(
                handoffStart,
                "ground-to-v-adapter",
                adapter.Next,
                1,
                seam == null ? 0 : 1);
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
            RecordGroundToVEnqueue();
            m_diagnostics.V2GroundToVSeedExtensions++;
            m_diagnostics.V2GroundToVCacheInsertions++;
            return true;
        }

        private void ExpandGroundToVAdapter(
            SearchNode adapterNode,
            AccessSearchSliceBudget? sliceBudget = null)
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
                TryRelax(adapterNode, resolved, sliceBudget);
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
                    TryRelax(adapterNode, resolved, sliceBudget);
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
            RecordTransitionEvaluationTiming(
                evaluationStart,
                null,
                transition,
                "ground-to-v");
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
                RecordHandoffEvaluationTiming(
                    handoffStart,
                    "ground-to-v",
                    state,
                    1,
                    seam == null ? 0 : 1);
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
            RecordGroundToVEnqueue();
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
            if (m_activeGroundExpansionTrace != null)
            {
                if (node.GroundCenter.HasValue)
                    m_activeGroundExpansionTrace.GroundEnqueueAttempts++;
                else
                    m_activeGroundExpansionTrace.VEnqueueAttempts++;
            }
            node.PotentialOwner = GetPotentialOwnerForV(
                node.Parent, node.State, node.Transition);
            if (!m_emittingOrdinaryGroundReplacement
                && ShouldPruneByOrdinaryGroundReplacement(node))
                return false;
            if (ExpansionTraced != null)
            {
                m_expansionTraceProvenance ??=
                    new Dictionary<SearchNode, ExpansionTraceProvenance>();
                ExpansionTraceProvenance provenance = default;
                if (!node.GroundCenter.HasValue
                    && node.Parent?.GroundCenter.HasValue == true)
                {
                    provenance.GroundLaunchCenter =
                        node.Parent.GroundCenter;
                    provenance.LaunchHistoryOrigins =
                        node.Parent.History.OriginCount;
                    provenance.LaunchHistoryRayConstraints =
                        node.Parent.History.RayConstraintCount;
                    provenance.LaunchHistoryCleanupKeys =
                        node.Parent.History.CleanupKeyCount;
                }
                else if (node.Parent != null)
                    m_expansionTraceProvenance.TryGetValue(
                        node.Parent, out provenance);
                if (provenance.GroundLaunchCenter.HasValue
                    && !provenance.PotentialMergeCenter.HasValue
                    && node.Parent != null
                    && !node.Parent.PotentialOwner.IsGlobal
                    && node.PotentialOwner.IsGlobal)
                    provenance.PotentialMergeCenter = node.GroundCenter
                        ?? AccessV2PotentialField.GetCanonicalCenter(
                            node.State);
                if (provenance.GroundLaunchCenter.HasValue)
                    m_expansionTraceProvenance[node] = provenance;
            }
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
            if (node.GroundCenter.HasValue
                && node.Handoff == null
                && (!m_bestGroundNodesByCenter.TryGetValue(
                        node.GroundCenter.Value,
                        out SearchNode knownGround)
                    || node.Cost < knownGround.Cost - 0.0001f))
                m_bestGroundNodesByCenter[node.GroundCenter.Value] = node;
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
            if (m_activeGroundExpansionTrace != null)
            {
                if (node.GroundCenter.HasValue)
                    m_activeGroundExpansionTrace.GroundEnqueueAccepted++;
                else
                    m_activeGroundExpansionTrace.VEnqueueAccepted++;
            }
            m_queueCount++;
            m_maxHistoryOrigins = Math.Max(
                m_maxHistoryOrigins, node.History.OriginCount);
            m_maxRayConstraints = Math.Max(
                m_maxRayConstraints, node.History.RayConstraintCount);
            return true;
        }

        /// <summary>
        /// A V label reached through a detour cannot be made cheaper by work
        /// credited from that detour: the credited work was already charged
        /// to the prefix.  Before retaining the label, ask whether an already
        /// cheaper concrete-G label can generate the identical V interface
        /// through the real seam and transition evaluators.  Successful
        /// regeneration is an exact cheapest-arrival replacement; failed or
        /// more expensive regeneration proves nothing and remains untouched.
        /// </summary>
        private bool ShouldPruneByOrdinaryGroundReplacement(SearchNode node)
        {
            if (node.GroundCenter.HasValue
                || node.Parent == null
                || node.IsGroundToVAdapter
                || (node.Parent.GroundCenter.HasValue
                    && !m_evaluateDirectGroundReplacementDominance)
                || (!node.PotentialOwner.IsGlobal
                    && !node.Parent.GroundCenter.HasValue)
                || m_groundGraph == null
                || m_groundToVHandoffEvaluator == null
                || m_terrainCenterHeightProvider == null)
                return false;

            if (!node.Parent.GroundCenter.HasValue)
            {
                if (!node.IsGroundRelaunchedV
                    || m_groundHeightProvider == null)
                    return false;
                Tile2i center =
                    AccessV2PotentialField.GetCanonicalCenter(node.State);
                int? groundHeight2 = m_groundHeightProvider(center);
                if (!groundHeight2.HasValue
                    || Math.Abs(
                        groundHeight2.Value
                            - node.State.Band.Lane0.Center2) > 2)
                    return false;
            }

            m_diagnostics.V2OrdinaryGroundReplacementChecks++;
            Tile2i anchor = node.State.Anchor;
            Tile2i travel = node.State.EntryDirection;
            AccessV2TravelAxis axis = node.State.Axis;

            // A containing band has exactly sixteen canonical vehicle-center
            // launch positions. Projected G may additionally launch through
            // the immediately outward fringe, giving at most 32 dictionary
            // probes instead of scanning a component or a bounding square.
            for (int fringe = 0; fringe <= 1; fringe++)
            {
                Tile2i containingAnchor = fringe == 0
                    ? anchor
                    : new Tile2i(
                        anchor.X - travel.X,
                        anchor.Y - travel.Y);
                for (int longitudinal = 0; longitudinal < 4; longitudinal++)
                {
                    for (int transverse = 0; transverse < 4; transverse++)
                    {
                        Tile2i ground = axis == AccessV2TravelAxis.X
                            ? new Tile2i(
                                containingAnchor.X + (travel.X > 0
                                    ? longitudinal
                                    : longitudinal + 1),
                                containingAnchor.Y + transverse + 2)
                            : new Tile2i(
                                containingAnchor.X + transverse + 2,
                                containingAnchor.Y + (travel.Y > 0
                                    ? longitudinal
                                    : longitudinal + 1));
                        if (!m_bestGroundNodesByCenter.TryGetValue(
                                ground, out SearchNode groundNode)
                            || groundNode.Cost > node.Cost + 0.0001f
                            || (fringe != 0
                                && !m_groundGraph.IsProjectedFixedGround(ground))
                            || !CanUseCanonicalGroundToVLaunchPosition(
                                ground, travel))
                            continue;

                        bool matchingAnchor = false;
                        foreach (Tile2i candidateAnchor in
                            EnumerateGroundToVBandAnchors(
                                ground, travel,
                                includeOutwardFringe:
                                    m_groundGraph.IsProjectedFixedGround(ground)))
                        {
                            if (candidateAnchor == anchor)
                            {
                                matchingAnchor = true;
                                break;
                            }
                        }
                        if (!matchingAnchor)
                            continue;

                        AccessV2PotentialOwner replacementOwner =
                            GetPotentialOwnerForV(
                                groundNode, node.State, transition: null);
                        if (IsVBandCostKnownNoWorse(
                                node.State, node.Cost,
                                strictSelfDisruption: false,
                                replacementOwner))
                        {
                            m_diagnostics.V2OrdinaryGroundReplacementPrunes++;
                            return true;
                        }

                        float terrainHeight =
                            m_preciseTerrainHeightProvider?.Invoke(ground)
                            ?? m_terrainCenterHeightProvider(ground) / 2f;
                        bool emitted = false;
                        m_emittingOrdinaryGroundReplacement = true;
                        try
                        {
                            if (Math.Abs(
                                    terrainHeight - Math.Round(terrainHeight))
                                <= 0.0001f)
                            {
                                foreach (GroundToVProfileCandidate candidate in
                                    EnumerateDirectLevelingProfiles(
                                        (int)Math.Round(terrainHeight),
                                        axis, travel))
                                {
                                    if (!IsExactBand(node.State, candidate))
                                        continue;
                                    m_diagnostics
                                        .V2OrdinaryGroundReplacementCandidates++;
                                    emitted |= TryEmitGroundToV(
                                        groundNode, ground, anchor, axis, travel,
                                        candidate, directLeveling: true);
                                }
                            }

                            foreach (GroundToVProfileCandidate candidate in
                                EnumerateGroundToVBandProfiles(
                                    anchor, terrainHeight, axis, travel,
                                    m_fixedProfileProvider,
                                    m_generatedVPrimeOriginValidator))
                            {
                                if (!IsExactBand(node.State, candidate))
                                    continue;
                                m_diagnostics
                                    .V2OrdinaryGroundReplacementCandidates++;
                                emitted |= TryEmitGroundToV(
                                    groundNode, ground, anchor, axis, travel,
                                    candidate, directLeveling: false);
                            }
                        }
                        finally
                        {
                            m_emittingOrdinaryGroundReplacement = false;
                        }

                        if (!emitted)
                            continue;
                        if (!IsVBandCostKnownNoWorse(
                                node.State, node.Cost,
                                strictSelfDisruption: false,
                                replacementOwner))
                            continue;

                        m_diagnostics.V2OrdinaryGroundReplacementPrunes++;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsExactBand(
            AccessV2BandState state,
            GroundToVProfileCandidate candidate)
            => AccessV2BandProfile.TryCreate(
                    state.Axis,
                    candidate.Lane0,
                    candidate.Lane1,
                    includeDeferred: true,
                    out AccessV2BandProfile band,
                    out _)
                && band.Equals(state.Band);

        private bool AreAllGroundSuccessorsOptimisticallyDominated(
            Tile2i center,
            IReadOnlyList<AccessV2TravelAxis> axes,
            AccessV2TravelAxis? fallbackAxis,
            Tile2i? portalRoot,
            float optimisticCost)
        {
            m_diagnostics.V2HandoffGroundDominanceChecks++;
            if (axes.Count == 0)
            {
                if (!IsGroundSuccessorOptimisticallyDominated(
                        center, fallbackAxis, portalRoot, optimisticCost))
                    return false;
            }
            else
            {
                for (int index = 0; index < axes.Count; index++)
                    if (!IsGroundSuccessorOptimisticallyDominated(
                            center, axes[index], null, optimisticCost))
                        return false;
            }
            m_diagnostics.V2HandoffGroundDominancePrunes++;
            return true;
        }

        /// <summary>
        /// A history-qualified handoff entry is useful only through its first
        /// consequence. For plain physical ground, if every geometrically
        /// possible ordinary-G successor already has an equal or cheaper
        /// history-free label even at zero cleanup cost, the entry cannot
        /// improve the search. Goal-connected, G-to-V-capable, projected, and
        /// fixed-navigation entries remain conservative because their
        /// consequences can depend on the handoff history or navigation seam.
        /// </summary>
        private bool ShouldPruneHistoryQualifiedGroundEntry(
            Tile2i center,
            float cost,
            AccessV2History history,
            AccessV2TravelAxis? fixedNavigationAxis,
            Tile2i? fixedNavigationPortalRoot)
        {
            if (m_groundGraph == null
                || fixedNavigationAxis.HasValue
                || fixedNavigationPortalRoot.HasValue
                || m_groundGraph.IsProjectedFixedGround(center)
                || m_groundGraph.TryGetGoalDistance(center, out _))
                return false;

            if (HasCanonicalGroundToVLaunchPosition(center)
                && !AreAllGroundToVSuccessorsOptimisticallyDominated(
                    center, cost, history))
                return false;

            m_diagnostics.V2HandoffGroundEntryDominanceChecks++;
            for (int index = 0; index < s_groundDirections.Length; index++)
            {
                Tile2i next = center + s_groundDirections[index];
                if (!m_groundGraph.CanTraverse(center, next))
                    continue;
                if (m_groundGraph.IsProjectedFixedGround(next))
                    return false;
                float optimisticCost = cost
                    + AccessV2GroundGraph.GetStepCost(center, next);
                if (!IsGroundSuccessorOptimisticallyDominated(
                        next, null, null, optimisticCost))
                    return false;
            }

            m_diagnostics.V2HandoffGroundEntryDominancePrunes++;
            return true;
        }

        private bool AreAllGroundToVSuccessorsOptimisticallyDominated(
            Tile2i ground,
            float optimisticCost,
            AccessV2History history)
        {
            m_diagnostics.V2HandoffGroundToVDominanceChecks++;
            if (m_groundGraph == null
                || m_groundToVHandoffEvaluator == null
                || m_terrainCenterHeightProvider == null)
            {
                m_diagnostics.V2HandoffGroundToVDominancePrunes++;
                return true;
            }

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
                foreach (Tile2i anchor in EnumerateGroundToVBandAnchors(
                    ground, travel,
                    includeOutwardFringe:
                        m_groundGraph.IsProjectedFixedGround(ground)))
                {
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
                    if (hasFixedAdapter
                        && !IsGroundToVStateOptimisticallyDominated(
                            ground, fixedAdapter.Next, optimisticCost))
                        return false;
                    if (!directBandResolvable)
                        continue;

                    float terrainHeight =
                        m_preciseTerrainHeightProvider?.Invoke(ground)
                        ?? m_terrainCenterHeightProvider(ground) / 2f;
                    if (Math.Abs(
                            terrainHeight - Math.Round(terrainHeight))
                        <= 0.0001f)
                    {
                        foreach (GroundToVProfileCandidate candidate in
                            EnumerateDirectLevelingProfiles(
                                (int)Math.Round(terrainHeight),
                                axis, travel))
                            if (!IsGroundToVProfileOptimisticallyDominated(
                                    ground, anchor, axis, travel,
                                    candidate, optimisticCost, history))
                                return false;
                    }

                    foreach (GroundToVProfileCandidate candidate in
                        EnumerateGroundToVBandProfiles(
                            anchor, terrainHeight, axis, travel,
                            m_fixedProfileProvider,
                            m_generatedVPrimeOriginValidator))
                        if (!IsGroundToVProfileOptimisticallyDominated(
                                ground, anchor, axis, travel,
                                candidate, optimisticCost, history))
                            return false;
                }
            }

            m_diagnostics.V2HandoffGroundToVDominancePrunes++;
            return true;
        }

        private bool IsGroundToVProfileOptimisticallyDominated(
            Tile2i ground,
            Tile2i anchor,
            AccessV2TravelAxis axis,
            Tile2i travel,
            GroundToVProfileCandidate candidate,
            float optimisticCost,
            AccessV2History history)
        {
            if (!AccessV2BandProfile.TryCreate(
                    axis, candidate.Lane0, candidate.Lane1,
                    includeDeferred: true,
                    out AccessV2BandProfile band, out _))
                return true;
            var state = new AccessV2BandState(anchor, band, travel);
            if (IsGroundToVStateOptimisticallyDominated(
                    ground, state, optimisticCost))
                return true;
            if (!TryResolveGroundToVTransition(
                    state, m_fixedProfileProvider,
                    m_generatedOriginValidator,
                    out AccessV2Transition transition)
                || !IsTransitionWithinUsefulHeightEnvelope(
                    m_usefulHeightEnvelope, transition, out _)
                || !history.ResetDirectionScope().TryApply(
                    transition, out _, out _))
                return true;
            return false;
        }

        private bool IsGroundToVStateOptimisticallyDominated(
            Tile2i ground,
            AccessV2BandState state,
            float optimisticCost)
        {
            if (!AccessV2Geometry.IsInsideBounds(
                    state, m_boundsMin, m_boundsMax))
                return true;
            AccessV2PotentialOwner owner =
                AccessV2PotentialOwner.FromGround(
                    m_groundGraph!, ground)
                .Advance(
                    m_groundGraph!, ground,
                    AccessV2PotentialField.GetCanonicalCenter(state));
            return IsVBandCostKnownNoWorse(
                state, optimisticCost,
                strictSelfDisruption: false,
                owner);
        }

        private bool IsGroundSuccessorOptimisticallyDominated(
            Tile2i center,
            AccessV2TravelAxis? fixedNavigationAxis,
            Tile2i? fixedNavigationPortalRoot,
            float optimisticCost)
        {
            var key = new SearchKey(
                center,
                fixedNavigationAxis,
                fixedNavigationPortalRoot);
            return m_best.TryGetValue(key, out float old)
                && old <= optimisticCost + 0.0001f;
        }

        private void CompleteActiveGroundExpansionTrace()
        {
            GroundExpansionTraceState? state =
                m_activeGroundExpansionTrace;
            if (state == null)
                return;
            m_activeGroundExpansionTrace = null;
            GroundExpansionOutcomeTraced?.Invoke(
                new AccessV2GroundExpansionOutcomeTrace(
                    state.Ordinal,
                    state.HasHandoff,
                    state.GoalAtPop,
                    state.SuffixAttempted,
                    state.SuffixSucceeded,
                    state.GroundEnqueueAttempts,
                    state.GroundEnqueueAccepted,
                    state.VEnqueueAttempts,
                    state.VEnqueueAccepted));
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

        private void RecordGroundToVEnqueue()
        {
            if (m_groundToVEnqueued)
                return;
            m_groundToVEnqueued = true;
            m_diagnostics.V2GroundToVFirstEnqueueVisited = m_visited;
        }

        private void CompleteSuccess(SearchNode goal)
        {
            long completionStart = AtdDiagnostics.Timestamp();
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
            m_diagnostics.RecordV2Completion(
                AtdDiagnostics.ElapsedSince(completionStart),
                $"steps={stepReverse.Count} states={reverse.Count} " +
                $"ground={groundReverse.Count} " +
                $"profiles={goal.History.OriginCount} " +
                $"rays={goal.History.RayConstraintCount}");
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

        private struct ExpansionTraceProvenance
        {
            public Tile2i? GroundLaunchCenter;
            public Tile2i? PotentialMergeCenter;
            public int LaunchHistoryOrigins;
            public int LaunchHistoryRayConstraints;
            public int LaunchHistoryCleanupKeys;
        }

        private sealed class GroundExpansionTraceState
        {
            public int Ordinal { get; }
            public bool HasHandoff { get; }
            public bool GoalAtPop;
            public bool SuffixAttempted;
            public bool SuffixSucceeded;
            public int GroundEnqueueAttempts;
            public int GroundEnqueueAccepted;
            public int VEnqueueAttempts;
            public int VEnqueueAccepted;

            public GroundExpansionTraceState(
                int ordinal,
                bool hasHandoff)
            {
                Ordinal = ordinal;
                HasHandoff = hasHandoff;
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
                    && left.IsBoundedTerminal == right.IsBoundedTerminal
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
            private readonly int m_groundHistorySignature;
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
                m_groundHistorySignature = 0;
                m_fixedNavigationAxis = null;
                m_fixedNavigationPortalRoot = null;
                m_isGroundToVAdapter = false;
                m_strictSelfDisruption = strictSelfDisruption;
                m_potentialOwner = potentialOwner;
            }

            public SearchKey(SearchNode node)
            {
                m_groundCenter = node.GroundCenter;
                m_groundHistorySignature = node.GroundCenter.HasValue
                    && node.Handoff != null
                    ? node.History.Signature
                    : 0;
                m_fixedNavigationAxis = node.FixedNavigationAxis;
                m_fixedNavigationPortalRoot =
                    node.FixedNavigationPortalRoot;
                m_isGroundToVAdapter = node.IsGroundToVAdapter;
                m_strictSelfDisruption = !node.GroundCenter.HasValue
                    && node.History.RequiresStrictSelfDisruptionChecks;
                m_potentialOwner = node.GroundCenter.HasValue
                    ? AccessV2PotentialOwner.Global
                    : node.PotentialOwner;
                // V labels retain V1's cheapest-arrival dominance. Competing
                // V-to-G entries remain history-qualified so a cheaper
                // incompatible handoff cannot erase a later usable one. Once
                // ordinary G traversal starts, collapse back to one cheapest
                // label per concrete center; propagating every V history over
                // the whole component causes a combinatorial ground frontier.
                m_state = node.GroundCenter.HasValue
                    ? default
                    : node.State;
            }

            public SearchKey(
                Tile2i groundCenter,
                AccessV2TravelAxis? fixedNavigationAxis,
                Tile2i? fixedNavigationPortalRoot)
            {
                m_state = default;
                m_groundCenter = groundCenter;
                m_groundHistorySignature = 0;
                m_fixedNavigationAxis = fixedNavigationAxis;
                m_fixedNavigationPortalRoot =
                    fixedNavigationPortalRoot;
                m_isGroundToVAdapter = false;
                m_strictSelfDisruption = false;
                m_potentialOwner = AccessV2PotentialOwner.Global;
            }

            public bool Equals(SearchKey other)
                => m_state.Equals(other.m_state)
                    && m_groundCenter == other.m_groundCenter
                    && m_groundHistorySignature
                        == other.m_groundHistorySignature
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
                    hash = (hash * 397) ^ m_groundHistorySignature;
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

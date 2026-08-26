using System;
using System.Collections.Generic;
using Mafi;
using AutoTerrainDesignations.Access;

namespace AutoTerrainDesignations.Access.V2
{
    /// <summary>
    /// Value-owned input for one terminal attempt. The delegate members are a
    /// compatibility adapter while the snapshot workspace is being widened;
    /// the evaluator itself remains request-local and bounded.
    /// </summary>
    internal readonly struct AccessV2TerminalRequest
    {
        public AccessV2BandState Predecessor { get; }
        public AccessV2Transition Straight { get; }
        public AccessV2History History { get; }
        public float PredecessorCost { get; }
        public Tile2i? ConnectedFixedOrigin { get; }
        public IReadOnlyList<AccessV2BandState> RecentNewestFirst { get; }
        public AccessV2GroundGraph Ground { get; }
        public AccessV2SingleLaneHandoffEvaluator SingleHandoff { get; }
        public AccessV2LaneSpanHandoffEvaluator SpanHandoff { get; }
        public AccessV2TerminalCrestEvaluator CrestEvaluator { get; }
        public AccessV2TerminalTransitionEvaluator TransitionEvaluator { get; }
        public AccessV2HandoffCenterEvaluator PostWorkCenterValidator { get; }
        public AccessV2HandoffGroundEntryEvaluator? GroundEntryValidator { get; }
        public Func<Tile2i, AccessV2History, bool>? ProjectedCenterValidator { get; }
        public Func<Tile2i, bool>? GeneratedOriginValidator { get; }
        public Tile2i BoundsMin { get; }
        public Tile2i BoundsMax { get; }
        public float CleanupCostScale { get; }
        public float CenterSpokeCost { get; }
        public float MaxCost { get; }
        public int VehicleWidth { get; }
        public AccessSearchSliceBudget? SliceBudget { get; }
        public Func<AccessV2Transition, bool>? TransitionValidator { get; }
        public AccessSearchDiagnostics? Diagnostics { get; }
        public Func<AccessV2BandState, IReadOnlyCollection<Tile2i>>?
            SafetyExemptionProvider { get; }

        public AccessV2TerminalRequest(
            AccessV2BandState predecessor,
            AccessV2Transition straight,
            AccessV2History history,
            float predecessorCost,
            Tile2i? connectedFixedOrigin,
            IReadOnlyList<AccessV2BandState> recentNewestFirst,
            AccessV2GroundGraph ground,
            AccessV2SingleLaneHandoffEvaluator singleHandoff,
            AccessV2LaneSpanHandoffEvaluator spanHandoff,
            AccessV2TerminalCrestEvaluator crestEvaluator,
            AccessV2TerminalTransitionEvaluator transitionEvaluator,
            AccessV2HandoffCenterEvaluator postWorkCenterValidator,
            AccessV2HandoffGroundEntryEvaluator? groundEntryValidator,
            Func<Tile2i, AccessV2History, bool>? projectedCenterValidator,
            Func<Tile2i, bool>? generatedOriginValidator,
            Tile2i boundsMin,
            Tile2i boundsMax,
            float cleanupCostScale,
            float centerSpokeCost,
            float maxCost,
            int vehicleWidth,
            AccessSearchSliceBudget? sliceBudget = null,
            Func<AccessV2Transition, bool>? transitionValidator = null,
            AccessSearchDiagnostics? diagnostics = null,
            Func<AccessV2BandState, IReadOnlyCollection<Tile2i>>?
                safetyExemptionProvider = null)
        {
            Predecessor = predecessor;
            Straight = straight;
            History = history;
            PredecessorCost = predecessorCost;
            ConnectedFixedOrigin = connectedFixedOrigin;
            RecentNewestFirst = recentNewestFirst;
            Ground = ground;
            SingleHandoff = singleHandoff;
            SpanHandoff = spanHandoff;
            CrestEvaluator = crestEvaluator;
            TransitionEvaluator = transitionEvaluator;
            PostWorkCenterValidator = postWorkCenterValidator;
            GroundEntryValidator = groundEntryValidator;
            ProjectedCenterValidator = projectedCenterValidator;
            GeneratedOriginValidator = generatedOriginValidator;
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            CleanupCostScale = cleanupCostScale;
            CenterSpokeCost = centerSpokeCost;
            MaxCost = maxCost;
            VehicleWidth = vehicleWidth;
            SliceBudget = sliceBudget;
            TransitionValidator = transitionValidator;
            Diagnostics = diagnostics;
            SafetyExemptionProvider = safetyExemptionProvider;
        }
    }

    internal enum AccessV2TerminalStatus
    {
        NotApplicable,
        NoHandoff,
        Success,
        Cancelled,
    }

    /// <summary>
    /// Compact identity for one catalogued terminal frontage. The descriptor
    /// is deliberately value-owned so replay does not depend on an incidental
    /// center path discovered by a proof search.
    /// </summary>
    internal readonly struct AccessV2TerminalFrontage
        : IEquatable<AccessV2TerminalFrontage>
    {
        public bool IsForward { get; }
        public bool IsInnerNotch { get; }
        public AccessV2TravelAxis Axis { get; }
        public Tile2i OutwardDirection { get; }
        public byte OwnerLane0 { get; }
        public byte OwnerLane1 { get; }
        public byte Edge0 { get; }
        public byte Edge1 { get; }

        public AccessV2TerminalFrontage(
            bool isForward,
            bool isInnerNotch,
            AccessV2TravelAxis axis,
            Tile2i outwardDirection,
            byte ownerLane0,
            byte ownerLane1,
            byte edge0,
            byte edge1)
        {
            IsForward = isForward;
            IsInnerNotch = isInnerNotch;
            Axis = axis;
            OutwardDirection = outwardDirection;
            OwnerLane0 = ownerLane0;
            OwnerLane1 = ownerLane1;
            Edge0 = edge0;
            Edge1 = edge1;
        }

        public bool Equals(AccessV2TerminalFrontage other)
            => IsForward == other.IsForward
                && IsInnerNotch == other.IsInnerNotch
                && Axis == other.Axis
                && OutwardDirection == other.OutwardDirection
                && OwnerLane0 == other.OwnerLane0
                && OwnerLane1 == other.OwnerLane1
                && Edge0 == other.Edge0
                && Edge1 == other.Edge1;

        public override bool Equals(object? obj)
            => obj is AccessV2TerminalFrontage other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = IsForward.GetHashCode();
                hash = (hash * 397) ^ IsInnerNotch.GetHashCode();
                hash = (hash * 397) ^ Axis.GetHashCode();
                hash = (hash * 397) ^ OutwardDirection.GetHashCode();
                hash = (hash * 397) ^ OwnerLane0;
                hash = (hash * 397) ^ OwnerLane1;
                hash = (hash * 397) ^ Edge0;
                return (hash * 397) ^ Edge1;
            }
        }

        public override string ToString()
            => $"{(IsForward ? "forward" : IsInnerNotch ? "notch" : "lateral")}" +
                $"/{Axis}/out={OutwardDirection}/owners={OwnerLane0},{OwnerLane1}" +
                $"/edges={Edge0},{Edge1}";
    }

    /// <summary>
    /// One persistent rank delta. The evaluator owns parent-chain storage;
    /// callers only receive flattened rank data in successful candidates.
    /// </summary>
    internal readonly struct AccessV2TerminalRankDelta
    {
        public int Rank { get; }
        public AccessSearchMode Mode { get; }
        public AccessV2OriginProfile Lane0 { get; }
        public AccessV2OriginProfile Lane1 { get; }
        public byte FrozenLanes { get; }
        public byte NewlyExposedFrontages { get; }

        public AccessV2TerminalRankDelta(
            int rank,
            AccessSearchMode mode,
            AccessV2OriginProfile lane0,
            AccessV2OriginProfile lane1,
            byte frozenLanes,
            byte newlyExposedFrontages)
        {
            Rank = rank;
            Mode = mode;
            Lane0 = lane0;
            Lane1 = lane1;
            FrozenLanes = frozenLanes;
            NewlyExposedFrontages = newlyExposedFrontages;
        }
    }

    internal sealed class AccessV2TerminalCandidate
    {
        public AccessHandoffOperation Operation { get; }
        public int RankCount { get; }
        public IReadOnlyList<AccessV2TerminalRankDelta> Ranks { get; }
        public AccessV2TerminalFrontage Frontage { get; }
        public Tile2i GroundEntry { get; }
        public int HandoffDistance { get; }
        public float HandoffTraversalCost { get; }
        public float GeneratedWorkCost { get; }
        public float GeneratedFixedCost { get; }
        public float DirectWorkCost { get; }
        public float ExteriorRayCost { get; }
        public float CleanupCost { get; }
        public IReadOnlyCollection<string> CleanupKeys { get; }

        // Transitional route-emission data. The terminal module owns these
        // immutable values; the old route format consumes them until replay
        // and materialization are migrated to rank deltas/proofs.
        internal AccessV2HandoffCandidate? CompatibilityHandoff { get; }
        internal IReadOnlyList<AccessV2Transition> Transitions { get; }
        internal IReadOnlyList<AccessV2TransitionEvaluation> Evaluations { get; }
        public IReadOnlyList<AccessRayHeightConstraint> RayConstraints { get; }
        public IReadOnlyList<AccessV2OriginProfile> FinalProfiles { get; }

        public float TotalCost
            => HandoffTraversalCost
                + GeneratedWorkCost
                + GeneratedFixedCost
                + DirectWorkCost
                + ExteriorRayCost
                + CleanupCost;

        public AccessV2TerminalCandidate(
            AccessHandoffOperation operation,
            int rankCount,
            IReadOnlyList<AccessV2TerminalRankDelta> ranks,
            AccessV2TerminalFrontage frontage,
            Tile2i groundEntry,
            int handoffDistance,
            float handoffTraversalCost,
            float generatedWorkCost,
            float generatedFixedCost,
            float directWorkCost,
            float exteriorRayCost,
            float cleanupCost,
            IReadOnlyCollection<string> cleanupKeys,
            AccessV2HandoffCandidate? compatibilityHandoff = null,
            IReadOnlyList<AccessV2Transition>? transitions = null,
            IReadOnlyList<AccessV2TransitionEvaluation>? evaluations = null)
        {
            Operation = operation;
            RankCount = rankCount;
            Ranks = ranks;
            Frontage = frontage;
            GroundEntry = groundEntry;
            HandoffDistance = handoffDistance;
            HandoffTraversalCost = handoffTraversalCost;
            GeneratedWorkCost = generatedWorkCost;
            GeneratedFixedCost = generatedFixedCost;
            DirectWorkCost = directWorkCost;
            ExteriorRayCost = exteriorRayCost;
            CleanupCost = cleanupCost;
            CleanupKeys = cleanupKeys;
            CompatibilityHandoff = compatibilityHandoff;
            Transitions = transitions ?? Array.Empty<AccessV2Transition>();
            Evaluations = evaluations
                ?? Array.Empty<AccessV2TransitionEvaluation>();
            var rays = new List<AccessRayHeightConstraint>();
            for (int index = 0; index < Evaluations.Count; index++)
                for (int rayIndex = 0;
                    rayIndex < Evaluations[index].RayConstraints.Count;
                    rayIndex++)
                    rays.Add(Evaluations[index].RayConstraints[rayIndex]);
            RayConstraints = rays;
            if (Ranks.Count == 0)
                FinalProfiles = Array.Empty<AccessV2OriginProfile>();
            else
            {
                AccessV2TerminalRankDelta final =
                    Ranks[Ranks.Count - 1];
                FinalProfiles = new[] { final.Lane0, final.Lane1 };
            }
        }
    }

    internal readonly struct AccessV2TerminalResult
    {
        public AccessV2TerminalStatus Status { get; }
        public IReadOnlyList<AccessV2TerminalCandidate> Candidates { get; }
        public string Reason { get; }
        public int EvaluatedBranches { get; }
        public int EvaluatedFrontages { get; }
        public int MaxRank { get; }

        public bool Succeeded
            => Status == AccessV2TerminalStatus.Success
                && Candidates.Count > 0;

        public AccessV2TerminalResult(
            AccessV2TerminalStatus status,
            IReadOnlyList<AccessV2TerminalCandidate> candidates,
            string reason = "",
            int evaluatedBranches = 0,
            int evaluatedFrontages = 0,
            int maxRank = 0)
        {
            Status = status;
            Candidates = candidates;
            Reason = reason ?? string.Empty;
            EvaluatedBranches = evaluatedBranches;
            EvaluatedFrontages = evaluatedFrontages;
            MaxRank = maxRank;
        }

        public static AccessV2TerminalResult NotApplicable(string reason)
            => new AccessV2TerminalResult(
                AccessV2TerminalStatus.NotApplicable,
                Array.Empty<AccessV2TerminalCandidate>(), reason);

        public static AccessV2TerminalResult NoHandoff(string reason)
            => new AccessV2TerminalResult(
                AccessV2TerminalStatus.NoHandoff,
                Array.Empty<AccessV2TerminalCandidate>(), reason);

        public static AccessV2TerminalResult Cancelled(string reason)
            => new AccessV2TerminalResult(
                AccessV2TerminalStatus.Cancelled,
                Array.Empty<AccessV2TerminalCandidate>(), reason);
    }
}

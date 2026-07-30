using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal readonly struct AccessV2TerminalExtensionRequest
    {
        public AccessHandoffOperation Operation { get; }
        public int ExtensionLane { get; }
        public bool IsValid => (Operation == AccessHandoffOperation.Mining
                || Operation == AccessHandoffOperation.Dumping)
            && (ExtensionLane == 0 || ExtensionLane == 1);

        public AccessV2TerminalExtensionRequest(
            AccessHandoffOperation operation,
            int extensionLane)
        {
            Operation = operation;
            ExtensionLane = extensionLane;
        }
    }

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
        public bool IsStaggeredExtension { get; }
        public int NonCrestLane { get; }
        public bool Lane0RequiresCrest => NonCrestLane != 0;
        public bool Lane1RequiresCrest => NonCrestLane != 1;
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
            float centerSpokeCost = 2f,
            bool isStaggeredExtension = false,
            int nonCrestLane = -1)
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
            IsStaggeredExtension = isStaggeredExtension;
            NonCrestLane = nonCrestLane;
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

    internal delegate bool AccessV2HandoffCenterEvaluator(
        Tile2i origin,
        AccessHandoffOperation operation,
        Tile2i center,
        AccessV2History history,
        IReadOnlyCollection<Tile2i> handoffOrigins);

    internal delegate bool AccessV2HandoffGroundEntryEvaluator(
        Tile2i center,
        IReadOnlyCollection<Tile2i> handoffClearingOrigins,
        AccessV2History history);

    internal delegate bool AccessV2PostWorkHeightEvaluator(
        Tile2i tile,
        out float height);

    internal delegate bool AccessV2DumpingPropBlockerEvaluator(Tile2i tile);

    internal static class AccessV2Handoffs
    {
        // One initially exposed origin plus up to
        // 1 + ceil(mega-clearance / 4) additional origins. The current V2
        // target is the five-tile Mega footprint, hence 1 + 3 = 4 rows.
        public const int MaxSpanLength = 4;

        /// <summary>
        /// A single lane reaching a mining/dumping crest while its companion
        /// remains buried/exposed is not a failed frontage. It requests a
        /// bounded forward terminal extension using the operation already
        /// proven by the first lane.
        /// </summary>
        internal static AccessV2TerminalExtensionRequest
            GetSameTypeExtensionRequest(
            IReadOnlyList<AccessV2BandState> recentNewestFirst,
            AccessV2SingleLaneHandoffEvaluator singleEvaluator)
        {
            if (recentNewestFirst.Count == 0)
                return default;
            AccessV2BandState current = recentNewestFirst[0];
            AccessHandoffOperation lane0 = GetLaneOperation(0);
            AccessHandoffOperation lane1 = GetLaneOperation(1);
            bool lane0Terminal = IsTerrainOperation(lane0);
            bool lane1Terminal = IsTerrainOperation(lane1);
            if (lane0Terminal == lane1Terminal)
                return default;
            AccessHandoffOperation operation = lane0Terminal ? lane0 : lane1;
            return operation == AccessHandoffOperation.Mining
                || operation == AccessHandoffOperation.Dumping
                    ? new AccessV2TerminalExtensionRequest(
                        operation, lane0Terminal ? 1 : 0)
                    : default;

            AccessHandoffOperation GetLaneOperation(int lane)
            {
                Tile2i origin = current.GetLaneOrigin(lane);
                AccessHeightProfile profile = current.GetLane(lane).Profile;
                Tile2i predecessor = AccessV2Geometry.Add(
                    origin,
                    AccessV2Geometry.Scale(current.EntryDirection, -1));
                AccessHeightProfile predecessorProfile =
                    recentNewestFirst.Count > 1
                        ? recentNewestFirst[1].GetLane(lane).Profile
                        : profile;
                AccessHandoffOperation[] operations = singleEvaluator(
                        origin, profile, predecessor, predecessorProfile)
                    .Select(item => item.Operation)
                    .Where(IsTerrainOperation)
                    .Distinct()
                    .ToArray();
                return operations.Length == 1
                    ? operations[0]
                    : AccessHandoffOperation.None;
            }

            bool IsTerrainOperation(AccessHandoffOperation candidate)
                => candidate == AccessHandoffOperation.Mining
                    || candidate == AccessHandoffOperation.Dumping;
        }

        internal static bool TryCreateDirectLevelingBridge(
            AccessV2BandState state,
            Tile2i groundEntry,
            float centerSpokeCost,
            out AccessV2HandoffCandidate candidate)
        {
            Tile2i exitDirection = new Tile2i(
                -state.EntryDirection.X, -state.EntryDirection.Y);
            Tile2i lane0Origin = state.GetLaneOrigin(0);
            Tile2i lane1Origin = state.GetLaneOrigin(1);
            bool travelsX = exitDirection.X != 0;
            int transverse = travelsX ? groundEntry.Y : groundEntry.X;
            int lane0Min = travelsX ? lane0Origin.Y : lane0Origin.X;
            bool bridgeIsLane0 = transverse >= lane0Min
                && transverse <= lane0Min + 4;
            Tile2i lane0Contact = bridgeIsLane0
                ? groundEntry
                : GetLevelingCompanionContact(
                    lane0Origin, exitDirection, groundEntry);
            Tile2i lane1Contact = bridgeIsLane0
                ? GetLevelingCompanionContact(
                    lane1Origin, exitDirection, groundEntry)
                : groundEntry;
            var lane0 = new AccessGroundHandoff(
                lane0Contact, AccessHandoffOperation.Leveling,
                new[] { groundEntry }, 1);
            var lane1 = new AccessGroundHandoff(
                lane1Contact, AccessHandoffOperation.Leveling,
                new[] { groundEntry }, 1);
            if (!TryBuildLevelingBridgeEscape(
                    lane0, lane1, exitDirection, 1, groundEntry,
                    out IReadOnlyList<Tile2i> escape,
                    out IReadOnlyList<Tile2i> entries))
            {
                candidate = null!;
                return false;
            }
            candidate = new AccessV2HandoffCandidate(
                exitDirection, 1, lane0, lane1,
                new[] { lane0Origin }, new[] { lane1Origin },
                escape, entries, Array.Empty<string>(), 0f,
                isQuickPath: true, centerSpokeCost: centerSpokeCost);
            return true;
        }

        /// <summary>
        /// Proves a reverse G-to-V handoff without searching inside the seam.
        /// The complete vehicle-width face must finish within one quarter level
        /// of the candidate V surface, then every cardinal step back to the
        /// reached G center must remain within the vehicle's half-level limit.
        /// </summary>
        internal static bool TryCreateDeterministicGroundToVBridge(
            AccessV2BandState state,
            Tile2i groundEntry,
            AccessHandoffOperation operation,
            int vehicleWidth,
            float centerSpokeCost,
            AccessV2PostWorkHeightEvaluator postWorkHeight,
            AccessV2DumpingPropBlockerEvaluator dumpingPropBlocker,
            out AccessV2HandoffCandidate candidate,
            AccessSearchDiagnostics? diagnostics = null)
        {
            candidate = null!;
            if (vehicleWidth <= 0
                || (operation != AccessHandoffOperation.Mining
                    && operation != AccessHandoffOperation.Dumping
                    && operation != AccessHandoffOperation.Leveling))
                return false;

            bool travelsX = state.EntryDirection.X != 0;
            int sign = Math.Sign(travelsX
                ? state.EntryDirection.X
                : state.EntryDirection.Y);
            if (sign == 0)
                return false;
            Tile2i lane0Origin = state.GetLaneOrigin(0);
            int faceLongitudinal = travelsX
                ? lane0Origin.X + (sign > 0 ? 4 : 0)
                : lane0Origin.Y + (sign > 0 ? 4 : 0);
            int groundLongitudinal = travelsX ? groundEntry.X : groundEntry.Y;
            int towardGround = Math.Sign(groundLongitudinal - faceLongitudinal);
            if (towardGround != -sign && groundLongitudinal != faceLongitudinal)
                return false;

            int halfWidth = vehicleWidth / 2;
            int groundTransverse = travelsX ? groundEntry.Y : groundEntry.X;
            var escape = new List<Tile2i>();
            for (int transverseOffset = -halfWidth;
                transverseOffset <= halfWidth;
                transverseOffset++)
            {
                int transverse = groundTransverse + transverseOffset;
                Tile2i face = travelsX
                    ? new Tile2i(faceLongitudinal, transverse)
                    : new Tile2i(transverse, faceLongitudinal);
                if (diagnostics != null)
                    diagnostics.V2GroundToVFaceChecks++;
                if (!TryGetBandTargetHeight(state, face, out float target)
                    || !postWorkHeight(face, out float previousHeight)
                    || Math.Abs(previousHeight - target) > 0.2501f)
                {
                    if (diagnostics != null)
                        diagnostics.V2GroundToVFaceRejects++;
                    return false;
                }
                if (operation == AccessHandoffOperation.Dumping
                    && dumpingPropBlocker(face))
                {
                    if (diagnostics != null)
                        diagnostics.V2GroundToVPropRejects++;
                    return false;
                }
                if (transverseOffset == 0)
                    escape.Add(face);

                int longitudinal = faceLongitudinal;
                while (longitudinal != groundLongitudinal)
                {
                    longitudinal += towardGround;
                    Tile2i next = travelsX
                        ? new Tile2i(longitudinal, transverse)
                        : new Tile2i(transverse, longitudinal);
                    if (diagnostics != null)
                        diagnostics.V2GroundToVBridgeSteps++;
                    if (!postWorkHeight(next, out float nextHeight)
                        || Math.Abs(nextHeight - previousHeight) > 0.5001f)
                    {
                        if (diagnostics != null)
                            diagnostics.V2GroundToVBridgeRejects++;
                        return false;
                    }
                    if (operation == AccessHandoffOperation.Dumping
                        && dumpingPropBlocker(next))
                    {
                        if (diagnostics != null)
                            diagnostics.V2GroundToVPropRejects++;
                        return false;
                    }
                    previousHeight = nextHeight;
                    if (transverseOffset == 0)
                        escape.Add(next);
                }
            }

            var exitDirection = new Tile2i(
                -state.EntryDirection.X, -state.EntryDirection.Y);
            int groundCoordinate = travelsX ? groundEntry.Y : groundEntry.X;
            int lane0Min = travelsX ? lane0Origin.Y : lane0Origin.X;
            bool groundIsLane0 = groundCoordinate >= lane0Min
                && groundCoordinate <= lane0Min + 4;
            Tile2i lane0Contact = groundIsLane0
                ? groundEntry
                : GetLevelingCompanionContact(
                    lane0Origin, exitDirection, groundEntry);
            Tile2i lane1Origin = state.GetLaneOrigin(1);
            Tile2i lane1Contact = groundIsLane0
                ? GetLevelingCompanionContact(
                    lane1Origin, exitDirection, groundEntry)
                : groundEntry;
            var lane0 = new AccessGroundHandoff(
                lane0Contact, operation, new[] { groundEntry }, 1);
            var lane1 = new AccessGroundHandoff(
                lane1Contact, operation, new[] { groundEntry }, 1);
            candidate = new AccessV2HandoffCandidate(
                exitDirection, 1, lane0, lane1,
                new[] { lane0Origin }, new[] { lane1Origin },
                escape, new[] { groundEntry }, Array.Empty<string>(), 0f,
                centerSpokeCost: centerSpokeCost);
            return true;
        }

        internal static bool TryValidatePlacedGroundToVBridge(
            AccessV2BandState state,
            AccessV2HandoffCandidate recorded,
            int vehicleWidth,
            AccessV2PostWorkHeightEvaluator postWorkHeight,
            AccessV2DumpingPropBlockerEvaluator dumpingPropBlocker,
            out string reason)
        {
            if (recorded.GroundEntryCenters.Count != 1)
            {
                reason = "GroundToVGroundEntryCount";
                return false;
            }
            if (recorded.Lane0Operation != recorded.Lane1Operation)
            {
                reason = "GroundToVMixedOperations";
                return false;
            }
            if (!TryCreateDeterministicGroundToVBridge(
                    state,
                    recorded.GroundEntryCenters[0],
                    recorded.Lane0Operation,
                    vehicleWidth,
                    recorded.CenterSpokeCost,
                    postWorkHeight,
                    dumpingPropBlocker,
                    out AccessV2HandoffCandidate replayed))
            {
                reason = "GroundToVDeterministicBridgeInvalid";
                return false;
            }
            if (replayed.ExitDirection != recorded.ExitDirection
                || replayed.SpanLength != recorded.SpanLength
                || replayed.Lane0Operation != recorded.Lane0Operation
                || replayed.Lane1Operation != recorded.Lane1Operation
                || replayed.Lane0Contact != recorded.Lane0Contact
                || replayed.Lane1Contact != recorded.Lane1Contact
                || !replayed.Lane0TerminalOrigins.SequenceEqual(
                    recorded.Lane0TerminalOrigins)
                || !replayed.Lane1TerminalOrigins.SequenceEqual(
                    recorded.Lane1TerminalOrigins)
                || !replayed.EscapeCenters.SequenceEqual(
                    recorded.EscapeCenters)
                || !replayed.GroundEntryCenters.SequenceEqual(
                    recorded.GroundEntryCenters))
            {
                reason = "GroundToVDeterministicBridgeMismatch";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        internal static bool TryResolvePlacedGroundToVPostWorkHeight(
            AccessV2BandState state,
            AccessHandoffOperation operation,
            Tile2i tile,
            float naturalHeight,
            Func<Tile2i, float?> projectedHeight,
            out float height)
        {
            if (TryGetBandTargetHeight(state, tile, out float target))
            {
                height = operation == AccessHandoffOperation.Mining
                    ? Math.Min(naturalHeight, target)
                    : operation == AccessHandoffOperation.Dumping
                        ? Math.Max(naturalHeight, target)
                        : target;
                return true;
            }

            float? projected = projectedHeight(tile);
            height = projected ?? naturalHeight;
            return true;
        }

        private static bool TryGetBandTargetHeight(
            AccessV2BandState state,
            Tile2i tile,
            out float height)
        {
            for (int lane = 0; lane < 2; lane++)
            {
                Tile2i origin = state.GetLaneOrigin(lane);
                int localX = tile.X - origin.X;
                int localY = tile.Y - origin.Y;
                if (localX < 0 || localX > 4
                    || localY < 0 || localY > 4)
                    continue;
                height = state.GetLane(lane).Profile
                    .GetHeight2NumeratorAt(localX, localY) / 32f;
                return true;
            }
            height = 0f;
            return false;
        }

        public static IReadOnlyList<AccessV2HandoffCandidate> Evaluate(
            IReadOnlyList<AccessV2BandState> recentNewestFirst,
            AccessV2History history,
            AccessV2GroundGraph ground,
            AccessV2SingleLaneHandoffEvaluator singleEvaluator,
            AccessV2LaneSpanHandoffEvaluator spanEvaluator,
            float cleanupCostScale = 1f,
            Func<Tile2i, AccessV2History, bool>? projectedCenterValidator = null,
            Func<Tile2i, AccessV2History, bool>? projectedCenterOverlapsWork = null,
            AccessV2HandoffCenterEvaluator? postWorkCenterValidator = null,
            AccessV2HandoffGroundEntryEvaluator? groundEntryValidator = null,
            int vehicleWidth = 0,
            float centerSpokeCost = 2f,
            Tile2i? requiredGroundEntry = null,
            AccessSearchDiagnostics? diagnostics = null)
        {
            var result = new List<AccessV2HandoffCandidate>();
            if (recentNewestFirst.Count == 0) return result;
            AccessV2BandState current = recentNewestFirst[0];

            long laneEvaluationStart = AtdDiagnostics.Timestamp();
            IReadOnlyList<AccessGroundHandoff> firstLane0 =
                EvaluateForwardLane(
                    recentNewestFirst, 1, 0,
                    singleEvaluator, spanEvaluator);
            IReadOnlyList<AccessGroundHandoff> firstLane1 =
                EvaluateForwardLane(
                    recentNewestFirst, 1, 1,
                    singleEvaluator, spanEvaluator);
            bool diagnoseFirstGenerated = diagnostics != null
                && recentNewestFirst.Count == 2
                && AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace);
            if (diagnoseFirstGenerated)
                diagnostics!.RecordFirstGeneratedHandoff(
                    $"v2 anchor={current.Anchor} entry={current.EntryDirection} " +
                    $"lane0Origin={current.GetLaneOrigin(0)} " +
                    $"lane0=[{FormatLaneHandoffs(firstLane0)}] " +
                    $"lane1Origin={current.GetLaneOrigin(1)} " +
                    $"lane1=[{FormatLaneHandoffs(firstLane1)}]");
            if (diagnostics != null)
                diagnostics.V2HandoffLaneEvaluationTicks +=
                    AtdDiagnostics.ElapsedSince(laneEvaluationStart);
            // Production V2 supplies a post-work center classifier and must
            // prove the complete rank-two-to-G corridor below.  Retain the
            // legacy quick path only for callers without that classifier.
            AccessV2HandoffCandidate? quick = postWorkCenterValidator == null
                ? TryBuildQuickForwardHandoff(
                    current, history, ground,
                    firstLane0, firstLane1, vehicleWidth,
                    cleanupCostScale, projectedCenterValidator,
                    centerSpokeCost)
                : null;
            if (quick != null
                && (!requiredGroundEntry.HasValue
                    || quick.GroundEntryCenters.Contains(
                        requiredGroundEntry.Value)))
            {
                if (diagnoseFirstGenerated)
                    diagnostics!.RecordFirstGeneratedHandoff(
                        $"v2 anchor={current.Anchor} accepted=quick {quick}");
                return new[] { quick };
            }

            int available = CountRecentStraightRows(recentNewestFirst);
            for (int span = 1;
                span <= Math.Min(MaxSpanLength, available);
                span++)
            {
                laneEvaluationStart = AtdDiagnostics.Timestamp();
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
                if (diagnoseFirstGenerated && span > 1)
                    diagnostics!.RecordFirstGeneratedHandoff(
                        $"v2 anchor={current.Anchor} span={span} " +
                        $"lane0=[{FormatLaneHandoffs(lane0)}] " +
                        $"lane1=[{FormatLaneHandoffs(lane1)}]");
                if (diagnostics != null)
                    diagnostics.V2HandoffLaneEvaluationTicks +=
                        AtdDiagnostics.ElapsedSince(laneEvaluationStart);
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
                    postWorkCenterValidator,
                    groundEntryValidator,
                    vehicleWidth,
                    centerSpokeCost, requiredGroundEntry, result,
                    diagnostics);
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
                        postWorkCenterValidator,
                        groundEntryValidator,
                        vehicleWidth,
                        centerSpokeCost, requiredGroundEntry, result,
                        diagnostics);
                }
            }

            IEnumerable<AccessV2HandoffCandidate> selected = result;
            if (requiredGroundEntry.HasValue)
                selected = selected.Where(candidate =>
                    candidate.GroundEntryCenters.Contains(
                        requiredGroundEntry.Value));
            AccessV2HandoffCandidate[] selectedArray = selected
                .OrderBy(item => item.TotalCost)
                .ThenBy(item => item.SpanLength)
                .ThenBy(item => item.ExitDirection.X)
                .ThenBy(item => item.ExitDirection.Y)
                .ThenBy(item => item.Lane0Contact.X)
                .ThenBy(item => item.Lane0Contact.Y)
                .ToArray();
            if (diagnoseFirstGenerated)
                diagnostics!.RecordFirstGeneratedHandoff(
                    $"v2 anchor={current.Anchor} paired={selectedArray.Length} " +
                    $"options=[{string.Join(";", selectedArray.Select(item => item.ToString()))}]");
            return selectedArray;

            string FormatLaneHandoffs(
                IReadOnlyList<AccessGroundHandoff> handoffs)
                => string.Join(";", handoffs.Select(item =>
                    $"{item.Operation}@{item.Tile}/span={item.SpanLength}"));
        }

        internal static IReadOnlyList<AccessV2HandoffCandidate>
            EvaluateStaggeredExtension(
                IReadOnlyList<AccessV2BandState> terminalOldestFirst,
                int extensionLane,
                AccessHandoffOperation operation,
                AccessV2History history,
                AccessV2GroundGraph ground,
                AccessV2SingleLaneHandoffEvaluator singleEvaluator,
                AccessV2LaneSpanHandoffEvaluator spanEvaluator,
                float cleanupCostScale,
                Func<Tile2i, AccessV2History, bool>?
                    projectedCenterValidator,
                AccessV2HandoffCenterEvaluator postWorkCenterValidator,
                AccessV2HandoffGroundEntryEvaluator? groundEntryValidator,
                float centerSpokeCost,
                AccessSearchDiagnostics? diagnostics = null)
        {
            var result = new List<AccessV2HandoffCandidate>();
            if (terminalOldestFirst.Count < 1
                || (extensionLane != 0 && extensionLane != 1)
                || (operation != AccessHandoffOperation.Mining
                    && operation != AccessHandoffOperation.Dumping))
                return result;
            int nearLane = 1 - extensionLane;
            AccessV2BandState first = terminalOldestFirst[0];
            Tile2i nearOrigin = first.GetLaneOrigin(nearLane);
            Tile2i predecessor = AccessV2Geometry.Add(
                nearOrigin,
                AccessV2Geometry.Scale(first.EntryDirection, -1));
            IReadOnlyList<AccessGroundHandoff> nearHandoffs = singleEvaluator(
                    nearOrigin, first.GetLane(nearLane).Profile,
                    predecessor, first.GetLane(nearLane).Profile)
                .Where(item => item.Operation == operation)
                .ToArray();
            var farCells = terminalOldestFirst.Select(state =>
                    new AccessHandoffSpanCell(
                        state.GetLaneOrigin(extensionLane),
                        state.GetLane(extensionLane).Profile,
                        state.EntryDirection))
                .ToArray();
            IReadOnlyList<AccessGroundHandoff> farHandoffs =
                terminalOldestFirst.Count == 1
                    ? nearHandoffs.Select(item =>
                        new AccessGroundHandoff(
                            GetLevelingCompanionContact(
                                first.GetLaneOrigin(extensionLane),
                                first.EntryDirection, item.Tile),
                            operation,
                            item.EscapeTiles,
                            item.SpanLength))
                        .ToArray()
                    : spanEvaluator(farCells)
                        .Where(item => item.Operation == operation)
                        .ToArray();
            if (nearHandoffs.Count == 0 || farHandoffs.Count == 0)
            {
                Trace($"v2-partial-exit rows={terminalOldestFirst.Count} " +
                    $"extensionLane={extensionLane} op={operation} " +
                    $"near={nearHandoffs.Count} far={farHandoffs.Count} " +
                    "reject=missing-operation-contact");
                return result;
            }

            IReadOnlyList<Tile2i> nearOrigins = new[] { nearOrigin };
            IReadOnlyList<Tile2i> farOrigins = farCells
                .Select(cell => cell.Origin).ToArray();
            IReadOnlyList<Tile2i> lane0Origins = extensionLane == 0
                ? farOrigins : nearOrigins;
            IReadOnlyList<Tile2i> lane1Origins = extensionLane == 1
                ? farOrigins : nearOrigins;
            foreach (AccessGroundHandoff near in nearHandoffs)
                foreach (AccessGroundHandoff far in farHandoffs)
                {
                    AccessGroundHandoff lane0 = extensionLane == 0
                        ? far : near;
                    AccessGroundHandoff lane1 = extensionLane == 1
                        ? far : near;
                    if (!TryBuildStaggeredPostWorkEscape(
                            first, terminalOldestFirst.Count,
                            lane0Origins, lane1Origins,
                            operation, history, ground,
                            projectedCenterValidator,
                            postWorkCenterValidator,
                            groundEntryValidator,
                            out IReadOnlyList<Tile2i> centers,
                            out IReadOnlyList<Tile2i> entries,
                            out string escapeReason))
                    {
                        Trace($"v2-partial-exit rows={terminalOldestFirst.Count} " +
                            $"extensionLane={extensionLane} op={operation} " +
                            $"contacts={near.Tile}/{far.Tile} reject={escapeReason}");
                        continue;
                    }
                    if (!ground.TryValidateLocalEscape(
                            entries, history, cleanupCostScale,
                            out IReadOnlyCollection<string> cleanupKeys,
                            out float cleanupCost))
                        continue;
                    result.Add(new AccessV2HandoffCandidate(
                        first.EntryDirection,
                        terminalOldestFirst.Count,
                        lane0, lane1,
                        lane0Origins, lane1Origins,
                        centers, entries,
                        cleanupKeys, cleanupCost,
                        centerSpokeCost: centerSpokeCost,
                        isStaggeredExtension: true,
                        nonCrestLane: extensionLane));
                  }
            Trace($"v2-partial-exit rows={terminalOldestFirst.Count} " +
                $"extensionLane={extensionLane} op={operation} " +
                $"accepted={result.Count}");
            return result.OrderBy(item => item.TotalCost).ToArray();

            void Trace(string message)
            {
                if (diagnostics != null
                    && AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                    diagnostics.RecordFirstGeneratedHandoff(message);
            }
        }

        private static bool TryBuildStaggeredPostWorkEscape(
            AccessV2BandState first,
            int terminalRows,
            IReadOnlyList<Tile2i> lane0Origins,
            IReadOnlyList<Tile2i> lane1Origins,
            AccessHandoffOperation operation,
            AccessV2History history,
            AccessV2GroundGraph ground,
            Func<Tile2i, AccessV2History, bool>?
                projectedCenterValidator,
            AccessV2HandoffCenterEvaluator postWorkCenterValidator,
            AccessV2HandoffGroundEntryEvaluator? groundEntryValidator,
            out IReadOnlyList<Tile2i> escape,
            out IReadOnlyList<Tile2i> groundEntries,
            out string reason)
        {
            escape = Array.Empty<Tile2i>();
            groundEntries = Array.Empty<Tile2i>();
            reason = string.Empty;
            Tile2i direction = first.EntryDirection;
            int forwardX = Math.Sign(direction.X);
            int forwardY = Math.Sign(direction.Y);
            bool travelsX = forwardX != 0;
            if (travelsX == (forwardY != 0))
            {
                reason = "invalid-direction";
                return false;
            }
            var clearingOrigins = new HashSet<Tile2i>(lane0Origins);
            clearingOrigins.UnionWith(lane1Origins);
            int transverseMin = Math.Min(
                travelsX ? first.GetLaneOrigin(0).Y
                    : first.GetLaneOrigin(0).X,
                travelsX ? first.GetLaneOrigin(1).Y
                    : first.GetLaneOrigin(1).X);
            int firstLongitudinal = travelsX
                ? first.Anchor.X : first.Anchor.Y;
            int maxRank = checked(terminalRows * 4 + 4);
            var pathable = new HashSet<Tile2i>();
            var rankByTile = new Dictionary<Tile2i, int>();
            for (int rank = 1; rank <= maxRank; rank++)
            {
                int longitudinal = firstLongitudinal
                    + (travelsX
                        ? forwardX > 0 ? rank : 3 - rank
                        : forwardY > 0 ? rank : 3 - rank);
                for (int file = 2; file <= 5; file++)
                {
                    int transverse = transverseMin + file;
                    Tile2i center = travelsX
                        ? new Tile2i(longitudinal, transverse)
                        : new Tile2i(transverse, longitudinal);
                    Tile2i owner = clearingOrigins.FirstOrDefault(origin =>
                        IsInsideOrigin(center, origin));
                    if (!clearingOrigins.Contains(owner))
                        owner = lane0Origins.Count > lane1Origins.Count
                            ? lane0Origins[lane0Origins.Count - 1]
                            : lane1Origins[lane1Origins.Count - 1];
                    if (!postWorkCenterValidator(
                            owner, operation, center, history,
                            clearingOrigins))
                        continue;
                    pathable.Add(center);
                    rankByTile[center] = rank;
                }
            }

            var queue = new Queue<Tile2i>();
            var parent = new Dictionary<Tile2i, Tile2i>();
            var visited = new HashSet<Tile2i>();
            foreach (KeyValuePair<Tile2i, int> pair in rankByTile)
                if (pair.Value == 1 && visited.Add(pair.Key))
                    queue.Enqueue(pair.Key);
            RelTile2i[] directions =
            {
                new RelTile2i(1, 0), new RelTile2i(-1, 0),
                new RelTile2i(0, 1), new RelTile2i(0, -1),
            };
            bool reachedGround = false;
            bool passedEntry = false;
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                bool groundValid = ground.IsTraversable(current);
                bool entryValid = groundEntryValidator == null
                    || groundEntryValidator(
                        current, clearingOrigins, history);
                reachedGround |= groundValid;
                passedEntry |= entryValid;
                if (groundValid && entryValid)
                {
                    var path = new List<Tile2i> { current };
                    while (parent.TryGetValue(current, out Tile2i previous))
                    {
                        current = previous;
                        path.Add(current);
                    }
                    path.Reverse();
                    escape = path;
                    groundEntries = new[] { path[path.Count - 1] };
                    return true;
                }
                for (int index = 0; index < directions.Length; index++)
                {
                    Tile2i next = current + directions[index];
                    if (!pathable.Contains(next) || !visited.Add(next))
                        continue;
                    parent[next] = current;
                    queue.Enqueue(next);
                }
            }
            reason = $"no-escape ranks=[{string.Join(",",
                Enumerable.Range(1, maxRank).Select(rank =>
                    rankByTile.Count(pair => pair.Value == rank)))}] " +
                $"ground={reachedGround} entry={passedEntry}";
            return false;

            bool IsInsideOrigin(Tile2i tile, Tile2i origin)
                => tile.X >= origin.X && tile.X < origin.X + 4
                    && tile.Y >= origin.Y && tile.Y < origin.Y + 4;
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
            if (!TrySelectForwardPair(
                    lane0, lane1, current,
                    out AccessGroundHandoff left,
                    out AccessGroundHandoff right))
                return null;

            int radius = Math.Max(0, vehicleWidth / 2);
            int minOffset = radius;
            int maxOffset = 8 - radius;
            if (minOffset > maxOffset)
                return null;
            IReadOnlyCollection<Tile2i> rayTiles =
                history.CollectHandoffRayTiles();
            Tile2i? selected = null;
            for (int offset = minOffset; offset <= maxOffset; offset++)
            {
                Tile2i center = GetForwardCenter(current, offset);
                if (!ground.IsTraversable(center, history)
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

        private static bool TrySelectForwardPair(
            IReadOnlyList<AccessGroundHandoff> lane0,
            IReadOnlyList<AccessGroundHandoff> lane1,
            AccessV2BandState state,
            out AccessGroundHandoff selected0,
            out AccessGroundHandoff selected1)
        {
            for (int leftIndex = 0; leftIndex < lane0.Count; leftIndex++)
                for (int rightIndex = 0; rightIndex < lane1.Count; rightIndex++)
                {
                    AccessGroundHandoff left = lane0[leftIndex];
                    AccessGroundHandoff right = lane1[rightIndex];
                    if (left.Operation == right.Operation
                        && (left.Operation == AccessHandoffOperation.Mining
                            || left.Operation
                                == AccessHandoffOperation.Dumping)
                        && IsAheadOfBand(left.Tile, state)
                        && IsAheadOfBand(right.Tile, state)
                        && AreConsecutiveContacts(left.Tile, right.Tile))
                    {
                        selected0 = left;
                        selected1 = right;
                        return true;
                    }
                }
            selected0 = default;
            selected1 = default;
            return false;
        }

        private static bool AreConsecutiveContacts(Tile2i first, Tile2i second)
            => Math.Abs(first.X - second.X)
                + Math.Abs(first.Y - second.Y) == 1;

        private static bool IsAheadOfBand(
            Tile2i tile,
            AccessV2BandState state)
            => state.EntryDirection.X > 0
                ? tile.X >= state.Anchor.X + 4
                : state.EntryDirection.X < 0
                    ? tile.X <= state.Anchor.X
                    : state.EntryDirection.Y > 0
                        ? tile.Y >= state.Anchor.Y + 4
                        : tile.Y <= state.Anchor.Y;

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
            AccessV2HandoffCenterEvaluator? postWorkCenterValidator,
            AccessV2HandoffGroundEntryEvaluator? groundEntryValidator,
            int vehicleWidth,
            float centerSpokeCost,
            Tile2i? requiredGroundEntry,
            ICollection<AccessV2HandoffCandidate> result,
            AccessSearchDiagnostics? diagnostics)
        {
            AccessGroundHandoff? levelBridge = SelectLevelBridge();
            if (levelBridge.HasValue)
            {
                AccessGroundHandoff bridge = levelBridge.Value;
                bool bridgeIsLane0 = lane0.Any(candidate =>
                    candidate.Operation == AccessHandoffOperation.Leveling
                    && candidate.Tile == bridge.Tile);
                Tile2i lane0Contact = bridgeIsLane0
                    ? bridge.Tile
                    : GetLevelingCompanionContact(
                        lane0TerminalOrigins[lane0TerminalOrigins.Count - 1],
                        exitDirection, bridge.Tile);
                Tile2i lane1Contact = bridgeIsLane0
                    ? GetLevelingCompanionContact(
                        lane1TerminalOrigins[lane1TerminalOrigins.Count - 1],
                        exitDirection, bridge.Tile)
                    : bridge.Tile;
                var leveledLane0 = new AccessGroundHandoff(
                    lane0Contact, AccessHandoffOperation.Leveling,
                    new[] { bridge.Tile }, spanLength);
                var leveledLane1 = new AccessGroundHandoff(
                    lane1Contact, AccessHandoffOperation.Leveling,
                    new[] { bridge.Tile }, spanLength);
                if (TryBuildLevelingGroundEscape(
                        leveledLane0, leveledLane1,
                        lane0TerminalOrigins, lane1TerminalOrigins,
                        exitDirection,
                        lane0TerminalOrigins.Count, bridge.Tile,
                        ground, history, groundEntryValidator,
                        vehicleWidth, cleanupCostScale,
                        requiredGroundEntry,
                        out IReadOnlyList<Tile2i> leveledCenters,
                        out IReadOnlyList<Tile2i> leveledEntries,
                        out IReadOnlyCollection<string> cleanupKeys,
                        out float cleanupCost,
                        out int outwardSteps))
                {
                    if (diagnostics != null)
                        diagnostics.V2LevelingBridgeAccepts++;
                    result.Add(new AccessV2HandoffCandidate(
                        exitDirection, spanLength,
                        leveledLane0, leveledLane1,
                        lane0TerminalOrigins,
                        lane1TerminalOrigins,
                        leveledCenters,
                        leveledEntries,
                        cleanupKeys, cleanupCost,
                        isQuickPath: true,
                        centerSpokeCost:
                            centerSpokeCost + outwardSteps));
                }
                return;
            }

            for (int leftIndex = 0; leftIndex < lane0.Count; leftIndex++)
            {
                    AccessGroundHandoff left = lane0[leftIndex];
                    for (int rightIndex = 0; rightIndex < lane1.Count; rightIndex++)
                    {
                        AccessGroundHandoff right = lane1[rightIndex];
                        if (left.Operation != right.Operation)
                        {
                            if (diagnostics != null)
                                diagnostics.V2MixedLanePairRejects++;
                            continue;
                        }
                        if (left.Operation != AccessHandoffOperation.Mining
                            && left.Operation
                                != AccessHandoffOperation.Dumping)
                            continue;
                        if (diagnostics != null)
                            diagnostics.V2HandoffPairChecks++;
                        bool levelingBridge =
                            left.Operation == AccessHandoffOperation.Leveling
                            && right.Operation == AccessHandoffOperation.Leveling;
                        if (!AreConsecutiveContacts(left.Tile, right.Tile)
                            && !(levelingBridge && left.Tile == right.Tile))
                            continue;
                        IReadOnlyList<Tile2i> entryCenters;
                        IReadOnlyList<Tile2i> centers;
                        if (levelingBridge)
                        {
                            if (!TryBuildLevelingBridgeEscape(
                                    left, right, exitDirection,
                                    lane0TerminalOrigins.Count,
                                    requiredGroundEntry,
                                    out centers, out entryCenters))
                                continue;
                        }
                        else if (postWorkCenterValidator != null)
                        {
                            if (diagnostics != null)
                                diagnostics.V2CorridorAttempts++;
                            long corridorStart = AtdDiagnostics.Timestamp();
                            bool corridorValid = TryBuildPostWorkCorridorEscape(
                                    left, right,
                                    lane0TerminalOrigins,
                                    lane1TerminalOrigins,
                                    exitDirection, history, ground,
                                    projectedCenterValidator,
                                    postWorkCenterValidator,
                                    groundEntryValidator,
                                    requiredGroundEntry,
                                    out centers, out entryCenters,
                                    diagnostics);
                            if (diagnostics != null)
                                diagnostics.V2CorridorTicks +=
                                    AtdDiagnostics.ElapsedSince(corridorStart);
                            if (!corridorValid)
                                continue;
                        }
                        else
                        {
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
                            var legacyCenters = new HashSet<Tile2i>(leftEscape);
                            legacyCenters.UnionWith(rightEscape);
                            centers = legacyCenters.OrderBy(item => item.X)
                                .ThenBy(item => item.Y).ToArray();
                            entryCenters = new[]
                            {
                                leftEscape[leftEscape.Count - 1],
                                rightEscape[rightEscape.Count - 1],
                            }.Distinct().ToArray();
                        }
                    IReadOnlyCollection<string> cleanupKeys;
                    float cleanupCost;
                    if (levelingBridge)
                    {
                        // Leveling contacts came from the captured pathable
                        // mask. Interior ownership and the bridge are already
                        // proven, so no further pathability/cleanup query is
                        // needed.
                        cleanupKeys = Array.Empty<string>();
                        cleanupCost = 0f;
                    }
                    else
                    {
                        long localEscapeStart = AtdDiagnostics.Timestamp();
                        bool localEscapeValid = ground.TryValidateLocalEscape(
                            entryCenters, history, cleanupCostScale,
                            out cleanupKeys, out cleanupCost);
                        if (diagnostics != null)
                            diagnostics.V2LocalEscapeTicks +=
                                AtdDiagnostics.ElapsedSince(localEscapeStart);
                        if (!localEscapeValid)
                            continue;
                    }
                    result.Add(new AccessV2HandoffCandidate(
                        exitDirection, spanLength,
                        left, right,
                        lane0TerminalOrigins,
                        lane1TerminalOrigins,
                        centers,
                        entryCenters,
                        cleanupKeys, cleanupCost,
                        isQuickPath: levelingBridge,
                        centerSpokeCost: centerSpokeCost
                            + Math.Max(0, centers.Count - spanLength * 4)
                                * centerSpokeCost / 2f));
                }
            }

            AccessGroundHandoff? SelectLevelBridge()
            {
                IEnumerable<AccessGroundHandoff> bridges = lane0.Concat(lane1)
                    .Where(candidate =>
                        candidate.Operation == AccessHandoffOperation.Leveling);
                if (requiredGroundEntry.HasValue)
                    bridges = bridges.Where(candidate =>
                        candidate.Tile == requiredGroundEntry.Value);
                foreach (AccessGroundHandoff candidate in bridges
                    .OrderBy(item => item.Tile.X)
                    .ThenBy(item => item.Tile.Y))
                    return candidate;
                return null;
            }
        }

        private static Tile2i GetLevelingCompanionContact(
            Tile2i origin,
            Tile2i exitDirection,
            Tile2i bridge)
        {
            if (exitDirection.X != 0)
                return new Tile2i(
                    exitDirection.X > 0 ? origin.X + 4 : origin.X,
                    Math.Max(origin.Y, Math.Min(origin.Y + 4, bridge.Y)));
            return new Tile2i(
                Math.Max(origin.X, Math.Min(origin.X + 4, bridge.X)),
                exitDirection.Y > 0 ? origin.Y + 4 : origin.Y);
        }

        private static bool TryBuildLevelingGroundEscape(
            AccessGroundHandoff lane0,
            AccessGroundHandoff lane1,
            IReadOnlyList<Tile2i> lane0TerminalOrigins,
            IReadOnlyList<Tile2i> lane1TerminalOrigins,
            Tile2i exitDirection,
            int spanLength,
            Tile2i bridge,
            AccessV2GroundGraph ground,
            AccessV2History history,
            AccessV2HandoffGroundEntryEvaluator? groundEntryValidator,
            int vehicleWidth,
            float cleanupCostScale,
            Tile2i? requiredGroundEntry,
            out IReadOnlyList<Tile2i> escape,
            out IReadOnlyList<Tile2i> groundEntries,
            out IReadOnlyCollection<string> cleanupKeys,
            out float cleanupCost,
            out int outwardSteps)
        {
            escape = Array.Empty<Tile2i>();
            groundEntries = Array.Empty<Tile2i>();
            cleanupKeys = Array.Empty<string>();
            cleanupCost = 0f;
            outwardSteps = 0;
            if (!TryBuildLevelingBridgeEscape(
                    lane0, lane1, exitDirection,
                    spanLength, bridge,
                    out IReadOnlyList<Tile2i> interior,
                    out _))
                return false;

            int forwardX = Math.Sign(exitDirection.X);
            int forwardY = Math.Sign(exitDirection.Y);
            var outward = new Tile2i(forwardX, forwardY);
            int maxOutward = Math.Max(1, vehicleWidth);
            var clearingOrigins = new HashSet<Tile2i>(
                lane0TerminalOrigins);
            clearingOrigins.UnionWith(lane1TerminalOrigins);
            for (int offset = 0; offset <= maxOutward; offset++)
            {
                Tile2i candidate = AccessV2Geometry.Add(
                    bridge, AccessV2Geometry.Scale(outward, offset));
                if (requiredGroundEntry.HasValue
                    && candidate != requiredGroundEntry.Value)
                    continue;
                if (!ground.IsTraversable(candidate, history)
                    || (groundEntryValidator != null
                        && !groundEntryValidator(
                            candidate, clearingOrigins, history))
                    || !ground.TryValidateLocalEscape(
                        new[] { candidate }, history, cleanupCostScale,
                        out IReadOnlyCollection<string> candidateCleanupKeys,
                        out float candidateCleanupCost))
                    continue;

                var centers = new List<Tile2i>(
                    interior.Count + offset);
                centers.AddRange(interior);
                for (int step = 1; step <= offset; step++)
                    centers.Add(AccessV2Geometry.Add(
                        bridge,
                        AccessV2Geometry.Scale(outward, step)));
                escape = centers;
                groundEntries = new[] { candidate };
                cleanupKeys = candidateCleanupKeys;
                cleanupCost = candidateCleanupCost;
                outwardSteps = offset;
                return true;
            }
            return false;
        }

        /// <summary>
        /// A captured mask-pathable edge tile that is level with the target
        /// surface is a complete bridge into a leveling designation. Leveling
        /// owns the full post-work surface, so the straight center corridor is
        /// known geometrically and does not need per-center classification or
        /// a flood fill.
        /// </summary>
        private static bool TryBuildLevelingBridgeEscape(
            AccessGroundHandoff lane0,
            AccessGroundHandoff lane1,
            Tile2i exitDirection,
            int spanLength,
            Tile2i? requiredGroundEntry,
            out IReadOnlyList<Tile2i> escape,
            out IReadOnlyList<Tile2i> groundEntries)
        {
            escape = Array.Empty<Tile2i>();
            groundEntries = Array.Empty<Tile2i>();
            if (spanLength <= 0)
                return false;

            Tile2i entry;
            if (requiredGroundEntry.HasValue)
            {
                entry = requiredGroundEntry.Value;
                if (entry != lane0.Tile && entry != lane1.Tile)
                    return false;
            }
            else
            {
                entry = lane0.Tile;
            }

            int forwardX = Math.Sign(exitDirection.X);
            int forwardY = Math.Sign(exitDirection.Y);
            if ((forwardX == 0) == (forwardY == 0))
                return false;
            var outward = new Tile2i(forwardX, forwardY);
            int rankCount = checked(spanLength * 4);
            var path = new List<Tile2i>(rankCount);
            for (int offset = rankCount - 1; offset >= 0; offset--)
                path.Add(AccessV2Geometry.Add(
                    entry, AccessV2Geometry.Scale(outward, -offset)));
            escape = path;
            groundEntries = new[] { entry };
            return true;
        }

        /// <summary>
        /// Proves a continuous post-work vehicle-center route through the
        /// complete eight-file handoff corridor.  Rank one is already joined
        /// to V by the corner-crest seam, so the flood starts on rank two.
        /// Only files three through six (one-based) are legal centers; the
        /// outer two files on each side are the Mega-mask clearance margin.
        /// </summary>
        private static bool TryBuildPostWorkCorridorEscape(
            AccessGroundHandoff lane0,
            AccessGroundHandoff lane1,
            IReadOnlyList<Tile2i> lane0Origins,
            IReadOnlyList<Tile2i> lane1Origins,
            Tile2i exitDirection,
            AccessV2History history,
            AccessV2GroundGraph ground,
            Func<Tile2i, AccessV2History, bool>? projectedCenterValidator,
            AccessV2HandoffCenterEvaluator postWorkCenterValidator,
            AccessV2HandoffGroundEntryEvaluator? groundEntryValidator,
            Tile2i? requiredGroundEntry,
            out IReadOnlyList<Tile2i> escape,
            out IReadOnlyList<Tile2i> groundEntries,
            AccessSearchDiagnostics? diagnostics)
        {
            escape = Array.Empty<Tile2i>();
            groundEntries = Array.Empty<Tile2i>();
            if (lane0Origins.Count == 0
                || lane0Origins.Count != lane1Origins.Count)
                return false;

            int forwardX = Math.Sign(exitDirection.X);
            int forwardY = Math.Sign(exitDirection.Y);
            if ((forwardX == 0) == (forwardY == 0))
                return false;
            bool travelsX = forwardX != 0;
            int rankCount = checked(lane0Origins.Count * 4);
            var pathable = new HashSet<Tile2i>();
            var rankByTile = new Dictionary<Tile2i, int>();
            var handoffClearingOrigins = new HashSet<Tile2i>();
            if (lane0.Operation == AccessHandoffOperation.Mining
                || lane0.Operation == AccessHandoffOperation.Leveling)
                handoffClearingOrigins.UnionWith(lane0Origins);
            if (lane1.Operation == AccessHandoffOperation.Mining
                || lane1.Operation == AccessHandoffOperation.Leveling)
                handoffClearingOrigins.UnionWith(lane1Origins);

            for (int segment = 0; segment < lane0Origins.Count; segment++)
            {
                Tile2i origin0 = lane0Origins[segment];
                Tile2i origin1 = lane1Origins[segment];
                int longitudinal0 = travelsX ? origin0.X : origin0.Y;
                int longitudinal1 = travelsX ? origin1.X : origin1.Y;
                int transverse0 = travelsX ? origin0.Y : origin0.X;
                int transverse1 = travelsX ? origin1.Y : origin1.X;
                if (longitudinal0 != longitudinal1
                    || Math.Abs(transverse0 - transverse1) != 4)
                    return false;
                if (segment > 0)
                {
                    Tile2i expected0 = new Tile2i(
                        lane0Origins[segment - 1].X + exitDirection.X,
                        lane0Origins[segment - 1].Y + exitDirection.Y);
                    Tile2i expected1 = new Tile2i(
                        lane1Origins[segment - 1].X + exitDirection.X,
                        lane1Origins[segment - 1].Y + exitDirection.Y);
                    if (origin0 != expected0 || origin1 != expected1)
                        return false;
                }

                int transverseMin = Math.Min(transverse0, transverse1);
                for (int withinRank = 0; withinRank < 4; withinRank++)
                {
                    int rank = segment * 4 + withinRank;
                    if (rank == 0)
                        continue;
                    int longitudinal = longitudinal0 + (travelsX
                        ? forwardX > 0 ? withinRank : 3 - withinRank
                        : forwardY > 0 ? withinRank : 3 - withinRank);
                    for (int file = 2; file <= 5; file++)
                    {
                        int transverse = transverseMin + file;
                        Tile2i center = travelsX
                            ? new Tile2i(longitudinal, transverse)
                            : new Tile2i(transverse, longitudinal);
                        bool inLane0 = IsInsideOrigin(center, origin0);
                        bool inLane1 = IsInsideOrigin(center, origin1);
                        if (inLane0 == inLane1)
                            return false;
                        Tile2i owner = inLane0 ? origin0 : origin1;
                        AccessHandoffOperation operation = inLane0
                            ? lane0.Operation
                            : lane1.Operation;
                        if (diagnostics != null)
                            diagnostics.V2CorridorCenterChecks++;
                        if (!postWorkCenterValidator(
                                owner, operation, center, history,
                                handoffClearingOrigins))
                            continue;
                        pathable.Add(center);
                        rankByTile[center] = rank;
                    }
                }
            }

            var queue = new Queue<Tile2i>();
            var visited = new HashSet<Tile2i>();
            var parent = new Dictionary<Tile2i, Tile2i>();
            foreach (KeyValuePair<Tile2i, int> pair in rankByTile
                .Where(pair => pair.Value == 1)
                .OrderBy(pair => pair.Key.X)
                .ThenBy(pair => pair.Key.Y))
            {
                visited.Add(pair.Key);
                queue.Enqueue(pair.Key);
            }
            if (queue.Count == 0)
            {
                RecordCorridorFailure("no-rank1");
                return false;
            }

            RelTile2i[] directions =
            {
                new RelTile2i(1, 0), new RelTile2i(-1, 0),
                new RelTile2i(0, 1), new RelTile2i(0, -1),
            };
            RelTile2i outward = new RelTile2i(forwardX, forwardY);
            const int maxOutsidePostWorkRanks = 4;
            bool reachedLastRank = false;
            bool requiredEntryMatched = false;
            bool outsideGround = false;
            bool outsideEntry = false;
            while (queue.Count > 0)
            {
                if (diagnostics != null)
                    diagnostics.V2CorridorBfsPops++;
                Tile2i current = queue.Dequeue();
                if (rankByTile[current] == rankCount - 1)
                {
                    reachedLastRank = true;
                    var outsideSpoke = new List<Tile2i>();
                    for (int outsideRank = 1;
                        outsideRank <= maxOutsidePostWorkRanks;
                        outsideRank++)
                    {
                        Tile2i outside = current + new RelTile2i(
                            outward.X * outsideRank,
                            outward.Y * outsideRank);
                        if (!postWorkCenterValidator(
                                lane0Origins[lane0Origins.Count - 1],
                                lane0.Operation, outside, history,
                                handoffClearingOrigins))
                            break;
                        outsideSpoke.Add(outside);
                        bool matchesRequired = !requiredGroundEntry.HasValue
                            || outside == requiredGroundEntry.Value;
                        bool groundValid = ground.IsTraversable(outside);
                        bool entryValid = groundEntryValidator == null
                            || groundEntryValidator(
                                outside,
                                handoffClearingOrigins,
                                history);
                        requiredEntryMatched |= matchesRequired;
                        outsideGround |= groundValid;
                        outsideEntry |= entryValid;
                        if (!matchesRequired || !groundValid
                            || !entryValid)
                            continue;

                        Tile2i cursor = current;
                        var path = new List<Tile2i> { cursor };
                        while (parent.TryGetValue(
                            cursor, out Tile2i previous))
                        {
                            cursor = previous;
                            path.Add(cursor);
                        }
                        path.Reverse();
                        path.AddRange(outsideSpoke);
                        escape = path;
                        groundEntries = new[] { outside };
                        return true;
                    }
                }

                for (int index = 0; index < directions.Length; index++)
                {
                    Tile2i next = current + directions[index];
                    if (!pathable.Contains(next) || !visited.Add(next))
                        continue;
                    parent[next] = current;
                    queue.Enqueue(next);
                }
            }
            RecordCorridorFailure(
                $"no-escape reachedLast={reachedLastRank} " +
                $"required={requiredEntryMatched} ground={outsideGround} " +
                $"entry={outsideEntry}");
            return false;

            void RecordCorridorFailure(string reason)
            {
                if (diagnostics == null
                    || !AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                    return;
                string ranks = string.Join(",",
                    Enumerable.Range(1, Math.Max(0, rankCount - 1))
                        .Select(rank => rankByTile.Count(pair =>
                            pair.Value == rank).ToString(
                                System.Globalization.CultureInfo.InvariantCulture)));
                diagnostics.RecordFirstGeneratedHandoff(
                    $"v2-corridor lane0=[{string.Join(",", lane0Origins)}] " +
                    $"lane1=[{string.Join(",", lane1Origins)}] " +
                    $"contacts={lane0.Tile}/{lane1.Tile} " +
                    $"op={lane0.Operation} exit={exitDirection} " +
                    $"ranks=[{ranks}] reject={reason}");
            }

            bool IsInsideOrigin(Tile2i tile, Tile2i origin)
                => tile.X >= origin.X && tile.X < origin.X + 4
                    && tile.Y >= origin.Y && tile.Y < origin.Y + 4;
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
                if (!ground.IsTraversable(center, history)
                    || (projectedCenterValidator != null
                        && !projectedCenterValidator(center, history)))
                {
                    escape = Array.Empty<Tile2i>();
                    return false;
                }
            }

            Tile2i start = prefix[prefix.Count - 1];
            if (!OverlapsWork(start) && ground.IsTraversable(start))
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
                    && (OverlapsWork(current)
                        || !ground.IsTraversable(current));
                step++)
            {
                Tile2i next = current + continuation;
                if (!ground.CanTraverse(current, next, history)
                    || (projectedCenterValidator != null
                        && !projectedCenterValidator(next, history)))
                {
                    escape = Array.Empty<Tile2i>();
                    return false;
                }
                prefix.Add(next);
                current = next;
            }
            if (OverlapsWork(current)
                || !ground.IsTraversable(current))
            {
                escape = Array.Empty<Tile2i>();
                return false;
            }
            escape = prefix;
            return true;

            bool OverlapsWork(Tile2i center)
                => projectedCenterOverlapsWork != null
                    && projectedCenterOverlapsWork(center, history);
        }
    }
}

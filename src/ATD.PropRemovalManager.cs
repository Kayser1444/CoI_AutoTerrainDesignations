// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Runtime coordination for temporary vanilla mining designations used to remove
// vehicle-blocking terrain props without permanently replacing player work.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;

namespace AutoTerrainDesignations
{
    internal enum ATDPropRemovalStage
    {
        Queued,
        QuickPending,
        PreparingTerrain,
        RemovingProp,
        SuspendedForSave,
        Completed,
    }

    internal enum ATDPropRemovalOutcome
    {
        Removed,
        AlreadyAbsent,
        Cancelled,
        PlayerOverride,
        LandscapingRequiredButDisabled,
        NoPropTileInOrigin,
        PlacementFailed,
        NoWorkableCandidate,
        OriginalDesignationRestoreFailed,
    }

    internal enum ATDTemporaryDesignationState
    {
        Owned,
        Missing,
        Replaced,
    }

    internal static class ATDPropRemovalLifecyclePolicy
    {
        internal static ATDTemporaryDesignationState ClassifyTemporary(
            bool liveHasValue,
            bool liveMatchesTemporary)
            => !liveHasValue
                ? ATDTemporaryDesignationState.Missing
                : liveMatchesTemporary
                    ? ATDTemporaryDesignationState.Owned
                    : ATDTemporaryDesignationState.Replaced;

        internal static bool ShouldPreplacePlannedWork(
            bool quickRemove,
            bool firstRequestAtOrigin,
            bool hasPlannedWork)
            => firstRequestAtOrigin && hasPlannedWork;

        internal static bool ValidateFixtures(out string failure)
        {
            failure = string.Empty;
            if (ClassifyTemporary(
                    liveHasValue: false,
                    liveMatchesTemporary: false)
                != ATDTemporaryDesignationState.Missing)
            {
                failure = "An absent owned preview must be retried, not treated as player override.";
                return false;
            }
            if (ClassifyTemporary(
                    liveHasValue: true,
                    liveMatchesTemporary: false)
                != ATDTemporaryDesignationState.Replaced)
            {
                failure = "A replaced owned preview must remain a player override.";
                return false;
            }
            if (ClassifyTemporary(
                    liveHasValue: true,
                    liveMatchesTemporary: true)
                != ATDTemporaryDesignationState.Owned)
            {
                failure = "A matching temporary designation must remain manager-owned.";
                return false;
            }
            if (!ShouldPreplacePlannedWork(
                    quickRemove: true,
                    firstRequestAtOrigin: true,
                    hasPlannedWork: true))
            {
                failure = "Quick cleanup must capture overlapping planned work as its original designation.";
                return false;
            }
            return true;
        }
    }

    internal readonly struct ATDPropRemovalResult
    {
        public int RequestId { get; }
        public TerrainPropId PropId { get; }
        public Tile2i Origin { get; }
        public ATDPropRemovalOutcome Outcome { get; }
        public bool OriginalDesignationRestored { get; }
        public string OwnerToken { get; }

        public ATDPropRemovalResult(int requestId, TerrainPropId propId, Tile2i origin,
            ATDPropRemovalOutcome outcome, bool originalDesignationRestored, string ownerToken)
        {
            RequestId = requestId;
            PropId = propId;
            Origin = origin;
            Outcome = outcome;
            OriginalDesignationRestored = originalDesignationRestored;
            OwnerToken = ownerToken ?? string.Empty;
        }
    }

    internal sealed class ATDPropRemovalRequestHandle
    {
        private event Action<ATDPropRemovalResult>? m_completed;

        public int RequestId { get; }
        public TerrainPropId PropId { get; }
        public Tile2i Origin { get; }
        public string OwnerToken { get; }
        public bool IsCompleted { get; private set; }
        public ATDPropRemovalResult Result { get; private set; }

        internal ATDPropRemovalRequestHandle(int requestId, TerrainPropId propId,
            Tile2i origin, string ownerToken)
        {
            RequestId = requestId;
            PropId = propId;
            Origin = origin;
            OwnerToken = ownerToken ?? string.Empty;
        }

        public ATDPropRemovalRequestHandle OnCompleted(Action<ATDPropRemovalResult> callback)
        {
            if (callback == null)
                return this;
            if (IsCompleted)
                callback(Result);
            else
                m_completed += callback;
            return this;
        }

        internal void Complete(ATDPropRemovalResult result)
        {
            if (IsCompleted)
                return;
            IsCompleted = true;
            Result = result;
            Action<ATDPropRemovalResult>? callbacks = m_completed;
            m_completed = null;
            if (callbacks == null)
                return;
            foreach (Action<ATDPropRemovalResult> callback in
                callbacks.GetInvocationList())
            {
                try { callback(result); }
                catch (Exception ex)
                {
                    AutoDepthDesignation.s_log.Exception(ex,
                        "ATD prop-removal request completion subscriber");
                }
            }
        }
    }

    internal sealed class ATDPropRemovalManager
    {
        private sealed class SavedDesignation
        {
            public string ProtoId { get; }
            public DesignationData Data { get; }

            public SavedDesignation(string protoId, DesignationData data)
            {
                ProtoId = protoId;
                Data = data;
            }
        }

        private sealed class Operation
        {
            public int OperationId;
            public TerrainPropId PropId;
            public Tile2i Origin;
            public SavedDesignation? Original;
            public DesignationData TemporaryData;
            public TerrainDesignationProto? TemporaryProto;
            public TerrainPropId TemporaryTargetPropId;
            public bool HasTemporaryDesignation;
            public bool OriginalSuspendedByManager;
            public ATDPropRemovalStage Stage;
            public bool KeepAliveWithoutHandles;
            public long NextQuickAttemptUtcTicks;
            public CandidateSearchState? CandidateSearch;
            public readonly HashSet<TerrainPropId> TargetProps =
                new HashSet<TerrainPropId>();
            public readonly HashSet<TerrainPropId> QuickTargets =
                new HashSet<TerrainPropId>();
            public readonly List<ATDPropRemovalRequestHandle> Handles =
                new List<ATDPropRemovalRequestHandle>();
        }

        private readonly struct TerrainCandidate
        {
            public TerrainDesignationProto Proto { get; }
            public DesignationData Data { get; }
            public float Cost { get; }
            public float MaxDelta { get; }

            public TerrainCandidate(TerrainDesignationProto proto,
                DesignationData data, float cost, float maxDelta)
            {
                Proto = proto;
                Data = data;
                Cost = cost;
                MaxDelta = maxDelta;
            }
        }

        private enum CandidateAdvanceResult
        {
            Progress,
            Installed,
            Failed,
        }

        private sealed class CandidateSearchState
        {
            public TerrainPropId PropId;
            public float PlacedHeight;
            public float BurialThreshold;
            public bool AllowMining;
            public bool AllowDumping;
            public bool AllowLandscaping;
            public bool HadLandscapingCandidate;
            public int CurrentLow;
            public int MaxLow;
            public int ProfileIndex;
            public bool GenerationComplete;
            public readonly List<TerrainCandidate> Candidates =
                new List<TerrainCandidate>();
        }

        private const double WORK_BUDGET_MILLISECONDS = 5d;

        private readonly TerrainDesignationsManager m_designations;
        private readonly TerrainPropsManager m_props;
        private readonly TerrainManager m_terrain;
        private readonly ProtosDb m_protosDb;
        private readonly TerrainDesignationProto m_miningProto;
        private readonly TerrainDesignationProto m_dumpingProto;
        private readonly PropsRemovalProcessor m_propsRemovalProcessor;
        private readonly Dictionary<TerrainPropId, Operation> m_operations =
            new Dictionary<TerrainPropId, Operation>();
        private readonly Dictionary<Tile2i, Operation> m_operationsByOrigin =
            new Dictionary<Tile2i, Operation>();
        private readonly List<ATDPropRemovalResult> m_pendingPublications =
            new List<ATDPropRemovalResult>();
        private int m_nextRequestId;
        private int m_roundRobinCursor;
        private long m_nextSlowTickWarningUtcTicks;
        private bool m_isSaving;

        public event Action<ATDPropRemovalResult>? PropRemovalCompleted;

        public int ActiveRequestCount => m_operationsByOrigin.Count;

        public ATDPropRemovalManager(TerrainDesignationsManager designations,
            TerrainPropsManager props, ProtosDb protosDb,
            TerrainDesignationProto miningProto,
            TerrainDesignationProto dumpingProto,
            PropsRemovalProcessor propsRemovalProcessor)
        {
            m_designations = designations;
            m_props = props;
            m_terrain = designations.TerrainManager;
            m_protosDb = protosDb;
            m_miningProto = miningProto;
            m_dumpingProto = dumpingProto;
            m_propsRemovalProcessor = propsRemovalProcessor;
        }

        private bool AddOrReplaceDesignation(DesignationData data,
            TerrainDesignationProto proto)
        {
            using (AutoDepthDesignation.BeginManagedDesignationMutation())
                return m_designations.AddOrReplaceDesignation(proto, data);
        }

        private void RemoveDesignation(Tile2i origin)
        {
            using (AutoDepthDesignation.BeginManagedDesignationMutation())
                m_designations.RemoveDesignation(origin);
        }

        public ATDPropRemovalRequestHandle RequestRemoval(TerrainPropId propId,
            Tile2i cleanupOrigin, string ownerToken,
            bool quickRemove)
        {
            cleanupOrigin = TerrainDesignation.GetOrigin(cleanupOrigin);
            if (!m_props.TerrainProps.ContainsKey(propId))
            {
                var absentHandle = new ATDPropRemovalRequestHandle(++m_nextRequestId,
                    propId, cleanupOrigin, ownerToken);
                if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                    AutoDepthDesignation.LogExperimentalAccessTrace(
                        $"[ATD Prop Removal] request={absentHandle.RequestId} " +
                        $"prop={propId} origin={cleanupOrigin} result=already-absent " +
                        $"owner={ownerToken}");
                CompleteHandle(absentHandle, ATDPropRemovalOutcome.AlreadyAbsent,
                    originalRestored: true);
                return absentHandle;
            }

            if (m_operations.TryGetValue(propId, out Operation existing))
            {
                var coalescedHandle = new ATDPropRemovalRequestHandle(++m_nextRequestId,
                    propId, existing.Origin, ownerToken);
                existing.Handles.Add(coalescedHandle);
                if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                    AutoDepthDesignation.LogExperimentalAccessTrace(
                        $"[ATD Prop Removal] request={coalescedHandle.RequestId} " +
                        $"prop={propId} origin={existing.Origin} action=join-prop " +
                        $"operation={existing.OperationId} quick={quickRemove} " +
                        $"owner={ownerToken}");
                // The first request owns the strategy. This guard is important:
                // a duplicate request must never reinterpret or replace the
                // manager's own in-flight designation.
                return coalescedHandle;
            }

            if (m_operationsByOrigin.TryGetValue(
                    cleanupOrigin, out Operation sameOrigin))
            {
                var joinedHandle = new ATDPropRemovalRequestHandle(++m_nextRequestId,
                    propId, sameOrigin.Origin, ownerToken);
                sameOrigin.TargetProps.Add(propId);
                if (quickRemove)
                    sameOrigin.QuickTargets.Add(propId);
                sameOrigin.Handles.Add(joinedHandle);
                m_operations.Add(propId, sameOrigin);
                if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                    AutoDepthDesignation.LogExperimentalAccessTrace(
                        $"[ATD Prop Removal] request={joinedHandle.RequestId} " +
                        $"prop={propId} origin={sameOrigin.Origin} action=join-origin " +
                        $"operation={sameOrigin.OperationId} targets={sameOrigin.TargetProps.Count} " +
                        $"quick={quickRemove} owner={ownerToken}");
                return joinedHandle;
            }

            var handle = new ATDPropRemovalRequestHandle(++m_nextRequestId,
                propId, cleanupOrigin, ownerToken);

            SavedDesignation? original = null;
            Option<TerrainDesignation> current = m_designations.GetDesignationAt(cleanupOrigin);
            if (current.HasValue)
                original = new SavedDesignation(current.Value.Prototype.Id.Value,
                    current.Value.Data);

            var operation = new Operation
            {
                OperationId = handle.RequestId,
                PropId = propId,
                Origin = cleanupOrigin,
                Original = original,
                Stage = ATDPropRemovalStage.Queued,
            };
            operation.TargetProps.Add(propId);
            if (quickRemove)
                operation.QuickTargets.Add(propId);
            operation.Handles.Add(handle);

            m_operations.Add(propId, operation);
            m_operationsByOrigin.Add(cleanupOrigin, operation);
            if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                AutoDepthDesignation.LogExperimentalAccessTrace(
                    $"[ATD Prop Removal] request={handle.RequestId} prop={propId} " +
                    $"origin={cleanupOrigin} action=new-operation " +
                    $"original={(original == null ? "none" : original.ProtoId)} " +
                    $"quick={quickRemove} owner={ownerToken}");
            return handle;
        }

        private bool TryQuickRemove(TerrainPropId propId)
        {
            Tile2i propTile = propId.Position.AsFull;
            var command = new QuickRemovePropsCmd(
                new RectangleTerrainArea2i(propTile, RelTile2i.One));
            m_propsRemovalProcessor.Invoke(command);
            bool removed = !m_props.TerrainProps.ContainsKey(propId);
            AutoDepthDesignation.LogExperimentalAccessDebug(
                $"[ATD Prop Removal] quickRemove prop={propId} removed={removed} " +
                $"error={(command.ResultSet && command.HasError ? command.ErrorMessage : "none")}");
            return removed;
        }

        public void Cancel(ATDPropRemovalRequestHandle handle)
        {
            if (handle == null || handle.IsCompleted)
                return;
            if (!m_operations.TryGetValue(handle.PropId, out Operation operation)
                || !operation.Handles.Remove(handle))
            {
                CompleteHandle(handle, ATDPropRemovalOutcome.Cancelled,
                    originalRestored: false);
                return;
            }

            CompleteHandle(handle, ATDPropRemovalOutcome.Cancelled,
                originalRestored: false);
            if (!operation.Handles.Any(item => item.PropId == handle.PropId)
                && !operation.KeepAliveWithoutHandles)
            {
                operation.TargetProps.Remove(handle.PropId);
                operation.QuickTargets.Remove(handle.PropId);
                if (operation.CandidateSearch?.PropId == handle.PropId)
                    operation.CandidateSearch = null;
                m_operations.Remove(handle.PropId);
                if (operation.TargetProps.Count > 0
                    && !operation.TargetProps.Contains(operation.PropId))
                    operation.PropId = operation.TargetProps.First();
            }
            if (operation.Handles.Count == 0 && !operation.KeepAliveWithoutHandles)
            {
                RestoreOriginal(operation, removeOwnedTemporary: true,
                    out bool restored);
                RemoveOperationMappings(operation);
                AutoDepthDesignation.LogExperimentalAccessDebug(
                    $"[ATD Prop Removal] cancelled prop={operation.PropId} " +
                    $"origin={operation.Origin} restored={restored}");
            }
        }

        public void Tick(bool allowQuickRemoval = true)
        {
            if (m_isSaving || m_operations.Count == 0)
            {
                PublishPendingResults();
                return;
            }

            Operation[] operations = m_operationsByOrigin.Values.ToArray();
            if (operations.Length == 0)
            {
                PublishPendingResults();
                return;
            }

            int index = m_roundRobinCursor % operations.Length;
            int unavailable = 0;
            int steps = 0;
            var blockedThisTick = new HashSet<Operation>();
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (unavailable < operations.Length
                && (steps == 0
                    || stopwatch.Elapsed.TotalMilliseconds
                        < WORK_BUDGET_MILLISECONDS))
            {
                Operation operation = operations[index];
                index = (index + 1) % operations.Length;
                if (operation.Stage == ATDPropRemovalStage.Completed
                    || blockedThisTick.Contains(operation))
                {
                    unavailable++;
                    continue;
                }

                bool madeProgress = AdvanceOperation(operation, allowQuickRemoval);
                steps++;
                if (!madeProgress
                    || operation.Stage == ATDPropRemovalStage.Completed)
                {
                    blockedThisTick.Add(operation);
                    unavailable++;
                }
                else
                    unavailable = 0;
            }
            m_roundRobinCursor = index;

            PublishPendingResults();
            if (stopwatch.Elapsed.TotalMilliseconds >= 100d)
            {
                long nowTicks = DateTime.UtcNow.Ticks;
                if (nowTicks >= m_nextSlowTickWarningUtcTicks)
                {
                    m_nextSlowTickWarningUtcTicks = nowTicks
                        + TimeSpan.TicksPerSecond * 10;
                    AutoDepthDesignation.s_log.Warning(
                        "[ATD Prop Removal] Cooperative manager tick took "
                        + stopwatch.Elapsed.TotalMilliseconds.ToString(
                            "0.##", CultureInfo.InvariantCulture)
                        + " ms; a single vanilla simulation call or completion "
                        + "subscriber exceeded the 5 ms cooperative budget.");
                }
            }
        }

        private bool AdvanceOperation(Operation operation, bool allowQuickRemoval)
        {
            operation.QuickTargets.RemoveWhere(
                propId => !m_props.TerrainProps.ContainsKey(propId));
            if (operation.QuickTargets.Count > 0)
            {
                // Paused ticks show a legacy-style mining preview for Quick
                // remove targets, but must not consume Unity.
                if (!allowQuickRemoval)
                    return TryPlacePausedQuickRemovalPreview(operation);
                return AdvanceQuickRemoval(operation);
            }

            TerrainPropId[] remaining = operation.TargetProps
                .Where(m_props.TerrainProps.ContainsKey).ToArray();
            if (remaining.Length == 0)
            {
                // Vanilla adds harvested products to the excavator's cargo
                // before releasing its job. Keep the temporary mining work
                // alive until the bucket has been offered to a truck.
                Option<TerrainDesignation> completedLive =
                    m_designations.GetDesignationAt(operation.Origin);
                if (completedLive.HasValue
                    && completedLive.Value.Prototype == operation.TemporaryProto
                    && completedLive.Value.Data.Equals(operation.TemporaryData)
                    && operation.TemporaryProto == m_miningProto
                    && completedLive.Value.NumberOfJobsAssigned > 0)
                    return false;
                FinishOperation(operation, ATDPropRemovalOutcome.Removed,
                    restoreOriginal: true);
                return true;
            }
            operation.PropId = remaining[0];

            if (operation.HasTemporaryDesignation)
            {
                Option<TerrainDesignation> live =
                    m_designations.GetDesignationAt(operation.Origin);
                if (!live.HasValue
                    || live.Value.Prototype != operation.TemporaryProto
                    || !live.Value.Data.Equals(operation.TemporaryData))
                {
                    FinishOperation(operation,
                        ATDPropRemovalOutcome.PlayerOverride,
                        restoreOriginal: false);
                    return true;
                }
                if (!m_props.TerrainProps.ContainsKey(
                        operation.TemporaryTargetPropId))
                {
                    if (operation.TemporaryProto == m_miningProto
                        && live.Value.NumberOfJobsAssigned > 0)
                        return false;
                    RemoveDesignation(operation.Origin);
                    operation.HasTemporaryDesignation = false;
                    operation.TemporaryProto = null;
                    operation.CandidateSearch = null;
                    operation.Stage = ATDPropRemovalStage.Queued;
                    return true;
                }
                if (!IsTemporaryFulfilled(operation, live.Value)
                    || live.Value.NumberOfJobsAssigned > 0)
                    return false;
                RemoveDesignation(operation.Origin);
                operation.HasTemporaryDesignation = false;
                operation.TemporaryProto = null;
                operation.CandidateSearch = null;
                operation.Stage = ATDPropRemovalStage.Queued;
                return true;
            }

            CandidateAdvanceResult result = AdvanceCandidateSearch(
                operation, out ATDPropRemovalOutcome failure);
            if (result == CandidateAdvanceResult.Failed)
            {
                FinishOperation(operation, failure, restoreOriginal: true);
                return true;
            }
            return true;
        }

        private bool TryPlacePausedQuickRemovalPreview(Operation operation)
        {
            // An existing player or pathfinder designation is already a clear
            // visual marker for this origin. Preserve it instead of replacing
            // it with the temporary Quick-remove preview.
            if (operation.Original != null)
                return false;
            if (operation.HasTemporaryDesignation)
            {
                Option<TerrainDesignation> live =
                    m_designations.GetDesignationAt(operation.Origin);
                ATDTemporaryDesignationState temporaryState =
                    ATDPropRemovalLifecyclePolicy.ClassifyTemporary(
                        live.HasValue,
                        live.HasValue
                        && live.Value.Prototype == operation.TemporaryProto
                        && live.Value.Data.Equals(operation.TemporaryData));
                if (temporaryState == ATDTemporaryDesignationState.Owned)
                    return false;
                if (temporaryState == ATDTemporaryDesignationState.Missing)
                {
                    ForgetMissingTemporaryDesignation(operation);
                    return true;
                }
                FinishOperation(operation,
                    ATDPropRemovalOutcome.PlayerOverride,
                    restoreOriginal: false);
                return true;
            }
            if (!IsOriginalOrEmpty(operation,
                    m_designations.GetDesignationAt(operation.Origin)))
            {
                FinishOperation(operation, ATDPropRemovalOutcome.PlayerOverride,
                    restoreOriginal: false);
                return true;
            }

            DesignationData preview = GetLegacyQuickRemovalPreview(
                operation.Origin);
            if (!AddOrReplaceDesignation(preview, m_miningProto))
                return false;

            operation.OriginalSuspendedByManager = operation.Original != null;
            operation.TemporaryData = preview;
            operation.TemporaryProto = m_miningProto;
            operation.TemporaryTargetPropId = operation.PropId;
            operation.HasTemporaryDesignation = true;
            operation.Stage = ATDPropRemovalStage.QuickPending;
            AutoDepthDesignation.LogExperimentalAccessDebug(
                $"[ATD Prop Removal] quick-preview prop={operation.PropId} " +
                $"origin={operation.Origin} data={preview}");
            return true;
        }

        private DesignationData GetLegacyQuickRemovalPreview(Tile2i origin)
        {
            HeightTilesI CornerHeight(int x, int y) => new HeightTilesI(
                (int)Math.Ceiling(m_terrain.GetHeight(
                    origin + new RelTile2i(x, y)).Value.ToFloat() + 1f));

            return new DesignationData(origin,
                CornerHeight(0, 0), CornerHeight(4, 0),
                CornerHeight(4, 4), CornerHeight(0, 4));
        }

        private bool AdvanceQuickRemoval(Operation operation)
        {
            operation.CandidateSearch = null;
            if (operation.HasTemporaryDesignation)
            {
                Option<TerrainDesignation> owned =
                    m_designations.GetDesignationAt(operation.Origin);
                ATDTemporaryDesignationState temporaryState =
                    ATDPropRemovalLifecyclePolicy.ClassifyTemporary(
                        owned.HasValue,
                        owned.HasValue
                        && owned.Value.Prototype == operation.TemporaryProto
                        && owned.Value.Data.Equals(operation.TemporaryData));
                if (temporaryState == ATDTemporaryDesignationState.Missing)
                {
                    ForgetMissingTemporaryDesignation(operation);
                    return true;
                }
                if (temporaryState == ATDTemporaryDesignationState.Replaced)
                {
                    FinishOperation(operation,
                        ATDPropRemovalOutcome.PlayerOverride,
                        restoreOriginal: false);
                    return true;
                }
                if (owned.Value.NumberOfJobsAssigned > 0)
                    return false;
                RemoveDesignation(operation.Origin);
                operation.HasTemporaryDesignation = false;
                operation.TemporaryProto = null;
                operation.Stage = ATDPropRemovalStage.QuickPending;
                return true;
            }

            if (operation.Stage != ATDPropRemovalStage.QuickPending)
            {
                Option<TerrainDesignation> live =
                    m_designations.GetDesignationAt(operation.Origin);
                if (!IsOriginalOrEmpty(operation, live))
                {
                    FinishOperation(operation,
                        ATDPropRemovalOutcome.PlayerOverride,
                        restoreOriginal: false);
                    return true;
                }
                if (live.HasValue)
                {
                    RemoveDesignation(operation.Origin);
                    operation.OriginalSuspendedByManager = true;
                }
                operation.Stage = ATDPropRemovalStage.QuickPending;
                return true;
            }

            long nowTicks = DateTime.UtcNow.Ticks;
            if (nowTicks < operation.NextQuickAttemptUtcTicks)
                return false;
            TerrainPropId quickTarget = operation.QuickTargets
                .OrderBy(item => item.Position.X)
                .ThenBy(item => item.Position.Y)
                .First();
            TryQuickRemove(quickTarget);
            operation.NextQuickAttemptUtcTicks = nowTicks
                + TimeSpan.TicksPerSecond;
            if (!m_props.TerrainProps.ContainsKey(quickTarget))
                operation.QuickTargets.Remove(quickTarget);
            if (operation.QuickTargets.Count == 0)
                operation.Stage = ATDPropRemovalStage.Queued;
            return true;
        }

        public void PrepareForSave()
        {
            if (m_isSaving)
                return;
            m_isSaving = true;
            foreach (Operation operation in m_operationsByOrigin.Values.ToArray())
            {
                operation.CandidateSearch = null;
                if (operation.HasTemporaryDesignation
                    && !OwnsLiveTemporaryDesignation(operation))
                {
                    FinishOperation(operation, ATDPropRemovalOutcome.PlayerOverride,
                        restoreOriginal: false);
                    continue;
                }
                if (!RestoreOriginal(operation, removeOwnedTemporary: true, out _))
                {
                    FinishOperation(operation, ATDPropRemovalOutcome.PlayerOverride,
                        restoreOriginal: false);
                    continue;
                }
                operation.Stage = ATDPropRemovalStage.SuspendedForSave;
            }
            PublishPendingResults();
        }

        public void ResumeAfterSave()
        {
            if (!m_isSaving)
                return;
            m_isSaving = false;
            ResumeSuspendedOperations();
        }

        public void ResumeLoadedRequests()
        {
            m_isSaving = false;
            ResumeSuspendedOperations();
        }

        public void Dispose(bool restoreOriginals)
        {
            foreach (Operation operation in m_operationsByOrigin.Values.ToArray())
            {
                bool restored = !restoreOriginals;
                if (restoreOriginals)
                    RestoreOriginal(operation, removeOwnedTemporary: true, out restored);
                CompleteOperationHandles(operation, ATDPropRemovalOutcome.Cancelled,
                    originalRestored: restored);
            }
            m_operations.Clear();
            m_operationsByOrigin.Clear();
            m_roundRobinCursor = 0;
            PublishPendingResults();
        }

        private void ResumeSuspendedOperations()
        {
            foreach (Operation operation in m_operationsByOrigin.Values.ToArray())
            {
                TerrainPropId[] remaining = operation.TargetProps
                    .Where(m_props.TerrainProps.ContainsKey).ToArray();
                if (remaining.Length == 0)
                {
                    FinishOperation(operation, ATDPropRemovalOutcome.Removed,
                        restoreOriginal: false);
                    continue;
                }
                operation.PropId = remaining[0];
                operation.HasTemporaryDesignation = false;
                operation.TemporaryProto = null;
                operation.CandidateSearch = null;
                operation.Stage = ATDPropRemovalStage.Queued;
            }
            PublishPendingResults();
        }

        private CandidateAdvanceResult AdvanceCandidateSearch(
            Operation operation, out ATDPropRemovalOutcome failure)
        {
            failure = ATDPropRemovalOutcome.NoWorkableCandidate;
            CandidateSearchState? search = operation.CandidateSearch;
            if (search == null || search.PropId != operation.PropId)
            {
                if (!TryInitializeCandidateSearch(operation,
                        out search, out failure))
                    return CandidateAdvanceResult.Failed;
                operation.CandidateSearch = search;
                return CandidateAdvanceResult.Progress;
            }

            if (!search.GenerationComplete)
            {
                if (search.CurrentLow > search.MaxLow)
                {
                    search.GenerationComplete = true;
                    if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                        AutoDepthDesignation.LogExperimentalAccessTrace(
                            $"[ATD Prop Removal] operation={operation.OperationId} " +
                            $"prop={search.PropId} candidate-generation-complete " +
                            $"candidates={search.Candidates.Count} " +
                            $"landscapingRejected={search.HadLandscapingCandidate}");
                    return CandidateAdvanceResult.Progress;
                }

                DesignationData data = GetProfile(operation.Origin,
                    search.CurrentLow, search.ProfileIndex);
                search.ProfileIndex++;
                if (search.ProfileIndex >= 5)
                {
                    search.ProfileIndex = 0;
                    search.CurrentLow++;
                }
                AddCandidateForProfile(operation, search, data);
                return CandidateAdvanceResult.Progress;
            }

            if (search.Candidates.Count == 0)
            {
                if (!search.AllowLandscaping
                    && search.HadLandscapingCandidate)
                    failure = ATDPropRemovalOutcome.LandscapingRequiredButDisabled;
                return CandidateAdvanceResult.Failed;
            }
            if (!IsOriginalOrEmpty(operation,
                    m_designations.GetDesignationAt(operation.Origin)))
            {
                failure = ATDPropRemovalOutcome.PlayerOverride;
                return CandidateAdvanceResult.Failed;
            }

            TerrainCandidate candidate = PopCandidate(search.Candidates);
            if (!AddOrReplaceDesignation(candidate.Data, candidate.Proto))
                return CandidateAdvanceResult.Progress;
            operation.OriginalSuspendedByManager =
                operation.Original != null;
            Option<TerrainDesignation> placed =
                m_designations.GetDesignationAt(operation.Origin);
            bool workable = placed.HasValue
                && placed.Value.Prototype == candidate.Proto
                && placed.Value.Data.Equals(candidate.Data)
                && (candidate.Proto == m_miningProto
                    ? placed.Value.IsReadyToMineNonAmphibious()
                    : placed.Value.IsReadyToDumpNonAmphibious());
            if (!workable)
            {
                if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                    AutoDepthDesignation.LogExperimentalAccessTrace(
                        $"[ATD Prop Removal] operation={operation.OperationId} " +
                        $"prop={operation.PropId} rejected-candidate " +
                        $"proto={candidate.Proto.Id.Value} data={candidate.Data} " +
                        $"cost={candidate.Cost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                        $"reason=vanilla-not-ready-or-placement-mismatch");
                if (placed.HasValue
                    && placed.Value.Prototype == candidate.Proto
                    && placed.Value.Data.Equals(candidate.Data))
                    RemoveDesignation(operation.Origin);
                return CandidateAdvanceResult.Progress;
            }

            operation.TemporaryData = candidate.Data;
            operation.TemporaryProto = candidate.Proto;
            operation.TemporaryTargetPropId = operation.PropId;
            operation.HasTemporaryDesignation = true;
            operation.Stage = ATDPropRemovalStage.RemovingProp;
            operation.CandidateSearch = null;
            AutoDepthDesignation.LogExperimentalAccessDebug(
                $"[ATD Prop Removal] prop={operation.PropId} origin={operation.Origin} " +
                $"proto={candidate.Proto.Id.Value} " +
                $"cost={candidate.Cost.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"maxDelta={candidate.MaxDelta.ToString("0.##", CultureInfo.InvariantCulture)}");
            return CandidateAdvanceResult.Installed;
        }

        private bool TryInitializeCandidateSearch(Operation operation,
            out CandidateSearchState search,
            out ATDPropRemovalOutcome failure)
        {
            search = null!;
            failure = ATDPropRemovalOutcome.NoWorkableCandidate;
            if (!m_props.TerrainProps.TryGetValue(operation.PropId,
                    out TerrainPropData prop))
            {
                failure = ATDPropRemovalOutcome.AlreadyAbsent;
                return false;
            }
            if (!TryFindPropTileInOrigin(operation.PropId, operation.Origin,
                    out _))
            {
                failure = ATDPropRemovalOutcome.NoPropTileInOrigin;
                return false;
            }
            if (!IsOriginalOrEmpty(operation,
                    m_designations.GetDesignationAt(operation.Origin)))
            {
                failure = ATDPropRemovalOutcome.PlayerOverride;
                return false;
            }

            float localPropX = prop.Position.X.ToFloat()
                - operation.Origin.X;
            float localPropY = prop.Position.Y.ToFloat()
                - operation.Origin.Y;
            if (localPropX < 0f || localPropX > 4f
                || localPropY < 0f || localPropY > 4f)
            {
                failure = ATDPropRemovalOutcome.NoPropTileInOrigin;
                return false;
            }
            float placedHeight = prop.PlacedAtHeight.Value.ToFloat();
            float burialThreshold = prop.Proto.DespawnBuriedThreshold
                .ScaledBy(prop.Scale).Value.ToFloat();
            string originalProtoId = operation.Original?.ProtoId
                ?? m_miningProto.Id.Value;
            bool allowMining;
            bool allowDumping;
            if (operation.Original == null)
            {
                allowMining = true;
                allowDumping = false;
            }
            else if (originalProtoId == m_miningProto.Id.Value)
            {
                // A fulfilled/no-op mining designation at or below the prop
                // level still needs a fresh workable mining profile (the area
                // cleanup button case). A mining target above the prop cannot
                // remove it, so accessway cleanup must bury it first.
                float originalTarget = GetTargetHeightAt(
                    operation.Original.Data, prop.Position);
                allowMining = originalTarget <= placedHeight + 0.0001f;
                allowDumping = !allowMining;
            }
            else if (originalProtoId == m_dumpingProto.Id.Value)
            {
                float originalTarget = GetTargetHeightAt(
                    operation.Original.Data, prop.Position);
                allowDumping = originalTarget - placedHeight
                    > burialThreshold + 0.0001f;
                allowMining = !allowDumping;
            }
            else
            {
                allowMining = true;
                allowDumping = true;
            }

            float minTerrain = float.MaxValue;
            float maxTerrain = float.MinValue;
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    float terrain = m_terrain.GetHeight(
                        operation.Origin + new RelTile2i(x, y))
                        .Value.ToFloat();
                    minTerrain = Math.Min(minTerrain, terrain);
                    maxTerrain = Math.Max(maxTerrain, terrain);
                }
            int minLow = (int)Math.Floor(
                Math.Min(minTerrain, placedHeight)) - 1;
            int maxLow = (int)Math.Ceiling(Math.Max(
                maxTerrain, placedHeight + burialThreshold)) + 1;
            search = new CandidateSearchState
            {
                PropId = operation.PropId,
                PlacedHeight = placedHeight,
                BurialThreshold = burialThreshold,
                AllowMining = allowMining,
                AllowDumping = allowDumping,
                AllowLandscaping =
                    AutoDepthDesignation.AccessAllowDigToRemoveDebris,
                CurrentLow = minLow,
                MaxLow = maxLow,
            };
            if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                AutoDepthDesignation.LogExperimentalAccessTrace(
                    $"[ATD Prop Removal] operation={operation.OperationId} " +
                    $"prop={operation.PropId} origin={operation.Origin} " +
                    $"search=mining:{allowMining},dumping:{allowDumping}," +
                    $"landscaping:{search.AllowLandscaping} " +
                    $"placedHeight={placedHeight.ToString("0.###", CultureInfo.InvariantCulture)} " +
                    $"burialThreshold={burialThreshold.ToString("0.###", CultureInfo.InvariantCulture)} " +
                    $"lowRange={minLow}..{maxLow}");
            return true;
        }

        private void AddCandidateForProfile(Operation operation,
            CandidateSearchState search, DesignationData data)
        {
            if (!m_props.TerrainProps.TryGetValue(search.PropId,
                    out TerrainPropData prop))
                return;
            float targetAtProp = GetTargetHeightAt(data, prop.Position);
            if (search.AllowMining
                && targetAtProp <= search.PlacedHeight + 0.0001f)
            {
                float movement = EstimateDirectionalVolume(
                    data, mining: true, out float maxDelta);
                // A prop at the designation's exact height makes an otherwise
                // fulfilled flat mining designation workable. Keep that
                // zero-terrain-movement candidate: it is the cheapest and
                // cleanest way to remove debris from level ground.
                if (movement <= 0.001f || search.AllowLandscaping)
                    PushCandidate(search.Candidates,
                        new TerrainCandidate(m_miningProto, data,
                            movement + EstimateRestoreCost(operation, data),
                            maxDelta));
                else
                    search.HadLandscapingCandidate = true;
            }
            if (search.AllowDumping
                && targetAtProp - search.PlacedHeight
                    > search.BurialThreshold + 0.0001f)
            {
                float movement = EstimateDirectionalVolume(
                    data, mining: false, out float maxDelta);
                if (movement <= 0.001f || search.AllowLandscaping)
                    PushCandidate(search.Candidates,
                        new TerrainCandidate(m_dumpingProto, data,
                            movement + EstimateRestoreCost(operation, data),
                            maxDelta));
                else
                    search.HadLandscapingCandidate = true;
            }
        }

        private static DesignationData GetProfile(Tile2i origin,
            int low, int profileIndex)
        {
            var lo = new HeightTilesI(low);
            var hi = new HeightTilesI(low + 1);
            switch (profileIndex)
            {
                case 1: return new DesignationData(origin, lo, hi, hi, lo);
                case 2: return new DesignationData(origin, hi, lo, lo, hi);
                case 3: return new DesignationData(origin, lo, lo, hi, hi);
                case 4: return new DesignationData(origin, hi, hi, lo, lo);
                default: return new DesignationData(origin, lo);
            }
        }

        private static void PushCandidate(List<TerrainCandidate> heap,
            TerrainCandidate candidate)
        {
            int index = heap.Count;
            heap.Add(candidate);
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (CompareCandidates(heap[parent], candidate) <= 0)
                    break;
                heap[index] = heap[parent];
                index = parent;
            }
            heap[index] = candidate;
        }

        private static TerrainCandidate PopCandidate(
            List<TerrainCandidate> heap)
        {
            TerrainCandidate result = heap[0];
            int lastIndex = heap.Count - 1;
            TerrainCandidate tail = heap[lastIndex];
            heap.RemoveAt(lastIndex);
            if (lastIndex == 0)
                return result;

            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= heap.Count)
                    break;
                int right = left + 1;
                int child = right < heap.Count
                    && CompareCandidates(heap[right], heap[left]) < 0
                        ? right : left;
                if (CompareCandidates(tail, heap[child]) <= 0)
                    break;
                heap[index] = heap[child];
                index = child;
            }
            heap[index] = tail;
            return result;
        }

        private static int CompareCandidates(TerrainCandidate left,
            TerrainCandidate right)
        {
            int comparison = left.Cost.CompareTo(right.Cost);
            if (comparison != 0) return comparison;
            comparison = left.MaxDelta.CompareTo(right.MaxDelta);
            if (comparison != 0) return comparison;
            comparison = string.Compare(left.Proto.Id.Value,
                right.Proto.Id.Value, StringComparison.Ordinal);
            if (comparison != 0) return comparison;
            comparison = left.Data.OriginTargetHeight.Value.CompareTo(
                right.Data.OriginTargetHeight.Value);
            if (comparison != 0) return comparison;
            comparison = left.Data.PlusXTargetHeight.Value.CompareTo(
                right.Data.PlusXTargetHeight.Value);
            if (comparison != 0) return comparison;
            comparison = left.Data.PlusXyTargetHeight.Value.CompareTo(
                right.Data.PlusXyTargetHeight.Value);
            if (comparison != 0) return comparison;
            return left.Data.PlusYTargetHeight.Value.CompareTo(
                right.Data.PlusYTargetHeight.Value);
        }

        private static bool IsOriginalOrEmpty(Operation operation,
            Option<TerrainDesignation> live)
        {
            if (!live.HasValue)
                return operation.Original == null
                    || operation.OriginalSuspendedByManager;
            return operation.Original != null
                && live.Value.Prototype.Id.Value
                    == operation.Original.ProtoId
                && live.Value.Data.Equals(operation.Original.Data);
        }

        private float EstimateDirectionalVolume(DesignationData data,
            bool mining, out float maxDelta)
        {
            float total = 0f;
            maxDelta = 0f;
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    Tile2i tile = data.OriginTile + new RelTile2i(x, y);
                    float terrain = m_terrain.GetHeight(tile).Value.ToFloat();
                    float target = GetTargetHeightAt(data, tile);
                    float delta = mining ? terrain - target : target - terrain;
                    if (delta <= 0f)
                        continue;
                    total += delta;
                    if (delta > maxDelta)
                        maxDelta = delta;
                }
            }
            return total;
        }

        private static float EstimateRestoreCost(Operation operation,
            DesignationData temporary)
        {
            if (operation.Original == null)
                return 0f;
            float total = 0f;
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    Tile2i tile = temporary.OriginTile
                        + new RelTile2i(x, y);
                    total += Math.Abs(
                        GetTargetHeightAt(operation.Original.Data, tile)
                        - GetTargetHeightAt(temporary, tile));
                }
            return total;
        }

        private static float GetTargetHeightAt(DesignationData data, Tile2i tile)
        {
            RelTile2i rel = tile - data.OriginTile;
            HeightTilesF west = data.OriginTargetHeight.HeightTilesF.Lerp(
                data.PlusYTargetHeight.HeightTilesF, rel.Y, 4);
            HeightTilesF east = data.PlusXTargetHeight.HeightTilesF.Lerp(
                data.PlusXyTargetHeight.HeightTilesF, rel.Y, 4);
            return west.Lerp(east, rel.X, 4).Value.ToFloat();
        }

        private static float GetTargetHeightAt(DesignationData data,
            Tile2f position)
        {
            float localX = position.X.ToFloat() - data.OriginTile.X;
            float localY = position.Y.ToFloat() - data.OriginTile.Y;
            float north = data.OriginTargetHeight.Value
                + (data.PlusXTargetHeight.Value
                    - data.OriginTargetHeight.Value) * localX / 4f;
            float south = data.PlusYTargetHeight.Value
                + (data.PlusXyTargetHeight.Value
                    - data.PlusYTargetHeight.Value) * localX / 4f;
            return north + (south - north) * localY / 4f;
        }

        private bool IsTemporaryFulfilled(Operation operation,
            TerrainDesignation designation)
        {
            return operation.TemporaryProto == m_miningProto
                ? designation.IsMiningFulfilled
                : operation.TemporaryProto == m_dumpingProto
                    && designation.IsDumpingFulfilled;
        }

        private bool TryFindPropTileInOrigin(TerrainPropId propId, Tile2i origin,
            out Tile2i propTile)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    Tile2i tile = origin + new RelTile2i(x, y);
                    if (m_props.TerrainTileToProp.TryGetValue(tile.AsSlim,
                            out TerrainPropId mapped) && mapped == propId)
                    {
                        propTile = tile;
                        return true;
                    }
                }
            }
            propTile = default;
            return false;
        }

        private static void ForgetMissingTemporaryDesignation(
            Operation operation)
        {
            operation.HasTemporaryDesignation = false;
            operation.TemporaryProto = null;
            operation.CandidateSearch = null;
            operation.Stage = ATDPropRemovalStage.QuickPending;
            AutoDepthDesignation.LogExperimentalAccessDebug(
                $"[ATD Prop Removal] operation={operation.OperationId} "
                + $"origin={operation.Origin} temporary-preview=missing "
                + "action=retry");
        }

        private bool RestoreOriginal(Operation operation, bool removeOwnedTemporary,
            out bool restored)
        {
            restored = operation.Original == null;
            Option<TerrainDesignation> live =
                m_designations.GetDesignationAt(operation.Origin);
            bool ownsLive = operation.HasTemporaryDesignation
                && live.HasValue
                && live.Value.Prototype == operation.TemporaryProto
                && live.Value.Data.Equals(operation.TemporaryData);
            if (removeOwnedTemporary && ownsLive)
            {
                RemoveDesignation(operation.Origin);
                operation.HasTemporaryDesignation = false;
                operation.TemporaryProto = null;
                operation.OriginalSuspendedByManager =
                    operation.Original != null;
                live = Option<TerrainDesignation>.None;
            }
            else if (operation.HasTemporaryDesignation)
                return false;

            if (live.HasValue)
            {
                if (operation.Original == null)
                    return false;
                restored = live.Value.Prototype.Id.Value == operation.Original.ProtoId
                    && live.Value.Data.Equals(operation.Original.Data);
                if (restored)
                    operation.OriginalSuspendedByManager = false;
                return restored;
            }

            if (operation.Original == null)
                return true;
            if (!m_protosDb.TryGetProto(new Proto.ID(operation.Original.ProtoId),
                    out TerrainDesignationProto proto))
                return false;
            restored = AddOrReplaceDesignation(operation.Original.Data, proto);
            if (restored)
                operation.OriginalSuspendedByManager = false;
            return restored;
        }

        private bool OwnsLiveTemporaryDesignation(Operation operation)
        {
            Option<TerrainDesignation> live =
                m_designations.GetDesignationAt(operation.Origin);
            return live.HasValue
                && live.Value.Prototype == operation.TemporaryProto
                && live.Value.Data.Equals(operation.TemporaryData);
        }

        private void FinishOperation(Operation operation,
            ATDPropRemovalOutcome outcome, bool restoreOriginal)
        {
            bool restored = !restoreOriginal;
            if (restoreOriginal
                && !RestoreOriginal(operation, removeOwnedTemporary: true,
                    out restored)
                && outcome == ATDPropRemovalOutcome.Removed)
                outcome = ATDPropRemovalOutcome.OriginalDesignationRestoreFailed;
            operation.Stage = ATDPropRemovalStage.Completed;
            AutoDepthDesignation.LogExperimentalAccessDebug(
                $"[ATD Prop Removal] operation={operation.OperationId} " +
                $"origin={operation.Origin} outcome={outcome} " +
                $"targets={operation.TargetProps.Count} handles={operation.Handles.Count} " +
                $"originalRestored={restored}");
            RemoveOperationMappings(operation);
            CompleteOperationHandles(operation, outcome, restored);
        }

        private void RemoveOperationMappings(Operation operation)
        {
            foreach (TerrainPropId propId in operation.TargetProps.ToArray())
                if (m_operations.TryGetValue(propId, out Operation mapped)
                    && ReferenceEquals(mapped, operation))
                    m_operations.Remove(propId);
            if (m_operationsByOrigin.TryGetValue(operation.Origin,
                    out Operation mappedOrigin)
                && ReferenceEquals(mappedOrigin, operation))
                m_operationsByOrigin.Remove(operation.Origin);
        }

        private void CompleteOperationHandles(Operation operation,
            ATDPropRemovalOutcome outcome, bool originalRestored)
        {
            foreach (ATDPropRemovalRequestHandle handle in operation.Handles.ToArray())
                CompleteHandle(handle, outcome, originalRestored);
            operation.Handles.Clear();
        }

        private void CompleteHandle(ATDPropRemovalRequestHandle handle,
            ATDPropRemovalOutcome outcome, bool originalRestored)
        {
            var result = new ATDPropRemovalResult(handle.RequestId, handle.PropId,
                handle.Origin, outcome, originalRestored, handle.OwnerToken);
            handle.Complete(result);
            m_pendingPublications.Add(result);
        }

        private void PublishPendingResults()
        {
            if (m_pendingPublications.Count == 0)
                return;
            ATDPropRemovalResult[] results = m_pendingPublications.ToArray();
            m_pendingPublications.Clear();
            Action<ATDPropRemovalResult>? subscribers = PropRemovalCompleted;
            if (subscribers == null)
                return;
            foreach (ATDPropRemovalResult result in results)
            {
                foreach (Action<ATDPropRemovalResult> subscriber in
                    subscribers.GetInvocationList())
                {
                    try { subscriber(result); }
                    catch (Exception ex)
                    {
                        AutoDepthDesignation.s_log.Exception(ex,
                            "ATD prop-removal completion subscriber");
                    }
                }
            }
        }

        internal void AppendPendingRequestsJson(StringBuilder sb)
        {
            sb.Append(",\"pendingPropRemovals\":[");
            bool first = true;
            foreach (Operation operation in m_operationsByOrigin.Values
                .OrderBy(item => item.OperationId))
            {
                foreach (TerrainPropId propId in operation.TargetProps
                    .OrderBy(item => item.Position.X)
                    .ThenBy(item => item.Position.Y))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"operationId\":").Append(operation.OperationId)
                    .Append(",\"propX\":").Append(propId.Position.X)
                    .Append(",\"propY\":").Append(propId.Position.Y)
                    .Append(",\"originX\":").Append(operation.Origin.X)
                    .Append(",\"originY\":").Append(operation.Origin.Y)
                    .Append(",\"quickRemove\":")
                    .Append(operation.QuickTargets.Contains(propId)
                        ? "true" : "false")
                    .Append(",\"hasOriginal\":")
                    .Append(operation.Original != null ? "true" : "false");
                if (operation.Original != null)
                {
                    DesignationData data = operation.Original.Data;
                    sb.Append(",\"protoId\":\"")
                        .Append(EscapeJson(operation.Original.ProtoId)).Append('"')
                        .Append(",\"nw\":").Append(data.OriginTargetHeight.Value)
                        .Append(",\"ne\":").Append(data.PlusXTargetHeight.Value)
                        .Append(",\"se\":").Append(data.PlusXyTargetHeight.Value)
                        .Append(",\"sw\":").Append(data.PlusYTargetHeight.Value);
                }
                    sb.Append('}');
                }
            }
            sb.Append(']');
        }

        internal void RestorePendingRequestsFromJsonEntries(object[] entries)
        {
            m_operations.Clear();
            m_operationsByOrigin.Clear();
            m_roundRobinCursor = 0;
            var operationsById = new Dictionary<int, Operation>();
            foreach (object raw in entries)
            {
                if (!(raw is Dict<string, object> entry)
                    || !TryInt(entry, "operationId", out int operationId)
                    || !TryInt(entry, "propX", out int propX)
                    || !TryInt(entry, "propY", out int propY)
                    || !TryInt(entry, "originX", out int originX)
                    || !TryInt(entry, "originY", out int originY))
                    continue;
                var propId = new TerrainPropId(propX, propY);
                SavedDesignation? original = null;
                if (TryBool(entry, "hasOriginal", out bool hasOriginal)
                    && hasOriginal
                    && TryString(entry, "protoId", out string protoId)
                    && TryInt(entry, "nw", out int nw)
                    && TryInt(entry, "ne", out int ne)
                    && TryInt(entry, "se", out int se)
                    && TryInt(entry, "sw", out int sw))
                {
                    var origin = new Tile2i(originX, originY);
                    original = new SavedDesignation(protoId,
                        new DesignationData(origin, new HeightTilesI(nw),
                            new HeightTilesI(ne), new HeightTilesI(se),
                            new HeightTilesI(sw)));
                }
                m_nextRequestId = Math.Max(m_nextRequestId, operationId);
                if (!operationsById.TryGetValue(operationId, out Operation operation))
                {
                    operation = new Operation
                    {
                        OperationId = operationId,
                        PropId = propId,
                        Origin = new Tile2i(originX, originY),
                        Original = original,
                        Stage = ATDPropRemovalStage.SuspendedForSave,
                        KeepAliveWithoutHandles = true,
                    };
                    operationsById.Add(operationId, operation);
                    m_operationsByOrigin[operation.Origin] = operation;
                }
                operation.TargetProps.Add(propId);
                if (TryBool(entry, "quickRemove", out bool quickRemove)
                    && quickRemove)
                    operation.QuickTargets.Add(propId);
                m_operations[propId] = operation;
            }
        }

        private static string EscapeJson(string value) => (value ?? string.Empty)
            .Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static bool TryInt(Dict<string, object> dict, string key,
            out int value)
        {
            value = 0;
            if (!dict.TryGetValue(key, out object raw)) return false;
            if (raw is int integer) { value = integer; return true; }
            if (raw is double number)
            {
                value = (int)number;
                return Math.Abs(number - value) < 0.0001d;
            }
            return false;
        }

        private static bool TryBool(Dict<string, object> dict, string key,
            out bool value)
        {
            value = false;
            if (dict.TryGetValue(key, out object raw) && raw is bool boolean)
            { value = boolean; return true; }
            return false;
        }

        private static bool TryString(Dict<string, object> dict, string key,
            out string value)
        {
            value = string.Empty;
            if (dict.TryGetValue(key, out object raw) && raw is string text)
            { value = text; return true; }
            return false;
        }
    }
}

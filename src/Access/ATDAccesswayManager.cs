using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AutoTerrainDesignations.Access
{
    internal enum ATDAccesswayRequestKind
    {
        FarmingPreparation,
        FarmingFilling,
        CreateDesignations,
        PlannedTower,
        ConstructionLeveling
    }

    internal enum ATDAccesswayPriority
    {
        Maintenance = 0,
        Derived = 1,
        Interactive = 2
    }

    internal enum ATDAccesswayRequestState
    {
        Queued,
        Active,
        Succeeded,
        Failed,
        Stale,
        Cancelled,
        Superseded
    }

    internal enum ATDAccesswayValidationDisposition
    {
        Current,
        Stale,
        OwnerGone
    }

    internal readonly struct ATDAccesswayValidationResult
    {
        public ATDAccesswayValidationDisposition Disposition { get; }
        public string Reason { get; }

        private ATDAccesswayValidationResult(
            ATDAccesswayValidationDisposition disposition,
            string reason)
        {
            Disposition = disposition;
            Reason = reason ?? string.Empty;
        }

        public static ATDAccesswayValidationResult Current()
            => new ATDAccesswayValidationResult(
                ATDAccesswayValidationDisposition.Current,
                string.Empty);

        public static ATDAccesswayValidationResult Stale(string reason)
            => new ATDAccesswayValidationResult(
                ATDAccesswayValidationDisposition.Stale,
                reason);

        public static ATDAccesswayValidationResult OwnerGone(string reason)
            => new ATDAccesswayValidationResult(
                ATDAccesswayValidationDisposition.OwnerGone,
                reason);
    }

    internal sealed class ATDAccesswayRequestResult
    {
        public ATDAccesswayRequestState State { get; }
        public string Reason { get; }
        public object? Payload { get; }
        public bool RetryEligible { get; }

        private ATDAccesswayRequestResult(
            ATDAccesswayRequestState state,
            string reason,
            object? payload,
            bool retryEligible)
        {
            State = state;
            Reason = reason ?? string.Empty;
            Payload = payload;
            RetryEligible = retryEligible;
        }

        public static ATDAccesswayRequestResult Succeeded(object? payload = null)
            => new ATDAccesswayRequestResult(
                ATDAccesswayRequestState.Succeeded,
                string.Empty,
                payload,
                retryEligible: false);

        public static ATDAccesswayRequestResult Failed(
            string reason,
            object? payload = null,
            bool retryEligible = true)
            => new ATDAccesswayRequestResult(
                ATDAccesswayRequestState.Failed,
                reason,
                payload,
                retryEligible);

        public static ATDAccesswayRequestResult Stale(string reason)
            => new ATDAccesswayRequestResult(
                ATDAccesswayRequestState.Stale,
                reason,
                null,
                retryEligible: true);

        public static ATDAccesswayRequestResult Cancelled(string reason)
            => new ATDAccesswayRequestResult(
                ATDAccesswayRequestState.Cancelled,
                reason,
                null,
                retryEligible: false);

        public static ATDAccesswayRequestResult Superseded()
            => new ATDAccesswayRequestResult(
                ATDAccesswayRequestState.Superseded,
                "Superseded",
                null,
                retryEligible: false);
    }

    internal interface IATDAccesswayManagedWork : IDisposable
    {
        bool Advance();
        void RequestCancellation(string reason);
        ATDAccesswayRequestResult GetTerminalResult();
        string Phase { get; }
        int VisitedNodes { get; }
        int PendingNodes { get; }
        double ProcessingMilliseconds { get; }
    }

    internal sealed class ATDAccesswayRequest
    {
        public string OwnerKey { get; }
        public string WorkFingerprint { get; }
        public ATDAccesswayRequestKind Kind { get; }
        public ATDAccesswayPriority Priority { get; }
        public Func<IATDAccesswayManagedWork> WorkFactory { get; }
        public Func<ATDAccesswayValidationResult>? Validation { get; }

        public ATDAccesswayRequest(
            string ownerKey,
            string workFingerprint,
            ATDAccesswayRequestKind kind,
            ATDAccesswayPriority priority,
            Func<IATDAccesswayManagedWork> workFactory,
            Func<ATDAccesswayValidationResult>? validation = null)
        {
            OwnerKey = string.IsNullOrWhiteSpace(ownerKey)
                ? throw new ArgumentException("Owner key is required.", nameof(ownerKey))
                : ownerKey;
            WorkFingerprint = workFingerprint ?? string.Empty;
            Kind = kind;
            Priority = priority;
            WorkFactory = workFactory
                ?? throw new ArgumentNullException(nameof(workFactory));
            Validation = validation;
        }
    }

    internal sealed class ATDAccesswayRequestHandle
    {
        internal ATDAccesswayRequestHandle(long requestId, ATDAccesswayRequest request)
        {
            RequestId = requestId;
            OwnerKey = request.OwnerKey;
            WorkFingerprint = request.WorkFingerprint;
            Kind = request.Kind;
            Priority = request.Priority;
            State = ATDAccesswayRequestState.Queued;
        }

        public long RequestId { get; }
        public string OwnerKey { get; }
        public string WorkFingerprint { get; }
        public ATDAccesswayRequestKind Kind { get; }
        public ATDAccesswayPriority Priority { get; }
        internal ATDAccesswayRequestState State { get; set; }
        internal ATDAccesswayRequestResult? Result { get; set; }
        internal IATDAccesswayManagedWork? Work { get; set; }
        internal int LastVisitedNodes { get; set; }
        internal int LastPendingNodes { get; set; }
        internal double LastProcessingMilliseconds { get; set; }
        internal string LastPhase { get; set; } = "Queued";
    }

    internal readonly struct ATDAccesswayHandleSnapshot
    {
        public ATDAccesswayRequestState State { get; }
        public ATDAccesswayRequestResult? Result { get; }
        public string Phase { get; }
        public int VisitedNodes { get; }
        public int PendingNodes { get; }
        public double ProcessingMilliseconds { get; }

        public bool IsTerminal
            => State == ATDAccesswayRequestState.Succeeded
                || State == ATDAccesswayRequestState.Failed
                || State == ATDAccesswayRequestState.Stale
                || State == ATDAccesswayRequestState.Cancelled
                || State == ATDAccesswayRequestState.Superseded;

        public ATDAccesswayHandleSnapshot(
            ATDAccesswayRequestState state,
            ATDAccesswayRequestResult? result,
            int visitedNodes,
            int pendingNodes,
            double processingMilliseconds,
            string phase = "Preparing")
        {
            State = state;
            Result = result;
            Phase = string.IsNullOrWhiteSpace(phase) ? "Preparing" : phase;
            VisitedNodes = visitedNodes;
            PendingNodes = pendingNodes;
            ProcessingMilliseconds = processingMilliseconds;
        }
    }

    internal readonly struct ATDAccesswayManagerHealthSnapshot
    {
        public int QueueDepth { get; }
        public long ActiveRequestId { get; }
        public double ActiveWallSeconds { get; }
        public double ActiveProcessingMilliseconds { get; }
        public int ActiveVisitedNodes { get; }
        public int ActivePendingNodes { get; }
        public double OldestQueueAgeSeconds { get; }
        public long CoalescedRequests { get; }
        public long SupersededRequests { get; }
        public long StaleRequests { get; }
        public long DroppedRequests { get; }
        public long CompletedRequests { get; }

        public ATDAccesswayManagerHealthSnapshot(
            int queueDepth,
            long activeRequestId,
            double activeWallSeconds,
            double activeProcessingMilliseconds,
            int activeVisitedNodes,
            int activePendingNodes,
            double oldestQueueAgeSeconds,
            long coalescedRequests,
            long supersededRequests,
            long staleRequests,
            long droppedRequests,
            long completedRequests)
        {
            QueueDepth = queueDepth;
            ActiveRequestId = activeRequestId;
            ActiveWallSeconds = activeWallSeconds;
            ActiveProcessingMilliseconds = activeProcessingMilliseconds;
            ActiveVisitedNodes = activeVisitedNodes;
            ActivePendingNodes = activePendingNodes;
            OldestQueueAgeSeconds = oldestQueueAgeSeconds;
            CoalescedRequests = coalescedRequests;
            SupersededRequests = supersededRequests;
            StaleRequests = staleRequests;
            DroppedRequests = droppedRequests;
            CompletedRequests = completedRequests;
        }
    }

    internal readonly struct ATDAccesswayTerminalDiagnostic
    {
        public long RequestId { get; }
        public string OwnerKey { get; }
        public string WorkFingerprint { get; }
        public ATDAccesswayRequestKind Kind { get; }
        public ATDAccesswayPriority Priority { get; }
        public ATDAccesswayRequestState PreviousState { get; }
        public ATDAccesswayRequestState State { get; }
        public string Reason { get; }
        public bool RetryEligible { get; }
        public double QueueAgeSeconds { get; }
        public double ActiveWallSeconds { get; }
        public double ProcessingMilliseconds { get; }
        public int VisitedNodes { get; }
        public int PendingNodes { get; }

        public ATDAccesswayTerminalDiagnostic(
            ATDAccesswayRequestHandle handle,
            ATDAccesswayRequestState previousState,
            ATDAccesswayRequestResult result,
            double queueAgeSeconds,
            double activeWallSeconds)
        {
            RequestId = handle.RequestId;
            OwnerKey = handle.OwnerKey;
            WorkFingerprint = handle.WorkFingerprint;
            Kind = handle.Kind;
            Priority = handle.Priority;
            PreviousState = previousState;
            State = result.State;
            Reason = result.Reason;
            RetryEligible = result.RetryEligible;
            QueueAgeSeconds = Math.Max(0d, queueAgeSeconds);
            ActiveWallSeconds = Math.Max(0d, activeWallSeconds);
            ProcessingMilliseconds = handle.LastProcessingMilliseconds;
            VisitedNodes = handle.LastVisitedNodes;
            PendingNodes = handle.LastPendingNodes;
        }
    }

    /// <summary>
    /// Runtime-only coordinator for access requests. Exactly one request may be active.
    /// </summary>
    internal sealed class ATDAccesswayManager
    {
        private sealed class Entry
        {
            public ATDAccesswayRequest Request { get; }
            public ATDAccesswayRequestHandle Handle { get; }
            public long Sequence { get; }
            public double EnqueuedAtSeconds { get; }
            public double ActivatedAtSeconds { get; set; }

            public Entry(
                ATDAccesswayRequest request,
                ATDAccesswayRequestHandle handle,
                long sequence,
                double enqueuedAtSeconds)
            {
                Request = request;
                Handle = handle;
                Sequence = sequence;
                EnqueuedAtSeconds = enqueuedAtSeconds;
            }
        }

        private readonly object m_sync = new object();
        private readonly List<Entry> m_queue = new List<Entry>();
        private readonly Dictionary<string, Entry> m_liveByOwner =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly int m_maxPendingRequests;
        private readonly Func<double> m_realtimeSeconds;
        private readonly Action<ATDAccesswayTerminalDiagnostic>?
            m_terminalObserver;
        private long m_nextRequestId;
        private long m_nextSequence;
        private Entry? m_active;
        private long m_coalescedRequests;
        private long m_supersededRequests;
        private long m_staleRequests;
        private long m_droppedRequests;
        private long m_completedRequests;

        public ATDAccesswayManager(
            int maxPendingRequests = 32,
            Func<double>? realtimeSeconds = null,
            Action<ATDAccesswayTerminalDiagnostic>? terminalObserver = null)
        {
            m_maxPendingRequests = Math.Max(1, maxPendingRequests);
            m_realtimeSeconds = realtimeSeconds
                ?? (() => Stopwatch.GetTimestamp()
                    / (double)Stopwatch.Frequency);
            m_terminalObserver = terminalObserver;
        }

        public ATDAccesswayRequestHandle Enqueue(ATDAccesswayRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            lock (m_sync)
            {
                if (m_liveByOwner.TryGetValue(request.OwnerKey, out Entry existing))
                {
                    if (string.Equals(
                        existing.Request.WorkFingerprint,
                        request.WorkFingerprint,
                        StringComparison.Ordinal))
                    {
                        m_coalescedRequests++;
                        return existing.Handle;
                    }
                    Supersede(existing);
                }

                var handle = new ATDAccesswayRequestHandle(
                    ++m_nextRequestId, request);
                double enqueuedAtSeconds = m_realtimeSeconds();
                var entry = new Entry(
                    request,
                    handle,
                    ++m_nextSequence,
                    enqueuedAtSeconds);
                if (!MakeQueueRoom(request.Priority))
                {
                    Complete(
                        entry,
                        ATDAccesswayRequestResult.Failed(
                            "QueueOverflow",
                            retryEligible: true));
                    m_droppedRequests++;
                    return handle;
                }
                m_queue.Add(entry);
                m_liveByOwner.Add(request.OwnerKey, entry);
                return handle;
            }
        }

        public ATDAccesswayHandleSnapshot Read(ATDAccesswayRequestHandle handle)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            lock (m_sync)
            {
                IATDAccesswayManagedWork? work = handle.Work;
                return new ATDAccesswayHandleSnapshot(
                    handle.State,
                    handle.Result,
                    work?.VisitedNodes ?? handle.LastVisitedNodes,
                    work?.PendingNodes ?? handle.LastPendingNodes,
                    work?.ProcessingMilliseconds
                        ?? handle.LastProcessingMilliseconds,
                    work?.Phase ?? handle.LastPhase);
            }
        }

        public bool TryReadActive(
            out ATDAccesswayRequestHandle? handle,
            out ATDAccesswayHandleSnapshot snapshot)
        {
            lock (m_sync)
            {
                if (m_active == null)
                {
                    handle = null;
                    snapshot = default;
                    return false;
                }
                handle = m_active.Handle;
                IATDAccesswayManagedWork? work = handle.Work;
                snapshot = new ATDAccesswayHandleSnapshot(
                    handle.State,
                    handle.Result,
                    work?.VisitedNodes ?? handle.LastVisitedNodes,
                    work?.PendingNodes ?? handle.LastPendingNodes,
                    work?.ProcessingMilliseconds
                        ?? handle.LastProcessingMilliseconds,
                    work?.Phase ?? handle.LastPhase);
                return true;
            }
        }

        public ATDAccesswayManagerHealthSnapshot ReadHealth()
        {
            lock (m_sync)
            {
                double now = m_realtimeSeconds();
                double oldestQueueAge = 0d;
                foreach (Entry entry in m_queue)
                    oldestQueueAge = Math.Max(
                        oldestQueueAge,
                        now - entry.EnqueuedAtSeconds);
                IATDAccesswayManagedWork? activeWork =
                    m_active?.Handle.Work;
                return new ATDAccesswayManagerHealthSnapshot(
                    m_queue.Count,
                    m_active?.Handle.RequestId ?? 0L,
                    m_active == null
                        ? 0d
                        : Math.Max(
                            0d,
                            now - m_active.ActivatedAtSeconds),
                    activeWork?.ProcessingMilliseconds ?? 0d,
                    activeWork?.VisitedNodes ?? 0,
                    activeWork?.PendingNodes ?? 0,
                    Math.Max(0d, oldestQueueAge),
                    m_coalescedRequests,
                    m_supersededRequests,
                    m_staleRequests,
                    m_droppedRequests,
                    m_completedRequests);
            }
        }

        public bool Tick(bool suspended)
        {
            if (suspended) return false;
            lock (m_sync)
            {
                bool managerWorkPerformed = false;
                if (m_active == null)
                    managerWorkPerformed = ActivateNext();
                if (m_active == null)
                    return managerWorkPerformed;

                Entry active = m_active;
                if (!TryValidate(active, out ATDAccesswayRequestResult? invalid))
                {
                    Complete(active, invalid!);
                    return true;
                }
                try
                {
                    if (active.Handle.Work!.Advance())
                        return true;
                    Complete(active, active.Handle.Work.GetTerminalResult());
                }
                catch (Exception ex)
                {
                    Complete(
                        active,
                        ATDAccesswayRequestResult.Failed(
                            "UnhandledWorkException:" + ex.GetType().Name));
                }
                return true;
            }
        }

        public void Cancel(ATDAccesswayRequestHandle handle, string reason)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            lock (m_sync)
            {
                Entry? entry = FindLiveEntry(handle);
                if (entry == null) return;
                if (ReferenceEquals(m_active, entry)
                    && entry.Handle.Work != null)
                {
                    entry.Handle.Work.RequestCancellation(reason);
                    return;
                }
                Complete(entry, ATDAccesswayRequestResult.Cancelled(reason));
            }
        }

        public void Reset(string reason)
        {
            lock (m_sync)
            {
                foreach (Entry entry in m_queue.ToArray())
                    Complete(entry, ATDAccesswayRequestResult.Cancelled(reason));
                if (m_active != null)
                    Complete(
                        m_active,
                        ATDAccesswayRequestResult.Cancelled(reason));
                m_queue.Clear();
                m_liveByOwner.Clear();
                m_active = null;
            }
        }

        private bool ActivateNext()
        {
            bool managerWorkPerformed = false;
            while (m_queue.Count > 0)
            {
                int bestIndex = 0;
                for (int index = 1; index < m_queue.Count; index++)
                {
                    Entry candidate = m_queue[index];
                    Entry best = m_queue[bestIndex];
                    if (candidate.Request.Priority > best.Request.Priority
                        || (candidate.Request.Priority == best.Request.Priority
                            && candidate.Sequence < best.Sequence))
                        bestIndex = index;
                }
                Entry entry = m_queue[bestIndex];
                managerWorkPerformed = true;
                if (!TryValidate(
                        entry,
                        out ATDAccesswayRequestResult? invalid))
                {
                    Complete(entry, invalid!);
                    continue;
                }
                m_queue.RemoveAt(bestIndex);
                try
                {
                    entry.Handle.Work = entry.Request.WorkFactory();
                }
                catch (Exception ex)
                {
                    Complete(
                        entry,
                        ATDAccesswayRequestResult.Failed(
                            "WorkFactoryException:"
                                + ex.GetType().Name));
                    continue;
                }
                entry.ActivatedAtSeconds = m_realtimeSeconds();
                entry.Handle.State = ATDAccesswayRequestState.Active;
                m_active = entry;
                return true;
            }
            return managerWorkPerformed;
        }

        private void Supersede(Entry entry)
        {
            ATDAccesswayRequestState previousState = entry.Handle.State;
            entry.Handle.Work?.RequestCancellation("Superseded");
            if (ReferenceEquals(m_active, entry))
                m_active = null;
            else
                m_queue.Remove(entry);
            CaptureTerminalWork(entry.Handle);
            entry.Handle.Work?.Dispose();
            entry.Handle.Work = null;
            entry.Handle.State = ATDAccesswayRequestState.Superseded;
            entry.Handle.Result = ATDAccesswayRequestResult.Superseded();
            m_liveByOwner.Remove(entry.Request.OwnerKey);
            NotifyTerminal(
                entry,
                previousState,
                entry.Handle.Result);
            m_supersededRequests++;
            m_completedRequests++;
        }

        private void Complete(Entry entry, ATDAccesswayRequestResult result)
        {
            ATDAccesswayRequestState previousState = entry.Handle.State;
            if (ReferenceEquals(m_active, entry))
                m_active = null;
            else
                m_queue.Remove(entry);
            CaptureTerminalWork(entry.Handle);
            entry.Handle.Work?.Dispose();
            entry.Handle.Work = null;
            entry.Handle.Result = result;
            entry.Handle.State = result.State;
            m_liveByOwner.Remove(entry.Request.OwnerKey);
            NotifyTerminal(entry, previousState, result);
            if (result.State == ATDAccesswayRequestState.Stale)
                m_staleRequests++;
            m_completedRequests++;
        }

        private static void CaptureTerminalWork(
            ATDAccesswayRequestHandle handle)
        {
            if (handle.Work == null)
                return;
            handle.LastVisitedNodes = handle.Work.VisitedNodes;
            handle.LastPendingNodes = handle.Work.PendingNodes;
            handle.LastProcessingMilliseconds =
                handle.Work.ProcessingMilliseconds;
            handle.LastPhase = handle.Work.Phase;
        }

        private void NotifyTerminal(
            Entry entry,
            ATDAccesswayRequestState previousState,
            ATDAccesswayRequestResult result)
        {
            if (m_terminalObserver == null)
                return;
            double now = m_realtimeSeconds();
            double queueEnd = entry.ActivatedAtSeconds > 0d
                ? entry.ActivatedAtSeconds
                : now;
            var diagnostic = new ATDAccesswayTerminalDiagnostic(
                entry.Handle,
                previousState,
                result,
                queueEnd - entry.EnqueuedAtSeconds,
                entry.ActivatedAtSeconds > 0d
                    ? now - entry.ActivatedAtSeconds
                    : 0d);
            try
            {
                m_terminalObserver(diagnostic);
            }
            catch
            {
                // Diagnostics must never disrupt request completion.
            }
        }

        private bool MakeQueueRoom(ATDAccesswayPriority incomingPriority)
        {
            if (m_queue.Count < m_maxPendingRequests)
                return true;

            int dropIndex = -1;
            for (int index = 0; index < m_queue.Count; index++)
            {
                Entry candidate = m_queue[index];
                if (candidate.Request.Priority > incomingPriority)
                    continue;
                if (dropIndex < 0
                    || candidate.Request.Priority
                        < m_queue[dropIndex].Request.Priority
                    || (candidate.Request.Priority
                            == m_queue[dropIndex].Request.Priority
                        && candidate.Sequence
                            < m_queue[dropIndex].Sequence))
                    dropIndex = index;
            }
            if (dropIndex < 0)
                return false;

            Entry dropped = m_queue[dropIndex];
            Complete(
                dropped,
                ATDAccesswayRequestResult.Failed(
                    "QueueOverflow",
                    retryEligible: true));
            m_droppedRequests++;
            return true;
        }

        private static bool TryValidate(
            Entry entry,
            out ATDAccesswayRequestResult? invalidResult)
        {
            invalidResult = null;
            Func<ATDAccesswayValidationResult>? validation =
                entry.Request.Validation;
            if (validation == null)
                return true;

            ATDAccesswayValidationResult validationResult;
            try
            {
                validationResult = validation();
            }
            catch (Exception ex)
            {
                invalidResult = ATDAccesswayRequestResult.Failed(
                    "ValidationException:" + ex.GetType().Name,
                    retryEligible: true);
                return false;
            }

            switch (validationResult.Disposition)
            {
                case ATDAccesswayValidationDisposition.Current:
                    return true;
                case ATDAccesswayValidationDisposition.Stale:
                    invalidResult = ATDAccesswayRequestResult.Stale(
                        string.IsNullOrEmpty(validationResult.Reason)
                            ? "LiveInputChanged"
                            : validationResult.Reason);
                    return false;
                default:
                    invalidResult = ATDAccesswayRequestResult.Cancelled(
                        string.IsNullOrEmpty(validationResult.Reason)
                            ? "OwnerGone"
                            : validationResult.Reason);
                    return false;
            }
        }

        private Entry? FindLiveEntry(ATDAccesswayRequestHandle handle)
        {
            if (!m_liveByOwner.TryGetValue(handle.OwnerKey, out Entry entry))
                return null;
            return ReferenceEquals(entry.Handle, handle) ? entry : null;
        }
    }
}

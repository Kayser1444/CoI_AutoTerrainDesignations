using System;
using System.Collections.Generic;

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
        Cancelled,
        Superseded
    }

    internal sealed class ATDAccesswayRequestResult
    {
        public ATDAccesswayRequestState State { get; }
        public string Reason { get; }
        public object? Payload { get; }

        private ATDAccesswayRequestResult(
            ATDAccesswayRequestState state,
            string reason,
            object? payload)
        {
            State = state;
            Reason = reason ?? string.Empty;
            Payload = payload;
        }

        public static ATDAccesswayRequestResult Succeeded(object? payload = null)
            => new ATDAccesswayRequestResult(
                ATDAccesswayRequestState.Succeeded, string.Empty, payload);

        public static ATDAccesswayRequestResult Failed(
            string reason,
            object? payload = null)
            => new ATDAccesswayRequestResult(
                ATDAccesswayRequestState.Failed, reason, payload);

        public static ATDAccesswayRequestResult Cancelled(string reason)
            => new ATDAccesswayRequestResult(
                ATDAccesswayRequestState.Cancelled, reason, null);
    }

    internal interface IATDAccesswayManagedWork : IDisposable
    {
        bool Advance();
        void RequestCancellation(string reason);
        ATDAccesswayRequestResult GetTerminalResult();
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

        public ATDAccesswayRequest(
            string ownerKey,
            string workFingerprint,
            ATDAccesswayRequestKind kind,
            ATDAccesswayPriority priority,
            Func<IATDAccesswayManagedWork> workFactory)
        {
            OwnerKey = string.IsNullOrWhiteSpace(ownerKey)
                ? throw new ArgumentException("Owner key is required.", nameof(ownerKey))
                : ownerKey;
            WorkFingerprint = workFingerprint ?? string.Empty;
            Kind = kind;
            Priority = priority;
            WorkFactory = workFactory
                ?? throw new ArgumentNullException(nameof(workFactory));
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
    }

    internal readonly struct ATDAccesswayHandleSnapshot
    {
        public ATDAccesswayRequestState State { get; }
        public ATDAccesswayRequestResult? Result { get; }
        public int VisitedNodes { get; }
        public int PendingNodes { get; }
        public double ProcessingMilliseconds { get; }

        public bool IsTerminal
            => State == ATDAccesswayRequestState.Succeeded
                || State == ATDAccesswayRequestState.Failed
                || State == ATDAccesswayRequestState.Cancelled
                || State == ATDAccesswayRequestState.Superseded;

        public ATDAccesswayHandleSnapshot(
            ATDAccesswayRequestState state,
            ATDAccesswayRequestResult? result,
            int visitedNodes,
            int pendingNodes,
            double processingMilliseconds)
        {
            State = state;
            Result = result;
            VisitedNodes = visitedNodes;
            PendingNodes = pendingNodes;
            ProcessingMilliseconds = processingMilliseconds;
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

            public Entry(
                ATDAccesswayRequest request,
                ATDAccesswayRequestHandle handle,
                long sequence)
            {
                Request = request;
                Handle = handle;
                Sequence = sequence;
            }
        }

        private readonly object m_sync = new object();
        private readonly List<Entry> m_queue = new List<Entry>();
        private readonly Dictionary<string, Entry> m_liveByOwner =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private long m_nextRequestId;
        private long m_nextSequence;
        private Entry? m_active;

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
                        return existing.Handle;
                    Supersede(existing);
                }

                var handle = new ATDAccesswayRequestHandle(
                    ++m_nextRequestId, request);
                var entry = new Entry(request, handle, ++m_nextSequence);
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
                        ?? handle.LastProcessingMilliseconds);
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
                        ?? handle.LastProcessingMilliseconds);
                return true;
            }
        }

        public void Tick(bool suspended)
        {
            if (suspended) return;
            lock (m_sync)
            {
                if (m_active == null)
                    ActivateNext();
                if (m_active == null)
                    return;

                Entry active = m_active;
                try
                {
                    if (active.Handle.Work!.Advance())
                        return;
                    Complete(active, active.Handle.Work.GetTerminalResult());
                }
                catch (Exception ex)
                {
                    Complete(
                        active,
                        ATDAccesswayRequestResult.Failed(
                            "UnhandledWorkException:" + ex.GetType().Name));
                }
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

        private void ActivateNext()
        {
            if (m_queue.Count == 0) return;
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
            m_queue.RemoveAt(bestIndex);
            entry.Handle.Work = entry.Request.WorkFactory();
            entry.Handle.State = ATDAccesswayRequestState.Active;
            m_active = entry;
        }

        private void Supersede(Entry entry)
        {
            entry.Handle.Work?.RequestCancellation("Superseded");
            if (ReferenceEquals(m_active, entry))
                m_active = null;
            else
                m_queue.Remove(entry);
            entry.Handle.Work?.Dispose();
            entry.Handle.Work = null;
            entry.Handle.State = ATDAccesswayRequestState.Superseded;
            entry.Handle.Result = ATDAccesswayRequestResult.Cancelled("Superseded");
            m_liveByOwner.Remove(entry.Request.OwnerKey);
        }

        private void Complete(Entry entry, ATDAccesswayRequestResult result)
        {
            if (ReferenceEquals(m_active, entry))
                m_active = null;
            else
                m_queue.Remove(entry);
            if (entry.Handle.Work != null)
            {
                entry.Handle.LastVisitedNodes =
                    entry.Handle.Work.VisitedNodes;
                entry.Handle.LastPendingNodes =
                    entry.Handle.Work.PendingNodes;
                entry.Handle.LastProcessingMilliseconds =
                    entry.Handle.Work.ProcessingMilliseconds;
                entry.Handle.Work.Dispose();
            }
            entry.Handle.Work = null;
            entry.Handle.Result = result;
            entry.Handle.State = result.State;
            m_liveByOwner.Remove(entry.Request.OwnerKey);
        }

        private Entry? FindLiveEntry(ATDAccesswayRequestHandle handle)
        {
            if (!m_liveByOwner.TryGetValue(handle.OwnerKey, out Entry entry))
                return null;
            return ReferenceEquals(entry.Handle, handle) ? entry : null;
        }
    }
}

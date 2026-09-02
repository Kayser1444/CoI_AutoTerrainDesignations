using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AutoTerrainDesignations.Access.V2;
using AutoTerrainDesignations.Mining;
using Mafi;

namespace AutoTerrainDesignations.Access.Worker
{
    internal sealed class AccessSearchWorkerJob
    {
        public long JobId { get; }
        public int WorldGeneration { get; }
        public string RequestId { get; }
        public AccessPathRequest Request { get; }
        public bool CaptureOverlay { get; }
        public MiningRequest? MiningRequest { get; }
        public MiningStage MiningStage { get; }

        public AccessSearchWorkerJob(long jobId, int worldGeneration,
            MiningRequest request, MiningStage stage)
        {
            JobId = jobId;
            WorldGeneration = worldGeneration;
            RequestId = "mining:" + jobId;
            Request = null!;
            MiningRequest = request ?? throw new ArgumentNullException(nameof(request));
            MiningStage = stage;
        }

        public AccessSearchWorkerJob(
            long jobId,
            int worldGeneration,
            AccessPathRequest request,
            bool captureOverlay = false)
        {
            JobId = jobId;
            WorldGeneration = worldGeneration;
            Request = request ?? throw new ArgumentNullException(nameof(request));
            RequestId = request.RequestId;
            CaptureOverlay = captureOverlay;
        }
    }

    internal readonly struct AccessSearchWorkerOverlaySample
    {
        public Tile2i Tile { get; }
        public int Height2 { get; }
        public bool IsGround { get; }
        public int? Priority { get; }

        public AccessSearchWorkerOverlaySample(
            Tile2i tile, int height2, bool isGround, int? priority)
        {
            Tile = tile;
            Height2 = height2;
            IsGround = isGround;
            Priority = priority;
        }
    }

    internal sealed class AccessSearchWorkerProgress
    {
        public long JobId { get; }
        public string Phase { get; }
        public string Subphase { get; }
        public int VisitedNodes { get; }
        public int PendingNodes { get; }
        public double ProcessingMilliseconds { get; }
        public bool CancellationRequested { get; }

        public AccessSearchWorkerProgress(
            long jobId,
            string phase,
            string subphase,
            int visitedNodes,
            int pendingNodes,
            double processingMilliseconds,
            bool cancellationRequested)
        {
            JobId = jobId;
            Phase = phase;
            Subphase = subphase;
            VisitedNodes = visitedNodes;
            PendingNodes = pendingNodes;
            ProcessingMilliseconds = processingMilliseconds;
            CancellationRequested = cancellationRequested;
        }
    }

    internal sealed class AccessSearchWorkerTerminal
    {
        public AccessSearchWorkerJob Job { get; }
        public AccessSearchExecutionOutcome? Outcome { get; }
        public string Fault { get; }
        public string Stack { get; }
        public long DroppedOverlaySamples { get; }
        public MiningPlan? MiningPlan { get; }
        public double MiningMilliseconds { get; }
        public bool IsFaulted => Outcome == null && MiningPlan == null;

        private AccessSearchWorkerTerminal(
            AccessSearchWorkerJob job,
            AccessSearchExecutionOutcome? outcome,
            string fault,
            string stack,
            long droppedOverlaySamples)
        {
            Job = job;
            Outcome = outcome;
            Fault = fault;
            Stack = stack;
            DroppedOverlaySamples = Math.Max(0L, droppedOverlaySamples);
        }

        internal static AccessSearchWorkerTerminal Completed(
            AccessSearchWorkerJob job,
            AccessSearchExecutionOutcome outcome,
            long droppedOverlaySamples = 0L)
            => new AccessSearchWorkerTerminal(
                job, outcome, string.Empty, string.Empty,
                droppedOverlaySamples);

        private AccessSearchWorkerTerminal(AccessSearchWorkerJob job, MiningPlan plan, double milliseconds)
        {
            Job = job;
            MiningPlan = plan;
            MiningMilliseconds = milliseconds;
            Fault = string.Empty;
            Stack = string.Empty;
        }

        internal static AccessSearchWorkerTerminal CompletedMining(
            AccessSearchWorkerJob job, MiningPlan plan, double milliseconds)
            => new AccessSearchWorkerTerminal(job, plan, milliseconds);

        internal static AccessSearchWorkerTerminal Faulted(
            AccessSearchWorkerJob job,
            Exception exception,
            long droppedOverlaySamples = 0L)
            => new AccessSearchWorkerTerminal(
                job,
                null,
                exception.GetType().Name + ": " + exception.Message,
                exception.StackTrace ?? string.Empty,
                droppedOverlaySamples);
    }

    /// <summary>
    /// One lazy process-lifetime worker with one job slot and one terminal
    /// slot. The game thread only submits and polls; it never waits.
    /// </summary>
    internal sealed class AccessSearchWorker
    {
        internal static AccessSearchWorker Shared { get; } =
            new AccessSearchWorker();

        private readonly object m_gate = new object();
        private readonly AutoResetEvent m_wake = new AutoResetEvent(false);
        private Thread? m_thread;
        private AccessSearchWorkerJob? m_pending;
        private AccessSearchWorkerJob? m_active;
        private AccessSearchWorkerTerminal? m_terminal;
        private AccessSearchWorkerProgress? m_progress;
        private WorkerControl? m_control;
        private long m_abandonedJobId;
        private readonly AccessSearchWorkerOverlayBuffer m_overlay =
            new AccessSearchWorkerOverlayBuffer(2048);
        private long m_overlayJobId;
        private int m_currentWorldGeneration;
        private bool m_restartUsed;
        private bool m_disabled;

        private AccessSearchWorker() { }

        // Logical cancellation can release the manager before computation has
        // stopped. Game-thread planning must wait for this physical boundary.
        internal bool HasRunningJob
        {
            get
            {
                lock (m_gate)
                    return m_pending != null || m_active != null;
            }
        }

        internal void SetCurrentWorld(int worldGeneration)
        {
            lock (m_gate)
            {
                m_currentWorldGeneration = worldGeneration;
                if (m_pending != null
                    && m_pending.WorldGeneration != worldGeneration)
                    m_pending = null;
                if (m_terminal != null
                    && m_terminal.Job.WorldGeneration != worldGeneration)
                    m_terminal = null;
                if (m_active != null
                    && m_active.WorldGeneration != worldGeneration)
                    m_control?.Cancel("WorldGenerationChanged");
            }
            m_wake.Set();
        }

        internal bool TrySubmit(
            AccessSearchWorkerJob job,
            out string failure)
        {
            lock (m_gate)
            {
                if (m_disabled)
                {
                    failure = "WorkerDisabled";
                    return false;
                }
                if (job.WorldGeneration != m_currentWorldGeneration)
                {
                    failure = "WorkerWorldMismatch";
                    return false;
                }
                if (m_terminal != null
                    && m_terminal.Job.WorldGeneration
                        != m_currentWorldGeneration)
                    m_terminal = null;
                if (m_pending != null || m_active != null || m_terminal != null)
                {
                    failure = "WorkerBusy";
                    return false;
                }
                if (!EnsureThreadLocked(out failure))
                    return false;
                m_pending = job;
                m_overlay.Reset();
                Interlocked.Exchange(ref m_overlayJobId, job.JobId);
                m_progress = new AccessSearchWorkerProgress(
                    job.JobId, "Queued for access search worker", string.Empty,
                    0, 0, 0d, false);
            }
            m_wake.Set();
            failure = string.Empty;
            return true;
        }

        internal bool TryReadProgress(
            long jobId,
            out AccessSearchWorkerProgress? progress)
        {
            lock (m_gate)
            {
                progress = m_progress?.JobId == jobId
                    ? m_progress
                    : null;
                return progress != null;
            }
        }

        internal bool TryConsumeTerminal(
            long jobId,
            int worldGeneration,
            out AccessSearchWorkerTerminal? terminal)
        {
            lock (m_gate)
            {
                if (m_terminal == null
                    || m_terminal.Job.JobId != jobId
                    || m_terminal.Job.WorldGeneration != worldGeneration)
                {
                    terminal = null;
                    return false;
                }
                terminal = m_terminal;
                m_terminal = null;
                m_progress = null;
                return true;
            }
        }

        internal int DrainOverlay(
            long jobId,
            List<AccessSearchWorkerOverlaySample> destination,
            int maxSamples)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            lock (m_gate)
            {
                if (Interlocked.Read(ref m_overlayJobId) != jobId) return 0;
            }
            return m_overlay.Drain(destination, maxSamples);
        }

        internal void Cancel(long jobId, string reason)
        {
            lock (m_gate)
            {
                if (m_terminal?.Job.JobId == jobId)
                {
                    AccessSearchWorkerJob completed = m_terminal.Job;
                    m_terminal = AccessSearchWorkerTerminal.Completed(
                        completed,
                        AccessSearchExecutionCore.CreateCancelled(
                            completed.Request,
                            "Cancellation won before terminal consumption"),
                        m_overlay.Dropped);
                    m_progress = new AccessSearchWorkerProgress(
                        jobId, "Stopping access search", reason,
                        0, 0, 0d, true);
                    return;
                }
                if (m_pending?.JobId == jobId)
                {
                    AccessSearchWorkerJob pending = m_pending;
                    m_pending = null;
                    m_terminal = AccessSearchWorkerTerminal.Completed(
                        pending,
                        AccessSearchExecutionCore.CreateCancelled(
                            pending.Request,
                            "Cancelled before worker claim"),
                        m_overlay.Dropped);
                    m_progress = new AccessSearchWorkerProgress(
                        jobId, "Stopping access search", reason,
                        0, 0, 0d, true);
                    return;
                }
                if (m_active?.JobId == jobId)
                    m_control?.Cancel(reason);
            }
            m_wake.Set();
        }

        internal void Abandon(long jobId, string reason)
        {
            lock (m_gate)
            {
                if (m_terminal?.Job.JobId == jobId)
                {
                    m_terminal = null;
                    if (m_progress?.JobId == jobId)
                        m_progress = null;
                    return;
                }
                if (m_pending?.JobId == jobId)
                {
                    m_pending = null;
                    if (m_progress?.JobId == jobId)
                        m_progress = null;
                    return;
                }
                if (m_active?.JobId == jobId)
                {
                    m_abandonedJobId = jobId;
                    m_control?.Cancel(reason);
                }
            }
            m_wake.Set();
        }

        private bool EnsureThreadLocked(out string failure)
        {
            if (m_thread?.IsAlive == true)
            {
                failure = string.Empty;
                return true;
            }
            if (m_thread != null)
            {
                if (m_restartUsed)
                {
                    m_disabled = true;
                    failure = "WorkerThreadRepeatedFailure";
                    return false;
                }
                m_restartUsed = true;
            }
            try
            {
                m_thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = "ATD Access Search Worker",
                    Priority = ThreadPriority.BelowNormal
                };
                m_thread.Start();
                failure = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                m_thread = null;
                failure = "WorkerStartFailed:" + ex.GetType().Name;
                return false;
            }
        }

        private void Run()
        {
            try
            {
                while (true)
                {
                    m_wake.WaitOne();
                    AccessSearchWorkerJob? job;
                    WorkerControl? control;
                    lock (m_gate)
                    {
                        job = m_pending;
                        if (job == null) continue;
                        m_pending = null;
                        m_active = job;
                        control = new WorkerControl(
                            this, job, Stopwatch.StartNew());
                        m_control = control;
                    }

                    AccessSearchWorkerTerminal terminal;
                    try
                    {
                        if (job.MiningRequest != null)
                        {
                            Stopwatch miningTimer = Stopwatch.StartNew();
                            MiningPlan mining = MiningPlanner.Execute(job.MiningRequest, job.MiningStage, control);
                            terminal = AccessSearchWorkerTerminal.CompletedMining(job, mining,
                                miningTimer.Elapsed.TotalMilliseconds);
                        }
                        else terminal = AccessSearchWorkerTerminal.Completed(
                            job,
                            AccessSearchExecutionCore.Execute(
                                job.Request, control),
                            m_overlay.Dropped);
                    }
                    catch (Exception ex)
                    {
                        terminal = AccessSearchWorkerTerminal.Faulted(
                            job, ex, m_overlay.Dropped);
                    }
                    lock (m_gate)
                    {
                        m_active = null;
                        m_control = null;
                        if (m_abandonedJobId == job.JobId)
                        {
                            m_abandonedJobId = 0L;
                            if (m_progress?.JobId == job.JobId)
                                m_progress = null;
                        }
                        else
                        {
                            m_terminal = terminal;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // A later submission may perform the one permitted clean
                // restart. The failed in-flight job is never replayed.
                lock (m_gate)
                {
                    if (m_active != null
                        && m_abandonedJobId == m_active.JobId)
                    {
                        m_abandonedJobId = 0L;
                        if (m_progress?.JobId == m_active.JobId)
                            m_progress = null;
                    }
                    else if (m_active != null && m_terminal == null)
                        m_terminal = AccessSearchWorkerTerminal.Faulted(
                            m_active, ex, m_overlay.Dropped);
                    m_active = null;
                    m_control = null;
                }
            }
        }

        private void Publish(
            AccessSearchWorkerJob job,
            Stopwatch timer,
            string phase,
            string subphase,
            int visited,
            int pending,
            bool cancellationRequested)
        {
            lock (m_gate)
            {
                if (m_active?.JobId != job.JobId) return;
                m_progress = new AccessSearchWorkerProgress(
                    job.JobId, phase, subphase, visited, pending,
                    timer.Elapsed.TotalMilliseconds,
                    cancellationRequested);
            }
        }

        private void PublishOverlay(
            AccessSearchWorkerJob job,
            AccessSearchWorkerOverlaySample sample)
        {
            if (!job.CaptureOverlay
                || Interlocked.Read(ref m_overlayJobId) != job.JobId)
                return;
            m_overlay.TryWrite(sample);
        }

        private sealed class WorkerControl : IAccessSearchExecutionControl
        {
            private readonly AccessSearchWorker m_owner;
            private readonly AccessSearchWorkerJob m_job;
            private readonly Stopwatch m_timer;
            private int m_cancelled;
            private string m_reason = string.Empty;
            private string m_lastPhase = string.Empty;
            private long m_lastPublishTimestamp;

            public bool CancellationRequested
                => Volatile.Read(ref m_cancelled) != 0;
            public bool CaptureOverlay => m_job.CaptureOverlay;
            public bool CaptureExpansionTrace => false;

            internal WorkerControl(
                AccessSearchWorker owner,
                AccessSearchWorkerJob job,
                Stopwatch timer)
            {
                m_owner = owner;
                m_job = job;
                m_timer = timer;
            }

            internal void Cancel(string reason)
            {
                m_reason = reason ?? string.Empty;
                Interlocked.Exchange(ref m_cancelled, 1);
                Publish("Stopping access search", m_reason, 0, 0);
            }

            public void Publish(
                string phase,
                string subphase,
                int visited,
                int pending)
            {
                long now = Stopwatch.GetTimestamp();
                bool phaseChanged = !string.Equals(
                    phase, m_lastPhase, StringComparison.Ordinal);
                long minimumTicks = Math.Max(
                    1L, Stopwatch.Frequency / 20L);
                if (!phaseChanged
                    && !CancellationRequested
                    && now - m_lastPublishTimestamp < minimumTicks)
                    return;
                m_lastPhase = phase;
                m_lastPublishTimestamp = now;
                m_owner.Publish(
                    m_job, m_timer, phase, subphase,
                    visited, pending, CancellationRequested);
            }

            public void RecordNode(
                Tile2i tile,
                int height2,
                bool isGround,
                int? priority)
                => m_owner.PublishOverlay(
                    m_job,
                    new AccessSearchWorkerOverlaySample(
                        tile, height2, isGround, priority));

            public void RecordExpansion(AccessV2ExpansionTrace expansion)
            {
            }

            public void RecordGroundExpansionOutcome(
                AccessV2GroundExpansionOutcomeTrace outcome)
            {
            }
        }

        private sealed class AccessSearchWorkerOverlayBuffer
        {
            private readonly AccessSearchWorkerOverlaySample[] m_samples;
            private long m_writeSequence;
            private long m_readSequence;
            private long m_dropped;

            internal AccessSearchWorkerOverlayBuffer(int capacity)
            {
                m_samples = new AccessSearchWorkerOverlaySample[
                    Math.Max(1, capacity)];
            }

            internal long Dropped => Volatile.Read(ref m_dropped);

            internal void Reset()
            {
                Volatile.Write(ref m_writeSequence, 0L);
                Volatile.Write(ref m_readSequence, 0L);
                Volatile.Write(ref m_dropped, 0L);
            }

            internal void TryWrite(AccessSearchWorkerOverlaySample sample)
            {
                long write = Volatile.Read(ref m_writeSequence);
                long read = Volatile.Read(ref m_readSequence);
                if (write - read >= m_samples.Length)
                {
                    Interlocked.Increment(ref m_dropped);
                    return;
                }
                m_samples[(int)(write % m_samples.Length)] = sample;
                Volatile.Write(ref m_writeSequence, write + 1L);
            }

            internal int Drain(
                List<AccessSearchWorkerOverlaySample> destination,
                int maxSamples)
            {
                int bounded = Math.Max(0, maxSamples);
                long read = Volatile.Read(ref m_readSequence);
                long write = Volatile.Read(ref m_writeSequence);
                int count = (int)Math.Min(
                    bounded, Math.Max(0L, write - read));
                for (int index = 0; index < count; index++)
                    destination.Add(
                        m_samples[(int)((read + index) % m_samples.Length)]);
                if (count > 0)
                    Volatile.Write(ref m_readSequence, read + count);
                return count;
            }
        }
    }
}

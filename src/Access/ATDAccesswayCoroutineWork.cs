using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AutoTerrainDesignations.Access
{
    internal sealed class ExperimentalAccessSliceControl
    {
        private sealed class PhaseTiming
        {
            public int Advances;
            public int Yields;
            public double ProcessingMilliseconds;
            public double MaxSliceMilliseconds;
            public string MaxSliceStep = string.Empty;
            public double WallMilliseconds;
        }

        private int m_visitedNodes;
        private int m_pendingNodes;
        private readonly Dictionary<string, PhaseTiming> m_phaseTimings =
            new Dictionary<string, PhaseTiming>(StringComparer.Ordinal);
        private readonly List<string> m_phaseOrder = new List<string>();
        private string m_phase = "Preparing";
        private long m_phaseStartedTimestamp = Stopwatch.GetTimestamp();
        private int m_advanceCount;
        private int m_yieldCount;
        private double m_processingMilliseconds;
        private double m_maxSliceMilliseconds;
        private string m_maxSlicePhase = string.Empty;
        private string m_maxSliceStep = string.Empty;
        private string m_atomicStep = string.Empty;

        public int SliceBudgetMilliseconds { get; set; } = 2;
        public bool CancellationRequested { get; private set; }
        public string CancellationReason { get; private set; } = string.Empty;
        public string Phase => m_phase;
        public int VisitedNodes => m_visitedNodes;
        public int PendingNodes => m_pendingNodes;

        public void RequestCancellation(string reason)
        {
            CancellationRequested = true;
            CancellationReason = reason ?? string.Empty;
        }

        public void ReportProgress(int visitedNodes, int pendingNodes)
        {
            m_visitedNodes = visitedNodes;
            m_pendingNodes = pendingNodes;
        }

        public void ReportAtomicStep(string step)
            => m_atomicStep = step ?? string.Empty;

        public void ReportPhase(string phase)
        {
            string nextPhase = string.IsNullOrWhiteSpace(phase)
                ? "Preparing"
                : phase;
            if (string.Equals(m_phase, nextPhase, StringComparison.Ordinal))
                return;

            long now = Stopwatch.GetTimestamp();
            AccumulateCurrentPhaseWallTime(now);
            m_phase = nextPhase;
            m_phaseStartedTimestamp = now;
            m_atomicStep = string.Empty;
        }

        public void RecordAdvance(
            string phase,
            double elapsedMilliseconds,
            bool yielded)
        {
            string recordedPhase = string.IsNullOrWhiteSpace(phase)
                ? "Preparing"
                : phase;
            PhaseTiming timing = GetPhaseTiming(recordedPhase);
            timing.Advances++;
            if (yielded)
                timing.Yields++;
            timing.ProcessingMilliseconds += elapsedMilliseconds;
            if (elapsedMilliseconds > timing.MaxSliceMilliseconds)
            {
                timing.MaxSliceMilliseconds = elapsedMilliseconds;
                timing.MaxSliceStep = m_atomicStep;
            }

            m_advanceCount++;
            if (yielded)
                m_yieldCount++;
            m_processingMilliseconds += elapsedMilliseconds;
            if (elapsedMilliseconds > m_maxSliceMilliseconds)
            {
                m_maxSliceMilliseconds = elapsedMilliseconds;
                m_maxSlicePhase = recordedPhase;
                m_maxSliceStep = m_atomicStep;
            }
        }

        public string FormatDiagnostics()
        {
            long now = Stopwatch.GetTimestamp();
            var builder = new StringBuilder();
            builder.Append("sliceStats=[advances=")
                .Append(m_advanceCount)
                .Append(" yields=")
                .Append(m_yieldCount)
                .Append(" processingMs=")
                .Append(m_processingMilliseconds.ToString(
                    "0.##", CultureInfo.InvariantCulture))
                .Append(" maxSliceMs=")
                .Append(m_maxSliceMilliseconds.ToString(
                    "0.##", CultureInfo.InvariantCulture))
                .Append(" maxSlicePhase=")
                .Append(string.IsNullOrEmpty(m_maxSlicePhase)
                    ? "none"
                    : m_maxSlicePhase)
                .Append(" maxSliceStep=")
                .Append(string.IsNullOrEmpty(m_maxSliceStep)
                    ? "none"
                    : m_maxSliceStep)
                .Append(" phases=[");
            for (int index = 0; index < m_phaseOrder.Count; index++)
            {
                if (index > 0)
                    builder.Append(';');
                string phase = m_phaseOrder[index];
                PhaseTiming timing = m_phaseTimings[phase];
                double wallMilliseconds = timing.WallMilliseconds;
                if (string.Equals(phase, m_phase, StringComparison.Ordinal))
                {
                    wallMilliseconds += (now - m_phaseStartedTimestamp)
                        * 1000d / Stopwatch.Frequency;
                }
                builder.Append(phase)
                    .Append("{advances=")
                    .Append(timing.Advances)
                    .Append(",yields=")
                    .Append(timing.Yields)
                    .Append(",wallMs=")
                    .Append(wallMilliseconds.ToString(
                        "0.##", CultureInfo.InvariantCulture))
                    .Append(",processingMs=")
                    .Append(timing.ProcessingMilliseconds.ToString(
                        "0.##", CultureInfo.InvariantCulture))
                    .Append(",maxSliceMs=")
                    .Append(timing.MaxSliceMilliseconds.ToString(
                        "0.##", CultureInfo.InvariantCulture))
                    .Append(",maxStep=")
                    .Append(string.IsNullOrEmpty(timing.MaxSliceStep)
                        ? "none"
                        : timing.MaxSliceStep)
                    .Append('}');
            }
            return builder.Append("]]").ToString();
        }

        private PhaseTiming GetPhaseTiming(string phase)
        {
            if (m_phaseTimings.TryGetValue(phase, out PhaseTiming timing))
                return timing;
            timing = new PhaseTiming();
            m_phaseTimings.Add(phase, timing);
            m_phaseOrder.Add(phase);
            return timing;
        }

        private void AccumulateCurrentPhaseWallTime(long now)
        {
            PhaseTiming timing = GetPhaseTiming(m_phase);
            timing.WallMilliseconds += (now - m_phaseStartedTimestamp)
                * 1000d / Stopwatch.Frequency;
        }
    }

    internal sealed class ATDAccesswayCoroutineWork : IATDAccesswayManagedWork
    {
        private readonly ExperimentalAccessSliceControl m_sliceControl;
        private readonly IEnumerator m_routine;
        private readonly Func<ATDAccesswayRequestResult> m_resultFactory;
        private readonly Func<int> m_budgetProvider;
        private double m_processingMilliseconds;
        private bool m_terminal;

        public ATDAccesswayCoroutineWork(
            Func<ExperimentalAccessSliceControl, IEnumerator> routineFactory,
            Func<ATDAccesswayRequestResult> resultFactory,
            Func<int> budgetProvider)
        {
            if (routineFactory == null)
                throw new ArgumentNullException(nameof(routineFactory));
            m_resultFactory = resultFactory
                ?? throw new ArgumentNullException(nameof(resultFactory));
            m_budgetProvider = budgetProvider
                ?? throw new ArgumentNullException(nameof(budgetProvider));
            m_sliceControl = new ExperimentalAccessSliceControl();
            m_routine = routineFactory(m_sliceControl)
                ?? throw new InvalidOperationException(
                    "Managed accessway routine factory returned null.");
        }

        public int VisitedNodes => m_sliceControl.VisitedNodes;
        public int PendingNodes => m_sliceControl.PendingNodes;
        public string Phase => m_sliceControl.Phase;
        public double ProcessingMilliseconds => m_processingMilliseconds;

        public bool Advance()
        {
            if (m_terminal) return false;
            m_sliceControl.SliceBudgetMilliseconds = Math.Max(
                1, m_budgetProvider());
            Stopwatch timer = Stopwatch.StartNew();
            bool running = m_routine.MoveNext();
            timer.Stop();
            m_processingMilliseconds += timer.Elapsed.TotalMilliseconds;
            // Report the phase after MoveNext: phase transitions happen before
            // a coroutine yields, so the completed slice is attributed to the
            // phase that produced that yield.
            m_sliceControl.RecordAdvance(
                m_sliceControl.Phase,
                timer.Elapsed.TotalMilliseconds,
                running);
            m_terminal = !running;
            return running;
        }

        public void RequestCancellation(string reason)
            => m_sliceControl.RequestCancellation(reason);

        public ATDAccesswayRequestResult GetTerminalResult()
        {
            if (m_sliceControl.CancellationRequested)
                return ATDAccesswayRequestResult.Cancelled(
                    m_sliceControl.CancellationReason);
            return m_resultFactory();
        }

        public void Dispose()
            => (m_routine as IDisposable)?.Dispose();
    }
}

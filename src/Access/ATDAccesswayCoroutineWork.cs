using System;
using System.Collections;
using System.Diagnostics;

namespace AutoTerrainDesignations.Access
{
    internal sealed class ExperimentalAccessSliceControl
    {
        private int m_visitedNodes;
        private int m_pendingNodes;

        public int SliceBudgetMilliseconds { get; set; } = 2;
        public bool CancellationRequested { get; private set; }
        public string CancellationReason { get; private set; } = string.Empty;
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

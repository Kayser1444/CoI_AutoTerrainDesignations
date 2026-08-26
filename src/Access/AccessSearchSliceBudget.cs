using System;
using System.Diagnostics;

namespace AutoTerrainDesignations.Access
{
    /// <summary>
    /// One cooperative search slice's shared deadline and cancellation state.
    /// Nested search helpers must consume the same instance so a child cannot
    /// accidentally reset the caller's allowance.
    /// </summary>
    internal sealed class AccessSearchSliceBudget
    {
        private readonly long m_deadlineTimestamp;

        public bool CancellationRequested { get; set; }

        public bool IsExpired
            => Stopwatch.GetTimestamp() >= m_deadlineTimestamp;

        public bool ShouldYield
            => CancellationRequested || IsExpired;

        public AccessSearchSliceBudget(
            int milliseconds,
            bool cancellationRequested = false)
        {
            int boundedMilliseconds = Math.Max(1, milliseconds);
            long ticks = (long)Math.Ceiling(
                boundedMilliseconds * (double)Stopwatch.Frequency / 1000d);
            m_deadlineTimestamp = Stopwatch.GetTimestamp()
                + Math.Max(1L, ticks);
            CancellationRequested = cancellationRequested;
        }
    }
}

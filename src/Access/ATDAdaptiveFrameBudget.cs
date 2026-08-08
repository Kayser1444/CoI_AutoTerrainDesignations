using System;

namespace AutoTerrainDesignations.Access
{
    internal enum ATDAdaptiveBudgetAction
    {
        None,
        Initialized,
        ModeChanged,
        Discontinuity,
        Held,
        Increased,
        ReducedForSlowFrame,
        ReducedForSliceOverrun
    }

    internal readonly struct ATDAdaptiveFrameBudgetSnapshot
    {
        public int BudgetMilliseconds { get; }
        public double FrameMilliseconds { get; }
        public double SliceMilliseconds { get; }
        public double EstimatedNonATDMilliseconds { get; }
        public ATDAdaptiveBudgetAction Action { get; }

        public ATDAdaptiveFrameBudgetSnapshot(
            int budgetMilliseconds,
            double frameMilliseconds,
            double sliceMilliseconds,
            double estimatedNonATDMilliseconds,
            ATDAdaptiveBudgetAction action)
        {
            BudgetMilliseconds = budgetMilliseconds;
            FrameMilliseconds = frameMilliseconds;
            SliceMilliseconds = sliceMilliseconds;
            EstimatedNonATDMilliseconds = estimatedNonATDMilliseconds;
            Action = action;
        }
    }

    /// <summary>
    /// Learns a cooperative main-thread work allowance from rendered-frame
    /// cadence. Slow frames and slice overruns cut the allowance immediately;
    /// healthy frames restore it gradually. Running and paused modes retain
    /// independent learned allowances.
    /// </summary>
    internal sealed class ATDAdaptiveFrameBudget
    {
        internal const int MinimumBudgetMilliseconds = 1;
        internal const double RunningTargetFrameMilliseconds = 1000d / 60d;
        internal const double PausedTargetFrameMilliseconds = 1000d / 30d;

        private const double SlowFrameFactor = 1.15d;
        private const double HealthyFrameFactor = 1.05d;
        private const double ReductionFactor = 0.5d;
        private const double MinimumIncreaseMilliseconds = 0.25d;
        private const double ProportionalIncrease = 0.08d;
        private const double DiscontinuityMilliseconds = 250d;
        private const double RisingExternalCostWeight = 0.35d;
        private const double FallingExternalCostWeight = 0.1d;

        private sealed class ModeState
        {
            public double BudgetMilliseconds;
            public bool HasEstimatedNonATDMilliseconds;
            public double EstimatedNonATDMilliseconds;

            public ModeState(double initialBudgetMilliseconds)
                => BudgetMilliseconds = initialBudgetMilliseconds;
        }

        private readonly ModeState m_running =
            new ModeState(MinimumBudgetMilliseconds);
        private readonly ModeState m_paused =
            new ModeState(MinimumBudgetMilliseconds);

        private bool m_hasPreviousFrame;
        private bool m_previousFramePaused;
        private double m_previousFrameTimestampMilliseconds;
        private bool m_previousSliceRecorded;
        private double m_previousSliceMilliseconds;
        private int m_previousAssignedBudgetMilliseconds =
            MinimumBudgetMilliseconds;

        public ATDAdaptiveFrameBudgetSnapshot Snapshot { get; private set; }

        public int BeginFrame(
            bool paused,
            double timestampMilliseconds,
            int maximumBudgetMilliseconds)
        {
            int maximum = Math.Max(
                MinimumBudgetMilliseconds,
                maximumBudgetMilliseconds);
            ModeState state = paused ? m_paused : m_running;
            state.BudgetMilliseconds = Math.Max(
                MinimumBudgetMilliseconds,
                Math.Min(maximum, state.BudgetMilliseconds));

            double frameMilliseconds = 0d;
            double estimatedNonATDMilliseconds =
                state.EstimatedNonATDMilliseconds;
            ATDAdaptiveBudgetAction action;
            bool sameMode = m_hasPreviousFrame
                && m_previousFramePaused == paused;
            if (!m_hasPreviousFrame)
            {
                action = ATDAdaptiveBudgetAction.Initialized;
            }
            else
            {
                frameMilliseconds = timestampMilliseconds
                    - m_previousFrameTimestampMilliseconds;
                if (!sameMode)
                {
                    action = ATDAdaptiveBudgetAction.ModeChanged;
                }
                else if (frameMilliseconds <= 0d
                    || frameMilliseconds > DiscontinuityMilliseconds)
                {
                    action = ATDAdaptiveBudgetAction.Discontinuity;
                }
                else if (!m_previousSliceRecorded)
                {
                    action = ATDAdaptiveBudgetAction.Held;
                }
                else
                {
                    double target = paused
                        ? PausedTargetFrameMilliseconds
                        : RunningTargetFrameMilliseconds;
                    double nonATDSample = Math.Max(
                        0d,
                        frameMilliseconds - (m_previousSliceRecorded
                            ? m_previousSliceMilliseconds
                            : 0d));
                    if (!state.HasEstimatedNonATDMilliseconds)
                    {
                        state.EstimatedNonATDMilliseconds = nonATDSample;
                        state.HasEstimatedNonATDMilliseconds = true;
                    }
                    else
                    {
                        double weight = nonATDSample
                                > state.EstimatedNonATDMilliseconds
                            ? RisingExternalCostWeight
                            : FallingExternalCostWeight;
                        state.EstimatedNonATDMilliseconds +=
                            (nonATDSample
                                - state.EstimatedNonATDMilliseconds) * weight;
                    }
                    estimatedNonATDMilliseconds =
                        state.EstimatedNonATDMilliseconds;
                    double overrunAllowance = Math.Max(
                        0.5d,
                        m_previousAssignedBudgetMilliseconds * 0.25d);
                    bool sliceOverran = m_previousSliceMilliseconds
                        > m_previousAssignedBudgetMilliseconds
                            + overrunAllowance;
                    bool frameWasSlow = frameMilliseconds
                        > target * SlowFrameFactor;
                    bool externalWorkWasSlow = estimatedNonATDMilliseconds
                        > target * SlowFrameFactor;
                    if (sliceOverran || frameWasSlow || externalWorkWasSlow)
                    {
                        state.BudgetMilliseconds = Math.Max(
                            MinimumBudgetMilliseconds,
                            state.BudgetMilliseconds * ReductionFactor);
                        action = sliceOverran
                            ? ATDAdaptiveBudgetAction.ReducedForSliceOverrun
                            : ATDAdaptiveBudgetAction.ReducedForSlowFrame;
                    }
                    else if (frameMilliseconds
                        <= target * HealthyFrameFactor)
                    {
                        state.BudgetMilliseconds = Math.Min(
                            maximum,
                            state.BudgetMilliseconds + Math.Max(
                                MinimumIncreaseMilliseconds,
                                state.BudgetMilliseconds
                                    * ProportionalIncrease));
                        action = ATDAdaptiveBudgetAction.Increased;
                    }
                    else
                    {
                        action = ATDAdaptiveBudgetAction.Held;
                    }
                }
            }

            int assigned = Math.Max(
                MinimumBudgetMilliseconds,
                Math.Min(maximum,
                    (int)Math.Floor(state.BudgetMilliseconds + 0.0001d)));
            m_hasPreviousFrame = true;
            m_previousFramePaused = paused;
            m_previousFrameTimestampMilliseconds = timestampMilliseconds;
            m_previousSliceRecorded = false;
            m_previousAssignedBudgetMilliseconds = assigned;
            Snapshot = new ATDAdaptiveFrameBudgetSnapshot(
                assigned,
                frameMilliseconds,
                m_previousSliceMilliseconds,
                estimatedNonATDMilliseconds,
                action);
            return assigned;
        }

        public void RecordSlice(double elapsedMilliseconds)
        {
            m_previousSliceMilliseconds = Math.Max(0d, elapsedMilliseconds);
            m_previousSliceRecorded = true;
        }
    }
}

// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.

using System;

namespace AutoTerrainDesignations.Access
{
    internal enum AccessFailureRetryBlockReason
    {
        None,
        SameSimulationStep,
        MinimumGrace,
        WaitingForChange
    }

    internal readonly struct AccessFailureRetryDecision
    {
        internal AccessFailureRetryDecision(
            bool shouldAttempt,
            AccessFailureRetryBlockReason blockReason,
            double retryAfterSeconds)
        {
            ShouldAttempt = shouldAttempt;
            BlockReason = blockReason;
            RetryAfterSeconds = retryAfterSeconds;
        }

        internal bool ShouldAttempt { get; }
        internal AccessFailureRetryBlockReason BlockReason { get; }
        internal double RetryAfterSeconds { get; }
    }

    internal sealed class AccessFailureRetryState
    {
        internal const double MinimumGraceSeconds = 10d;
        internal const double MaximumGraceSeconds = 60d;

        private bool m_hasFailure;
        private string m_failedFingerprint = string.Empty;
        private double m_failedAtSeconds;
        private int m_failedAtSimulationStep = int.MinValue;

        internal bool HasFailure => m_hasFailure;

        internal AccessFailureRetryDecision Evaluate(
            string fingerprint,
            double nowSeconds,
            int simulationStep)
        {
            if (!m_hasFailure)
                return Allow();

            double elapsedSeconds = Math.Max(0d, nowSeconds - m_failedAtSeconds);
            if (simulationStep == m_failedAtSimulationStep)
            {
                return Block(
                    AccessFailureRetryBlockReason.SameSimulationStep,
                    Math.Max(0d, MinimumGraceSeconds - elapsedSeconds));
            }

            if (elapsedSeconds < MinimumGraceSeconds)
            {
                return Block(
                    AccessFailureRetryBlockReason.MinimumGrace,
                    MinimumGraceSeconds - elapsedSeconds);
            }

            if (!string.Equals(
                    fingerprint ?? string.Empty,
                    m_failedFingerprint,
                    StringComparison.Ordinal))
                return Allow();

            if (elapsedSeconds < MaximumGraceSeconds)
            {
                return Block(
                    AccessFailureRetryBlockReason.WaitingForChange,
                    MaximumGraceSeconds - elapsedSeconds);
            }

            return Allow();
        }

        private static AccessFailureRetryDecision Allow()
        {
            return new AccessFailureRetryDecision(
                shouldAttempt: true,
                AccessFailureRetryBlockReason.None,
                retryAfterSeconds: 0d);
        }

        private static AccessFailureRetryDecision Block(
            AccessFailureRetryBlockReason reason,
            double retryAfterSeconds)
        {
            return new AccessFailureRetryDecision(
                shouldAttempt: false,
                reason,
                Math.Max(0d, retryAfterSeconds));
        }

        internal void RecordFailure(
            string fingerprint,
            double nowSeconds,
            int simulationStep)
        {
            m_hasFailure = true;
            m_failedFingerprint = fingerprint ?? string.Empty;
            m_failedAtSeconds = nowSeconds;
            m_failedAtSimulationStep = simulationStep;
        }

        internal void Clear()
        {
            m_hasFailure = false;
            m_failedFingerprint = string.Empty;
            m_failedAtSeconds = 0d;
            m_failedAtSimulationStep = int.MinValue;
        }
    }

    internal static class AccessFailureRetryPolicyFixtures
    {
        internal static bool ValidateAll(out string failure)
        {
            var state = new AccessFailureRetryState();
            state.RecordFailure("terrain-a", nowSeconds: 100d, simulationStep: 42);

            if (!AssertBlocked(
                    state.Evaluate("terrain-a", 100d, 42),
                    AccessFailureRetryBlockReason.SameSimulationStep,
                    "same simulation step",
                    out failure))
                return false;

            if (!AssertBlocked(
                    state.Evaluate("terrain-b", 109.999d, 43),
                    AccessFailureRetryBlockReason.MinimumGrace,
                    "changed fingerprint during minimum grace",
                    out failure))
                return false;

            AccessFailureRetryDecision changed = state.Evaluate(
                "terrain-b", 110d, 43);
            if (!changed.ShouldAttempt)
            {
                failure = "a changed fingerprint did not reopen at the minimum grace";
                return false;
            }

            if (!AssertBlocked(
                    state.Evaluate("terrain-a", 159.999d, 43),
                    AccessFailureRetryBlockReason.WaitingForChange,
                    "unchanged fingerprint before maximum grace",
                    out failure))
                return false;

            AccessFailureRetryDecision maximum = state.Evaluate(
                "terrain-a", 160d, 43);
            if (!maximum.ShouldAttempt)
            {
                failure = "an unchanged fingerprint did not reopen at the maximum grace";
                return false;
            }

            state.Clear();
            if (!state.Evaluate("terrain-a", 100d, 42).ShouldAttempt)
            {
                failure = "cleared retry state still blocked an attempt";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool AssertBlocked(
            AccessFailureRetryDecision decision,
            AccessFailureRetryBlockReason expectedReason,
            string scenario,
            out string failure)
        {
            if (decision.ShouldAttempt)
            {
                failure = scenario + " was allowed";
                return false;
            }

            if (decision.BlockReason != expectedReason)
            {
                failure = scenario + " used " + decision.BlockReason
                    + " instead of " + expectedReason;
                return false;
            }

            failure = string.Empty;
            return true;
        }
    }
}

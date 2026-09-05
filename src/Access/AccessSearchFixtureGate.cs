using System;

namespace AutoTerrainDesignations.Access
{
    /// <summary>
    /// Caches the deterministic access-search fixture result for the lifetime
    /// of the loaded mod assembly. These checks validate code invariants, not
    /// world state, so repeating them for every production snapshot only adds
    /// latency to the snapshot preparation path.
    /// </summary>
    internal static class AccessSearchFixtureGate
    {
        private static readonly object s_sync = new object();
        private static bool s_initialized;
        private static bool s_valid;
        private static string s_failureReason = string.Empty;
        private static int s_validationRunCount;

        internal static int ValidationRunCount
        {
            get
            {
                lock (s_sync)
                    return s_validationRunCount;
            }
        }

        internal static bool EnsureInitialized(out string failureReason)
        {
            lock (s_sync)
            {
                if (!s_initialized)
                    Initialize();

                failureReason = s_failureReason;
                return s_valid;
            }
        }

        private static void Initialize()
        {
            s_validationRunCount++;
            bool v1Valid = false;
            bool v2Valid = false;
            bool architectureValid = false;
            bool captureValid = false;
            bool reductionValid = false;
            string v1Failure = string.Empty;
            string v2Failure = string.Empty;
            string architectureFailure = string.Empty;
            string captureFailure = string.Empty;
            string reductionFailure = string.Empty;

            try
            {
                architectureValid = AccessSearchArchitectureFixtures.ValidateAll(
                    out architectureFailure);
            }
            catch (Exception ex)
            {
                architectureFailure = "Exception:" + ex.GetType().Name;
            }

            try
            {
                captureValid = AccessCaptureFixtures.ValidateAll(
                    out captureFailure);
            }
            catch (Exception ex)
            {
                captureFailure = "Exception:" + ex.GetType().Name;
            }

            try
            {
                reductionValid = Reduction.ReducedAccessDomainFixtures
                    .ValidateAll(out reductionFailure);
            }
            catch (Exception ex)
            {
                reductionFailure = "Exception:" + ex.GetType().Name;
            }

            try
            {
                v1Valid = AccessPathSearch.ValidateCoreTransitions(
                    out v1Failure);
            }
            catch (Exception ex)
            {
                v1Failure = "Exception:" + ex.GetType().Name;
            }

            try
            {
                v2Valid = V2.AccessV2Fixtures.ValidateAll(
                    out v2Failure);
            }
            catch (Exception ex)
            {
                v2Failure = "Exception:" + ex.GetType().Name;
            }

            s_valid = architectureValid && captureValid && reductionValid
                && v1Valid && v2Valid;
            if (s_valid)
            {
                s_failureReason = string.Empty;
            }
            else
            {
                s_failureReason =
                    "Architecture=" + (architectureValid ? "ok" : architectureFailure)
                    + ";Capture=" + (captureValid ? "ok" : captureFailure)
                    + ";Reduction=" + (reductionValid ? "ok" : reductionFailure)
                    + ";V1=" + (v1Valid ? "ok" : v1Failure)
                    + ";V2=" + (v2Valid ? "ok" : v2Failure);
            }
            s_initialized = true;
        }
    }
}

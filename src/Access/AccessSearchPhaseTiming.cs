using System;

namespace AutoTerrainDesignations.Access
{
    /// <summary>
    /// Timings for access search preparation, execution, and materialization.
    /// </summary>
    internal readonly struct AccessSearchPhaseTiming
    {
        public double PreparationMilliseconds { get; }
        public double SearchMilliseconds { get; }
        public double MaterializationMilliseconds { get; }

        public AccessSearchPhaseTiming(
            double preparationMilliseconds,
            double searchMilliseconds,
            double materializationMilliseconds)
        {
            PreparationMilliseconds = Math.Max(0d, preparationMilliseconds);
            SearchMilliseconds = Math.Max(0d, searchMilliseconds);
            MaterializationMilliseconds = Math.Max(0d, materializationMilliseconds);
        }
    }
}

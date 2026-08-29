using System;

namespace AutoTerrainDesignations.Access
{
    /// <summary>
    /// Terminal output owned by one sliced access-search invocation.
    /// </summary>
    internal sealed class ExperimentalAccessDryRunResult
    {
        public AccessSearchResult? SearchResult { get; private set; }
        public AccessDesignationPlan? Plan { get; private set; }
        public AccessReplayPhaseTiming ReplayTiming { get; private set; }
        public bool IsComplete { get; private set; }

        public void Complete(
            AccessSearchResult searchResult,
            AccessDesignationPlan? plan,
            AccessReplayPhaseTiming replayTiming = default)
        {
            if (IsComplete)
                throw new InvalidOperationException(
                    "A sliced access dry run can only complete once.");
            SearchResult = searchResult
                ?? throw new ArgumentNullException(nameof(searchResult));
            Plan = plan;
            ReplayTiming = replayTiming;
            IsComplete = true;
        }
    }
}

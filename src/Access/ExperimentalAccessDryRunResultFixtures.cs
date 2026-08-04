using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    internal static class ExperimentalAccessDryRunResultFixtures
    {
        internal static bool ValidateAll(out string failure)
        {
            var firstSearch = CreateSearchResult(
                success: true, reason: string.Empty, start: new Tile2i(4, 8));
            var firstPlan = AccessDesignationPlan.Invalid(
                "fixture-plan", firstSearch.StartOrigin);
            var secondSearch = CreateSearchResult(
                success: false, reason: "SearchCancelled",
                start: new Tile2i(40, 80));
            var first = new ExperimentalAccessDryRunResult();
            var second = new ExperimentalAccessDryRunResult();

            first.Complete(firstSearch, firstPlan);
            second.Complete(secondSearch, plan: null);

            if (!first.IsComplete
                || !ReferenceEquals(first.SearchResult, firstSearch)
                || !ReferenceEquals(first.Plan, firstPlan))
            {
                failure = "First invocation did not retain its own terminal pair.";
                return false;
            }
            if (!second.IsComplete
                || !ReferenceEquals(second.SearchResult, secondSearch)
                || second.Plan != null)
            {
                failure = "Cancelled invocation did not retain its own diagnostics without a plan.";
                return false;
            }
            if (!ReferenceEquals(first.SearchResult, firstSearch)
                || !ReferenceEquals(first.Plan, firstPlan))
            {
                failure = "Completing a later invocation changed the earlier result.";
                return false;
            }

            try
            {
                first.Complete(secondSearch, plan: null);
                failure = "A terminal invocation accepted a second completion.";
                return false;
            }
            catch (InvalidOperationException)
            {
            }

            failure = string.Empty;
            return true;
        }

        private static AccessSearchResult CreateSearchResult(
            bool success,
            string reason,
            Tile2i start)
            => new AccessSearchResult(
                success,
                reason,
                start,
                Array.Empty<AccessSearchNode>(),
                0f,
                0,
                new Dictionary<string, int>());
    }
}

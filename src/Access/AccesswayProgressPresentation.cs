using System.Globalization;

namespace AutoTerrainDesignations.Access
{
    internal static class AccesswayProgressPresentation
    {
        internal static string FormatStats(
            ATDAccesswayHandleSnapshot snapshot,
            int maxVisitedNodes,
            int sliceBudgetMilliseconds,
            int timeoutSeconds)
        {
            string prefix =
                $"visited {snapshot.VisitedNodes:N0}/{maxVisitedNodes:N0} · "
                + $"queue {snapshot.PendingNodes:N0} · ";
            if (snapshot.ExecutionBackend
                == ATDAccesswayExecutionBackend.Worker)
            {
                return prefix
                    + "worker elapsed "
                    + (snapshot.StatusElapsedMilliseconds / 1000d).ToString(
                        "0.0", CultureInfo.InvariantCulture)
                    + $"/{timeoutSeconds}s";
            }
            return prefix
                + $"budget {sliceBudgetMilliseconds} ms/frame · "
                + $"processing {snapshot.ProcessingMilliseconds / 1000d:0.0}/"
                + $"{timeoutSeconds}s";
        }
    }
}

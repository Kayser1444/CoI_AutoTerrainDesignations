#if ATD_REPLAY_LAB
using System;
#endif
using System.Globalization;

namespace AutoTerrainDesignations.Access
{
    internal static class AccesswayProgressPresentation
    {
        internal static bool ShouldShowToast(ATDAccesswayHandleSnapshot snapshot,
            double activeWallSeconds)
            // Polling a background encoder/worker barely consumes game-thread
            // processing time. Visibility follows how long the player has waited.
            => !snapshot.IsTerminal && activeWallSeconds >= 0.25d;

#if ATD_REPLAY_LAB
        internal static bool IsReplayCapture(ATDAccesswayHandleSnapshot snapshot)
            => snapshot.Phase.StartsWith("Recording access replay", StringComparison.Ordinal)
                || snapshot.Phase.StartsWith("Cancelling access replay", StringComparison.Ordinal);
#endif

        internal static string FormatStats(
            ATDAccesswayHandleSnapshot snapshot,
            int maxVisitedNodes,
            int sliceBudgetMilliseconds,
            int timeoutSeconds,
            double? operationElapsedSeconds = null,
            string operationElapsedFormat = "Total elapsed {0:0.0}s")
        {
#if ATD_REPLAY_LAB
            if (IsReplayCapture(snapshot))
                return "Background recording · abort keeps placed terrain work";
#endif
            string prefix =
                $"visited {snapshot.VisitedNodes:N0}/{maxVisitedNodes:N0} · "
                + $"queue {snapshot.PendingNodes:N0} · ";
            if (operationElapsedSeconds.HasValue)
            {
                string elapsed = string.Format(
                    CultureInfo.CurrentCulture, operationElapsedFormat, operationElapsedSeconds.Value);
                return snapshot.VisitedNodes > 0 || snapshot.PendingNodes > 0
                    ? prefix + elapsed
                    : elapsed;
            }
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

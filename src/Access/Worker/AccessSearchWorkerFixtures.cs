using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Mafi;

namespace AutoTerrainDesignations.Access.Worker
{
    internal static class AccessSearchWorkerFixtures
    {
        internal static bool ValidateAll(out string failure)
        {
            try
            {
                if (!AutoDepthDesignation
                    .ValidateCreateDesignationsWorkerCancellationFixture(
                        out failure))
                    return false;

                AccessPathRequest request = CreateRequest("worker-parity");
                var cooperativeWorkspace =
                    new AccessSearchWorkspace(request.Snapshot);
                AccessSearchResult cooperativeResult =
                    AccessPathSearch.FindPath(request, cooperativeWorkspace);
                AccessDesignationPlan cooperativePlan =
                    cooperativeResult.Success
                        ? AccessPathMaterializer.Materialize(
                            cooperativeWorkspace, cooperativeResult)
                        : AccessDesignationPlan.Invalid(
                            cooperativeResult.FailureReason,
                            cooperativeResult.StartOrigin);
                byte[] expected = AccessSearchReplayCanonical.Serialize(
                    cooperativeResult, cooperativePlan);

                const int world = 81001;
                AccessSearchWorker worker = AccessSearchWorker.Shared;
                worker.SetCurrentWorld(world);
                var job = new AccessSearchWorkerJob(
                    8100101, world, request, captureOverlay: true);
                if (!worker.TrySubmit(job, out string submitFailure))
                {
                    failure = "Worker parity submit failed: " + submitFailure;
                    return false;
                }
                if (!WaitForTerminal(
                        worker, job, out AccessSearchWorkerTerminal? terminal)
                    || terminal == null
                    || terminal.IsFaulted
                    || terminal.Outcome == null)
                {
                    failure = "Worker parity job did not complete cleanly: "
                        + (terminal?.Fault ?? "timeout");
                    return false;
                }
                byte[] actual = AccessSearchReplayCanonical.Serialize(
                    terminal.Outcome.SearchResult,
                    terminal.Outcome.Plan);
                if (!expected.SequenceEqual(actual))
                {
                    failure = "Worker and cooperative canonical outcomes differ.";
                    return false;
                }
                var overlaySamples =
                    new List<AccessSearchWorkerOverlaySample>();
                worker.DrainOverlay(
                    job.JobId, overlaySamples, int.MaxValue);
                if (overlaySamples.Count == 0)
                {
                    failure =
                        "Overlay-enabled worker search delivered zero node samples. "
                        + "visited=" + terminal.Outcome.SearchResult.VisitedNodes
                        + " dropped=" + terminal.DroppedOverlaySamples;
                    return false;
                }

                var cancelledJob = new AccessSearchWorkerJob(
                    8100102, world, CreateRequest("worker-cancel"));
                if (!worker.TrySubmit(
                        cancelledJob, out string cancelSubmitFailure))
                {
                    failure = "Worker cancellation submit failed: "
                        + cancelSubmitFailure;
                    return false;
                }
                Stopwatch cancellation = Stopwatch.StartNew();
                worker.Cancel(cancelledJob.JobId, "FixtureCancellation");
                if (!WaitForTerminal(
                        worker, cancelledJob,
                        out AccessSearchWorkerTerminal? cancelledTerminal)
                    || cancelledTerminal?.Outcome == null
                    || !string.Equals(
                        cancelledTerminal.Outcome.SearchResult.FailureReason,
                        "SearchCancelled", StringComparison.Ordinal))
                {
                    failure = "Worker did not acknowledge cancellation.";
                    return false;
                }
                cancellation.Stop();
                if (cancellation.ElapsedMilliseconds > 250)
                {
                    failure = "Worker cancellation acknowledgment exceeded "
                        + "250 ms: " + cancellation.ElapsedMilliseconds + " ms.";
                    return false;
                }

                var disposableJob = new AccessSearchWorkerJob(
                    8100103, world, CreateRequest("worker-dispose", 200));
                var disposableWork = new ATDAccesswayCoroutineWork(
                    control => RunDisposableWorkerJob(
                        control, worker, disposableJob),
                    () => ATDAccesswayRequestResult.Succeeded(),
                    () => 1);
                if (!disposableWork.Advance())
                {
                    failure = "Disposable worker fixture did not yield.";
                    return false;
                }
                bool activeObserved = SpinWait.SpinUntil(
                    () => worker.TryReadProgress(
                            disposableJob.JobId,
                            out AccessSearchWorkerProgress? progress)
                        && progress != null
                        && !string.Equals(
                            progress.Phase,
                            "Queued for access search worker",
                            StringComparison.Ordinal),
                    millisecondsTimeout: 1000);
                if (!activeObserved)
                {
                    disposableWork.Dispose();
                    worker.TryConsumeTerminal(
                        disposableJob.JobId,
                        disposableJob.WorldGeneration,
                        out _);
                    failure =
                        "Disposable worker fixture did not observe active search.";
                    return false;
                }
                disposableWork.Dispose();

                var recoveryJob = new AccessSearchWorkerJob(
                    8100104, world, CreateRequest("worker-recovery"));
                string recoveryFailure = string.Empty;
                bool recovered = SpinWait.SpinUntil(
                    () => worker.TrySubmit(
                        recoveryJob, out recoveryFailure),
                    millisecondsTimeout: 500);
                if (!recovered)
                {
                    worker.TryConsumeTerminal(
                        disposableJob.JobId,
                        disposableJob.WorldGeneration,
                        out _);
                    failure = "Disposed worker job was not reclaimed: "
                        + recoveryFailure;
                    return false;
                }
                worker.Cancel(recoveryJob.JobId, "FixtureCleanup");
                WaitForTerminal(worker, recoveryJob, out _);

                failure = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
                return false;
            }
        }

        private static System.Collections.IEnumerator RunDisposableWorkerJob(
            ExperimentalAccessSliceControl control,
            AccessSearchWorker worker,
            AccessSearchWorkerJob job)
        {
            control.RegisterDisposalCancellation(
                reason => worker.Abandon(job.JobId, reason));
            if (!worker.TrySubmit(job, out string failure))
                throw new InvalidOperationException(failure);
            while (true)
                yield return null;
        }

        private static bool WaitForTerminal(
            AccessSearchWorker worker,
            AccessSearchWorkerJob job,
            out AccessSearchWorkerTerminal? terminal)
        {
            AccessSearchWorkerTerminal? found = null;
            bool completed = SpinWait.SpinUntil(
                () => worker.TryConsumeTerminal(
                    job.JobId, job.WorldGeneration, out found),
                millisecondsTimeout: 5000);
            terminal = found;
            return completed;
        }

        private static AccessPathRequest CreateRequest(
            string id,
            int extent = 12)
        {
            var heights = new Dictionary<Tile2i, int>();
            var ground = new List<Tile2i>();
            for (int y = 0; y <= extent; y++)
                for (int x = 0; x <= extent; x++)
                {
                    var tile = new Tile2i(x, y);
                    heights[tile] = 0;
                    ground.Add(tile);
                }
            var start = new Tile2i(0, 0);
            var goal = new Tile2i(extent - 1, extent - 1);
            var flat = new AccessHeightProfile(0, 0, 0, 0);
            var snapshot = new AccessSearchSnapshot(
                Tile2i.Zero,
                new Tile2i(extent, extent),
                new Tile2i(extent / 2, extent / 2),
                -2,
                2,
                true,
                false,
                false,
                1f,
                1f,
                heights,
                new Dictionary<Tile2i, int>(),
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [start] = flat,
                },
                new[] { start },
                ground,
                new[] { goal },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>());
            return new AccessPathRequest(
                id,
                snapshot,
                new AccessPathEndpoint(
                    AccessPathEndpointKind.FixedProfiles,
                    new[] { start }),
                new AccessPathEndpoint(
                    AccessPathEndpointKind.GroundTiles,
                    new[] { goal }),
                1,
                AccessPathIntent.ConstructAccessway);
        }
    }
}

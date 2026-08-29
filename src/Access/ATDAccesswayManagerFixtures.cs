using System;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    internal static class ATDAccesswayManagerFixtures
    {
        internal static bool ValidateAll(out string failure)
        {
            int fixtureRunsBefore = AccessSearchFixtureGate.ValidationRunCount;
            if (!AccessSearchFixtureGate.EnsureInitialized(
                    out string fixtureFailure))
            {
                failure = "Cached access fixture gate failed: " + fixtureFailure;
                return false;
            }
            int fixtureRunsAfterFirst =
                AccessSearchFixtureGate.ValidationRunCount;
            if (!AccessSearchFixtureGate.EnsureInitialized(
                    out string repeatedFixtureFailure))
            {
                failure =
                    "Cached access fixture gate changed result on reuse: "
                    + repeatedFixtureFailure;
                return false;
            }
            int fixtureRunsAfterSecond =
                AccessSearchFixtureGate.ValidationRunCount;
            if (fixtureRunsAfterSecond != fixtureRunsAfterFirst
                || fixtureRunsAfterFirst > fixtureRunsBefore + 1)
            {
                failure =
                    "Deterministic access fixtures were not cached for the "
                    + "lifetime of the runtime.";
                return false;
            }

            if (!ValidateAdaptiveBudget(out failure))
                return false;
            if (!ValidateManagerHardening(out failure))
                return false;
            if (!ValidateProgressPresentation(out failure))
                return false;

            var manager = new ATDAccesswayManager();
            var work = new FixtureWork(steps: 2);
            var request = new ATDAccesswayRequest(
                "farm-prep/tower:40",
                "fingerprint-a",
                ATDAccesswayRequestKind.FarmingPreparation,
                ATDAccesswayPriority.Derived,
                () => work);

            ATDAccesswayRequestHandle first = manager.Enqueue(request);
            ATDAccesswayRequestHandle duplicate = manager.Enqueue(request);
            if (!ReferenceEquals(first, duplicate))
            {
                failure = "An unchanged live obligation did not coalesce to its existing handle.";
                return false;
            }

            manager.Tick(suspended: false);
            if (work.AdvanceCount != 1
                || manager.Read(first).State != ATDAccesswayRequestState.Active)
            {
                failure = "One manager tick did not advance exactly one work slice.";
                return false;
            }
            manager.Tick(suspended: false);
            ATDAccesswayHandleSnapshot terminal = manager.Read(first);
            if (work.AdvanceCount != 2
                || terminal.State != ATDAccesswayRequestState.Succeeded)
            {
                failure = "A completed request did not publish its terminal handle result.";
                return false;
            }

            var suspendedManager = new ATDAccesswayManager();
            var suspendedWork = new FixtureWork(steps: 2);
            ATDAccesswayRequestHandle suspendedHandle =
                suspendedManager.Enqueue(CreateRequest(
                    "farm-prep/tower:41", "a", suspendedWork));
            suspendedManager.Tick(suspended: true);
            if (suspendedWork.AdvanceCount != 0
                || suspendedManager.Read(suspendedHandle).State
                    != ATDAccesswayRequestState.Queued)
            {
                failure =
                    "Interactive suspension advanced derived work or activated its handle.";
                return false;
            }

            ATDAccesswayRequestHandle superseded =
                suspendedManager.Enqueue(CreateRequest(
                    "farm-prep/tower:41", "b", new FixtureWork(1)));
            if (suspendedManager.Read(suspendedHandle).State
                    != ATDAccesswayRequestState.Superseded
                || ReferenceEquals(suspendedHandle, superseded))
            {
                failure =
                    "A changed owner fingerprint did not supersede only its older request.";
                return false;
            }

            suspendedManager.Tick(suspended: false);
            if (suspendedManager.Read(superseded).State
                != ATDAccesswayRequestState.Succeeded)
            {
                failure = "Suspended work did not resume after the interactive gate cleared.";
                return false;
            }

            var resetManager = new ATDAccesswayManager();
            ATDAccesswayRequestHandle resetActive = resetManager.Enqueue(
                CreateRequest("farm-prep/tower:42", "a", new FixtureWork(3)));
            ATDAccesswayRequestHandle resetQueued = resetManager.Enqueue(
                CreateRequest("farm-fill/tower:42", "a", new FixtureWork(1)));
            resetManager.Tick(suspended: false);
            resetManager.Reset("WorldReset");
            if (resetManager.Read(resetActive).State
                    != ATDAccesswayRequestState.Cancelled
                || resetManager.Read(resetQueued).State
                    != ATDAccesswayRequestState.Cancelled)
            {
                failure = "World reset left active or queued access work alive.";
                return false;
            }

            var priorityManager = new ATDAccesswayManager();
            var derivedWork = new FixtureWork(1);
            var interactiveWork = new FixtureWork(1);
            ATDAccesswayRequestHandle derived = priorityManager.Enqueue(
                CreateRequest("farm-prep/tower:43", "a", derivedWork));
            var interactiveRequest = new ATDAccesswayRequest(
                "create-designations/tower:43",
                "a",
                ATDAccesswayRequestKind.CreateDesignations,
                ATDAccesswayPriority.Interactive,
                () => interactiveWork);
            ATDAccesswayRequestHandle interactive =
                priorityManager.Enqueue(interactiveRequest);
            priorityManager.Tick(suspended: false);
            if (interactiveWork.AdvanceCount != 1
                || derivedWork.AdvanceCount != 0
                || priorityManager.Read(interactive).State
                    != ATDAccesswayRequestState.Succeeded
                || priorityManager.Read(derived).State
                    != ATDAccesswayRequestState.Queued)
            {
                failure =
                    "Strict interactive priority did not select interactive work ahead of queued derived work.";
                return false;
            }

            double cancellationNow = 10d;
            ATDAccesswayTerminalDiagnostic? cancellationDiagnostic = null;
            var cancellationManager = new ATDAccesswayManager(
                realtimeSeconds: () => cancellationNow,
                terminalObserver: diagnostic =>
                    cancellationDiagnostic = diagnostic);
            var cancellableWork = new FixtureWork(3);
            ATDAccesswayRequestHandle cancellable = cancellationManager.Enqueue(
                CreateRequest(
                    "farm-prep/tower:44", "a", cancellableWork));
            cancellationNow = 12d;
            cancellationManager.Tick(suspended: false);
            cancellationManager.Cancel(cancellable, "UserCancelled");
            if (cancellationManager.Read(cancellable).State
                != ATDAccesswayRequestState.Active)
            {
                failure =
                    "Active user cancellation did not remain cooperative until the next slice boundary.";
                return false;
            }
            cancellationNow = 13d;
            cancellationManager.Tick(suspended: false);
            if (cancellationManager.Read(cancellable).State
                    != ATDAccesswayRequestState.Cancelled
                || !cancellationDiagnostic.HasValue
                || cancellationDiagnostic.Value.PreviousState
                    != ATDAccesswayRequestState.Active
                || cancellationDiagnostic.Value.Reason
                    != "UserCancelled"
                || cancellationDiagnostic.Value.RetryEligible
                || Math.Abs(
                    cancellationDiagnostic.Value.QueueAgeSeconds - 2d)
                    > 0.0001d
                || Math.Abs(
                    cancellationDiagnostic.Value.ActiveWallSeconds - 1d)
                    > 0.0001d
                || cancellationDiagnostic.Value.VisitedNodes != 2
                || cancellationDiagnostic.Value.PendingNodes != 1
                || Math.Abs(
                    cancellationDiagnostic.Value.ProcessingMilliseconds - 2d)
                    > 0.0001d)
            {
                failure =
                    "Cooperative cancellation did not publish complete request-owned terminal diagnostics.";
                return false;
            }

            AutoDepthDesignation.TickAccesswayManager(gamePaused: false);
            int activeBudget =
                AutoDepthDesignation.GetManagedAccesswaySliceBudgetMilliseconds();
            AutoDepthDesignation.TickAccesswayManager(gamePaused: true);
            int pausedBudget =
                AutoDepthDesignation.GetManagedAccesswaySliceBudgetMilliseconds();
            if (activeBudget
                    != AutoTerrainDesignationsMod.AccessManagerAutomatedFrameBudgetMs
                || pausedBudget
                    != AutoTerrainDesignationsMod.AccessManagerPausedMaxFrameBudgetMs)
            {
                failure =
                    "Managed fixed access budget did not follow the authoritative pause state.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateProgressPresentation(out string failure)
        {
            var workerSnapshot = new ATDAccesswayHandleSnapshot(
                ATDAccesswayRequestState.Active,
                null,
                33380,
                11203,
                processingMilliseconds: 1014d,
                phase: "Searching",
                statusElapsedMilliseconds: 26484d,
                executionBackend: ATDAccesswayExecutionBackend.Worker);
            string worker = AccesswayProgressPresentation.FormatStats(
                workerSnapshot, 100000, 15, 120);
            if (worker.Contains("budget")
                || worker.Contains("processing")
                || !worker.Contains("worker elapsed 26.5/120s"))
            {
                failure = "Worker progress text exposes cooperative timing: "
                    + worker;
                return false;
            }

            var cooperativeSnapshot = new ATDAccesswayHandleSnapshot(
                ATDAccesswayRequestState.Active,
                null,
                123,
                45,
                processingMilliseconds: 2500d,
                phase: "Searching");
            string cooperative = AccesswayProgressPresentation.FormatStats(
                cooperativeSnapshot, 100000, 15, 120);
            if (!cooperative.Contains("budget 15 ms/frame")
                || !cooperative.Contains("processing 2")
                || !cooperative.Contains("/120s"))
            {
                failure = "Cooperative progress text lost slice diagnostics: "
                    + cooperative;
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool ValidateAdaptiveBudget(out string failure)
        {
            var controller = new ATDAdaptiveFrameBudget();
            double timestamp = 0d;
            int budget = controller.BeginFrame(
                paused: false,
                timestamp,
                maximumBudgetMilliseconds: 15);
            if (budget != ATDAdaptiveFrameBudget.MinimumBudgetMilliseconds)
            {
                failure = "Adaptive budget did not start at its 1 ms floor.";
                return false;
            }

            controller.RecordSlice(0.8d);
            for (int frame = 0; frame < 120; frame++)
            {
                timestamp += 16d;
                budget = controller.BeginFrame(
                    paused: false,
                    timestamp,
                    maximumBudgetMilliseconds: 15);
                controller.RecordSlice(budget * 0.8d);
            }
            if (budget != 15)
            {
                failure =
                    "Sustained healthy running frames did not recover to the 15 ms cap.";
                return false;
            }

            timestamp += 40d;
            int reducedRunning = controller.BeginFrame(
                paused: false,
                timestamp,
                maximumBudgetMilliseconds: 15);
            if (reducedRunning >= budget
                || controller.Snapshot.Action
                    != ATDAdaptiveBudgetAction.ReducedForSlowFrame)
            {
                failure =
                    "A slow running frame did not promptly reduce the adaptive budget.";
                return false;
            }

            timestamp += 1d;
            budget = controller.BeginFrame(
                paused: true,
                timestamp,
                maximumBudgetMilliseconds: 30);
            controller.RecordSlice(0.8d);
            for (int frame = 0; frame < 180; frame++)
            {
                timestamp += 30d;
                budget = controller.BeginFrame(
                    paused: true,
                    timestamp,
                    maximumBudgetMilliseconds: 30);
                controller.RecordSlice(budget * 0.8d);
            }
            if (budget != 30)
            {
                failure =
                    "Sustained healthy paused frames did not recover to the 30 ms cap.";
                return false;
            }

            timestamp += 70d;
            int reducedPaused = controller.BeginFrame(
                paused: true,
                timestamp,
                maximumBudgetMilliseconds: 30);
            if (reducedPaused >= budget)
            {
                failure =
                    "A slow paused frame did not reduce the adaptive budget.";
                return false;
            }

            controller.RecordSlice(100d);
            timestamp += 30d;
            int reducedForOverrun = controller.BeginFrame(
                paused: true,
                timestamp,
                maximumBudgetMilliseconds: 30);
            if (reducedForOverrun >= reducedPaused
                || controller.Snapshot.Action
                    != ATDAdaptiveBudgetAction.ReducedForSliceOverrun)
            {
                failure =
                    "An access slice overrun did not independently reduce the adaptive budget.";
                return false;
            }

            controller.RecordSlice(0.5d);
            timestamp += 500d;
            int afterDiscontinuity = controller.BeginFrame(
                paused: true,
                timestamp,
                maximumBudgetMilliseconds: 30);
            if (afterDiscontinuity != reducedForOverrun
                || controller.Snapshot.Action
                    != ATDAdaptiveBudgetAction.Discontinuity)
            {
                failure =
                    "A long callback gap incorrectly trained the adaptive budget.";
                return false;
            }

            budget = afterDiscontinuity;
            for (int frame = 0; frame < 10 && budget > 1; frame++)
            {
                controller.RecordSlice(budget * 0.8d);
                timestamp += 70d;
                budget = controller.BeginFrame(
                    paused: true,
                    timestamp,
                    maximumBudgetMilliseconds: 30);
            }
            if (budget != ATDAdaptiveFrameBudget.MinimumBudgetMilliseconds)
            {
                failure =
                    "Repeated stress did not reduce the adaptive budget to its 1 ms floor.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateManagerHardening(out string failure)
        {
            double nowSeconds = 100d;
            var staleManager = new ATDAccesswayManager(
                maxPendingRequests: 4,
                realtimeSeconds: () => nowSeconds);
            var staleWork = new FixtureWork(steps: 3);
            ATDAccesswayValidationResult validation =
                ATDAccesswayValidationResult.Current();
            var staleRequest = new ATDAccesswayRequest(
                "farm-prep/tower:validation",
                "a",
                ATDAccesswayRequestKind.FarmingPreparation,
                ATDAccesswayPriority.Derived,
                () => staleWork,
                () => validation);
            ATDAccesswayRequestHandle staleHandle =
                staleManager.Enqueue(staleRequest);
            staleManager.Tick(suspended: false);
            validation = ATDAccesswayValidationResult.Stale(
                "FixtureInputChanged");
            nowSeconds += 0.1d;
            staleManager.Tick(suspended: false);
            ATDAccesswayHandleSnapshot staleSnapshot =
                staleManager.Read(staleHandle);
            if (staleWork.AdvanceCount != 1
                || staleSnapshot.State != ATDAccesswayRequestState.Stale
                || staleSnapshot.Result == null
                || staleSnapshot.Result.Reason != "FixtureInputChanged"
                || !staleSnapshot.Result.RetryEligible)
            {
                failure =
                    "Live validation did not stop stale work before its next slice with retryable diagnostics.";
                return false;
            }

            ATDAccesswayValidationResult postCommitValidation =
                ATDAccesswayValidationResult.Current();
            var postCommitManager = new ATDAccesswayManager();
            var postCommitWork = new FixtureWork(
                steps: 3,
                postCommitAfterAdvance: 1);
            ATDAccesswayRequestHandle postCommitHandle =
                postCommitManager.Enqueue(new ATDAccesswayRequest(
                    "farm-prep/tower:post-commit",
                    "a",
                    ATDAccesswayRequestKind.FarmingPreparation,
                    ATDAccesswayPriority.Derived,
                    () => postCommitWork,
                    () => postCommitValidation));
            postCommitManager.Tick(suspended: false);
            postCommitValidation = ATDAccesswayValidationResult.Stale(
                "SelfAuthoredDesignationChanged");
            postCommitManager.Tick(suspended: false);
            postCommitManager.Tick(suspended: false);
            if (postCommitManager.Read(postCommitHandle).State
                    != ATDAccesswayRequestState.Succeeded
                || postCommitWork.AdvanceCount != 3)
            {
                failure =
                    "Post-commit capture finalization was invalidated by the request's self-authored world mutation.";
                return false;
            }

            int ownerGoneFactories = 0;
            var ownerGoneRequest = new ATDAccesswayRequest(
                "farm-prep/tower:gone",
                "a",
                ATDAccesswayRequestKind.FarmingPreparation,
                ATDAccesswayPriority.Derived,
                () =>
                {
                    ownerGoneFactories++;
                    return new FixtureWork(1);
                },
                () => ATDAccesswayValidationResult.OwnerGone(
                    "FixtureOwnerGone"));
            ATDAccesswayRequestHandle ownerGoneHandle =
                staleManager.Enqueue(ownerGoneRequest);
            staleManager.Tick(suspended: false);
            ATDAccesswayHandleSnapshot ownerGoneSnapshot =
                staleManager.Read(ownerGoneHandle);
            if (ownerGoneFactories != 0
                || ownerGoneSnapshot.State
                    != ATDAccesswayRequestState.Cancelled
                || ownerGoneSnapshot.Result?.RetryEligible != false)
            {
                failure =
                    "A vanished queued owner created work or remained retryable.";
                return false;
            }

            ATDAccesswayManagerHealthSnapshot staleHealth =
                staleManager.ReadHealth();
            if (staleHealth.StaleRequests != 1
                || staleHealth.CompletedRequests != 2)
            {
                failure =
                    "Manager health counters did not include stale and owner-gone terminals.";
                return false;
            }

            var boundedManager = new ATDAccesswayManager(
                maxPendingRequests: 2,
                realtimeSeconds: () => nowSeconds);
            ATDAccesswayRequest firstRequest = CreateRequest(
                "farm-prep/tower:queue-1",
                "a",
                new FixtureWork(1));
            ATDAccesswayRequest secondRequest = CreateRequest(
                "farm-prep/tower:queue-2",
                "a",
                new FixtureWork(1));
            ATDAccesswayRequest thirdRequest = CreateRequest(
                "farm-prep/tower:queue-3",
                "a",
                new FixtureWork(1));
            ATDAccesswayRequestHandle first =
                boundedManager.Enqueue(firstRequest);
            ATDAccesswayRequestHandle second =
                boundedManager.Enqueue(secondRequest);
            ATDAccesswayRequestHandle third =
                boundedManager.Enqueue(thirdRequest);
            if (boundedManager.Read(first).State
                    != ATDAccesswayRequestState.Failed
                || boundedManager.Read(first).Result?.Reason
                    != "QueueOverflow"
                || boundedManager.Read(second).State
                    != ATDAccesswayRequestState.Queued
                || boundedManager.Read(third).State
                    != ATDAccesswayRequestState.Queued)
            {
                failure =
                    "Queue backpressure did not evict the oldest equal-priority derived request.";
                return false;
            }

            var interactiveRequest = new ATDAccesswayRequest(
                "create-designations/tower:queue-4",
                "a",
                ATDAccesswayRequestKind.CreateDesignations,
                ATDAccesswayPriority.Interactive,
                () => new FixtureWork(1));
            ATDAccesswayRequestHandle interactive =
                boundedManager.Enqueue(interactiveRequest);
            if (boundedManager.Read(second).State
                    != ATDAccesswayRequestState.Failed
                || boundedManager.Read(interactive).State
                    != ATDAccesswayRequestState.Queued)
            {
                failure =
                    "A newest interactive request was not preserved by queue backpressure.";
                return false;
            }

            ATDAccesswayRequestHandle duplicate =
                boundedManager.Enqueue(thirdRequest);
            if (!ReferenceEquals(third, duplicate))
            {
                failure =
                    "Queue pressure prevented an unchanged request from coalescing.";
                return false;
            }

            nowSeconds += 5d;
            ATDAccesswayManagerHealthSnapshot boundedHealth =
                boundedManager.ReadHealth();
            if (boundedHealth.QueueDepth != 2
                || boundedHealth.DroppedRequests != 2
                || boundedHealth.CoalescedRequests != 1
                || boundedHealth.OldestQueueAgeSeconds < 4.999d)
            {
                failure =
                    "Queue health diagnostics did not report bounded depth, eviction, coalescing, or age.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static ATDAccesswayRequest CreateRequest(
            string owner,
            string fingerprint,
            FixtureWork work)
            => new ATDAccesswayRequest(
                owner,
                fingerprint,
                owner.StartsWith("farm-fill/", StringComparison.Ordinal)
                    ? ATDAccesswayRequestKind.FarmingFilling
                    : ATDAccesswayRequestKind.FarmingPreparation,
                ATDAccesswayPriority.Derived,
                () => work);

        private sealed class FixtureWork : IATDAccesswayManagedWork
        {
            private readonly int m_steps;
            private readonly int m_postCommitAfterAdvance;
            private string? m_cancellationReason;

            public FixtureWork(
                int steps,
                int postCommitAfterAdvance = int.MaxValue)
            {
                m_steps = steps;
                m_postCommitAfterAdvance = postCommitAfterAdvance;
            }

            public int AdvanceCount { get; private set; }
            public int VisitedNodes => AdvanceCount;
            public int PendingNodes => Math.Max(0, m_steps - AdvanceCount);
            public string Phase => "Fixture";
            public bool IsPostCommit
                => AdvanceCount >= m_postCommitAfterAdvance;
            public double ProcessingMilliseconds => AdvanceCount;
            public double StatusElapsedMilliseconds => AdvanceCount;
            public ATDAccesswayExecutionBackend ExecutionBackend
                => ATDAccesswayExecutionBackend.Cooperative;
            public Tile2i? FocusTile => null;

            public bool Advance()
            {
                AdvanceCount++;
                if (m_cancellationReason != null)
                    return false;
                return AdvanceCount < m_steps;
            }

            public void RequestCancellation(string reason)
                => m_cancellationReason = reason;

            public ATDAccesswayRequestResult GetTerminalResult()
                => m_cancellationReason == null
                    ? ATDAccesswayRequestResult.Succeeded()
                    : ATDAccesswayRequestResult.Cancelled(
                        m_cancellationReason);

            public void Dispose() { }
        }
    }
}

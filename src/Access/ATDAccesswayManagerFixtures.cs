using System;

namespace AutoTerrainDesignations.Access
{
    internal static class ATDAccesswayManagerFixtures
    {
        internal static bool ValidateAll(out string failure)
        {
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

            var cancellationManager = new ATDAccesswayManager();
            var cancellableWork = new FixtureWork(3);
            ATDAccesswayRequestHandle cancellable = cancellationManager.Enqueue(
                CreateRequest(
                    "farm-prep/tower:44", "a", cancellableWork));
            cancellationManager.Tick(suspended: false);
            cancellationManager.Cancel(cancellable, "UserCancelled");
            if (cancellationManager.Read(cancellable).State
                != ATDAccesswayRequestState.Active)
            {
                failure =
                    "Active user cancellation did not remain cooperative until the next slice boundary.";
                return false;
            }
            cancellationManager.Tick(suspended: false);
            if (cancellationManager.Read(cancellable).State
                != ATDAccesswayRequestState.Cancelled)
            {
                failure =
                    "Cooperative cancellation did not publish a terminal cancelled result.";
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
                    "Managed access budget did not follow the authoritative pause state after work had started.";
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
            private string? m_cancellationReason;

            public FixtureWork(int steps)
            {
                m_steps = steps;
            }

            public int AdvanceCount { get; private set; }
            public int VisitedNodes => AdvanceCount;
            public int PendingNodes => Math.Max(0, m_steps - AdvanceCount);
            public double ProcessingMilliseconds => AdvanceCount;

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

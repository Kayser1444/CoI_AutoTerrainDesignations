using System;
using System.Collections.Generic;
using System.Diagnostics;
using AutoTerrainDesignations.Access.V2;
using Mafi;

namespace AutoTerrainDesignations.Access.Worker
{
    /// <summary>
    /// The canonical value-only preparation/search/materialization pipeline.
    /// Execution adapters choose the thread and publish progress; route
    /// semantics remain owned by this module.
    /// </summary>
    internal static class AccessSearchExecutionCore
    {
        internal static AccessSearchExecutionOutcome CreateCancelled(
            AccessPathRequest request,
            string phase)
        {
            var timer = Stopwatch.StartNew();
            return Cancelled(request, phase, timer, 0, 0, 0d);
        }

        internal static AccessSearchExecutionOutcome Execute(
            AccessPathRequest request,
            IAccessSearchExecutionControl? control = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            Stopwatch total = Stopwatch.StartNew();
            control?.Publish("Preparing search workspace", string.Empty, 0, 0);
            var workspace = new AccessSearchWorkspace(request.Snapshot);
            AccessPathSearch.AccessPathSearchSessionBuilder builder =
                AccessPathSearch.CreateSessionBuilder(request, workspace);
            int timeoutSeconds = Math.Max(
                1, request.Snapshot.Policy.SearchTimeoutSeconds);

            while (!builder.IsComplete)
            {
                if (IsCancelled(control))
                    return Cancelled(
                        request, builder.Phase, total, 0, 0,
                        builder.Diagnostics.TotalMilliseconds);
                if (total.Elapsed.TotalSeconds >= timeoutSeconds)
                    return TimedOut(
                        request, builder.Phase, total, 0, 0,
                        builder.Diagnostics.TotalMilliseconds);
                builder.Advance(64);
                control?.Publish(builder.Phase, string.Empty, 0, 0);
            }

            AccessPathSearch.AccessPathSearchSession session = builder.Session;
            if (control?.CaptureOverlay == true)
                session.NodeExplored = control.RecordNode;
            if (control?.CaptureExpansionTrace == true)
            {
                session.ExpansionTraced = control.RecordExpansion;
                session.GroundExpansionOutcomeTraced =
                    control.RecordGroundExpansionOutcome;
            }
            double preparationMilliseconds = total.Elapsed.TotalMilliseconds;
            while (!session.IsComplete)
            {
                if (IsCancelled(control))
                    return Cancelled(
                        request, session.Phase, total,
                        session.VisitedNodes, session.PendingNodes,
                        preparationMilliseconds,
                        session.Rejections, session.Diagnostics);
                if (total.Elapsed.TotalSeconds >= timeoutSeconds)
                    return TimedOut(
                        request, session.Phase, total,
                        session.VisitedNodes, session.PendingNodes,
                        preparationMilliseconds,
                        session.Rejections, session.Diagnostics);

                // A short shared deadline gives nested V1/V2 continuations a
                // bounded opportunity to return so cancellation can be
                // refreshed without changing search order.
                if (control == null)
                {
                    session.Step(int.MaxValue);
                }
                else
                {
                    var budget = new AccessSearchSliceBudget(25);
                    session.Step(1, budget);
                }
                control?.Publish(
                    session.Phase, string.Empty,
                    session.VisitedNodes, session.PendingNodes);
            }

            AccessSearchResult result = session.Result;
            double searchMilliseconds = Math.Max(
                0d, total.Elapsed.TotalMilliseconds - preparationMilliseconds);
            if (IsCancelled(control))
                return Cancelled(
                    request, "Search complete", total,
                    session.VisitedNodes, session.PendingNodes,
                    preparationMilliseconds,
                    session.Rejections, session.Diagnostics);

            control?.Publish(
                "Materializing access plan", string.Empty,
                session.VisitedNodes, session.PendingNodes);
            Stopwatch materialization = Stopwatch.StartNew();
            AccessDesignationPlan plan = result.Success
                ? AccessPathMaterializer.Materialize(workspace, result)
                : AccessDesignationPlan.Invalid(
                    result.FailureReason, result.StartOrigin);
            materialization.Stop();
            total.Stop();
            var timing = new AccessReplayPhaseTiming(
                preparationMilliseconds,
                searchMilliseconds,
                materialization.Elapsed.TotalMilliseconds);
            return AccessSearchExecutionOutcome.Completed(
                result, plan, timing, total.Elapsed, session.PendingNodes);
        }

        private static bool IsCancelled(IAccessSearchExecutionControl? control)
            => control?.CancellationRequested == true;

        private static AccessSearchExecutionOutcome Cancelled(
            AccessPathRequest request,
            string phase,
            Stopwatch total,
            int visited,
            int pending,
            double preparationMilliseconds,
            IReadOnlyDictionary<string, int>? rejections = null,
            AccessSearchDiagnostics? diagnostics = null)
            => Interrupted(
                request, "SearchCancelled", phase, total, visited, pending,
                preparationMilliseconds, rejections, diagnostics);

        private static AccessSearchExecutionOutcome TimedOut(
            AccessPathRequest request,
            string phase,
            Stopwatch total,
            int visited,
            int pending,
            double preparationMilliseconds,
            IReadOnlyDictionary<string, int>? rejections = null,
            AccessSearchDiagnostics? diagnostics = null)
            => Interrupted(
                request, "SearchTimeLimit", phase, total, visited, pending,
                preparationMilliseconds, rejections, diagnostics);

        private static AccessSearchExecutionOutcome Interrupted(
            AccessPathRequest request,
            string reason,
            string phase,
            Stopwatch total,
            int visited,
            int pending,
            double preparationMilliseconds,
            IReadOnlyDictionary<string, int>? rejections,
            AccessSearchDiagnostics? diagnostics)
        {
            total.Stop();
            var result = new AccessSearchResult(
                false,
                reason,
                request.Start.Nodes.Count > 0
                    ? request.Start.Nodes[0]
                    : default,
                Array.Empty<AccessSearchNode>(),
                0f,
                visited,
                rejections ?? new Dictionary<string, int>(StringComparer.Ordinal),
                diagnostics ?? new AccessSearchDiagnostics());
            return AccessSearchExecutionOutcome.Interrupted(
                result,
                AccessDesignationPlan.Invalid(reason, result.StartOrigin),
                new AccessReplayPhaseTiming(
                    preparationMilliseconds,
                    Math.Max(
                        0d,
                        total.Elapsed.TotalMilliseconds
                            - preparationMilliseconds),
                    0d),
                total.Elapsed,
                pending,
                phase);
        }
    }

    internal interface IAccessSearchExecutionControl
    {
        bool CancellationRequested { get; }
        bool CaptureOverlay { get; }
        bool CaptureExpansionTrace { get; }
        void Publish(string phase, string subphase, int visited, int pending);
        void RecordNode(Tile2i tile, int height2, bool isGround, int? priority);
        void RecordExpansion(AccessV2ExpansionTrace expansion);
        void RecordGroundExpansionOutcome(
            AccessV2GroundExpansionOutcomeTrace outcome);
    }

    internal sealed class AccessSearchExecutionOutcome
    {
        public AccessSearchResult SearchResult { get; }
        public AccessDesignationPlan Plan { get; }
        public AccessReplayPhaseTiming Timing { get; }
        public TimeSpan ProcessingTime { get; }
        public int PendingNodes { get; }
        public string TerminalPhase { get; }

        private AccessSearchExecutionOutcome(
            AccessSearchResult searchResult,
            AccessDesignationPlan plan,
            AccessReplayPhaseTiming timing,
            TimeSpan processingTime,
            int pendingNodes,
            string terminalPhase)
        {
            SearchResult = searchResult;
            Plan = plan;
            Timing = timing;
            ProcessingTime = processingTime;
            PendingNodes = pendingNodes;
            TerminalPhase = terminalPhase;
        }

        internal static AccessSearchExecutionOutcome Completed(
            AccessSearchResult result,
            AccessDesignationPlan plan,
            AccessReplayPhaseTiming timing,
            TimeSpan processingTime,
            int pendingNodes)
            => new AccessSearchExecutionOutcome(
                result, plan, timing, processingTime, pendingNodes,
                "Completed");

        internal static AccessSearchExecutionOutcome Interrupted(
            AccessSearchResult result,
            AccessDesignationPlan plan,
            AccessReplayPhaseTiming timing,
            TimeSpan processingTime,
            int pendingNodes,
            string terminalPhase)
            => new AccessSearchExecutionOutcome(
                result, plan, timing, processingTime, pendingNodes,
                terminalPhase);
    }
}

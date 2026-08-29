// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
using System;
using System.Diagnostics;
using AutoTerrainDesignations.Access;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.Ui;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static ATDAccesswayManager? s_accesswayManager;
        private static bool s_accesswayManagerSuspendedForSave;
        private static bool s_accesswayManagerGamePaused;
        private static int s_accesswayManagerLastLoggedBudget = -1;
        private static double s_accesswayManagerNextHealthLogSeconds;
        private static bool s_accesswayManagerToastVisible;
        private static long s_accesswayManagerToastRequestId;
        private static bool s_accesswayManagerToastCaptureMode;
        private static Label? s_accesswayManagerProgressLabel;
        private static Label? s_accesswayManagerStatsLabel;
        private static UiContext? s_uiContext;

        private static void InitializeAccesswayManagerRuntime()
        {
            Access.Worker.AccessSearchWorker.Shared.SetCurrentWorld(
                CurrentWorldGeneration);
            s_accesswayManager?.Reset("WorldReinitialized");
            s_accesswayManager = new ATDAccesswayManager(
                terminalObserver: LogAccesswayTerminalDiagnostic);
            if (!AccessSearchFixtureGate.EnsureInitialized(
                    out string fixtureFailure))
            {
                s_log.Warning(
                    "[ATD Access Manager] deterministic access fixtures failed; "
                    + "managed access preparation will fail closed. reason="
                    + fixtureFailure);
            }
            s_accesswayManagerSuspendedForSave = false;
            s_accesswayManagerGamePaused = false;
            s_accesswayManagerLastLoggedBudget = -1;
            s_accesswayManagerNextHealthLogSeconds = 0d;
            s_accesswayManagerToastVisible = false;
            s_accesswayManagerToastRequestId = 0;
            s_accesswayManagerToastCaptureMode = false;
            s_accesswayManagerProgressLabel = null;
            s_accesswayManagerStatsLabel = null;
        }

        internal static ATDAccesswayRequestHandle EnqueueAccesswayRequest(
            ATDAccesswayRequest request)
        {
            if (s_accesswayManager == null)
                throw new InvalidOperationException(
                    "Accessway manager is unavailable for this world.");
            ATDAccesswayRequestHandle handle =
                s_accesswayManager.Enqueue(request);
            ATDAccesswayHandleSnapshot snapshot =
                s_accesswayManager.Read(handle);
            LogExperimentalAccessDebug(
                $"[ATD Access Manager] id={handle.RequestId} "
                + $"owner={handle.OwnerKey} state={snapshot.State} "
                + $"priority={handle.Priority} kind={handle.Kind}"
                + (snapshot.Result == null
                    ? string.Empty
                    : $" reason={snapshot.Result.Reason} "
                        + $"retryEligible={snapshot.Result.RetryEligible}"));
            return handle;
        }

        internal static ATDAccesswayHandleSnapshot ReadAccesswayRequest(
            ATDAccesswayRequestHandle handle)
            => s_accesswayManager?.Read(handle)
                ?? new ATDAccesswayHandleSnapshot(
                    ATDAccesswayRequestState.Cancelled,
                    ATDAccesswayRequestResult.Cancelled("WorldUnavailable"),
                    0,
                    0,
                    0d);

        internal static void CancelAccesswayRequest(
            ATDAccesswayRequestHandle? handle,
            string reason)
        {
            if (handle != null)
                s_accesswayManager?.Cancel(handle, reason);
        }

        internal static void TickAccesswayManager(bool gamePaused)
        {
            s_accesswayManagerGamePaused = gamePaused;
            ATDAccesswayManager? manager = s_accesswayManager;
            if (manager == null || s_accesswayManagerSuspendedForSave)
                return;

            // Create Designations owns the manager only while its accessway
            // request is queued/active. Its scan and commit phases remain
            // exclusive, but the manager must keep ticking once the request
            // has been handed off or the interactive operation would deadlock.
            bool suspendedForInteractive = s_createDesignationsOperationActive
                && s_createDesignationsAccessRequest == null;
            manager.Tick(suspendedForInteractive);
            if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Debug))
            {
                int budget = GetManagedAccesswaySliceBudgetMilliseconds();
                if (budget != s_accesswayManagerLastLoggedBudget
                    && manager.TryReadActive(out _, out _))
                {
                    s_accesswayManagerLastLoggedBudget = budget;
                    LogExperimentalAccessDebug(
                        $"[ATD Access Manager] pause={gamePaused} "
                        + $"sliceBudgetMs={budget} scheduling=fixed");
                }
                double nowSeconds = Stopwatch.GetTimestamp()
                    / (double)Stopwatch.Frequency;
                if (nowSeconds >= s_accesswayManagerNextHealthLogSeconds)
                {
                    s_accesswayManagerNextHealthLogSeconds =
                        nowSeconds + 10d;
                    ATDAccesswayManagerHealthSnapshot health =
                        manager.ReadHealth();
                    if (health.ActiveRequestId != 0 || health.QueueDepth > 0)
                    {
                        LogExperimentalAccessDebug(
                            "[ATD Access Manager Health] "
                            + $"active={health.ActiveRequestId} "
                            + $"activeWallSeconds={health.ActiveWallSeconds:0.##} "
                            + $"processingMs={health.ActiveProcessingMilliseconds:0.##} "
                            + $"visited={health.ActiveVisitedNodes} "
                            + $"pending={health.ActivePendingNodes} "
                            + $"queued={health.QueueDepth} "
                            + $"oldestQueueSeconds={health.OldestQueueAgeSeconds:0.##} "
                            + $"coalesced={health.CoalescedRequests} "
                            + $"superseded={health.SupersededRequests} "
                            + $"stale={health.StaleRequests} "
                            + $"dropped={health.DroppedRequests} "
                            + $"completed={health.CompletedRequests}");
                    }
                }
            }
            UpdateAccesswayManagerToast(manager, suspendedForInteractive);
        }

        private static void LogAccesswayTerminalDiagnostic(
            ATDAccesswayTerminalDiagnostic diagnostic)
        {
            if (diagnostic.State == ATDAccesswayRequestState.Succeeded)
                return;
            string work = diagnostic.WorkFingerprint.Length <= 160
                ? diagnostic.WorkFingerprint
                : diagnostic.WorkFingerprint.Substring(0, 160)
                    + $"...({diagnostic.WorkFingerprint.Length} chars)";
            LogInfo(
                "[ATD Access Manager Terminal] "
                + $"id={diagnostic.RequestId} "
                + $"owner={diagnostic.OwnerKey} "
                + $"kind={diagnostic.Kind} "
                + $"priority={diagnostic.Priority} "
                + $"phase={diagnostic.PreviousState} "
                + $"state={diagnostic.State} "
                + $"reason={diagnostic.Reason} "
                + $"queueAgeSeconds={diagnostic.QueueAgeSeconds:0.##} "
                + $"activeWallSeconds={diagnostic.ActiveWallSeconds:0.##} "
                + $"processingMs={diagnostic.ProcessingMilliseconds:0.##} "
                + $"visited={diagnostic.VisitedNodes} "
                + $"pending={diagnostic.PendingNodes} "
                + $"retryEligible={diagnostic.RetryEligible} "
                + $"work={work}");
        }

        internal static int GetManagedAccesswaySliceBudgetMilliseconds()
            // The adaptive controller is intentionally parked until ticket 10.
            => s_accesswayManagerGamePaused
                ? AutoTerrainDesignationsMod.AccessManagerPausedMaxFrameBudgetMs
                : AutoTerrainDesignationsMod.AccessManagerAutomatedFrameBudgetMs;

        internal static void PrepareAccesswayManagerForSave()
        {
            s_accesswayManagerSuspendedForSave = true;
            HideAccesswayManagerToast();
        }

        internal static void ResumeAccesswayManagerAfterSave()
            => s_accesswayManagerSuspendedForSave = false;

        private static void ResetAccesswayManagerRuntime(string reason)
        {
            s_accesswayManager?.Reset(reason);
            s_accesswayManager = null;
            s_accesswayManagerSuspendedForSave = false;
            s_accesswayManagerGamePaused = false;
            s_accesswayManagerLastLoggedBudget = -1;
            s_accesswayManagerNextHealthLogSeconds = 0d;
            HideAccesswayManagerToast();
        }

        private static void UpdateAccesswayManagerToast(
            ATDAccesswayManager manager,
            bool suspendedForInteractive)
        {
            if (suspendedForInteractive)
            {
                HideAccesswayManagerToast();
                return;
            }
            if (!manager.TryReadActive(
                    out ATDAccesswayRequestHandle? handle,
                    out ATDAccesswayHandleSnapshot snapshot)
                || handle == null
                || snapshot.ProcessingMilliseconds < 250d
                || s_uiRoot == null)
            {
                if (handle == null)
                    HideAccesswayManagerToast();
                return;
            }

            try
            {
                string workType = handle.Kind switch
                {
                    ATDAccesswayRequestKind.FarmingFilling
                        => "farming filling access",
                    ATDAccesswayRequestKind.FarmingPreparation
                        => "farming preparation access",
                    ATDAccesswayRequestKind.CreateDesignations
                        => "interactive mining access",
                    ATDAccesswayRequestKind.PlannedTower
                        => "planned tower access",
                    ATDAccesswayRequestKind.ConstructionLeveling
                        => "construction leveling access",
                    _ => "terrain access"
                };
                bool captureMode = snapshot.Phase.StartsWith(
                    "Recording access replay", StringComparison.Ordinal)
                    || snapshot.Phase.StartsWith(
                        "Cancelling access replay", StringComparison.Ordinal);
                string stopWork = captureMode
                    ? "Abort replay capture"
                    : handle.Kind switch
                {
                    ATDAccesswayRequestKind.CreateDesignations
                        => "Stop interactive mining access",
                    ATDAccesswayRequestKind.PlannedTower
                        => "Stop planned tower access",
                    _ => "Stop automatic farming access"
                };
                var progressText = new LocStrFormatted(
                    $"[ATD] {snapshot.Phase}; finding {workType}");
                var statsText = new LocStrFormatted(
                    AccesswayProgressPresentation.FormatStats(
                        snapshot,
                        AutoTerrainDesignationsMod.AccessMaxVisitedNodes,
                        GetManagedAccesswaySliceBudgetMilliseconds(),
                        AutoTerrainDesignationsMod.AccessSearchTimeoutSeconds));
                if (s_terrainAnalysisToastHidden)
                    return;

                var notification = s_uiRoot.ToastNotifProvider.m_notification;
                if (!s_accesswayManagerToastVisible
                    || s_accesswayManagerToastRequestId != handle.RequestId
                    || s_accesswayManagerToastCaptureMode != captureMode
                    || s_accesswayManagerProgressLabel == null
                    || s_accesswayManagerStatsLabel == null)
                {
                    notification.ShowGeneral(
                        new LocStrFormatted(
                            "[ATD] Terrain analysis in progress"),
                        showForever: true);
                    s_accesswayManagerProgressLabel =
                        new Label(progressText)
                            .FontSize(16)
                            .NoTextWrap()
                            .Width(TerrainAnalysisToastTextWidthPixels.px());
                    s_accesswayManagerStatsLabel =
                        new Label(statsText)
                            .FontSize(14)
                            .NoTextWrap()
                            .Width(TerrainAnalysisToastTextWidthPixels.px());
                    var progressColumn = new Column()
                        .Width(TerrainAnalysisToastTextWidthPixels.px());
                    progressColumn.Add(
                        s_accesswayManagerProgressLabel,
                        s_accesswayManagerStatsLabel);
                    notification.Body.SetChildren(
                        progressColumn,
                        new ButtonIcon(
                            Button.General,
                            "Assets/Unity/UserInterface/Toolbar/MapPin.svg",
                            () =>
                            {
                                if (handle.FocusTile.HasValue)
                                {
                                    s_uiContext?.CameraController.PanTo(
                                        handle.FocusTile.Value.CenterTile2f);
                                }
                            })
                            .IconSize(16.px())
                            .Padding(2.pt())
                            .MarginLeft(8.pt())
                            .Tooltip(
                                AtdLocalization
                                    .ZoomToAccessActionTooltip.AsFormatted),
                        new ButtonText(
                            Button.General,
                            new LocStrFormatted(stopWork),
                            () => manager.Cancel(handle, "UserCancelled"))
                            .MarginLeft(8.pt()),
                        new ButtonText(
                            Button.General,
                            new LocStrFormatted("Hide"),
                            HideAccesswayManagerToastUntilComplete)
                            .MarginLeft(8.pt()));
                    s_accesswayManagerToastRequestId = handle.RequestId;
                    s_accesswayManagerToastCaptureMode = captureMode;
                    s_accesswayManagerToastVisible = true;
                }
                else
                {
                    s_accesswayManagerProgressLabel.Value(progressText);
                    s_accesswayManagerStatsLabel.Value(statsText);
                }
            }
            catch (Exception ex)
            {
                LogExperimentalAccessDebug(
                    "[ATD Access Manager] progress toast failed: "
                    + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void HideAccesswayManagerToast()
        {
            TryResetTerrainAnalysisToastHidden();
            if (!s_accesswayManagerToastVisible)
                return;
            try
            {
                s_uiRoot?.ToastNotifProvider.m_notification.Hide();
            }
            catch
            {
            }
            s_accesswayManagerToastVisible = false;
            s_accesswayManagerToastRequestId = 0;
            s_accesswayManagerToastCaptureMode = false;
            s_accesswayManagerProgressLabel = null;
        }

        private static void HideAccesswayManagerToastUntilComplete()
        {
            HideTerrainAnalysisToastForCurrentSearch();
            try
            {
                s_uiRoot?.ToastNotifProvider.m_notification.Hide();
            }
            catch
            {
            }
        }
    }
}

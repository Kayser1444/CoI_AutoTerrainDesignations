using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using AutoTerrainDesignations.Access;
using AutoTerrainDesignations.Access.Worker;
using AutoTerrainDesignations.Mining;
using AutoTerrainDesignations.Planning;
using Mafi;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private sealed class MiningExecutionResult
        {
            public MiningRequest? Request;
            public MiningPlan? Plan;
            public string Failure = string.Empty;
            public bool Cancelled;
            public string PolicyKey = string.Empty;
        }

        private static string MiningPolicyKey(IAreaManagingTower tower)
        {
            ATDTowerSettings settings = GetOrCreateTowerSettings(tower);
            int level = settings.OrePurityLevel;
            return string.Join("|", tower.Area.BoundingBoxMin, tower.Area.BoundingBoxMax,
                GetSelectedOre(tower)?.Id.Value, settings.MaxHeightDiff,
                settings.MaxLayersToExcavate, settings.MaxDepthToDigTo,
                settings.CorridorClearance, level, s_minOreHeightByLevel[level],
                s_minOrePurityByLevel[level], s_minBottomOreDensityByLevel[level],
                s_minComponentSizeByLevel[level], AutoTerrainDesignationsMod.BottomFlatteningEnabled,
                AutoTerrainDesignationsMod.BottomFlatteningStrength,
                FilterOreSpikes,
                AutoTerrainDesignationsMod.AccessPlanningSettingsFingerprint);
        }

        private static IEnumerator RunManagedMining(IAreaManagingTower tower,
            MiningExecutionResult output)
        {
            int world = CurrentWorldGeneration;
            string policyKey = MiningPolicyKey(tower);
            output.PolicyKey = policyKey;
            var request = new ATDAccesswayRequest(
                BuildCreateDesignationsAccessOwnerKey(tower, "mining"), policyKey,
                ATDAccesswayRequestKind.CreateDesignations, ATDAccesswayPriority.Interactive,
                () => new ATDAccesswayCoroutineWork(
                    control => GuardMiningCapture(tower, control, output),
                    () => output.Plan != null
                        ? ATDAccesswayRequestResult.Succeeded(output)
                        : ATDAccesswayRequestResult.Failed(output.Failure),
                    GetManagedAccesswaySliceBudgetMilliseconds),
                () => !IsWorldGenerationActive(world) || (tower is IEntity entity && entity.IsDestroyed)
                    ? ATDAccesswayValidationResult.OwnerGone("MiningOwnerGone")
                    : MiningPolicyKey(tower) != policyKey
                        ? ATDAccesswayValidationResult.Stale("MiningSettingsChanged")
                        : ATDAccesswayValidationResult.Current(),
                focusTile: tower.Area.BoundingBoxCenter);
            ATDAccesswayRequestHandle handle = EnqueueAccesswayRequest(request);
            s_createDesignationsAccessRequest = handle;
            bool finished = false;
            try
            {
                ATDAccesswayHandleSnapshot snapshot;
                do
                {
                    snapshot = ReadAccesswayRequest(handle);
                    if (!snapshot.IsTerminal) yield return null;
                } while (!snapshot.IsTerminal);
                finished = true;
                if (snapshot.State != ATDAccesswayRequestState.Succeeded)
                {
                    output.Plan = null;
                    output.Cancelled = snapshot.State != ATDAccesswayRequestState.Failed;
                    output.Failure = snapshot.Result?.Reason ?? "MiningFailed";
                    s_log.Warning("[ATD Mining] " + output.Failure);
                }
            }
            finally
            {
                if (!finished) CancelAccesswayRequest(handle, "MiningSuperseded");
                if (ReferenceEquals(s_createDesignationsAccessRequest, handle))
                    s_createDesignationsAccessRequest = null;
            }
        }

        private static IEnumerator GuardMiningCapture(IAreaManagingTower tower,
            ExperimentalAccessSliceControl control, MiningExecutionResult output)
        {
            IEnumerator routine = CaptureAndPlanMining(tower, control, output);
            try
            {
                while (!control.CancellationRequested)
                {
                    bool advanced;
                    try { advanced = routine.MoveNext(); }
                    catch (Exception ex)
                    {
                        output.Plan = null;
                        output.Failure = ex.GetType().Name + ": " + ex.Message;
                        break;
                    }
                    if (!advanced) break;
                    yield return routine.Current;
                }
            }
            finally { (routine as IDisposable)?.Dispose(); }
        }

        private static IEnumerator CaptureAndPlanMining(IAreaManagingTower tower,
            ExperimentalAccessSliceControl control, MiningExecutionResult output)
        {
            AccessSearchReplayRecorder.RequestSnapshotSaveIfArmed("mining");
            TerrainManager terrain = s_desigManager!.TerrainManager;
            int world = CurrentWorldGeneration;
            bool useWorkerThread = UseWorkerThread;
            var area = tower.Area;
            ATDTowerSettings settings = GetOrCreateTowerSettings(tower);
            int level = settings.OrePurityLevel;
            ResolveAccessMaterialSlopes(tower, out float dumpingSlope,
                out float fallbackMiningSlope, out _, out _, out _);
            var policy = new MiningPolicy(level, settings.MaxHeightDiff,
                settings.MaxLayersToExcavate, settings.MaxDepthToDigTo,
                settings.CorridorClearance, s_minOreHeightByLevel[level],
                s_minOrePurityByLevel[level], s_minBottomOreDensityByLevel[level],
                s_minComponentSizeByLevel[level], AutoTerrainDesignationsMod.BottomFlatteningEnabled,
                AutoTerrainDesignationsMod.BottomFlatteningStrength, FilterOreSpikes,
                AccessAvoidBuildings,
                AccessAvoidOcean, AutoTerrainDesignationsMod.AccessRayEndBuffer,
                BuildingSafetyBufferTiles, dumpingSlope, fallbackMiningSlope);
            BuildBuildingOccupiedTiles(tower, forceRefresh: true);
            var buildings = new HashSet<Tile2i>(s_buildingOccupiedTiles);
            var origins = new List<Tile2i>();
            var columns = new Dictionary<Tile2i, CapturedTerrainColumn>();
            var timer = Stopwatch.StartNew();
            double captureMilliseconds = 0;
            double maxColumnMilliseconds = 0;
            var columnTimer = new Stopwatch();
            long estimatedBytes = buildings.Count * 64L;
            long ceiling = AutoTerrainDesignationsMod.AccessSnapshotMemoryCeilingMiB * 1024L * 1024L;
            Tile2i min = TerrainDesignation.GetOrigin(area.BoundingBoxMin);
            Tile2i max = TerrainDesignation.GetOrigin(area.BoundingBoxMax);
            control.ReportPhase("Capturing mine terrain");
            for (int y = min.Y; y < max.Y; y += 4)
            for (int x = min.X; x < max.X; x += 4)
            {
                CheckCapture();
                Tile2i origin = new Tile2i(x, y);
                if (IsDesignatableTileFullyInsideArea(area, origin))
                {
                    origins.Add(origin);
                    foreach (Tile2i cell in MiningPlanner.Cells(origin))
                    {
                        Capture(cell);
                        if (timer.ElapsedMilliseconds >= control.SliceBudgetMilliseconds)
                        { yield return null; timer.Restart(); }
                    }
                }
                if (timer.ElapsedMilliseconds >= control.SliceBudgetMilliseconds)
                { yield return null; timer.Restart(); }
            }
            var request = new MiningRequest(origins.ToArray(),
                GetCandidateScanProducts(tower).Select(product => product.Id.ToString()).ToArray(),
                policy, new Tile2i(terrain.TerrainSize.X, terrain.TerrainSize.Y), columns, buildings);
            // Geometry determines the needed safety footprint. Each publication owns
            // sealed collections; later capture cannot mutate a worker's input.
            MiningStage stage = MiningStage.Body;
            while (true)
            {
                CheckCapture();
                output.Request = request.Seal();
                IEnumerator work = useWorkerThread
                    ? ExecuteMiningOnWorker(output.Request, stage, control, output, world)
                    : ExecuteMiningOnGameThread(output.Request, stage, control, output, world);
                try { while (work.MoveNext()) yield return work.Current; }
                finally { (work as IDisposable)?.Dispose(); }
                if (output.Plan == null) yield break;
                if (output.Plan.Depths.Count == 0) break;
                IEnumerable<Tile2i>? facts = stage == MiningStage.Body
                    ? MiningSafety.DirectFacts(request, output.Plan)
                    : output.Plan.NeedsSafetyCoverage
                        ? MiningSafety.TraceExterior(request, output.Plan, new List<Tile2i>()) : null;
                if (facts == null) break;
                control.ReportPhase("Capturing mine safety footprint");
                timer.Restart();
                foreach (Tile2i tile in facts)
                {
                    CheckCapture();
                    Capture(tile);
                    if (timer.ElapsedMilliseconds >= control.SliceBudgetMilliseconds)
                    { yield return null; timer.Restart(); }
                }
                stage = MiningStage.SafetyCoverage;
            }
            LogDebug($"[ATD Mining Capture] columns={columns.Count} estimatedRetainedBytes={estimatedBytes} "
                + $"terrainReadMs={captureMilliseconds:0.###} maxColumnMs={maxColumnMilliseconds:0.###}");

            void CheckCapture()
            {
                if (control.CancellationRequested || !IsWorldGenerationActive(world))
                    throw new OperationCanceledException("Mining capture cancelled");
            }
            void Capture(Tile2i tile)
            {
                CheckCapture();
                if (columns.ContainsKey(tile)) return;
                if (!terrain.IsValidCoord(tile))
                    throw new InvalidOperationException("Mining capture outside map: " + tile);
                columnTimer.Restart();
                CapturedTerrainColumn column = CapturePlanningTerrainColumn(terrain, tile);
                double milliseconds = columnTimer.Elapsed.TotalMilliseconds;
                captureMilliseconds += milliseconds;
                maxColumnMilliseconds = Math.Max(maxColumnMilliseconds, milliseconds);
                // Includes managed object/collection overhead and two owned strings
                // per layer. Conservative retained-memory guard, not a heap measure.
                long bytes = 128L;
                for (int i = 0; i < column.LayerCount; i++)
                {
                    CapturedTerrainLayer layer = column.LayerAt(i);
                    bytes += 128L + 2L * (layer.MaterialId.Length + layer.ProductId.Length);
                }
                estimatedBytes += bytes;
                if (estimatedBytes > ceiling)
                    throw new InvalidOperationException("MiningSnapshotTooLarge");
                columns.Add(tile, column);
            }
        }

        private static IEnumerator ExecuteMiningOnWorker(MiningRequest request,
            MiningStage stage, ExperimentalAccessSliceControl control,
            MiningExecutionResult output, int world)
        {
            output.Plan = null;
            long id = Interlocked.Increment(ref s_nextAccessSearchWorkerJobId);
            AccessSearchWorker worker = AccessSearchWorker.Shared;
            var job = new AccessSearchWorkerJob(id, world, request, stage);
            bool consumed = false;
            control.RegisterDisposalCancellation(reason => worker.Abandon(id, reason));
            try
            {
                while (!worker.TrySubmit(job, out string failure))
                {
                    if (control.CancellationRequested || !IsWorldGenerationActive(world)) yield break;
                    if (failure != "WorkerBusy")
                    { output.Failure = failure; yield break; }
                    yield return null;
                }
                while (true)
                {
                    if (control.CancellationRequested || !IsWorldGenerationActive(world))
                    { worker.Abandon(id, "MiningCancelled"); yield break; }
                    if (worker.TryConsumeTerminal(id, world, out AccessSearchWorkerTerminal? terminal))
                    {
                        consumed = true;
                        output.Plan = terminal!.MiningPlan;
                        output.Failure = terminal.Fault;
                        LogDebug($"[ATD Mining Worker] stage={stage} computeMs={terminal.MiningMilliseconds:0.###}");
                        yield break;
                    }
                    if (worker.TryReadProgress(id, out AccessSearchWorkerProgress? progress) && progress != null)
                        control.ReportWorkerProgress(progress.Phase, 0, 0, progress.ProcessingMilliseconds);
                    yield return null;
                }
            }
            finally
            {
                if (!consumed) worker.Abandon(id, "MiningDisposed");
                control.ClearDisposalCancellation();
            }
        }

        private static IEnumerator ExecuteMiningOnGameThread(
            MiningRequest request,
            MiningStage stage,
            ExperimentalAccessSliceControl control,
            MiningExecutionResult output,
            int world)
        {
            output.Plan = null;
            IEnumerator wait = WaitForAccessSearchWorkerToStop(control, world);
            while (wait.MoveNext())
                yield return wait.Current;
            if (control.CancellationRequested || !IsWorldGenerationActive(world))
                yield break;

            control.ReportPhase("Planning mine");
            // The pure mining planner is synchronous; unlike access search it
            // has no resumable session. Opting out can therefore cause a hitch.
            output.Plan = MiningPlanner.Execute(request, stage);
            yield break;
        }

        private static IEnumerator RecordMiningReplay(MiningExecutionResult mining)
        {
            if (mining.Request == null || mining.Plan == null) yield break;
            AccessReplayCaptureOperation? operation = AccessSearchReplayRecorder.BeginRecordMining(
                mining.Request, mining.Plan);
            if (operation == null) yield break;
            int world = CurrentWorldGeneration;
            var request = new ATDAccesswayRequest(
                "create-designations/mining-replay", "mining-replay:" + world,
                ATDAccesswayRequestKind.CreateDesignations, ATDAccesswayPriority.Interactive,
                () => new ATDAccesswayCoroutineWork(Complete,
                    () => ATDAccesswayRequestResult.Succeeded(),
                    GetManagedAccesswaySliceBudgetMilliseconds),
                () => IsWorldGenerationActive(world) ? ATDAccesswayValidationResult.Current()
                    : ATDAccesswayValidationResult.OwnerGone("MiningReplayWorldGone"));
            ATDAccesswayRequestHandle handle = EnqueueAccesswayRequest(request);
            s_createDesignationsAccessRequest = handle;
            bool finished = false;
            try
            {
                while (!ReadAccesswayRequest(handle).IsTerminal) yield return null;
            }
            finally
            {
                if (!finished) operation.CancelAndDiscardWhenComplete();
                CancelAccesswayRequest(handle, "MiningReplayDisposed");
                if (ReferenceEquals(s_createDesignationsAccessRequest, handle))
                    s_createDesignationsAccessRequest = null;
            }

            IEnumerator Complete(ExperimentalAccessSliceControl control)
            {
                control.BeginPostCommitCancellation(operation.Cancel);
                while (!operation.IsComplete)
                {
                    control.ReportPhase(operation.Stage + " (" + operation.Percent + "%)");
                    yield return null;
                }
                operation.CompleteOnMainThread();
                finished = true;
            }
        }
    }
}

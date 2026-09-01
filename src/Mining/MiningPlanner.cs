using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerrainDesignations.Access.Worker;
using AutoTerrainDesignations.Planning;
using Mafi;
using Mafi.Collections;

namespace AutoTerrainDesignations.Mining
{
    /// <summary>Legacy ore decisions, preserved over captured values. No live game access.</summary>
    internal sealed partial class MiningPlanner
    {
        private readonly MiningRequest m_request;
        private readonly MiningPolicy m_policy;
        private readonly IAccessSearchExecutionControl? m_control;
        private string m_phase = "";
        private static readonly Tile2i[] s_cardinalDirections = {
            new Tile2i(4, 0), new Tile2i(-4, 0), new Tile2i(0, 4), new Tile2i(0, -4) };
        private MiningPlanner(MiningRequest request, IAccessSearchExecutionControl? control)
        { m_request = request; m_policy = request.Policy; m_control = control; }
        private void Checkpoint()
        {
            if (m_control?.CancellationRequested == true)
                throw new OperationCanceledException("Mining cancelled");
            m_control?.Publish("Planning mine", m_phase, 0, 0);
        }
        // Geometry diagnostics are observational and never call the game logger on a worker.
        private void LogDebug(string message) { }

        internal static MiningPlan Execute(MiningRequest request,
            MiningStage stage = MiningStage.Complete, IAccessSearchExecutionControl? control = null)
            => new MiningPlanner(request, control).Run(stage);

        private MiningPlan Run(MiningStage stage)
        {
            m_phase = stage.ToString();
            Checkpoint();
            m_control?.Publish("Planning mine", stage.ToString(), 0, 0);
            var targets = new HashSet<string>(m_request.ProductIds, StringComparer.Ordinal);
            Dictionary<Tile2i, float> spikeCutoffs = MiningSpikeFilter.BuildOreBottomCutoffs(
                m_request, targets, MiningSpikeFilterParameters.VanillaCorrection);
            var resources = new Dictionary<Tile2i, List<MiningOreInterval>>();
            var depths = new Dict<Tile2i, int>();
            foreach (Tile2i origin in m_request.Origins)
            {
                Checkpoint();
                var intervals = new List<MiningOreInterval>();
                float surface = float.MaxValue, total = 0f, ore = 0f;
                foreach (Tile2i cell in Cells(origin))
                {
                    CapturedTerrainColumn column = m_request.Column(cell);
                    surface = Math.Min(surface, column.SurfaceHeight);
                    float columnTotal = 0f, columnOre = 0f;
                    for (int i = 0; i < column.LayerCount; i++)
                    {
                        CapturedTerrainLayer layer = column.LayerAt(i);
                        if (layer.IsBedrock) break;
                        columnTotal += layer.Thickness;
                        float visibleTargetThickness = MiningSpikeFilter.VisibleTargetThickness(
                            layer, targets.Contains(layer.ProductId),
                            spikeCutoffs.TryGetValue(cell, out float cutoff), cutoff);
                        if (visibleTargetThickness <= 0f) continue;
                        columnOre += visibleTargetThickness;
                        intervals.Add(new MiningOreInterval(layer.ProductId,
                            visibleTargetThickness, layer.ResourceDepth));
                    }
                    // Preserve the original per-column sums before aggregating purity.
                    total += columnTotal;
                    ore += columnOre;
                }
                if (intervals.Count == 0) continue;
                resources.Add(origin, intervals);
                if (m_policy.MinPurity > 0f
                    && (total > 0f ? ore / total : 0f) < m_policy.MinPurity) continue;
                if (m_policy.MinOreHeight > 0f
                    && GetTargetProductAmount(intervals, targets) < m_policy.MinOreHeight) continue;
                bool found = m_policy.MinBottomDensity > 0f
                    ? TryGetPurityAdjustedDepth(intervals, targets, surface,
                        m_policy.MinBottomDensity, out int depth)
                    : TryGetDeepestResourceDepth(intervals, targets, surface, out depth);
                if (!found) continue;
                if (m_policy.MaxLayers > 0)
                    depth = Math.Max(depth, (int)surface - m_policy.MaxLayers);
                if (m_policy.MinElevation.HasValue)
                    depth = Math.Max(depth, m_policy.MinElevation.Value);
                depths[origin] = depth;
            }
            if (depths.Count == 0) return Empty("NoQualifyingOre");
            FilterIsolatedDesignations(depths, targets, resources, m_policy.PurityLevel);
            if (depths.Count == 0) return Empty("NoComponents");
            FillRectilinearHull(depths, targets, resources, m_policy.CorridorClearance);
            if (m_policy.FlattenBottom)
                FlattenDesignationBottom(depths, m_policy.PurityLevel, m_policy.FlatteningStrength);
            if (stage == MiningStage.Body)
                return new MiningPlan(depths, new Dict<Tile2i, int>(), "Body");
            int removed = 0;
            foreach (Tile2i origin in depths.Keys.ToArray())
            {
                Checkpoint();
                if (MiningSafety.IsDirectlyProtected(m_request, origin))
                { depths.Remove(origin); removed++; }
            }
            if (removed > 0)
                FilterIsolatedDesignations(depths, targets, resources, m_policy.PurityLevel);
            if (depths.Count == 0) return Empty("SafetyRemovedAll", true);
            var corners = BuildAndSmoothCornerHeights(depths, m_policy.MaxHeightDiff,
                m_policy.PurityLevel <= 0);
            var plan = new MiningPlan(depths, corners, "Planned");
            if (stage == MiningStage.DirectSafety) return plan;
            var rejected = new List<Tile2i>();
            foreach (Tile2i fact in MiningSafety.TraceExterior(m_request, plan, rejected))
            {
                Checkpoint();
                // Fail closed on absent facts; a missing sample is never empty terrain.
                m_request.Column(fact);
            }
            foreach (Tile2i origin in rejected) depths.Remove(origin);
            if (rejected.Count > 0)
            {
                FilterIsolatedDesignations(depths, targets, resources, m_policy.PurityLevel);
                if (depths.Count == 0) return Empty("SafetyRemovedAll", true);
                corners = BuildAndSmoothCornerHeights(depths, m_policy.MaxHeightDiff,
                    m_policy.PurityLevel <= 0);
            }
            Checkpoint();
            return new MiningPlan(depths, corners, "Planned");
        }

        private static MiningPlan Empty(string reason, bool reconcile = false)
            => new MiningPlan(new Dict<Tile2i, int>(), new Dict<Tile2i, int>(), reason, reconcile);

        internal static IEnumerable<Tile2i> Cells(Tile2i origin)
        {
            // Preserve aggregation order: rows first, columns within each row.
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    yield return origin + new RelTile2i(x, y);
        }
    }
}

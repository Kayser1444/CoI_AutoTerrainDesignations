using System;
using System.Collections.Generic;
using AutoTerrainDesignations.Planning;
using Mafi;

namespace AutoTerrainDesignations.Mining
{
    internal readonly struct MiningSpikeFilterParameters
    {
        public readonly float AllowedBedrockResidual;
        public readonly int MinimumBedrockNeighbors;

        public MiningSpikeFilterParameters(float allowedBedrockResidual,
            int minimumBedrockNeighbors)
        {
            AllowedBedrockResidual = Math.Max(0f, allowedBedrockResidual);
            MinimumBedrockNeighbors = Math.Max(1, Math.Min(8, minimumBedrockNeighbors));
        }

        public static MiningSpikeFilterParameters VanillaCorrection =>
            new MiningSpikeFilterParameters(4f, 8);
    }

    /// <summary>
    /// Derives a filtered ore interpretation without changing captured terrain facts.
    /// A cutoff is the lowest elevation at which selected ore remains visible to the
    /// ordinary mining planner.
    /// </summary>
    internal static class MiningSpikeFilter
    {
        private const float Epsilon = 0.0001f;

        internal static Dictionary<Tile2i, float> BuildOreBottomCutoffs(
            MiningRequest request, HashSet<string> targets,
            MiningSpikeFilterParameters parameters)
        {
            var cutoffs = new Dictionary<Tile2i, float>();
            if (!request.Policy.FilterOreSpikes || targets.Count == 0)
                return cutoffs;

            foreach (Tile2i origin in request.Origins)
            foreach (Tile2i tile in MiningPlanner.Cells(origin))
            {
                if (cutoffs.ContainsKey(tile)
                    || !request.TryGetColumn(tile, out CapturedTerrainColumn center)
                    || !TryGetBedrockTopAndDeepestTargetBottom(center, targets,
                        out float centerBedrockTop, out float deepestTargetBottom))
                    continue;

                var neighborBedrockTops = new List<float>(8);
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0) continue;
                    Tile2i neighbor = tile + new RelTile2i(x, y);
                    if (request.TryGetColumn(neighbor, out CapturedTerrainColumn column)
                        && TryGetBedrockTop(column, out float bedrockTop))
                        neighborBedrockTops.Add(bedrockTop);
                }
                if (neighborBedrockTops.Count < parameters.MinimumBedrockNeighbors)
                    continue;

                neighborBedrockTops.Sort();
                int middle = neighborBedrockTops.Count / 2;
                float median = neighborBedrockTops.Count % 2 == 0
                    ? (neighborBedrockTops[middle - 1] + neighborBedrockTops[middle]) * 0.5f
                    : neighborBedrockTops[middle];
                float raise = median - centerBedrockTop - parameters.AllowedBedrockResidual;
                if (raise > Epsilon)
                    cutoffs[tile] = deepestTargetBottom + raise;
            }
            return cutoffs;
        }

        internal static float VisibleTargetThickness(CapturedTerrainLayer layer,
            bool isTarget, bool hasCutoff, float cutoff)
        {
            if (!isTarget) return 0f;
            if (!hasCutoff) return layer.Thickness;
            // Keep captured thickness verbatim when the complete layer remains.
            // Top-bottom subtraction is not guaranteed to reproduce the game's
            // independently captured thickness arithmetic.
            if (layer.Bottom >= cutoff - Epsilon) return layer.Thickness;
            return Math.Max(0f, Math.Min(layer.Thickness, layer.Top - cutoff));
        }

        private static bool TryGetBedrockTopAndDeepestTargetBottom(
            CapturedTerrainColumn column, HashSet<string> targets,
            out float bedrockTop, out float deepestTargetBottom)
        {
            deepestTargetBottom = float.MaxValue;
            bool foundTarget = false;
            for (int i = 0; i < column.LayerCount; i++)
            {
                CapturedTerrainLayer layer = column.LayerAt(i);
                if (layer.IsBedrock)
                {
                    bedrockTop = layer.Top;
                    return foundTarget;
                }
                if (targets.Contains(layer.ProductId))
                {
                    deepestTargetBottom = Math.Min(deepestTargetBottom, layer.Bottom);
                    foundTarget = true;
                }
            }
            bedrockTop = 0f;
            return false;
        }

        private static bool TryGetBedrockTop(CapturedTerrainColumn column,
            out float bedrockTop)
        {
            for (int i = 0; i < column.LayerCount; i++)
            {
                CapturedTerrainLayer layer = column.LayerAt(i);
                if (!layer.IsBedrock) continue;
                bedrockTop = layer.Top;
                return true;
            }
            bedrockTop = 0f;
            return false;
        }
    }
}

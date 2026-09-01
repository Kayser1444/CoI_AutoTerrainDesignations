using System;
using System.Collections.Generic;
using Mafi;
namespace AutoTerrainDesignations.Mining
{
    internal sealed partial class MiningPlanner
    {
        private bool TryGetDeepestResourceDepth(
            List<MiningOreInterval> resources,
            HashSet<string> targetProductIds,
            float terrainHeight,
            out int depthInt)
        {
            depthInt = 0;
            bool found = false;

            foreach (MiningOreInterval resource in resources)
            {
                Checkpoint();
                if (!targetProductIds.Contains(resource.ProductId))
                {
                    continue;
                }

                int candidateDepth = (terrainHeight - resource.Depth - resource.Height).FloorToInt();
                if (!found || candidateDepth < depthInt)
                {
                    depthInt = candidateDepth;
                    found = true;
                }
            }

            return found;
        }

        private bool TryGetPurityAdjustedDepth(
            List<MiningOreInterval> resources,
            HashSet<string> targetProductIds,
            float terrainHeight,
            float minBottomOreDensity,
            out int depthInt)
        {
            depthInt = 0;
            var intervals = new List<(float top, float bottom, float thickness)>();
            foreach (var resource in resources)
            {
                Checkpoint();
                if (!targetProductIds.Contains(resource.ProductId))
                    continue;
                float topDepth    = resource.Depth;
                float thickness   = resource.Height;
                float bottomDepth = topDepth + thickness;
                intervals.Add((topDepth, bottomDepth, thickness));
            }
            if (intervals.Count == 0) return false;

            if (minBottomOreDensity <= 0f)
            {
                // No trimming — use deepest bottom
                float deepest = 0f;
                bool anyFound = false;
                foreach (var iv in intervals)
                {
                Checkpoint();
                    if (!anyFound || iv.bottom > deepest) { deepest = iv.bottom; anyFound = true; }
                }
                depthInt = (terrainHeight - deepest).FloorToInt();
                return true;
            }

            // Sort top-to-bottom (shallowest first)
            intervals.Sort((a, b) => a.top.CompareTo(b.top));

            float stopDepth = 0f;
            bool found = false;
            for (int i = 0; i < intervals.Count; i++)
            {
                Checkpoint();
                var iv = intervals[i];
                float localDensity;
                if (i == 0)
                {
                    // Shallowest interval always qualifies — no zone above it to evaluate
                    localDensity = 1f;
                }
                else
                {
                    // Zone = from bottom of previous ore interval to bottom of this one
                    // (includes the waste gap between them plus this ore seam)
                    float zoneThickness = iv.bottom - intervals[i - 1].bottom;
                    localDensity = zoneThickness > 0f ? iv.thickness / zoneThickness : 1f;
                }

                if (localDensity >= minBottomOreDensity)
                {
                    stopDepth = iv.bottom;
                    found = true;
                }
                else
                {
                    // This zone is too sparse — don't dig deeper
                    break;
                }
            }

            if (!found) return false;
            depthInt = (terrainHeight - stopDepth).FloorToInt();
            return true;
        }
    }
}

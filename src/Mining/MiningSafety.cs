using System.Collections.Generic;
using AutoTerrainDesignations.Planning;
using Mafi;

namespace AutoTerrainDesignations.Mining
{
    /// <summary>
    /// The same read sequence discovers capture coverage and evaluates replay.
    /// A yielded coordinate must be captured before advancing. Capture owns the
    /// builder exclusively; worker/replay only drain this sequence over sealed facts.
    /// </summary>
    internal static class MiningSafety
    {
        internal static IEnumerable<Tile2i> DirectFacts(MiningRequest request, MiningPlan plan)
        {
            if (!request.Policy.AvoidOcean) yield break;
            int buffer = request.Policy.RayBuffer;
            foreach (Tile2i origin in plan.Depths.Keys)
                for (int y = -buffer; y <= 3 + buffer; y++)
                    for (int x = -buffer; x <= 3 + buffer; x++)
                    {
                        Tile2i tile = origin + new RelTile2i(x, y);
                        if (request.IsValid(tile)) yield return tile;
                    }
        }

        internal static bool IsDirectlyProtected(MiningRequest request, Tile2i origin)
        {
            MiningPolicy policy = request.Policy;
            if (policy.AvoidOcean)
                for (int y = -policy.RayBuffer; y <= 3 + policy.RayBuffer; y++)
                    for (int x = -policy.RayBuffer; x <= 3 + policy.RayBuffer; x++)
                    {
                        Tile2i tile = origin + new RelTile2i(x, y);
                        if (request.IsValid(tile) && request.Column(tile).IsOcean) return true;
                    }
            if (policy.AvoidBuildings)
            {
                int margin = policy.RayBuffer + policy.BuildingBuffer;
                for (int y = -margin; y <= 3 + margin; y++)
                    for (int x = -margin; x <= 3 + margin; x++)
                        if (request.BuildingAt(origin + new RelTile2i(x, y))) return true;
            }
            return false;
        }

        private static bool BuildingAt(MiningRequest request, Tile2i tile)
        {
            int radius = request.Policy.BuildingBuffer;
            for (int y = tile.Y - radius; y <= tile.Y + radius; y++)
                for (int x = tile.X - radius; x <= tile.X + radius; x++)
                    if (request.BuildingAt(new Tile2i(x, y))) return true;
            return false;
        }

        internal static IEnumerable<Tile2i> TraceExterior(MiningRequest request,
            MiningPlan plan, List<Tile2i> rejected)
        {
            MiningPolicy policy = request.Policy;
            if (!policy.AvoidOcean && !policy.AvoidBuildings) yield break;
            var directions = new[] { new Tile2i(-1, 0), new Tile2i(1, 0),
                new Tile2i(0, -1), new Tile2i(0, 1) };
            foreach (Tile2i origin in plan.Depths.Keys)
            {
                bool hazardous = false;
                for (int side = 0; side < 4 && !hazardous; side++)
                {
                    Tile2i direction = directions[side];
                    if (plan.Depths.ContainsKey(origin
                        + new RelTile2i(direction.X * 4, direction.Y * 4))) continue;
                    for (int step = 0; step <= 4 && !hazardous; step++)
                    {
                        int x = side == 0 ? 0 : side == 1 ? 4 : step;
                        int y = side == 2 ? 0 : side == 3 ? 4 : step;
                        Tile2i start = origin + new RelTile2i(x, y);
                        yield return start;
                        float height = (plan.Corners[origin] * (4 - x) * (4 - y)
                            + plan.Corners[origin.AddX(4)] * x * (4 - y)
                            + plan.Corners[origin.AddY(4)] * (4 - x) * y
                            + plan.Corners[origin.AddXy(4)] * x * y) / 16f;
                        float surface = request.Column(start).SurfaceHeight;
                        bool cut = height < surface - 0.0001f;
                        if (!cut && height <= surface + 0.0001f) continue;
                        int maxDistance = direction.X != 0
                            ? (direction.X < 0 ? start.X : request.TerrainSize.X - 1 - start.X)
                            : (direction.Y < 0 ? start.Y : request.TerrainSize.Y - 1 - start.Y);
                        for (int distance = 1; distance <= maxDistance; distance++)
                        {
                            Tile2i tile = start + new RelTile2i(direction.X * distance, direction.Y * distance);
                            yield return tile;
                            CapturedTerrainColumn column = request.Column(tile);
                            float slope = policy.DumpingSlope;
                            if (cut && !column.TryGetSlope(height, out slope))
                                slope = policy.FallbackMiningSlope;
                            height += cut ? slope : -slope;
                            if ((policy.AvoidBuildings && BuildingAt(request, tile))
                                || (policy.AvoidOcean && cut && column.IsOcean && height < 1f))
                            { hazardous = true; break; }
                            bool passed = cut ? column.SurfaceHeight <= height : column.SurfaceHeight >= height;
                            if (passed && (!cut || height >= 1f))
                            {
                                for (int tail = 1; tail <= policy.RayBuffer; tail++)
                                {
                                    Tile2i buffered = tile + new RelTile2i(direction.X * tail, direction.Y * tail);
                                    if (!request.IsValid(buffered)) break;
                                    yield return buffered;
                                    if ((policy.AvoidOcean && request.Column(buffered).IsOcean)
                                        || (policy.AvoidBuildings && BuildingAt(request, buffered)))
                                    { hazardous = true; break; }
                                }
                                break;
                            }
                        }
                    }
                }
                if (hazardous) rejected.Add(origin);
            }
        }
    }
}

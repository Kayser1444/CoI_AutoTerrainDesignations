using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core.Terrain;

namespace AutoTerrainDesignations.Access.Reduction
{
    internal static class ReducedAccessDomainFixtures
    {
        internal static bool ValidateAll(out string failure)
        {
            PolygonTerrainArea2i area =
                PolygonTerrainArea2i.FromRectOriginSize(
                    new Tile2i(0, 0), new RelTile2i(512, 512));
            var sources = new[]
            {
                new Tile2i(20, 24),
                new Tile2i(24, 24),
            };
            var goals = new[] { new Tile2i(460, 420) };
            if (!ReducedAccessDomainPlanner.TryPlan(
                    sources, goals, goals[0], area,
                    Tile2i.Zero, new Tile2i(511, 511),
                    0, 4, 12, 256L * 1024L * 1024L,
                    out ReducedAccessDomainPlan first,
                    out failure))
                return false;

            if (!ReducedAccessDomainPlanner.TryPlan(
                    sources.Reverse().ToArray(), goals, goals[0], area,
                    Tile2i.Zero, new Tile2i(511, 511),
                    0, 4, 12, 256L * 1024L * 1024L,
                    out ReducedAccessDomainPlan second,
                    out failure)
                || first.Fingerprint != second.Fingerprint
                || first.SearchOrigins.Count != second.SearchOrigins.Count
                || first.CaptureTiles.Count != second.CaptureTiles.Count)
            {
                failure = "Reduced coverage changed when endpoint input order changed.";
                return false;
            }

            foreach (Tile2i origin in first.SearchOrigins.EnumerateTiles())
            {
                if (!area.ContainsTile(origin)
                    || !area.ContainsTile(origin + new RelTile2i(3, 0))
                    || !area.ContainsTile(origin + new RelTile2i(0, 3))
                    || !area.ContainsTile(origin + new RelTile2i(3, 3)))
                {
                    failure = "Reduced search coverage escaped generated-area authority.";
                    return false;
                }
            }

            long boxTiles =
                ((long)first.CaptureTiles.BoundsMax.X
                    - first.CaptureTiles.BoundsMin.X + 1L)
                * ((long)first.CaptureTiles.BoundsMax.Y
                    - first.CaptureTiles.BoundsMin.Y + 1L);
            if (boxTiles <= first.CaptureTiles.Count * 2L)
            {
                failure = "Thin-corridor coverage still scales like its enclosing box.";
                return false;
            }

            if (ReducedAccessDomainPlanner.TryPlan(
                    sources, goals, goals[0], area,
                    Tile2i.Zero, new Tile2i(511, 511),
                    0, 4, 12, 8L * 1024L * 1024L,
                    out _, out _))
            {
                failure = "Reducer accepted a corridor without memory for its tiles.";
                return false;
            }

            var broadSources = new[]
            {
                new Tile2i(20, 24),
                new Tile2i(20, 300),
                new Tile2i(300, 24),
                new Tile2i(300, 300),
            };
            if (!ReducedAccessDomainPlanner.TryPlan(
                    broadSources, goals, goals[0], area,
                    Tile2i.Zero, new Tile2i(511, 511),
                    0, 4, 12, 128L * 1024L * 1024L,
                    out ReducedAccessDomainPlan partial,
                    out failure)
                || partial.SelectedSources.Count == 0
                || partial.SelectedSources.Count >= broadSources.Length)
            {
                failure = "Reducer did not admit a fitting subset of source branches.";
                return false;
            }

            Tile2i[] overlappingSources = Enumerable.Range(0, 9)
                .SelectMany(y => Enumerable.Range(0, 9)
                    .Select(x => new Tile2i(324 + x * 4, 324 + y * 4)))
                .ToArray();
            var overlappingGoal = new Tile2i(340, 340);
            if (!ReducedAccessDomainPlanner.TryPlan(
                    overlappingSources,
                    new[] { overlappingGoal },
                    overlappingGoal,
                    area,
                    Tile2i.Zero,
                    new Tile2i(511, 511),
                    0, 4, 12, 64L * 1024L * 1024L,
                    out ReducedAccessDomainPlan overlapping,
                    out failure)
                || overlapping.SelectedSources.Count != 65
                || overlapping.BuiltSourceBranchCount != 1)
            {
                failure =
                    "Reducer rebuilt a shared corridor for overlapping sources.";
                return false;
            }

            var wideningSource = new Tile2i(20, 256);
            var wideningGoal = new Tile2i(488, 256);
            const long wideningBudget = 256L * 1024L * 1024L;
            if (!ReducedAccessDomainPlanner.TryPlan(
                    new[] { wideningSource },
                    new[] { wideningGoal },
                    wideningGoal,
                    area,
                    Tile2i.Zero,
                    new Tile2i(511, 511),
                    0, 4, 12, wideningBudget,
                    out ReducedAccessDomainPlan widened,
                    out failure)
                || widened.EstimatedBytes < wideningBudget * 3L / 4L)
            {
                failure =
                    "Reducer left most of a usable widening budget unspent.";
                return false;
            }

            long captureContractEstimate =
                AccessSnapshotMemoryEstimator.EstimateRetainedBytes(
                    widened.CaptureTiles.Count,
                    widened.SearchOrigins.Count,
                    widened.SearchOrigins.Count,
                    widened.SearchOrigins.Count,
                    widened.SearchOrigins.Count,
                    widened.CaptureTiles.Count,
                    widened.CaptureTiles.Count,
                    widened.SearchOrigins.Count,
                    widened.CaptureTiles.Count,
                    widened.SearchOrigins.Count,
                    widened.CaptureTiles.Count,
                    widened.SearchOrigins.Count * 4L,
                    widened.CaptureTiles.Count);
            if (captureContractEstimate > wideningBudget)
            {
                failure =
                    "Reducer budget disagreed with snapshot preflight accounting.";
                return false;
            }

            failure = string.Empty;
            return true;
        }
    }
}

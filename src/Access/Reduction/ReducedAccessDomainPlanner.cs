using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core.Terrain;

namespace AutoTerrainDesignations.Access.Reduction
{
    /// <summary>
    /// Compact immutable scanline coverage. Its storage is proportional to the
    /// covered runs, never to the area of the enclosing rectangle.
    /// </summary>
    internal sealed class AccessTileCoverage
    {
        private readonly Dictionary<int, Run[]> m_rows;

        private readonly struct Run
        {
            public readonly int MinX;
            public readonly int MaxX;

            public Run(int minX, int maxX)
            {
                MinX = minX;
                MaxX = maxX;
            }
        }

        public long Count { get; }
        public Tile2i BoundsMin { get; }
        public Tile2i BoundsMax { get; }
        public int Fingerprint { get; }

        private AccessTileCoverage(
            Dictionary<int, Run[]> rows,
            long count,
            Tile2i boundsMin,
            Tile2i boundsMax,
            int fingerprint)
        {
            m_rows = rows;
            Count = count;
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            Fingerprint = fingerprint;
        }

        public bool Contains(Tile2i tile)
        {
            if (!m_rows.TryGetValue(tile.Y, out Run[] runs))
                return false;
            int low = 0;
            int high = runs.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                Run run = runs[middle];
                if (tile.X < run.MinX)
                    high = middle - 1;
                else if (tile.X > run.MaxX)
                    low = middle + 1;
                else
                    return true;
            }
            return false;
        }

        public IEnumerable<Tile2i> EnumerateTiles()
        {
            foreach (KeyValuePair<int, Run[]> row in m_rows.OrderBy(p => p.Key))
                foreach (Run run in row.Value)
                    for (int x = run.MinX; x <= run.MaxX; x++)
                        yield return new Tile2i(x, row.Key);
        }

        internal static AccessTileCoverage Create(IEnumerable<Tile2i> tiles)
        {
            var xsByY = new SortedDictionary<int, SortedSet<int>>();
            foreach (Tile2i tile in tiles)
            {
                if (!xsByY.TryGetValue(tile.Y, out SortedSet<int> xs))
                {
                    xs = new SortedSet<int>();
                    xsByY.Add(tile.Y, xs);
                }
                xs.Add(tile.X);
            }
            if (xsByY.Count == 0)
                throw new ArgumentException("Coverage cannot be empty.", nameof(tiles));

            var rows = new Dictionary<int, Run[]>(xsByY.Count);
            long count = 0;
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            unchecked
            {
                int hash = 17;
                foreach (KeyValuePair<int, SortedSet<int>> row in xsByY)
                {
                    var runs = new List<Run>();
                    int runMin = 0;
                    int previous = 0;
                    bool hasRun = false;
                    foreach (int x in row.Value)
                    {
                        if (!hasRun)
                        {
                            runMin = previous = x;
                            hasRun = true;
                        }
                        else if ((long)x == (long)previous + 1L)
                        {
                            previous = x;
                        }
                        else
                        {
                            runs.Add(new Run(runMin, previous));
                            runMin = previous = x;
                        }
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        count++;
                    }
                    if (hasRun)
                        runs.Add(new Run(runMin, previous));
                    rows.Add(row.Key, runs.ToArray());
                    hash = hash * 31 + row.Key;
                    foreach (Run run in runs)
                    {
                        hash = hash * 31 + run.MinX;
                        hash = hash * 31 + run.MaxX;
                    }
                }
                return new AccessTileCoverage(
                    rows,
                    count,
                    new Tile2i(minX, xsByY.First().Key),
                    new Tile2i(maxX, xsByY.Last().Key),
                    hash);
            }
        }
    }

    internal sealed class ReducedAccessDomainPlan
    {
        public IReadOnlyList<Tile2i> SelectedSources { get; }
        public AccessTileCoverage SearchOrigins { get; }
        public AccessTileCoverage CaptureTiles { get; }
        public Tile2i GoalProxy { get; }
        public int CorridorHalfWidth { get; }
        public long EstimatedBytes { get; }
        public int BuiltSourceBranchCount { get; }
        public int Fingerprint { get; }

        public ReducedAccessDomainPlan(
            IReadOnlyList<Tile2i> selectedSources,
            AccessTileCoverage searchOrigins,
            AccessTileCoverage captureTiles,
            Tile2i goalProxy,
            int corridorHalfWidth,
            long estimatedBytes,
            int builtSourceBranchCount)
        {
            SelectedSources = selectedSources;
            SearchOrigins = searchOrigins;
            CaptureTiles = captureTiles;
            GoalProxy = goalProxy;
            CorridorHalfWidth = corridorHalfWidth;
            EstimatedBytes = estimatedBytes;
            BuiltSourceBranchCount = builtSourceBranchCount;
            unchecked
            {
                Fingerprint = ((searchOrigins.Fingerprint * 397)
                    ^ captureTiles.Fingerprint) * 397 ^ goalProxy.X;
                Fingerprint = Fingerprint * 397 ^ goalProxy.Y;
                foreach (Tile2i source in selectedSources)
                {
                    Fingerprint = Fingerprint * 397 ^ source.X;
                    Fingerprint = Fingerprint * 397 ^ source.Y;
                }
            }
        }
    }

    /// <summary>
    /// Pure geometry reducer. It consumes endpoint coordinates and the copied
    /// polygon value only; terrain, pathability, designations, buildings and
    /// props cannot influence its corridor.
    /// </summary>
    internal static class ReducedAccessDomainPlanner
    {
        internal const int Version = 3;
        private const long FixedOverheadBytes = 8L * 1024L * 1024L;
        // Match AccessSnapshotMemoryEstimator's initial-capture contract. The
        // capture-tile value includes the worst-case occupied-building term so
        // geometry planning cannot hand capture a mask that preflight rejects.
        private const int EstimatedCaptureTileBytes = 2368;
        private const int EstimatedOriginBytes = 5056;
        // Keep pathological clusters from turning source admission into an
        // allocation storm. The nearest complete branch is always tried;
        // additional sources are only a best-effort use of remaining budget.
        private const int MaximumAdditionalSources = 64;

        public static bool TryPlan(
            IReadOnlyCollection<Tile2i> sources,
            IReadOnlyCollection<Tile2i> goals,
            Tile2i towerGoalProxy,
            PolygonTerrainArea2i area,
            Tile2i physicalMin,
            Tile2i physicalMax,
            int generatedAreaMargin,
            int minimumHalfWidth,
            int captureHalo,
            long memoryCeilingBytes,
            out ReducedAccessDomainPlan plan,
            out string failureReason)
        {
            plan = null!;
            failureReason = string.Empty;
            Tile2i[] orderedSources = sources.Distinct().OrderBy(t => t.Y)
                .ThenBy(t => t.X).ToArray();
            Tile2i[] orderedGoals = goals.Concat(new[] { towerGoalProxy })
                .Distinct().OrderBy(t => t.Y).ThenBy(t => t.X).ToArray();
            if (orderedSources.Length == 0 || orderedGoals.Length == 0)
            {
                failureReason = "NoEndpointPair";
                return false;
            }

            Tile2i selectedGoal = orderedGoals
                .OrderBy(g => orderedSources.Min(s => Manhattan(s, g)))
                .ThenBy(g => g.Y).ThenBy(g => g.X).First();
            Tile2i[] sourceCandidates = orderedSources
                .OrderBy(source => Manhattan(source, selectedGoal))
                .ThenBy(source => source.Y)
                .ThenBy(source => source.X)
                .ToArray();
            long minimumSpine = sourceCandidates.Min(
                source => Manhattan(source, selectedGoal) / 4L + 2L);
            long conservativeMinimum = FixedOverheadBytes
                + minimumSpine * Math.Max(1, minimumHalfWidth / 2L)
                    * (EstimatedOriginBytes + EstimatedCaptureTileBytes * captureHalo);
            if (conservativeMinimum > memoryCeilingBytes)
            {
                failureReason = "MinimumCorridorExceedsBudget";
                return false;
            }

            int minWidth = RoundWidth(Math.Max(4, minimumHalfWidth));
            // Start branch admission at a useful maneuver width, then spend the
            // remaining budget by widening the accepted network uniformly.
            int baseUsefulWidth = RoundWidth(minWidth + 16);
            for (int halfWidth = baseUsefulWidth;
                halfWidth >= minWidth;
                halfWidth -= 4)
            {
                var selectedSources = new List<Tile2i>();
                var branchSources = new List<Tile2i>();
                var selectedSearchTiles = new HashSet<Tile2i>();
                var selectedCaptureTiles = new HashSet<Tile2i>();
                bool selectedFirstBranch = false;
                int firstBranchIndex = -1;
                int builtSourceBranchCount = 0;
                for (int sourceIndex = 0;
                    sourceIndex < sourceCandidates.Length;
                    sourceIndex++)
                {
                    Tile2i source = sourceCandidates[sourceIndex];
                    long sourceSpine = Manhattan(source, selectedGoal) / 4L + 2L;
                    long sourceLowerBound = FixedOverheadBytes
                        + sourceSpine * Math.Max(1, halfWidth / 2L)
                            * (EstimatedOriginBytes
                                + EstimatedCaptureTileBytes * captureHalo);
                    if (sourceLowerBound > memoryCeilingBytes)
                        continue;
                    builtSourceBranchCount++;
                    if (!TryBuildCoverage(
                            new[] { source }, selectedGoal, area,
                            physicalMin, physicalMax, generatedAreaMargin,
                            halfWidth, captureHalo,
                            out AccessTileCoverage trialSearch,
                            out AccessTileCoverage trialCapture))
                        continue;
                    long estimate = FixedOverheadBytes
                        + trialCapture.Count * EstimatedCaptureTileBytes
                        + trialSearch.Count * EstimatedOriginBytes;
                    if (estimate > memoryCeilingBytes)
                        continue;
                    selectedSources.Add(source);
                    branchSources.Add(source);
                    foreach (Tile2i tile in trialSearch.EnumerateTiles())
                        selectedSearchTiles.Add(tile);
                    foreach (Tile2i tile in trialCapture.EnumerateTiles())
                        selectedCaptureTiles.Add(tile);
                    selectedFirstBranch = true;
                    firstBranchIndex = sourceIndex;
                    break;
                }
                if (!selectedFirstBranch)
                    continue;

                int additionalSources = 0;
                for (int sourceIndex = firstBranchIndex + 1;
                    sourceIndex < sourceCandidates.Length
                        && additionalSources < MaximumAdditionalSources;
                    sourceIndex++)
                {
                    Tile2i source = sourceCandidates[sourceIndex];
                    if (selectedSearchTiles.Contains(Align(source)))
                    {
                        selectedSources.Add(source);
                        additionalSources++;
                        continue;
                    }
                    builtSourceBranchCount++;
                    if (!TryBuildSearchTiles(
                            source, selectedGoal, area,
                            generatedAreaMargin, halfWidth,
                            out HashSet<Tile2i> trialSearchTiles))
                        continue;
                    var addedSearchTiles = new List<Tile2i>();
                    foreach (Tile2i tile in trialSearchTiles)
                        if (selectedSearchTiles.Add(tile))
                            addedSearchTiles.Add(tile);
                    var addedCaptureTiles = new List<Tile2i>();
                    AddCaptureTiles(
                        addedSearchTiles, selectedCaptureTiles,
                        physicalMin, physicalMax, captureHalo,
                        addedCaptureTiles);
                    long estimate = FixedOverheadBytes
                        + (long)selectedCaptureTiles.Count
                            * EstimatedCaptureTileBytes
                        + (long)selectedSearchTiles.Count
                            * EstimatedOriginBytes;
                    if (estimate > memoryCeilingBytes)
                    {
                        foreach (Tile2i tile in addedCaptureTiles)
                            selectedCaptureTiles.Remove(tile);
                        foreach (Tile2i tile in addedSearchTiles)
                            selectedSearchTiles.Remove(tile);
                        continue;
                    }
                    selectedSources.Add(source);
                    branchSources.Add(source);
                    additionalSources++;
                }

                WidenToBudgetIncrementally(
                    branchSources, selectedGoal, area, physicalMin,
                    physicalMax, generatedAreaMargin, captureHalo,
                    memoryCeilingBytes, halfWidth,
                    selectedSearchTiles, selectedCaptureTiles,
                    out halfWidth);
                AccessTileCoverage selectedSearch =
                    AccessTileCoverage.Create(selectedSearchTiles);
                AccessTileCoverage selectedCapture =
                    AccessTileCoverage.Create(selectedCaptureTiles);
                long selectedEstimate =
                    EstimateBytes(selectedSearch, selectedCapture);
                selectedSources = sourceCandidates
                    .Where(source => selectedSearch.Contains(Align(source)))
                    .Take(MaximumAdditionalSources + 1)
                    .ToList();
                plan = new ReducedAccessDomainPlan(
                    selectedSources.ToArray(), selectedSearch, selectedCapture,
                    selectedGoal, halfWidth, selectedEstimate,
                    builtSourceBranchCount);
                return true;
            }

            failureReason = "NoGeometryCorridorWithinArea";
            return false;
        }

        private static void WidenToBudgetIncrementally(
            IReadOnlyList<Tile2i> branchSources,
            Tile2i goal,
            PolygonTerrainArea2i area,
            Tile2i physicalMin,
            Tile2i physicalMax,
            int generatedAreaMargin,
            int captureHalo,
            long memoryCeilingBytes,
            int minimumWidth,
            HashSet<Tile2i> searchTiles,
            HashSet<Tile2i> captureTiles,
            out int bestWidth)
        {
            bestWidth = minimumWidth;
            while (bestWidth <= int.MaxValue - 4)
            {
                int trialWidth = bestWidth + 4;
                var addedSearchTiles = new List<Tile2i>();
                foreach (Tile2i source in branchSources)
                    AddOriginRing(
                        searchTiles, addedSearchTiles, source, goal,
                        trialWidth, area, generatedAreaMargin);
                if (addedSearchTiles.Count == 0)
                    return;

                var addedCaptureTiles = new List<Tile2i>();
                AddCaptureTiles(
                    addedSearchTiles, captureTiles, physicalMin,
                    physicalMax, captureHalo, addedCaptureTiles);
                long estimate = FixedOverheadBytes
                    + (long)captureTiles.Count * EstimatedCaptureTileBytes
                    + (long)searchTiles.Count * EstimatedOriginBytes;
                if (estimate <= memoryCeilingBytes)
                {
                    bestWidth = trialWidth;
                    continue;
                }
                foreach (Tile2i tile in addedCaptureTiles)
                    captureTiles.Remove(tile);
                foreach (Tile2i tile in addedSearchTiles)
                    searchTiles.Remove(tile);
                return;
            }
        }

        private static void AddOriginRing(
            HashSet<Tile2i> searchTiles,
            List<Tile2i> addedTiles,
            Tile2i source,
            Tile2i goal,
            int radius,
            PolygonTerrainArea2i area,
            int generatedAreaMargin)
        {
            List<Tile2i>? spine = BuildCandidateSpines(source, goal)
                .FirstOrDefault(candidate => candidate.All(point =>
                    IsAuthorizedOrigin(point, area, generatedAreaMargin)));
            if (spine == null)
                return;
            foreach (Tile2i center in spine)
                for (int y = -radius; y <= radius; y += 4)
                    for (int x = -radius; x <= radius; x += 4)
                    {
                        if (Math.Abs(x) != radius
                            && Math.Abs(y) != radius)
                            continue;
                        Tile2i origin = center + new RelTile2i(x, y);
                        if (IsAuthorizedOrigin(
                                origin, area, generatedAreaMargin)
                            && searchTiles.Add(origin))
                            addedTiles.Add(origin);
                    }
        }

        private static long EstimateBytes(
            AccessTileCoverage search, AccessTileCoverage capture)
            => FixedOverheadBytes
                + capture.Count * EstimatedCaptureTileBytes
                + search.Count * EstimatedOriginBytes;

        private static int RoundWidth(int width)
            => ((width + 3) / 4) * 4;

        private static bool TryBuildCoverage(
            IReadOnlyList<Tile2i> sources,
            Tile2i goal,
            PolygonTerrainArea2i area,
            Tile2i physicalMin,
            Tile2i physicalMax,
            int generatedAreaMargin,
            int halfWidth,
            int captureHalo,
            out AccessTileCoverage searchCoverage,
            out AccessTileCoverage captureCoverage)
        {
            var search = new HashSet<Tile2i>();
            foreach (Tile2i source in sources)
            {
                if (!TryBuildSearchTiles(
                        source, goal, area, generatedAreaMargin, halfWidth,
                        out HashSet<Tile2i> sourceSearch))
                {
                    searchCoverage = null!;
                    captureCoverage = null!;
                    return false;
                }
                search.UnionWith(sourceSearch);
            }

            if (search.Count == 0)
            {
                searchCoverage = null!;
                captureCoverage = null!;
                return false;
            }

            var capture = new HashSet<Tile2i>();
            int halo = Math.Max(4, captureHalo);
            AddCaptureTiles(
                search, capture, physicalMin, physicalMax, halo, null);
            // A tower-access bulb allows the canonical captured-ground flood to
            // discover real goals around the geometry-only proxy.
            for (int y = -halo; y <= halo; y++)
                for (int x = -halo; x <= halo; x++)
                {
                    Tile2i tile = goal + new RelTile2i(x, y);
                    if (tile.X >= physicalMin.X && tile.Y >= physicalMin.Y
                        && tile.X <= physicalMax.X && tile.Y <= physicalMax.Y)
                        capture.Add(tile);
                }
            searchCoverage = AccessTileCoverage.Create(search);
            captureCoverage = AccessTileCoverage.Create(capture);
            return true;
        }

        private static bool TryBuildSearchTiles(
            Tile2i source,
            Tile2i goal,
            PolygonTerrainArea2i area,
            int generatedAreaMargin,
            int halfWidth,
            out HashSet<Tile2i> search)
        {
            search = new HashSet<Tile2i>();
            List<Tile2i>? best = null;
            foreach (List<Tile2i> candidate in BuildCandidateSpines(source, goal))
            {
                if (candidate.All(p => IsAuthorizedOrigin(
                        p, area, generatedAreaMargin)))
                {
                    best = candidate;
                    break;
                }
            }
            if (best == null)
                return false;
            foreach (Tile2i spine in best)
                AddOriginBulb(search, spine, halfWidth, area,
                    generatedAreaMargin);
            return search.Count > 0;
        }

        private static void AddCaptureTiles(
            IEnumerable<Tile2i> searchOrigins,
            HashSet<Tile2i> capture,
            Tile2i physicalMin,
            Tile2i physicalMax,
            int captureHalo,
            List<Tile2i>? addedTiles)
        {
            int halo = Math.Max(4, captureHalo);
            foreach (Tile2i origin in searchOrigins)
                for (int y = -halo; y <= 4 + halo; y++)
                    for (int x = -halo; x <= 4 + halo; x++)
                    {
                        Tile2i tile = origin + new RelTile2i(x, y);
                        if (tile.X >= physicalMin.X && tile.Y >= physicalMin.Y
                            && tile.X <= physicalMax.X
                            && tile.Y <= physicalMax.Y
                            && capture.Add(tile))
                            addedTiles?.Add(tile);
                    }
        }

        private static IEnumerable<List<Tile2i>> BuildCandidateSpines(
            Tile2i source, Tile2i goal)
        {
            Tile2i alignedSource = Align(source);
            Tile2i alignedGoal = Align(goal);
            var horizontalFirst = new Tile2i(alignedGoal.X, alignedSource.Y);
            yield return BuildOrthogonalSpine(
                alignedSource, horizontalFirst, alignedGoal);
            var verticalFirst = new Tile2i(alignedSource.X, alignedGoal.Y);
            yield return BuildOrthogonalSpine(
                alignedSource, verticalFirst, alignedGoal);
        }

        private static List<Tile2i> BuildOrthogonalSpine(
            Tile2i start, Tile2i bend, Tile2i end)
        {
            var result = new List<Tile2i>();
            AddSegment(result, start, bend);
            AddSegment(result, bend, end);
            return result;
        }

        private static void AddSegment(
            List<Tile2i> result, Tile2i start, Tile2i end)
        {
            int dx = Math.Sign(end.X - start.X) * 4;
            int dy = Math.Sign(end.Y - start.Y) * 4;
            Tile2i current = start;
            while (true)
            {
                if (result.Count == 0 || result[result.Count - 1] != current)
                    result.Add(current);
                if (current == end)
                    return;
                current = new Tile2i(
                    dx == 0 ? current.X : current.X + dx,
                    dy == 0 ? current.Y : current.Y + dy);
            }
        }

        private static void AddOriginBulb(
            HashSet<Tile2i> result,
            Tile2i center,
            int halfWidth,
            PolygonTerrainArea2i area,
            int generatedAreaMargin)
        {
            int radius = ((Math.Max(4, halfWidth) + 3) / 4) * 4;
            for (int y = -radius; y <= radius; y += 4)
                for (int x = -radius; x <= radius; x += 4)
                {
                    Tile2i origin = center + new RelTile2i(x, y);
                    if (IsAuthorizedOrigin(origin, area, generatedAreaMargin))
                        result.Add(origin);
                }
        }

        private static bool IsAuthorizedOrigin(
            Tile2i origin,
            PolygonTerrainArea2i area,
            int margin)
            => IsAuthorizedTile(origin, area, margin)
                && IsAuthorizedTile(origin + new RelTile2i(3, 0), area, margin)
                && IsAuthorizedTile(origin + new RelTile2i(0, 3), area, margin)
                && IsAuthorizedTile(origin + new RelTile2i(3, 3), area, margin);

        private static bool IsAuthorizedTile(
            Tile2i tile, PolygonTerrainArea2i area, int margin)
        {
            if (area.ContainsTile(tile))
                return true;
            for (int dx = -margin; dx <= margin; dx++)
            {
                int remaining = margin - Math.Abs(dx);
                for (int dy = -remaining; dy <= remaining; dy++)
                    if (area.ContainsTile(tile + new RelTile2i(dx, dy)))
                        return true;
            }
            return false;
        }

        private static Tile2i Align(Tile2i tile)
            => new Tile2i(tile.X & -4, tile.Y & -4);

        private static long Manhattan(Tile2i left, Tile2i right)
            => Math.Abs((long)left.X - right.X)
                + Math.Abs((long)left.Y - right.Y);
    }
}

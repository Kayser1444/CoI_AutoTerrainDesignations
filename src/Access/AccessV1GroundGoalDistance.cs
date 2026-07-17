using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    /// <summary>
    /// Reverse ground-distance field for V1 tower goals. It mirrors V1's
    /// captured ground/cleanup connectivity rather than treating the snapshot
    /// rectangle as freely traversable.
    /// </summary>
    internal sealed class AccessV1GroundGoalDistance
    {
        private static readonly RelTile2i[] s_directions =
        {
            new RelTile2i(1, 0), new RelTile2i(-1, 0),
            new RelTile2i(0, 1), new RelTile2i(0, -1),
            new RelTile2i(1, 1), new RelTile2i(1, -1),
            new RelTile2i(-1, 1), new RelTile2i(-1, -1),
        };

        private readonly HashSet<Tile2i> m_groundNodes;
        private readonly Dictionary<Tile2i, AccessPropCleanupInfo> m_cleanupByTile;
        private readonly Dictionary<Tile2i, float> m_distances;

        public int ReachableNodeCount => m_distances.Count;

        public AccessV1GroundGoalDistance(
            IEnumerable<Tile2i> groundNodes,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> cleanupByTile,
            IEnumerable<Tile2i> goals)
        {
            m_groundNodes = new HashSet<Tile2i>(groundNodes);
            m_cleanupByTile = new Dictionary<Tile2i, AccessPropCleanupInfo>();
            foreach (KeyValuePair<Tile2i, AccessPropCleanupInfo> pair in cleanupByTile)
                if (!m_groundNodes.Contains(pair.Key) && pair.Value.IsEligible)
                    m_cleanupByTile.Add(pair.Key, pair.Value);
            m_distances = BuildDistances(goals);
        }

        public bool TryGetDistance(Tile2i tile, out float distance)
            => m_distances.TryGetValue(tile, out distance);

        public IEnumerable<Tile2i> EnumerateDescendingSteps(Tile2i tile)
        {
            if (!m_distances.TryGetValue(tile, out float currentDistance))
                yield break;
            for (int index = 0; index < s_directions.Length; index++)
            {
                Tile2i next = tile + s_directions[index];
                if (!CanTraverse(tile, next)
                    || !m_distances.TryGetValue(next, out float nextDistance))
                    continue;
                float stepCost = tile.X != next.X && tile.Y != next.Y
                    ? 1.41421356237f : 1f;
                if (Math.Abs(currentDistance - stepCost - nextDistance)
                    <= 0.0001f)
                    yield return next;
            }
        }

        private Dictionary<Tile2i, float> BuildDistances(IEnumerable<Tile2i> goals)
        {
            var distances = new Dictionary<Tile2i, float>();
            var queue = new SortedDictionary<float, Queue<Tile2i>>();
            foreach (Tile2i goal in goals)
            {
                if (!IsTraversable(goal) || distances.ContainsKey(goal)) continue;
                distances.Add(goal, 0f);
                Enqueue(goal, 0f);
            }

            while (queue.Count > 0)
            {
                KeyValuePair<float, Queue<Tile2i>> first = First(queue);
                Tile2i current = first.Value.Dequeue();
                if (first.Value.Count == 0) queue.Remove(first.Key);
                if (!distances.TryGetValue(current, out float currentDistance)
                    || Math.Abs(currentDistance - first.Key) > 0.0001f)
                    continue;

                for (int index = 0; index < s_directions.Length; index++)
                {
                    Tile2i next = current + s_directions[index];
                    if (!CanTraverse(current, next)) continue;
                    float stepCost = current.X != next.X && current.Y != next.Y
                        ? 1.41421356237f : 1f;
                    float nextDistance = currentDistance + stepCost;
                    if (distances.TryGetValue(next, out float old)
                        && old <= nextDistance + 0.0001f)
                        continue;
                    distances[next] = nextDistance;
                    Enqueue(next, nextDistance);
                }
            }
            return distances;

            void Enqueue(Tile2i tile, float distance)
            {
                if (!queue.TryGetValue(distance, out Queue<Tile2i> bucket))
                {
                    bucket = new Queue<Tile2i>();
                    queue.Add(distance, bucket);
                }
                bucket.Enqueue(tile);
            }
        }

        private bool IsTraversable(Tile2i tile)
            => m_groundNodes.Contains(tile) || m_cleanupByTile.ContainsKey(tile);

        private bool CanTraverse(Tile2i from, Tile2i to)
        {
            int dx = Math.Abs(from.X - to.X);
            int dy = Math.Abs(from.Y - to.Y);
            if (dx == 0 || dy == 0)
                return dx + dy == 1 && CanTraverseCardinal(from, to);
            if (dx != 1 || dy != 1) return false;

            // V1 diagonals require both cardinal corridors to be ordinary G.
            // Generated-history conflicts are omitted from this static field.
            Tile2i sideX = new Tile2i(to.X, from.Y);
            Tile2i sideY = new Tile2i(from.X, to.Y);
            return m_groundNodes.Contains(sideX) && m_groundNodes.Contains(sideY)
                && CanTraverseCardinal(from, to);
        }

        private bool CanTraverseCardinal(Tile2i from, Tile2i to)
        {
            if (!IsTraversable(from) || !IsTraversable(to)) return false;
            if (m_groundNodes.Contains(from) || m_groundNodes.Contains(to)) return true;

            AccessPropCleanupInfo fromInfo = m_cleanupByTile[from];
            AccessPropCleanupInfo toInfo = m_cleanupByTile[to];
            if (fromInfo.HasTreeCleanup && !fromInfo.HasDenseDebrisCleanup
                && toInfo.HasTreeCleanup && !toInfo.HasDenseDebrisCleanup)
                return true;
            for (int fromIndex = 0; fromIndex < fromInfo.Samples.Count; fromIndex++)
                for (int toIndex = 0; toIndex < toInfo.Samples.Count; toIndex++)
                    if (fromInfo.Samples[fromIndex].CleanupObjectKey
                        == toInfo.Samples[toIndex].CleanupObjectKey)
                        return true;
            return false;
        }

        private static KeyValuePair<float, Queue<Tile2i>> First(
            SortedDictionary<float, Queue<Tile2i>> items)
        {
            foreach (KeyValuePair<float, Queue<Tile2i>> pair in items)
                return pair;
            throw new InvalidOperationException("Ground goal-distance queue is empty.");
        }
    }
}

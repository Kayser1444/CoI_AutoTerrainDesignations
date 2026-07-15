using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    /// <summary>
    /// Immutable clearance-two ground view captured from the concrete Mega
    /// pathability mask. Cleanup entries are keyed by vehicle-center tile, so
    /// every required footprint blocker must be removable at that center.
    /// </summary>
    internal sealed class AccessV2GroundGraph
    {
        private static readonly RelTile2i[] s_directions =
        {
            new RelTile2i(1, 0),
            new RelTile2i(-1, 0),
            new RelTile2i(0, 1),
            new RelTile2i(0, -1),
        };

        private readonly HashSet<Tile2i> m_groundNodes;
        private readonly Dictionary<Tile2i, AccessPropCleanupInfo> m_cleanupByTile;
        private readonly HashSet<Tile2i> m_goals;
        private readonly Dictionary<Tile2i, int> m_componentByTile;
        private readonly HashSet<int> m_goalComponents;
        private readonly Dictionary<Tile2i, int> m_goalDistanceByTile;

        public int GroundNodeCount => m_groundNodes.Count;
        public int CleanupNodeCount => m_cleanupByTile.Count;
        public int GoalCount => m_goals.Count;

        public AccessV2GroundGraph(
            IEnumerable<Tile2i> groundNodes,
            IEnumerable<Tile2i> goals,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> cleanupByTile)
        {
            m_groundNodes = new HashSet<Tile2i>(groundNodes);
            m_goals = new HashSet<Tile2i>(goals);
            m_cleanupByTile = new Dictionary<Tile2i, AccessPropCleanupInfo>();
            foreach (KeyValuePair<Tile2i, AccessPropCleanupInfo> pair in cleanupByTile)
                if (!m_groundNodes.Contains(pair.Key) && pair.Value.IsEligible)
                    m_cleanupByTile.Add(pair.Key, pair.Value);
            BuildComponents(out m_componentByTile, out m_goalComponents);
            m_goalDistanceByTile = BuildGoalDistances();
        }

        public bool IsGround(Tile2i tile) => m_groundNodes.Contains(tile);

        public bool IsCleanupGround(Tile2i tile)
            => m_cleanupByTile.ContainsKey(tile);

        public bool IsTraversable(Tile2i tile)
            => IsGround(tile) || IsCleanupGround(tile);

        public bool IsGoal(Tile2i tile) => m_goals.Contains(tile);

        public bool TryGetGoalDistance(Tile2i tile, out int distance)
            => m_goalDistanceByTile.TryGetValue(tile, out distance);

        public bool CanTraverse(Tile2i from, Tile2i to)
        {
            if (Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) != 1
                || !IsTraversable(from)
                || !IsTraversable(to))
                return false;
            if (m_groundNodes.Contains(from) || m_groundNodes.Contains(to))
                return true;
            AccessPropCleanupInfo fromInfo = m_cleanupByTile[from];
            AccessPropCleanupInfo toInfo = m_cleanupByTile[to];
            if (fromInfo.HasTreeCleanup && !fromInfo.HasDenseDebrisCleanup
                && toInfo.HasTreeCleanup && !toInfo.HasDenseDebrisCleanup)
                return true;
            return ShareCleanupObject(fromInfo, toInfo);
        }

        public IReadOnlyCollection<string> CollectUnchargedCleanupKeys(
            IEnumerable<Tile2i> footprintCenters,
            ISet<string> alreadyCharged)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (Tile2i tile in footprintCenters)
            {
                if (!m_cleanupByTile.TryGetValue(
                        tile, out AccessPropCleanupInfo info))
                    continue;
                for (int index = 0; index < info.Samples.Count; index++)
                {
                    string key = info.Samples[index].CleanupObjectKey;
                    if (!alreadyCharged.Contains(key)) keys.Add(key);
                }
            }
            return keys;
        }

        public bool TryValidateGoalEscape(
            IEnumerable<Tile2i> requiredCenters,
            AccessV2History history,
            float cleanupCostScale,
            out IReadOnlyCollection<string> cleanupKeys,
            out float cleanupCost)
            => TryValidateEscape(
                requiredCenters, history, cleanupCostScale,
                requireGoalComponent: true,
                out cleanupKeys, out cleanupCost);

        public bool TryValidateLocalEscape(
            IEnumerable<Tile2i> requiredCenters,
            AccessV2History history,
            float cleanupCostScale,
            out IReadOnlyCollection<string> cleanupKeys,
            out float cleanupCost)
            => TryValidateEscape(
                requiredCenters, history, cleanupCostScale,
                requireGoalComponent: false,
                out cleanupKeys, out cleanupCost);

        private bool TryValidateEscape(
            IEnumerable<Tile2i> requiredCenters,
            AccessV2History history,
            float cleanupCostScale,
            bool requireGoalComponent,
            out IReadOnlyCollection<string> cleanupKeys,
            out float cleanupCost)
        {
            var centers = new HashSet<Tile2i>(requiredCenters);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            float cost = 0f;
            cleanupKeys = keys;
            cleanupCost = 0f;
            if (centers.Count == 0) return false;

            int component = -1;
            foreach (Tile2i center in centers)
            {
                if (!m_componentByTile.TryGetValue(center, out int found))
                    return false;
                if (component < 0) component = found;
                else if (component != found) return false;

                if (!m_cleanupByTile.TryGetValue(
                        center, out AccessPropCleanupInfo info))
                    continue;
                if (info.Samples.Count == 0)
                {
                    AddKey(
                        $"cleanup-origin:{info.Origin.X},{info.Origin.Y}",
                        isTree: false);
                    continue;
                }
                for (int index = 0; index < info.Samples.Count; index++)
                {
                    AccessPropSample sample = info.Samples[index];
                    AddKey(sample.CleanupObjectKey, sample.IsTree);
                }
            }
            cleanupCost = cost;
            return !requireGoalComponent || m_goalComponents.Contains(component);

            void AddKey(string key, bool isTree)
            {
                if (history.ContainsCleanupKey(key) || !keys.Add(key))
                    return;
                cost += cleanupCostScale * AccessPropCleanupPolicy
                    .GetCleanupLandscapingCost(isTree);
            }
        }

        public HashSet<Tile2i> Flood(Tile2i seed)
        {
            var reached = new HashSet<Tile2i>();
            if (!IsTraversable(seed)) return reached;
            var queue = new Queue<Tile2i>();
            reached.Add(seed);
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                for (int index = 0; index < s_directions.Length; index++)
                {
                    Tile2i next = current + s_directions[index];
                    if (reached.Contains(next) || !CanTraverse(current, next))
                        continue;
                    reached.Add(next);
                    queue.Enqueue(next);
                }
            }
            return reached;
        }

        private static bool ShareCleanupObject(
            AccessPropCleanupInfo left,
            AccessPropCleanupInfo right)
        {
            for (int leftIndex = 0; leftIndex < left.Samples.Count; leftIndex++)
                for (int rightIndex = 0; rightIndex < right.Samples.Count; rightIndex++)
                    if (left.Samples[leftIndex].CleanupObjectKey
                        == right.Samples[rightIndex].CleanupObjectKey)
                        return true;
            return false;
        }

        private void BuildComponents(
            out Dictionary<Tile2i, int> componentByTile,
            out HashSet<int> goalComponents)
        {
            componentByTile = new Dictionary<Tile2i, int>();
            goalComponents = new HashSet<int>();
            var all = new HashSet<Tile2i>(m_groundNodes);
            all.UnionWith(m_cleanupByTile.Keys);
            int component = 0;
            foreach (Tile2i seed in all)
            {
                if (componentByTile.ContainsKey(seed)) continue;
                var queue = new Queue<Tile2i>();
                componentByTile.Add(seed, component);
                queue.Enqueue(seed);
                bool containsGoal = false;
                while (queue.Count > 0)
                {
                    Tile2i current = queue.Dequeue();
                    if (m_goals.Contains(current)) containsGoal = true;
                    for (int index = 0; index < s_directions.Length; index++)
                    {
                        Tile2i next = current + s_directions[index];
                        if (componentByTile.ContainsKey(next)
                            || !all.Contains(next)
                            || !CanTraverse(current, next))
                            continue;
                        componentByTile.Add(next, component);
                        queue.Enqueue(next);
                    }
                }
                if (containsGoal) goalComponents.Add(component);
                component++;
            }
        }

        private Dictionary<Tile2i, int> BuildGoalDistances()
        {
            var distances = new Dictionary<Tile2i, int>();
            var queue = new Queue<Tile2i>();
            foreach (Tile2i goal in m_goals)
            {
                if (!IsTraversable(goal) || distances.ContainsKey(goal))
                    continue;
                distances.Add(goal, 0);
                queue.Enqueue(goal);
            }
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                int nextDistance = distances[current] + 1;
                for (int index = 0; index < s_directions.Length; index++)
                {
                    Tile2i next = current + s_directions[index];
                    if (distances.ContainsKey(next)
                        || !CanTraverse(current, next))
                        continue;
                    distances.Add(next, nextDistance);
                    queue.Enqueue(next);
                }
            }
            return distances;
        }
    }
}

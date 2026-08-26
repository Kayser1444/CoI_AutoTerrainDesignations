using System;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly RelTile2i[] s_cardinalDirections =
        {
            new RelTile2i(1, 0),
            new RelTile2i(-1, 0),
            new RelTile2i(0, 1),
            new RelTile2i(0, -1),
        };
        private static readonly RelTile2i[] s_allDirections =
        {
            new RelTile2i(1, 0), new RelTile2i(-1, 0),
            new RelTile2i(0, 1), new RelTile2i(0, -1),
            new RelTile2i(1, 1), new RelTile2i(1, -1),
            new RelTile2i(-1, 1), new RelTile2i(-1, -1),
        };
        internal const float DiagonalCost = 1.41421356237f;

        private readonly HashSet<Tile2i> m_groundNodes;
        private readonly HashSet<Tile2i> m_projectedFixedNodes;
        private readonly Dictionary<Tile2i, AccessPropCleanupInfo> m_cleanupByTile;
        private readonly Dictionary<Tile2i, AccessPropCleanupInfo>
            m_generatedClearableByTile;
        private readonly HashSet<Tile2i> m_goals;
        private readonly Dictionary<Tile2i, int> m_componentByTile;
        private readonly HashSet<int> m_goalComponents;
        private readonly Dictionary<Tile2i, float> m_goalDistanceByTile;
        private readonly float m_cleanupUnitCost;

        public int GroundNodeCount => m_groundNodes.Count;
        public int CleanupNodeCount => m_cleanupByTile.Count;
        public int GoalCount => m_goals.Count;
        public IReadOnlyDictionary<Tile2i, float> GoalDistances
            => m_goalDistanceByTile;
        internal IEnumerable<Tile2i> TraversableNodes
            => m_componentByTile.Keys;

        public AccessV2GroundGraph(
            IEnumerable<Tile2i> groundNodes,
            IEnumerable<Tile2i> goals,
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> cleanupByTile,
            IEnumerable<Tile2i>? projectedFixedNodes = null,
            float cleanupUnitCost = 8f)
        {
            m_cleanupUnitCost = cleanupUnitCost;
            m_groundNodes = new HashSet<Tile2i>(groundNodes);
            m_projectedFixedNodes = projectedFixedNodes != null
                ? new HashSet<Tile2i>(projectedFixedNodes)
                : new HashSet<Tile2i>();
            m_goals = new HashSet<Tile2i>(goals);
            m_cleanupByTile = new Dictionary<Tile2i, AccessPropCleanupInfo>();
            m_generatedClearableByTile =
                new Dictionary<Tile2i, AccessPropCleanupInfo>();
            foreach (KeyValuePair<Tile2i, AccessPropCleanupInfo> pair in cleanupByTile)
                if (!m_groundNodes.Contains(pair.Key) && pair.Value.IsEligible)
                    m_cleanupByTile.Add(pair.Key, pair.Value);
                else if (!m_groundNodes.Contains(pair.Key)
                    && pair.Value.IsEligibleWithinGeneratedV
                    && pair.Value.HasDenseDebrisCleanup)
                    m_generatedClearableByTile.Add(pair.Key, pair.Value);
            BuildComponents(out m_componentByTile, out m_goalComponents);
            m_goalDistanceByTile = BuildGoalDistances();
        }

        private AccessV2GroundGraph(
            AccessV2GroundGraph source,
            IEnumerable<Tile2i> additionalGoals)
        {
            m_groundNodes = source.m_groundNodes;
            m_projectedFixedNodes = source.m_projectedFixedNodes;
            m_cleanupByTile = source.m_cleanupByTile;
            m_generatedClearableByTile = source.m_generatedClearableByTile;
            m_goals = new HashSet<Tile2i>(source.m_goals);
            m_componentByTile = source.m_componentByTile;
            m_goalComponents = new HashSet<int>(source.m_goalComponents);
            m_cleanupUnitCost = source.m_cleanupUnitCost;
            var newGoals = new HashSet<Tile2i>();
            foreach (Tile2i goal in additionalGoals)
                if (IsTraversable(goal))
                {
                    m_goals.Add(goal);
                    if (!source.m_goals.Contains(goal))
                        newGoals.Add(goal);
                    if (m_componentByTile.TryGetValue(
                            goal, out int component))
                        m_goalComponents.Add(component);
                }
            m_goalDistanceByTile = BuildGoalDistancesWithAdditionalGoals(
                source.m_goalDistanceByTile, newGoals);
        }

        internal AccessV2GroundGraph WithAdditionalGoals(
            IEnumerable<Tile2i> additionalGoals)
            => new AccessV2GroundGraph(this, additionalGoals);

        public bool IsGround(Tile2i tile) => m_groundNodes.Contains(tile);

        internal bool IsProjectedFixedGround(Tile2i tile)
            => m_projectedFixedNodes.Contains(tile);

        public bool IsCleanupGround(Tile2i tile)
            => m_cleanupByTile.ContainsKey(tile);

        public bool IsTraversable(Tile2i tile)
            => IsGround(tile) || IsCleanupGround(tile);

        internal bool IsTraversable(
            Tile2i tile,
            AccessV2History history)
            => IsTraversable(tile)
                || IsClearedByGeneratedWork(tile, history);

        public bool IsGoal(Tile2i tile) => m_goals.Contains(tile);

        public bool TryGetGoalDistance(Tile2i tile, out float distance)
            => m_goalDistanceByTile.TryGetValue(tile, out distance);

        internal bool IsGoalConnected(Tile2i tile)
            => m_componentByTile.TryGetValue(tile, out int component)
                && m_goalComponents.Contains(component);

        internal bool TryGetComponentId(Tile2i tile, out int component)
            => m_componentByTile.TryGetValue(tile, out component);

        internal bool IsInComponent(Tile2i tile, int component)
            => m_componentByTile.TryGetValue(tile, out int found)
                && found == component;

        internal HashSet<int> CollectGoalOrExitComponents(
            Func<Tile2i, bool> canExitToGeneratedV)
        {
            var components = new HashSet<int>(m_goalComponents);
            foreach (KeyValuePair<Tile2i, int> pair in m_componentByTile)
                if (!components.Contains(pair.Value)
                    && canExitToGeneratedV(pair.Key))
                    components.Add(pair.Value);
            return components;
        }

        internal bool IsInComponent(
            Tile2i tile,
            ISet<int> components)
            => m_componentByTile.TryGetValue(
                    tile, out int component)
                && components.Contains(component);

        public bool CanTraverse(Tile2i from, Tile2i to)
        {
            int dx = Math.Abs(from.X - to.X);
            int dy = Math.Abs(from.Y - to.Y);
            if (dx + dy == 1)
                return CanTraverseCardinal(from, to);
            if (dx != 1 || dy != 1)
                return false;
            Tile2i sideX = new Tile2i(to.X, from.Y);
            Tile2i sideY = new Tile2i(from.X, to.Y);
            return CanTraverseCardinal(from, sideX)
                && CanTraverseCardinal(sideX, to)
                && CanTraverseCardinal(from, sideY)
                && CanTraverseCardinal(sideY, to);
        }

        internal bool CanTraverse(
            Tile2i from,
            Tile2i to,
            AccessV2History history)
        {
            int dx = Math.Abs(from.X - to.X);
            int dy = Math.Abs(from.Y - to.Y);
            if (dx + dy == 1)
                return CanTraverseCardinal(from, to, history);
            if (dx != 1 || dy != 1)
                return false;
            Tile2i sideX = new Tile2i(to.X, from.Y);
            Tile2i sideY = new Tile2i(from.X, to.Y);
            return CanTraverseCardinal(from, sideX, history)
                && CanTraverseCardinal(sideX, to, history)
                && CanTraverseCardinal(from, sideY, history)
                && CanTraverseCardinal(sideY, to, history);
        }

        public static float GetStepCost(Tile2i from, Tile2i to)
            => from.X != to.X && from.Y != to.Y ? DiagonalCost : 1f;

        public static IReadOnlyList<Tile2i> GetSweptCenters(
            Tile2i from, Tile2i to)
        {
            if (from.X == to.X || from.Y == to.Y)
                return new[] { to };
            return new[]
            {
                to,
                new Tile2i(to.X, from.Y),
                new Tile2i(from.X, to.Y),
            };
        }

        private bool CanTraverseCardinal(Tile2i from, Tile2i to)
        {
            if (Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) != 1
                || !IsTraversable(from)
                || !IsTraversable(to))
                return false;
            if (m_groundNodes.Contains(from) || m_groundNodes.Contains(to))
                return true;
            // Both centers are eligible cleanup ground. Clearing independent,
            // adjacent removable props makes their footprints one continuous
            // ground corridor; they do not need to share a cleanup object.
            return true;
        }

        private bool CanTraverseCardinal(
            Tile2i from,
            Tile2i to,
            AccessV2History history)
        {
            if (Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) != 1
                || !IsTraversable(from, history)
                || !IsTraversable(to, history))
                return false;
            if (IsClearedByGeneratedWork(from, history)
                || IsClearedByGeneratedWork(to, history))
                return true;
            return CanTraverseCardinal(from, to);
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

            var baseComponents = new HashSet<int>();
            var overlayCenters = new HashSet<Tile2i>();
            foreach (Tile2i center in centers)
            {
                if (!IsTraversable(center, history))
                    return false;
                if (m_componentByTile.TryGetValue(center, out int found))
                    baseComponents.Add(found);
                else
                    overlayCenters.Add(center);

                if (!TryGetCleanupInfo(
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
            if (baseComponents.Count == 0)
                return false;
            if (overlayCenters.Count > 0
                && !EveryOverlayCenterReachesBase(
                    centers, overlayCenters, history))
                return false;
            if (baseComponents.Count > 1
                && !AllRequiredCentersConnected(centers, history))
                return false;
            cleanupCost = cost;
            return !requireGoalComponent
                || baseComponents.Any(item => m_goalComponents.Contains(item));

            void AddKey(string key, bool isTree)
            {
                if (history.ContainsCleanupKey(key) || !keys.Add(key))
                    return;
                cost += cleanupCostScale
                    * (isTree ? 0f : m_cleanupUnitCost);
            }
        }

        private bool IsClearedByGeneratedWork(
            Tile2i tile,
            AccessV2History history)
        {
            if (!m_generatedClearableByTile.TryGetValue(
                    tile, out AccessPropCleanupInfo info))
                return false;
            bool foundDense = false;
            if (info.Samples.Count == 0)
                return history.ContainsCleanupKey(
                    $"cleanup-origin:{info.Origin.X},{info.Origin.Y}");
            for (int index = 0; index < info.Samples.Count; index++)
            {
                AccessPropSample sample = info.Samples[index];
                if (!sample.IsDenseDebris)
                    continue;
                foundDense = true;
                if (!sample.IsRemovable
                    || !history.ContainsCleanupKey(sample.CleanupObjectKey))
                    return false;
            }
            return foundDense;
        }

        private bool TryGetCleanupInfo(
            Tile2i tile,
            out AccessPropCleanupInfo info)
        {
            if (m_cleanupByTile.TryGetValue(tile, out info))
                return true;
            return m_generatedClearableByTile.TryGetValue(tile, out info);
        }

        private bool EveryOverlayCenterReachesBase(
            ISet<Tile2i> centers,
            ISet<Tile2i> overlayCenters,
            AccessV2History history)
        {
            var reached = new HashSet<Tile2i>();
            var queue = new Queue<Tile2i>();
            foreach (Tile2i center in centers)
                if (m_componentByTile.ContainsKey(center))
                {
                    reached.Add(center);
                    queue.Enqueue(center);
                }
            FloodRequiredCenters(centers, history, reached, queue);
            return overlayCenters.All(reached.Contains);
        }

        private bool AllRequiredCentersConnected(
            ISet<Tile2i> centers,
            AccessV2History history)
        {
            var reached = new HashSet<Tile2i>();
            var queue = new Queue<Tile2i>();
            Tile2i seed = centers.First();
            reached.Add(seed);
            queue.Enqueue(seed);
            FloodRequiredCenters(centers, history, reached, queue);
            return reached.Count == centers.Count;
        }

        private void FloodRequiredCenters(
            ISet<Tile2i> centers,
            AccessV2History history,
            ISet<Tile2i> reached,
            Queue<Tile2i> queue)
        {
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                for (int index = 0; index < s_cardinalDirections.Length; index++)
                {
                    Tile2i next = current + s_cardinalDirections[index];
                    if (!centers.Contains(next)
                        || reached.Contains(next)
                        || !CanTraverse(current, next, history))
                        continue;
                    reached.Add(next);
                    queue.Enqueue(next);
                }
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
                for (int index = 0; index < s_cardinalDirections.Length; index++)
                {
                    Tile2i next = current + s_cardinalDirections[index];
                    if (reached.Contains(next) || !CanTraverse(current, next))
                        continue;
                    reached.Add(next);
                    queue.Enqueue(next);
                }
            }
            return reached;
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
                    for (int index = 0; index < s_cardinalDirections.Length; index++)
                    {
                        Tile2i next = current + s_cardinalDirections[index];
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

        private Dictionary<Tile2i, float> BuildGoalDistances()
        {
            var distances = new Dictionary<Tile2i, float>();
            var queue = new SortedDictionary<float, Queue<Tile2i>>();
            foreach (Tile2i goal in m_goals)
            {
                if (!IsTraversable(goal) || distances.ContainsKey(goal))
                    continue;
                distances.Add(goal, 0);
                Enqueue(goal, 0f);
            }
            while (queue.Count > 0)
            {
                KeyValuePair<float, Queue<Tile2i>> first = queue.First();
                Tile2i current = first.Value.Dequeue();
                if (first.Value.Count == 0) queue.Remove(first.Key);
                if (!distances.TryGetValue(current, out float currentDistance)
                    || Math.Abs(currentDistance - first.Key) > 0.0001f)
                    continue;
                for (int index = 0; index < s_allDirections.Length; index++)
                {
                    Tile2i next = current + s_allDirections[index];
                    if (!CanTraverse(current, next))
                        continue;
                    float nextDistance = currentDistance
                        + GetStepCost(current, next);
                    if (distances.TryGetValue(next, out float old)
                        && old <= nextDistance + 0.0001f)
                        continue;
                    distances[next] = nextDistance;
                    Enqueue(next, nextDistance);
                }
            }
            return distances;

            void Enqueue(Tile2i tile, float cost)
            {
                if (!queue.TryGetValue(cost, out Queue<Tile2i> bucket))
                {
                    bucket = new Queue<Tile2i>();
                    queue.Add(cost, bucket);
                }
                bucket.Enqueue(tile);
            }
        }

        private Dictionary<Tile2i, float> BuildGoalDistancesWithAdditionalGoals(
            IReadOnlyDictionary<Tile2i, float> sourceDistances,
            IEnumerable<Tile2i> additionalGoals)
        {
            var distances = new Dictionary<Tile2i, float>();
            foreach (KeyValuePair<Tile2i, float> pair in sourceDistances)
                distances.Add(pair.Key, pair.Value);
            var queue = new SortedDictionary<float, Queue<Tile2i>>();
            foreach (Tile2i goal in additionalGoals)
            {
                if (!distances.TryGetValue(goal, out float existing)
                    || existing > 0f)
                {
                    distances[goal] = 0f;
                    Enqueue(goal, 0f);
                }
            }

            while (queue.Count > 0)
            {
                KeyValuePair<float, Queue<Tile2i>> first = queue.First();
                Tile2i current = first.Value.Dequeue();
                if (first.Value.Count == 0) queue.Remove(first.Key);
                if (!distances.TryGetValue(current, out float currentDistance)
                    || Math.Abs(currentDistance - first.Key) > 0.0001f)
                    continue;
                for (int index = 0; index < s_allDirections.Length; index++)
                {
                    Tile2i next = current + s_allDirections[index];
                    if (!CanTraverse(current, next))
                        continue;
                    float nextDistance = currentDistance
                        + GetStepCost(current, next);
                    if (distances.TryGetValue(next, out float old)
                        && old <= nextDistance + 0.0001f)
                        continue;
                    distances[next] = nextDistance;
                    Enqueue(next, nextDistance);
                }
            }
            return distances;

            void Enqueue(Tile2i tile, float cost)
            {
                if (!queue.TryGetValue(cost, out Queue<Tile2i> bucket))
                {
                    bucket = new Queue<Tile2i>();
                    queue.Add(cost, bucket);
                }
                bucket.Enqueue(tile);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal static class AccessV2CostModel
    {
        public static float GetMinimumVTravelCostPerTile(float fixedOriginCost)
            => 1f + Math.Max(0f, fixedOriginCost) / 4f;

        public static float GetCenterSpokeCost(float fixedOriginCost)
            => 2f * GetMinimumVTravelCostPerTile(fixedOriginCost);
    }

    /// <summary>
    /// Request-scoped relaxed lower bound for V2 canonical centers. Tower
    /// ground contributes its exact G suffix; fixed frontages contribute the
    /// exact canonical center at which their match test can succeed. Cardinal
    /// propagation ignores all V feasibility and therefore cannot overestimate.
    /// </summary>
    internal sealed class AccessV2PotentialField
    {
        private static readonly RelTile2i[] s_directions =
        {
            new RelTile2i(1, 0), new RelTile2i(-1, 0),
            new RelTile2i(0, 1), new RelTile2i(0, -1),
        };

        private readonly Tile2i m_min;
        private readonly int m_width;
        private readonly int m_height;
        private readonly float[] m_costs;

        public AccessV2PotentialField(
            Tile2i boundsMin,
            Tile2i boundsMax,
            AccessV2GroundGraph? ground,
            IReadOnlyList<AccessV2FixedFrontage> fixedGoals,
            float minimumVTravelCostPerTile = 1f)
        {
            m_min = boundsMin;
            m_width = Math.Max(0, boundsMax.X - boundsMin.X + 1);
            m_height = Math.Max(0, boundsMax.Y - boundsMin.Y + 1);
            m_costs = new float[m_width * m_height];
            minimumVTravelCostPerTile = Math.Max(
                1f, minimumVTravelCostPerTile);
            for (int index = 0; index < m_costs.Length; index++)
                m_costs[index] = float.PositiveInfinity;

            var queue = new SortedDictionary<float, Queue<Tile2i>>();
            if (ground != null)
                foreach (KeyValuePair<Tile2i, float> pair in ground.GoalDistances)
                    Seed(pair.Key, pair.Value);
            for (int index = 0; index < fixedGoals.Count; index++)
            {
                AccessV2FixedFrontage goal = fixedGoals[index];
                Tile2i matchCenter = GetCanonicalCenter(goal.State)
                    + new RelTile2i(
                        goal.ExposedDirection.X,
                        goal.ExposedDirection.Y);
                Seed(matchCenter, goal.TerminalCost);
            }

            while (queue.Count > 0)
            {
                KeyValuePair<float, Queue<Tile2i>> first = First(queue);
                Tile2i current = first.Value.Dequeue();
                if (first.Value.Count == 0) queue.Remove(first.Key);
                if (!TryIndex(current, out int currentIndex)
                    || Math.Abs(m_costs[currentIndex] - first.Key) > 0.0001f)
                    continue;
                for (int direction = 0;
                    direction < s_directions.Length;
                    direction++)
                {
                    Tile2i next = current + s_directions[direction];
                    Seed(next, first.Key + minimumVTravelCostPerTile);
                }
            }

            void Seed(Tile2i tile, float cost)
            {
                if (cost < 0f || !TryIndex(tile, out int index)
                    || cost >= m_costs[index] - 0.0001f)
                    return;
                m_costs[index] = cost;
                if (!queue.TryGetValue(cost, out Queue<Tile2i> bucket))
                {
                    bucket = new Queue<Tile2i>();
                    queue.Add(cost, bucket);
                }
                bucket.Enqueue(tile);
            }
        }

        public float GetPotential(AccessV2BandState state)
            => GetPotential(GetCanonicalCenter(state));

        public float GetPotential(Tile2i center)
            => TryIndex(center, out int index)
                && !float.IsPositiveInfinity(m_costs[index])
                    ? m_costs[index]
                    : 0f;

        internal static Tile2i GetCanonicalCenter(AccessV2BandState state)
            => state.Axis == AccessV2TravelAxis.X
                ? state.Anchor + new RelTile2i(2, 4)
                : state.Anchor + new RelTile2i(4, 2);

        private bool TryIndex(Tile2i tile, out int index)
        {
            int x = tile.X - m_min.X;
            int y = tile.Y - m_min.Y;
            if (x < 0 || x >= m_width || y < 0 || y >= m_height)
            {
                index = -1;
                return false;
            }
            index = y * m_width + x;
            return true;
        }

        private static KeyValuePair<float, Queue<Tile2i>> First(
            SortedDictionary<float, Queue<Tile2i>> queue)
        {
            foreach (KeyValuePair<float, Queue<Tile2i>> pair in queue)
                return pair;
            throw new InvalidOperationException("Potential queue is empty.");
        }
    }

    /// <summary>
    /// Lower bound for G states whose captured ground component has no tower
    /// goal. The state may travel cheaply within its current G component, then
    /// pays the relaxed V potential from the best reachable escape point plus
    /// the unavoidable fixed overhead of the first generated width-two band.
    /// Physical G-to-V feasibility and the nonnegative center spoke remain
    /// omitted, preserving a lower bound.
    /// </summary>
    internal sealed class AccessV2GroundEscapePotentialField
    {
        private static readonly RelTile2i[] s_directions =
        {
            new RelTile2i(1, 0), new RelTile2i(-1, 0),
            new RelTile2i(0, 1), new RelTile2i(0, -1),
            new RelTile2i(1, 1), new RelTile2i(1, -1),
            new RelTile2i(-1, 1), new RelTile2i(-1, -1),
        };

        private readonly Dictionary<Tile2i, float> m_costs =
            new Dictionary<Tile2i, float>();

        public AccessV2GroundEscapePotentialField(
            AccessV2GroundGraph ground,
            AccessV2PotentialField vPotential,
            float minimumGeneratedEntryCost = 0f,
            Func<Tile2i, bool>? canExitToGeneratedV = null)
        {
            var queue = new SortedDictionary<float, Queue<Tile2i>>();
            foreach (Tile2i tile in ground.TraversableNodes)
            {
                if (ground.IsGoalConnected(tile)
                    || (canExitToGeneratedV != null
                        && !canExitToGeneratedV(tile)))
                    continue;
                Seed(tile, vPotential.GetPotential(tile)
                    + Math.Max(0f, minimumGeneratedEntryCost));
            }

            while (queue.Count > 0)
            {
                KeyValuePair<float, Queue<Tile2i>> first = First(queue);
                Tile2i current = first.Value.Dequeue();
                if (first.Value.Count == 0) queue.Remove(first.Key);
                if (!m_costs.TryGetValue(current, out float currentCost)
                    || Math.Abs(currentCost - first.Key) > 0.0001f)
                    continue;
                for (int index = 0; index < s_directions.Length; index++)
                {
                    Tile2i next = current + s_directions[index];
                    if (ground.IsGoalConnected(next)
                        || !ground.CanTraverse(current, next))
                        continue;
                    Seed(next, currentCost
                        + AccessV2GroundGraph.GetStepCost(current, next));
                }
            }

            void Seed(Tile2i tile, float cost)
            {
                if (cost < 0f
                    || (m_costs.TryGetValue(tile, out float old)
                        && old <= cost + 0.0001f))
                    return;
                m_costs[tile] = cost;
                if (!queue.TryGetValue(cost, out Queue<Tile2i> bucket))
                {
                    bucket = new Queue<Tile2i>();
                    queue.Add(cost, bucket);
                }
                bucket.Enqueue(tile);
            }
        }

        public float GetPotential(Tile2i tile)
            => m_costs.TryGetValue(tile, out float cost) ? cost : 0f;

        private static KeyValuePair<float, Queue<Tile2i>> First(
            SortedDictionary<float, Queue<Tile2i>> queue)
        {
            foreach (KeyValuePair<float, Queue<Tile2i>> pair in queue)
                return pair;
            throw new InvalidOperationException(
                "Ground escape potential queue is empty.");
        }
    }
}

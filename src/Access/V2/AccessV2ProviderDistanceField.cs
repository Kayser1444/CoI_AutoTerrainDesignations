using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    /// <summary>
    /// Optimistic downstream travel field over accepted, already-established
    /// designation profiles. It preserves exact tower-ground suffix costs but
    /// deliberately omits the full vehicle-clearance test inside providers.
    /// </summary>
    internal sealed class AccessV2ProviderDistanceField
    {
        private static readonly RelTile2i[] s_cardinalDirections =
        {
            new RelTile2i(1, 0), new RelTile2i(-1, 0),
            new RelTile2i(0, 1), new RelTile2i(0, -1),
        };
        private static readonly RelTile2i[] s_allDirections =
        {
            new RelTile2i(1, 0), new RelTile2i(-1, 0),
            new RelTile2i(0, 1), new RelTile2i(0, -1),
            new RelTile2i(1, 1), new RelTile2i(1, -1),
            new RelTile2i(-1, 1), new RelTile2i(-1, -1),
        };

        private readonly Dictionary<Tile2i, float> m_heights =
            new Dictionary<Tile2i, float>();
        private readonly Dictionary<Tile2i, float> m_distances =
            new Dictionary<Tile2i, float>();

        public int ProviderNodeCount => m_heights.Count;
        public int ConnectedNodeCount => m_distances.Count;

        public AccessV2ProviderDistanceField(
            AccessSearchSnapshot snapshot,
            IEnumerable<Tile2i> acceptedProviderOrigins)
        {
            var accepted = new HashSet<Tile2i>(acceptedProviderOrigins);
            var conflicts = new HashSet<Tile2i>();
            foreach (Tile2i origin in accepted)
            {
                if (!snapshot.FixedProfiles.TryGetValue(
                        origin, out AccessHeightProfile profile))
                    continue;
                for (int y = 0; y <= 4; y++)
                    for (int x = 0; x <= 4; x++)
                    {
                        Tile2i center = origin + new RelTile2i(x, y);
                        if (conflicts.Contains(center)) continue;
                        float height = profile.GetHeight2NumeratorAt(x, y) / 32f;
                        if (m_heights.TryGetValue(center, out float existing)
                            && Math.Abs(existing - height) > 0.0001f)
                        {
                            // Conflicting established profiles are not a safe
                            // provider surface at their shared sample.
                            m_heights.Remove(center);
                            conflicts.Add(center);
                            continue;
                        }
                        m_heights[center] = height;
                    }
            }

            var queue = new SortedDictionary<float, Queue<Tile2i>>();
            AccessV2GroundGraph? ground = snapshot.V2GroundGraph;
            if (ground == null) return;

            foreach (KeyValuePair<Tile2i, float> pair in m_heights)
            {
                Tile2i providerCenter = pair.Key;
                if (ground.TryGetGoalDistance(
                        providerCenter, out float overlappingDistance))
                    Seed(providerCenter, overlappingDistance);

                for (int direction = 0; direction < s_cardinalDirections.Length; direction++)
                {
                    Tile2i groundCenter = providerCenter + s_cardinalDirections[direction];
                    if (!ground.TryGetGoalDistance(
                            groundCenter, out float groundDistance)
                        || !snapshot.TryGetGroundHeight2(
                            groundCenter, out int groundHeight2)
                        || Math.Abs(pair.Value - groundHeight2 / 2f)
                            > snapshot.VehicleMaxSteepnessDelta + 0.0001f)
                        continue;
                    Seed(providerCenter, groundDistance + 1);
                }
            }

            while (queue.Count > 0)
            {
                KeyValuePair<float, Queue<Tile2i>> first = queue.First();
                Tile2i current = first.Value.Dequeue();
                if (first.Value.Count == 0) queue.Remove(first.Key);
                if (!m_distances.TryGetValue(current, out float known)
                    || Math.Abs(known - first.Key) > 0.0001f)
                    continue;
                for (int direction = 0; direction < s_allDirections.Length; direction++)
                {
                    Tile2i next = current + s_allDirections[direction];
                    if (!m_heights.TryGetValue(next, out float nextHeight)
                        || Math.Abs(m_heights[current] - nextHeight)
                            > snapshot.VehicleMaxSteepnessDelta + 0.0001f)
                        continue;
                    if (current.X != next.X && current.Y != next.Y)
                    {
                        Tile2i sideX = new Tile2i(next.X, current.Y);
                        Tile2i sideY = new Tile2i(current.X, next.Y);
                        if (!m_heights.TryGetValue(sideX, out float sideXHeight)
                            || !m_heights.TryGetValue(sideY, out float sideYHeight)
                            || Math.Abs(m_heights[current] - sideXHeight)
                                > snapshot.VehicleMaxSteepnessDelta + 0.0001f
                            || Math.Abs(sideXHeight - nextHeight)
                                > snapshot.VehicleMaxSteepnessDelta + 0.0001f
                            || Math.Abs(m_heights[current] - sideYHeight)
                                > snapshot.VehicleMaxSteepnessDelta + 0.0001f
                            || Math.Abs(sideYHeight - nextHeight)
                                > snapshot.VehicleMaxSteepnessDelta + 0.0001f)
                            continue;
                    }
                    Seed(next, known
                        + AccessV2GroundGraph.GetStepCost(current, next));
                }
            }

            void Seed(Tile2i center, float distance)
            {
                if (distance < 0f
                    || (m_distances.TryGetValue(center, out float old)
                        && old <= distance + 0.0001f))
                    return;
                m_distances[center] = distance;
                if (!queue.TryGetValue(distance, out Queue<Tile2i> bucket))
                {
                    bucket = new Queue<Tile2i>();
                    queue.Add(distance, bucket);
                }
                bucket.Enqueue(center);
            }
        }

        public bool TryGetDistance(Tile2i center, out float distance)
            => m_distances.TryGetValue(center, out distance);

        public AccessV2EndpointSet ApplyTerminalCosts(
            AccessV2EndpointSet endpoints)
        {
            var charged = new List<AccessV2FixedFrontage>();
            for (int index = 0; index < endpoints.FixedGoals.Count; index++)
            {
                AccessV2FixedFrontage goal = endpoints.FixedGoals[index];
                Tile2i interiorCenter =
                    AccessV2PotentialField.GetCanonicalCenter(goal.State);
                if (!TryGetDistance(interiorCenter, out float providerDistance))
                {
                    endpoints.Diagnostics.Reject("FixedProviderNoTowerRoute");
                    continue;
                }
                // The candidate matches one complete 4-tile slice outside the
                // provider. Charge that final entry as well as the provider's
                // downstream distance to tower ground.
                charged.Add(new AccessV2FixedFrontage(
                    goal.State, goal.ExposedDirection,
                    terminalCost: 4f + providerDistance));
            }
            endpoints.Diagnostics.FixedFrontageCount = charged.Count;
            return new AccessV2EndpointSet(
                endpoints.StartTiers, charged, endpoints.Diagnostics);
        }
    }
}

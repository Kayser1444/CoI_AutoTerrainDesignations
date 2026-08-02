using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    /// <summary>
    /// Selects global P or the provisional commitment belonging to the static
    /// G component from which the current uninterrupted V segment launched.
    /// Uncertain geometry deliberately weakens to global ownership so this
    /// optimization never excludes a modeled route.
    /// </summary>
    internal readonly struct AccessV2PotentialOwner
        : IEquatable<AccessV2PotentialOwner>
    {
        private readonly int m_sourceGroundComponent;

        public bool IsGlobal { get; }

        private AccessV2PotentialOwner(
            bool isGlobal,
            int sourceGroundComponent)
        {
            IsGlobal = isGlobal;
            m_sourceGroundComponent = sourceGroundComponent;
        }

        public static AccessV2PotentialOwner Global
            => new AccessV2PotentialOwner(true, -1);

        public static AccessV2PotentialOwner FromGround(
            AccessV2GroundGraph ground,
            Tile2i center)
            => ground.TryGetComponentId(center, out int component)
                ? new AccessV2PotentialOwner(false, component)
                : Global;

        public AccessV2PotentialOwner Advance(
            AccessV2GroundGraph ground,
            Tile2i from,
            Tile2i to)
        {
            if (IsGlobal || from == to)
                return this;
            if (!ground.IsInComponent(from, m_sourceGroundComponent))
                return Global;

            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            int stepX = Math.Sign(dx);
            int stepY = Math.Sign(dy);
            int length = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (length == 0)
                return this;
            if (dx != 0 && dy != 0 && Math.Abs(dx) != Math.Abs(dy))
                return Global;

            Tile2i current = from;
            for (int index = 0; index < length; index++)
            {
                Tile2i next = new Tile2i(
                    current.X + stepX,
                    current.Y + stepY);
                // Cleanup may make the apparently equivalent G route more
                // expensive. Do not claim dominance in that ambiguous case.
                if (ground.IsCleanupGround(current)
                    || ground.IsCleanupGround(next)
                    || !ground.IsInComponent(next, m_sourceGroundComponent)
                    || !ground.CanTraverse(current, next))
                    return Global;
                current = next;
            }
            return this;
        }

        public bool CanReturnTo(
            AccessV2GroundGraph ground,
            Tile2i center)
            => IsGlobal
                || !ground.IsInComponent(center, m_sourceGroundComponent);

        public bool Equals(AccessV2PotentialOwner other)
            => IsGlobal == other.IsGlobal
                && (IsGlobal
                    || m_sourceGroundComponent
                        == other.m_sourceGroundComponent);

        public override bool Equals(object? obj)
            => obj is AccessV2PotentialOwner other && Equals(other);

        public override int GetHashCode()
            => IsGlobal ? -1 : m_sourceGroundComponent;
    }

    internal readonly struct AccessV2PotentialSample
    {
        public Tile2i Center { get; }
        public float Cost { get; }
        public bool IsGenerated { get; }
        public AccessV2TravelAxis Axis { get; }

        public AccessV2PotentialSample(
            Tile2i center,
            float cost,
            bool isGenerated,
            AccessV2TravelAxis axis)
        {
            Center = center;
            Cost = cost;
            IsGenerated = isGenerated;
            Axis = axis;
        }
    }

    internal static class AccessV2CostModel
    {
        public static float GetMinimumVTravelCostPerTile(float fixedOriginCost)
            => 1f + Math.Max(0f, fixedOriginCost) / 4f;

        public static float GetCenterSpokeCost(float fixedOriginCost)
            => 2f * GetMinimumVTravelCostPerTile(fixedOriginCost);
    }

    /// <summary>
    /// Request-scoped sparse route lower bound over actual generated V origins
    /// and reusable fixed-navigation (FV) nodes. A generated-origin lookup
    /// assumes that origin's fixed overhead is already paid. Reverse edges
    /// charge the overhead only when the corresponding forward move enters a
    /// generated origin.
    /// </summary>
    internal sealed class AccessV2PotentialField
    {
        private const float Epsilon = 0.0001f;
        private static readonly RelTile2i[] s_originDirections =
        {
            new RelTile2i(4, 0), new RelTile2i(-4, 0),
            new RelTile2i(0, 4), new RelTile2i(0, -4),
        };
        private static readonly RelTile2i[] s_groundCenterOffsets =
        {
            new RelTile2i(2, 4), new RelTile2i(2, 0),
            new RelTile2i(4, 2), new RelTile2i(0, 2),
        };

        private readonly HashSet<Tile2i> m_generatedOrigins;
        private readonly HashSet<Tile2i> m_vPrimeOrigins;
        private readonly AccessV2GroundGraph? m_ground;
        private readonly AccessV2FixedNavigationGraph? m_fixedNavigation;
        private readonly HashSet<NodeKey> m_fixedNodes =
            new HashSet<NodeKey>();
        private readonly Dictionary<NodeKey, float> m_costs =
            new Dictionary<NodeKey, float>();
        private readonly Dictionary<Tile2i, IReadOnlyList<NodeKey>>
            m_fixedContactsByGenerated =
                new Dictionary<Tile2i, IReadOnlyList<NodeKey>>();
        private readonly Dictionary<NodeKey, IReadOnlyList<Tile2i>>
            m_generatedContactsByFixed =
                new Dictionary<NodeKey, IReadOnlyList<Tile2i>>();
        private readonly float m_generatedFixedCost;
        private readonly float m_centerSpokeCost;

        public int GeneratedNodeCount => m_generatedOrigins.Count;
        public int FixedNodeCount => m_fixedNodes.Count;
        public int NodeCount => GeneratedNodeCount + FixedNodeCount;

        public AccessV2PotentialField(
            AccessSearchSnapshot snapshot,
            Tile2i boundsMin,
            Tile2i boundsMax,
            float generatedFixedCost,
            float centerSpokeCost)
            : this(
                boundsMin,
                boundsMax,
                snapshot.V2PotentialGeneratedOrigins,
                snapshot.V2PotentialVPrimeOrigins,
                snapshot.V2GroundGraph,
                snapshot.V2FixedNavigationGraph,
                generatedFixedCost,
                centerSpokeCost)
        {
        }

        internal AccessV2PotentialField(
            Tile2i boundsMin,
            Tile2i boundsMax,
            IEnumerable<Tile2i> generatedOrigins,
            IEnumerable<Tile2i> vPrimeOrigins,
            AccessV2GroundGraph? ground,
            AccessV2FixedNavigationGraph? fixedNavigation,
            float generatedFixedCost,
            float centerSpokeCost)
        {
            m_generatedFixedCost = Math.Max(0f, generatedFixedCost);
            m_centerSpokeCost = Math.Max(0f, centerSpokeCost);
            m_ground = ground;
            m_fixedNavigation = fixedNavigation;
            m_generatedOrigins = new HashSet<Tile2i>(
                generatedOrigins.Where(origin =>
                    IsInside(origin, boundsMin, boundsMax)));
            m_vPrimeOrigins = new HashSet<Tile2i>(
                vPrimeOrigins.Where(m_generatedOrigins.Contains));
            if (m_fixedNavigation != null)
                foreach (AccessV2FixedNavigationNode node
                    in m_fixedNavigation.Nodes)
                    if (IsInside(node.Anchor, boundsMin, boundsMax))
                        m_fixedNodes.Add(
                            NodeKey.Fixed(node.Axis, node.Center));
            BuildFixedContacts();
            BuildPotentials();
        }

        public float GetPotential(AccessV2BandState state)
        {
            float best = GetStored(NodeKey.Generated(
                state.GetLaneOrigin(0)));
            best = Math.Min(best, GetStored(NodeKey.Generated(
                state.GetLaneOrigin(1))));
            best = Math.Min(best, GetStored(NodeKey.Fixed(
                state.Axis, GetCanonicalCenter(state))));
            return ToPublicPotential(best);
        }

        /// <summary>
        /// Returns the paid-current-origin sparse P value.
        /// </summary>
        public float GetPotential(Tile2i origin)
            => ToPublicPotential(GetStored(NodeKey.Generated(origin)));

        /// <summary>
        /// Returns an immutable diagnostic projection of the reached sparse
        /// nodes. Generated origins are projected to their 4x4 center; FV
        /// nodes retain their axis because both values can occupy one center.
        /// </summary>
        internal IReadOnlyList<AccessV2PotentialSample>
            GetDiagnosticSamples()
            => m_costs
                .Select(pair => new AccessV2PotentialSample(
                    pair.Key.Kind == NodeKind.Generated
                        ? pair.Key.Position + new RelTile2i(2, 2)
                        : pair.Key.Position,
                    pair.Value,
                    pair.Key.Kind == NodeKind.Generated,
                    pair.Key.Axis))
                .OrderBy(sample => sample.Center.X)
                .ThenBy(sample => sample.Center.Y)
                .ThenBy(sample => sample.IsGenerated ? 0 : 1)
                .ThenBy(sample => sample.Axis)
                .ToArray();

        /// <summary>
        /// Returns the cheapest relaxed launch from one physical-G center.
        /// FV reuse pays no generated-origin overhead; a generated launch pays
        /// the first origin overhead and the shared minimum center spoke.
        /// </summary>
        public float GetGroundLaunchPotential(
            Tile2i center,
            bool allowGenerated = true)
        {
            float best = float.PositiveInfinity;
            if (m_fixedNavigation != null)
            {
                IReadOnlyList<AccessV2TravelAxis> axes =
                    m_fixedNavigation.GetNodeAxes(center);
                for (int index = 0; index < axes.Count; index++)
                    best = Math.Min(best, GetStored(
                        NodeKey.Fixed(axes[index], center)));
            }
            if (allowGenerated)
            {
                for (int index = 0;
                    index < s_groundCenterOffsets.Length;
                    index++)
                {
                    Tile2i origin = new Tile2i(
                        center.X - s_groundCenterOffsets[index].X,
                        center.Y - s_groundCenterOffsets[index].Y);
                    float continuation = GetStored(
                        NodeKey.Generated(origin));
                    if (!float.IsPositiveInfinity(continuation))
                        best = Math.Min(best,
                            m_centerSpokeCost
                                + m_generatedFixedCost
                                + continuation);
                }
            }
            return ToPublicPotential(best);
        }

        internal static Tile2i GetCanonicalCenter(AccessV2BandState state)
            => state.Axis == AccessV2TravelAxis.X
                ? state.Anchor + new RelTile2i(2, 4)
                : state.Anchor + new RelTile2i(4, 2);

        private void BuildFixedContacts()
        {
            if (m_fixedNavigation == null || m_vPrimeOrigins.Count == 0)
                return;
            var fixedByGenerated =
                new Dictionary<Tile2i, HashSet<NodeKey>>();
            var generatedByFixed =
                new Dictionary<NodeKey, HashSet<Tile2i>>();
            foreach (AccessV2FixedNavigationNode node
                in m_fixedNavigation.Nodes)
            {
                NodeKey fixedKey = NodeKey.Fixed(node.Axis, node.Center);
                if (!m_fixedNodes.Contains(fixedKey))
                    continue;
                Tile2i laneDirection =
                    AccessV2BandProfile.GetLaneDirection(node.Axis);
                Tile2i[] fixedOrigins =
                {
                    node.Anchor,
                    AccessV2Geometry.Add(node.Anchor, laneDirection),
                };
                for (int lane = 0; lane < fixedOrigins.Length; lane++)
                {
                    for (int direction = 0;
                        direction < s_originDirections.Length;
                        direction++)
                    {
                        Tile2i candidate = fixedOrigins[lane]
                            + s_originDirections[direction];
                        if (!m_generatedOrigins.Contains(candidate)
                            || !m_vPrimeOrigins.Contains(candidate))
                            continue;
                        if (!fixedByGenerated.TryGetValue(
                                candidate, out HashSet<NodeKey> fixedContacts))
                        {
                            fixedContacts = new HashSet<NodeKey>();
                            fixedByGenerated.Add(candidate, fixedContacts);
                        }
                        fixedContacts.Add(fixedKey);
                        if (!generatedByFixed.TryGetValue(
                                fixedKey, out HashSet<Tile2i> generatedContacts))
                        {
                            generatedContacts = new HashSet<Tile2i>();
                            generatedByFixed.Add(fixedKey, generatedContacts);
                        }
                        generatedContacts.Add(candidate);
                    }
                }
            }
            foreach (KeyValuePair<Tile2i, HashSet<NodeKey>> pair
                in fixedByGenerated)
                m_fixedContactsByGenerated[pair.Key] = pair.Value.ToArray();
            foreach (KeyValuePair<NodeKey, HashSet<Tile2i>> pair
                in generatedByFixed)
                m_generatedContactsByFixed[pair.Key] = pair.Value.ToArray();
        }

        private void BuildPotentials()
        {
            var queue = new SortedDictionary<float, Queue<NodeKey>>();
            if (m_ground != null)
            {
                if (m_fixedNavigation != null)
                {
                    foreach (AccessV2FixedNavigationNode node
                        in m_fixedNavigation.Nodes)
                    {
                        if (m_ground.TryGetGoalDistance(
                                node.Center, out float suffix))
                        {
                            NodeKey key = NodeKey.Fixed(
                                node.Axis, node.Center);
                            if (m_fixedNodes.Contains(key))
                                Seed(key, suffix);
                        }
                    }
                }
                foreach (Tile2i origin in m_generatedOrigins)
                {
                    float suffix = GetBestGoalSuffix(origin);
                    if (!float.IsPositiveInfinity(suffix))
                        Seed(NodeKey.Generated(origin),
                            m_centerSpokeCost + suffix);
                }
            }

            while (queue.Count > 0)
            {
                KeyValuePair<float, Queue<NodeKey>> first = queue.First();
                NodeKey current = first.Value.Dequeue();
                if (first.Value.Count == 0) queue.Remove(first.Key);
                if (!m_costs.TryGetValue(current, out float known)
                    || Math.Abs(known - first.Key) > Epsilon)
                    continue;
                if (current.Kind == NodeKind.Generated)
                    RelaxGeneratedPredecessors(current, known);
                else
                    RelaxFixedPredecessors(current, known);
            }

            void Seed(NodeKey key, float cost)
            {
                if (cost < 0f
                    || (m_costs.TryGetValue(key, out float old)
                        && old <= cost + Epsilon))
                    return;
                m_costs[key] = cost;
                if (!queue.TryGetValue(cost, out Queue<NodeKey> bucket))
                {
                    bucket = new Queue<NodeKey>();
                    queue.Add(cost, bucket);
                }
                bucket.Enqueue(key);
            }

            void RelaxGeneratedPredecessors(NodeKey current, float known)
            {
                float enterGeneratedCost = 4f + m_generatedFixedCost;
                for (int index = 0;
                    index < s_originDirections.Length;
                    index++)
                {
                    Tile2i predecessor = current.Position
                        + s_originDirections[index];
                    if (m_generatedOrigins.Contains(predecessor))
                        Seed(NodeKey.Generated(predecessor),
                            known + enterGeneratedCost);
                }
                if (m_fixedContactsByGenerated.TryGetValue(
                        current.Position,
                        out IReadOnlyList<NodeKey> fixedContacts))
                {
                    for (int index = 0;
                        index < fixedContacts.Count;
                        index++)
                        Seed(fixedContacts[index],
                            known + enterGeneratedCost);
                }
            }

            void RelaxFixedPredecessors(NodeKey current, float known)
            {
                if (m_fixedNavigation != null)
                {
                    IReadOnlyList<AccessV2FixedNavigationMove> moves =
                        m_fixedNavigation.EnumerateMoves(
                            current.Axis, current.Position);
                    for (int index = 0; index < moves.Count; index++)
                    {
                        NodeKey predecessor = NodeKey.Fixed(
                            moves[index].Axis,
                            moves[index].Center);
                        if (m_fixedNodes.Contains(predecessor))
                            Seed(predecessor,
                                known + moves[index].Cost);
                    }
                }
                if (m_generatedContactsByFixed.TryGetValue(
                        current,
                        out IReadOnlyList<Tile2i> generatedContacts))
                {
                    for (int index = 0;
                        index < generatedContacts.Count;
                        index++)
                        Seed(NodeKey.Generated(generatedContacts[index]),
                            known + 4f);
                }
            }
        }

        private float GetBestGoalSuffix(Tile2i origin)
        {
            if (m_ground == null)
                return float.PositiveInfinity;
            float best = float.PositiveInfinity;
            for (int index = 0;
                index < s_groundCenterOffsets.Length;
                index++)
            {
                Tile2i center = origin + s_groundCenterOffsets[index];
                if (m_ground.TryGetGoalDistance(center, out float suffix))
                    best = Math.Min(best, suffix);
            }
            return best;
        }

        private float GetStored(NodeKey key)
            => m_costs.TryGetValue(key, out float cost)
                ? cost
                : float.PositiveInfinity;

        private static float ToPublicPotential(float cost)
            => float.IsPositiveInfinity(cost) ? 0f : Math.Max(0f, cost);

        private static bool IsInside(
            Tile2i origin,
            Tile2i min,
            Tile2i max)
            => origin.X >= min.X && origin.Y >= min.Y
                && origin.X <= max.X && origin.Y <= max.Y;

        private enum NodeKind : byte
        {
            Generated,
            Fixed,
        }

        private readonly struct NodeKey : IEquatable<NodeKey>
        {
            public NodeKind Kind { get; }
            public AccessV2TravelAxis Axis { get; }
            public Tile2i Position { get; }

            private NodeKey(
                NodeKind kind,
                AccessV2TravelAxis axis,
                Tile2i position)
            {
                Kind = kind;
                Axis = axis;
                Position = position;
            }

            public static NodeKey Generated(Tile2i origin)
                => new NodeKey(
                    NodeKind.Generated,
                    AccessV2TravelAxis.X,
                    origin);

            public static NodeKey Fixed(
                AccessV2TravelAxis axis,
                Tile2i center)
                => new NodeKey(NodeKind.Fixed, axis, center);

            public bool Equals(NodeKey other)
                => Kind == other.Kind
                    && (Kind == NodeKind.Generated || Axis == other.Axis)
                    && Position == other.Position;

            public override bool Equals(object? obj)
                => obj is NodeKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)Kind;
                    if (Kind == NodeKind.Fixed)
                        hash = (hash * 397) ^ (int)Axis;
                    return (hash * 397) ^ Position.GetHashCode();
                }
            }
        }
    }

    /// <summary>
    /// Lazy exact-G escape lower bounds. The first lookup in a disconnected
    /// component builds only that component and seeds its canonical V/FV
    /// launches from sparse P.
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

        private readonly AccessV2GroundGraph m_ground;
        private readonly AccessV2PotentialField m_vPotential;
        private readonly Func<Tile2i, bool>? m_canExitToGeneratedV;
        private readonly Dictionary<Tile2i, float> m_costs =
            new Dictionary<Tile2i, float>();

        public int BuiltComponentCount { get; private set; }

        public AccessV2GroundEscapePotentialField(
            AccessV2GroundGraph ground,
            AccessV2PotentialField vPotential,
            Func<Tile2i, bool>? canExitToGeneratedV = null)
        {
            m_ground = ground;
            m_vPotential = vPotential;
            m_canExitToGeneratedV = canExitToGeneratedV;
        }

        public float GetPotential(Tile2i tile)
        {
            if (m_costs.TryGetValue(tile, out float cost))
                return cost;
            if (!m_ground.IsTraversable(tile)
                || m_ground.IsGoalConnected(tile))
                return 0f;
            BuildComponent(tile);
            return m_costs.TryGetValue(tile, out cost) ? cost : 0f;
        }

        private void BuildComponent(Tile2i start)
        {
            var component = new HashSet<Tile2i> { start };
            var flood = new Queue<Tile2i>();
            flood.Enqueue(start);
            while (flood.Count > 0)
            {
                Tile2i current = flood.Dequeue();
                for (int index = 0; index < s_directions.Length; index++)
                {
                    Tile2i next = current + s_directions[index];
                    if (component.Contains(next)
                        || !m_ground.CanTraverse(current, next))
                        continue;
                    component.Add(next);
                    flood.Enqueue(next);
                }
            }

            var local = new Dictionary<Tile2i, float>();
            var queue = new SortedDictionary<float, Queue<Tile2i>>();
            foreach (Tile2i tile in component)
            {
                bool allowGenerated =
                    m_canExitToGeneratedV?.Invoke(tile) != false;
                float seed = m_vPotential.GetGroundLaunchPotential(
                    tile, allowGenerated);
                if (seed > 0f)
                    Relax(tile, seed);
            }
            while (queue.Count > 0)
            {
                KeyValuePair<float, Queue<Tile2i>> first = queue.First();
                Tile2i current = first.Value.Dequeue();
                if (first.Value.Count == 0) queue.Remove(first.Key);
                if (!local.TryGetValue(current, out float known)
                    || Math.Abs(known - first.Key) > 0.0001f)
                    continue;
                for (int index = 0; index < s_directions.Length; index++)
                {
                    Tile2i next = current + s_directions[index];
                    if (!component.Contains(next)
                        || !m_ground.CanTraverse(current, next))
                        continue;
                    Relax(next, known
                        + AccessV2GroundGraph.GetStepCost(current, next));
                }
            }
            foreach (Tile2i tile in component)
                m_costs[tile] = local.TryGetValue(tile, out float value)
                    ? value
                    : 0f;
            BuiltComponentCount++;

            void Relax(Tile2i tile, float cost)
            {
                if (local.TryGetValue(tile, out float old)
                    && old <= cost + 0.0001f)
                    return;
                local[tile] = cost;
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

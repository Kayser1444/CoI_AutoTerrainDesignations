using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal readonly struct AccessV2FixedNavigationMove
    {
        public AccessV2TravelAxis Axis { get; }
        public Tile2i Center { get; }
        public IReadOnlyList<Tile2i> ExactCenterPath { get; }
        public float Cost { get; }

        public AccessV2FixedNavigationMove(
            AccessV2TravelAxis axis,
            Tile2i center,
            IReadOnlyList<Tile2i> exactCenterPath,
            float cost)
        {
            Axis = axis;
            Center = center;
            ExactCenterPath = exactCenterPath;
            Cost = cost;
        }
    }

    internal readonly struct AccessV2FixedNavigationPortal
    {
        public Tile2i Center { get; }
        public IReadOnlyList<Tile2i> ExactCenterPath { get; }
        public float Cost { get; }

        public AccessV2FixedNavigationPortal(
            Tile2i center,
            IReadOnlyList<Tile2i> exactCenterPath,
            float cost)
        {
            Center = center;
            ExactCenterPath = exactCenterPath;
            Cost = cost;
        }
    }

    /// <summary>
    /// Directionless fixed-navigation node backed by two compatible cardinally
    /// adjacent fixed origins. Axis describes the pair's longitudinal lattice,
    /// not an entry heading or a generated-V propagation constraint.
    /// </summary>
    internal readonly struct AccessV2FixedNavigationNode
        : IEquatable<AccessV2FixedNavigationNode>
    {
        public AccessV2TravelAxis Axis { get; }
        public Tile2i Anchor { get; }
        public Tile2i Center { get; }

        public AccessV2FixedNavigationNode(
            AccessV2TravelAxis axis,
            Tile2i anchor,
            Tile2i center)
        {
            Axis = axis;
            Anchor = anchor;
            Center = center;
        }

        public bool Equals(AccessV2FixedNavigationNode other)
            => Axis == other.Axis && Center == other.Center;

        public override bool Equals(object? obj)
            => obj is AccessV2FixedNavigationNode other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Axis * 397) ^ Center.GetHashCode();
            }
        }

        public override string ToString() => $"FV:{Axis}@{Center}";
    }

    /// <summary>
    /// Sparse exact navigation over regular interiors of fixed terrain work.
    /// Nodes are canonical centers of compatible width-two fixed bands. Macro
    /// edges translate four tiles cardinally or diagonally and retain the exact
    /// vehicle-center path used to validate them. Diagonals inherit the ground
    /// graph's strict two-corridor clearance rule.
    ///
    /// Irregular fringes, physical-ground contacts, blockers, and cleanup
    /// changes require explicit portals and are deliberately outside this
    /// regular-interior graph.
    /// </summary>
    internal sealed class AccessV2FixedNavigationGraph
    {
        // One four-tile transition-origin band plus the projected Mega
        // footprint's center offset on the fixed side.
        private const int MaxPortalCenterRayLength = 8;

        private static readonly RelTile2i[] s_macroDirections =
        {
            new RelTile2i(4, 0), new RelTile2i(-4, 0),
            new RelTile2i(0, 4), new RelTile2i(0, -4),
            new RelTile2i(4, 4), new RelTile2i(4, -4),
            new RelTile2i(-4, 4), new RelTile2i(-4, -4),
        };

        private readonly Dictionary<NodeKey, AccessV2FixedNavigationNode>
            m_nodes;
        private readonly Dictionary<NodeKey, IReadOnlyList<Edge>> m_edges;
        private readonly AccessV2GroundGraph m_exactGround;

        public int NodeCount => m_nodes.Count;
        internal IEnumerable<AccessV2FixedNavigationNode> Nodes
            => m_nodes.Values;

        public AccessV2FixedNavigationGraph(
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            AccessV2GroundGraph exactGround)
        {
            m_exactGround = exactGround;
            m_nodes =
                new Dictionary<NodeKey, AccessV2FixedNavigationNode>();
            BuildNodes(fixedProfiles, exactGround);
            m_edges = BuildEdges(exactGround);
        }

        public bool ContainsNode(
            AccessV2TravelAxis axis,
            Tile2i center)
            => m_nodes.ContainsKey(new NodeKey(axis, center));

        public IReadOnlyList<AccessV2TravelAxis> GetNodeAxes(
            Tile2i center)
        {
            var result = new List<AccessV2TravelAxis>(2);
            if (ContainsNode(AccessV2TravelAxis.X, center))
                result.Add(AccessV2TravelAxis.X);
            if (ContainsNode(AccessV2TravelAxis.Y, center))
                result.Add(AccessV2TravelAxis.Y);
            return result;
        }

        public IReadOnlyList<AccessV2FixedNavigationMove> EnumerateMoves(
            AccessV2TravelAxis axis,
            Tile2i center)
        {
            if (!m_edges.TryGetValue(
                    new NodeKey(axis, center),
                    out IReadOnlyList<Edge> edges))
                return Array.Empty<AccessV2FixedNavigationMove>();
            var result =
                new AccessV2FixedNavigationMove[edges.Count];
            for (int index = 0; index < edges.Count; index++)
            {
                Edge edge = edges[index];
                result[index] = new AccessV2FixedNavigationMove(
                    edge.To.Axis,
                    edge.To.Center,
                    edge.ExactCenterPath,
                    edge.Cost);
            }
            return result;
        }

        public IReadOnlyList<AccessV2FixedNavigationPortal>
            EnumerateExitPortals(
                AccessV2TravelAxis axis,
                Tile2i center)
        {
            if (!ContainsNode(axis, center))
                return Array.Empty<AccessV2FixedNavigationPortal>();
            var result =
                new List<AccessV2FixedNavigationPortal>();
            for (int directionIndex = 0;
                directionIndex < s_macroDirections.Length;
                directionIndex++)
            {
                RelTile2i macro = s_macroDirections[directionIndex];
                var unit = new RelTile2i(
                    Math.Sign(macro.X), Math.Sign(macro.Y));
                var path = new List<Tile2i>(
                    MaxPortalCenterRayLength + 1) { center };
                Tile2i current = center;
                float cost = 0f;
                for (int step = 0;
                    step < MaxPortalCenterRayLength;
                    step++)
                {
                    Tile2i next = current + unit;
                    if (!m_exactGround.CanTraverse(current, next))
                        break;
                    cost += AccessV2GroundGraph.GetStepCost(
                        current, next);
                    path.Add(next);
                    if (!m_exactGround.IsProjectedFixedGround(next))
                    {
                        result.Add(
                            new AccessV2FixedNavigationPortal(
                                next, path.ToArray(), cost));
                        break;
                    }
                    current = next;
                }
            }
            return result;
        }

        public bool CanTraverse(
            AccessV2TravelAxis axis,
            Tile2i from,
            Tile2i to)
            => CanTraverse(axis, from, axis, to);

        public bool CanTraverse(
            AccessV2TravelAxis fromAxis,
            Tile2i from,
            AccessV2TravelAxis toAxis,
            Tile2i to)
        {
            if (!m_edges.TryGetValue(
                    new NodeKey(fromAxis, from),
                    out IReadOnlyList<Edge> edges))
                return false;
            for (int index = 0; index < edges.Count; index++)
                if (edges[index].To.Axis == toAxis
                    && edges[index].To.Center == to)
                    return true;
            return false;
        }

        public bool RequiresPortal(
            AccessV2TravelAxis axis,
            Tile2i from,
            Tile2i to)
            => RequiresPortal(axis, from, axis, to);

        public bool RequiresPortal(
            AccessV2TravelAxis fromAxis,
            Tile2i from,
            AccessV2TravelAxis toAxis,
            Tile2i to)
        {
            if (!m_nodes.ContainsKey(new NodeKey(fromAxis, from))
                || !m_nodes.ContainsKey(new NodeKey(toAxis, to))
                || CanTraverse(fromAxis, from, toAxis, to))
                return false;
            return TryBuildExactPortalPath(
                m_exactGround, from, to, out _, out _);
        }

        public bool TryGetShortestPath(
            AccessV2TravelAxis axis,
            Tile2i start,
            Tile2i goal,
            out IReadOnlyList<Tile2i> exactCenterPath,
            out float cost)
            => TryGetShortestPath(
                axis, start, axis, goal,
                out exactCenterPath, out cost);

        public bool TryGetShortestPath(
            AccessV2TravelAxis startAxis,
            Tile2i start,
            AccessV2TravelAxis goalAxis,
            Tile2i goal,
            out IReadOnlyList<Tile2i> exactCenterPath,
            out float cost)
        {
            var startKey = new NodeKey(startAxis, start);
            var goalKey = new NodeKey(goalAxis, goal);
            if (!m_nodes.ContainsKey(startKey)
                || !m_nodes.ContainsKey(goalKey))
            {
                exactCenterPath = Array.Empty<Tile2i>();
                cost = 0f;
                return false;
            }

            var distances = new Dictionary<NodeKey, float>
            {
                [startKey] = 0f,
            };
            var parents = new Dictionary<NodeKey, Edge>();
            var queue = new SortedDictionary<float, Queue<NodeKey>>();
            queue.Add(0f, new Queue<NodeKey>(new[] { startKey }));
            while (queue.Count > 0)
            {
                KeyValuePair<float, Queue<NodeKey>> first = queue.First();
                NodeKey current = first.Value.Dequeue();
                if (first.Value.Count == 0) queue.Remove(first.Key);
                if (!distances.TryGetValue(current, out float known)
                    || Math.Abs(known - first.Key) > 0.0001f)
                    continue;
                if (current.Equals(goalKey))
                {
                    cost = known;
                    exactCenterPath = Reconstruct(
                        startKey, goalKey, parents);
                    return true;
                }
                if (!m_edges.TryGetValue(
                        current, out IReadOnlyList<Edge> edges))
                    continue;
                for (int index = 0; index < edges.Count; index++)
                {
                    Edge edge = edges[index];
                    NodeKey next = new NodeKey(
                        edge.To.Axis, edge.To.Center);
                    float nextCost = known + edge.Cost;
                    if (distances.TryGetValue(next, out float old)
                        && old <= nextCost + 0.0001f)
                        continue;
                    distances[next] = nextCost;
                    parents[next] = edge;
                    if (!queue.TryGetValue(
                            nextCost, out Queue<NodeKey> bucket))
                    {
                        bucket = new Queue<NodeKey>();
                        queue.Add(nextCost, bucket);
                    }
                    bucket.Enqueue(next);
                }
            }

            exactCenterPath = Array.Empty<Tile2i>();
            cost = 0f;
            return false;
        }

        private void BuildNodes(
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            AccessV2GroundGraph exactGround)
        {
            foreach (KeyValuePair<Tile2i, AccessHeightProfile> pair
                in fixedProfiles)
            {
                Add(AccessV2TravelAxis.X, pair.Key, pair.Value);
                Add(AccessV2TravelAxis.Y, pair.Key, pair.Value);
            }

            void Add(
                AccessV2TravelAxis axis,
                Tile2i anchor,
                AccessHeightProfile lane0)
            {
                Tile2i laneDirection =
                    AccessV2BandProfile.GetLaneDirection(axis);
                Tile2i companion =
                    AccessV2Geometry.Add(anchor, laneDirection);
                if (!fixedProfiles.TryGetValue(
                        companion, out AccessHeightProfile lane1)
                    || !AccessPathSearch.EdgesMatch(
                        lane0, lane1, laneDirection))
                    return;
                Tile2i center = axis == AccessV2TravelAxis.X
                    ? anchor + new RelTile2i(2, 4)
                    : anchor + new RelTile2i(4, 2);
                if (!exactGround.IsProjectedFixedGround(center)
                    || !exactGround.IsTraversable(center))
                    return;
                var node = new AccessV2FixedNavigationNode(
                    axis, anchor, center);
                m_nodes[new NodeKey(axis, center)] = node;
            }
        }

        private Dictionary<NodeKey, IReadOnlyList<Edge>> BuildEdges(
            AccessV2GroundGraph exactGround)
        {
            var result =
                new Dictionary<NodeKey, IReadOnlyList<Edge>>();
            foreach (KeyValuePair<NodeKey, AccessV2FixedNavigationNode> pair
                in m_nodes)
            {
                var edges = new List<Edge>();
                for (int index = 0;
                    index < s_macroDirections.Length;
                    index++)
                {
                    Tile2i targetCenter =
                        pair.Value.Center + s_macroDirections[index];
                    var targetKey =
                        new NodeKey(pair.Value.Axis, targetCenter);
                    if (!m_nodes.TryGetValue(
                            targetKey,
                            out AccessV2FixedNavigationNode target)
                        || !TryBuildExactMacroPath(
                            exactGround,
                            pair.Value.Center,
                            targetCenter,
                            out IReadOnlyList<Tile2i> path,
                            out float cost))
                        continue;
                    edges.Add(new Edge(pair.Value, target, path, cost));
                }
                AccessV2TravelAxis otherAxis =
                    AccessV2Geometry.OtherAxis(pair.Value.Axis);
                for (int xSign = -1; xSign <= 1; xSign += 2)
                    for (int ySign = -1; ySign <= 1; ySign += 2)
                    {
                        Tile2i targetCenter = pair.Value.Center
                            + new RelTile2i(2 * xSign, 2 * ySign);
                        var targetKey =
                            new NodeKey(otherAxis, targetCenter);
                        if (!m_nodes.TryGetValue(
                                targetKey,
                                out AccessV2FixedNavigationNode target)
                            || !TryBuildExactMacroPath(
                                exactGround,
                                pair.Value.Center,
                                targetCenter,
                                out IReadOnlyList<Tile2i> path,
                                out float cost))
                            continue;
                        edges.Add(new Edge(
                            pair.Value, target, path, cost));
                    }
                result.Add(pair.Key, edges);
            }
            return result;
        }

        private static bool TryBuildExactMacroPath(
            AccessV2GroundGraph exactGround,
            Tile2i from,
            Tile2i to,
            out IReadOnlyList<Tile2i> path,
            out float cost)
            => TryBuildExactPath(
                exactGround, from, to, requireProjected: true,
                out path, out cost);

        private static bool TryBuildExactPortalPath(
            AccessV2GroundGraph exactGround,
            Tile2i from,
            Tile2i to,
            out IReadOnlyList<Tile2i> path,
            out float cost)
            => TryBuildExactPath(
                exactGround, from, to, requireProjected: false,
                out path, out cost);

        private static bool TryBuildExactPath(
            AccessV2GroundGraph exactGround,
            Tile2i from,
            Tile2i to,
            bool requireProjected,
            out IReadOnlyList<Tile2i> path,
            out float cost)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            bool regularMacro = steps == 4
                && (dx == 0 || Math.Abs(dx) == 4)
                && (dy == 0 || Math.Abs(dy) == 4);
            bool orientationConnector = steps == 2
                && Math.Abs(dx) == 2
                && Math.Abs(dy) == 2;
            if (!regularMacro && !orientationConnector)
            {
                path = Array.Empty<Tile2i>();
                cost = 0f;
                return false;
            }

            var centers = new List<Tile2i>(steps + 1) { from };
            Tile2i current = from;
            cost = 0f;
            var unit = new RelTile2i(Math.Sign(dx), Math.Sign(dy));
            for (int step = 0; step < steps; step++)
            {
                Tile2i next = current + unit;
                IReadOnlyList<Tile2i> swept =
                    AccessV2GroundGraph.GetSweptCenters(current, next);
                if (!exactGround.CanTraverse(current, next)
                    || (requireProjected && swept.Any(center =>
                        !exactGround.IsProjectedFixedGround(center))))
                {
                    path = Array.Empty<Tile2i>();
                    cost = 0f;
                    return false;
                }
                cost += AccessV2GroundGraph.GetStepCost(current, next);
                centers.Add(next);
                current = next;
            }
            path = centers;
            return true;
        }

        private static IReadOnlyList<Tile2i> Reconstruct(
            NodeKey start,
            NodeKey goal,
            IReadOnlyDictionary<NodeKey, Edge> parents)
        {
            var reverse = new List<Edge>();
            NodeKey cursor = goal;
            while (!cursor.Equals(start))
            {
                Edge edge = parents[cursor];
                reverse.Add(edge);
                cursor = new NodeKey(edge.From.Axis, edge.From.Center);
            }
            reverse.Reverse();
            var result = new List<Tile2i> { start.Center };
            for (int edgeIndex = 0; edgeIndex < reverse.Count; edgeIndex++)
                for (int centerIndex = 1;
                    centerIndex < reverse[edgeIndex].ExactCenterPath.Count;
                    centerIndex++)
                    result.Add(
                        reverse[edgeIndex].ExactCenterPath[centerIndex]);
            return result;
        }

        private readonly struct NodeKey : IEquatable<NodeKey>
        {
            public AccessV2TravelAxis Axis { get; }
            public Tile2i Center { get; }

            public NodeKey(AccessV2TravelAxis axis, Tile2i center)
            {
                Axis = axis;
                Center = center;
            }

            public bool Equals(NodeKey other)
                => Axis == other.Axis && Center == other.Center;

            public override bool Equals(object? obj)
                => obj is NodeKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)Axis * 397) ^ Center.GetHashCode();
                }
            }
        }

        private sealed class Edge
        {
            public AccessV2FixedNavigationNode From { get; }
            public AccessV2FixedNavigationNode To { get; }
            public IReadOnlyList<Tile2i> ExactCenterPath { get; }
            public float Cost { get; }

            public Edge(
                AccessV2FixedNavigationNode from,
                AccessV2FixedNavigationNode to,
                IReadOnlyList<Tile2i> exactCenterPath,
                float cost)
            {
                From = from;
                To = to;
                ExactCenterPath = exactCenterPath;
                Cost = cost;
            }
        }
    }
}

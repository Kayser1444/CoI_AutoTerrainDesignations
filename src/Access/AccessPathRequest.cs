using System;
using System.Collections.Generic;
using Mafi;
using AutoTerrainDesignations.Access.V2;

namespace AutoTerrainDesignations.Access
{
    internal enum AccessPathEndpointKind
    {
        FixedProfiles,
        GroundTiles,
        CombinedGoals
    }

    internal enum AccessPathIntent
    {
        InspectExistingRoute,
        ConstructAccessway
    }

    internal sealed class AccessPathEndpoint
    {
        public AccessPathEndpointKind Kind { get; }
        public IReadOnlyList<Tile2i> Nodes { get; }
        public IReadOnlyList<Tile2i> FixedProfileNodes { get; }
        public IReadOnlyList<Tile2i> GroundTileNodes { get; }

        public AccessPathEndpoint(AccessPathEndpointKind kind, IEnumerable<Tile2i> nodes)
        {
            Kind = kind;
            Nodes = new List<Tile2i>(nodes);
            FixedProfileNodes = kind == AccessPathEndpointKind.FixedProfiles
                ? Nodes
                : Array.Empty<Tile2i>();
            GroundTileNodes = kind == AccessPathEndpointKind.GroundTiles
                ? Nodes
                : Array.Empty<Tile2i>();
        }

        public AccessPathEndpoint(
            IEnumerable<Tile2i> fixedProfileNodes,
            IEnumerable<Tile2i> groundTileNodes)
        {
            Kind = AccessPathEndpointKind.CombinedGoals;
            FixedProfileNodes = new List<Tile2i>(fixedProfileNodes);
            GroundTileNodes = new List<Tile2i>(groundTileNodes);
            var combined = new List<Tile2i>(
                FixedProfileNodes.Count + GroundTileNodes.Count);
            combined.AddRange(FixedProfileNodes);
            combined.AddRange(GroundTileNodes);
            Nodes = combined;
        }
    }

    internal sealed class AccessPathRequest
    {
        public string RequestId { get; }
        public AccessSearchSnapshot Snapshot { get; }
        public AccessPathEndpoint Start { get; }
        public AccessPathEndpoint Goal { get; }
        public Tile2i BoundsMin { get; }
        public Tile2i BoundsMax { get; }
        public int RequiredWidth { get; }
        public AccessPathIntent Intent { get; }
        public float MaxCostLimit { get; }
        public AccessV2EndpointSet? V2Endpoints { get; }

        public AccessPathRequest(
            string requestId,
            AccessSearchSnapshot snapshot,
            AccessPathEndpoint start,
            AccessPathEndpoint goal,
            int requiredWidth,
            AccessPathIntent intent,
            float maxCostLimit = float.MaxValue,
            AccessV2EndpointSet? v2Endpoints = null)
        {
            RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Start = start ?? throw new ArgumentNullException(nameof(start));
            Goal = goal ?? throw new ArgumentNullException(nameof(goal));
            BoundsMin = snapshot.BoundsMin;
            BoundsMax = snapshot.BoundsMax;
            RequiredWidth = requiredWidth;
            Intent = intent;
            MaxCostLimit = maxCostLimit;
            V2Endpoints = v2Endpoints;
        }
    }
}

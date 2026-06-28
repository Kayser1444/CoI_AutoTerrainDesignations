using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    internal enum AccessPathEndpointKind
    {
        FixedProfiles,
        GroundTiles
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

        public AccessPathEndpoint(AccessPathEndpointKind kind, IEnumerable<Tile2i> nodes)
        {
            Kind = kind;
            Nodes = new List<Tile2i>(nodes);
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

        public AccessPathRequest(
            string requestId,
            AccessSearchSnapshot snapshot,
            AccessPathEndpoint start,
            AccessPathEndpoint goal,
            int requiredWidth,
            AccessPathIntent intent,
            float maxCostLimit = float.MaxValue)
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
        }
    }
}

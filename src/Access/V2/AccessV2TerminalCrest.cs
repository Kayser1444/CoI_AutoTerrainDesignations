using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal enum AccessV2TerminalCrestState : byte
    {
        Uncrested,
        Partial,
        Full,
    }

    internal readonly struct AccessV2TerminalCrestEvidence
    {
        public AccessHandoffOperation Operation { get; }
        public AccessV2TerminalCrestState Lane0 { get; }
        public AccessV2TerminalCrestState Lane1 { get; }
        public string Reason { get; }
        public int TerrainReads { get; }
        public bool IsApplicable
            => Operation == AccessHandoffOperation.Mining
                || Operation == AccessHandoffOperation.Dumping;

        public AccessV2TerminalCrestEvidence(
            AccessHandoffOperation operation,
            AccessV2TerminalCrestState lane0,
            AccessV2TerminalCrestState lane1,
            string reason,
            int terrainReads)
        {
            Operation = operation;
            Lane0 = lane0;
            Lane1 = lane1;
            Reason = reason ?? string.Empty;
            TerrainReads = terrainReads;
        }
    }

    internal delegate AccessV2TerminalCrestEvidence
        AccessV2TerminalCrestEvaluator(
            AccessV2BandState next,
            byte activeLanes,
            AccessHandoffOperation expectedOperation);

    /// <summary>
    /// Allocation-free terminal trigger over captured terrain facts. It owns
    /// operation and crest semantics; handoff materialization consumes its
    /// result only after a frontage becomes eligible.
    /// </summary>
    internal static class AccessV2TerminalCrestClassifier
    {
        public static AccessV2TerminalCrestEvidence Classify(
            AccessV2BandState next,
            byte activeLanes,
            AccessHandoffOperation expectedOperation,
            IReadOnlyDictionary<Tile2i, float> terrainHeights)
        {
            activeLanes &= 3;
            if (activeLanes == 0)
                return Evidence(
                    AccessHandoffOperation.None,
                    AccessV2TerminalCrestState.Uncrested,
                    AccessV2TerminalCrestState.Uncrested,
                    "NoActiveTerminalLane", 0);
            if (!ProfilesAreInteger(next, activeLanes))
                return Evidence(
                    AccessHandoffOperation.None,
                    AccessV2TerminalCrestState.Uncrested,
                    AccessV2TerminalCrestState.Uncrested,
                    "HalfHeightTerminalProfile", 0);

            int reads = 0;
            AccessHandoffOperation operation = expectedOperation;
            if (operation == AccessHandoffOperation.None)
            {
                if (!TryReadBandEdge(
                        next, activeLanes, leading: false,
                        terrainHeights, ref reads,
                        out EdgeSigns incoming0,
                        out EdgeSigns incoming1))
                    return Evidence(
                        AccessHandoffOperation.None,
                        AccessV2TerminalCrestState.Uncrested,
                        AccessV2TerminalCrestState.Uncrested,
                        "MissingTerminalTerrain", reads);
                if ((activeLanes & 1) != 0 && incoming0.IsMixed
                    || (activeLanes & 2) != 0 && incoming1.IsMixed)
                    return Evidence(
                        AccessHandoffOperation.None,
                        AccessV2TerminalCrestState.Uncrested,
                        AccessV2TerminalCrestState.Uncrested,
                        "AmbiguousLeadingEdgeOperation", reads);
                AccessHandoffOperation lane0 = (activeLanes & 1) != 0
                    ? DeriveOperation(incoming0)
                    : AccessHandoffOperation.None;
                AccessHandoffOperation lane1 = (activeLanes & 2) != 0
                    ? DeriveOperation(incoming1)
                    : AccessHandoffOperation.None;
                if (lane0 != AccessHandoffOperation.None
                    && lane1 != AccessHandoffOperation.None
                    && lane0 != lane1)
                    return Evidence(
                        AccessHandoffOperation.None,
                        AccessV2TerminalCrestState.Uncrested,
                        AccessV2TerminalCrestState.Uncrested,
                        "MixedLeadingEdgeOperations", reads);
                operation = lane0 != AccessHandoffOperation.None
                    ? lane0 : lane1;
                if (operation == AccessHandoffOperation.None)
                    return Evidence(
                        operation,
                        AccessV2TerminalCrestState.Uncrested,
                        AccessV2TerminalCrestState.Uncrested,
                        "NoLeadingEdgeOperation", reads);
            }
            else if (operation != AccessHandoffOperation.Mining
                && operation != AccessHandoffOperation.Dumping)
                return Evidence(
                    AccessHandoffOperation.None,
                    AccessV2TerminalCrestState.Uncrested,
                    AccessV2TerminalCrestState.Uncrested,
                    "UnsupportedTerminalOperation", reads);

            if (!TryReadBandEdge(
                    next, activeLanes, leading: true,
                    terrainHeights, ref reads,
                    out EdgeSigns leading0,
                    out EdgeSigns leading1))
                return Evidence(
                    AccessHandoffOperation.None,
                    AccessV2TerminalCrestState.Uncrested,
                    AccessV2TerminalCrestState.Uncrested,
                    "MissingTerminalTerrain", reads);
            AccessV2TerminalCrestState crest0 = (activeLanes & 1) != 0
                ? ClassifyCrest(leading0, operation)
                : AccessV2TerminalCrestState.Uncrested;
            AccessV2TerminalCrestState crest1 = (activeLanes & 2) != 0
                ? ClassifyCrest(leading1, operation)
                : AccessV2TerminalCrestState.Uncrested;
            if (expectedOperation == AccessHandoffOperation.None
                && crest0 == AccessV2TerminalCrestState.Uncrested
                && crest1 == AccessV2TerminalCrestState.Uncrested)
                return Evidence(
                    AccessHandoffOperation.None, crest0, crest1,
                    "NoLeadingEdgeCrest", reads);
            return Evidence(
                operation, crest0, crest1,
                "TerminalOperation=" + operation, reads);
        }

        private static bool TryReadBandEdge(
            AccessV2BandState state,
            byte lanes,
            bool leading,
            IReadOnlyDictionary<Tile2i, float> terrain,
            ref int reads,
            out EdgeSigns lane0,
            out EdgeSigns lane1)
        {
            lane0 = default;
            lane1 = default;
            Tile2i endpoint0 = default;
            Tile2i endpoint1 = default;
            int endpointSign0 = 0;
            int endpointSign1 = 0;
            bool haveEndpoints = false;
            if ((lanes & 1) != 0
                && !TryReadLaneEdge(
                    state, 0, leading, terrain, ref reads,
                    false, default, 0, default, 0,
                    out lane0, out endpoint0, out endpointSign0,
                    out endpoint1, out endpointSign1))
                return false;
            haveEndpoints = (lanes & 1) != 0;
            if ((lanes & 2) != 0
                && !TryReadLaneEdge(
                    state, 1, leading, terrain, ref reads,
                    haveEndpoints,
                    endpoint0, endpointSign0,
                    endpoint1, endpointSign1,
                    out lane1, out _, out _, out _, out _))
                return false;
            return true;
        }

        private static bool TryReadLaneEdge(
            AccessV2BandState state,
            int lane,
            bool leading,
            IReadOnlyDictionary<Tile2i, float> terrain,
            ref int reads,
            bool reuseEndpoints,
            Tile2i reused0,
            int reusedSign0,
            Tile2i reused1,
            int reusedSign1,
            out EdgeSigns signs,
            out Tile2i endpoint0,
            out int endpointSign0,
            out Tile2i endpoint1,
            out int endpointSign1)
        {
            signs = default;
            endpoint0 = default;
            endpoint1 = default;
            endpointSign0 = 0;
            endpointSign1 = 0;
            Tile2i origin = state.GetLaneOrigin(lane);
            AccessHeightProfile profile = state.GetLane(lane).Profile;
            for (int offset = 0; offset <= 4; offset++)
            {
                GetLocalEdgeSample(
                    state.EntryDirection, leading, offset,
                    out int x, out int y);
                Tile2i tile = origin + new RelTile2i(x, y);
                int sign;
                if (reuseEndpoints && tile == reused0)
                    sign = reusedSign0;
                else if (reuseEndpoints && tile == reused1)
                    sign = reusedSign1;
                else
                {
                    if (!terrain.TryGetValue(tile, out float ground))
                        return false;
                    reads++;
                    float target = profile.GetHeight2NumeratorAt(x, y) / 32f;
                    float delta = target - ground;
                    sign = Math.Abs(delta) <= 0.0001f
                        ? 0 : Math.Sign(delta);
                }
                signs = signs.Add(sign);
                if (offset == 0)
                {
                    endpoint0 = tile;
                    endpointSign0 = sign;
                }
                else if (offset == 4)
                {
                    endpoint1 = tile;
                    endpointSign1 = sign;
                }
            }
            return true;
        }

        private static void GetLocalEdgeSample(
            Tile2i direction,
            bool leading,
            int offset,
            out int x,
            out int y)
        {
            if (direction.X != 0)
            {
                x = (direction.X > 0) == leading ? 4 : 0;
                y = offset;
            }
            else
            {
                x = offset;
                y = (direction.Y > 0) == leading ? 4 : 0;
            }
        }

        private static AccessHandoffOperation DeriveOperation(EdgeSigns signs)
        {
            if (signs.Positive == 0 && signs.Negative > 0)
                return AccessHandoffOperation.Mining;
            if (signs.Negative == 0 && signs.Positive > 0)
                return AccessHandoffOperation.Dumping;
            return AccessHandoffOperation.None;
        }

        private static AccessV2TerminalCrestState ClassifyCrest(
            EdgeSigns signs,
            AccessHandoffOperation operation)
        {
            int work = operation == AccessHandoffOperation.Mining
                ? signs.Negative : signs.Positive;
            int crested = signs.Total - work;
            if (crested == 0)
                return AccessV2TerminalCrestState.Uncrested;
            return work == 0
                ? AccessV2TerminalCrestState.Full
                : AccessV2TerminalCrestState.Partial;
        }

        private static bool ProfilesAreInteger(
            AccessV2BandState state,
            byte lanes)
        {
            for (int lane = 0; lane < 2; lane++)
            {
                if ((lanes & (1 << lane)) == 0)
                    continue;
                AccessHeightProfile profile = state.GetLane(lane).Profile;
                if (((profile.Nw2 | profile.Ne2
                    | profile.Se2 | profile.Sw2) & 1) != 0)
                    return false;
            }
            return true;
        }

        private static AccessV2TerminalCrestEvidence Evidence(
            AccessHandoffOperation operation,
            AccessV2TerminalCrestState lane0,
            AccessV2TerminalCrestState lane1,
            string reason,
            int reads)
            => new AccessV2TerminalCrestEvidence(
                operation, lane0, lane1, reason, reads);

        private readonly struct EdgeSigns
        {
            public int Negative { get; }
            public int Zero { get; }
            public int Positive { get; }
            public int Total => Negative + Zero + Positive;
            public bool IsMixed => Negative > 0 && Positive > 0;

            private EdgeSigns(int negative, int zero, int positive)
            {
                Negative = negative;
                Zero = zero;
                Positive = positive;
            }

            public EdgeSigns Add(int sign)
                => new EdgeSigns(
                    Negative + (sign < 0 ? 1 : 0),
                    Zero + (sign == 0 ? 1 : 0),
                    Positive + (sign > 0 ? 1 : 0));
        }
    }
}

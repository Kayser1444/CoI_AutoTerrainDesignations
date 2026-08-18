using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;

namespace AutoTerrainDesignations.Access
{
    [Flags]
    internal enum AccessPropCleanupClass
    {
        None = 0,
        Tree = 1,
        DenseDebris = 2
    }

    internal enum AccessPropBlockerKind
    {
        None,
        HardBlocker,
        Building,
        ActiveTerrainDesignation,
        Ocean,
        Durability,
        UnderlyingTerrain,
        SourceWorkOrigin,
        OutOfArea
    }

    internal enum AccessPropCleanupDiagnostic
    {
        ClearGround,
        TreeCleanup,
        DenseDebrisCleanup,
        HardBlocker,
        TerrainRemovalPolicy
    }

    internal readonly struct AccessPropSample
    {
        public Tile2i Tile { get; }
        public bool IsTree { get; }
        public bool IsDenseDebris { get; }
        public bool IsRemovable { get; }
        public string CleanupObjectKey { get; }
        public TerrainPropId? DenseDebrisPropId { get; }
        public IReadOnlyList<Tile2i> EligibleCleanupOrigins { get; }
        public bool HasDumpBurialProbe { get; }
        public Tile2i DumpBurialProbeTile { get; }
        public float DumpBurialProbeOffsetX { get; }
        public float DumpBurialProbeOffsetY { get; }
        public float PlacedHeight { get; }
        public float DumpBurialThreshold { get; }

        public AccessPropSample(Tile2i tile, bool isTree, bool isDenseDebris, bool isRemovable,
            string? cleanupObjectKey = null,
            IReadOnlyList<Tile2i>? eligibleCleanupOrigins = null,
            Tile2i? dumpBurialProbeTile = null,
            float dumpBurialProbeOffsetX = 0f,
            float dumpBurialProbeOffsetY = 0f,
            float placedHeight = 0f,
            float dumpBurialThreshold = 0.5f,
            TerrainPropId? denseDebrisPropId = null)
        {
            Tile = tile;
            IsTree = isTree;
            IsDenseDebris = isDenseDebris;
            IsRemovable = isRemovable;
            CleanupObjectKey = cleanupObjectKey ?? $"{(isTree ? "tree" : isDenseDebris ? "debris" : "prop")}:{tile.X},{tile.Y}";
            DenseDebrisPropId = denseDebrisPropId;
            EligibleCleanupOrigins = eligibleCleanupOrigins
                ?? Array.Empty<Tile2i>();
            HasDumpBurialProbe = dumpBurialProbeTile.HasValue;
            DumpBurialProbeTile = dumpBurialProbeTile ?? default;
            DumpBurialProbeOffsetX = dumpBurialProbeOffsetX;
            DumpBurialProbeOffsetY = dumpBurialProbeOffsetY;
            PlacedHeight = placedHeight;
            DumpBurialThreshold = dumpBurialThreshold;
        }
    }

    /// <summary>
    /// Primitive prop/tree facts copied from the live world during capture.
    /// The cleanup policy consumes this value-owned record and never needs to
    /// enumerate a live prop or tree manager while preparing a snapshot.
    /// </summary>
    internal sealed class AccessCapturedProp
    {
        public bool IsTree { get; }
        public bool IsDenseDebris { get; }
        public string CleanupObjectKey { get; }
        public IReadOnlyList<Tile2i> OccupiedTiles { get; }
        public TerrainPropId? DenseDebrisPropId { get; }
        public bool HasDumpBurialProbe { get; }
        public Tile2i DumpBurialProbeTile { get; }
        public float DumpBurialProbeOffsetX { get; }
        public float DumpBurialProbeOffsetY { get; }
        public float PlacedHeight { get; }
        public float DumpBurialThreshold { get; }

        public AccessCapturedProp(
            bool isTree,
            bool isDenseDebris,
            string cleanupObjectKey,
            IReadOnlyList<Tile2i> occupiedTiles,
            TerrainPropId? denseDebrisPropId = null,
            Tile2i? dumpBurialProbeTile = null,
            float dumpBurialProbeOffsetX = 0f,
            float dumpBurialProbeOffsetY = 0f,
            float placedHeight = 0f,
            float dumpBurialThreshold = 0.5f)
        {
            IsTree = isTree;
            IsDenseDebris = isDenseDebris;
            CleanupObjectKey = cleanupObjectKey ?? string.Empty;
            OccupiedTiles = occupiedTiles ?? Array.Empty<Tile2i>();
            DenseDebrisPropId = denseDebrisPropId;
            HasDumpBurialProbe = dumpBurialProbeTile.HasValue;
            DumpBurialProbeTile = dumpBurialProbeTile ?? default;
            DumpBurialProbeOffsetX = dumpBurialProbeOffsetX;
            DumpBurialProbeOffsetY = dumpBurialProbeOffsetY;
            PlacedHeight = placedHeight;
            DumpBurialThreshold = dumpBurialThreshold;
        }
    }

    internal sealed class AccessPropCleanupInfo
    {
        public Tile2i Origin { get; }
        public AccessPropCleanupClass Classes { get; }
        public AccessPropBlockerKind BlockerKind { get; }
        public bool UsesTerrainRemovalPolicy { get; }
        public IReadOnlyList<AccessPropSample> Samples { get; }
        public bool IsEligible => BlockerKind == AccessPropBlockerKind.None && Classes != AccessPropCleanupClass.None;
        public bool IsEligibleWithinGeneratedV => Classes != AccessPropCleanupClass.None
            && (BlockerKind == AccessPropBlockerKind.None
                || BlockerKind == AccessPropBlockerKind.Durability
                || BlockerKind == AccessPropBlockerKind.UnderlyingTerrain);
        public bool HasTreeCleanup => (Classes & AccessPropCleanupClass.Tree) != 0;
        public bool HasDenseDebrisCleanup => (Classes & AccessPropCleanupClass.DenseDebris) != 0;

        public AccessPropCleanupInfo(Tile2i origin, AccessPropCleanupClass classes,
            AccessPropBlockerKind blockerKind, bool usesTerrainRemovalPolicy,
            IReadOnlyList<AccessPropSample>? samples = null)
        {
            Origin = origin;
            Classes = classes;
            BlockerKind = blockerKind;
            UsesTerrainRemovalPolicy = usesTerrainRemovalPolicy;
            Samples = samples ?? Array.Empty<AccessPropSample>();
        }

        public static AccessPropCleanupInfo Clear(Tile2i origin)
            => new AccessPropCleanupInfo(origin, AccessPropCleanupClass.None,
                AccessPropBlockerKind.None, false);

        public static AccessPropCleanupInfo HardBlocked(Tile2i origin, AccessPropBlockerKind blockerKind)
            => new AccessPropCleanupInfo(origin, AccessPropCleanupClass.None,
                blockerKind == AccessPropBlockerKind.None ? AccessPropBlockerKind.HardBlocker : blockerKind, false);
    }

    internal static class AccessPropCleanupPolicy
    {
        public const int NonTreeDumpRemovalThresholdHeight2 = 1;
        public const int TreeMiningRemovalThresholdHeight2 = 1;
        public const int TreeDumpingRemovalThresholdHeight2 = 2;

        public static AccessPropCleanupClass Classify(AccessPropSample sample)
        {
            AccessPropCleanupClass classes = AccessPropCleanupClass.None;
            if (sample.IsTree) classes |= AccessPropCleanupClass.Tree;
            if (sample.IsDenseDebris) classes |= AccessPropCleanupClass.DenseDebris;
            return classes;
        }

        public static bool DoesTerrainDeltaDestroyTree(
            AccessHandoffOperation operation, int terrainHeight2, int targetHeight2)
        {
            int delta = targetHeight2 - terrainHeight2;
            if (operation == AccessHandoffOperation.Mining)
                return delta <= -TreeMiningRemovalThresholdHeight2;
            if (operation == AccessHandoffOperation.Dumping)
                return delta >= TreeDumpingRemovalThresholdHeight2;
            if (operation == AccessHandoffOperation.Leveling)
                return delta <= -TreeMiningRemovalThresholdHeight2
                    || delta >= TreeDumpingRemovalThresholdHeight2;
            return false;
        }

        public static bool DoesDumpingDestroyNonTreeProp(int terrainHeight2, int targetHeight2)
        {
            int delta = targetHeight2 - terrainHeight2;
            return delta > NonTreeDumpRemovalThresholdHeight2;
        }

        public static bool DoesDumpingDestroyNonTreeProp(
            float placedHeight,
            float targetHeight,
            float burialThreshold)
            => targetHeight - placedHeight > burialThreshold;

        public static bool OperationRemovesNonTreeProp(
            AccessHandoffOperation operation, int terrainHeight2, int targetHeight2)
        {
            if (operation == AccessHandoffOperation.Dumping)
                return DoesDumpingDestroyNonTreeProp(terrainHeight2, targetHeight2);
            return false;
        }

        public static bool PlannedOperationRemovesNonTreeProp(
            AccessHandoffOperation operation, DesignationData data,
            AccessPropSample sample)
        {
            if (!TryGetDesignationTargetHeight(data, sample,
                    out float targetHeight))
                return false;
            bool buries = DoesDumpingDestroyNonTreeProp(
                sample.PlacedHeight, targetHeight,
                sample.DumpBurialThreshold);
            return operation == AccessHandoffOperation.Dumping && buries;
        }

        public static bool TryGetNonBuriedPropRemovalStrategy(
            QuickRemoveDebrisPolicy policy,
            bool buriedByPlannedDumping,
            out bool quickRemove)
        {
            if (buriedByPlannedDumping)
            {
                quickRemove = false;
                return false;
            }
            quickRemove = policy != QuickRemoveDebrisPolicy.Never;
            return true;
        }

        public static bool TryGetDesignationTargetHeight(
            DesignationData data, AccessPropSample sample,
            out float targetHeight)
        {
            if (!sample.HasDumpBurialProbe)
            {
                targetHeight = 0f;
                return false;
            }
            float worldX = sample.DumpBurialProbeTile.X
                + sample.DumpBurialProbeOffsetX;
            float worldY = sample.DumpBurialProbeTile.Y
                + sample.DumpBurialProbeOffsetY;
            float localX = worldX - data.OriginTile.X;
            float localY = worldY - data.OriginTile.Y;
            if (localX < 0f || localX > 4f
                || localY < 0f || localY > 4f)
            {
                targetHeight = 0f;
                return false;
            }
            float north = data.OriginTargetHeight.Value
                + (data.PlusXTargetHeight.Value
                    - data.OriginTargetHeight.Value) * localX / 4f;
            float south = data.PlusYTargetHeight.Value
                + (data.PlusXyTargetHeight.Value
                    - data.PlusYTargetHeight.Value) * localX / 4f;
            targetHeight = north + (south - north) * localY / 4f;
            return true;
        }

        public static AccessPropCleanupInfo BuildOriginInfo(Tile2i origin,
            IEnumerable<AccessPropSample> samples,
            AccessPropBlockerKind blockerKind = AccessPropBlockerKind.None,
            bool usesTerrainRemovalPolicy = true)
        {
            AccessPropCleanupClass classes = AccessPropCleanupClass.None;
            var collectedSamples = new List<AccessPropSample>();
            var seenSamples = new HashSet<string>(StringComparer.Ordinal);
            foreach (AccessPropSample sample in samples)
            {
                if (!sample.IsRemovable)
                    return AccessPropCleanupInfo.HardBlocked(origin, AccessPropBlockerKind.HardBlocker);
                string sampleKey = sample.CleanupObjectKey
                    + "|"
                    + sample.Tile.X.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ","
                    + sample.Tile.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "|"
                    + sample.IsTree
                    + "|"
                    + sample.IsDenseDebris;
                if (!seenSamples.Add(sampleKey))
                    continue;
                collectedSamples.Add(sample);
                classes |= Classify(sample);
            }

            if (classes == AccessPropCleanupClass.None)
                return blockerKind == AccessPropBlockerKind.None
                    ? AccessPropCleanupInfo.Clear(origin)
                    : AccessPropCleanupInfo.HardBlocked(origin, blockerKind);
            return blockerKind == AccessPropBlockerKind.None
                ? new AccessPropCleanupInfo(origin, classes, AccessPropBlockerKind.None,
                    usesTerrainRemovalPolicy, collectedSamples)
                : new AccessPropCleanupInfo(origin, classes, blockerKind,
                    usesTerrainRemovalPolicy, collectedSamples);
        }

        public static float GetCleanupLandscapingCost(
            AccessSearchPolicySnapshot? policy = null)
            => (policy ?? AccessSearchPolicySnapshot.Capture())
                .PropCleanupLandscapingCost;

        public static float GetCleanupLandscapingCost(
            bool isTree,
            AccessSearchPolicySnapshot? policy = null) =>
            isTree
                ? 0f
                : GetCleanupLandscapingCost(policy);
    }
}

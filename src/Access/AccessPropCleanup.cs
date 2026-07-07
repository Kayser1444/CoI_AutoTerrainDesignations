using System;
using System.Collections.Generic;
using Mafi;

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
        SourceWorkOrigin,
        OutOfArea
    }

    internal enum AccessPropCleanupDiagnostic
    {
        ClearGround,
        TreeCleanup,
        DenseDebrisCleanup,
        HardBlocker,
        StubbedTerrainRemovalThreshold
    }

    internal readonly struct AccessPropSample
    {
        public Tile2i Tile { get; }
        public bool IsTree { get; }
        public bool IsDenseDebris { get; }
        public bool IsRemovable { get; }
        public string CleanupObjectKey { get; }

        public AccessPropSample(Tile2i tile, bool isTree, bool isDenseDebris, bool isRemovable,
            string? cleanupObjectKey = null)
        {
            Tile = tile;
            IsTree = isTree;
            IsDenseDebris = isDenseDebris;
            IsRemovable = isRemovable;
            CleanupObjectKey = cleanupObjectKey ?? $"{(isTree ? "tree" : isDenseDebris ? "debris" : "prop")}:{tile.X},{tile.Y}";
        }
    }

    internal sealed class AccessPropCleanupInfo
    {
        public Tile2i Origin { get; }
        public AccessPropCleanupClass Classes { get; }
        public AccessPropBlockerKind BlockerKind { get; }
        public bool UsesStubbedTerrainRemovalThreshold { get; }
        public IReadOnlyList<AccessPropSample> Samples { get; }
        public bool IsEligible => BlockerKind == AccessPropBlockerKind.None && Classes != AccessPropCleanupClass.None;
        public bool HasTreeCleanup => (Classes & AccessPropCleanupClass.Tree) != 0;
        public bool HasDenseDebrisCleanup => (Classes & AccessPropCleanupClass.DenseDebris) != 0;

        public AccessPropCleanupInfo(Tile2i origin, AccessPropCleanupClass classes,
            AccessPropBlockerKind blockerKind, bool usesStubbedTerrainRemovalThreshold,
            IReadOnlyList<AccessPropSample>? samples = null)
        {
            Origin = origin;
            Classes = classes;
            BlockerKind = blockerKind;
            UsesStubbedTerrainRemovalThreshold = usesStubbedTerrainRemovalThreshold;
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
        public const int StubbedRemovalThresholdHeight2 = 2;

        public static AccessPropCleanupClass Classify(AccessPropSample sample)
        {
            AccessPropCleanupClass classes = AccessPropCleanupClass.None;
            if (sample.IsTree) classes |= AccessPropCleanupClass.Tree;
            if (sample.IsDenseDebris) classes |= AccessPropCleanupClass.DenseDebris;
            return classes;
        }

        public static bool DoesStubbedTerrainDeltaRemoveProp(
            AccessHandoffOperation operation, int terrainHeight2, int targetHeight2)
        {
            int delta = targetHeight2 - terrainHeight2;
            if (operation == AccessHandoffOperation.Mining)
                return delta <= -StubbedRemovalThresholdHeight2;
            if (operation == AccessHandoffOperation.Dumping)
                return delta >= StubbedRemovalThresholdHeight2;
            return false;
        }

        public static AccessPropCleanupInfo BuildOriginInfo(Tile2i origin,
            IEnumerable<AccessPropSample> samples,
            AccessPropBlockerKind blockerKind = AccessPropBlockerKind.None,
            bool usesStubbedTerrainRemovalThreshold = true)
        {
            if (blockerKind != AccessPropBlockerKind.None)
                return AccessPropCleanupInfo.HardBlocked(origin, blockerKind);

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

            return classes == AccessPropCleanupClass.None
                ? AccessPropCleanupInfo.Clear(origin)
                : new AccessPropCleanupInfo(origin, classes, AccessPropBlockerKind.None,
                    usesStubbedTerrainRemovalThreshold, collectedSamples);
        }

        public static float GetCleanupLandscapingCost() =>
            AutoTerrainDesignationsMod.AccessPropCleanupLandscapingCost;
    }
}

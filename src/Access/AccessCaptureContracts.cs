using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    internal readonly struct AccessCapturedLayoutOccupancy
    {
        public int FromHeight { get; }
        public int ToHeightExclusive { get; }
        public float EntityHeight { get; }

        public AccessCapturedLayoutOccupancy(
            int fromHeight,
            int toHeightExclusive,
            float entityHeight)
        {
            FromHeight = fromHeight;
            ToHeightExclusive = toHeightExclusive;
            EntityHeight = entityHeight;
        }

        public bool ContainsHeight(int height)
            => height >= FromHeight && height < ToHeightExclusive;
    }

    /// <summary>
    /// Value-owned building facts captured at the beginning of access
    /// preparation. The capture must not keep consulting the farming cache
    /// while terrain and cleanup facts are being derived.
    /// </summary>
    internal sealed class AccessCapturedBuildingFacts
    {
        private readonly HashSet<Tile2i> m_occupiedTiles;
        private readonly Dictionary<Tile2i, HashSet<int>>
            m_fixedHeights2ByTile;
        private readonly Dictionary<Tile2i, AccessCapturedLayoutOccupancy[]>
            m_layoutOccupanciesByTile;

        public int OccupiedTileCount => m_occupiedTiles.Count;
        public IReadOnlyDictionary<Tile2i, HashSet<int>> FixedHeights2ByTile
            => m_fixedHeights2ByTile;
        internal IReadOnlyDictionary<Tile2i, AccessCapturedLayoutOccupancy[]>
            LayoutOccupanciesByTile => m_layoutOccupanciesByTile;

        private AccessCapturedBuildingFacts(
            IEnumerable<Tile2i> occupiedTiles,
            IReadOnlyDictionary<Tile2i, HashSet<int>> fixedHeights2ByTile,
            IReadOnlyDictionary<Tile2i, List<AccessCapturedLayoutOccupancy>>
                layoutOccupanciesByTile)
        {
            m_occupiedTiles = new HashSet<Tile2i>(occupiedTiles);
            m_fixedHeights2ByTile = new Dictionary<Tile2i, HashSet<int>>();
            foreach (KeyValuePair<Tile2i, HashSet<int>> pair
                in fixedHeights2ByTile)
            {
                m_fixedHeights2ByTile[pair.Key] = new HashSet<int>(pair.Value);
            }
            m_layoutOccupanciesByTile =
                new Dictionary<Tile2i, AccessCapturedLayoutOccupancy[]>();
            foreach (KeyValuePair<Tile2i, List<AccessCapturedLayoutOccupancy>>
                pair in layoutOccupanciesByTile)
            {
                m_layoutOccupanciesByTile[pair.Key] = pair.Value.ToArray();
            }
        }

        internal static AccessCapturedBuildingFacts Capture(
            IEnumerable<Tile2i> occupiedTiles,
            IReadOnlyDictionary<Tile2i, HashSet<int>> fixedHeights2ByTile,
            IReadOnlyDictionary<Tile2i, List<AccessCapturedLayoutOccupancy>>?
                layoutOccupanciesByTile = null)
            => new AccessCapturedBuildingFacts(
                occupiedTiles ?? Array.Empty<Tile2i>(),
                fixedHeights2ByTile
                    ?? new Dictionary<Tile2i, HashSet<int>>(),
                layoutOccupanciesByTile
                    ?? new Dictionary<Tile2i,
                        List<AccessCapturedLayoutOccupancy>>());

        public bool ContainsOccupiedTile(Tile2i tile)
            => m_occupiedTiles.Contains(tile);

        public IEnumerable<Tile2i> EnumerateOccupiedTiles()
            => m_occupiedTiles;

        public bool DoesOriginOverlap(Tile2i origin)
        {
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    if (ContainsOccupiedTile(
                            origin + new RelTile2i(x, y)))
                        return true;
            return false;
        }
    }

    internal enum AccessCaptureInvalidationKind
    {
        None,
        EnvironmentalDirty,
        HardInvalidation
    }

    /// <summary>
    /// Tower-local settings that affect the shape or clearance of an access
    /// search. These are separate from the global semantic policy fingerprint
    /// because they belong to the servicing tower rather than the mod world.
    /// </summary>
    internal readonly struct AccessRequestSettingsRevision : IEquatable<AccessRequestSettingsRevision>
    {
        public int RampWidth { get; }
        public AutoTerrainDesignations.AccessVehicleClearanceMode ClearanceMode { get; }
        public int PlanningSettingsFingerprint { get; }

        public AccessRequestSettingsRevision(
            int rampWidth,
            AutoTerrainDesignations.AccessVehicleClearanceMode clearanceMode,
            int planningSettingsFingerprint)
        {
            RampWidth = rampWidth;
            ClearanceMode = clearanceMode;
            PlanningSettingsFingerprint = planningSettingsFingerprint;
        }

        public bool Equals(AccessRequestSettingsRevision other)
            => RampWidth == other.RampWidth
                && ClearanceMode == other.ClearanceMode
                && PlanningSettingsFingerprint
                    == other.PlanningSettingsFingerprint;

        public override bool Equals(object? obj)
            => obj is AccessRequestSettingsRevision other
                && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = RampWidth;
                hash = hash * 31 + (int)ClearanceMode;
                return hash * 31 + PlanningSettingsFingerprint;
            }
        }

        public static bool operator ==(
            AccessRequestSettingsRevision left,
            AccessRequestSettingsRevision right)
            => left.Equals(right);

        public static bool operator !=(
            AccessRequestSettingsRevision left,
            AccessRequestSettingsRevision right)
            => !left.Equals(right);
    }

    internal static class AccessCaptureRevisionPolicy
    {
        internal static AccessCaptureInvalidationKind Classify(
            AccessCaptureRevision start,
            AccessCaptureRevision current)
        {
            if (start.WorldGeneration != current.WorldGeneration
                || start.PolicyFingerprint != current.PolicyFingerprint)
                return AccessCaptureInvalidationKind.HardInvalidation;
            if (start.TerrainDesignationRevision
                != current.TerrainDesignationRevision)
                return AccessCaptureInvalidationKind.EnvironmentalDirty;
            return AccessCaptureInvalidationKind.None;
        }
    }

    /// <summary>
    /// Immutable source revisions captured before access preparation starts.
    /// World generation is a hard lifetime boundary; designation revision is
    /// an environmental source revision that may make a completed snapshot
    /// dirty without invalidating its internal structure.
    /// </summary>
    internal readonly struct AccessCaptureRevision : IEquatable<AccessCaptureRevision>
    {
        public int WorldGeneration { get; }
        public long TerrainDesignationRevision { get; }
        public int PolicyFingerprint { get; }

        public AccessCaptureRevision(
            int worldGeneration,
            long terrainDesignationRevision,
            int policyFingerprint)
        {
            WorldGeneration = worldGeneration;
            TerrainDesignationRevision = terrainDesignationRevision;
            PolicyFingerprint = policyFingerprint;
        }

        public bool Equals(AccessCaptureRevision other)
            => WorldGeneration == other.WorldGeneration
                && TerrainDesignationRevision == other.TerrainDesignationRevision
                && PolicyFingerprint == other.PolicyFingerprint;

        public override bool Equals(object? obj)
            => obj is AccessCaptureRevision other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldGeneration;
                hash = hash * 31 + TerrainDesignationRevision.GetHashCode();
                return hash * 31 + PolicyFingerprint;
            }
        }

        public static bool operator ==(
            AccessCaptureRevision left,
            AccessCaptureRevision right)
            => left.Equals(right);

        public static bool operator !=(
            AccessCaptureRevision left,
            AccessCaptureRevision right)
            => !left.Equals(right);

        public override string ToString()
            => string.Format(
                CultureInfo.InvariantCulture,
                "world={0},designations={1},policy={2}",
                WorldGeneration,
                TerrainDesignationRevision,
                PolicyFingerprint);
    }

    /// <summary>
    /// Runtime-only capture outcome. A dirty capture remains structurally
    /// coherent and may finish, but its success is provisional until the
    /// caller validates it against live state.
    /// </summary>
    internal sealed class AccessCaptureDiagnostics
    {
        public AccessCaptureRevision StartRevision { get; }
        public AccessCaptureRevision CompletionRevision { get; private set; }
        public bool IsEnvironmentallyDirty { get; private set; }
        public string DirtyReason { get; private set; } = string.Empty;
        public long EstimatedRetainedBytes { get; private set; }
        public long MemoryCeilingBytes { get; }
        public string TerminalReason { get; private set; } = string.Empty;

        public AccessCaptureDiagnostics(
            AccessCaptureRevision startRevision,
            long memoryCeilingBytes)
        {
            StartRevision = startRevision;
            CompletionRevision = startRevision;
            MemoryCeilingBytes = Math.Max(0L, memoryCeilingBytes);
        }

        public void ObserveCompletion(AccessCaptureRevision revision)
            => CompletionRevision = revision;

        public void MarkEnvironmentallyDirty(
            AccessCaptureRevision revision,
            string reason)
        {
            CompletionRevision = revision;
            IsEnvironmentallyDirty = true;
            if (string.IsNullOrEmpty(DirtyReason))
                DirtyReason = string.IsNullOrEmpty(reason)
                    ? "EnvironmentalRevisionChanged"
                    : reason;
        }

        public void SetEstimatedRetainedBytes(long bytes)
            => EstimatedRetainedBytes = Math.Max(0L, bytes);

        public void SetTerminalReason(string reason)
            => TerminalReason = reason ?? string.Empty;

        public string Format()
            => string.Format(
                CultureInfo.InvariantCulture,
                "capture=[start:{0} complete:{1} dirty:{2} reason:{3} "
                    + "estimatedMiB:{4:0.##} ceilingMiB:{5:0.##} terminal:{6}]",
                StartRevision,
                CompletionRevision,
                IsEnvironmentallyDirty,
                string.IsNullOrEmpty(DirtyReason) ? "none" : DirtyReason,
                EstimatedRetainedBytes / (1024d * 1024d),
                MemoryCeilingBytes / (1024d * 1024d),
                string.IsNullOrEmpty(TerminalReason) ? "none" : TerminalReason);
    }

    /// <summary>
    /// Conservative retained-memory guard used before large capture growth.
    /// It is deliberately an estimate rather than a GC measurement: the
    /// latter is global, noisy, and too late to prevent an oversized capture.
    /// </summary>
    internal sealed class AccessCaptureMemoryBudget
    {
        public long CeilingBytes { get; }
        public long EstimatedRetainedBytes { get; private set; }

        public AccessCaptureMemoryBudget(long ceilingBytes)
        {
            CeilingBytes = Math.Max(0L, ceilingBytes);
        }

        public bool TryAccept(long estimatedRetainedBytes)
        {
            EstimatedRetainedBytes = Math.Max(0L, estimatedRetainedBytes);
            return EstimatedRetainedBytes <= CeilingBytes;
        }
    }

    /// <summary>
    /// Non-blocking process-local capture slot. The manager already permits
    /// one active access request; this slot also prevents a future worker
    /// handoff or an accidental nested caller from allocating two large
    /// primitive snapshots at once.
    /// </summary>
    internal static class AccessCaptureBackpressure
    {
        private static int s_activeCapture;

        internal static IDisposable? TryAcquire()
        {
            return Interlocked.CompareExchange(ref s_activeCapture, 1, 0) == 0
                ? new CaptureLease()
                : null;
        }

        private sealed class CaptureLease : IDisposable
        {
            private int m_released;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_released, 1) == 0)
                    Volatile.Write(ref s_activeCapture, 0);
            }
        }
    }

    /// <summary>
    /// Stable, intentionally conservative estimate for the collections kept
    /// by a prepared access snapshot and its immediate derived graphs.
    /// Constants are implementation accounting units, not serialized sizes.
    /// </summary>
    internal static class AccessSnapshotMemoryEstimator
    {
        private const long BaseBytes = 8L * 1024L * 1024L;

        internal static long EstimateRetainedBytes(
            long terrainTileCount,
            long terrainCenterCount,
            long terrainColumnCount,
            long fixedProfileCount,
            long workOriginCount,
            long groundNodeCount,
            long goalNodeCount,
            long designationCount,
            long projectedDisturbanceCount,
            long cleanupOriginCount,
            long cleanupTileCount,
            long durabilityCornerCount,
            long buildingOccupiedTileCount = 0,
            long designationReadinessFactCount = 0)
        {
            long estimate = BaseBytes;
            estimate = Add(estimate, terrainTileCount, 320L);
            estimate = Add(estimate, terrainCenterCount, 192L);
            estimate = Add(estimate, terrainColumnCount, 768L);
            estimate = Add(estimate, fixedProfileCount, 384L);
            estimate = Add(estimate, workOriginCount, 256L);
            estimate = Add(estimate, groundNodeCount, 256L);
            estimate = Add(estimate, goalNodeCount, 256L);
            estimate = Add(estimate, designationCount, 384L);
            estimate = Add(estimate, projectedDisturbanceCount, 768L);
            estimate = Add(estimate, cleanupOriginCount, 1024L);
            estimate = Add(estimate, cleanupTileCount, 512L);
            estimate = Add(estimate, durabilityCornerCount, 512L);
            estimate = Add(estimate, buildingOccupiedTileCount, 256L);
            estimate = Add(estimate, designationReadinessFactCount, 192L);
            return estimate;
        }

        private static long Add(long total, long count, long bytesPerItem)
        {
            if (count <= 0 || bytesPerItem <= 0)
                return total;
            long addition;
            try
            {
                checked
                {
                    addition = count * bytesPerItem;
                    return checked(total + addition);
                }
            }
            catch (OverflowException)
            {
                return long.MaxValue;
            }
        }
    }
}

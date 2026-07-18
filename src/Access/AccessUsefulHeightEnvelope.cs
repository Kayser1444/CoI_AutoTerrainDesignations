using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    internal readonly struct AccessUsefulHeightEnvelopeDiagnostics
    {
        public int Width { get; }
        public int Height { get; }
        public int TileCount { get; }
        public int TerrainSourceCount { get; }
        public int OceanUpperSourceCount { get; }
        public int FixedProfileCount { get; }
        public int FixedProfileSampleCount { get; }
        public int MissingBandCount { get; }
        public int MinimumBandWidth32 { get; }
        public int MaximumBandWidth32 { get; }
        public double AverageBandWidth32 { get; }
        public long ArrayBytes { get; }

        public AccessUsefulHeightEnvelopeDiagnostics(
            int width,
            int height,
            int terrainSourceCount,
            int oceanUpperSourceCount,
            int fixedProfileCount,
            int fixedProfileSampleCount,
            int missingBandCount,
            int minimumBandWidth32,
            int maximumBandWidth32,
            double averageBandWidth32)
        {
            Width = width;
            Height = height;
            TileCount = checked(width * height);
            TerrainSourceCount = terrainSourceCount;
            OceanUpperSourceCount = oceanUpperSourceCount;
            FixedProfileCount = fixedProfileCount;
            FixedProfileSampleCount = fixedProfileSampleCount;
            MissingBandCount = missingBandCount;
            MinimumBandWidth32 = minimumBandWidth32;
            MaximumBandWidth32 = maximumBandWidth32;
            AverageBandWidth32 = averageBandWidth32;
            ArrayBytes = (long)TileCount * sizeof(int) * 2;
        }
    }

    /// <summary>
    /// Immutable dense upper/lower useful-height fields for one captured access
    /// snapshot. The experimental V1 search uses it to prune generated-profile
    /// centers only; fixed and ground nodes remain explicit search nodes.
    /// </summary>
    internal sealed class AccessUsefulHeightEnvelope
    {
        private const int NEGATIVE_INFINITY = int.MinValue;
        private const int POSITIVE_INFINITY = int.MaxValue;
        private const int GRADE_STEP32 = 8;
        private const int OCEAN_MINIMUM_DRIVABLE_HEIGHT32 = 32;
        internal const int DEFAULT_V1_LOWER_ALLOWANCE32 = 16;
        internal const int DEFAULT_V2_LOWER_ALLOWANCE32 = 32;
        internal const int DEFAULT_V1_UPPER_ALLOWANCE32 = 16;
        internal const int DEFAULT_V2_UPPER_ALLOWANCE32 = 32;

        private readonly int[] m_upperHeight32;
        private readonly int[] m_lowerHeight32;
        private readonly int m_v1LowerTargetExtension32;
        private readonly int m_v2LowerTargetExtension32;
        private readonly int m_v1UpperTargetExtension32;
        private readonly int m_v2UpperTargetExtension32;

        public Tile2i Min { get; }
        public int Width { get; }
        public int Height { get; }
        public int V1LowerAllowance32 => m_v1LowerTargetExtension32;
        public int V2LowerAllowance32 => m_v2LowerTargetExtension32;
        public int V1UpperAllowance32 => m_v1UpperTargetExtension32;
        public int V2UpperAllowance32 => m_v2UpperTargetExtension32;
        public AccessUsefulHeightEnvelopeDiagnostics Diagnostics { get; }

        private AccessUsefulHeightEnvelope(
            Tile2i min,
            int width,
            int height,
            int[] upperHeight32,
            int[] lowerHeight32,
            int v1LowerAllowance32,
            int v2LowerAllowance32,
            int v1UpperAllowance32,
            int v2UpperAllowance32,
            AccessUsefulHeightEnvelopeDiagnostics diagnostics)
        {
            Min = min;
            Width = width;
            Height = height;
            m_upperHeight32 = upperHeight32;
            m_lowerHeight32 = lowerHeight32;
            m_v1LowerTargetExtension32 = v1LowerAllowance32;
            m_v2LowerTargetExtension32 = v2LowerAllowance32;
            m_v1UpperTargetExtension32 = v1UpperAllowance32;
            m_v2UpperTargetExtension32 = v2UpperAllowance32;
            Diagnostics = diagnostics;
        }

        public bool TryGetBand(
            Tile2i tile,
            out int lowerHeight32,
            out int upperHeight32)
        {
            if (!TryGetIndex(tile, out int index)
                || m_upperHeight32[index] == NEGATIVE_INFINITY
                || m_lowerHeight32[index] == POSITIVE_INFINITY)
            {
                lowerHeight32 = 0;
                upperHeight32 = 0;
                return false;
            }

            lowerHeight32 = m_lowerHeight32[index];
            upperHeight32 = m_upperHeight32[index];
            return true;
        }

        public bool IsV1CenterHeightUseful(
            Tile2i center,
            int centerHeight32,
            out string rejection)
            => IsCenterHeightUseful(
                center, centerHeight32, out rejection);

        public bool IsV2CenterHeightUseful(
            Tile2i center,
            int centerHeight32,
            out string rejection)
            => IsCenterHeightUseful(
                center, centerHeight32, out rejection);

        private bool IsCenterHeightUseful(
            Tile2i center,
            int centerHeight32,
            out string rejection)
        {
            if (!TryGetBand(center, out int lowerHeight32, out int upperHeight32))
            {
                rejection = "HeightEnvelopeMissingSample";
                return true;
            }
            if (centerHeight32 > upperHeight32)
            {
                rejection = "HeightEnvelopeAbove";
                return false;
            }
            if (centerHeight32 < lowerHeight32)
            {
                rejection = "HeightEnvelopeBelow";
                return false;
            }

            rejection = string.Empty;
            return true;
        }

        /// <summary>
        /// Returns a request-local strict hull whose fixed targets have been
        /// extended vertically before cone propagation. This localizes the
        /// flat turn-landing exception to the target approach instead of
        /// admitting the same excess height at every generated center.
        /// </summary>
        public AccessUsefulHeightEnvelope WithExtendedFixedTargets(
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            IEnumerable<Tile2i> targetOrigins,
            bool useV2)
        {
            int lowerExtension32 = useV2
                ? m_v2LowerTargetExtension32
                : m_v1LowerTargetExtension32;
            int upperExtension32 = useV2
                ? m_v2UpperTargetExtension32
                : m_v1UpperTargetExtension32;
            if (lowerExtension32 == 0 && upperExtension32 == 0)
                return this;

            var targets = new List<KeyValuePair<Tile2i, AccessHeightProfile>>();
            var seen = new HashSet<Tile2i>();
            foreach (Tile2i origin in targetOrigins)
                if (seen.Add(origin)
                    && fixedProfiles.TryGetValue(
                        origin, out AccessHeightProfile profile))
                    targets.Add(new KeyValuePair<Tile2i, AccessHeightProfile>(
                        origin, profile));
            if (targets.Count == 0)
                return this;

            int[] upperHeight32 = (int[])m_upperHeight32.Clone();
            int[] lowerHeight32 = (int[])m_lowerHeight32.Clone();
            bool changedUpper = false;
            bool changedLower = false;
            foreach (KeyValuePair<Tile2i, AccessHeightProfile> target in targets)
            {
                Tile2i origin = target.Key;
                AccessHeightProfile profile = target.Value;
                for (int y = 0; y <= 4; y++)
                {
                    for (int x = 0; x <= 4; x++)
                    {
                        Tile2i tile = origin + new RelTile2i(x, y);
                        if (!TryGetIndex(tile, Min, Width, Height, out int index))
                            continue;
                        int height32 = profile.GetHeight2NumeratorAt(x, y);
                        if (upperExtension32 > 0)
                        {
                            int extended = SaturatingAdd(
                                height32, upperExtension32);
                            if (extended > upperHeight32[index])
                            {
                                upperHeight32[index] = extended;
                                changedUpper = true;
                            }
                        }
                        if (lowerExtension32 > 0)
                        {
                            int extended = SaturatingAdd(
                                height32, -(long)lowerExtension32);
                            if (extended < lowerHeight32[index])
                            {
                                lowerHeight32[index] = extended;
                                changedLower = true;
                            }
                        }
                    }
                }
            }
            if (!changedUpper && !changedLower)
                return this;
            if (changedUpper) RelaxUpper(upperHeight32, Width, Height);
            if (changedLower) RelaxLower(lowerHeight32, Width, Height);
            return new AccessUsefulHeightEnvelope(
                Min, Width, Height, upperHeight32, lowerHeight32,
                m_v1LowerTargetExtension32, m_v2LowerTargetExtension32,
                m_v1UpperTargetExtension32, m_v2UpperTargetExtension32,
                Diagnostics);
        }

        public static bool TryCreate(
            IReadOnlyDictionary<Tile2i, float> preciseTerrainHeights,
            IReadOnlyCollection<Tile2i> oceanTiles,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            out AccessUsefulHeightEnvelope? envelope,
            out string failureReason,
            int v1LowerAllowance32 = DEFAULT_V1_LOWER_ALLOWANCE32,
            int v2LowerAllowance32 = DEFAULT_V2_LOWER_ALLOWANCE32,
            int v1UpperAllowance32 = DEFAULT_V1_UPPER_ALLOWANCE32,
            int v2UpperAllowance32 = DEFAULT_V2_UPPER_ALLOWANCE32)
        {
            envelope = null;
            failureReason = string.Empty;
            if (v1LowerAllowance32 < 0 || v2LowerAllowance32 < 0
                || v1UpperAllowance32 < 0 || v2UpperAllowance32 < 0)
            {
                failureReason = "NegativeAllowance";
                return false;
            }
            if (preciseTerrainHeights.Count == 0)
            {
                failureReason = "NoPreciseTerrainSamples";
                return false;
            }

            Tile2i min = default;
            Tile2i max = default;
            bool hasBounds = false;
            foreach (Tile2i tile in preciseTerrainHeights.Keys)
            {
                if (!hasBounds)
                {
                    min = tile;
                    max = tile;
                    hasBounds = true;
                    continue;
                }
                min = new Tile2i(Math.Min(min.X, tile.X), Math.Min(min.Y, tile.Y));
                max = new Tile2i(Math.Max(max.X, tile.X), Math.Max(max.Y, tile.Y));
            }

            long widthLong = (long)max.X - min.X + 1;
            long heightLong = (long)max.Y - min.Y + 1;
            long tileCountLong = widthLong * heightLong;
            if (widthLong <= 0 || heightLong <= 0 || tileCountLong > int.MaxValue)
            {
                failureReason = "EnvelopeBoundsTooLarge";
                return false;
            }

            int width = (int)widthLong;
            int height = (int)heightLong;
            int tileCount = (int)tileCountLong;
            var upperHeight32 = new int[tileCount];
            var lowerHeight32 = new int[tileCount];
            for (int index = 0; index < tileCount; index++)
            {
                upperHeight32[index] = NEGATIVE_INFINITY;
                lowerHeight32[index] = POSITIVE_INFINITY;
            }

            int terrainSourceCount = 0;
            foreach (KeyValuePair<Tile2i, float> pair in preciseTerrainHeights)
            {
                if (!TryGetIndex(pair.Key, min, width, height, out int index))
                    continue;
                upperHeight32[index] = Math.Max(
                    upperHeight32[index], ToUpperHeight32(pair.Value));
                lowerHeight32[index] = Math.Min(
                    lowerHeight32[index], ToLowerHeight32(pair.Value));
                terrainSourceCount++;
            }

            int oceanUpperSourceCount = 0;
            foreach (Tile2i tile in oceanTiles)
            {
                if (!TryGetIndex(tile, min, width, height, out int index))
                    continue;
                upperHeight32[index] = Math.Max(
                    upperHeight32[index], OCEAN_MINIMUM_DRIVABLE_HEIGHT32);
                oceanUpperSourceCount++;
            }

            int fixedProfileSampleCount = 0;
            foreach (KeyValuePair<Tile2i, AccessHeightProfile> pair in fixedProfiles)
            {
                for (int y = 0; y <= 4; y++)
                {
                    for (int x = 0; x <= 4; x++)
                    {
                        Tile2i tile = pair.Key + new RelTile2i(x, y);
                        if (!TryGetIndex(tile, min, width, height, out int index))
                            continue;
                        int height32 = pair.Value.GetHeight2NumeratorAt(x, y);
                        upperHeight32[index] = Math.Max(upperHeight32[index], height32);
                        lowerHeight32[index] = Math.Min(lowerHeight32[index], height32);
                        fixedProfileSampleCount++;
                    }
                }
            }

            RelaxUpper(upperHeight32, width, height);
            RelaxLower(lowerHeight32, width, height);

            int missingBandCount = 0;
            int minimumBandWidth32 = int.MaxValue;
            int maximumBandWidth32 = int.MinValue;
            long totalBandWidth32 = 0;
            int measuredBandCount = 0;
            for (int index = 0; index < tileCount; index++)
            {
                if (upperHeight32[index] == NEGATIVE_INFINITY
                    || lowerHeight32[index] == POSITIVE_INFINITY)
                {
                    missingBandCount++;
                    continue;
                }
                int bandWidth32 = upperHeight32[index] - lowerHeight32[index];
                minimumBandWidth32 = Math.Min(minimumBandWidth32, bandWidth32);
                maximumBandWidth32 = Math.Max(maximumBandWidth32, bandWidth32);
                totalBandWidth32 += bandWidth32;
                measuredBandCount++;
            }

            if (measuredBandCount == 0)
            {
                failureReason = "NoEnvelopeBands";
                return false;
            }

            var diagnostics = new AccessUsefulHeightEnvelopeDiagnostics(
                width,
                height,
                terrainSourceCount,
                oceanUpperSourceCount,
                fixedProfiles.Count,
                fixedProfileSampleCount,
                missingBandCount,
                minimumBandWidth32,
                maximumBandWidth32,
                (double)totalBandWidth32 / measuredBandCount);
            envelope = new AccessUsefulHeightEnvelope(
                min, width, height, upperHeight32, lowerHeight32,
                v1LowerAllowance32, v2LowerAllowance32,
                v1UpperAllowance32, v2UpperAllowance32, diagnostics);
            return true;
        }

        internal static bool ValidateSelfTest(out string failure)
        {
            var terrain = new Dictionary<Tile2i, float>();
            for (int y = 0; y <= 4; y++)
                for (int x = 0; x <= 8; x++)
                    terrain.Add(new Tile2i(x, y), 0f);
            var fixedProfiles = new Dictionary<Tile2i, AccessHeightProfile>
            {
                [Tile2i.Zero] = new AccessHeightProfile(8, 8, 8, 8)
            };
            if (!TryCreate(
                    terrain,
                    Array.Empty<Tile2i>(),
                    fixedProfiles,
                    out AccessUsefulHeightEnvelope? envelope,
                    out failure)
                || envelope == null)
            {
                failure = "BuildFailed:" + failure;
                return false;
            }
            if (!envelope.TryGetBand(new Tile2i(8, 2), out int lowerHeight32,
                    out int upperHeight32)
                || lowerHeight32 != 0
                || upperHeight32 != 96)
            {
                failure = "UnexpectedConeAt8x2:lower=" + lowerHeight32
                    + ",upper=" + upperHeight32;
                return false;
            }
            if (!envelope.IsV1CenterHeightUseful(new Tile2i(8, 2), 96, out _))
            {
                failure = "StrictUpperBoundaryRejected";
                return false;
            }
            bool acceptedAbove = envelope.IsV1CenterHeightUseful(
                new Tile2i(8, 2), 97, out string rejection);
            if (acceptedAbove || rejection != "HeightEnvelopeAbove")
            {
                failure = "CenterRejectionMismatch:" + rejection;
                return false;
            }

            AccessUsefulHeightEnvelope extended =
                envelope.WithExtendedFixedTargets(
                    fixedProfiles, new[] { Tile2i.Zero }, useV2: false);
            if (!extended.TryGetBand(
                    new Tile2i(8, 2), out int extendedLower32,
                    out int extendedUpper32)
                || extendedLower32 != 0
                || extendedUpper32 != 112
                || !extended.IsV1CenterHeightUseful(
                    new Tile2i(8, 2), 112, out _)
                || extended.IsV1CenterHeightUseful(
                    new Tile2i(8, 2), 113, out _))
            {
                failure = "TargetExtensionMismatch:lower=" + extendedLower32
                    + ",upper=" + extendedUpper32;
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private bool TryGetIndex(Tile2i tile, out int index)
            => TryGetIndex(tile, Min, Width, Height, out index);

        private static bool TryGetIndex(
            Tile2i tile,
            Tile2i min,
            int width,
            int height,
            out int index)
        {
            int x = tile.X - min.X;
            int y = tile.Y - min.Y;
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                index = 0;
                return false;
            }
            index = y * width + x;
            return true;
        }

        private static int ToUpperHeight32(float height)
            => checked((int)Math.Ceiling((double)height * 32d));

        private static int ToLowerHeight32(float height)
            => checked((int)Math.Floor((double)height * 32d));

        private static int SaturatingAdd(int value, long delta)
        {
            long result = (long)value + delta;
            if (result > int.MaxValue) return int.MaxValue;
            if (result < int.MinValue) return int.MinValue;
            return (int)result;
        }

        private static void RelaxUpper(int[] upperHeight32, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * width;
                for (int x = 1; x < width; x++)
                    RelaxUpperFrom(upperHeight32, rowStart + x, rowStart + x - 1);
                for (int x = width - 2; x >= 0; x--)
                    RelaxUpperFrom(upperHeight32, rowStart + x, rowStart + x + 1);
            }
            for (int x = 0; x < width; x++)
            {
                for (int y = 1; y < height; y++)
                    RelaxUpperFrom(upperHeight32, y * width + x, (y - 1) * width + x);
                for (int y = height - 2; y >= 0; y--)
                    RelaxUpperFrom(upperHeight32, y * width + x, (y + 1) * width + x);
            }
        }

        private static void RelaxLower(int[] lowerHeight32, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * width;
                for (int x = 1; x < width; x++)
                    RelaxLowerFrom(lowerHeight32, rowStart + x, rowStart + x - 1);
                for (int x = width - 2; x >= 0; x--)
                    RelaxLowerFrom(lowerHeight32, rowStart + x, rowStart + x + 1);
            }
            for (int x = 0; x < width; x++)
            {
                for (int y = 1; y < height; y++)
                    RelaxLowerFrom(lowerHeight32, y * width + x, (y - 1) * width + x);
                for (int y = height - 2; y >= 0; y--)
                    RelaxLowerFrom(lowerHeight32, y * width + x, (y + 1) * width + x);
            }
        }

        private static void RelaxUpperFrom(int[] values, int currentIndex, int neighborIndex)
        {
            int neighbor = values[neighborIndex];
            if (neighbor == NEGATIVE_INFINITY)
                return;
            values[currentIndex] = Math.Max(values[currentIndex], neighbor - GRADE_STEP32);
        }

        private static void RelaxLowerFrom(int[] values, int currentIndex, int neighborIndex)
        {
            int neighbor = values[neighborIndex];
            if (neighbor == POSITIVE_INFINITY)
                return;
            values[currentIndex] = Math.Min(values[currentIndex], neighbor + GRADE_STEP32);
        }
    }
}

using System;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    internal readonly struct AccessSideRayCacheKey : IEquatable<AccessSideRayCacheKey>
    {
        private readonly Tile2i m_corner;
        private readonly int m_plannedHeight2;
        private readonly Tile2i m_direction;
        private readonly AccessHandoffOperation m_workOperation;
        private readonly bool m_hasExemptDesignationOrigin;
        private readonly Tile2i m_exemptDesignationOrigin;

        public AccessSideRayCacheKey(
            Tile2i corner,
            int plannedHeight2,
            Tile2i direction,
            AccessHandoffOperation workOperation,
            Tile2i? exemptDesignationOrigin = null)
        {
            m_corner = corner;
            m_plannedHeight2 = plannedHeight2;
            m_direction = direction;
            m_workOperation = workOperation;
            m_hasExemptDesignationOrigin = exemptDesignationOrigin.HasValue;
            m_exemptDesignationOrigin = exemptDesignationOrigin.GetValueOrDefault();
        }

        public bool Equals(AccessSideRayCacheKey other)
            => m_corner == other.m_corner
                && m_plannedHeight2 == other.m_plannedHeight2
                && m_direction == other.m_direction
                && m_workOperation == other.m_workOperation
                && m_hasExemptDesignationOrigin == other.m_hasExemptDesignationOrigin
                && (!m_hasExemptDesignationOrigin
                    || m_exemptDesignationOrigin == other.m_exemptDesignationOrigin);

        public override bool Equals(object? obj)
            => obj is AccessSideRayCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = m_corner.GetHashCode();
                hash = (hash * 397) ^ m_plannedHeight2;
                hash = (hash * 397) ^ m_direction.GetHashCode();
                hash = (hash * 397) ^ (int)m_workOperation;
                hash = (hash * 397) ^ m_hasExemptDesignationOrigin.GetHashCode();
                if (m_hasExemptDesignationOrigin)
                    hash = (hash * 397) ^ m_exemptDesignationOrigin.GetHashCode();
                return hash;
            }
        }
    }

    internal enum AccessSideRayOperation
    {
        None,
        Fill,
        Cut,
    }

    internal readonly struct AccessSideRayTerrainSample
    {
        public AccessTerrainSampleKind Kind { get; }
        public float TerrainHeight { get; }
        public string? BlockerReason { get; }
        public bool TerminatesSameOperationRay { get; }

        public AccessSideRayTerrainSample(
            AccessTerrainSampleKind kind,
            float terrainHeight,
            string? blockerReason = null,
            bool terminatesSameOperationRay = false)
        {
            Kind = kind;
            TerrainHeight = terrainHeight;
            BlockerReason = blockerReason;
            TerminatesSameOperationRay = terminatesSameOperationRay;
        }
    }

    internal readonly struct AccessSideRayResult
    {
        public float IntegratedCost { get; }
        public float UnresolvedPenalty { get; }
        public int SampleCount { get; }
        public bool IsUnresolved { get; }
        public bool ReachedCostCap { get; }
        public string? FatalReason { get; }
        public int DisturbedDistance { get; }
        public float TotalCost => IntegratedCost + UnresolvedPenalty;
        public bool IsFatal => !string.IsNullOrEmpty(FatalReason);

        public AccessSideRayResult(
            float integratedCost,
            float unresolvedPenalty,
            int sampleCount,
            bool isUnresolved,
            bool reachedCostCap,
            string? fatalReason = null,
            int disturbedDistance = 0)
        {
            IntegratedCost = integratedCost;
            UnresolvedPenalty = unresolvedPenalty;
            SampleCount = sampleCount;
            IsUnresolved = isUnresolved;
            ReachedCostCap = reachedCostCap;
            FatalReason = fatalReason;
            DisturbedDistance = disturbedDistance;
        }
    }

    internal static class AccessSideRayCost
    {
        internal const float DefaultMaxRayCost = 500f;
        internal const float DefaultUnresolvedPenalty = 200f;
        private const float MinimumDryOceanHeight = 1f;
        private const string BuildingBlockerReason = "SideRayBuilding";
        private static readonly int[] s_sampleDistances = { 1, 2, 3, 5, 8, 13, 16 };

        public static AccessSideRayResult Score(
            AccessSearchSnapshot snapshot,
            Tile2i corner,
            Tile2i lateralDirection,
            float plannedCornerHeight,
            AccessSideRayOperation operation,
            float materialSlope,
            float maxRayCost = DefaultMaxRayCost,
            float unresolvedPenalty = DefaultUnresolvedPenalty,
            int postTerminationSafetyMargin = 0,
            int? maxTraceDistance = null,
            Tile2i? exemptDesignationOrigin = null,
            Func<Tile2i, AccessSideRayOperation, bool>?
                terminatesAtSameOperationRay = null)
            => Score(
                tile =>
                {
                    AccessTerrainSampleKind kind =
                        snapshot.GetSideRayTerrainSample(tile, out float height);
                    return new AccessSideRayTerrainSample(
                        kind, height, snapshot.GetSideRayBlockerReason(
                            tile, operation, exemptDesignationOrigin),
                        terminatesSameOperationRay:
                            terminatesAtSameOperationRay?.Invoke(
                                tile, operation) == true);
                },
                corner,
                lateralDirection,
                plannedCornerHeight,
                operation,
                materialSlope,
                maxRayCost,
                unresolvedPenalty,
                postTerminationSafetyMargin,
                maxTraceDistance,
                snapshot.AvoidOcean);

        internal static AccessSideRayResult Score(
            Func<Tile2i, AccessSideRayTerrainSample> sampleTerrain,
            Tile2i corner,
            Tile2i lateralDirection,
            float plannedCornerHeight,
            AccessSideRayOperation operation,
            float materialSlope,
            float maxRayCost = DefaultMaxRayCost,
            float unresolvedPenalty = DefaultUnresolvedPenalty,
            int postTerminationSafetyMargin = 0,
            int? maxTraceDistance = null,
            bool avoidOcean = true)
        {
            if (operation == AccessSideRayOperation.None)
                return new AccessSideRayResult(0f, 0f, 0, false, false);
            if ((Math.Abs(lateralDirection.X) + Math.Abs(lateralDirection.Y)) != 1)
                return Fatal("SideRayInvalidDirection", 0, 0f);
            if (materialSlope <= 0f || float.IsNaN(materialSlope)
                || float.IsInfinity(materialSlope))
                return Fatal("SideRayInvalidMaterialSlope", 0, 0f);
            if (maxRayCost < 0f || unresolvedPenalty < 0f || postTerminationSafetyMargin < 0
                || (maxTraceDistance.HasValue && maxTraceDistance.Value < 1))
                return Fatal("SideRayInvalidCostLimit", 0, 0f);

            float integratedCost = 0f;
            int previousDistance = 0;
            int sampleCount = 0;
            int disturbedDistance = 0;
            int maxDistance = maxTraceDistance
                ?? s_sampleDistances[s_sampleDistances.Length - 1];
            for (int distance = 1; distance <= maxDistance; distance++)
            {
                Tile2i tile = new Tile2i(
                    corner.X + lateralDirection.X * distance,
                    corner.Y + lateralDirection.Y * distance);
                AccessSideRayTerrainSample sample = sampleTerrain(tile);
                sampleCount++;

                if (sample.Kind == AccessTerrainSampleKind.MissingSnapshot)
                    return Fatal("SideRaySnapshotMissing", sampleCount, integratedCost);
                if (sample.Kind == AccessTerrainSampleKind.PhysicalMapEdge)
                {
                    if (operation == AccessSideRayOperation.Fill)
                        return Fatal("SideRayFillMapEdge", sampleCount, integratedCost);
                    return new AccessSideRayResult(
                        integratedCost, 0f, sampleCount, false, false,
                        disturbedDistance: disturbedDistance);
                }
                if (sample.TerminatesSameOperationRay)
                {
                    return new AccessSideRayResult(
                        integratedCost, 0f, sampleCount, false, false,
                        disturbedDistance: Math.Max(0, distance - 1));
                }
                float rayHeight = operation == AccessSideRayOperation.Fill
                    ? plannedCornerHeight - distance * materialSlope
                    : plannedCornerHeight + distance * materialSlope;
                float gap = operation == AccessSideRayOperation.Fill
                    ? rayHeight - sample.TerrainHeight
                    : sample.TerrainHeight - rayHeight;
                bool hasPassedTerrain = gap <= 0f;
                bool hasReachedDryCutHeight =
                    operation != AccessSideRayOperation.Cut
                    || rayHeight >= MinimumDryOceanHeight;
                if (hasPassedTerrain && hasReachedDryCutHeight)
                {
                    disturbedDistance = Math.Min(
                        maxDistance,
                        distance + postTerminationSafetyMargin);
                    for (int safetyDistance = distance + 1;
                        safetyDistance <= Math.Min(maxDistance, distance + postTerminationSafetyMargin);
                        safetyDistance++)
                    {
                        Tile2i safetyTile = new Tile2i(
                            corner.X + lateralDirection.X * safetyDistance,
                            corner.Y + lateralDirection.Y * safetyDistance);
                        AccessSideRayTerrainSample safetySample = sampleTerrain(safetyTile);
                        sampleCount++;
                        if (safetySample.Kind == AccessTerrainSampleKind.MissingSnapshot)
                            return Fatal("SideRaySnapshotMissing", sampleCount, integratedCost);
                        if (safetySample.Kind == AccessTerrainSampleKind.PhysicalMapEdge)
                        {
                            if (operation == AccessSideRayOperation.Fill)
                                return Fatal("SideRayFillMapEdge", sampleCount, integratedCost);
                            continue;
                        }
                        if (avoidOcean
                            && operation == AccessSideRayOperation.Cut
                            && safetySample.Kind == AccessTerrainSampleKind.Ocean
                            // The ray has already terminated.  The configured
                            // tail is a spatial safety margin around the
                            // below-sea-level cut, not a continuation whose
                            // synthetic height may rise above the ocean.
                            && plannedCornerHeight < MinimumDryOceanHeight)
                            return Fatal("SideRayCutOcean", sampleCount, integratedCost);
                        // The building-specific margin applies to the active
                        // ray. Do not stack the generic end buffer on top of it
                        // after the ray has already met terrain.
                        if (!string.IsNullOrEmpty(safetySample.BlockerReason)
                            && safetySample.BlockerReason != BuildingBlockerReason)
                            return Fatal(safetySample.BlockerReason!, sampleCount, integratedCost);
                    }
                    return new AccessSideRayResult(
                        integratedCost, 0f, sampleCount, false, false,
                        disturbedDistance: disturbedDistance);
                }
                if (avoidOcean
                    && operation == AccessSideRayOperation.Cut
                    && sample.Kind == AccessTerrainSampleKind.Ocean
                    && rayHeight < MinimumDryOceanHeight)
                    return Fatal("SideRayCutOcean", sampleCount, integratedCost);
                if (!string.IsNullOrEmpty(sample.BlockerReason))
                    return Fatal(sample.BlockerReason!, sampleCount, integratedCost);

                // A cut below +1 remains active after first meeting low
                // terrain so ocean cannot hide immediately beyond that
                // intersection.  This dry-threshold extension is protection,
                // not additional excavation cost.
                if (hasPassedTerrain)
                {
                    disturbedDistance = distance;
                    continue;
                }

                // Feasibility is dense: an ocean, building, designation, or
                // terrain intersection at a skipped Fibonacci distance must
                // not be invisible to the ray.  Cost remains sampled at the
                // original accelerating distances so this safety change does
                // not retune the established cost model.
                if (!IsCostSampleDistance(distance))
                {
                    disturbedDistance = distance;
                    continue;
                }

                int stepLength = distance - previousDistance;
                integratedCost = Math.Min(
                    maxRayCost,
                    integratedCost + stepLength * gap);
                disturbedDistance = distance;
                if (integratedCost >= maxRayCost)
                    return new AccessSideRayResult(
                        maxRayCost, 0f, sampleCount, true, true,
                        disturbedDistance: disturbedDistance);
                previousDistance = distance;
            }

            float appliedPenalty = Math.Min(
                unresolvedPenalty,
                maxRayCost - integratedCost);
            return new AccessSideRayResult(
                integratedCost,
                appliedPenalty,
                sampleCount,
                true,
                integratedCost + appliedPenalty >= maxRayCost,
                disturbedDistance: disturbedDistance);
        }

        private static bool IsCostSampleDistance(int distance)
        {
            for (int index = 0; index < s_sampleDistances.Length; index++)
                if (s_sampleDistances[index] == distance)
                    return true;
            return false;
        }

        private static AccessSideRayResult Fatal(
            string reason,
            int sampleCount,
            float integratedCost)
            => new AccessSideRayResult(
                integratedCost,
                0f,
                sampleCount,
                false,
                false,
                reason);
    }
}

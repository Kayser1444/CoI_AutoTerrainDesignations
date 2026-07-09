using System;
using Mafi;

namespace AutoTerrainDesignations.Access
{
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

        public AccessSideRayTerrainSample(
            AccessTerrainSampleKind kind,
            float terrainHeight)
        {
            Kind = kind;
            TerrainHeight = terrainHeight;
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
        public float TotalCost => IntegratedCost + UnresolvedPenalty;
        public bool IsFatal => !string.IsNullOrEmpty(FatalReason);

        public AccessSideRayResult(
            float integratedCost,
            float unresolvedPenalty,
            int sampleCount,
            bool isUnresolved,
            bool reachedCostCap,
            string? fatalReason = null)
        {
            IntegratedCost = integratedCost;
            UnresolvedPenalty = unresolvedPenalty;
            SampleCount = sampleCount;
            IsUnresolved = isUnresolved;
            ReachedCostCap = reachedCostCap;
            FatalReason = fatalReason;
        }
    }

    internal static class AccessSideRayCost
    {
        internal const float DefaultMaxRayCost = 512f;
        internal const float DefaultUnresolvedPenalty = 128f;
        private const float MinimumDryCutOceanHeight = 1f;
        private static readonly int[] s_sampleDistances = { 1, 2, 3, 5, 8, 13, 16 };

        public static AccessSideRayResult Score(
            AccessSearchSnapshot snapshot,
            Tile2i corner,
            Tile2i lateralDirection,
            float plannedCornerHeight,
            AccessSideRayOperation operation,
            float materialSlope,
            float maxRayCost = DefaultMaxRayCost,
            float unresolvedPenalty = DefaultUnresolvedPenalty)
            => Score(
                tile =>
                {
                    AccessTerrainSampleKind kind =
                        snapshot.GetSideRayTerrainSample(tile, out float height);
                    return new AccessSideRayTerrainSample(kind, height);
                },
                corner,
                lateralDirection,
                plannedCornerHeight,
                operation,
                materialSlope,
                maxRayCost,
                unresolvedPenalty);

        internal static AccessSideRayResult Score(
            Func<Tile2i, AccessSideRayTerrainSample> sampleTerrain,
            Tile2i corner,
            Tile2i lateralDirection,
            float plannedCornerHeight,
            AccessSideRayOperation operation,
            float materialSlope,
            float maxRayCost = DefaultMaxRayCost,
            float unresolvedPenalty = DefaultUnresolvedPenalty)
        {
            if (operation == AccessSideRayOperation.None)
                return new AccessSideRayResult(0f, 0f, 0, false, false);
            if ((Math.Abs(lateralDirection.X) + Math.Abs(lateralDirection.Y)) != 1)
                return Fatal("SideRayInvalidDirection", 0, 0f);
            if (materialSlope <= 0f || float.IsNaN(materialSlope)
                || float.IsInfinity(materialSlope))
                return Fatal("SideRayInvalidMaterialSlope", 0, 0f);
            if (maxRayCost < 0f || unresolvedPenalty < 0f)
                return Fatal("SideRayInvalidCostLimit", 0, 0f);

            float integratedCost = 0f;
            int previousDistance = 0;
            int sampleCount = 0;
            for (int i = 0; i < s_sampleDistances.Length; i++)
            {
                int distance = s_sampleDistances[i];
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
                        integratedCost, 0f, sampleCount, false, false);
                }
                if (operation == AccessSideRayOperation.Cut
                    && sample.Kind == AccessTerrainSampleKind.Ocean
                    && sample.TerrainHeight < MinimumDryCutOceanHeight)
                    return Fatal("SideRayCutOcean", sampleCount, integratedCost);

                float rayHeight = operation == AccessSideRayOperation.Fill
                    ? plannedCornerHeight - distance * materialSlope
                    : plannedCornerHeight + distance * materialSlope;
                float gap = operation == AccessSideRayOperation.Fill
                    ? rayHeight - sample.TerrainHeight
                    : sample.TerrainHeight - rayHeight;
                if (gap <= 0f)
                    return new AccessSideRayResult(
                        integratedCost, 0f, sampleCount, false, false);

                int stepLength = distance - previousDistance;
                integratedCost = Math.Min(
                    maxRayCost,
                    integratedCost + stepLength * gap);
                if (integratedCost >= maxRayCost)
                    return new AccessSideRayResult(
                        maxRayCost, 0f, sampleCount, true, true);
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
                integratedCost + appliedPenalty >= maxRayCost);
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

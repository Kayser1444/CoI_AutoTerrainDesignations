using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations.Access
{
    internal readonly struct AccessPropPlannedTerrainWork
    {
        internal DesignationData Data { get; }
        internal AccessHandoffOperation Operation { get; }

        internal AccessPropPlannedTerrainWork(
            DesignationData data, AccessHandoffOperation operation)
        {
            Data = data;
            Operation = operation;
        }
    }

    /// <summary>
    /// Commit-time cleanup decision. Never used to score or select a route.
    /// Prop destruction facts are supplied from the live prop, not replay defaults.
    /// </summary>
    internal static class AccessPropTerrainRemoval
    {
        internal const float SafetyMargin = 0.5f;

        internal static bool TryGetRemovalStrategy(
            QuickRemoveDebrisPolicy policy,
            AccessPropSample prop,
            float placementHeightOffset,
            IReadOnlyList<AccessPropPlannedTerrainWork> work,
            Func<Tile2i, AccessProjectedTerrainEffect> projectedAt,
            out bool quickRemove)
        {
            quickRemove = false;
            if (WillBeDestroyed(prop, placementHeightOffset, work, projectedAt))
                return false;
            quickRemove = policy != QuickRemoveDebrisPolicy.Never;
            return true;
        }

        private static bool WillBeDestroyed(
            AccessPropSample prop,
            float placementHeightOffset,
            IReadOnlyList<AccessPropPlannedTerrainWork> work,
            Func<Tile2i, AccessProjectedTerrainEffect> projectedAt)
        {
            if (!prop.HasDumpBurialProbe || !prop.IsDenseDebris
                || !IsFinite(prop.PlacedHeight) || !IsFinite(placementHeightOffset)
                || !IsFinite(prop.DumpBurialThreshold) || prop.DumpBurialThreshold < 0f
                || !IsFinite(prop.DumpBurialProbeOffsetX)
                || !IsFinite(prop.DumpBurialProbeOffsetY)
                || prop.DumpBurialProbeOffsetX < 0f || prop.DumpBurialProbeOffsetX >= 1f
                || prop.DumpBurialProbeOffsetY < 0f || prop.DumpBurialProbeOffsetY >= 1f)
                return false;

            // Vanilla TerrainPropsManager.shouldRemoveProp compares terrain minus
            // PlacedAtHeight against PlacementHeightOffset (cut) and the scaled
            // DespawnBuriedThreshold (fill), both strictly. Keep that distinction.
            float cutLimit = prop.PlacedHeight + placementHeightOffset - SafetyMargin;
            float fillLimit = prop.PlacedHeight + prop.DumpBurialThreshold + SafetyMargin;
            if (!IsFinite(cutLimit) || !IsFinite(fillLimit))
                return false;

            int direct = ClassifyDirectWork(prop, work, cutLimit, fillLimit,
                out bool covered);
            if (covered)
                return direct != 0;

            // Ray work is approximate. Require the same destruction direction at
            // every surrounding terrain sample rather than interpolating across
            // missing, opposing, or safety-only effects. Explicit work at a corner
            // takes precedence over a ray crossing it.
            int direction = 0;
            for (int y = 0; y <= 1; y++)
                for (int x = 0; x <= 1; x++)
                {
                    Tile2i tile = prop.DumpBurialProbeTile + new RelTile2i(x, y);
                    var cornerProbe = new AccessPropSample(tile, false, true, true,
                        dumpBurialProbeTile: tile);
                    int corner = ClassifyDirectWork(cornerProbe, work,
                        cutLimit, fillLimit, out bool cornerCovered);
                    if (!cornerCovered)
                    {
                        AccessProjectedTerrainEffect effect = projectedAt(tile);
                        if (effect.HasAmbiguousWork)
                            return false;
                        corner = effect.HasCutWork && IsFinite(effect.CutCeiling)
                            && effect.CutCeiling < cutLimit ? -1
                            : effect.HasFillWork && IsFinite(effect.FillFloor)
                                && effect.FillFloor > fillLimit ? 1 : 0;
                    }
                    if (corner == 0 || (direction != 0 && direction != corner))
                        return false;
                    direction = corner;
                }
            return direction != 0;
        }

        private static int ClassifyDirectWork(
            AccessPropSample probe,
            IReadOnlyList<AccessPropPlannedTerrainWork> work,
            float cutLimit, float fillLimit, out bool covered)
        {
            covered = false;
            int direction = 0;
            foreach (AccessPropPlannedTerrainWork item in work)
            {
                if (!AccessPropCleanupPolicy.TryGetDesignationTargetHeight(
                        item.Data, probe, out float target))
                    continue;
                covered = true;
                if (!IsFinite(target))
                    return 0;
                int current = (item.Operation == AccessHandoffOperation.Mining
                        || item.Operation == AccessHandoffOperation.Leveling)
                    && target < cutLimit ? -1
                    : (item.Operation == AccessHandoffOperation.Dumping
                            || item.Operation == AccessHandoffOperation.Leveling)
                        && target > fillLimit ? 1 : 0;
                if (current == 0 || (direction != 0 && direction != current))
                    return 0;
                direction = current;
            }
            return direction;
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

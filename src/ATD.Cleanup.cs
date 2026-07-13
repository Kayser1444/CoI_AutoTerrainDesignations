// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Designation Cleanup
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static readonly HashSet<Tile2i> s_lastClearedAccesswayOrigins =
            new HashSet<Tile2i>();

        private static void CaptureClearedAccesswayOrigins(IAreaManagingTower tower)
        {
            s_lastClearedAccesswayOrigins.Clear();
            foreach (Tile2i origin in GetRegisteredGeneratedAccesswayOrigins(tower))
                s_lastClearedAccesswayOrigins.Add(origin);
        }

        private static void ClearDesignationsInArea(IAreaManagingTower tower)
        {
            if (s_desigManager == null) return;

            var originsToRemove = new List<Tile2i>();
            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(
                tower.Area.BoundingBoxMin, tower.Area.BoundingBoxMax))
            {
                if (IsOriginInsideTower(tower, designation.OriginTileCoord)
                    && IsTerrainWorkDesignationProto(designation.Prototype))
                    originsToRemove.Add(designation.OriginTileCoord);
            }

            foreach (Tile2i origin in originsToRemove)
            {
                s_desigManager.RemoveDesignation(origin);
            }
        }

        private static bool IsMiningDesignation(TerrainDesignation designation)
        {
            if (s_miningProto != null && designation.Prototype == s_miningProto)
            {
                return true;
            }

            return designation.Prototype.Id.Value == "MiningDesignator";
        }

        private static bool HasTerrainDesignationAtOrigin(IAreaManagingTower tower, Tile2i origin)
        {
            return IsOriginInsideTower(tower, origin)
                && s_desigManager != null
                && s_desigManager.GetDesignationAt(origin).HasValue;
        }

        internal static void ClearDesignationsForTower(IAreaManagingTower tower)
        {
            CaptureClearedAccesswayOrigins(tower);
            ClearDesignationsInArea(tower);
            ClearRegisteredGeneratedAccessways(tower);
            ClearRegisteredGeneratedDesignations(tower);
            MarkTowerMiningPlanDirty(tower);
        }
        internal static bool HasGeneratedDesignationsForTower(IAreaManagingTower tower)
        {
            if (s_desigManager == null) return false;

            foreach (Tile2i origin in GetRegisteredGeneratedDesignationOrigins(tower))
            {
                if (s_desigManager.GetDesignationAt(origin).HasValue)
                    return true;
            }

            return false;
        }

        internal static void ClearGeneratedDesignationsForTower(IAreaManagingTower tower)
        {
            CaptureClearedAccesswayOrigins(tower);
            if (s_desigManager == null)
            {
                ClearRegisteredGeneratedAccessways(tower);
                ClearRegisteredGeneratedDesignations(tower);
                return;
            }

            IReadOnlyList<Tile2i> registeredOrigins =
                GetRegisteredGeneratedDesignationOrigins(tower);
            var originsToRemove = new List<Tile2i>();
            foreach (Tile2i origin in registeredOrigins)
            {
                if (s_desigManager.GetDesignationAt(origin).HasValue)
                    originsToRemove.Add(origin);
            }

            foreach (Tile2i origin in originsToRemove)
            {
                s_desigManager.RemoveDesignation(origin);
            }

            int remainingLive = 0;
            foreach (Tile2i origin in registeredOrigins)
                if (s_desigManager.GetDesignationAt(origin).HasValue)
                    remainingLive++;
            LogExperimentalAccessDebug(
                $"[ATD Generated Clear Audit] registered={registeredOrigins.Count} " +
                $"liveBefore={originsToRemove.Count} removed={originsToRemove.Count} " +
                $"liveAfter={remainingLive}");

            ClearRegisteredGeneratedAccessways(tower);
            ClearRegisteredGeneratedDesignations(tower);
            MarkTowerMiningPlanDirty(tower);
        }

        private static void RemoveFulfilledDesignationsForTower(
            IAreaManagingTower tower,
            HashSet<Tile2i>? protectedOrigins = null)
        {
            if (s_desigManager == null)
            {
                return;
            }

            var fulfilledOrigins = new List<Tile2i>();
            foreach (TerrainDesignation designation in tower.ManagedDesignations)
            {
                if (IsMiningDesignation(designation)
                    && designation.IsFulfilled
                    && (protectedOrigins == null || !protectedOrigins.Contains(designation.OriginTileCoord)))
                {
                    fulfilledOrigins.Add(designation.OriginTileCoord);
                }
            }

            foreach (Tile2i origin in fulfilledOrigins)
            {
                s_desigManager.RemoveDesignation(origin);
            }
        }

        private static void CleanupIsolatedLeftoverDesignationsForTower(
            IAreaManagingTower tower,
            Dict<Tile2i, int> originalOreOrigins,
            HashSet<Tile2i>? protectedOrigins = null)
        {
            if (s_desigManager == null)
            {
                return;
            }

            var remainingOrigins = new HashSet<Tile2i>();
            foreach (TerrainDesignation designation in tower.ManagedDesignations)
            {
                if (IsMiningDesignation(designation) && !designation.IsFulfilled)
                {
                    remainingOrigins.Add(designation.OriginTileCoord);
                }
            }

            if (remainingOrigins.Count == 0)
            {
                return;
            }

            var originalOriginSet = new HashSet<Tile2i>(originalOreOrigins.Keys);
            var visited = new HashSet<Tile2i>();
            var originsToRemove = new List<Tile2i>();

            foreach (Tile2i origin in remainingOrigins)
            {
                if (visited.Contains(origin))
                {
                    continue;
                }

                var component = new List<Tile2i>();
                FloodFillOrigins(origin, remainingOrigins, visited, component);

                bool touchesOriginalOre = component.Any(originalOriginSet.Contains);
                bool containsProtectedAccessway = protectedOrigins != null && component.Any(protectedOrigins.Contains);
                if (!touchesOriginalOre && !containsProtectedAccessway)
                {
                    originsToRemove.AddRange(component);
                }
            }

            foreach (Tile2i origin in originsToRemove)
            {
                s_desigManager.RemoveDesignation(origin);
            }

            if (originsToRemove.Count > 0)
            {
                LogDebug(string.Format("Removed {0} isolated leftover designation tile(s) after ramp cleanup", originsToRemove.Count));
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    /// <summary>
    /// Primitive facts needed to answer vanilla mining/dumping fulfillment
    /// checks without calling a live terrain-designation manager.
    /// </summary>
    internal sealed class AccessDesignationReadinessFacts
    {
        private readonly Dictionary<Tile2i, float>
            m_maxPropPlacedHeightByTile;
        private readonly Dictionary<Tile2i, float>
            m_maxStumpPlantedHeightByTile;
        private readonly IReadOnlyDictionary<Tile2i,
            AccessCapturedLayoutOccupancy[]> m_layoutOccupanciesByTile;
        private readonly int m_layoutOccupancyCount;

        public int FactCount
            => m_maxPropPlacedHeightByTile.Count
                + m_maxStumpPlantedHeightByTile.Count
                + m_layoutOccupancyCount;

        public AccessDesignationReadinessFacts(
            IReadOnlyDictionary<Tile2i, float>? maxPropPlacedHeightByTile = null,
            IReadOnlyDictionary<Tile2i, float>? maxStumpPlantedHeightByTile = null,
            IReadOnlyDictionary<Tile2i, AccessCapturedLayoutOccupancy[]>?
                layoutOccupanciesByTile = null)
        {
            m_maxPropPlacedHeightByTile = Copy(maxPropPlacedHeightByTile);
            m_maxStumpPlantedHeightByTile = Copy(maxStumpPlantedHeightByTile);
            m_layoutOccupanciesByTile = Copy(layoutOccupanciesByTile);
            m_layoutOccupancyCount = CountLayoutOccupancies(
                m_layoutOccupanciesByTile);
        }

        private AccessDesignationReadinessFacts(
            Dictionary<Tile2i, float> maxPropPlacedHeightByTile,
            Dictionary<Tile2i, float> maxStumpPlantedHeightByTile,
            IReadOnlyDictionary<Tile2i, AccessCapturedLayoutOccupancy[]>
                layoutOccupanciesByTile)
        {
            m_maxPropPlacedHeightByTile = maxPropPlacedHeightByTile;
            m_maxStumpPlantedHeightByTile = maxStumpPlantedHeightByTile;
            m_layoutOccupanciesByTile = layoutOccupanciesByTile;
            m_layoutOccupancyCount = CountLayoutOccupancies(
                layoutOccupanciesByTile);
        }

        public bool IsMiningFulfilled(
            Tile2i tile,
            float terrainHeight,
            float designationHeight,
            bool upperEdge)
        {
            if (terrainHeight > designationHeight)
                return false;
            if (upperEdge)
                return true;
            return !HasHeightAtOrAbove(
                    m_maxPropPlacedHeightByTile, tile, designationHeight)
                && !HasHeightAtOrAbove(
                    m_maxStumpPlantedHeightByTile, tile, designationHeight);
        }

        public bool IsDumpingFulfilled(
            Tile2i tile,
            float terrainHeight,
            float designationHeight)
        {
            if (terrainHeight >= designationHeight)
                return true;
            if (!m_layoutOccupanciesByTile.TryGetValue(
                    tile, out AccessCapturedLayoutOccupancy[] occupancies))
                return false;

            int terrainHeightI = (int)Math.Floor(terrainHeight);
            for (int index = 0; index < occupancies.Length; index++)
            {
                AccessCapturedLayoutOccupancy occupancy = occupancies[index];
                if (occupancy.ContainsHeight(terrainHeightI)
                    && occupancy.EntityHeight >= designationHeight)
                    return true;
            }
            return false;
        }

        private static Dictionary<Tile2i, float> Copy(
            IReadOnlyDictionary<Tile2i, float>? source)
        {
            var copy = new Dictionary<Tile2i, float>();
            if (source != null)
                foreach (KeyValuePair<Tile2i, float> pair in source)
                    copy[pair.Key] = pair.Value;
            return copy;
        }

        private static bool HasHeightAtOrAbove(
            IReadOnlyDictionary<Tile2i, float> heights,
            Tile2i tile,
            float threshold)
            => heights.TryGetValue(tile, out float height)
                && height >= threshold;

        private static Dictionary<Tile2i, AccessCapturedLayoutOccupancy[]>
            Copy(
                IReadOnlyDictionary<Tile2i,
                    AccessCapturedLayoutOccupancy[]>? source)
        {
            var copy = new Dictionary<Tile2i,
                AccessCapturedLayoutOccupancy[]>();
            if (source != null)
            {
                foreach (KeyValuePair<Tile2i,
                    AccessCapturedLayoutOccupancy[]> pair in source)
                {
                    var values = new AccessCapturedLayoutOccupancy[
                        pair.Value.Length];
                    Array.Copy(pair.Value, values, values.Length);
                    copy[pair.Key] = values;
                }
            }
            return copy;
        }

        private static int CountLayoutOccupancies(
            IReadOnlyDictionary<Tile2i, AccessCapturedLayoutOccupancy[]>
                occupanciesByTile)
        {
            int count = 0;
            foreach (AccessCapturedLayoutOccupancy[] occupancies
                in occupanciesByTile.Values)
                count += occupancies.Length;
            return count;
        }

        internal static AccessDesignationReadinessFacts Capture(
            Dictionary<Tile2i, float> propPlacedHeights,
            Dictionary<Tile2i, float> stumpPlantedHeights,
            AccessCapturedBuildingFacts buildingFacts)
        {
            return new AccessDesignationReadinessFacts(
                propPlacedHeights,
                stumpPlantedHeights,
                buildingFacts.LayoutOccupanciesByTile);
        }
    }
}

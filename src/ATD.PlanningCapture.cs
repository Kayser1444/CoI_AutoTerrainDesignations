using System.Collections.Generic;
using AutoTerrainDesignations.Planning;
using Mafi;
using Mafi.Core.Terrain;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static CapturedTerrainColumn CapturePlanningTerrainColumn(
            TerrainManager terrain, Tile2i tile)
        {
            var layers = new List<CapturedTerrainLayer>();
            float surface = terrain.GetHeight(tile).Value.ToFloat();
            float top = surface;
            ThicknessTilesF depth = ThicknessTilesF.Zero;
            TerrainLayerEnumerator enumerator = terrain.EnumerateLayers(
                terrain.GetTileIndex(tile));
            while (enumerator.MoveNext())
            {
                TerrainMaterialThicknessSlim layer = enumerator.Current;
                var material = layer.SlimId.ToFull(terrain);
                float thickness = layer.Thickness.Value.ToFloat();
                float bottom = top - thickness;
                layers.Add(new CapturedTerrainLayer(material.Id.ToString(),
                    material.MinedProduct?.Id.ToString() ?? string.Empty, top, bottom, thickness,
                    depth.Value.ToFloat(), GetCutMaterialSlope(material),
                    s_bedrockTerrainMaterial != null
                        && layer.SlimId == s_bedrockTerrainMaterial.SlimId));
                depth += layer.Thickness;
                top = bottom;
            }
            return new CapturedTerrainColumn(surface, terrain.IsOcean(tile), layers);
        }
    }
}

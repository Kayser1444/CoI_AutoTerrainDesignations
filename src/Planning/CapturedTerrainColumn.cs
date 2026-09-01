using System;
using System.Collections.Generic;
using AutoTerrainDesignations.Access;

namespace AutoTerrainDesignations.Planning
{
    /// <summary>
    /// Primitive full-column facts. Thickness and cumulative resource depth are
    /// captured independently of float elevation endpoints: subtracting the latter
    /// would change the legacy mining arithmetic. No prototype or world reference.
    /// </summary>
    internal readonly struct CapturedTerrainLayer
    {
        public readonly string MaterialId;
        public readonly string ProductId;
        public readonly float Top;
        public readonly float Bottom;
        public readonly float Thickness;
        public readonly float ResourceDepth;
        public readonly float Slope;
        public readonly bool IsBedrock;

        public CapturedTerrainLayer(string materialId, string productId,
            float top, float bottom, float thickness, float resourceDepth,
            float slope, bool isBedrock)
        {
            MaterialId = materialId;
            ProductId = productId;
            Top = top;
            Bottom = bottom;
            Thickness = thickness;
            ResourceDepth = resourceDepth;
            Slope = slope;
            IsBedrock = isBedrock;
        }
    }

    internal sealed class CapturedTerrainColumn
    {
        public readonly float SurfaceHeight;
        public readonly bool IsOcean;
        private readonly CapturedTerrainLayer[] m_layers;
        public int LayerCount => m_layers.Length;
        public CapturedTerrainLayer LayerAt(int index) => m_layers[index];

        public CapturedTerrainColumn(float surfaceHeight, bool isOcean,
            IEnumerable<CapturedTerrainLayer> layers)
        {
            SurfaceHeight = surfaceHeight;
            IsOcean = isOcean;
            m_layers = new List<CapturedTerrainLayer>(layers).ToArray();
        }

        // Preserve the existing access replay representation and its numeric
        // endpoints while both live consumers use the same primitive collector.
        public AccessTerrainColumn ToAccessColumn()
        {
            var layers = new AccessTerrainLayer[m_layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                CapturedTerrainLayer layer = m_layers[i];
                layers[i] = new AccessTerrainLayer(layer.Top, layer.Bottom,
                    layer.Slope, layer.MaterialId);
            }
            return new AccessTerrainColumn(layers);
        }

        public bool TryGetSlope(float elevation, out float slope)
        {
            for (int i = 0; i < m_layers.Length; i++)
            {
                CapturedTerrainLayer layer = m_layers[i];
                if (elevation > layer.Top + 0.0001f
                    || (elevation <= layer.Bottom + 0.0001f
                        && i < m_layers.Length - 1)) continue;
                slope = layer.Slope;
                return true;
            }
            slope = 0f;
            return false;
        }
    }
}

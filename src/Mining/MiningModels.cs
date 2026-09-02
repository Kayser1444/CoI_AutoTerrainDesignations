using System;
using System.Collections.Generic;
using AutoTerrainDesignations.Planning;
using Mafi;
using Mafi.Collections;

namespace AutoTerrainDesignations.Mining
{
    internal readonly struct MiningOreInterval
    {
        public readonly string ProductId;
        public readonly float Height;
        public readonly float Depth;
        public MiningOreInterval(string productId, float height, float depth)
        { ProductId = productId; Height = height; Depth = depth; }
    }

    internal sealed class MiningPolicy
    {
        public readonly int PurityLevel, MaxHeightDiff, MaxLayers, CorridorClearance;
        public readonly int? MinElevation;
        public readonly float MinOreHeight, MinPurity, MinBottomDensity;
        public readonly int MinComponentSize, FlatteningStrength;
        public readonly bool FlattenBottom, FilterOreSpikes, AvoidBuildings, AvoidOcean;
        public readonly int RayBuffer, BuildingBuffer;
        public readonly float DumpingSlope, FallbackMiningSlope;
        public MiningPolicy(int purityLevel, int maxHeightDiff, int maxLayers,
            int? minElevation, int corridorClearance, float minOreHeight,
            float minPurity, float minBottomDensity, int minComponentSize,
            bool flattenBottom, int flatteningStrength, bool filterOreSpikes, bool avoidBuildings,
            bool avoidOcean, int rayBuffer, int buildingBuffer,
            float dumpingSlope, float fallbackMiningSlope)
        {
            PurityLevel = purityLevel; MaxHeightDiff = maxHeightDiff;
            MaxLayers = maxLayers; MinElevation = minElevation;
            CorridorClearance = corridorClearance; MinOreHeight = minOreHeight;
            MinPurity = minPurity; MinBottomDensity = minBottomDensity;
            MinComponentSize = minComponentSize; FlattenBottom = flattenBottom;
            FlatteningStrength = flatteningStrength; FilterOreSpikes = filterOreSpikes;
            AvoidBuildings = avoidBuildings;
            AvoidOcean = avoidOcean; RayBuffer = rayBuffer; BuildingBuffer = buildingBuffer;
            DumpingSlope = dumpingSlope; FallbackMiningSlope = fallbackMiningSlope;
        }
    }

    /// <summary>Captured values. Seal the builder view before publishing to a worker or recorder.</summary>
    internal sealed class MiningRequest
    {
        public readonly Tile2i[] Origins;
        public readonly string[] ProductIds;
        public readonly MiningPolicy Policy;
        public readonly Tile2i TerrainSize;
        private readonly Dictionary<Tile2i, CapturedTerrainColumn> m_columns;
        private readonly HashSet<Tile2i> m_buildings;
        public MiningRequest(Tile2i[] origins, string[] productIds, MiningPolicy policy,
            Tile2i terrainSize, Dictionary<Tile2i, CapturedTerrainColumn> columns,
            HashSet<Tile2i> buildings)
        {
            Origins = origins; ProductIds = productIds; Policy = policy;
            TerrainSize = terrainSize; m_columns = columns; m_buildings = buildings;
        }
        public bool IsValid(Tile2i tile) => tile.X >= 0 && tile.Y >= 0
            && tile.X < TerrainSize.X && tile.Y < TerrainSize.Y;
        public CapturedTerrainColumn Column(Tile2i tile)
            => m_columns.TryGetValue(tile, out CapturedTerrainColumn column)
                ? column : throw new InvalidOperationException("Missing mining terrain fact: " + tile);
        public bool TryGetColumn(Tile2i tile, out CapturedTerrainColumn column)
            => m_columns.TryGetValue(tile, out column);
        public bool BuildingAt(Tile2i tile) => m_buildings.Contains(tile);
        public MiningRequest Seal() => new MiningRequest((Tile2i[])Origins.Clone(),
            (string[])ProductIds.Clone(), Policy, TerrainSize,
            new Dictionary<Tile2i, CapturedTerrainColumn>(m_columns),
            new HashSet<Tile2i>(m_buildings));
    }

    internal enum MiningStage { Body, DirectSafety, Complete, SafetyCoverage }

    internal sealed class MiningPlan
    {
        internal const string SafetyCoverageRequired = "SafetyCoverageRequired";
        public readonly Dict<Tile2i, int> Depths;
        public readonly Dict<Tile2i, int> Corners;
        public readonly bool ReconcileEmpty;
        public readonly string Outcome;
        public bool NeedsSafetyCoverage => Outcome == SafetyCoverageRequired;
        public MiningPlan(Dict<Tile2i, int> depths, Dict<Tile2i, int> corners,
            string outcome, bool reconcileEmpty = false)
        { Depths = depths; Corners = corners; Outcome = outcome; ReconcileEmpty = reconcileEmpty; }
    }
}

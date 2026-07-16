using System;
using System.Collections.Generic;
using Mafi;
using AutoTerrainDesignations.Access.V2;

namespace AutoTerrainDesignations.Access
{
    internal enum AccessReachedGoalKind
    {
        None,
        TowerGround,
        FixedNetwork
    }

    internal enum AccessSearchMode
    {
        Ground,
        Flat,
        XPositive,
        XNegative,
        YPositive,
        YNegative,
        Existing
    }

    internal enum AccessHandoffOperation
    {
        None,
        Leveling,
        Mining,
        Dumping
    }

    internal readonly struct AccessGroundHandoff
    {
        public Tile2i Tile { get; }
        public AccessHandoffOperation Operation { get; }
        public IReadOnlyList<Tile2i> EscapeTiles { get; }
        public int SpanLength { get; }

        public AccessGroundHandoff(
            Tile2i tile,
            AccessHandoffOperation operation,
            IReadOnlyList<Tile2i>? escapeTiles = null,
            int spanLength = 1)
        {
            Tile = tile;
            Operation = operation;
            EscapeTiles = escapeTiles ?? Array.Empty<Tile2i>();
            SpanLength = Math.Max(1, spanLength);
        }
    }

    internal readonly struct AccessHandoffSpanCell
    {
        public Tile2i Origin { get; }
        public AccessHeightProfile Profile { get; }
        public Tile2i EntryDirection { get; }

        public AccessHandoffSpanCell(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i entryDirection)
        {
            Origin = origin;
            Profile = profile;
            EntryDirection = entryDirection;
        }
    }

    internal readonly struct AccessSearchNode : IEquatable<AccessSearchNode>
    {
        public Tile2i Position { get; }
        public int Height2 { get; }
        public AccessSearchMode Mode { get; }
        public AccessHandoffOperation HandoffOperation { get; }
        public int HandoffSpanLength { get; }
        public Tile2i EntryDirection { get; }

        public AccessSearchNode(Tile2i position, int height2, AccessSearchMode mode,
            AccessHandoffOperation handoffOperation = AccessHandoffOperation.None,
            Tile2i entryDirection = default,
            int handoffSpanLength = 0)
        {
            Position = position;
            Height2 = height2;
            Mode = mode;
            HandoffOperation = handoffOperation;
            EntryDirection = entryDirection;
            HandoffSpanLength = Math.Max(0, handoffSpanLength);
        }

        public bool IsGround => Mode == AccessSearchMode.Ground;
        public Tile2i CostPosition => IsGround ? Position : Position + new RelTile2i(2, 2);

        public bool Equals(AccessSearchNode other)
            => Position == other.Position && Height2 == other.Height2 && Mode == other.Mode
                && HandoffOperation == other.HandoffOperation
                && HandoffSpanLength == other.HandoffSpanLength
                && EntryDirection == other.EntryDirection;

        public override bool Equals(object? obj) => obj is AccessSearchNode other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Position.GetHashCode();
                hash = (hash * 397) ^ Height2;
                hash = (hash * 397) ^ (int)Mode;
                hash = (hash * 397) ^ (int)HandoffOperation;
                hash = (hash * 397) ^ HandoffSpanLength;
                hash = (hash * 397) ^ EntryDirection.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
            => $"{Mode}@{Position}/h2={Height2}/handoff={HandoffOperation}/span={HandoffSpanLength}/entry={EntryDirection}";
    }

    internal readonly struct AccessHeightProfile
    {
        public int Nw2 { get; }
        public int Ne2 { get; }
        public int Se2 { get; }
        public int Sw2 { get; }

        public AccessHeightProfile(int nw2, int ne2, int se2, int sw2)
        {
            Nw2 = nw2;
            Ne2 = ne2;
            Se2 = se2;
            Sw2 = sw2;
        }

        public int Center2 => (Nw2 + Ne2 + Se2 + Sw2) / 4;
        public bool HasIntegerCorners => ((Nw2 | Ne2 | Se2 | Sw2) & 1) == 0;

        public int GetHeight2NumeratorAt(int x, int y)
        {
            // Bilinear target height over a 4x4 designation. The denominator is 16;
            // retaining the numerator avoids rounding away operation incompatibilities.
            return Nw2 * (4 - x) * (4 - y)
                + Ne2 * x * (4 - y)
                + Sw2 * (4 - x) * y
                + Se2 * x * y;
        }

        public static bool TryForMode(AccessSearchMode mode, int center2, out AccessHeightProfile profile)
        {
            switch (mode)
            {
                case AccessSearchMode.Flat when (center2 & 1) == 0:
                    profile = new AccessHeightProfile(center2, center2, center2, center2);
                    return true;
                case AccessSearchMode.XPositive when (center2 & 1) != 0:
                    profile = new AccessHeightProfile(center2 - 1, center2 + 1, center2 + 1, center2 - 1);
                    return true;
                case AccessSearchMode.XNegative when (center2 & 1) != 0:
                    profile = new AccessHeightProfile(center2 + 1, center2 - 1, center2 - 1, center2 + 1);
                    return true;
                case AccessSearchMode.YPositive when (center2 & 1) != 0:
                    profile = new AccessHeightProfile(center2 - 1, center2 - 1, center2 + 1, center2 + 1);
                    return true;
                case AccessSearchMode.YNegative when (center2 & 1) != 0:
                    profile = new AccessHeightProfile(center2 + 1, center2 + 1, center2 - 1, center2 - 1);
                    return true;
                default:
                    profile = default;
                    return false;
            }
        }

        public void GetEdge(Tile2i direction, out int first2, out int second2)
        {
            if (direction.X > 0) { first2 = Ne2; second2 = Se2; return; }
            if (direction.X < 0) { first2 = Nw2; second2 = Sw2; return; }
            if (direction.Y > 0) { first2 = Sw2; second2 = Se2; return; }
            first2 = Nw2; second2 = Ne2;
        }

        public void AddWorldCorners(Tile2i origin, Action<Tile2i, int> add)
        {
            add(origin, Nw2);
            add(origin + new RelTile2i(4, 0), Ne2);
            add(origin + new RelTile2i(4, 4), Se2);
            add(origin + new RelTile2i(0, 4), Sw2);
        }
    }

    internal readonly struct AccessDurabilityCorner
    {
        public Tile2i Position { get; }
        public int Height2 { get; }
        public float HorizontalRunPerHeight { get; }

        public AccessDurabilityCorner(
            Tile2i position, int height2, float horizontalRunPerHeight = 0f)
        {
            Position = position;
            Height2 = height2;
            HorizontalRunPerHeight = horizontalRunPerHeight;
        }

        public float GetHorizontalRunPerHeight(float fallback)
            => HorizontalRunPerHeight > 0f ? HorizontalRunPerHeight : fallback;

        public bool Blocks(Tile2i position, int height2, float horizontalRunPerHeight)
        {
            int delta2 = Math.Abs(height2 - Height2);
            horizontalRunPerHeight = GetHorizontalRunPerHeight(horizontalRunPerHeight);
            return delta2 > 0
                && Math.Abs(position.X - Position.X) * 2 < delta2 * horizontalRunPerHeight
                && Math.Abs(position.Y - Position.Y) * 2 < delta2 * horizontalRunPerHeight;
        }

        public bool BlocksVehicleFootprint(
            Tile2i center,
            int height2,
            float horizontalRunPerHeight,
            int clearanceRadius)
        {
            int delta2 = Math.Abs(height2 - Height2);
            horizontalRunPerHeight = GetHorizontalRunPerHeight(horizontalRunPerHeight);
            int nearestDx = Math.Max(0, Math.Abs(center.X - Position.X) - clearanceRadius);
            int nearestDy = Math.Max(0, Math.Abs(center.Y - Position.Y) - clearanceRadius);
            return delta2 > 0
                && nearestDx * 2 < delta2 * horizontalRunPerHeight
                && nearestDy * 2 < delta2 * horizontalRunPerHeight;
        }
    }

    internal enum AccessTerrainSampleKind
    {
        Terrain,
        Ocean,
        PhysicalMapEdge,
        MissingSnapshot,
    }

    internal readonly struct AccessTerrainLayer
    {
        public float TopHeight { get; }
        public float BottomHeight { get; }
        public float NormalSlope { get; }
        public string MaterialId { get; }

        public AccessTerrainLayer(
            float topHeight,
            float bottomHeight,
            float normalSlope,
            string materialId)
        {
            TopHeight = topHeight;
            BottomHeight = bottomHeight;
            NormalSlope = normalSlope;
            MaterialId = materialId;
        }
    }

    internal sealed class AccessTerrainColumn
    {
        private readonly AccessTerrainLayer[] m_layers;

        public AccessTerrainColumn(IEnumerable<AccessTerrainLayer> layers)
        {
            m_layers = new List<AccessTerrainLayer>(layers).ToArray();
        }

        public bool TryGetNormalSlopeAt(
            float elevation,
            out float slope,
            out string materialId)
        {
            const float epsilon = 0.0001f;
            for (int i = 0; i < m_layers.Length; i++)
            {
                AccessTerrainLayer layer = m_layers[i];
                if (elevation > layer.TopHeight + epsilon
                    || (elevation <= layer.BottomHeight + epsilon
                        && i < m_layers.Length - 1))
                    continue;
                slope = layer.NormalSlope;
                materialId = layer.MaterialId;
                return true;
            }

            slope = 0f;
            materialId = string.Empty;
            return false;
        }
    }

    internal sealed class AccessSearchSnapshot
    {
        private readonly Dictionary<AccessSideRayCacheKey, AccessSideRayResult> m_sideRayCache =
            new Dictionary<AccessSideRayCacheKey, AccessSideRayResult>();
        private readonly Dictionary<Tile2i, int> m_groundHeight2;
        private readonly Dictionary<Tile2i, float> m_preciseTerrainHeights;
        private readonly Dictionary<Tile2i, AccessTerrainColumn> m_terrainColumns;
        private readonly Dictionary<Tile2i, int> m_terrainCenterHeight2;
        private readonly Dictionary<Tile2i, AccessHeightProfile> m_fixedProfiles;
        private readonly HashSet<Tile2i> m_workOrigins;
        private readonly HashSet<Tile2i> m_groundNodes;
        private readonly HashSet<Tile2i> m_goalGroundNodes;
        private readonly HashSet<Tile2i> m_occupiedTiles;
        private readonly HashSet<Tile2i> m_expandedBuildingRayBlockers;
        private readonly HashSet<Tile2i> m_cutDesignationRayBlockers;
        private readonly HashSet<Tile2i> m_fillDesignationRayBlockers;
        private readonly HashSet<Tile2i> m_projectedCutRayBlockers;
        private readonly HashSet<Tile2i> m_projectedFillRayBlockers;
        private readonly Dictionary<Tile2i, HashSet<Tile2i>> m_projectedCutSourcesByTile;
        private readonly Dictionary<Tile2i, HashSet<Tile2i>> m_projectedFillSourcesByTile;
        private readonly Dictionary<Tile2i, float> m_projectedCutSupportCeilings;
        private readonly Dictionary<Tile2i, float> m_projectedFillSurfaceFloors;
        private readonly HashSet<Tile2i> m_hardDesignationRayBlockers;
        private readonly HashSet<Tile2i> m_oceanTiles;
        private readonly Dictionary<Tile2i, AccessPropCleanupInfo> m_propCleanupByOrigin;
        private readonly Dictionary<Tile2i, AccessPropCleanupInfo> m_propCleanupByTile;
        private readonly Dictionary<Tile2i, string> m_groundExclusionReasons;
        private readonly HashSet<Tile2i> m_validOrigins;
        private readonly float[] m_anyGoalDistance;
        private readonly int m_minGoalHeight2;
        private readonly int m_maxGoalHeight2;
        private readonly int m_goalDistanceWidth;
        private readonly int m_goalDistanceHeight;
        private readonly Tile2i m_goalDistanceMin;
        private readonly Tile2i m_goalDistanceMax;
        private readonly AccessDurabilityCorner[] m_durabilityCorners;
        private readonly List<AccessDurabilityCorner>?[,] m_spatialGrid;
        private readonly int m_gridWidth;
        private readonly int m_gridHeight;
        private const int SPATIAL_CELL_SIZE = 16;
        private readonly Func<Tile2i, AccessHeightProfile, Tile2i, AccessHeightProfile,
            IReadOnlyList<AccessGroundHandoff>>? m_workableHandoffs;
        private readonly Func<IReadOnlyList<AccessHandoffSpanCell>,
            IReadOnlyList<AccessGroundHandoff>>? m_workableHandoffSpans;
        private readonly Func<Tile2i, AccessHeightProfile, Tile2i, AccessHeightProfile,
            IReadOnlyList<AccessGroundHandoff>>? m_v2WorkableHandoffs;
        private readonly Func<IReadOnlyList<AccessHandoffSpanCell>,
            IReadOnlyList<AccessGroundHandoff>>? m_v2WorkableHandoffSpans;
        private V2.AccessV2History? m_projectedV2CachedHistory;
        private IReadOnlyDictionary<Tile2i, AccessHeightProfile>
            m_projectedV2CachedProfiles =
                new Dictionary<Tile2i, AccessHeightProfile>();

        public Tile2i BoundsMin { get; }
        public Tile2i BoundsMax { get; }
        public Tile2i TowerCenter { get; }
        public int MinHeight2 { get; }
        public int MaxHeight2 { get; }
        public bool IsMining { get; }
        public bool AllowsMixedWork { get; }
        public bool UseAStar { get; }
        public bool AvoidOcean { get; }
        public bool AvoidBuildings { get; }
        public float LandscapingCostDistanceScale { get; }
        public float LandslideRunPerHeight { get; }
        public int VehicleClearanceRadius { get; }
        public int VehicleWidth { get; }
        public float VehicleMaxSteepnessDelta { get; }
        public Tile2i PhysicalTerrainMin { get; }
        public Tile2i PhysicalTerrainMax { get; }
        public float DumpingMaterialSlope { get; }
        public float FallbackMiningSlope { get; }
        public bool DumpingSlopeUsedFallback { get; }
        public bool HasDumpingMaterial { get; }
        public int GoalCount => m_goalGroundNodes.Count;
        public int EligibleCleanupOriginCount { get; }
        public V2.AccessV2GroundGraph? V2GroundGraph { get; }
        /// <summary>
        /// Diagnostic useful-height hull built from this immutable snapshot when
        /// the experimental console flag is enabled. It is not yet used to prune
        /// search states.
        /// </summary>
        public AccessUsefulHeightEnvelope? UsefulHeightEnvelope { get; }
        public IEnumerable<Tile2i> GoalGroundNodes => m_goalGroundNodes;
        public int LandslideSourceCount => m_durabilityCorners.Length;
        public IEnumerable<AccessPropCleanupInfo> PropCleanupOrigins => m_propCleanupByOrigin.Values;

        public AccessSearchSnapshot(
            Tile2i boundsMin,
            Tile2i boundsMax,
            Tile2i towerCenter,
            int minHeight2,
            int maxHeight2,
            bool isMining,
            bool allowsMixedWork,
            bool useAStar,
            float landscapingCostDistanceScale,
            float landslideRunPerHeight,
            IDictionary<Tile2i, int> groundHeight2,
            IDictionary<Tile2i, int> terrainCenterHeight2,
            IDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            IEnumerable<Tile2i> workOrigins,
            IEnumerable<Tile2i> groundNodes,
            IEnumerable<Tile2i> goalGroundNodes,
            IEnumerable<Tile2i> occupiedTiles,
            IEnumerable<Tile2i> oceanTiles,
            IEnumerable<AccessDurabilityCorner> durabilityCorners,
            Func<Tile2i, AccessHeightProfile, Tile2i, AccessHeightProfile,
                IReadOnlyList<AccessGroundHandoff>>? workableHandoffs = null,
            IDictionary<Tile2i, AccessPropCleanupInfo>? propCleanupByOrigin = null,
            IDictionary<Tile2i, float>? preciseTerrainHeights = null,
            IDictionary<Tile2i, AccessTerrainColumn>? terrainColumns = null,
            Tile2i? physicalTerrainMin = null,
            Tile2i? physicalTerrainMax = null,
            float dumpingMaterialSlope = 1f,
            float fallbackMiningSlope = 1f,
            bool dumpingSlopeUsedFallback = false,
            bool hasDumpingMaterial = true,
            IDictionary<Tile2i, string>? groundExclusionReasons = null,
            IEnumerable<Tile2i>? rayMiningDesignationOrigins = null,
            IEnumerable<Tile2i>? rayDumpingDesignationOrigins = null,
            IEnumerable<Tile2i>? rayLevelingDesignationOrigins = null,
            IEnumerable<Tile2i>? projectedCutDisturbedTiles = null,
            IEnumerable<Tile2i>? projectedFillDisturbedTiles = null,
            IDictionary<Tile2i, float>? projectedCutSupportCeilings = null,
            IDictionary<Tile2i, float>? projectedFillSurfaceFloors = null,
            IDictionary<Tile2i, HashSet<Tile2i>>? projectedCutSourcesByTile = null,
            IDictionary<Tile2i, HashSet<Tile2i>>? projectedFillSourcesByTile = null,
            int vehicleClearanceRadius = 1,
            bool avoidOcean = true,
            bool avoidBuildings = true,
            int vehicleWidth = 1,
            Func<IReadOnlyList<AccessHandoffSpanCell>,
                IReadOnlyList<AccessGroundHandoff>>? workableHandoffSpans = null,
            IDictionary<Tile2i, AccessPropCleanupInfo>? propCleanupByTile = null,
            Func<Tile2i, AccessHeightProfile, Tile2i, AccessHeightProfile,
                IReadOnlyList<AccessGroundHandoff>>? v2WorkableHandoffs = null,
            Func<IReadOnlyList<AccessHandoffSpanCell>,
                IReadOnlyList<AccessGroundHandoff>>? v2WorkableHandoffSpans = null,
            float vehicleMaxSteepnessDelta = 0.5f,
            AccessUsefulHeightEnvelope? usefulHeightEnvelope = null)
        {
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            TowerCenter = towerCenter;
            MinHeight2 = minHeight2;
            MaxHeight2 = maxHeight2;
            IsMining = isMining;
            AllowsMixedWork = allowsMixedWork;
            UseAStar = useAStar;
            AvoidOcean = avoidOcean;
            AvoidBuildings = avoidBuildings;
            LandscapingCostDistanceScale = landscapingCostDistanceScale;
            LandslideRunPerHeight = landslideRunPerHeight;
            VehicleClearanceRadius = Math.Max(0, vehicleClearanceRadius);
            VehicleWidth = Math.Max(1, vehicleWidth);
            VehicleMaxSteepnessDelta = Math.Max(0f, vehicleMaxSteepnessDelta);
            UsefulHeightEnvelope = usefulHeightEnvelope;
            m_groundHeight2 = new Dictionary<Tile2i, int>(groundHeight2);
            m_preciseTerrainHeights = preciseTerrainHeights != null
                ? new Dictionary<Tile2i, float>(preciseTerrainHeights)
                : BuildPreciseTerrainHeights(groundHeight2);
            m_terrainColumns = terrainColumns != null
                ? new Dictionary<Tile2i, AccessTerrainColumn>(terrainColumns)
                : new Dictionary<Tile2i, AccessTerrainColumn>();
            PhysicalTerrainMin = physicalTerrainMin ?? boundsMin;
            PhysicalTerrainMax = physicalTerrainMax ?? boundsMax;
            DumpingMaterialSlope = dumpingMaterialSlope;
            FallbackMiningSlope = fallbackMiningSlope;
            DumpingSlopeUsedFallback = dumpingSlopeUsedFallback;
            HasDumpingMaterial = hasDumpingMaterial;
            m_terrainCenterHeight2 = new Dictionary<Tile2i, int>(terrainCenterHeight2);
            m_fixedProfiles = new Dictionary<Tile2i, AccessHeightProfile>(fixedProfiles);
            m_workOrigins = new HashSet<Tile2i>(workOrigins);
            m_groundNodes = new HashSet<Tile2i>(groundNodes);
            m_goalGroundNodes = new HashSet<Tile2i>(goalGroundNodes);
            m_occupiedTiles = new HashSet<Tile2i>(occupiedTiles);
            m_expandedBuildingRayBlockers = BuildExpandedBuildingRayBlockers(
                occupiedTiles, boundsMin, boundsMax);
            m_cutDesignationRayBlockers = BuildDesignationRayBlockers(
                rayMiningDesignationOrigins ?? Array.Empty<Tile2i>());
            m_fillDesignationRayBlockers = BuildDesignationRayBlockers(
                rayDumpingDesignationOrigins ?? Array.Empty<Tile2i>());
            m_hardDesignationRayBlockers = BuildDesignationRayBlockers(
                rayLevelingDesignationOrigins ?? fixedProfiles.Keys);
            m_projectedCutSupportCeilings = projectedCutSupportCeilings != null
                ? new Dictionary<Tile2i, float>(projectedCutSupportCeilings)
                : new Dictionary<Tile2i, float>();
            m_projectedFillSurfaceFloors = projectedFillSurfaceFloors != null
                ? new Dictionary<Tile2i, float>(projectedFillSurfaceFloors)
                : new Dictionary<Tile2i, float>();
            m_projectedCutRayBlockers = projectedCutDisturbedTiles != null
                ? new HashSet<Tile2i>(projectedCutDisturbedTiles)
                : new HashSet<Tile2i>();
            m_projectedFillRayBlockers = projectedFillDisturbedTiles != null
                ? new HashSet<Tile2i>(projectedFillDisturbedTiles)
                : new HashSet<Tile2i>();
            m_projectedCutSourcesByTile = CopySourceMap(projectedCutSourcesByTile);
            m_projectedFillSourcesByTile = CopySourceMap(projectedFillSourcesByTile);
            m_oceanTiles = new HashSet<Tile2i>(oceanTiles);
            m_propCleanupByOrigin = propCleanupByOrigin != null
                ? new Dictionary<Tile2i, AccessPropCleanupInfo>(propCleanupByOrigin)
                : new Dictionary<Tile2i, AccessPropCleanupInfo>();
            m_groundExclusionReasons = groundExclusionReasons != null
                ? new Dictionary<Tile2i, string>(groundExclusionReasons)
                : new Dictionary<Tile2i, string>();
            m_propCleanupByTile = propCleanupByTile != null
                ? new Dictionary<Tile2i, AccessPropCleanupInfo>(propCleanupByTile)
                : BuildCleanupByTile(m_propCleanupByOrigin);
            V2GroundGraph = VehicleWidth > 4
                ? new V2.AccessV2GroundGraph(
                    m_groundNodes, m_goalGroundNodes, m_propCleanupByTile)
                : null;
            int eligibleCleanupOriginCount = 0;
            foreach (AccessPropCleanupInfo info in m_propCleanupByOrigin.Values)
                if (info.IsEligible)
                    eligibleCleanupOriginCount++;
            EligibleCleanupOriginCount = eligibleCleanupOriginCount;
            m_validOrigins = new HashSet<Tile2i>(m_terrainCenterHeight2.Keys);
            Tile2i goalDistanceMin = boundsMin;
            Tile2i goalDistanceMax = boundsMax;
            foreach (Tile2i tile in m_groundNodes)
                ExpandGoalDistanceBounds(tile);
            foreach (KeyValuePair<Tile2i, AccessPropCleanupInfo> pair
                in m_propCleanupByTile)
                if (pair.Value.IsEligible)
                    ExpandGoalDistanceBounds(pair.Key);
            m_goalDistanceMin = goalDistanceMin;
            m_goalDistanceMax = goalDistanceMax;
            m_goalDistanceWidth =
                m_goalDistanceMax.X - m_goalDistanceMin.X + 1;
            m_goalDistanceHeight =
                m_goalDistanceMax.Y - m_goalDistanceMin.Y + 1;
            m_anyGoalDistance = useAStar
                ? BuildGoalDistance(
                    m_goalDistanceMin, m_goalDistanceMax,
                    m_goalGroundNodes)
                : Array.Empty<float>();

            void ExpandGoalDistanceBounds(Tile2i tile)
            {
                goalDistanceMin = new Tile2i(
                    Math.Min(goalDistanceMin.X, tile.X),
                    Math.Min(goalDistanceMin.Y, tile.Y));
                goalDistanceMax = new Tile2i(
                    Math.Max(goalDistanceMax.X, tile.X),
                    Math.Max(goalDistanceMax.Y, tile.Y));
            }

            int minGoalHeight2 = int.MaxValue;
            int maxGoalHeight2 = int.MinValue;
            foreach (Tile2i goal in m_goalGroundNodes)
            {
                if (m_groundHeight2.TryGetValue(goal, out int height2))
                {
                    if (height2 < minGoalHeight2) minGoalHeight2 = height2;
                    if (height2 > maxGoalHeight2) maxGoalHeight2 = height2;
                }
            }
            m_minGoalHeight2 = minGoalHeight2 == int.MaxValue ? 0 : minGoalHeight2;
            m_maxGoalHeight2 = maxGoalHeight2 == int.MinValue ? 0 : maxGoalHeight2;
            m_durabilityCorners = new List<AccessDurabilityCorner>(durabilityCorners).ToArray();

            int widthTiles = boundsMax.X - boundsMin.X + 1;
            int heightTiles = boundsMax.Y - boundsMin.Y + 1;
            m_gridWidth = (widthTiles + SPATIAL_CELL_SIZE - 1) / SPATIAL_CELL_SIZE;
            m_gridHeight = (heightTiles + SPATIAL_CELL_SIZE - 1) / SPATIAL_CELL_SIZE;
            m_spatialGrid = new List<AccessDurabilityCorner>[m_gridWidth, m_gridHeight];

            foreach (AccessDurabilityCorner corner in m_durabilityCorners)
            {
                int maxDelta2 = Math.Max(Math.Abs(MinHeight2 - corner.Height2), Math.Abs(MaxHeight2 - corner.Height2));
                int maxDistance = (int)Math.Ceiling(
                    maxDelta2 * corner.GetHorizontalRunPerHeight(LandslideRunPerHeight) / 2.0);

                int minX = Math.Max(boundsMin.X, corner.Position.X - maxDistance);
                int maxX = Math.Min(boundsMax.X, corner.Position.X + maxDistance);
                int minY = Math.Max(boundsMin.Y, corner.Position.Y - maxDistance);
                int maxY = Math.Min(boundsMax.Y, corner.Position.Y + maxDistance);

                int minCx = Math.Max(0, (minX - boundsMin.X) / SPATIAL_CELL_SIZE);
                int maxCx = Math.Min(m_gridWidth - 1, (maxX - boundsMin.X) / SPATIAL_CELL_SIZE);
                int minCy = Math.Max(0, (minY - boundsMin.Y) / SPATIAL_CELL_SIZE);
                int maxCy = Math.Min(m_gridHeight - 1, (maxY - boundsMin.Y) / SPATIAL_CELL_SIZE);

                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    for (int cy = minCy; cy <= maxCy; cy++)
                    {
                        if (m_spatialGrid[cx, cy] == null)
                        {
                            m_spatialGrid[cx, cy] = new List<AccessDurabilityCorner>();
                        }
                        m_spatialGrid[cx, cy]!.Add(corner);
                    }
                }
            }

            m_workableHandoffs = workableHandoffs;
            m_workableHandoffSpans = workableHandoffSpans;
            m_v2WorkableHandoffs = v2WorkableHandoffs;
            m_v2WorkableHandoffSpans = v2WorkableHandoffSpans;
        }

        private static Dictionary<Tile2i, float> BuildPreciseTerrainHeights(
            IDictionary<Tile2i, int> groundHeight2)
        {
            var result = new Dictionary<Tile2i, float>(groundHeight2.Count);
            foreach (KeyValuePair<Tile2i, int> pair in groundHeight2)
                result[pair.Key] = pair.Value / 2f;
            return result;
        }

        public bool IsOriginInside(Tile2i origin) => m_validOrigins.Contains(origin);

        public bool IsTileInside(Tile2i tile)
            => tile.X >= BoundsMin.X && tile.Y >= BoundsMin.Y
                && tile.X <= BoundsMax.X && tile.Y <= BoundsMax.Y;

        public bool IsWorkOrigin(Tile2i origin) => m_workOrigins.Contains(origin);
        public IReadOnlyDictionary<Tile2i, AccessHeightProfile> FixedProfiles
            => m_fixedProfiles;
        public bool TryGetFixedProfile(Tile2i origin, out AccessHeightProfile profile) => m_fixedProfiles.TryGetValue(origin, out profile);
        public bool IsGroundNode(Tile2i tile) => m_groundNodes.Contains(tile);
        public bool IsCleanupGroundNode(Tile2i tile)
            => !m_groundNodes.Contains(tile)
                && m_propCleanupByTile.TryGetValue(tile, out AccessPropCleanupInfo info)
                && info.IsEligible;
        public bool IsGroundOrCleanupNode(Tile2i tile) => IsGroundNode(tile) || IsCleanupGroundNode(tile);
        public bool TryGetPropCleanupInfo(Tile2i origin, out AccessPropCleanupInfo info)
            => m_propCleanupByOrigin.TryGetValue(origin, out info);
        public bool IsCleanupOrigin(Tile2i origin)
            => m_propCleanupByOrigin.TryGetValue(origin, out AccessPropCleanupInfo info)
                && info.IsEligible;
        public bool TryGetCleanupInfoForTile(Tile2i tile, out AccessPropCleanupInfo info)
            => m_propCleanupByTile.TryGetValue(tile, out info);
        public bool TryGetRequiredCleanupInfoForTile(Tile2i tile, out AccessPropCleanupInfo info)
        {
            if (m_groundNodes.Contains(tile))
            {
                info = null!;
                return false;
            }
            return m_propCleanupByTile.TryGetValue(tile, out info)
                && info.IsEligible;
        }
        public bool CanTraverseToCleanupGround(Tile2i fromTile, Tile2i toTile)
        {
            if (!m_propCleanupByTile.TryGetValue(toTile, out AccessPropCleanupInfo toInfo)
                || !toInfo.IsEligible)
                return false;
            if (m_groundNodes.Contains(fromTile))
                return true;
            if (!m_propCleanupByTile.TryGetValue(fromTile, out AccessPropCleanupInfo fromInfo)
                || !fromInfo.IsEligible)
                return false;
            if (fromInfo.Origin == toInfo.Origin
                && (fromInfo.Samples.Count == 0 || toInfo.Samples.Count == 0))
                return true;
            if (fromInfo.HasTreeCleanup && !fromInfo.HasDenseDebrisCleanup
                && toInfo.HasTreeCleanup && !toInfo.HasDenseDebrisCleanup)
                return true;
            for (int fromIndex = 0; fromIndex < fromInfo.Samples.Count; fromIndex++)
            {
                string key = fromInfo.Samples[fromIndex].CleanupObjectKey;
                for (int toIndex = 0; toIndex < toInfo.Samples.Count; toIndex++)
                    if (toInfo.Samples[toIndex].CleanupObjectKey == key)
                        return true;
            }
            return false;
        }
        public string DescribeGroundGoalStatus(Tile2i tile)
        {
            Tile2i alignedOrigin = TerrainOriginForTile(tile);
            bool fixedProfileTile = m_fixedProfiles.ContainsKey(alignedOrigin);
            if (m_goalGroundNodes.Contains(tile))
                return fixedProfileTile ? "GoalGround+FixedTile" : "GoalGround";
            if (m_groundNodes.Contains(tile))
                return fixedProfileTile ? "GroundNotTowerReachable+FixedTile" : "GroundNotTowerReachable";
            if (m_propCleanupByTile.TryGetValue(tile, out AccessPropCleanupInfo cleanup)
                && cleanup.IsEligible)
                return fixedProfileTile ? "CleanupGround+FixedTile" : "CleanupGround";
            if (m_groundExclusionReasons.TryGetValue(tile, out string reason))
                return fixedProfileTile
                    ? "Excluded:" + reason + "+FixedTile"
                    : "Excluded:" + reason;
            return fixedProfileTile ? "NotCaptured+FixedTile" : "NotCaptured";
        }
        private static Tile2i TerrainOriginForTile(Tile2i tile)
            => new Tile2i(tile.X & -4, tile.Y & -4);

        private static Dictionary<Tile2i, AccessPropCleanupInfo> BuildCleanupByTile(
            IReadOnlyDictionary<Tile2i, AccessPropCleanupInfo> cleanupByOrigin)
        {
            var samplesByTile = new Dictionary<Tile2i, List<AccessPropSample>>();
            foreach (AccessPropCleanupInfo info in cleanupByOrigin.Values)
            {
                if (!info.IsEligible)
                    continue;
                if (info.Samples.Count == 0)
                {
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++)
                            AddSamples(info.Origin + new RelTile2i(x, y), Array.Empty<AccessPropSample>());
                    continue;
                }
                foreach (AccessPropSample sample in info.Samples)
                    AddSamples(sample.Tile, new[] { sample });
            }

            var result = new Dictionary<Tile2i, AccessPropCleanupInfo>();
            foreach (KeyValuePair<Tile2i, List<AccessPropSample>> pair in samplesByTile)
            {
                Tile2i origin = TerrainOriginForTile(pair.Key);
                if (pair.Value.Count == 0
                    && cleanupByOrigin.TryGetValue(origin, out AccessPropCleanupInfo fallback))
                {
                    result[pair.Key] = fallback;
                }
                else
                {
                    result[pair.Key] = AccessPropCleanupPolicy.BuildOriginInfo(origin, pair.Value);
                }
            }
            return result;

            void AddSamples(Tile2i tile, IReadOnlyList<AccessPropSample> samples)
            {
                if (!samplesByTile.TryGetValue(tile, out List<AccessPropSample> collected))
                {
                    collected = new List<AccessPropSample>();
                    samplesByTile.Add(tile, collected);
                }
                for (int i = 0; i < samples.Count; i++)
                    collected.Add(samples[i]);
            }
        }
        public bool IsGoalGroundNode(Tile2i tile) => m_goalGroundNodes.Contains(tile);
        public static bool IsDiagonalGoalTile(Tile2i tile)
            => (tile.X & 3) == (tile.Y & 3);

        public static HashSet<Tile2i> BuildDiagonalGoalNodes(IEnumerable<Tile2i> groundNodes)
        {
            var result = new HashSet<Tile2i>();
            foreach (Tile2i tile in groundNodes)
                if (IsDiagonalGoalTile(tile))
                    result.Add(tile);
            return result;
        }

        internal float[] AnyGoalDistance => m_anyGoalDistance;
        internal int MinGoalHeight2 => m_minGoalHeight2;
        internal int MaxGoalHeight2 => m_maxGoalHeight2;
        internal int GoalDistanceWidth => m_goalDistanceWidth;
        internal int GoalDistanceHeight => m_goalDistanceHeight;
        internal Tile2i GoalDistanceMin => m_goalDistanceMin;
        internal Tile2i GoalDistanceMax => m_goalDistanceMax;

        public float GetGoalTravelLowerBound(Tile2i tile, int height2)
        {
            if (m_anyGoalDistance == null || m_anyGoalDistance.Length == 0) return 0;
            int x = tile.X - m_goalDistanceMin.X;
            int y = tile.Y - m_goalDistanceMin.Y;
            if (x < 0 || x >= m_goalDistanceWidth || y < 0 || y >= m_goalDistanceHeight) return 0;
            int index = y * m_goalDistanceWidth + x;
            float horizontalDistance = m_anyGoalDistance[index];
            if (horizontalDistance < 0) return 0;
            int verticalDistance = height2 < m_minGoalHeight2
                ? m_minGoalHeight2 - height2
                : (height2 > m_maxGoalHeight2 ? height2 - m_maxGoalHeight2 : 0);
            return Math.Max(horizontalDistance, verticalDistance);
        }
        public bool TryGetGroundHeight2(Tile2i tile, out int height2) => m_groundHeight2.TryGetValue(tile, out height2);

        public bool IsProjectedV2CenterPathable(
            Tile2i center,
            V2.AccessV2History history)
        {
            EnsureProjectedV2ProfileCache(history);
            if (V2GroundGraph == null || !V2GroundGraph.IsTraversable(center))
                return false;
            int clearance = Math.Max(1, VehicleWidth);
            Tile2i corner = center + new RelTile2i(
                -(clearance / 2), -(clearance / 2));
            IReadOnlyCollection<Tile2i> rayTiles = history.CollectRayTiles();
            var raySet = rayTiles as HashSet<Tile2i>
                ?? new HashSet<Tile2i>(rayTiles);
            const float epsilon = 0.0001f;
            for (int y = 0; y < clearance; y++)
                for (int x = 0; x < clearance; x++)
                {
                    Tile2i tile = corner + new RelTile2i(x, y);
                    if (raySet.Contains(tile)
                        && !history.ContainsGeneratedTile(tile))
                        return false;
                    if (!TryGetProjectedV2Height(tile, history, out float height)
                        || !TryGetProjectedV2Height(
                            tile + new RelTile2i(1, 0), history,
                            out float plusX)
                        || !TryGetProjectedV2Height(
                            tile + new RelTile2i(0, 1), history,
                            out float plusY))
                        return false;
                    if (Math.Max(
                            Math.Abs(height - plusX),
                            Math.Abs(height - plusY))
                        > VehicleMaxSteepnessDelta + epsilon)
                        return false;
                }
            return true;
        }

        public bool DoesProjectedV2CenterOverlapWork(
            Tile2i center,
            V2.AccessV2History history)
        {
            EnsureProjectedV2ProfileCache(history);
            int clearance = Math.Max(1, VehicleWidth);
            Tile2i corner = center + new RelTile2i(
                -(clearance / 2), -(clearance / 2));
            const float epsilon = 0.0001f;
            for (int y = 0; y < clearance; y++)
                for (int x = 0; x < clearance; x++)
                {
                    Tile2i tile = corner + new RelTile2i(x, y);
                    if (!TryGetProjectedV2Height(tile, history, out float projected)
                        || !m_preciseTerrainHeights.TryGetValue(
                            tile, out float natural))
                        return true;
                    if (Math.Abs(projected - natural) > epsilon)
                        return true;
                }
            return false;
        }

        private bool TryGetProjectedV2Height(
            Tile2i tile,
            V2.AccessV2History history,
            out float height)
        {
            Tile2i canonical = TerrainOriginForTile(tile);
            Tile2i[] candidates =
            {
                canonical,
                canonical + new RelTile2i(-4, 0),
                canonical + new RelTile2i(0, -4),
                canonical + new RelTile2i(-4, -4),
            };
            for (int index = 0; index < candidates.Length; index++)
            {
                Tile2i origin = candidates[index];
                int localX = tile.X - origin.X;
                int localY = tile.Y - origin.Y;
                if (localX < 0 || localX > 4
                    || localY < 0 || localY > 4)
                    continue;
                if (m_projectedV2CachedProfiles.TryGetValue(
                        origin, out AccessHeightProfile generatedProfile))
                {
                    height = generatedProfile.GetHeight2NumeratorAt(
                        localX, localY) / 32f;
                    return true;
                }
            }
            for (int index = 0; index < candidates.Length; index++)
            {
                Tile2i origin = candidates[index];
                int localX = tile.X - origin.X;
                int localY = tile.Y - origin.Y;
                if (localX < 0 || localX > 4
                    || localY < 0 || localY > 4)
                    continue;
                if (m_fixedProfiles.TryGetValue(
                        origin, out AccessHeightProfile fixedProfile))
                {
                    height = fixedProfile.GetHeight2NumeratorAt(
                        localX, localY) / 32f;
                    return true;
                }
            }
            return m_preciseTerrainHeights.TryGetValue(tile, out height);
        }

        private void EnsureProjectedV2ProfileCache(
            V2.AccessV2History history)
        {
            if (ReferenceEquals(m_projectedV2CachedHistory, history))
                return;
            m_projectedV2CachedHistory = history;
            m_projectedV2CachedProfiles = history.Flatten();
        }
        public AccessTerrainSampleKind GetSideRayTerrainSample(
            Tile2i tile,
            out float terrainHeight)
        {
            if (tile.X < PhysicalTerrainMin.X
                || tile.Y < PhysicalTerrainMin.Y
                || tile.X > PhysicalTerrainMax.X
                || tile.Y > PhysicalTerrainMax.Y)
            {
                terrainHeight = 0f;
                return AccessTerrainSampleKind.PhysicalMapEdge;
            }
            if (!m_preciseTerrainHeights.TryGetValue(tile, out terrainHeight))
                return AccessTerrainSampleKind.MissingSnapshot;
            return m_oceanTiles.Contains(tile)
                ? AccessTerrainSampleKind.Ocean
                : AccessTerrainSampleKind.Terrain;
        }

        public bool TryGetMiningMaterialSlope(
            Tile2i tile,
            float plannedElevation,
            out float slope,
            out string materialId,
            out bool usedFallback)
        {
            if (m_terrainColumns.TryGetValue(tile, out AccessTerrainColumn column)
                && column.TryGetNormalSlopeAt(
                    plannedElevation, out slope, out materialId))
            {
                usedFallback = false;
                return true;
            }

            slope = FallbackMiningSlope;
            materialId = string.Empty;
            usedFallback = true;
            return slope > 0f;
        }

        public int GetTerrainCenterHeight2(Tile2i origin) => m_terrainCenterHeight2.TryGetValue(origin, out int h2) ? h2 : 0;
        public string? GetSideRayBlockerReason(
            Tile2i tile, AccessSideRayOperation rayOperation,
            Tile2i? exemptDesignationOrigin = null)
            => AvoidBuildings && m_expandedBuildingRayBlockers.Contains(tile)
                ? "SideRayBuilding"
                : exemptDesignationOrigin.HasValue
                    && IsDesignationFootprintTile(tile, exemptDesignationOrigin.Value)
                    ? null
                : m_hardDesignationRayBlockers.Contains(tile)
                    ? "SideRayDesignation"
                : rayOperation == AccessSideRayOperation.Cut
                        ? m_fillDesignationRayBlockers.Contains(tile)
                            || IsProjectedBlockerFromOtherSource(
                                tile, exemptDesignationOrigin,
                                m_projectedFillRayBlockers,
                                m_projectedFillSourcesByTile)
                            ? "SideRayOpposingDesignationWork"
                            : null
                        : rayOperation == AccessSideRayOperation.Fill
                            && (m_cutDesignationRayBlockers.Contains(tile)
                                || IsProjectedBlockerFromOtherSource(
                                    tile, exemptDesignationOrigin,
                                    m_projectedCutRayBlockers,
                                    m_projectedCutSourcesByTile))
                                ? "SideRayOpposingDesignationWork"
                                : null;

        private static bool IsProjectedBlockerFromOtherSource(
            Tile2i tile,
            Tile2i? exemptDesignationOrigin,
            ISet<Tile2i> projectedBlockers,
            IReadOnlyDictionary<Tile2i, HashSet<Tile2i>> sourcesByTile)
        {
            if (!projectedBlockers.Contains(tile)) return false;
            if (!exemptDesignationOrigin.HasValue) return true;
            if (!sourcesByTile.TryGetValue(tile, out HashSet<Tile2i> sources))
                return true;
            foreach (Tile2i source in sources)
                if (source != exemptDesignationOrigin.Value)
                    return true;
            return false;
        }

        private static Dictionary<Tile2i, HashSet<Tile2i>> CopySourceMap(
            IDictionary<Tile2i, HashSet<Tile2i>>? source)
        {
            var copy = new Dictionary<Tile2i, HashSet<Tile2i>>();
            if (source == null) return copy;
            foreach (KeyValuePair<Tile2i, HashSet<Tile2i>> pair in source)
                copy.Add(pair.Key, new HashSet<Tile2i>(pair.Value));
            return copy;
        }

        private static bool IsDesignationFootprintTile(
            Tile2i tile, Tile2i origin)
            => tile.X >= origin.X && tile.X <= origin.X + 4
                && tile.Y >= origin.Y && tile.Y <= origin.Y + 4;
        public bool TryGetCachedSideRay(AccessSideRayCacheKey key, out AccessSideRayResult result)
            => m_sideRayCache.TryGetValue(key, out result);
        public void CacheSideRay(AccessSideRayCacheKey key, AccessSideRayResult result)
            => m_sideRayCache[key] = result;
        public bool HasWorkableHandoffEvaluator => m_workableHandoffs != null;
        public bool HasWorkableHandoffSpanEvaluator => m_workableHandoffSpans != null;
        public IReadOnlyList<AccessGroundHandoff> GetWorkableHandoffs(
            Tile2i origin, AccessHeightProfile profile,
            Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile)
            => m_workableHandoffs?.Invoke(origin, profile, predecessorOrigin, predecessorProfile)
                ?? Array.Empty<AccessGroundHandoff>();
        public IReadOnlyList<AccessGroundHandoff> GetWorkableHandoffSpans(
            IReadOnlyList<AccessHandoffSpanCell> cells)
            => m_workableHandoffSpans?.Invoke(cells)
                ?? Array.Empty<AccessGroundHandoff>();
        public bool HasV2WorkableHandoffEvaluator
            => m_v2WorkableHandoffs != null && m_v2WorkableHandoffSpans != null;
        public IReadOnlyList<AccessGroundHandoff> GetV2WorkableHandoffs(
            Tile2i origin, AccessHeightProfile profile,
            Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile)
            => m_v2WorkableHandoffs?.Invoke(
                    origin, profile, predecessorOrigin, predecessorProfile)
                ?? Array.Empty<AccessGroundHandoff>();
        public IReadOnlyList<AccessGroundHandoff> GetV2WorkableHandoffSpans(
            IReadOnlyList<AccessHandoffSpanCell> cells)
            => m_v2WorkableHandoffSpans?.Invoke(cells)
                ?? Array.Empty<AccessGroundHandoff>();

        public bool IsProfileOceanBlocked(Tile2i origin, AccessHeightProfile profile)
        {
            if (!AvoidOcean)
                return false;
            const float minimumDrivableOceanHeight = 1f;
            for (int y = 0; y <= 4; y++)
                for (int x = 0; x <= 4; x++)
                {
                    Tile2i tile = origin + new RelTile2i(x, y);
                    // Fill rays may spill into ocean, but the vehicle-bearing
                    // profile itself must finish above the drivable threshold.
                    if (m_oceanTiles.Contains(tile)
                        && profile.GetHeight2NumeratorAt(x, y) / 32f
                            < minimumDrivableOceanHeight)
                        return true;
                }
            return false;
        }

        public bool IsProfileBlockedByProjectedDesignationHeight(
            Tile2i origin, AccessHeightProfile profile)
        {
            const float epsilon = 0.0001f;
            for (int y = 0; y <= 4; y++)
                for (int x = 0; x <= 4; x++)
                {
                    Tile2i tile = origin + new RelTile2i(x, y);
                    float profileHeight = profile.GetHeight2NumeratorAt(x, y) / 32f;
                    if (m_projectedCutSupportCeilings.TryGetValue(
                            tile, out float supportCeiling)
                        && profileHeight > supportCeiling + epsilon)
                        return true;
                    if (m_projectedFillSurfaceFloors.TryGetValue(
                            tile, out float fillFloor)
                        && profileHeight < fillFloor - epsilon)
                        return true;
                }
            return false;
        }

        public bool IsCandidateProfileFeasible(Tile2i origin, AccessHeightProfile profile, out string reason)
            => IsCandidateProfileFeasibleCore(origin, profile, out reason);

        public bool IsCandidateProfileFeasibleFromValidatedPredecessor(
            Tile2i origin, AccessHeightProfile profile, Tile2i predecessorOrigin,
            Tile2i direction, out string reason)
            => IsCandidateProfileFeasibleCore(origin, profile, out reason);

        private bool IsCandidateProfileFeasibleCore(
            Tile2i origin, AccessHeightProfile profile, out string reason)
        {
            if (!IsOriginInside(origin)) { reason = "HorizontalBounds"; return false; }
            if (m_workOrigins.Contains(origin)) { reason = "WorkOrigin"; return false; }
            if (m_fixedProfiles.ContainsKey(origin)) { reason = "ExistingDesignation"; return false; }
            if (!profile.HasIntegerCorners) { reason = "HalfLevelCorner"; return false; }
            if (profile.Center2 < MinHeight2 || profile.Center2 > MaxHeight2) { reason = "VerticalBounds"; return false; }
            if (IsProfileOceanBlocked(origin, profile)) { reason = "OceanBelowMinimum"; return false; }

            string? operationMismatch = null;
            for (int y = 0; !AllowsMixedWork && y <= 4 && operationMismatch == null; y++)
            {
                for (int x = 0; x <= 4; x++)
                {
                    Tile2i sample = origin + new RelTile2i(x, y);
                    if (!m_groundHeight2.TryGetValue(sample, out int terrainHeight2)) continue;
                    int targetHeight2Numerator = profile.GetHeight2NumeratorAt(x, y);
                    int terrainHeight2Numerator = terrainHeight2 * 16;
                    if (IsMining && targetHeight2Numerator > terrainHeight2Numerator)
                    {
                        operationMismatch = "RequiresDumping";
                        break;
                    }
                    if (!IsMining && targetHeight2Numerator < terrainHeight2Numerator)
                    {
                        operationMismatch = "RequiresMining";
                        break;
                    }
                }
            }
            if (operationMismatch != null) { reason = operationMismatch; return false; }

            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    if (m_occupiedTiles.Contains(origin + new RelTile2i(x, y)))
                    { reason = "Building"; return false; }

            bool durabilityBlocked = false;
            profile.AddWorldCorners(origin, (corner, height2) =>
            {
                if (durabilityBlocked) return;
                durabilityBlocked = IsDurabilityBlocked(corner, height2);
            });
            if (durabilityBlocked) { reason = "Durability"; return false; }

            if (!MatchesFixedNeighbors(origin, profile)) { reason = "FightInvariant"; return false; }
            reason = string.Empty;
            return true;
        }

        private static HashSet<Tile2i> BuildExpandedBuildingRayBlockers(
            IEnumerable<Tile2i> occupiedTiles,
            Tile2i boundsMin,
            Tile2i boundsMax)
        {
            const int safetyBoundary = AutoDepthDesignation.BuildingSafetyBufferTiles;
            const int rayDistance = 16;
            int minX = boundsMin.X - rayDistance - safetyBoundary;
            int maxX = boundsMax.X + rayDistance + safetyBoundary;
            int minY = boundsMin.Y - rayDistance - safetyBoundary;
            int maxY = boundsMax.Y + rayDistance + safetyBoundary;
            var result = new HashSet<Tile2i>();
            foreach (Tile2i occupied in occupiedTiles)
            {
                if (occupied.X < minX || occupied.X > maxX
                    || occupied.Y < minY || occupied.Y > maxY)
                    continue;
                for (int dy = -safetyBoundary; dy <= safetyBoundary; dy++)
                    for (int dx = -safetyBoundary; dx <= safetyBoundary; dx++)
                        result.Add(new Tile2i(occupied.X + dx, occupied.Y + dy));
            }
            return result;
        }

        private static HashSet<Tile2i> BuildDesignationRayBlockers(
            IEnumerable<Tile2i> origins)
        {
            var result = new HashSet<Tile2i>();
            foreach (Tile2i origin in origins)
                for (int y = 0; y <= 4; y++)
                    for (int x = 0; x <= 4; x++)
                        result.Add(origin + new RelTile2i(x, y));
            return result;
        }

        public bool IsDurabilityBlocked(Tile2i position, int height2)
        {
            if (position.X < BoundsMin.X || position.X > BoundsMax.X
                || position.Y < BoundsMin.Y || position.Y > BoundsMax.Y)
                return false;

            int cx = (position.X - BoundsMin.X) / SPATIAL_CELL_SIZE;
            int cy = (position.Y - BoundsMin.Y) / SPATIAL_CELL_SIZE;
            if (cx < 0 || cx >= m_gridWidth || cy < 0 || cy >= m_gridHeight) return false;
            List<AccessDurabilityCorner>? cellCorners = m_spatialGrid[cx, cy];
            if (cellCorners == null) return false;

            foreach (AccessDurabilityCorner corner in cellCorners)
            {
                if (corner.Blocks(position, height2, LandslideRunPerHeight))
                    return true;
            }
            return false;
        }

        private bool MatchesFixedNeighbors(Tile2i origin, AccessHeightProfile profile)
        {
            var candidateCorners = new Dictionary<Tile2i, int>();
            profile.AddWorldCorners(origin, (p, h) => candidateCorners[p] = h);
            for (int dy = -4; dy <= 4; dy += 4)
            {
                for (int dx = -4; dx <= 4; dx += 4)
                {
                    if (dx == 0 && dy == 0) continue;
                    Tile2i neighbor = origin + new RelTile2i(dx, dy);
                    if (!m_fixedProfiles.TryGetValue(neighbor, out AccessHeightProfile fixedProfile)) continue;
                    bool mismatch = false;
                    fixedProfile.AddWorldCorners(neighbor, (p, h) =>
                    {
                        if (candidateCorners.TryGetValue(p, out int own) && own != h) mismatch = true;
                    });
                    if (mismatch) return false;
                }
            }
            return true;
        }



        internal static float[] BuildGoalDistance(Tile2i boundsMin, Tile2i boundsMax,
            HashSet<Tile2i> goals)
        {
            int width = boundsMax.X - boundsMin.X + 1;
            int height = boundsMax.Y - boundsMin.Y + 1;
            var result = new float[width * height];
            for (int i = 0; i < result.Length; i++) result[i] = -1;
            var queue = new SortedDictionary<float, Queue<int>>();
            foreach (Tile2i goal in goals)
            {
                if (goal.X < boundsMin.X || goal.X > boundsMax.X
                    || goal.Y < boundsMin.Y || goal.Y > boundsMax.Y)
                    continue;
                int index = (goal.Y - boundsMin.Y) * width + goal.X - boundsMin.X;
                result[index] = 0;
                Enqueue(index, 0f);
            }
            while (queue.Count > 0)
            {
                KeyValuePair<float, Queue<int>> first = First(queue);
                int current = first.Value.Dequeue();
                if (first.Value.Count == 0) queue.Remove(first.Key);
                if (Math.Abs(result[current] - first.Key) > 0.0001f)
                    continue;
                int x = current % width;
                int y = current / width;
                Relax(current - 1, first.Key, 1f, x > 0);
                Relax(current + 1, first.Key, 1f, x + 1 < width);
                Relax(current - width, first.Key, 1f, y > 0);
                Relax(current + width, first.Key, 1f, y + 1 < height);
                const float diagonal = 1.41421356237f;
                Relax(current - width - 1, first.Key, diagonal, x > 0 && y > 0);
                Relax(current - width + 1, first.Key, diagonal, x + 1 < width && y > 0);
                Relax(current + width - 1, first.Key, diagonal, x > 0 && y + 1 < height);
                Relax(current + width + 1, first.Key, diagonal, x + 1 < width && y + 1 < height);
            }
            return result;

            void Relax(int next, float currentDistance, float stepCost, bool inside)
            {
                if (!inside) return;
                float nextDistance = currentDistance + stepCost;
                if (result[next] >= 0f && result[next] <= nextDistance + 0.0001f)
                    return;
                result[next] = nextDistance;
                Enqueue(next, nextDistance);
            }

            void Enqueue(int tile, float distance)
            {
                if (!queue.TryGetValue(distance, out Queue<int> bucket))
                {
                    bucket = new Queue<int>();
                    queue.Add(distance, bucket);
                }
                bucket.Enqueue(tile);
            }

            KeyValuePair<float, Queue<int>> First(
                SortedDictionary<float, Queue<int>> items)
            {
                foreach (KeyValuePair<float, Queue<int>> pair in items)
                    return pair;
                throw new InvalidOperationException("Goal-distance queue is empty.");
            }
        }
    }

    internal readonly struct AccessLandscapingCost
    {
        public float DirectWorkCost { get; }
        public float LeftSideRayCost { get; }
        public float RightSideRayCost { get; }
        public float UnresolvedPenalty { get; }
        public int RaySampleCount { get; }
        public IReadOnlyList<Tile2i> DisturbedRayTiles { get; }
        public IReadOnlyList<AccessRayHeightConstraint> RayHeightConstraints { get; }
        public string? FatalReason { get; }
        public float TotalCost =>
            DirectWorkCost + LeftSideRayCost + RightSideRayCost + UnresolvedPenalty;
        public bool IsFatal => !string.IsNullOrEmpty(FatalReason);

        public AccessLandscapingCost(
            float directWorkCost,
            float leftSideRayCost = 0f,
            float rightSideRayCost = 0f,
            float unresolvedPenalty = 0f,
            int raySampleCount = 0,
            string? fatalReason = null,
            IEnumerable<Tile2i>? disturbedRayTiles = null,
            IEnumerable<AccessRayHeightConstraint>? rayHeightConstraints = null)
        {
            DirectWorkCost = directWorkCost;
            LeftSideRayCost = leftSideRayCost;
            RightSideRayCost = rightSideRayCost;
            UnresolvedPenalty = unresolvedPenalty;
            RaySampleCount = raySampleCount;
            DisturbedRayTiles = disturbedRayTiles != null
                ? new List<Tile2i>(disturbedRayTiles).ToArray()
                : Array.Empty<Tile2i>();
            RayHeightConstraints = rayHeightConstraints != null
                ? new List<AccessRayHeightConstraint>(rayHeightConstraints).ToArray()
                : Array.Empty<AccessRayHeightConstraint>();
            FatalReason = fatalReason;
        }
    }

    internal readonly struct AccessRayHeightConstraint
    {
        public Tile2i Tile { get; }
        public AccessSideRayOperation Operation { get; }
        public float Height { get; }

        public AccessRayHeightConstraint(
            Tile2i tile, AccessSideRayOperation operation, float height)
        {
            Tile = tile;
            Operation = operation;
            Height = height;
        }
    }

    internal sealed class AccessSearchResult
    {
        public bool Success { get; }
        public string FailureReason { get; }
        public Tile2i StartOrigin { get; }
        public IReadOnlyList<AccessSearchNode> Path { get; }
        public float Cost { get; }
        public int VisitedNodes { get; }
        public IReadOnlyDictionary<string, int> Rejections { get; }
        public float TraversalCost { get; }
        public float GeneratedWorkCost { get; }
        public float GeneratedFixedCost { get; }
        public float TreeCleanupCost { get; }
        public float DenseDebrisCleanupCost { get; }
        public float GeneratedDirectWorkCost { get; }
        public float LeftSideRayCost { get; }
        public float RightSideRayCost { get; }
        public float SideRayUnresolvedPenalty { get; }
        public int SideRaySampleCount { get; }
        public AccessReachedGoalKind ReachedGoalKind { get; }
        public AccessSearchDiagnostics Diagnostics { get; }
        public AccessV2RouteData? V2Route { get; }

        public AccessSearchResult(bool success, string failureReason, Tile2i startOrigin,
            IReadOnlyList<AccessSearchNode> path, float cost, int visitedNodes,
            IReadOnlyDictionary<string, int> rejections)
            : this(success, failureReason, startOrigin, path, cost, visitedNodes,
                rejections, cost, 0f, 0f, 0f, 0f, AccessReachedGoalKind.None)
        {
        }

        public AccessSearchResult(bool success, string failureReason, Tile2i startOrigin,
            IReadOnlyList<AccessSearchNode> path, float cost, int visitedNodes,
            IReadOnlyDictionary<string, int> rejections,
            AccessSearchDiagnostics diagnostics)
            : this(success, failureReason, startOrigin, path, cost, visitedNodes,
                rejections, cost, 0f, 0f, 0f, 0f,
                AccessReachedGoalKind.None, diagnostics: diagnostics)
        {
        }

        public AccessSearchResult(bool success, string failureReason, Tile2i startOrigin,
            IReadOnlyList<AccessSearchNode> path, float cost, int visitedNodes,
            IReadOnlyDictionary<string, int> rejections, float traversalCost,
            float generatedWorkCost, float generatedFixedCost, float treeCleanupCost,
            float denseDebrisCleanupCost,
            AccessReachedGoalKind reachedGoalKind = AccessReachedGoalKind.None,
            float generatedDirectWorkCost = 0f,
            float leftSideRayCost = 0f,
            float rightSideRayCost = 0f,
            float sideRayUnresolvedPenalty = 0f,
            int sideRaySampleCount = 0,
            AccessSearchDiagnostics? diagnostics = null,
            AccessV2RouteData? v2Route = null)
        {
            Success = success;
            FailureReason = failureReason;
            StartOrigin = startOrigin;
            Path = path;
            Cost = cost;
            VisitedNodes = visitedNodes;
            Rejections = rejections;
            TraversalCost = traversalCost;
            GeneratedWorkCost = generatedWorkCost;
            GeneratedFixedCost = generatedFixedCost;
            TreeCleanupCost = treeCleanupCost;
            DenseDebrisCleanupCost = denseDebrisCleanupCost;
            GeneratedDirectWorkCost = generatedDirectWorkCost;
            LeftSideRayCost = leftSideRayCost;
            RightSideRayCost = rightSideRayCost;
            SideRayUnresolvedPenalty = sideRayUnresolvedPenalty;
            SideRaySampleCount = sideRaySampleCount;
            ReachedGoalKind = reachedGoalKind;
            Diagnostics = diagnostics?.Clone() ?? new AccessSearchDiagnostics();
            V2Route = v2Route;
        }
    }

    internal sealed class AccessSearchDiagnostics
    {
        private const int MaxStartDiagnosticDetails = 64;

        public int GroundExpansions;
        public int OriginExpansions;
        public int GroundSuccessorChecks;
        public int GroundRelaxations;
        public int CleanupGroundSuccessorChecks;
        public int CleanupGroundRelaxations;
        public int OriginNeighborChecks;
        public int FixedProfileSuccessorChecks;
        public int FixedProfileRelaxations;
        public int GeneratedModeAttempts;
        public int GeneratedProfileFeasibleChecks;
        public int GeneratedProfileFeasibleFailures;
        public int GeneratedPathHistoryFailures;
        public int SideRayCostChecks;
        public int SideRayCostRejections;
        public int SideRayCostSamples;
        public int SideRayCacheHits;
        public int SideRayCacheMisses;
        public int GeneratedHistoryCostReuses;
        public int GeneratedHistoryCostRecalculations;
        public int GeneratedHistoryNodesCreated;
        public int GeneratedHistoryMaxDepth;
        public int PropCleanupChecks;
        public int PropCleanupHits;
        public int PropCleanupRejections;
        public int GeneratedRelaxations;
        public int GroundToGeneratedOriginChecks;
        public int GroundToGeneratedProfileAttempts;
        public int GroundToGeneratedHandoffFailures;
        public int GoalPops;
        public int GoalRejected;
        public int GoalAcceptedAtVisited;
        public int QueueRelaxations;
        public int QueueStalePops;
        public long GroundExpansionTicks;
        public long OriginExpansionTicks;
        public long ProfileFeasibilityTicks;
        public long HandoffValidationTicks;
        public long PathHistoryTicks;
        public long SideRayCostTicks;
        public long PropCleanupTicks;
        public List<string> StartSuccessorDetails { get; } = new List<string>();
        public List<string> FirstGeneratedHandoffDetails { get; } = new List<string>();
        public string V2DryRunSummary = string.Empty;
        public string V2DryRunPath = string.Empty;

        public void RecordStartSuccessor(string detail)
        {
            if (StartSuccessorDetails.Count < MaxStartDiagnosticDetails)
                StartSuccessorDetails.Add(detail);
        }

        public void RecordFirstGeneratedHandoff(string detail)
        {
            if (FirstGeneratedHandoffDetails.Count < MaxStartDiagnosticDetails)
                FirstGeneratedHandoffDetails.Add(detail);
        }

        public AccessSearchDiagnostics Clone()
        {
            return (AccessSearchDiagnostics)MemberwiseClone();
        }
    }
}

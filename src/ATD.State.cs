// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Input;
using Mafi.Core.Notifications;
using Mafi.Core.PathFinding;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.Terrain.Resources;
using Mafi.Core.Terrain.Trees;
using Mafi.Core.Vehicles.Excavators;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.World;
using CoI.AutoHelpers.Logging;
using UnityEngine;

namespace AutoTerrainDesignations
{
    internal enum AccessVehicleClearanceMode { Off = 0, Auto = 1, T1 = 2, T2 = 3, T3 = 4 }

    public static partial class AutoDepthDesignation
    {
        // Shared Chebyshev-distance margin around static building footprints for
        // mining-body exclusion and accessway terrain-disturbance checks.
        internal const int BuildingSafetyBufferTiles = 3;

        private static TerrainDesignationsManager? s_desigManager;
        private static TerrainDesignationProto? s_miningProto;
        private static TerrainDesignationProto? s_dumpingProto;
        private static TerrainDesignationProto? s_levelingProto;
        private static TerrainMaterialProto? s_bedrockTerrainMaterial;
        private static MonoBehaviour? s_coroutineHost;
        private static ProtosDb? s_protosDb;
        private static WorldMapManager? s_worldMapManager;
        private static IEntitiesManager? s_entitiesManager;
        private static IInputScheduler? s_inputScheduler;
        private static ConfigSerializationContext? s_configSerializationContext;
        private static TerrainPropsManager? s_terrainPropsManager;
        private static TreesManager? s_treesManager;
        internal static IVehiclePathFindingManager? s_vehiclePathFindingManager;
        private static IVehiclesManager? s_vehiclesManager;
        private static ParkAndWaitJobFactory? s_parkAndWaitJobFactory;
        private static VehiclePathFindingParams? s_excavatorPathFindingParams;
        internal static VehiclePathFindingParams? s_standardVehiclePathFindingParams;
        private static string? s_modRootDirectoryPath;
        private static int s_worldGeneration;
        internal static readonly ModLogger s_log = new ModLogger("ATD");

        private const int BATCH_SIZE = 30;
        private const int MAX_BATCH_SIZE = 200;
        private const int HULL_CONNECTION_WIDTH = 2;
        private static int s_batchSize = BATCH_SIZE;

        private sealed class ATDTowerSettings
        {
            public int MaxHeightDiff { get; private set; }
            public AccessVehicleClearanceMode VehicleClearance { get; private set; }
            public int RampWidth => VehicleClearance == AccessVehicleClearanceMode.Off
                ? 0 : VehicleClearance == AccessVehicleClearanceMode.T3 ? 2 : 1;
            public int MaxLayersToExcavate { get; private set; }
            public int? MaxDepthToDigTo { get; private set; }
            public int OrePurityLevel { get; private set; }
            public int CorridorClearance => VehicleClearance == AccessVehicleClearanceMode.Off
                ? 0 : VehicleClearance == AccessVehicleClearanceMode.T3 ? 2 : 1;
            public bool AutoReleaseExcavatorsWhenIdle { get; private set; }
            public bool AutoReleaseTrucksWhenIdle { get; private set; }
            public bool MiningPlanDirty { get; private set; } = true;
            public string? LastMiningPlanFingerprint { get; private set; }

            /// <summary>Outcome of the most recent ramp generation attempt. Null = no scan run yet.</summary>
            public RampPlacementOutcome? LastRampOutcome { get; set; }

            public ATDTowerSettings(int maxHeightDiff, int rampWidth, int maxLayersToExcavate, int? maxDepthToDigTo, int orePurityLevel, int corridorClearance, bool autoReleaseExcavatorsWhenIdle = false, bool autoReleaseTrucksWhenIdle = false)
            {
                SetMaxHeightDiff(maxHeightDiff);
                SetRampWidth(rampWidth);
                SetMaxLayersToExcavate(maxLayersToExcavate);
                SetMaxDepthToDigTo(maxDepthToDigTo);
                SetOrePurityLevel(orePurityLevel);
                SetAutoReleaseExcavatorsWhenIdle(autoReleaseExcavatorsWhenIdle);
                SetAutoReleaseTrucksWhenIdle(autoReleaseTrucksWhenIdle);
            }

            public static ATDTowerSettings FromGlobalDefaults()
            {
                var settings = new ATDTowerSettings(
                AutoTerrainDesignationsMod.MaxHeightDiff,
                AutoTerrainDesignationsMod.RampWidth,
                AutoTerrainDesignationsMod.MaxLayersToExcavate,
                AutoTerrainDesignationsMod.MaxDepthToDigTo,
                AutoTerrainDesignationsMod.OrePurityLevel,
                AutoTerrainDesignationsMod.MinCorridorClearance,
                AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle,
                AutoTerrainDesignationsMod.AutoReleaseTrucksWhenIdle);
                settings.SetVehicleClearance(AutoTerrainDesignationsMod.VehicleClearance);
                return settings;
            }

            public void SetMaxHeightDiff(int value)
            {
                int clamped = Math.Max(1, Math.Min(3, value));
                if (MaxHeightDiff != clamped)
                    MiningPlanDirty = true;
                MaxHeightDiff = clamped;
            }

            public void SetRampWidth(int value) => SetVehicleClearance(
                value == 0 ? AccessVehicleClearanceMode.Off : AccessVehicleClearanceMode.Auto);

            public void SetVehicleClearance(AccessVehicleClearanceMode value)
            {
                AccessVehicleClearanceMode clamped = value < AccessVehicleClearanceMode.Off
                    || value > AccessVehicleClearanceMode.T3
                        ? AccessVehicleClearanceMode.Auto : value;
                if (VehicleClearance != clamped) MiningPlanDirty = true;
                VehicleClearance = clamped;
            }

            public void SetMaxLayersToExcavate(int value)
            {
                int clamped = Math.Max(0, value);
                if (MaxLayersToExcavate != clamped)
                    MiningPlanDirty = true;
                MaxLayersToExcavate = clamped;
            }

            public void SetMaxDepthToDigTo(int? value)
            {
                if (MaxDepthToDigTo != value)
                    MiningPlanDirty = true;
                MaxDepthToDigTo = value;
            }

            public void SetOrePurityLevel(int value)
            {
                int clamped = Math.Max(0, Math.Min(4, value));
                if (OrePurityLevel != clamped)
                    MiningPlanDirty = true;
                OrePurityLevel = clamped;
            }

            public void SetCorridorClearance(int value) { }

            public void SetAutoReleaseExcavatorsWhenIdle(bool value) => AutoReleaseExcavatorsWhenIdle = value;

            public void SetAutoReleaseTrucksWhenIdle(bool value) => AutoReleaseTrucksWhenIdle = value;

            public void SetAutoReleaseWhenIdle(bool value)
            {
                SetAutoReleaseExcavatorsWhenIdle(value);
                SetAutoReleaseTrucksWhenIdle(value);
            }

            public void MarkMiningPlanDirty()
            {
                MiningPlanDirty = true;
            }

            public void MarkMiningPlanClean(string fingerprint)
            {
                LastMiningPlanFingerprint = fingerprint;
                MiningPlanDirty = false;
            }

            public bool MatchesGlobalDefaults()
            {
                return MaxHeightDiff == AutoTerrainDesignationsMod.MaxHeightDiff
                    && RampWidth == AutoTerrainDesignationsMod.RampWidth
                    && MaxLayersToExcavate == AutoTerrainDesignationsMod.MaxLayersToExcavate
                    && MaxDepthToDigTo == AutoTerrainDesignationsMod.MaxDepthToDigTo
                    && OrePurityLevel == AutoTerrainDesignationsMod.OrePurityLevel
                    && CorridorClearance == AutoTerrainDesignationsMod.MinCorridorClearance
                    && AutoReleaseExcavatorsWhenIdle == AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle
                    && AutoReleaseTrucksWhenIdle == AutoTerrainDesignationsMod.AutoReleaseTrucksWhenIdle;
            }
        }

        private static readonly Tile2i[] s_cardinalDirections =
        {
            new Tile2i(4, 0),
            new Tile2i(-4, 0),
            new Tile2i(0, 4),
            new Tile2i(0, -4),
        };

        // Per-tower ore selection: entityId -> selected ore (missing/null = AUTO)
        private static readonly Dictionary<EntityId, ProductProto?> s_selectedOrePerTower =
            new Dictionary<EntityId, ProductProto?>();
        private static readonly Dictionary<EntityId, ATDTowerSettings> s_towerSettingsByEntityId =
            new Dictionary<EntityId, ATDTowerSettings>();
        private static readonly Dictionary<EntityId, LooseProductProto> s_excavatorPriorityByTowerEntityId =
            new Dictionary<EntityId, LooseProductProto>();
        // Per-tower panel collapsed states
        private static readonly Dictionary<EntityId, bool> s_terrainPanelCollapsedByEntityId =
            new Dictionary<EntityId, bool>();
        private static readonly Dictionary<EntityId, bool> s_orePanelCollapsedByEntityId =
            new Dictionary<EntityId, bool>();
        private static readonly Dictionary<EntityId, bool> s_farmingPanelCollapsedByEntityId =
            new Dictionary<EntityId, bool>();
        private static readonly Dictionary<EntityId, HashSet<Tile2i>> s_generatedAccesswayOriginsByTowerEntityId =
            new Dictionary<EntityId, HashSet<Tile2i>>();
        private static readonly Dictionary<EntityId, HashSet<Tile2i>> s_generatedDesignationOriginsByTowerEntityId =
            new Dictionary<EntityId, HashSet<Tile2i>>();
        private static readonly Dictionary<EntityId, HashSet<Tile2i>> s_generatedHarvestTreePositionsByTowerEntityId =
            new Dictionary<EntityId, HashSet<Tile2i>>();
        private static bool s_startupTowerPrioritySyncCompleted;
        private static int s_startupTowerPrioritySyncAttempts;
        private static bool s_accessAvoidOcean = true;
        private static bool s_accessAvoidBuildings = true;
        private static bool s_accessHarvestDisruptedTrees = true;

        internal static bool AccessAvoidOcean => s_accessAvoidOcean;
        internal static bool AccessAvoidBuildings => s_accessAvoidBuildings;
        internal static bool AccessHarvestDisruptedTrees => s_accessHarvestDisruptedTrees;

        internal static void SetAccessAvoidOcean(bool value) => s_accessAvoidOcean = value;
        internal static void SetAccessAvoidBuildings(bool value) => s_accessAvoidBuildings = value;
        internal static void SetAccessHarvestDisruptedTrees(bool value) => s_accessHarvestDisruptedTrees = value;

        internal static void ResetWorldPathfinderSettingsToDefaults()
        {
            s_accessAvoidOcean = AutoTerrainDesignationsMod.AccessAvoidOcean;
            s_accessAvoidBuildings = AutoTerrainDesignationsMod.AccessAvoidBuildings;
            s_accessHarvestDisruptedTrees = AutoTerrainDesignationsMod.AccessHarvestDisruptedTrees;
        }

        internal static void SaveWorldPathfinderSettingsAsGlobalDefaults()
        {
            AutoTerrainDesignationsMod.SetAccessAvoidOcean(s_accessAvoidOcean);
            AutoTerrainDesignationsMod.SetAccessAvoidBuildings(s_accessAvoidBuildings);
            AutoTerrainDesignationsMod.SetAccessHarvestDisruptedTrees(s_accessHarvestDisruptedTrees);
        }

        // Reserved for a future public diagnostics toggle. Keep command-scoped
        // tracing off by default without suppressing warnings or unrelated logs.
        private const bool CreateDesignationsVerboseLoggingEnabled = false;
        private static bool s_createDesignationsDebugContext;

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            if (s_createDesignationsDebugContext && !CreateDesignationsVerboseLoggingEnabled)
                return;

            s_log.Info(message);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogLegacyAccessDebug(string message)
        {
            if (Access.AccessDiagnostics.VerboseLoggingEnabled)
                s_log.Info(message);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogExperimentalAccessDebug(string message)
        {
            s_log.Info(message);
        }

        internal static ProductProto? GetSelectedOre(IAreaManagingTower tower)
        {
            if (tower == null) return null;
            if (!TryGetTowerEntityId(tower, out EntityId entityId)) return null;
            return s_selectedOrePerTower.TryGetValue(entityId, out var ore) ? ore : null;
        }

        internal static void SetSelectedOre(IAreaManagingTower tower, ProductProto? ore)
        {
            if (tower == null) return;
            if (!TryGetTowerEntityId(tower, out EntityId entityId)) return;
            ProductProto? previous = s_selectedOrePerTower.TryGetValue(entityId, out var existing) ? existing : null;
            if (!ReferenceEquals(previous, ore))
                GetOrCreateTowerSettings(tower).MarkMiningPlanDirty();
            if (ore == null)
                s_selectedOrePerTower.Remove(entityId);
            else
                s_selectedOrePerTower[entityId] = ore;
        }

        private static bool TryGetTowerEntityId(IAreaManagingTower tower, out EntityId entityId)
        {
            entityId = EntityId.Invalid;
            if (tower is IEntity entity && entity.Id.IsValid)
            {
                entityId = entity.Id;
                return true;
            }

            return false;
        }

        private static ATDTowerSettings GetOrCreateTowerSettings(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId entityId))
            {
                if (!s_towerSettingsByEntityId.TryGetValue(entityId, out ATDTowerSettings settings))
                {
                    settings = ATDTowerSettings.FromGlobalDefaults();
                    s_towerSettingsByEntityId[entityId] = settings;
                }

                return settings;
            }

            return ATDTowerSettings.FromGlobalDefaults();
        }

        // --- Per-tower settings accessors (used by API) ---

        internal static int GetTowerMaxHeightDiff(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).MaxHeightDiff;
        internal static void SetTowerMaxHeightDiff(IAreaManagingTower tower, int value) => GetOrCreateTowerSettings(tower).SetMaxHeightDiff(value);

        internal static int GetTowerRampWidth(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).RampWidth;
        internal static void SetTowerRampWidth(IAreaManagingTower tower, int value) => GetOrCreateTowerSettings(tower).SetRampWidth(value);
        internal static AccessVehicleClearanceMode GetTowerVehicleClearance(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).VehicleClearance;
        internal static void SetTowerVehicleClearance(IAreaManagingTower tower, AccessVehicleClearanceMode value) => GetOrCreateTowerSettings(tower).SetVehicleClearance(value);

        internal static int GetTowerMaxLayersToExcavate(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).MaxLayersToExcavate;
        internal static void SetTowerMaxLayersToExcavate(IAreaManagingTower tower, int value) => GetOrCreateTowerSettings(tower).SetMaxLayersToExcavate(value);

        internal static int? GetTowerMaxDepthToDigTo(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).MaxDepthToDigTo;
        internal static void SetTowerMaxDepthToDigTo(IAreaManagingTower tower, int? value) => GetOrCreateTowerSettings(tower).SetMaxDepthToDigTo(value);

        internal static int GetTowerOrePurityLevel(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).OrePurityLevel;
        internal static void SetTowerOrePurityLevel(IAreaManagingTower tower, int value) => GetOrCreateTowerSettings(tower).SetOrePurityLevel(value);

        internal static int GetTowerCorridorClearance(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).CorridorClearance;
        internal static void SetTowerCorridorClearance(IAreaManagingTower tower, int value) => GetOrCreateTowerSettings(tower).SetCorridorClearance(value);

        internal static bool IsTowerMiningPlanDirty(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).MiningPlanDirty;

        internal static bool IsTowerMiningPlanCurrent(IAreaManagingTower tower, string fingerprint)
        {
            ATDTowerSettings settings = GetOrCreateTowerSettings(tower);
            return !settings.MiningPlanDirty
                && string.Equals(settings.LastMiningPlanFingerprint, fingerprint, StringComparison.Ordinal);
        }

        internal static void MarkTowerMiningPlanDirty(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).MarkMiningPlanDirty();

        internal static void MarkAllMiningPlansDirty()
        {
            foreach (ATDTowerSettings settings in s_towerSettingsByEntityId.Values)
                settings.MarkMiningPlanDirty();
        }

        internal static void MarkTowerMiningPlanClean(IAreaManagingTower tower, string fingerprint) =>
            GetOrCreateTowerSettings(tower).MarkMiningPlanClean(fingerprint);

        private static void RegisterGeneratedAccesswayOrigins(IAreaManagingTower tower, IEnumerable<Tile2i> origins)
        {
            if (!TryGetTowerEntityId(tower, out EntityId entityId))
                return;

            if (!s_generatedAccesswayOriginsByTowerEntityId.TryGetValue(entityId, out HashSet<Tile2i> registered))
            {
                registered = new HashSet<Tile2i>();
                s_generatedAccesswayOriginsByTowerEntityId[entityId] = registered;
            }

            foreach (Tile2i origin in origins)
                registered.Add(origin);
        }

        private static bool IsRegisteredGeneratedAccesswayOrigin(IAreaManagingTower tower, Tile2i origin)
        {
            return TryGetTowerEntityId(tower, out EntityId entityId)
                && s_generatedAccesswayOriginsByTowerEntityId.TryGetValue(entityId, out HashSet<Tile2i> registered)
                && registered.Contains(origin);
        }

        internal static void RegisterGeneratedDesignationOrigin(IAreaManagingTower tower, Tile2i origin)
        {
            if (!TryGetTowerEntityId(tower, out EntityId entityId))
                return;

            if (!s_generatedDesignationOriginsByTowerEntityId.TryGetValue(entityId, out HashSet<Tile2i> registered))
            {
                registered = new HashSet<Tile2i>();
                s_generatedDesignationOriginsByTowerEntityId[entityId] = registered;
            }

            registered.Add(origin);
        }

        internal static void UnregisterGeneratedDesignationOrigin(IAreaManagingTower tower, Tile2i origin)
        {
            if (!TryGetTowerEntityId(tower, out EntityId entityId))
                return;

            if (s_generatedDesignationOriginsByTowerEntityId.TryGetValue(entityId, out HashSet<Tile2i> registered))
                registered.Remove(origin);
        }

        private static bool IsGeneratedDesignationOrigin(IAreaManagingTower tower, Tile2i origin)
        {
            return TryGetTowerEntityId(tower, out EntityId entityId)
                && s_generatedDesignationOriginsByTowerEntityId.TryGetValue(entityId, out HashSet<Tile2i> registered)
                && registered.Contains(origin);
        }

        private static IReadOnlyList<Tile2i> GetRegisteredGeneratedAccesswayOrigins(
            IAreaManagingTower tower)
        {
            if (!TryGetTowerEntityId(tower, out EntityId entityId)
                || !s_generatedAccesswayOriginsByTowerEntityId.TryGetValue(
                    entityId, out HashSet<Tile2i> registered))
                return Array.Empty<Tile2i>();
            return registered.ToArray();
        }

        private static IReadOnlyList<Tile2i> GetRegisteredGeneratedDesignationOrigins(
            IAreaManagingTower tower)
        {
            if (!TryGetTowerEntityId(tower, out EntityId entityId)
                || !s_generatedDesignationOriginsByTowerEntityId.TryGetValue(
                    entityId, out HashSet<Tile2i> registered))
                return Array.Empty<Tile2i>();
            return registered.ToArray();
        }

        private static void ClearRegisteredGeneratedDesignations(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId entityId))
                s_generatedDesignationOriginsByTowerEntityId.Remove(entityId);
        }

        private static void ClearRegisteredGeneratedAccessways(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId entityId))
                s_generatedAccesswayOriginsByTowerEntityId.Remove(entityId);
        }

        private static void RegisterGeneratedHarvestTreePositions(
            IAreaManagingTower tower, IEnumerable<TreeId> trees)
        {
            if (!TryGetTowerEntityId(tower, out EntityId entityId))
                return;
            if (!s_generatedHarvestTreePositionsByTowerEntityId.TryGetValue(
                    entityId, out HashSet<Tile2i> registered))
            {
                registered = new HashSet<Tile2i>();
                s_generatedHarvestTreePositionsByTowerEntityId[entityId] = registered;
            }
            foreach (TreeId tree in trees)
                registered.Add(tree.Position.AsFull);
        }

        private static IReadOnlyList<Tile2i> GetRegisteredGeneratedHarvestTreePositions(
            IAreaManagingTower tower)
        {
            if (!TryGetTowerEntityId(tower, out EntityId entityId)
                || !s_generatedHarvestTreePositionsByTowerEntityId.TryGetValue(
                    entityId, out HashSet<Tile2i> registered))
                return Array.Empty<Tile2i>();
            return registered.ToArray();
        }

        private static void ClearRegisteredGeneratedHarvestTrees(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId entityId))
                s_generatedHarvestTreePositionsByTowerEntityId.Remove(entityId);
        }

        internal static bool GetTowerAutoReleaseExcavatorsWhenIdle(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId entityId) && s_towerSettingsByEntityId.TryGetValue(entityId, out ATDTowerSettings settings))
                return settings.AutoReleaseExcavatorsWhenIdle;
            return AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle;
        }

        internal static void SetTowerAutoReleaseExcavatorsWhenIdle(IAreaManagingTower tower, bool value)
        {
            ATDTowerSettings settings = GetOrCreateTowerSettings(tower);
            settings.SetAutoReleaseExcavatorsWhenIdle(value);
            if (!value)
                TryRestoreIdleReleasedVehiclesForTower(tower, settings.AutoReleaseExcavatorsWhenIdle, settings.AutoReleaseTrucksWhenIdle);
        }

        internal static bool GetTowerAutoReleaseTrucksWhenIdle(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId entityId) && s_towerSettingsByEntityId.TryGetValue(entityId, out ATDTowerSettings settings))
                return settings.AutoReleaseTrucksWhenIdle;
            return AutoTerrainDesignationsMod.AutoReleaseTrucksWhenIdle;
        }

        internal static void SetTowerAutoReleaseTrucksWhenIdle(IAreaManagingTower tower, bool value)
        {
            ATDTowerSettings settings = GetOrCreateTowerSettings(tower);
            settings.SetAutoReleaseTrucksWhenIdle(value);
            if (!value)
                TryRestoreIdleReleasedVehiclesForTower(tower, settings.AutoReleaseExcavatorsWhenIdle, settings.AutoReleaseTrucksWhenIdle);
        }

        internal static bool GetTowerAutoReleaseWhenIdle(IAreaManagingTower tower)
        {
            return GetTowerAutoReleaseExcavatorsWhenIdle(tower) || GetTowerAutoReleaseTrucksWhenIdle(tower);
        }

        internal static void SetTowerAutoReleaseWhenIdle(IAreaManagingTower tower, bool value)
        {
            ATDTowerSettings settings = GetOrCreateTowerSettings(tower);
            settings.SetAutoReleaseWhenIdle(value);
            if (!value)
                TryRestoreIdleReleasedVehiclesForTower(tower, settings.AutoReleaseExcavatorsWhenIdle, settings.AutoReleaseTrucksWhenIdle);
        }

        internal static RampPlacementOutcome? GetTowerLastRampOutcome(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).LastRampOutcome;

        internal static void SetTowerLastRampOutcome(IAreaManagingTower tower, RampPlacementOutcome outcome)
        {
            GetOrCreateTowerSettings(tower).LastRampOutcome = outcome;
            UpdateTowerRampWarningNotification(tower, outcome);
        }

        internal static void ClearTowerLastRampOutcome(IAreaManagingTower tower)
        {
            GetOrCreateTowerSettings(tower).LastRampOutcome = null;
            ClearTowerRampWarningNotification(tower);
        }

        internal static bool GetTowerTerrainPanelCollapsed(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId id) && s_terrainPanelCollapsedByEntityId.TryGetValue(id, out bool v)) return v;
            return AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed;
        }

        internal static void SetTowerTerrainPanelCollapsed(IAreaManagingTower tower, bool collapsed)
        {
            if (TryGetTowerEntityId(tower, out EntityId id))
                s_terrainPanelCollapsedByEntityId[id] = collapsed;
        }

        internal static bool GetTowerOreCompositionPanelCollapsed(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId id) && s_orePanelCollapsedByEntityId.TryGetValue(id, out bool v)) return v;
            return AutoTerrainDesignationsMod.OreCompositionPanelCollapsed;
        }

        internal static void SetTowerOreCompositionPanelCollapsed(IAreaManagingTower tower, bool collapsed)
        {
            if (TryGetTowerEntityId(tower, out EntityId id))
                s_orePanelCollapsedByEntityId[id] = collapsed;
        }

        internal static bool GetTowerFarmingPanelCollapsed(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId id) && s_farmingPanelCollapsedByEntityId.TryGetValue(id, out bool v)) return v;
            return AutoTerrainDesignationsMod.FarmingPanelCollapsed;
        }

        internal static void SetTowerFarmingPanelCollapsed(IAreaManagingTower tower, bool collapsed)
        {
            if (TryGetTowerEntityId(tower, out EntityId id))
                s_farmingPanelCollapsedByEntityId[id] = collapsed;
        }

        internal static string FormatPanelStateDebug()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[ATD] Panel collapsed dict contents:");
            sb.AppendLine($"  Globals: terrain={AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed}, ore={AutoTerrainDesignationsMod.OreCompositionPanelCollapsed}, farming={AutoTerrainDesignationsMod.FarmingPanelCollapsed}");
            var allIds = new System.Collections.Generic.HashSet<EntityId>();
            foreach (var p in s_terrainPanelCollapsedByEntityId) allIds.Add(p.Key);
            foreach (var p in s_orePanelCollapsedByEntityId) allIds.Add(p.Key);
            foreach (var p in s_farmingPanelCollapsedByEntityId) allIds.Add(p.Key);
            if (allIds.Count == 0)
            {
                sb.Append("  (no per-tower panel state stored)");
            }
            else
            {
                foreach (EntityId id in allIds)
                {
                    string t = s_terrainPanelCollapsedByEntityId.TryGetValue(id, out bool tv) ? tv.ToString() : "-";
                    string o = s_orePanelCollapsedByEntityId.TryGetValue(id, out bool ov) ? ov.ToString() : "-";
                    string f = s_farmingPanelCollapsedByEntityId.TryGetValue(id, out bool fv) ? fv.ToString() : "-";
                    sb.AppendLine($"  entityId={id.Value}: terrain={t}, ore={o}, farming={f}");
                }
            }
            return sb.ToString();
        }

        internal static int CurrentWorldGeneration => s_worldGeneration;

        internal static bool IsWorldGenerationActive(int worldGeneration)
        {
            return worldGeneration == s_worldGeneration && s_desigManager != null;
        }

        internal static void ResetWorldRuntimeState()
        {
            s_worldGeneration++;
            s_latestCreateDesignationsRequestId++;
            s_cancelExperimentalAccessSearch = true;
            s_createDesignationsOperationActive = false;

            s_desigManager = null;
            s_miningProto = null;
            s_dumpingProto = null;
            s_levelingProto = null;
            s_bedrockTerrainMaterial = null;
            s_coroutineHost = null;
            s_protosDb = null;
            s_worldMapManager = null;
            s_entitiesManager = null;
            s_terrainPropsManager = null;
            s_treesManager = null;
            s_vehiclePathFindingManager = null;
            s_vehiclesManager = null;
            s_parkAndWaitJobFactory = null;
            s_excavatorPathFindingParams = null;
            s_standardVehiclePathFindingParams = null;
            s_inputScheduler = null;
            s_configSerializationContext = null;
            s_batchSize = BATCH_SIZE;
            ResetWorldPathfinderSettingsToDefaults();

            s_selectedOrePerTower.Clear();
            s_towerSettingsByEntityId.Clear();
            s_excavatorPriorityByTowerEntityId.Clear();
            s_terrainPanelCollapsedByEntityId.Clear();
            s_orePanelCollapsedByEntityId.Clear();
            s_farmingPanelCollapsedByEntityId.Clear();
            s_generatedDesignationOriginsByTowerEntityId.Clear();
            s_generatedAccesswayOriginsByTowerEntityId.Clear();
            s_generatedHarvestTreePositionsByTowerEntityId.Clear();
            s_startupTowerPrioritySyncCompleted = false;
            s_startupTowerPrioritySyncAttempts = 0;

            ResetTransientNotifications();
            ClearFarmingRuntimeState();
            ClearFarmPlacementAssistRuntimeState();
            ClearIdleVehicleReleaseState();
            ClearTowerReachabilityFlood();
        }

        public static void Initialize(
            ITerrainDesignationsManager desigManager,
            ProtosDb protosDb,
            IWorldMapManager worldMapManager,
            MonoBehaviour coroutineHost,
            IEntitiesManager entitiesManager,
            TerrainPropsManager terrainPropsManager,
            TreesManager treesManager,
            IVehiclePathFindingManager? vehiclePathFindingManager = null,
            ParkAndWaitJobFactory? parkAndWaitJobFactory = null,
            INotificationsManager? notificationsManager = null,
            IInputScheduler? inputScheduler = null,
            ConfigSerializationContext? configSerializationContext = null,
            IVehiclesManager? vehiclesManager = null)
        {
            ResetWorldRuntimeState();

            // Load defaults after logging is initialized so diagnostics are visible.
            LoadSettingsFromJson();
            ResetWorldPathfinderSettingsToDefaults();

            s_desigManager = desigManager as TerrainDesignationsManager;
            s_coroutineHost = coroutineHost;
            s_protosDb = protosDb;
            s_worldMapManager = worldMapManager as WorldMapManager;
            s_entitiesManager = entitiesManager;
            s_terrainPropsManager = terrainPropsManager;
            s_treesManager = treesManager;
            s_vehiclePathFindingManager = vehiclePathFindingManager;
            s_vehiclesManager = vehiclesManager;
            s_parkAndWaitJobFactory = parkAndWaitJobFactory;
            s_inputScheduler = inputScheduler;
            s_configSerializationContext = configSerializationContext;
            s_excavatorPathFindingParams = FindExcavatorPathFindingParams(protosDb);
            s_standardVehiclePathFindingParams = s_excavatorPathFindingParams;

            if (protosDb.TryGetProto(new Proto.ID("MiningDesignator"), out TerrainDesignationProto proto))
                s_miningProto = proto;
            else
                s_log.Warning("MiningDesignator proto not found");

            if (protosDb.TryGetProto(new Proto.ID("DumpingDesignator"), out TerrainDesignationProto dumpProto))
                s_dumpingProto = dumpProto;
            else
                s_log.Warning("DumpingDesignator proto not found");

            if (protosDb.TryGetProto(new Proto.ID("LevelDesignator"), out TerrainDesignationProto levelProto))
                s_levelingProto = levelProto;
            else
                s_log.Warning("LevelDesignator proto not found");

            if (protosDb.TryGetProto(new Proto.ID("Bedrock_Terrain"), out TerrainMaterialProto bedrockProto))
                s_bedrockTerrainMaterial = bedrockProto;
            else
                s_log.Warning("Bedrock terrain material not found");

            InitializeTransientNotifications(notificationsManager, protosDb);

            OreCompositionPanel.Initialize(s_desigManager, s_protosDb, s_bedrockTerrainMaterial);
            DesignationPanel.Initialize(s_protosDb);
        }

        public static void SetModRootDirectoryPath(string? modRootDirectoryPath)
        {
            s_modRootDirectoryPath = modRootDirectoryPath;
        }

        /// <summary>Returns true once Initialize has completed successfully.</summary>
        internal static bool IsInitialized => s_desigManager != null && s_coroutineHost != null;

        private static VehiclePathFindingParams FindExcavatorPathFindingParams(ProtosDb protosDb)
        {
            foreach (ExcavatorProto proto in protosDb.All<ExcavatorProto>())
                return proto.PathFindingParams;
            return VehiclePathFindingParams.DEFAULT;
        }

        private static VehiclePathFindingParams GetExcavatorPathFindingParamsForTower(
            IAreaManagingTower tower,
            out string source)
        {
            AccessVehicleClearanceMode requestedMode = GetTowerVehicleClearance(tower);
            if (requestedMode == AccessVehicleClearanceMode.T1
                || requestedMode == AccessVehicleClearanceMode.T2
                || requestedMode == AccessVehicleClearanceMode.T3)
            {
                string tierToken = requestedMode.ToString();
                if (s_protosDb != null)
                {
                    foreach (ExcavatorProto proto in s_protosDb.All<ExcavatorProto>()
                        .OrderBy(candidate => candidate.Id.Value, StringComparer.Ordinal))
                    {
                        if (proto.Id.Value.IndexOf(tierToken, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            int explicitClearance = GetVehicleClearance(proto.PathFindingParams);
                            source = $"explicit{tierToken}:{proto.Id}:clearance={explicitClearance}";
                            return proto.PathFindingParams;
                        }
                    }
                }
            }
            var towerCandidates = new List<VehiclePathabilityCandidate>();
            if (tower is MineTower mineTower)
            {
                var excavators = mineTower.AllAssignedExcavators;
                if (excavators != null)
                {
                    foreach (Excavator excavator in excavators)
                        if (excavator != null && !excavator.IsDestroyed)
                            towerCandidates.Add(new VehiclePathabilityCandidate(
                                excavator.PathFindingParams,
                                $"assignedExcavator:{excavator.Prototype.Id}",
                                GetVehicleClearance(excavator.PathFindingParams),
                                excavator.Prototype.Id.ToString()));
                }

                if (TryGetTowerEntityId(tower, out EntityId towerId))
                {
                    if (s_idleReleasedVehiclesByTower.TryGetValue(towerId, out List<Vehicle> released))
                        foreach (Vehicle vehicle in released)
                            if (vehicle is Excavator excavator && !excavator.IsDestroyed)
                                towerCandidates.Add(new VehiclePathabilityCandidate(
                                    excavator.PathFindingParams,
                                    $"releasedExcavator:{excavator.Prototype.Id}",
                                    GetVehicleClearance(excavator.PathFindingParams),
                                    excavator.Prototype.Id.ToString()));

                    if (s_protosDb != null)
                        foreach (DynamicEntityProto.ID protoId in PendingVehicleAllocations.GetQueuedProtoIdsForTower(towerId))
                            if (s_protosDb.TryGetProto(protoId, out ExcavatorProto proto))
                                towerCandidates.Add(new VehiclePathabilityCandidate(
                                    proto.PathFindingParams,
                                    $"preAssignedExcavator:{proto.Id}",
                                    GetVehicleClearance(proto.PathFindingParams),
                                    proto.Id.ToString()));
                }
            }

            if (TrySelectPathabilityCandidate(towerCandidates, int.MaxValue, out VehiclePathabilityCandidate towerSelected))
            {
                source = $"{towerSelected.Source}:clearance={towerSelected.Clearance}";
                return towerSelected.Params;
            }

            if (s_vehiclesManager != null)
            {
                var fleetCandidates = new List<VehiclePathabilityCandidate>();
                foreach (Excavator excavator in s_vehiclesManager.Excavators)
                    if (excavator != null && !excavator.IsDestroyed)
                        fleetCandidates.Add(new VehiclePathabilityCandidate(
                            excavator.PathFindingParams,
                            $"fleetExcavator:{excavator.Prototype.Id}",
                            GetVehicleClearance(excavator.PathFindingParams),
                            excavator.Prototype.Id.ToString()));

                if (TrySelectPathabilityCandidate(fleetCandidates, int.MaxValue, out VehiclePathabilityCandidate selected))
                {
                    source = $"{selected.Source}:clearance={selected.Clearance}";
                    return selected.Params;
                }
            }

            source = "autoOff:noExcavatorsOnMap";
            return s_excavatorPathFindingParams ?? VehiclePathFindingParams.DEFAULT;
        }

        internal static bool ShouldGenerateAccessways(IAreaManagingTower tower)
        {
            AccessVehicleClearanceMode mode = GetTowerVehicleClearance(tower);
            if (mode == AccessVehicleClearanceMode.Off) return false;
            if (mode != AccessVehicleClearanceMode.Auto) return true;
            GetExcavatorPathFindingParamsForTower(tower, out string source);
            return !source.StartsWith("autoOff:", StringComparison.Ordinal);
        }

        private readonly struct VehiclePathabilityCandidate
        {
            public VehiclePathFindingParams Params { get; }
            public string Source { get; }
            public int Clearance { get; }
            public int HeightClearance { get; }
            public string SortKey { get; }

            public VehiclePathabilityCandidate(
                VehiclePathFindingParams pathFindingParams,
                string source,
                int clearance,
                string sortKey)
            {
                Params = pathFindingParams;
                Source = source;
                Clearance = clearance;
                HeightClearance = pathFindingParams.MinHeightClearance.Value;
                SortKey = sortKey;
            }
        }

        private static bool TrySelectPathabilityCandidate(
            IEnumerable<VehiclePathabilityCandidate> candidates,
            int targetClearance,
            out VehiclePathabilityCandidate selected)
        {
            selected = default;
            bool foundAtOrBelowTarget = false;
            VehiclePathabilityCandidate fallbackAboveTarget = default;
            bool foundAboveTarget = false;
            foreach (VehiclePathabilityCandidate candidate in candidates)
            {
                if (candidate.Clearance > targetClearance)
                {
                    if (!foundAboveTarget
                        || candidate.Clearance < fallbackAboveTarget.Clearance
                        || (candidate.Clearance == fallbackAboveTarget.Clearance
                            && (candidate.HeightClearance > fallbackAboveTarget.HeightClearance
                                || (candidate.HeightClearance == fallbackAboveTarget.HeightClearance
                                    && string.CompareOrdinal(candidate.SortKey, fallbackAboveTarget.SortKey) < 0))))
                    {
                        fallbackAboveTarget = candidate;
                        foundAboveTarget = true;
                    }
                    continue;
                }

                if (!foundAtOrBelowTarget
                    || candidate.Clearance > selected.Clearance
                    || (candidate.Clearance == selected.Clearance
                        && (candidate.HeightClearance > selected.HeightClearance
                            || (candidate.HeightClearance == selected.HeightClearance
                                && string.CompareOrdinal(candidate.SortKey, selected.SortKey) < 0))))
                {
                    selected = candidate;
                    foundAtOrBelowTarget = true;
                }
            }
            if (foundAtOrBelowTarget)
                return true;

            if (foundAboveTarget)
            {
                selected = fallbackAboveTarget;
                return true;
            }

            return false;
        }

        private static int GetVehicleClearance(VehiclePathFindingParams pathFindingParams)
        {
            var mask = pathFindingParams.PathabilityQueryMask;
            return Math.Max(1, ClearancePathabilityProvider.ExtractClearanceFromMask(ref mask).Value);
        }

    }
}

// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Ore sorting plants do not implement the vanilla output-assignment interfaces.
// This module provides in-memory adapters for RegisteredOutputBuffer (IEntityAssignedAsOutput
// and IEntityEnforcingAssignedVehicles) backed by ATD-owned sidecar state, enabling vanilla
// VehicleBuffersRegistry and TerrainDumpingManager to handle routing, priority, and assigned trucks natively.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Collections.ReadonlyCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Forestry;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.OreSorting;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Economy;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Validators;
using Mafi.Core.GameLoop;
using Mafi.Core.Products;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Commands;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Core.Vehicles.Trucks.JobProviders;
using Mafi.Localization;
using Mafi.Serialization;
using Mafi.Core.Syncers;
using Mafi.Unity;
using Mafi.Unity.Entities;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.Ui.Library.Inspectors;
using Mafi.Unity.UiStatic;
using Mafi.Unity.UiStatic.Cursors;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using CoI.AutoHelpers.Persistence;
using UnityEngine;
using EntityId = Mafi.Core.EntityId;

namespace AutoTerrainDesignations;

public static partial class AutoDepthDesignation
{
    internal const string OreSorterExportConfigKey = "atdOreSorterExports";

    private const int OreSorterExportConfigSchemaVersion = 1;
    private const int OreSorterPriorityMin = 0;
    private const int OreSorterPriorityMax = 14;

    private static readonly object s_oreSorterLock = new object();

    private static readonly Dictionary<EntityId, OreSorterExportState> s_oreSorterExportStates =
        new Dictionary<EntityId, OreSorterExportState>();

    private static readonly ConditionalWeakTable<object, OreSorterExportPanelMarker> s_oreSorterExportPanels =
        new ConditionalWeakTable<object, OreSorterExportPanelMarker>();

    private static IVehicleBuffersRegistry? s_oreSorterBuffersRegistry;
    private static int s_oreSorterExportUiVersion;

    private sealed class OreSorterExportPanelMarker
    {
    }

    private sealed class OreSorterExportState
    {
        public int? ExportPriority;
        public bool EnforceAssignedTrucks;
        public readonly HashSet<EntityId> AssignedTruckIds = new HashSet<EntityId>();
        public readonly List<OreSorterExportRoute> Routes = new List<OreSorterExportRoute>();

        public bool HasPersistedState => ExportPriority.HasValue
            || EnforceAssignedTrucks
            || AssignedTruckIds.Count > 0
            || Routes.Count > 0;
    }

    private sealed class OreSorterExportRoute
    {
        public EntityId DestinationId;
        public string? ProductId;
    }

    private sealed class OreSortingPlantOutputAssigneeAdapter : IEntityAssignedAsOutput
    {
        private readonly OreSortingPlant m_sorter;
        private readonly ProductProto? m_product;

        public OreSortingPlantOutputAssigneeAdapter(OreSortingPlant sorter, ProductProto? product = null)
        {
            m_sorter = sorter;
            m_product = product;
        }

        public OreSortingPlant Sorter => m_sorter;
        public ProductProto? Product => m_product;

        // IEntity
        public EntityId Id => m_sorter.Id;
        public LayoutEntityProto Prototype => m_sorter.Prototype;
        StaticEntityProto IStaticEntity.Prototype => m_sorter.Prototype;
        EntityProto IEntity.Prototype => m_sorter.Prototype;
        public EntityContext Context => m_sorter.Context;
        public bool IsEnabled => m_sorter.IsEnabled;
        public bool IsPaused => m_sorter.IsPaused;
        public bool CanBePaused => m_sorter.CanBePaused;
        public bool IsDestroyed => m_sorter.IsDestroyed;
        public void UpdateIsEnabled() => m_sorter.UpdateIsEnabled();
        public void UpdateIsBroken() => m_sorter.UpdateIsBroken();
        public void UpdateProperties() => m_sorter.UpdateProperties();
        public void SetPaused(bool isPaused) => m_sorter.SetPaused(isPaused);
        public void AddObserver(IEntityObserver observer) => m_sorter.AddObserver(observer);
        public void RemoveObserver(IEntityObserver observer) => m_sorter.RemoveObserver(observer);

        // IObjectWithTitle
        public LocStrFormatted DefaultTitle => m_sorter.DefaultTitle;

        // IEntityWithPosition
        public Tile2f Position2f => m_sorter.Position2f;
        public Tile3f Position3f => m_sorter.Position3f;

        // IAreaSelectableEntity
        public bool IsSelected(RectangleTerrainArea2i area) => m_sorter.IsSelected(area);

        // IRenderedEntity
        private ulong m_rendererData;
        public ulong RendererData
        {
            get => m_sorter is IRenderedEntity re ? re.RendererData : m_rendererData;
            set
            {
                if (m_sorter is IRenderedEntity re)
                    re.RendererData = value;
                else
                    m_rendererData = value;
            }
        }

        // IStaticEntity
        public Tile3i CenterTile => m_sorter.CenterTile;
        public ImmutableArray<OccupiedTileRelative> OccupiedTiles => m_sorter.OccupiedTiles;
        public ImmutableArray<OccupiedVertexRelative> OccupiedVertices => m_sorter.OccupiedVertices;
        public LayoutTileConstraint OccupiedVerticesCombinedConstraint => m_sorter.OccupiedVerticesCombinedConstraint;
        public ImmutableArray<KeyValuePair<Tile2i, HeightTilesF>> VehicleSurfaceHeights => m_sorter.VehicleSurfaceHeights;
        public ConstructionState ConstructionState => m_sorter.ConstructionState;
        public Option<IEntityConstructionProgress> ConstructionProgress => m_sorter.ConstructionProgress;
        public bool IsConstructed => m_sorter.IsConstructed;
        public StaticEntityPfTargetTiles PfTargetTiles => m_sorter.PfTargetTiles;
        public bool AlwaysUseCustomPfTargetTiles => m_sorter.AlwaysUseCustomPfTargetTiles;
        public bool AreConstructionCubesDisabled => m_sorter.AreConstructionCubesDisabled;
        public bool DoNotAdjustTerrainDuringConstruction => m_sorter.DoNotAdjustTerrainDuringConstruction;
        public ulong ZoneMask => m_sorter.ZoneMask;
        public AssetValue GetConstructionCost() => m_sorter.GetConstructionCost();
        public bool GetCustomPfTargetTiles(int retryNumber, Lyst<Tile2i> outTiles) => m_sorter.GetCustomPfTargetTiles(retryNumber, outTiles);
        public ImmutableArray<IProductBufferReadOnly> GetConstructionBuffers() => m_sorter.GetConstructionBuffers();
        public EntityValidationResult CanStartDeconstruction() => m_sorter.CanStartDeconstruction();
        public bool CanMoveFromPendingDeconstruction() => m_sorter.CanMoveFromPendingDeconstruction();
        public void StartDeconstructionIfCan() => m_sorter.StartDeconstructionIfCan();
        public void AbortDeconstruction() => m_sorter.AbortDeconstruction();
        public void SetConstructionState(ConstructionState state) => m_sorter.SetConstructionState(state);
        public ImmutableArray<ConstrCubeSpec> GetConstructionCubesSpec(out int totalCubesVolume) => m_sorter.GetConstructionCubesSpec(out totalCubesVolume);
        public void NotifyUnevenTerrain(Mafi.Collections.IReadOnlySet<int> groundVerticesViolatingConstraints, int newIndex, bool wasAdded, out bool canCollapse) => m_sorter.NotifyUnevenTerrain(groundVerticesViolatingConstraints, newIndex, wasAdded, out canCollapse);
        public bool TryCollapseOnUnevenTerrain(Mafi.Collections.IReadOnlySet<int> groundVerticesViolatingConstraints, EntityCollapseHelper collapseHelper) => m_sorter.TryCollapseOnUnevenTerrain(groundVerticesViolatingConstraints, collapseHelper);

        // ILayoutEntity
        public TileTransform Transform => m_sorter.Transform;
        public Tile3f GetCenter() => m_sorter.GetCenter();

        // IEntityAssignedAsOutput
        public IReadOnlySet<IEntityAssignedAsInput> AssignedInputs
        {
            get
            {
                var set = new Set<IEntityAssignedAsInput>();
                if (s_entitiesManager == null || !TryGetOreSorterExportState(m_sorter.Id, out OreSorterExportState state))
                    return set;

                lock (s_oreSorterLock)
                {
                    foreach (OreSorterExportRoute route in state.Routes)
                    {
                        if (m_product != null)
                        {
                            if (!IsOreSorterExportRouteForProduct(m_sorter, route, m_product))
                                continue;

                            if (s_entitiesManager.TryGetEntity<IEntityAssignedAsInput>(route.DestinationId, out IEntityAssignedAsInput destination)
                                && !destination.IsDestroyed)
                            {
                                if (destination is MineTower tower)
                                {
                                    if (m_product.DumpableProduct.HasValue && tower.CanAcceptDumpOf(m_product))
                                        set.Add(destination);
                                }
                                else
                                {
                                    if (s_oreSorterBuffersRegistry?.TryGetInputBuffer(destination, m_product).HasValue == true)
                                        set.Add(destination);
                                }
                            }
                        }
                        else
                        {
                            if (s_entitiesManager.TryGetEntity<IEntityAssignedAsInput>(route.DestinationId, out IEntityAssignedAsInput destination)
                                && !destination.IsDestroyed)
                            {
                                set.Add(destination);
                            }
                        }
                    }
                }
                return set;
            }
        }

        public bool CanBeAssignedWithInput(IEntityAssignedAsInput entity)
            => CanOreSorterExportTo(m_sorter, entity);

        public void AssignStaticInputEntity(IEntityAssignedAsInput entity)
            => AddOreSorterExportRoute(m_sorter, entity.Id, m_product);

        public void UnassignStaticInputEntity(IEntityAssignedAsInput entity)
            => RemoveOreSorterExportRoute(m_sorter, entity.Id, m_product);

        public override bool Equals(object? obj)
        {
            if (obj is OreSortingPlantOutputAssigneeAdapter other)
                return m_sorter.Id == other.m_sorter.Id && Equals(m_product, other.m_product);
            if (obj is OreSortingPlant sorter)
                return m_sorter.Id == sorter.Id;
            return false;
        }

        public override int GetHashCode() => m_sorter.Id.GetHashCode();
    }

    private sealed class OreSortingPlantVehiclesEnforcerAdapter : IEntityEnforcingAssignedVehicles, IEntityAssignedWithVehicles, IEntityWithPosition, IRenderedEntity, IAreaSelectableEntity, IObjectWithTitle, IEntity, IIsSafeAsHashKey
    {
        private readonly OreSortingPlant m_sorter;
        private readonly Lyst<Vehicle> m_vehiclesCache = new Lyst<Vehicle>();

        public OreSortingPlantVehiclesEnforcerAdapter(OreSortingPlant sorter)
        {
            m_sorter = sorter;
        }

        public OreSortingPlant Sorter => m_sorter;

        // IEntity
        public EntityId Id => m_sorter.Id;
        public LayoutEntityProto Prototype => m_sorter.Prototype;
        EntityProto IEntity.Prototype => m_sorter.Prototype;
        public EntityContext Context => m_sorter.Context;
        public bool IsEnabled => m_sorter.IsEnabled;
        public bool IsPaused => m_sorter.IsPaused;
        public bool CanBePaused => m_sorter.CanBePaused;
        public bool IsDestroyed => m_sorter.IsDestroyed;
        public void UpdateIsEnabled() => m_sorter.UpdateIsEnabled();
        public void UpdateIsBroken() => m_sorter.UpdateIsBroken();
        public void UpdateProperties() => m_sorter.UpdateProperties();
        public void SetPaused(bool isPaused) => m_sorter.SetPaused(isPaused);
        public void AddObserver(IEntityObserver observer) => m_sorter.AddObserver(observer);
        public void RemoveObserver(IEntityObserver observer) => m_sorter.RemoveObserver(observer);

        // IObjectWithTitle
        public LocStrFormatted DefaultTitle => m_sorter.DefaultTitle;

        // IEntityWithPosition
        public Tile2f Position2f => m_sorter.Position2f;
        public Tile3f Position3f => m_sorter.Position3f;

        // IAreaSelectableEntity
        public bool IsSelected(RectangleTerrainArea2i area) => m_sorter.IsSelected(area);

        // IRenderedEntity
        private ulong m_rendererData;
        public ulong RendererData
        {
            get => m_sorter is IRenderedEntity re ? re.RendererData : m_rendererData;
            set
            {
                if (m_sorter is IRenderedEntity re)
                    re.RendererData = value;
                else
                    m_rendererData = value;
            }
        }

        // IEntityAssignedWithVehicles
        public ulong ZoneMask => m_sorter.ZoneMask;

        public IIndexable<Vehicle> AllVehicles
        {
            get
            {
                m_vehiclesCache.Clear();
                m_vehiclesCache.AddRange(GetOreSorterAssignedVehicles(m_sorter));
                return m_vehiclesCache;
            }
        }

        public bool CanVehicleBeAssigned(DynamicEntityProto vehicle)
            => vehicle is TruckProto && m_sorter != null && !m_sorter.IsDestroyed;

        public void AssignVehicle(Vehicle vehicle, bool doNotCancelJobs = false)
            => AssignOreSorterTruck(m_sorter, vehicle, doNotCancelJobs);

        public void UnassignVehicle(Vehicle vehicle, bool cancelJobs = true)
            => UnassignOreSorterTruck(m_sorter, vehicle, cancelJobs);

        // IEntityEnforcingAssignedVehicles
        public bool AreOnlyAssignedVehiclesAllowed => GetOreSorterAssignedTruckEnforcement(m_sorter);

        public void SetEnforceAssignedVehicles(bool isEnforceOn)
            => SetOreSorterAssignedTruckEnforcement(m_sorter, isEnforceOn);

        public override bool Equals(object? obj)
        {
            if (obj is OreSortingPlantVehiclesEnforcerAdapter other)
                return m_sorter.Id == other.m_sorter.Id;
            if (obj is OreSortingPlant sorter)
                return m_sorter.Id == sorter.Id;
            return false;
        }

        public override int GetHashCode() => m_sorter.Id.GetHashCode();
    }

    internal static void ApplyOreSorterExportPatches(Harmony harmony)
    {
        try
        {
            var assembly = typeof(Mafi.Unity.Entities.EntityMb).Assembly;
            var inspectorType = assembly.GetType("Mafi.Unity.Ui.Inspectors.OreSortingPlantInspector");
            var inspectorCtor = inspectorType?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault();
            if (inspectorCtor != null)
            {
                harmony.Patch(inspectorCtor,
                    postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(OreSorterInspectorCtorPostfix)));
            }
            else
            {
                Log.Warning("[ATD] Ore sorting plant inspector constructor not found.");
            }

            MethodInfo? storageAssignedOutputs = typeof(Storage).GetProperty(
                nameof(Storage.AssignedOutputs),
                BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
            if (storageAssignedOutputs != null)
            {
                harmony.Patch(storageAssignedOutputs,
                    postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(StorageAssignedOutputsGetterPostfix)));
            }

            MethodInfo? mineTowerAssignedOutputs = typeof(MineTower).GetProperty(
                nameof(MineTower.AssignedOutputs),
                BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
            if (mineTowerAssignedOutputs != null)
            {
                harmony.Patch(mineTowerAssignedOutputs,
                    postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(MineTowerAssignedOutputsGetterPostfix)));
            }

            MethodInfo? forestryTowerAssignedOutputs = typeof(ForestryTower).GetProperty(
                nameof(ForestryTower.AssignedOutputs),
                BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
            if (forestryTowerAssignedOutputs != null)
            {
                harmony.Patch(forestryTowerAssignedOutputs,
                    postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(ForestryTowerAssignedOutputsGetterPostfix)));
            }

            MethodInfo? updateHighlight = typeof(AssignedBuildingsHighlighter).GetMethod(
                nameof(AssignedBuildingsHighlighter.UpdateHighlightOfAssignedEntities),
                BindingFlags.Instance | BindingFlags.Public);
            if (updateHighlight != null)
            {
                harmony.Patch(updateHighlight,
                    prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(AssignedBuildingsHighlighterUpdateHighlightPrefix)));
            }

            MethodInfo? unassignStaticEntity = typeof(EntitiesCommandsProcessor).GetMethod(
                nameof(EntitiesCommandsProcessor.Invoke),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(UnassignStaticEntityCmd) },
                null);
            if (unassignStaticEntity != null)
            {
                harmony.Patch(unassignStaticEntity,
                    prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(UnassignStaticEntityCmdPrefix)));
            }

            MethodInfo? assignStaticEntity = typeof(EntitiesCommandsProcessor).GetMethod(
                nameof(EntitiesCommandsProcessor.Invoke),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(AssignStaticEntityCmd) },
                null);
            if (assignStaticEntity != null)
            {
                harmony.Patch(assignStaticEntity,
                    prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(AssignStaticEntityCmdPrefix)));
            }

            MethodInfo? buildingsAssignerInputUpdate = typeof(BuildingsAssigner).GetMethod(
                nameof(BuildingsAssigner.InputUpdate),
                BindingFlags.Instance | BindingFlags.Public);
            if (buildingsAssignerInputUpdate != null)
            {
                harmony.Patch(buildingsAssignerInputUpdate,
                    prefix: new HarmonyMethod(
                        typeof(AutoDepthDesignation),
                        nameof(BuildingsAssignerInputUpdatePrefix)));
            }

            MethodInfo? buildingsAssignerRenderUpdate = typeof(BuildingsAssigner).GetMethod(
                "renderUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (buildingsAssignerRenderUpdate != null)
            {
                harmony.Patch(buildingsAssignerRenderUpdate,
                    prefix: new HarmonyMethod(
                        typeof(AutoDepthDesignation),
                        nameof(BuildingsAssignerRenderUpdatePrefix)));
            }

            MethodInfo? outputBufferInitAll = typeof(RegisteredOutputBuffer).GetMethod(
                "initAll",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (outputBufferInitAll != null)
            {
                harmony.Patch(outputBufferInitAll,
                    postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(RegisteredOutputBufferInitAllPostfix)));
            }

            MethodInfo? refreshPriorities = typeof(RegisteredOutputBuffer).GetMethod(
                nameof(RegisteredOutputBuffer.RefreshPriorities),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(bool) },
                null);
            if (refreshPriorities != null)
            {
                harmony.Patch(refreshPriorities,
                    postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(RegisteredOutputBufferRefreshPostfix)));
            }

            MethodInfo? outputCanBeServedBy = typeof(RegisteredOutputBuffer).GetMethod(
                nameof(RegisteredOutputBuffer.CanBeServedBy),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(Vehicle), typeof(bool) },
                null);
            if (outputCanBeServedBy != null)
            {
                harmony.Patch(outputCanBeServedBy,
                    prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(RegisteredOutputBufferCanBeServedByPrefix)));
            }

            MethodInfo? inputCanBeServedBy = typeof(RegisteredInputBuffer).GetMethod(
                nameof(RegisteredInputBuffer.CanBeServedBy),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(Vehicle), typeof(bool) },
                null);
            if (inputCanBeServedBy != null)
            {
                harmony.Patch(inputCanBeServedBy,
                    prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(RegisteredInputBufferCanBeServedByPrefix)));
            }


            MethodInfo? assignVehicle = typeof(VehicleCommandsProcessor).GetMethod(
                nameof(VehicleCommandsProcessor.Invoke),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(AssignVehicleTypeToEntityCmd) },
                null);
            if (assignVehicle != null)
            {
                harmony.Patch(assignVehicle,
                    prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(AssignSorterVehiclePrefix)));
            }

            MethodInfo? unassignVehicle = typeof(VehicleCommandsProcessor).GetMethod(
                nameof(VehicleCommandsProcessor.Invoke),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(UnassignVehicleFromEntityCmd) },
                null);
            if (unassignVehicle != null)
            {
                harmony.Patch(unassignVehicle,
                    prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(UnassignSorterVehiclePrefix)));
            }

            MethodInfo? vehicleCanBeAssigned = typeof(Vehicle).GetProperty(
                nameof(Vehicle.CanBeAssigned),
                BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
            if (vehicleCanBeAssigned != null)
            {
                harmony.Patch(vehicleCanBeAssigned,
                    postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(VehicleCanBeAssignedPostfix)));
            }

            MethodInfo? vehicleAssignedTo = typeof(Vehicle).GetProperty(
                nameof(Vehicle.AssignedTo),
                BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
            if (vehicleAssignedTo != null)
            {
                harmony.Patch(vehicleAssignedTo,
                    postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(VehicleAssignedToPostfix)));
            }

            MethodInfo? vehicleSerializeData = typeof(Vehicle).GetMethod(
                "SerializeData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (vehicleSerializeData != null)
            {
                harmony.Patch(vehicleSerializeData,
                    prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(VehicleSerializeDataPrefix)),
                    finalizer: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(VehicleSerializeDataFinalizer)));
            }

            MethodInfo? unassignVehicleCmd = typeof(VehicleCommandsProcessor).GetMethod(
                nameof(VehicleCommandsProcessor.Invoke),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(UnassignVehicleCmd) },
                null);
            if (unassignVehicleCmd != null)
            {
                harmony.Patch(unassignVehicleCmd,
                    prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(UnassignVehicleCmdPrefix)));
            }

            MethodInfo? assignVehicleToEntityCmd = typeof(VehicleCommandsProcessor).GetMethod(
                nameof(VehicleCommandsProcessor.Invoke),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(AssignVehicleToEntityCmd) },
                null);
            if (assignVehicleToEntityCmd != null)
            {
                harmony.Patch(assignVehicleToEntityCmd,
                    prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(AssignVehicleToEntityCmdPrefix)));
            }

            MethodInfo? defaultTruckTryGetJob = typeof(DefaultTruckJobProvider).GetMethod(
                nameof(DefaultTruckJobProvider.TryGetJobFor),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(Truck) },
                null);
            if (defaultTruckTryGetJob != null)
            {
                harmony.Patch(defaultTruckTryGetJob,
                    postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(DefaultTruckJobProviderTryGetJobForPostfix)));
            }

            LogInfo("Ore sorting plant export policy patches applied.");
        }
        catch (Exception ex)
        {
            Log.Warning($"[ATD] Failed to apply ore sorting plant export patches: {ex}");
        }
    }

    internal static void ConfigureOreSorterExports(IVehicleBuffersRegistry? buffersRegistry)
    {
        s_oreSorterBuffersRegistry = buffersRegistry;
        AttachAdaptersAndRefreshAllOreSorters();
    }

    internal static void ResetOreSorterExportRuntime()
    {
        lock (s_oreSorterLock)
        {
            s_oreSorterExportStates.Clear();
        }
        s_oreSorterBuffersRegistry = null;
        s_oreSorterExportUiVersion++;
    }

    internal static void OnOreSorterExportEntityRemoved(IEntity entity)
    {
        bool changed = false;
        lock (s_oreSorterLock)
        {
            if (entity is OreSortingPlant)
            {
                changed = s_oreSorterExportStates.Remove(entity.Id);
            }

            if (entity is Truck)
            {
                foreach (OreSorterExportState state in s_oreSorterExportStates.Values)
                    changed |= state.AssignedTruckIds.Remove(entity.Id);
            }

            foreach (OreSorterExportState state in s_oreSorterExportStates.Values)
            {
                int routeCount = state.Routes.Count;
                state.Routes.RemoveAll(route => route.DestinationId == entity.Id);
                changed |= routeCount != state.Routes.Count;
            }
        }

        if (changed)
            s_oreSorterExportUiVersion++;
    }

    internal static void LoadOreSorterExportsFromJsonStore(IModStateJsonStore store)
    {
        string json = store.LoadJson();
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            object parsed = new JsonParser().Parse(new StringReader(json));
            if (!(parsed is Dict<string, object> root)
                || !TryGetOreSorterInt(root, "schemaVersion", out int schemaVersion)
                || schemaVersion != OreSorterExportConfigSchemaVersion
                || !root.TryGetValue("sorters", out object rawSorters)
                || !(rawSorters is object[] sorters))
            {
                Log.Warning("[ATD] Ore sorter export state has an unsupported schema or shape; skipping it.");
                return;
            }

            lock (s_oreSorterLock)
            {
                s_oreSorterExportStates.Clear();
                foreach (object rawSorter in sorters)
                {
                    if (!(rawSorter is Dict<string, object> sorter)
                        || !TryGetOreSorterInt(sorter, "entityId", out int entityIdValue)
                        || entityIdValue <= 0)
                        continue;

                    var state = new OreSorterExportState();
                    if (TryGetOreSorterInt(sorter, "exportPriority", out int priority))
                        state.ExportPriority = ClampPriority(priority);
                    if (TryGetOreSorterBool(sorter, "enforceAssignedTrucks", out bool enforce))
                        state.EnforceAssignedTrucks = enforce;

                    if (sorter.TryGetValue("assignedTrucks", out object rawTrucks)
                        && rawTrucks is object[] trucks)
                    {
                        foreach (object rawTruck in trucks)
                        {
                            if (TryGetIntValue(rawTruck, out int truckId) && truckId > 0)
                                state.AssignedTruckIds.Add(new EntityId(truckId));
                        }
                    }

                    if (sorter.TryGetValue("routes", out object rawRoutes)
                        && rawRoutes is object[] routes)
                    {
                        foreach (object rawRoute in routes)
                        {
                            if (!(rawRoute is Dict<string, object> route)
                                || !TryGetOreSorterInt(route, "destinationId", out int destinationId)
                                || destinationId <= 0)
                                continue;

                            string? productId = null;
                            if (route.TryGetValue("productId", out object rawProductId)
                                && rawProductId is string productIdValue
                                && !string.IsNullOrWhiteSpace(productIdValue))
                            {
                                productId = productIdValue;
                            }

                            state.Routes.Add(new OreSorterExportRoute
                            {
                                DestinationId = new EntityId(destinationId),
                                ProductId = productId
                            });
                        }
                    }

                    s_oreSorterExportStates[new EntityId(entityIdValue)] = state;
                }
            }

            s_oreSorterExportUiVersion++;
            AttachAdaptersAndRefreshAllOreSorters();
            LogInfo($"Persistence: loaded {s_oreSorterExportStates.Count} ore sorter export record(s).");
        }
        catch (Exception ex)
        {
            Log.Warning($"[ATD] Failed to load ore sorter export state: {ex.Message}");
        }
    }

    internal static void SaveOreSorterExportsToJsonStore(IModStateJsonStore store)
    {
        var builder = new StringBuilder();
        builder.Append("{\"schemaVersion\":");
        builder.Append(OreSorterExportConfigSchemaVersion.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"sorters\":[");

        bool firstSorter = true;
        lock (s_oreSorterLock)
        {
            foreach (KeyValuePair<EntityId, OreSorterExportState> entry in s_oreSorterExportStates)
            {
                OreSorterExportState state = entry.Value;
                if (!entry.Key.IsValid || !state.HasPersistedState)
                    continue;

                if (!firstSorter)
                    builder.Append(',');
                firstSorter = false;

                builder.Append("{\"entityId\":");
                builder.Append(entry.Key.Value.ToString(CultureInfo.InvariantCulture));
                if (state.ExportPriority.HasValue)
                {
                    builder.Append(",\"exportPriority\":");
                    builder.Append(state.ExportPriority.Value.ToString(CultureInfo.InvariantCulture));
                }
                if (state.EnforceAssignedTrucks)
                    builder.Append(",\"enforceAssignedTrucks\":true");

                builder.Append(",\"assignedTrucks\":[");
                bool firstTruck = true;
                foreach (EntityId truckId in state.AssignedTruckIds.OrderBy(id => id.Value))
                {
                    if (!firstTruck)
                        builder.Append(',');
                    firstTruck = false;
                    builder.Append(truckId.Value.ToString(CultureInfo.InvariantCulture));
                }
                builder.Append(']');

                builder.Append(",\"routes\":[");
                bool firstRoute = true;
                foreach (OreSorterExportRoute route in state.Routes)
                {
                    if (!route.DestinationId.IsValid)
                        continue;
                    if (!firstRoute)
                        builder.Append(',');
                    firstRoute = false;
                    builder.Append("{\"destinationId\":");
                    builder.Append(route.DestinationId.Value.ToString(CultureInfo.InvariantCulture));
                    builder.Append(",\"productId\":");
                    if (route.ProductId == null)
                        builder.Append("null");
                    else
                    {
                        builder.Append('"');
                        builder.Append(JsonWriter.JsonEscapeString(route.ProductId));
                        builder.Append('"');
                    }
                    builder.Append('}');
                }
                builder.Append("]}");
            }
        }

        builder.Append("]}");
        ModStateJsonSaveResult result = store.SaveJson(builder.ToString());
        if (!result.Succeeded)
            Log.Warning($"[ATD] Failed to save ore sorter export state: {result.ErrorMessage}");
    }

    private static OreSorterExportState GetOrCreateOreSorterExportState(EntityId sorterId)
    {
        lock (s_oreSorterLock)
        {
            if (!s_oreSorterExportStates.TryGetValue(sorterId, out OreSorterExportState? state))
            {
                state = new OreSorterExportState();
                s_oreSorterExportStates[sorterId] = state;
            }
            return state;
        }
    }

    private static bool TryGetOreSorterExportState(EntityId sorterId, out OreSorterExportState state)
    {
        lock (s_oreSorterLock)
        {
            return s_oreSorterExportStates.TryGetValue(sorterId, out state!);
        }
    }

    internal static int GetOreSorterExportPriority(OreSortingPlant sorter)
    {
        return TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state)
            && state.ExportPriority.HasValue
            ? state.ExportPriority.Value
            : ClampPriority(sorter.GeneralPriority);
    }

    internal static void SetOreSorterExportPriority(OreSortingPlant sorter, int priority)
    {
        lock (s_oreSorterLock)
        {
            OreSorterExportState state = GetOrCreateOreSorterExportState(sorter.Id);
            state.ExportPriority = ClampPriority(priority);
        }
        s_oreSorterExportUiVersion++;
        RefreshOreSorterOutputPriorities(sorter);
    }

    internal static bool GetOreSorterAssignedTruckEnforcement(OreSortingPlant sorter)
        => TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state)
            && state.EnforceAssignedTrucks;

    internal static void SetOreSorterAssignedTruckEnforcement(OreSortingPlant sorter, bool enabled)
    {
        lock (s_oreSorterLock)
        {
            OreSorterExportState state = GetOrCreateOreSorterExportState(sorter.Id);
            state.EnforceAssignedTrucks = enabled;
        }
        s_oreSorterExportUiVersion++;
        RefreshOreSorterOutputPriorities(sorter);
    }

    internal static bool AddOreSorterExportRoute(OreSortingPlant sorter, EntityId destinationId, ProductProto? product)
    {
        if (!destinationId.IsValid || destinationId == sorter.Id)
            return false;

        string? productId = product?.Id.Value;
        lock (s_oreSorterLock)
        {
            OreSorterExportState state = GetOrCreateOreSorterExportState(sorter.Id);
            if (state.Routes.Any(route => route.DestinationId == destinationId && route.ProductId == productId))
                return false;

            state.Routes.Add(new OreSorterExportRoute { DestinationId = destinationId, ProductId = productId });
        }
        s_oreSorterExportUiVersion++;
        RefreshOreSorterOutputPriorities(sorter);
        return true;
    }

    private static OreSorterExportRoute? TryGetOreSorterGenericRoute(
        OreSortingPlant sorter,
        EntityId destinationId)
    {
        lock (s_oreSorterLock)
        {
            return TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state)
                ? state.Routes.FirstOrDefault(route => route.DestinationId == destinationId
                    && route.ProductId == null)
                : null;
        }
    }

    private static bool CanOreSorterExportTo(
        OreSortingPlant sorter,
        IEntityAssignedAsInput destination)
    {
        if (destination is MineTower)
            return true;
        if (s_oreSorterBuffersRegistry == null)
            return false;

        foreach (ProductProto product in sorter.AllowedProducts)
        {
            if (s_oreSorterBuffersRegistry.TryGetInputBuffer(destination, product).HasValue)
                return true;
        }
        return false;
    }

    internal static void RemoveOreSorterExportRoute(OreSortingPlant sorter, EntityId destinationId, ProductProto? product)
    {
        string? productId = product?.Id.Value;
        bool removed = false;
        lock (s_oreSorterLock)
        {
            if (TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
            {
                removed = state.Routes.RemoveAll(route => route.DestinationId == destinationId && route.ProductId == productId) > 0;
            }
        }
        if (removed)
        {
            s_oreSorterExportUiVersion++;
            RefreshOreSorterOutputPriorities(sorter);
        }
    }

    private static void RemoveOreSorterExportRoutesToDestination(
        OreSortingPlant sorter,
        EntityId destinationId)
    {
        bool removed = false;
        lock (s_oreSorterLock)
        {
            if (TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
            {
                removed = state.Routes.RemoveAll(route => route.DestinationId == destinationId) > 0;
            }
        }
        if (removed)
        {
            s_oreSorterExportUiVersion++;
            RefreshOreSorterOutputPriorities(sorter);
        }
    }

    private static IEnumerable<OreSortingPlant> GetOreSorterExportSourcesForDestination(
        EntityId destinationId)
    {
        var seen = new HashSet<EntityId>();
        if (s_entitiesManager == null)
            yield break;

        List<KeyValuePair<EntityId, OreSorterExportState>> entries;
        lock (s_oreSorterLock)
        {
            entries = s_oreSorterExportStates.ToList();
        }

        foreach (KeyValuePair<EntityId, OreSorterExportState> entry in entries)
        {
            if (!entry.Value.Routes.Any(route => route.DestinationId == destinationId)
                || !seen.Add(entry.Key)
                || !s_entitiesManager.TryGetEntity<OreSortingPlant>(entry.Key, out OreSortingPlant sorter)
                || sorter.IsDestroyed)
                continue;

            yield return sorter;
        }
    }

    private static void RemoveOreSorterExportRoute(OreSortingPlant sorter, OreSorterExportRoute route)
    {
        bool removed = false;
        lock (s_oreSorterLock)
        {
            if (TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
            {
                removed = state.Routes.Remove(route);
            }
        }
        if (removed)
        {
            s_oreSorterExportUiVersion++;
            RefreshOreSorterOutputPriorities(sorter);
        }
    }

    private static bool IsOreSorterExportRouteForProduct(
        OreSortingPlant sorter,
        OreSorterExportRoute route,
        ProductProto? product)
    {
        if (s_entitiesManager == null
            || !s_entitiesManager.TryGetEntity<IEntityAssignedAsInput>(route.DestinationId, out IEntityAssignedAsInput destination)
            || destination.IsDestroyed)
            return false;

        if (product == null)
            return true;

        if (route.ProductId != null)
            return route.ProductId == product.Id.Value;

        if (destination is MineTower tower)
        {
            return product.DumpableProduct.HasValue && tower.CanAcceptDumpOf(product);
        }

        if (s_oreSorterBuffersRegistry != null)
        {
            return s_oreSorterBuffersRegistry.TryGetInputBuffer(destination, product).HasValue;
        }

        return false;
    }

    private static readonly ConditionalWeakTable<OreSortingPlant, OreSortingPlantOutputAssigneeAdapter>
        s_oreSorterGenericAdapters = new ConditionalWeakTable<OreSortingPlant, OreSortingPlantOutputAssigneeAdapter>();

    private static OreSortingPlantOutputAssigneeAdapter GetOrCreateOreSorterOutputAdapter(OreSortingPlant sorter)
        => s_oreSorterGenericAdapters.GetValue(sorter, s => new OreSortingPlantOutputAssigneeAdapter(s, null));

    private static Lyst<IEntityAssignedAsOutput> GetOreSorterExportAdaptersForDestination(EntityId destinationId)
    {
        var result = new Lyst<IEntityAssignedAsOutput>();
        if (s_entitiesManager == null)
            return result;

        lock (s_oreSorterLock)
        {
            foreach (var kvp in s_oreSorterExportStates)
            {
                if (kvp.Value.Routes.Any(r => r.DestinationId == destinationId)
                    && s_entitiesManager.TryGetEntity<OreSortingPlant>(kvp.Key, out OreSortingPlant sorter)
                    && !sorter.IsDestroyed)
                {
                    result.Add(GetOrCreateOreSorterOutputAdapter(sorter));
                }
            }
        }
        return result;
    }

    private static Lyst<IEntityAssignedAsInput> GetOreSorterExportDestinations(OreSortingPlant sorter)
    {
        var result = new Lyst<IEntityAssignedAsInput>();
        if (s_entitiesManager == null || !TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
            return result;

        lock (s_oreSorterLock)
        {
            var seen = new HashSet<EntityId>();
            foreach (OreSorterExportRoute route in state.Routes)
            {
                if (seen.Add(route.DestinationId)
                    && s_entitiesManager.TryGetEntity<IEntityAssignedAsInput>(route.DestinationId, out IEntityAssignedAsInput destination)
                    && !destination.IsDestroyed)
                {
                    result.Add(destination);
                }
            }
        }
        return result;
    }

    public static void StorageAssignedOutputsGetterPostfix(
        Storage __instance,
        ref IReadOnlySet<IEntityAssignedAsOutput> __result)
    {
        var adapters = GetOreSorterExportAdaptersForDestination(__instance.Id);
        if (adapters.Count == 0)
            return;

        var combined = new Set<IEntityAssignedAsOutput>(__result);
        foreach (IEntityAssignedAsOutput adapter in adapters)
            combined.Add(adapter);
        __result = combined;
    }

    public static void MineTowerAssignedOutputsGetterPostfix(
        MineTower __instance,
        ref Mafi.Collections.IReadOnlySet<IEntityAssignedAsOutput> __result)
    {
        var adapters = GetOreSorterExportAdaptersForDestination(__instance.Id);
        if (adapters.Count == 0)
            return;

        var combined = new Set<IEntityAssignedAsOutput>(__result);
        foreach (IEntityAssignedAsOutput adapter in adapters)
            combined.Add(adapter);
        __result = combined;
    }

    public static void ForestryTowerAssignedOutputsGetterPostfix(
        ForestryTower __instance,
        ref Mafi.Collections.IReadOnlySet<IEntityAssignedAsOutput> __result)
    {
        var adapters = GetOreSorterExportAdaptersForDestination(__instance.Id);
        if (adapters.Count == 0)
            return;

        var combined = new Set<IEntityAssignedAsOutput>(__result);
        foreach (IEntityAssignedAsOutput adapter in adapters)
            combined.Add(adapter);
        __result = combined;
    }

    public static void AssignedBuildingsHighlighterUpdateHighlightPrefix(
        ref IEnumerable<IEntityAssignedAsInput> assignedInputs,
        IEnumerable<IEntityAssignedAsOutput> assignedOutputs,
        ILayoutEntity entity)
    {
        if (entity is OreSortingPlant sorter)
        {
            var exportDestinations = GetOreSorterExportDestinations(sorter);
            if (exportDestinations.Count > 0)
            {
                if (assignedInputs == null || !assignedInputs.Any())
                {
                    assignedInputs = exportDestinations;
                }
                else
                {
                    var combined = new Lyst<IEntityAssignedAsInput>(assignedInputs);
                    foreach (var dest in exportDestinations)
                    {
                        if (!combined.Contains(dest))
                            combined.Add(dest);
                    }
                    assignedInputs = combined;
                }
            }
        }
    }

    public static bool UnassignStaticEntityCmdPrefix(
        EntitiesCommandsProcessor __instance,
        UnassignStaticEntityCmd cmd)
    {
        if (s_entitiesManager != null
            && s_entitiesManager.TryGetEntity(cmd.FirstEntityId, out OreSortingPlant sorter))
        {
            RemoveOreSorterExportRoutesToDestination(sorter, cmd.SecondEntityId);
            cmd.SetResultSuccess();
            return false;
        }
        return true;
    }

    public static bool AssignStaticEntityCmdPrefix(
        EntitiesCommandsProcessor __instance,
        AssignStaticEntityCmd cmd)
    {
        if (s_entitiesManager != null
            && s_entitiesManager.TryGetEntity(cmd.FirstEntityId, out OreSortingPlant sorter))
        {
            if (AddOreSorterExportRoute(sorter, cmd.SecondEntityId, null))
            {
                cmd.SetResultSuccess();
            }
            else
            {
                cmd.SetResultError("Entities are not compatible.");
            }
            return false;
        }
        return true;
    }

    internal static IIndexable<Vehicle> GetOreSorterAssignedVehicles(OreSortingPlant? sorter)
    {
        var result = new Lyst<Vehicle>();
        if (sorter == null || s_entitiesManager == null || !TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
            return result;

        lock (s_oreSorterLock)
        {
            foreach (EntityId vehicleId in state.AssignedTruckIds.ToArray())
            {
                if (s_entitiesManager.TryGetEntity<Vehicle>(vehicleId, out Vehicle vehicle)
                    && !vehicle.IsDestroyed)
                {
                    result.Add(vehicle);
                }
                else
                {
                    state.AssignedTruckIds.Remove(vehicleId);
                }
            }
        }
        return result;
    }

    private static int GetOreSorterAssignedVehicleCount(OreSortingPlant? sorter)
        => GetOreSorterAssignedVehicles(sorter).Count;


    internal static bool IsOreSorterTruckAssigned(OreSortingPlant sorter, EntityId vehicleId)
    {
        lock (s_oreSorterLock)
        {
            return TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state)
                && state.AssignedTruckIds.Contains(vehicleId);
        }
    }

    private static readonly ConditionalWeakTable<OreSortingPlant, OreSortingPlantVehiclesEnforcerAdapter>
        s_oreSorterVehicleAdapters = new ConditionalWeakTable<OreSortingPlant, OreSortingPlantVehiclesEnforcerAdapter>();

    private static OreSortingPlantVehiclesEnforcerAdapter GetOrCreateOreSorterVehiclesEnforcerAdapter(OreSortingPlant sorter)
        => s_oreSorterVehicleAdapters.GetValue(sorter, s => new OreSortingPlantVehiclesEnforcerAdapter(s));

    internal static void AssignOreSorterTruck(OreSortingPlant? sorter, Vehicle? vehicle, bool doNotCancelJobs = false)
    {
        if (sorter != null && !sorter.IsDestroyed && vehicle != null && !vehicle.IsDestroyed)
        {
            lock (s_oreSorterLock)
            {
                GetOrCreateOreSorterExportState(sorter.Id).AssignedTruckIds.Add(vehicle.Id);
            }
            s_oreSorterExportUiVersion++;
            RefreshOreSorterOutputPriorities(sorter);
        }
    }

    internal static void UnassignOreSorterTruck(OreSortingPlant? sorter, Vehicle? vehicle, bool cancelJobs = true)
    {
        if (sorter != null && vehicle != null)
        {
            bool removed = false;
            lock (s_oreSorterLock)
            {
                if (TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
                    removed = state.AssignedTruckIds.Remove(vehicle.Id);
            }
            if (removed)
            {
                s_oreSorterExportUiVersion++;
                RefreshOreSorterOutputPriorities(sorter);
            }
        }
    }

    internal static bool TryGetOreSorterForAssignedTruck(EntityId vehicleId, out OreSortingPlant sorter)
    {
        sorter = null!;
        if (s_entitiesManager == null)
            return false;

        lock (s_oreSorterLock)
        {
            foreach (var kvp in s_oreSorterExportStates)
            {
                if (kvp.Value.AssignedTruckIds.Contains(vehicleId))
                {
                    if (s_entitiesManager.TryGetEntity<OreSortingPlant>(kvp.Key, out OreSortingPlant s)
                        && !s.IsDestroyed)
                    {
                        sorter = s;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    internal static bool IsTruckAssignedToAnyOreSorter(EntityId vehicleId)
        => TryGetOreSorterForAssignedTruck(vehicleId, out _);

    public static void VehicleCanBeAssignedPostfix(Vehicle __instance, ref bool __result)
    {
        // Defensive belt-and-suspenders check ensuring trucks assigned to an ore sorting plant
        // cannot be picked by vanilla VehiclesManager or other building assigners.
        if (__result && IsTruckAssignedToAnyOreSorter(__instance.Id))
        {
            __result = false;
        }
    }

    [ThreadStatic]
    private static bool s_isVehicleSerializing;

    public static void VehicleAssignedToPostfix(Vehicle __instance, ref Option<IEntityAssignedWithVehicles> __result)
    {
        if (!s_isVehicleSerializing && __result.IsNone && TryGetOreSorterForAssignedTruck(__instance.Id, out OreSortingPlant sorter))
        {
            __result = Option.Some<IEntityAssignedWithVehicles>(GetOrCreateOreSorterVehiclesEnforcerAdapter(sorter));
        }
    }

    public static void VehicleSerializeDataPrefix()
    {
        s_isVehicleSerializing = true;
    }

    public static void VehicleSerializeDataFinalizer()
    {
        s_isVehicleSerializing = false;
    }

    public static void UnassignVehicleCmdPrefix(UnassignVehicleCmd cmd)
    {
        if (TryGetOreSorterForAssignedTruck(cmd.VehicleId, out OreSortingPlant sorter))
        {
            lock (s_oreSorterLock)
            {
                if (TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
                    state.AssignedTruckIds.Remove(cmd.VehicleId);
            }
            s_oreSorterExportUiVersion++;
            RefreshOreSorterOutputPriorities(sorter);
        }
    }

    public static void AssignVehicleToEntityCmdPrefix(AssignVehicleToEntityCmd cmd)
    {
        if (TryGetOreSorterForAssignedTruck(cmd.VehicleId, out OreSortingPlant sorter)
            && sorter.Id != cmd.EntityId)
        {
            lock (s_oreSorterLock)
            {
                if (TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
                    state.AssignedTruckIds.Remove(cmd.VehicleId);
            }
            s_oreSorterExportUiVersion++;
            RefreshOreSorterOutputPriorities(sorter);
        }
    }

    internal static bool IsTruckEnforcedToAnyOreSorter(EntityId truckId)
    {
        lock (s_oreSorterLock)
        {
            foreach (OreSorterExportState state in s_oreSorterExportStates.Values)
            {
                if (state.EnforceAssignedTrucks && state.AssignedTruckIds.Contains(truckId))
                    return true;
            }
        }
        return false;
    }

    private static bool TryAssignOreSorterTruck(OreSortingPlant sorter, DynamicEntityProto proto, ulong zoneMask)
    {
        if (!(proto is TruckProto) || s_vehiclesManager == null)
            return false;

        lock (s_oreSorterLock)
        {
            OreSorterExportState state = GetOrCreateOreSorterExportState(sorter.Id);
            Truck? candidate = s_vehiclesManager.Trucks
                .Where(truck => truck.Prototype == proto
                    && truck.CanBeAssigned
                    && (truck.ZoneMask & zoneMask) != 0
                    && !state.AssignedTruckIds.Contains(truck.Id)
                    && !IsOreSorterTruckAssignedToAnotherSorter(sorter, truck.Id))
                .OrderBy(truck => truck.Position2f.DistanceSqrTo(sorter.Position2f))
                .FirstOrDefault();
            if (candidate == null)
                return false;

            state.AssignedTruckIds.Add(candidate.Id);
        }
        s_oreSorterExportUiVersion++;
        RefreshOreSorterOutputPriorities(sorter);
        return true;
    }

    private static bool TryUnassignOreSorterTruck(OreSortingPlant sorter, DynamicEntityProto proto)
    {
        if (s_entitiesManager == null)
            return false;

        lock (s_oreSorterLock)
        {
            if (!TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
                return false;

            foreach (EntityId vehicleId in state.AssignedTruckIds.ToArray().Reverse())
            {
                if (!s_entitiesManager.TryGetEntity<Vehicle>(vehicleId, out Vehicle vehicle)
                    || vehicle.Prototype != proto)
                    continue;

                state.AssignedTruckIds.Remove(vehicleId);
                s_oreSorterExportUiVersion++;
                RefreshOreSorterOutputPriorities(sorter);
                return true;
            }
        }
        return false;
    }

    private static bool IsOreSorterTruckAssignedToAnotherSorter(OreSortingPlant sorter, EntityId vehicleId)
    {
        lock (s_oreSorterLock)
        {
            foreach (KeyValuePair<EntityId, OreSorterExportState> entry in s_oreSorterExportStates)
            {
                if (entry.Key != sorter.Id && entry.Value.AssignedTruckIds.Contains(vehicleId))
                    return true;
            }
        }
        return false;
    }

    private static bool HasUsefulIdleOreSorterTruck(OreSorterExportState state)
    {
        if (s_entitiesManager == null)
            return false;

        foreach (EntityId vehicleId in state.AssignedTruckIds)
        {
            if (s_entitiesManager.TryGetEntity<Truck>(vehicleId, out Truck truck)
                && truck.IsEnabled && !truck.IsDestroyed && !truck.HasTrueJob)
                return true;
        }
        return false;
    }

    internal static bool TryHandleSorterVehicleAssignment(AssignVehicleTypeToEntityCmd command)
    {
        if (s_entitiesManager == null
            || !s_entitiesManager.TryGetEntity<OreSortingPlant>(command.EntityId, out OreSortingPlant sorter)
            || s_protosDb == null
            || !s_protosDb.TryGetProto<DynamicEntityProto>(command.VehicleId, out DynamicEntityProto proto))
        {
            return false;
        }

        ulong zoneMask = sorter.ZoneMask;
        if (command.ZoneId.HasValue)
        {
            foreach (LogisticsZone candidateZone in sorter.Context.LogisticsZonesManager.AllZones)
            {
                if (candidateZone.Id == command.ZoneId.Value)
                {
                    zoneMask = candidateZone.Mask;
                    break;
                }
            }
        }

        bool handled = false;
        for (int index = 0; index < command.Count; index++)
            handled |= TryAssignOreSorterTruck(sorter, proto, zoneMask);
        command.SetResultSuccess();
        return true;
    }

    internal static bool TryHandleSorterVehicleUnassignment(UnassignVehicleFromEntityCmd command)
    {
        if (s_entitiesManager == null
            || !s_entitiesManager.TryGetEntity<OreSortingPlant>(command.EntityId, out OreSortingPlant sorter)
            || s_protosDb == null
            || !s_protosDb.TryGetProto<DynamicEntityProto>(command.VehicleId, out DynamicEntityProto proto))
        {
            return false;
        }

        bool handled = false;
        for (int index = 0; index < command.Count; index++)
            handled |= TryUnassignOreSorterTruck(sorter, proto);
        command.SetResultSuccess();
        return true;
    }

    public static bool AssignSorterVehiclePrefix(AssignVehicleTypeToEntityCmd cmd)
        => !TryHandleSorterVehicleAssignment(cmd);

    public static bool UnassignSorterVehiclePrefix(UnassignVehicleFromEntityCmd cmd)
        => !TryHandleSorterVehicleUnassignment(cmd);

    public static void RegisteredOutputBufferInitAllPostfix(RegisteredOutputBuffer __instance)
    {
        if (__instance.Entity is OreSortingPlant sorter)
        {
            AttachAdapters(__instance, sorter);
        }
    }

    private static void AttachAdapters(RegisteredOutputBuffer buffer, OreSortingPlant sorter)
    {
        if (buffer.EntityAsAssignee.ValueOrNull is not OreSortingPlantOutputAssigneeAdapter)
        {
            buffer.EntityAsAssignee = Option.Some<IEntityAssignedAsOutput>(
                new OreSortingPlantOutputAssigneeAdapter(sorter, buffer.Product));
        }
        if (buffer.VehiclesEnforcer.ValueOrNull is not OreSortingPlantVehiclesEnforcerAdapter)
        {
            buffer.VehiclesEnforcer = Option.Some<IEntityEnforcingAssignedVehicles>(
                new OreSortingPlantVehiclesEnforcerAdapter(sorter));
        }
    }

    public static void RegisteredOutputBufferRefreshPostfix(RegisteredOutputBuffer __instance)
    {
        if (__instance.IsAvailableCached
            && __instance.Entity is OreSortingPlant sorter
            && !sorter.IsPortSetForProduct(__instance.Product)
            && TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state)
            && state.ExportPriority.HasValue)
        {
            __instance.RawPriorityCached = ClampPriority(state.ExportPriority.Value);
            __instance.CombinedPriorityCached = __instance.RawPriorityCached
                + Math.Max(0, __instance.JobsCount - 4) / 2;
        }
    }

    private static bool IsOreSorterExportRouteForDestination(
        OreSortingPlant sorter,
        EntityId destinationId,
        ProductProto product)
    {
        lock (s_oreSorterLock)
        {
            if (!TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
                return false;

            foreach (OreSorterExportRoute route in state.Routes)
            {
                if (route.DestinationId == destinationId
                    && IsOreSorterExportRouteForProduct(sorter, route, product))
                {
                    return true;
                }
            }
            return false;
        }
    }

    private static bool HasAnyOreSorterExportRoutesForProduct(
        OreSortingPlant sorter,
        ProductProto product)
    {
        lock (s_oreSorterLock)
        {
            if (!TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
                return false;

            foreach (OreSorterExportRoute route in state.Routes)
            {
                if (IsOreSorterExportRouteForProduct(sorter, route, product))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public static bool RegisteredOutputBufferCanBeServedByPrefix(
        RegisteredOutputBuffer __instance,
        Vehicle vehicle,
        bool isBalancingJob,
        ref bool __result)
    {
        if (vehicle != null)
        {
            if (TryGetOreSorterForAssignedTruck(vehicle.Id, out OreSortingPlant assignedSorter))
            {
                // This truck is assigned to an Ore Sorting Plant.
                // It CANNOT serve any other entity's output buffer (storage, factory, other sorter, etc.).
                // It can ONLY serve output buffers belonging to its assigned Ore Sorting Plant!
                if (__instance.Entity is OreSortingPlant sorter && sorter.Id == assignedSorter.Id)
                {
                    __result = true;
                    return false;
                }

                __result = false;
                return false;
            }
            else if (__instance.Entity is OreSortingPlant sorter)
            {
                // Unassigned truck trying to serve an Ore Sorting Plant.
                // If the sorter enforces assigned trucks, block it.
                if (GetOreSorterAssignedTruckEnforcement(sorter))
                {
                    __result = false;
                    return false;
                }
            }
        }
        return true;
    }

    public static bool RegisteredInputBufferCanBeServedByPrefix(
        RegisteredInputBuffer __instance,
        Vehicle vehicle,
        bool isBalancingJob,
        ref bool __result)
    {
        if (vehicle != null)
        {
            if (TryGetOreSorterForAssignedTruck(vehicle.Id, out OreSortingPlant assignedSorter))
            {
                // The truck is assigned to an Ore Sorting Plant.
                // It should only serve input buffers that are valid configured export destinations for this product
                // (if any routes are defined for this product).
                if (HasAnyOreSorterExportRoutesForProduct(assignedSorter, __instance.Product)
                    && !IsOreSorterExportRouteForDestination(assignedSorter, __instance.Entity.Id, __instance.Product))
                {
                    __result = false;
                    return false;
                }
            }
        }
        return true;
    }

    public static void DefaultTruckJobProviderTryGetJobForPostfix(
        DefaultTruckJobProvider __instance,
        Truck truck,
        ref bool __result)
    {
        if (!__result
            && !truck.HasJobs
            && s_parkAndWaitJobFactory != null
            && TryGetOreSorterForAssignedTruck(truck.Id, out OreSortingPlant sorter))
        {
            __result = s_parkAndWaitJobFactory.TryEnqueueParkingJobIfNeeded(truck, sorter);
        }
    }

    private static void AttachAdaptersAndRefreshAllOreSorters()
    {
        if (s_entitiesManager == null || s_oreSorterBuffersRegistry == null)
            return;

        foreach (OreSortingPlant sorter in s_entitiesManager.GetAllEntitiesOfType<OreSortingPlant>())
        {
            if (sorter.IsDestroyed)
                continue;

            foreach (ProductProto product in sorter.AllowedProducts)
            {
                RegisteredOutputBuffer? buffer = s_oreSorterBuffersRegistry.TryGetOutputBuffer(sorter, product).ValueOrNull;
                if (buffer != null)
                {
                    AttachAdapters(buffer, sorter);
                    buffer.RefreshPriorities();
                }
            }
        }
    }

    private static void RefreshAllOreSorterOutputPriorities()
    {
        if (s_entitiesManager == null)
            return;

        foreach (OreSortingPlant sorter in s_entitiesManager.GetAllEntitiesOfType<OreSortingPlant>())
            RefreshOreSorterOutputPriorities(sorter);
    }

    private static void RefreshOreSorterOutputPriorities(OreSortingPlant sorter)
    {
        if (s_oreSorterBuffersRegistry == null || sorter == null || sorter.IsDestroyed)
            return;

        foreach (ProductProto product in sorter.AllowedProducts)
        {
            RegisteredOutputBuffer? buffer = s_oreSorterBuffersRegistry.TryGetOutputBuffer(sorter, product).ValueOrNull;
            if (buffer != null)
            {
                AttachAdapters(buffer, sorter);
                buffer.RefreshPriorities();
            }
        }
    }

    private static int ClampPriority(int priority)
        => Math.Max(OreSorterPriorityMin, Math.Min(OreSorterPriorityMax, priority));

    private static bool TryGetOreSorterBool(Dict<string, object> dict, string key, out bool value)
    {
        value = false;
        if (dict.TryGetValue(key, out object rawValue) && rawValue is bool boolValue)
        {
            value = boolValue;
            return true;
        }
        return false;
    }

    private static bool TryGetOreSorterInt(Dict<string, object> dict, string key, out int value)
    {
        value = 0;
        return dict.TryGetValue(key, out object rawValue) && TryGetIntValue(rawValue, out value);
    }

    private static bool TryGetIntValue(object rawValue, out int value)
    {
        value = 0;
        if (rawValue is int intValue)
        {
            value = intValue;
            return true;
        }
        if (rawValue is long longValue)
        {
            value = (int)longValue;
            return true;
        }
        if (rawValue is double doubleValue)
        {
            value = (int)doubleValue;
            return Math.Abs(doubleValue - value) < 0.0001d;
        }
        return false;
    }

    private static void OreSorterInspectorCtorPostfix(
        object __instance,
        AssignedBuildingsHighlighter highlighter)
    {
        try
        {
            if (!s_oreSorterExportPanels.TryGetValue(__instance, out _))
            {
                Type? type = __instance.GetType();
                PropertyInfo? entityProperty = FindProperty(type, "Entity");
                PropertyInfo? contextProperty = FindProperty(type, "Context");
                FieldInfo? mainBodyField = FindField(type, "MainBody");
                if (contextProperty?.GetValue(__instance) is not UiContext context
                    || mainBodyField?.GetValue(__instance) is not Column mainBody)
                    return;

                Func<OreSortingPlant?> entityProvider = () => entityProperty?.GetValue(__instance) as OreSortingPlant;
                AttachOreSorterExportPriority(mainBody, entityProvider);
                if (!AttachOreSorterExportRoutes(mainBody, context, entityProvider, highlighter))
                {
                    mainBody.Add(new PanelWithHeader()
                        .BodyAdd(new OreSorterExportRoutesUi(context, entityProvider, highlighter))
                        .Title(Tr.ExportRoutesTitle));
                }
                AddOreSorterAssignedTrucksPanel(mainBody, context, entityProvider);
                s_oreSorterExportPanels.Add(__instance, new OreSorterExportPanelMarker());
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[ATD] Failed to add ore sorter export controls: {ex}");
        }
    }

    private static bool AttachOreSorterExportRoutes(
        Column mainBody,
        UiContext context,
        Func<OreSortingPlant?> entityProvider,
        AssignedBuildingsHighlighter highlighter)
    {
        PanelWithHeader? importPanel = null;
        for (int index = mainBody.ChildrenCount - 1; index >= 0; index--)
        {
            if (mainBody.ChildAtOrDefault(index) is PanelWithHeader panel)
            {
                importPanel = panel;
                break;
            }
        }
        if (importPanel == null)
            return false;

        var importBodyChildren = new List<UiComponent>();
        while (importPanel.Body.ChildrenCount > 0)
        {
            UiComponent? child = importPanel.Body.ChildAtOrDefault(0);
            if (child == null)
                break;
            child.RemoveFromHierarchy();
            importBodyChildren.Add(child);
        }

        Column importColumn = new Column(1.pt()).AlignItemsStretch().FlexGrow(1f, Percent.Fifty);
        importColumn.Add(importBodyChildren);
        Column exportColumn = new Column(1.pt()).AlignItemsStretch().FlexGrow(1f, Percent.Fifty);
        exportColumn.Add(new OreSorterExportRoutesUi(context, entityProvider, highlighter));
        Row routeRow = new Row(1.pt())
        {
            importColumn,
            new VerticalDivider().MarginRight(2.pt()),
            exportColumn
        };
        importPanel.Body.Add(routeRow.AlignItemsStretch());
        importPanel.Header.Add(
            new VerticalDivider().MarginRight(2.pt()),
            new BuildingsAssignerUiHeader(false, Tr.AssignedForLogistics__ExportTooltipGeneral)
                .FlexGrow(1f, Percent.Fifty));
        return true;
    }

    private static void AttachOreSorterExportPriority(
        Column mainBody,
        Func<OreSortingPlant?> entityProvider)
    {
        FieldInfo? legendField = FindField(typeof(BufferWithMultipleProductsUi), "m_legend");
        FieldInfo? rowField = FindField(typeof(BufferWithMultipleProductsUi), "m_row");
        if (legendField == null || rowField == null)
            return;

        for (int index = 0; index < mainBody.ChildrenCount; index++)
        {
            if (!(mainBody.ChildAtOrDefault(index) is PanelWithHeader panel)
                || !(panel.Body.ChildAtOrDefault(0) is BufferWithMultipleProductsUi buffer)
                || !(legendField.GetValue(buffer) is Row legend)
                || !(rowField.GetValue(buffer) is Row row))
                continue;

            legend.RemoveFromHierarchy();
            Row priorityGroup = new Row(1.pt())
            {
                new Label(Tr.ExportPriority.AppendColon()).MarginLeftRight(1.pt()),
                CreateOreSorterExportPriority(entityProvider).FlexShrink(0f)
            };
            priorityGroup.AlignItemsCenter().NoShrink();

            Row legendRow = new Row(1.pt())
            {
                legend.FlexGrow(1f).AlignSelfStretch(),
                priorityGroup
            };
            legendRow.AlignItemsCenter().AlignSelfStretch();
            buffer.SetChildren(legendRow, row);
            return;
        }
    }

    private static Dropdown<int> CreateOreSorterExportPriority(Func<OreSortingPlant?> entityProvider)
    {
        return PriorityDropdown.Create(
                PriorityDropdown.GeneralPriorityFactory,
                Button.General,
                OreSorterPriorityMin,
                OreSorterPriorityMax)
            .AsExportPrio()
            .Tooltip(Tr.ExportPriority__StorageTooltip)
            .ObserveValueDropdown(() =>
            {
                OreSortingPlant? sorter = entityProvider();
                return sorter == null ? OreSorterPriorityMax : GetOreSorterExportPriority(sorter);
            })
            .OnValueChanged((value, _) =>
            {
                OreSortingPlant? sorter = entityProvider();
                if (sorter != null)
                    SetOreSorterExportPriority(sorter, value);
            });
    }

    private static void AddOreSorterAssignedTrucksPanel(
        Column mainBody,
        UiContext context,
        Func<OreSortingPlant?> entityProvider)
    {
        PanelWithHeader panel = mainBody.AddAndReturn(new PanelWithHeader()
            .BodyAdd(new OreSorterAssignedTrucksUi(context, entityProvider))
            .Title(Tr.AssignedTrucks__Title)
            .TitleTooltip(Tr.AssignedTrucks__Building_Tooltip));
        panel.Collapsed(false);
        panel.Observe(() => GetOreSorterAssignedVehicleCount(entityProvider()))
            .Observe(() => s_oreSorterExportUiVersion)
            .Do((int count, int _) => panel.Title($"{Tr.AssignedTrucks__Title} ({count})".AsLoc()));
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        while (type != null)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
                return property;
            type = type.BaseType!;
        }
        return null;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        while (type != null)
        {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field;
            type = type.BaseType!;
        }
        return null;
    }


    private static readonly FieldInfo? s_buildingsAssignerInputEntityField =
        typeof(BuildingsAssigner).GetField(
            "m_inputEntity",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_buildingsAssignerIsForInputsField =
        typeof(BuildingsAssigner).GetField(
            "m_isForInputs",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_buildingsAssignerPickerField =
        typeof(BuildingsAssigner).GetField(
            "m_picker",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_buildingsAssignerShortcutsField =
        typeof(BuildingsAssigner).GetField(
            "m_shortcutsManager",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_buildingsAssignerHighlighterField =
        typeof(BuildingsAssigner).GetField(
            "m_highlighter",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_buildingsAssignerLinePreviewField =
        typeof(BuildingsAssigner).GetField(
            "m_linePreview",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_buildingsAssignerUnassignCursorField =
        typeof(BuildingsAssigner).GetField(
            "m_unassignCursor",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_buildingsAssignerAssignCursorField =
        typeof(BuildingsAssigner).GetField(
            "m_assignCursor",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_buildingsAssignerAssignToolField =
        typeof(BuildingsAssigner).GetField(
            "m_assignTool",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_buildingsAssignerAssignClickAudioField =
        typeof(BuildingsAssigner).GetField(
            "m_assignClickAudio",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_buildingsAssignerUnassignClickAudioField =
        typeof(BuildingsAssigner).GetField(
            "m_unassignClickAudio",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static MethodInfo? s_oreSorterAudioPlayMethod;
    private static MethodInfo? s_oreSorterGetSharedAudioUiMethod;

    private static void PlayOreSorterRouteAudio(object? audio)
    {
        if (audio == null)
            return;

        if (s_oreSorterAudioPlayMethod == null)
        {
            s_oreSorterAudioPlayMethod = audio.GetType().GetMethod(
                "Play",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
        }
        s_oreSorterAudioPlayMethod?.Invoke(audio, null);
    }

    private static object? GetOreSorterRouteAudio(UiContext context, string assetPath)
    {
        object audioDb = context.AudioDb;
        if (s_oreSorterGetSharedAudioUiMethod == null)
        {
            s_oreSorterGetSharedAudioUiMethod = audioDb.GetType().GetMethod(
                "GetSharedAudioUi",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(string) },
                null);
        }
        return s_oreSorterGetSharedAudioUiMethod?.Invoke(audioDb, new object[] { assetPath });
    }

    private sealed class OreSorterBuildingsAssignerHoverState
    {
        public OreSortingPlant? Sorter;
    }

    private static readonly ConditionalWeakTable<BuildingsAssigner, OreSorterBuildingsAssignerHoverState>
        s_oreSorterBuildingsAssignerHoverStates =
            new ConditionalWeakTable<BuildingsAssigner, OreSorterBuildingsAssignerHoverState>();

    private static bool BuildingsAssignerInputUpdatePrefix(BuildingsAssigner __instance)
    {
        if (!TryGetOreSorterReceiverRouteCandidate(
                __instance,
                out IEntityAssignedAsInput destination,
                out OreSortingPlant sorter,
                out ShortcutsManager shortcuts)
            || (!shortcuts.IsPrimaryActionDown && !Input.GetMouseButtonDown(0)))
        {
            return true;
        }

        OreSorterExportRoute? route = TryGetOreSorterGenericRoute(sorter, destination.Id);
        if (route != null)
        {
            RemoveOreSorterExportRoute(sorter, route);
            PlayOreSorterRouteAudio(s_buildingsAssignerUnassignClickAudioField?.GetValue(__instance));
        }
        else if (AddOreSorterExportRoute(sorter, destination.Id, null))
        {
            PlayOreSorterRouteAudio(s_buildingsAssignerAssignClickAudioField?.GetValue(__instance));
        }

        return false;
    }

    private static bool BuildingsAssignerRenderUpdatePrefix(BuildingsAssigner __instance)
    {
        if (!TryGetOreSorterReceiverRouteCandidate(
                __instance,
                out IEntityAssignedAsInput destination,
                out OreSortingPlant sorter,
                out _))
        {
            ClearOreSorterBuildingsAssignerHover(__instance);
            return true;
        }

        OreSorterBuildingsAssignerHoverState hoverState =
            s_oreSorterBuildingsAssignerHoverStates.GetOrCreateValue(__instance);
        if (hoverState.Sorter != null && hoverState.Sorter.Id != sorter.Id)
            ClearOreSorterBuildingsAssignerHover(__instance);

        hoverState.Sorter = sorter;

        if (s_buildingsAssignerHighlighterField?.GetValue(__instance)
            is AssignedBuildingsHighlighter highlighter)
        {
            highlighter.EntityHighlighter.Highlight(sorter, 1612277.ToColorRgba());
        }

        if (s_buildingsAssignerLinePreviewField?.GetValue(__instance) is LineMb linePreview)
        {
            linePreview.SetStartPoint(AssignedBuildingsHighlighter.GetCenterForOutput(sorter));
            linePreview.SetEndPoint(AssignedBuildingsHighlighter.GetCenterForOutput(destination));
            linePreview.SetColor(TryGetOreSorterGenericRoute(sorter, destination.Id) != null
                ? AssignedBuildingsHighlighter.LINE_REMOVING
                : AssignedBuildingsHighlighter.LINE_ADDING_OUTPUT);
        }

        bool routeExists = TryGetOreSorterGenericRoute(sorter, destination.Id) != null;
        FieldInfo? activeCursorField = routeExists
            ? s_buildingsAssignerUnassignCursorField
            : s_buildingsAssignerAssignCursorField;
        FieldInfo? inactiveCursorField = routeExists
            ? s_buildingsAssignerAssignCursorField
            : s_buildingsAssignerUnassignCursorField;
        (activeCursorField?.GetValue(__instance) as Cursoor)?.Show();
        (inactiveCursorField?.GetValue(__instance) as Cursoor)?.Hide();
        (s_buildingsAssignerAssignToolField?.GetValue(__instance) as Cursoor)?.Hide();
        return false;
    }

    private static void ClearOreSorterBuildingsAssignerHover(BuildingsAssigner assigner)
    {
        if (!s_oreSorterBuildingsAssignerHoverStates.TryGetValue(
                assigner,
                out OreSorterBuildingsAssignerHoverState? hoverState)
            || hoverState.Sorter == null)
        {
            return;
        }

        if (s_buildingsAssignerHighlighterField?.GetValue(assigner)
            is AssignedBuildingsHighlighter highlighter)
        {
            highlighter.EntityHighlighter.RemoveHighlight(hoverState.Sorter);
        }
        (s_buildingsAssignerUnassignCursorField?.GetValue(assigner) as Cursoor)?.Hide();
        (s_buildingsAssignerAssignCursorField?.GetValue(assigner) as Cursoor)?.Hide();
        hoverState.Sorter = null;
    }

    private static bool TryGetOreSorterReceiverRouteCandidate(
        BuildingsAssigner assigner,
        out IEntityAssignedAsInput destination,
        out OreSortingPlant sorter,
        out ShortcutsManager shortcuts)
    {
        destination = null!;
        sorter = null!;
        shortcuts = null!;

        if (s_buildingsAssignerIsForInputsField?.GetValue(assigner) is not bool isForInputs
            || isForInputs
            || s_buildingsAssignerInputEntityField?.GetValue(assigner)
                is not Option<IEntityAssignedAsInput> inputEntityOption
            || inputEntityOption.ValueOrNull is not IEntityAssignedAsInput inputEntity
            || s_buildingsAssignerPickerField?.GetValue(assigner) is not CursorPickingManager picker
            || s_buildingsAssignerShortcutsField?.GetValue(assigner) is not ShortcutsManager shortcutManager)
        {
            return false;
        }

        IRenderedEntity? pickedEntity = picker.PickEntity<IRenderedEntity>().ValueOrNull;
        if (!(pickedEntity is OreSortingPlant pickedSorter)
            || pickedSorter.Id == inputEntity.Id
            || !CanOreSorterExportTo(pickedSorter, inputEntity))
        {
            return false;
        }

        destination = inputEntity;
        sorter = pickedSorter;
        shortcuts = shortcutManager;
        return true;
    }

    private sealed class OreSorterExportRoutesUi : Column
    {
        private readonly UiContext m_context;
        private readonly Func<OreSortingPlant?> m_entityProvider;
        private readonly OreSorterRoutePicker m_routePicker;
        private readonly AssignedBuildingsHighlighter m_highlighter;
        private readonly Label m_noRoutesLabel;
        private readonly Row m_routes;

        public OreSorterExportRoutesUi(
            UiContext context,
            Func<OreSortingPlant?> entityProvider,
            AssignedBuildingsHighlighter highlighter)
        {
            m_context = context;
            m_entityProvider = entityProvider;
            m_highlighter = highlighter;
            m_routePicker = new OreSorterRoutePicker(context, highlighter);
            m_routes = new Row().Fill().Wrap();
            m_noRoutesLabel = new Label($"({Tr.AssignedForLogistics__Empty})".AsLoc()).MarginTop(3.pt());

            this.Gap(2.pt()).AlignItemsStretch();
            Add(new Row(1.pt())
                {
                    new ButtonIcon("Assets/Unity/UserInterface/General/PlusMinus.svg")
                        .MarginTopBottom(1.pt())
                        .Tooltip(Tr.AssignedForLogistics__ExportTooltipGeneral)
                        .OnClick(StartRoutePicker)
                        .AlignSelfStart(),
                    m_routes
                }
                .AlignItemsStart()
                .AlignSelfStretch());

            this.Observe(() => m_entityProvider())
                .Observe(() => s_oreSorterExportUiVersion)
                .Do((OreSortingPlant? _, int _) => RebuildRoutes());
        }

        public override void OnAttached()
        {
            base.OnAttached();
            RebuildRoutes();
        }

        private void StartRoutePicker()
        {
            OreSortingPlant? sorter = m_entityProvider();
            if (sorter == null)
                return;
            m_routePicker.ActivateFor(sorter);
        }

        private void RefreshHighlighter(OreSortingPlant sorter)
        {
            m_highlighter.UpdateHighlightOfAssignedEntities(
                GetOreSorterExportDestinations(sorter),
                sorter.AssignedOutputs,
                sorter);
        }

        private void RebuildRoutes()
        {
            m_routes.Clear();
            OreSortingPlant? sorter = m_entityProvider();
            if (sorter == null)
            {
                m_noRoutesLabel.KeepInHierarchyIf(m_routes, true);
                return;
            }

            bool hasRoutes = false;
            if (TryGetOreSorterExportState(sorter.Id, out OreSorterExportState state))
                hasRoutes = AddRouteIcons(sorter, state.Routes);
            m_noRoutesLabel.KeepInHierarchyIf(m_routes, !hasRoutes);
            if (hasRoutes && TryGetClosestParent(parent => parent is PanelWithHeader, out UiComponent panelComponent)
                && panelComponent is PanelWithHeader panel)
            {
                panel.Collapsed(false);
            }

            if (!m_routePicker.IsActive)
                RefreshHighlighter(sorter);
        }

        private bool AddRouteIcons(
            OreSortingPlant sorter,
            IEnumerable<OreSorterExportRoute> routes)
        {
            bool added = false;
            var seen = new HashSet<EntityId>();
            foreach (OreSorterExportRoute route in routes)
            {
                if (!seen.Add(route.DestinationId))
                    continue;
                if (s_entitiesManager == null
                    || !s_entitiesManager.TryGetEntity<IEntityAssignedAsInput>(route.DestinationId, out IEntityAssignedAsInput destination)
                    || destination.IsDestroyed)
                    continue;
                m_routes.Add(new AssignedBuildingIcon(
                    entity => m_context.CameraController.PanTo(entity.Position2f),
                    _ => RemoveOreSorterExportRoutesToDestination(sorter, destination.Id)).Value(destination));
                added = true;
            }
            return added;
        }
    }

    private sealed class OreSorterAssignedTrucksUi : Column
    {
        public OreSorterAssignedTrucksUi(UiContext context, Func<OreSortingPlant?> entityProvider)
        {
            this.AlignItemsStretch();
            var adapter = new OreSorterAssignedVehiclesAdapter(entityProvider);
            Add(new VehicleAssignerUi(this, context, typeof(TruckProto), () => adapter));
            Add(new PanelFooterRow().AlignSelfStretch().BodyAdd(
                c => c.AlignSelfStretch().JustifyItemsCenter(),
                new Toggle(standalone: true)
                    .Label(Tr.AssignedTrucksEnforce__Title)
                    .Tooltip(AtdLocalization.OreSorterAssignedTrucksEnforceTip)
                    .ObserveValue(() => entityProvider() != null
                        && GetOreSorterAssignedTruckEnforcement(entityProvider()!))
                    .ObserveEnabled(() => GetOreSorterAssignedVehicleCount(entityProvider()) > 0)
                    .OnValueChanged(value =>
                    {
                        OreSortingPlant? sorter = entityProvider();
                        if (sorter != null)
                            SetOreSorterAssignedTruckEnforcement(sorter, value);
                    })));
        }
    }

    private sealed class OreSorterAssignedVehiclesAdapter : IEntityAssignedWithVehicles
    {
        private readonly Func<OreSortingPlant?> m_entityProvider;
        private readonly Lyst<Vehicle> m_vehicles = new Lyst<Vehicle>();

        public OreSorterAssignedVehiclesAdapter(Func<OreSortingPlant?> entityProvider)
        {
            m_entityProvider = entityProvider;
        }

        private OreSortingPlant? Entity => m_entityProvider();

        public EntityId Id => Entity?.Id ?? default;
        public EntityProto Prototype => Entity?.Prototype!;
        public EntityContext Context => Entity?.Context!;
        public bool IsEnabled => Entity?.IsEnabled ?? false;
        public bool IsPaused => Entity?.IsPaused ?? false;
        public bool CanBePaused => Entity?.CanBePaused ?? false;
        public bool IsDestroyed => Entity?.IsDestroyed ?? true;
        public LocStrFormatted DefaultTitle => Entity?.DefaultTitle ?? LocStrFormatted.Empty;
        public Tile2f Position2f => Entity?.Position2f ?? default;
        public ulong ZoneMask => Entity?.ZoneMask ?? 0;

        public IIndexable<Vehicle> AllVehicles
        {
            get
            {
                m_vehicles.Clear();
                OreSortingPlant? sorter = Entity;
                if (sorter != null)
                    m_vehicles.AddRange(GetOreSorterAssignedVehicles(sorter));
                return m_vehicles;
            }
        }

        public bool CanVehicleBeAssigned(DynamicEntityProto vehicle)
            => vehicle is TruckProto && Entity != null;

        public void AssignVehicle(Vehicle vehicle, bool doNotCancelJobs = false)
            => AssignOreSorterTruck(Entity, vehicle, doNotCancelJobs);

        public void UnassignVehicle(Vehicle vehicle, bool cancelJobs = true)
            => UnassignOreSorterTruck(Entity, vehicle, cancelJobs);

        public void UpdateIsEnabled() => Entity?.UpdateIsEnabled();
        public void UpdateIsBroken() => Entity?.UpdateIsBroken();
        public void UpdateProperties() => Entity?.UpdateProperties();
        public void SetPaused(bool isPaused) => Entity?.SetPaused(isPaused);
        public void AddObserver(IEntityObserver observer) => Entity?.AddObserver(observer);
        public void RemoveObserver(IEntityObserver observer) => Entity?.RemoveObserver(observer);
    }

    private sealed class OreSorterRoutePicker : IUnityInputController
    {
        private readonly UiContext m_context;
        private readonly AssignedBuildingsHighlighter m_highlighter;
        private readonly LineMb m_linePreview;
        private readonly Cursoor m_unassignCursor;
        private readonly Cursoor m_assignCursor;
        private readonly Cursoor m_assignTool;
        private readonly object? m_assignClickAudio;
        private readonly object? m_unassignClickAudio;
        private readonly object? m_invalidClickAudio;

        private IEntityAssignedAsInput? m_hoveredDestination;
        private OreSortingPlant? m_sorter;
        private bool m_isToolActive;

        public bool IsActive => m_isToolActive;

        public OreSorterRoutePicker(
            UiContext context,
            AssignedBuildingsHighlighter highlighter)
        {
            m_context = context;
            m_highlighter = highlighter;
            m_unassignCursor = context.CursorManager.RegisterCursor(CursorsStyles.Unassign);
            m_assignCursor = context.CursorManager.RegisterCursor(CursorsStyles.Assign);
            m_assignTool = context.CursorManager.RegisterCursor(CursorsStyles.AssignGeneric);
            m_assignClickAudio = GetOreSorterRouteAudio(context,
                "Assets/Unity/UserInterface/Audio/AssignStructure.prefab");
            m_unassignClickAudio = GetOreSorterRouteAudio(context,
                "Assets/Unity/UserInterface/Audio/UnassignStructure.prefab");
            m_invalidClickAudio = GetOreSorterRouteAudio(context,
                "Assets/Unity/UserInterface/Audio/InvalidOp.prefab");
            m_linePreview = context.LinesFactory.CreateLine(
                Vector3.zero,
                Vector3.zero,
                1.5f,
                Color.white,
                highlighter.MovingArrowsLineMaterialShared);
            m_linePreview.SetTextureMode(LineTextureMode.Tile);
            m_linePreview.Hide();
        }

        public ControllerConfig Config => ControllerConfig.Tool;

        public void ActivateFor(OreSortingPlant sorter)
        {
            m_sorter = sorter;
            m_context.InputMgr.ActivateNewController(this);
        }

        public void Activate()
        {
            if (m_isToolActive || m_sorter == null)
                return;

            m_isToolActive = true;
            m_context.TerrainCursor.Activate();
            Vector3 origin = AssignedBuildingsHighlighter.GetCenterForInput(m_sorter);
            m_linePreview.SetStartPoint(origin);
            m_linePreview.SetEndPoint(origin);
            m_linePreview.Show();
            RefreshSorterHighlight(m_sorter);
            m_context.GameLoopEvents.RenderUpdate.AddNonSaveable(this, RenderUpdate);
        }

        public void Deactivate()
        {
            if (m_isToolActive)
            {
                m_isToolActive = false;
                m_context.GameLoopEvents.RenderUpdate.RemoveNonSaveable(this, RenderUpdate);
                m_context.TerrainCursor.Deactivate();
                m_linePreview.Hide();
                ClearHoveredDestination();
                if (m_sorter != null)
                    RefreshSorterHighlight(m_sorter);
            }

            m_unassignCursor.Hide();
            m_assignCursor.Hide();
            m_assignTool.Hide();
            m_sorter = null;
        }

        public bool InputUpdate()
        {
            if (!m_isToolActive)
                return false;

            if (Input.GetKeyDown(KeyCode.Escape)
                || m_context.ShortcutsManager.IsSecondaryActionUp
                || Input.GetMouseButtonDown(1))
            {
                m_context.InputMgr.DeactivateController(this);
                return true;
            }

            if (!m_context.ShortcutsManager.IsPrimaryActionDown
                && !Input.GetMouseButtonDown(0))
                return false;

            OreSortingPlant? sorter = m_sorter;
            if (sorter == null)
                return true;

            IRenderedEntity? picked = m_context.CursorPickingManager
                .PickEntity<IRenderedEntity>()
                .ValueOrNull;
            if (picked == null)
                return true;

            if (picked is not IEntityAssignedAsInput destination
                || destination.Id == sorter.Id
                || !CanOreSorterExportTo(sorter, destination))
            {
                PlayOreSorterRouteAudio(m_invalidClickAudio);
                return true;
            }

            OreSorterExportRoute? existingRoute = TryGetOreSorterGenericRoute(sorter, destination.Id);
            if (existingRoute != null)
            {
                RemoveOreSorterExportRoute(sorter, existingRoute);
                PlayOreSorterRouteAudio(m_unassignClickAudio);
            }
            else if (AddOreSorterExportRoute(sorter, destination.Id, null))
            {
                PlayOreSorterRouteAudio(m_assignClickAudio);
            }

            RefreshSorterHighlight(sorter);
            return true;
        }

        private void RenderUpdate(GameTime _)
        {
            OreSortingPlant? sorter = m_sorter;
            if (!m_isToolActive || sorter == null)
                return;

            Vector3 startPoint = AssignedBuildingsHighlighter.GetCenterForInput(sorter);
            m_linePreview.SetStartPoint(startPoint);

            IEntityAssignedAsInput? destination = PickDestination(sorter);
            if (destination == null)
            {
                m_assignTool.Show();
                ClearHoveredDestination();
                m_linePreview.SetColor(AssignedBuildingsHighlighter.LINE_COLOR_INPUT_ACTIVE);
                if (m_context.TerrainCursor.HasValue && !m_context.CameraController.IsInFreeLookMode)
                {
                    m_linePreview.SetEndPoint(m_context.TerrainCursor.Tile3f
                        .AddZ(AssignedBuildingsHighlighter.LINES_OFFSET_TILES + 2)
                        .ToVector3());
                }
                return;
            }

            if (m_hoveredDestination == null || m_hoveredDestination.Id != destination.Id)
            {
                ClearHoveredDestination();
                m_hoveredDestination = destination;
                m_highlighter.EntityHighlighter.Highlight(destination, AssignedBuildingsHighlighter.COLOR_ENTITY_HOVERED);
            }

            m_linePreview.SetEndPoint(AssignedBuildingsHighlighter.GetCenterForInput(destination));

            OreSorterExportRoute? existingRoute = TryGetOreSorterGenericRoute(sorter, destination.Id);
            if (existingRoute != null)
            {
                m_highlighter.HideInputLineIfCan(destination);
                m_linePreview.SetColor(AssignedBuildingsHighlighter.LINE_REMOVING);
                m_unassignCursor.Show();
                m_assignCursor.Hide();
                m_assignTool.Hide();
            }
            else
            {
                m_highlighter.UnHideInputLineIfCan();
                m_linePreview.SetColor(AssignedBuildingsHighlighter.LINE_ADDING_INPUT);
                m_assignCursor.Show();
                m_unassignCursor.Hide();
                m_assignTool.Hide();
            }
        }

        private IEntityAssignedAsInput? PickDestination(OreSortingPlant sorter)
        {
            IRenderedEntity? renderedEntity = m_context.CursorPickingManager
                .PickEntity<IRenderedEntity>()
                .ValueOrNull;
            return renderedEntity is IEntityAssignedAsInput destination
                && destination.Id != sorter.Id
                && CanOreSorterExportTo(sorter, destination)
                ? destination
                : null;
        }

        private void ClearHoveredDestination()
        {
            if (m_hoveredDestination != null)
            {
                m_highlighter.EntityHighlighter.RemoveHighlight(m_hoveredDestination);
                m_highlighter.UnHideInputLineIfCan();
                m_hoveredDestination = null;
            }
        }

        private void RefreshSorterHighlight(OreSortingPlant sorter)
        {
            m_highlighter.UpdateHighlightOfAssignedEntities(
                GetOreSorterExportDestinations(sorter),
                sorter.AssignedOutputs,
                sorter);
        }
    }
}

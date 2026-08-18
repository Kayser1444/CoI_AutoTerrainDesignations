// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Priority-driven active dumping. ATD advertises runtime-only input demand to
// vanilla logistics and translates a winning delivery into a real terrain dump.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Products;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.Vehicles.Trucks;

namespace AutoTerrainDesignations;

public static partial class AutoDepthDesignation
{
    internal const int DumpingPriorityMinimum = 1;
    internal const int DumpingPriorityMaximum = 15;
    internal const int DumpingPriorityPassive = DumpingPriorityMaximum + 1;
    private const int FarmingActiveDumpingPriorityMaximum = DumpingPriorityMaximum - 1;

    private static IVehicleBuffersRegistry? s_activeDumpingBuffersRegistry;
    private static UnreachableTerrainDesignationsManager? s_activeDumpingUnreachables;
    private static DumpingJob.Factory? s_activeDumpingJobFactory;
    private static CargoPickUpJob.Factory? s_activeDumpingPickUpFactory;
    private static ChainedNavigationJob.Factory? s_activeDumpingChainFactory;
    private static VehicleLastOutputBufferManager? s_activeDumpingLastOutputBuffers;
    private static readonly Dictionary<ActiveDumpingDemandKey, ActiveDumpingDemand> s_activeDumpingDemands =
        new Dictionary<ActiveDumpingDemandKey, ActiveDumpingDemand>();
    private static readonly Dictionary<RegisteredInputBuffer, ActiveDumpingDemand> s_activeDumpingByBuffer =
        new Dictionary<RegisteredInputBuffer, ActiveDumpingDemand>();
    private static readonly Dictionary<Tile2i, ActiveDumpingClaim> s_activeDumpingClaims =
        new Dictionary<Tile2i, ActiveDumpingClaim>();
    private static string s_lastActiveDumpingDetail = string.Empty;
    private static bool s_activeDumpingCompatibilityAvailable;
    private static bool s_activeDumpingHooksAvailable;
    private static bool s_activeDumpingCompatibilityFailureLogged;

    private readonly struct ActiveDumpingDemandKey : IEquatable<ActiveDumpingDemandKey>
    {
        public EntityId TowerId { get; }
        public LooseProductProto Product { get; }

        public ActiveDumpingDemandKey(EntityId towerId, LooseProductProto product)
        {
            TowerId = towerId;
            Product = product;
        }

        public bool Equals(ActiveDumpingDemandKey other) =>
            TowerId == other.TowerId && ReferenceEquals(Product, other.Product);

        public override bool Equals(object? obj) =>
            obj is ActiveDumpingDemandKey other && Equals(other);

        public override int GetHashCode() =>
            (TowerId.GetHashCode() * 397) ^ Product.GetHashCode();
    }

    private sealed class ActiveDumpingDemand
    {
        public MineTower Tower { get; set; }
        public LooseProductProto Product { get; }
        public ActiveDumpingDemandBuffer Buffer { get; }
        public ActiveDumpingPriorityProvider PriorityProvider { get; }
        public RegisteredInputBuffer? RegisteredBuffer { get; set; }

        public ActiveDumpingDemand(
            MineTower tower,
            LooseProductProto product,
            ActiveDumpingDemandBuffer buffer,
            ActiveDumpingPriorityProvider priorityProvider)
        {
            Tower = tower;
            Product = product;
            Buffer = buffer;
            PriorityProvider = priorityProvider;
        }
    }

    private sealed class ActiveDumpingClaim
    {
        public MineTower Tower { get; }
        public TerrainDesignation Designation { get; }
        public Truck Truck { get; }

        public ActiveDumpingClaim(MineTower tower, TerrainDesignation designation, Truck truck)
        {
            Tower = tower;
            Designation = designation;
            Truck = truck;
        }
    }

    /// <summary>
    /// Runtime-only input buffer. It advertises capacity to vanilla logistics but
    /// rejects every physical delivery; the selected cargo is dumped at a real
    /// terrain designation by the delivery adapter.
    /// </summary>
    private sealed class ActiveDumpingDemandBuffer : IProductBuffer
    {
        private int m_openSlots;

        public ProductProto Product { get; }
        public Quantity Quantity => Quantity.Zero;
        public Quantity Capacity => m_openSlots <= 0
            ? Quantity.Zero
            : TruckCaps.LargeTruckCapacity * m_openSlots;
        public Quantity UsableCapacity => Capacity;
        public int OpenSlots => m_openSlots;

        public ActiveDumpingDemandBuffer(LooseProductProto product)
        {
            Product = product;
        }

        public void SetOpenSlots(int slots) => m_openSlots = Math.Max(0, slots);

        public Quantity StoreAsMuchAs(Quantity quantity) => quantity;

        public Quantity RemoveAsMuchAs(Quantity maxQuantity) => Quantity.Zero;
    }

    private sealed class ActiveDumpingPriorityProvider : IInputBufferPriorityProvider
    {
        public int InternalPriority { get; private set; }

        public ActiveDumpingPriorityProvider(int displayedPriority)
        {
            SetDisplayedPriority(displayedPriority);
        }

        public void SetDisplayedPriority(int displayedPriority)
        {
            InternalPriority = Math.Max(DumpingPriorityMinimum,
                Math.Min(DumpingPriorityMaximum, displayedPriority)) - 1;
        }

        public BufferStrategy GetInputPriority(IProductBuffer buffer, Quantity pendingQuantity) =>
            BufferStrategy.NoQuantityPreference(InternalPriority);
    }

    internal static void ConfigureActiveSoilImport(
        IVehicleBuffersRegistry? buffersRegistry,
        ITruckJobsFilterManager? truckJobsFilter,
        UnreachableTerrainDesignationsManager? unreachables,
        DumpingJob.Factory? dumpingJobFactory = null,
        CargoPickUpJob.Factory? pickUpFactory = null,
        ChainedNavigationJob.Factory? chainFactory = null,
        VehicleLastOutputBufferManager? lastOutputBuffers = null)
    {
        s_activeDumpingBuffersRegistry = buffersRegistry;
        s_activeDumpingUnreachables = unreachables;
        s_activeDumpingJobFactory = dumpingJobFactory;
        s_activeDumpingPickUpFactory = pickUpFactory;
        s_activeDumpingChainFactory = chainFactory;
        s_activeDumpingLastOutputBuffers = lastOutputBuffers;
        s_activeDumpingCompatibilityAvailable = buffersRegistry != null
            && s_activeDumpingHooksAvailable
            && unreachables != null
            && dumpingJobFactory != null
            && pickUpFactory != null
            && chainFactory != null
            && lastOutputBuffers != null;
        s_activeDumpingCompatibilityFailureLogged = false;
        UnregisterAllActiveDumpingDemand();
        s_activeDumpingClaims.Clear();
        s_lastActiveDumpingDetail = string.Empty;
    }

    private static void ResetActiveSoilImportRuntime()
    {
        UnregisterAllActiveDumpingDemand();
        s_activeDumpingBuffersRegistry = null;
        s_activeDumpingUnreachables = null;
        s_activeDumpingJobFactory = null;
        s_activeDumpingPickUpFactory = null;
        s_activeDumpingChainFactory = null;
        s_activeDumpingLastOutputBuffers = null;
        s_activeDumpingClaims.Clear();
        s_activeDumpingCompatibilityAvailable = false;
        s_activeDumpingCompatibilityFailureLogged = false;
        s_lastActiveDumpingDetail = string.Empty;
    }

    internal static void PrepareActiveDumpingForSave() => UnregisterAllActiveDumpingDemand();

    internal static void ResumeActiveDumpingAfterSave()
    {
        s_activeDumpingClaims.Clear();
        SyncActiveDumpingDemand();
    }

    internal static void TickActiveDumpingDemand()
    {
        SyncActiveDumpingDemand();
        foreach (FarmingPreparationSession activeSession in s_farmingPreparationSessions.Values)
            activeSession.LastActiveSoilImportDetail = s_lastActiveDumpingDetail;
    }

    private static void SyncActiveDumpingDemand()
    {
        s_lastActiveDumpingDetail = string.Empty;
        if (!s_activeDumpingCompatibilityAvailable || s_activeDumpingBuffersRegistry == null)
        {
            LogActiveDumpingCompatibilityFailure("Required vanilla logistics seam is unavailable.");
            return;
        }

        try
        {
            PruneActiveDumpingClaims();
            var desired = new Dictionary<ActiveDumpingDemandKey, MineTower>();
            if (s_entitiesManager != null)
            {
                foreach (MineTower tower in s_entitiesManager.GetAllEntitiesOfType<MineTower>())
                {
                    if (tower == null || tower.IsDestroyed || !tower.IsEnabled)
                        continue;

                    int priority = GetEffectiveTowerDumpingPriority(tower);
                    if (priority >= DumpingPriorityPassive)
                        continue;

                    foreach (ProductProto productProto in tower.DumpableProducts)
                    {
                        if (!(productProto is LooseProductProto product))
                            continue;
                        if (!HasEligibleActiveDumpingOrigin(tower, product))
                            continue;
                        if (!TryGetTowerEntityId(tower, out EntityId towerId))
                            continue;
                        desired[new ActiveDumpingDemandKey(towerId, product)] = tower;
                    }
                }
            }

            foreach (ActiveDumpingDemandKey staleKey in s_activeDumpingDemands.Keys
                .Where(key => !desired.ContainsKey(key)).ToList())
                UnregisterActiveDumpingDemand(staleKey);

            foreach (KeyValuePair<ActiveDumpingDemandKey, MineTower> desiredEntry in desired)
            {
                if (!s_activeDumpingDemands.TryGetValue(desiredEntry.Key, out ActiveDumpingDemand? demand))
                {
                    demand = new ActiveDumpingDemand(
                        desiredEntry.Value,
                        desiredEntry.Key.Product,
                        new ActiveDumpingDemandBuffer(desiredEntry.Key.Product),
                        new ActiveDumpingPriorityProvider(
                            GetEffectiveTowerDumpingPriority(desiredEntry.Value)));
                    demand.Buffer.SetOpenSlots(
                        CountEligibleActiveDumpingOrigins(
                            desiredEntry.Value, desiredEntry.Key.Product));
                    if (!s_activeDumpingBuffersRegistry.TryRegisterInputBuffer(
                        desiredEntry.Value,
                        demand.Buffer,
                        demand.PriorityProvider))
                        continue;

                    demand.RegisteredBuffer = s_activeDumpingBuffersRegistry
                        .TryGetInputBuffer(desiredEntry.Value, desiredEntry.Key.Product)
                        .ValueOrNull;
                    if (demand.RegisteredBuffer == null)
                    {
                        s_activeDumpingBuffersRegistry.TryUnregisterInputBuffer(demand.Buffer);
                        continue;
                    }
                    s_activeDumpingDemands[desiredEntry.Key] = demand;
                    s_activeDumpingByBuffer[demand.RegisteredBuffer] = demand;
                }
                else
                {
                    demand.Tower = desiredEntry.Value;
                    demand.PriorityProvider.SetDisplayedPriority(
                        GetEffectiveTowerDumpingPriority(desiredEntry.Value));
                    demand.Buffer.SetOpenSlots(
                        CountEligibleActiveDumpingOrigins(
                            desiredEntry.Value, desiredEntry.Key.Product));
                }
            }

            s_lastActiveDumpingDetail = $"Active dumping demand: {s_activeDumpingDemands.Count} virtual input(s), {s_activeDumpingClaims.Count} claimed origin(s).";
        }
        catch (Exception ex)
        {
            LogActiveDumpingCompatibilityFailure("Demand synchronization failed: " + ex.Message);
            UnregisterAllActiveDumpingDemand();
        }
    }

    private static void UnregisterAllActiveDumpingDemand()
    {
        if (s_activeDumpingBuffersRegistry != null)
        {
            foreach (ActiveDumpingDemand demand in s_activeDumpingDemands.Values.ToList())
            {
                if (demand.RegisteredBuffer != null)
                    s_activeDumpingBuffersRegistry.TryUnregisterInputBuffer(demand.Buffer);
            }
        }
        s_activeDumpingByBuffer.Clear();
        s_activeDumpingDemands.Clear();
    }

    private static void UnregisterActiveDumpingDemand(ActiveDumpingDemandKey key)
    {
        if (!s_activeDumpingDemands.TryGetValue(key, out ActiveDumpingDemand? demand))
            return;
        if (demand.RegisteredBuffer != null)
            s_activeDumpingByBuffer.Remove(demand.RegisteredBuffer);
        s_activeDumpingBuffersRegistry?.TryUnregisterInputBuffer(demand.Buffer);
        s_activeDumpingDemands.Remove(key);
    }

    private static void PruneActiveDumpingClaims()
    {
        foreach (KeyValuePair<Tile2i, ActiveDumpingClaim> entry in s_activeDumpingClaims.ToList())
        {
            ActiveDumpingClaim claim = entry.Value;
            if (claim.Tower.IsDestroyed
                || claim.Designation.IsDestroyed
                || claim.Designation.IsDumpingFulfilled
                || !HasDumpingJobFor(claim.Truck, claim.Designation))
                s_activeDumpingClaims.Remove(entry.Key);
        }
    }

    private static bool HasDumpingJobFor(Truck truck, TerrainDesignation designation)
    {
        if (truck == null || truck.IsDestroyed)
            return false;
        for (int index = 0; index < truck.Jobs.Count; index++)
        {
            if (truck.Jobs[index] is DumpingJob job && job.PrimaryDesignation == designation)
                return true;
        }
        return truck.HasTrueJob && truck.IsNotEmpty;
    }

    private static bool HasEligibleActiveDumpingOrigin(MineTower tower, LooseProductProto product)
    {
        foreach (TerrainDesignation designation in tower.ManagedDesignations)
        {
            if (IsEligibleActiveDumpingOrigin(tower, designation, product, null))
                return true;
        }
        return false;
    }

    private static int CountEligibleActiveDumpingOrigins(
        MineTower tower,
        LooseProductProto product)
    {
        int count = 0;
        foreach (TerrainDesignation designation in tower.ManagedDesignations)
        {
            if (IsEligibleActiveDumpingOrigin(tower, designation, product, null))
                count++;
        }
        return count;
    }

    private static bool IsEligibleActiveDumpingOrigin(
        MineTower tower,
        TerrainDesignation designation,
        LooseProductProto product,
        Truck? truck)
    {
        if (tower.IsDestroyed || !tower.IsEnabled
            || designation == null || designation.IsDestroyed
            || designation.IsDumpingFulfilled
            || !IsDumpingDesignation(designation)
            || !tower.DumpableProducts.Contains(product)
            || s_activeDumpingClaims.ContainsKey(designation.OriginTileCoord)
            || designation.NumberOfJobsAssigned > 0
            || !designation.CanBeAssigned(tryIgnoreReservations: false))
            return false;

        if (truck != null)
        {
            if (!designation.IsReadyToDump(truck.Prototype))
                return false;
            try
            {
                if (s_activeDumpingUnreachables?.GetUnreachableDesignationsFor(truck).Contains(designation) == true)
                    return false;
            }
            catch
            {
                // A missing reachability cache is not proof of unreachability.
            }
        }

        return true;
    }

    private static bool TryClaimActiveDumpingOrigin(
        ActiveDumpingDemand demand,
        Truck truck,
        out TerrainDesignation designation)
    {
        designation = null!;
        IEnumerable<TerrainDesignation> candidates = demand.Tower.ManagedDesignations
            .Where(item => IsEligibleActiveDumpingOrigin(demand.Tower, item, demand.Product, truck))
            .OrderBy(item => item.CenterTileCoord.DistanceSqrTo(truck.GroundPositionTile2i));

        foreach (TerrainDesignation candidate in candidates)
        {
            Tile2i origin = candidate.OriginTileCoord;
            if (s_activeDumpingClaims.ContainsKey(origin))
                continue;
            s_activeDumpingClaims[origin] = new ActiveDumpingClaim(demand.Tower, candidate, truck);
            demand.Buffer.SetOpenSlots(Math.Max(0, demand.Buffer.OpenSlots - 1));
            demand.RegisteredBuffer?.RefreshPriorities();
            designation = candidate;
            return true;
        }

        return false;
    }

    internal static bool IsActiveDumpingInput(RegisteredInputBuffer buffer) =>
        buffer != null && s_activeDumpingByBuffer.ContainsKey(buffer);

    internal static bool TryHandleActiveDumpingBalancingJob(BalancingJobSpec spec)
    {
        if (!spec.InputBuffer.HasValue
            || !s_activeDumpingByBuffer.TryGetValue(spec.InputBuffer.Value, out ActiveDumpingDemand? demand))
            return false;

        // Mine-tower-assigned trucks escort excavators; they are never dumping
        // trucks, even when vanilla selects a tower-owned input buffer.
        if (spec.Truck.AssignedTo.ValueOrNull is MineTower)
            return true;

        try
        {
            // A synthetic input must never be passed to vanilla's delivery job.
            if (spec.OutputBuffer.IsNone
                || !(spec.ProductQuantity.Product is LooseProductProto)
                || spec.SecondaryInputBuffers.HasValue
                || spec.SecondaryOutputBuffers.HasValue
                || s_activeDumpingPickUpFactory == null
                || s_activeDumpingJobFactory == null
                || s_activeDumpingChainFactory == null
                || s_activeDumpingLastOutputBuffers == null)
                return true;

            if (!spec.Truck.IsAvailableToBalanceCargo())
                return true;
            if (spec.Truck.HasJobs)
            {
                if (spec.Truck.HasTrueJob)
                    return true;
                spec.Truck.CancelAllJobsAndResetState();
            }

            if (!TryClaimActiveDumpingOrigin(demand, spec.Truck, out TerrainDesignation designation))
                return true;

            RegisteredOutputBuffer source = spec.OutputBuffer.Value;
            ProductQuantity quantity = spec.ProductQuantity;
            // DumpingJob uses this same vanilla side table when a partial load
            // remains after the first origin. Preserve that continuation path.
            s_activeDumpingLastOutputBuffers.ReportOutputBufferFor(spec.Truck, source);
            CargoPickUpJob pickup = s_activeDumpingPickUpFactory.EnqueueJob(
                spec.Truck,
                quantity,
                source,
                Option<Lyst<SecondaryOutputBufferSpec>>.None);
            DumpingJob dump = s_activeDumpingJobFactory.EnqueueJob(
                spec.Truck,
                (LooseProductProto)quantity.Product,
                designation);
            s_activeDumpingChainFactory.EnqueueAsFirstJob(spec.Truck, pickup, dump);
            return true;
        }
        catch (Exception ex)
        {
            s_log.Warning("[ATD Dumping] Failed to translate vanilla balancing job: " + ex.Message);
            return true;
        }
    }

    internal static bool TryHandleActiveDumpingDelivery(
        Truck truck,
        ProductQuantity quantity,
        RegisteredInputBuffer inputBuffer)
    {
        if (!s_activeDumpingByBuffer.TryGetValue(inputBuffer, out ActiveDumpingDemand? demand))
            return false;

        // Assigned tower trucks are reserved for escort work. If one is
        // already carrying cargo, let vanilla's ordinary dumping search get
        // rid of it rather than sending it to the tower's virtual input.
        if (truck.AssignedTo.ValueOrNull is MineTower)
        {
            s_activeDumpingJobFactory?.TryCreateAndEnqueueJob(
                truck, quantity.Product, truck.ZoneMask);
            return true;
        }

        try
        {
            if (quantity.Product is LooseProductProto looseProduct
                && s_activeDumpingJobFactory != null
                && TryClaimActiveDumpingOrigin(demand, truck, out TerrainDesignation designation))
            {
                s_activeDumpingJobFactory.EnqueueJob(truck, looseProduct, designation);
                return true;
            }

            // The vanilla input was selected at the tower position. If that
            // position has no reachable origin for this truck, never enqueue a
            // synthetic delivery to the virtual buffer. Let ordinary dumping
            // rules try to dispose of the cargo instead.
            if (s_activeDumpingJobFactory != null)
                s_activeDumpingJobFactory.TryCreateAndEnqueueJob(
                    truck, quantity.Product, truck.ZoneMask);
            return true;
        }
        catch (Exception ex)
        {
            s_log.Warning("[ATD Dumping] Failed to translate loaded-truck delivery: " + ex.Message);
            return true;
        }
    }

    internal static int ClampDumpingPriority(int value) =>
        Math.Max(DumpingPriorityMinimum, Math.Min(DumpingPriorityPassive, value));

    /// <summary>
    /// A tower's configured priority is normally authoritative. During ATD's
    /// farmland fill window, however, the tower's dumpable products are narrowed
    /// to farmable materials, so the old active-import behavior remains useful
    /// without making ordinary tower dumping globally active by default.
    /// </summary>
    private static int GetEffectiveTowerDumpingPriority(MineTower tower)
    {
        int configuredPriority = GetTowerDumpingPriority(tower);
        if (!IsTowerInFarmingFillWindow(tower))
            return configuredPriority;

        return Math.Min(FarmingActiveDumpingPriorityMaximum, configuredPriority);
    }

    private static bool IsTowerInFarmingFillWindow(MineTower tower)
    {
        if (!TryGetTowerEntityId(tower, out EntityId towerId)
            || !towerId.IsValid
            || !s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession? session)
            || !session.Enabled)
        {
            return false;
        }

        return session.TowerDumpRulesOwned
            || session.Origins.Values.Any(origin => origin.Phase == FarmingOriginPhase.Filling);
    }

    private static void LogActiveDumpingCompatibilityFailure(string detail)
    {
        if (s_activeDumpingCompatibilityFailureLogged)
            return;
        s_activeDumpingCompatibilityFailureLogged = true;
        s_log.Warning("[ATD Dumping] Active dumping disabled; using vanilla passive dumping. " + detail);
    }

    internal static void DisableActiveDumpingCompatibility(string detail)
    {
        s_activeDumpingCompatibilityAvailable = false;
        LogActiveDumpingCompatibilityFailure(detail);
        UnregisterAllActiveDumpingDemand();
    }

    internal static void SetActiveDumpingHooksAvailable(bool available)
    {
        s_activeDumpingHooksAvailable = available;
        if (!available)
        {
            s_activeDumpingCompatibilityAvailable = false;
            UnregisterAllActiveDumpingDemand();
        }
    }
}

internal static class ActiveDumpingPatches
{
    internal static void Apply(Harmony harmony)
    {
        try
        {
            AutoDepthDesignation.SetActiveDumpingHooksAvailable(false);
            MethodInfo? assignMethod = AccessTools.Method(
                typeof(Truck), nameof(Truck.AssignBalancingJob),
                new[] { typeof(BalancingJobSpec) });
            if (assignMethod == null)
                throw new MissingMethodException("Truck.AssignBalancingJob(BalancingJobSpec)");
            harmony.Patch(
                assignMethod,
                prefix: new HarmonyMethod(typeof(ActiveDumpingPatches), nameof(TruckAssignBalancingJobPrefix)));

            MethodInfo? deliveryMethod = AccessTools.Method(
                typeof(CargoDeliveryJob.Factory), nameof(CargoDeliveryJob.Factory.EnqueueJob));
            if (deliveryMethod == null)
                throw new MissingMethodException("CargoDeliveryJob.Factory.EnqueueJob");
            harmony.Patch(
                deliveryMethod,
                prefix: new HarmonyMethod(typeof(ActiveDumpingPatches), nameof(CargoDeliveryEnqueuePrefix)));
            AutoDepthDesignation.SetActiveDumpingHooksAvailable(true);
        }
        catch (Exception ex)
        {
            AutoDepthDesignation.DisableActiveDumpingCompatibility(
                "Could not install active-demand delivery hooks: " + ex.Message);
        }
    }

    private static bool TruckAssignBalancingJobPrefix(BalancingJobSpec spec)
    {
        return !AutoDepthDesignation.TryHandleActiveDumpingBalancingJob(spec);
    }

    private static bool CargoDeliveryEnqueuePrefix(
        Truck truck,
        ProductQuantity toDeliver,
        RegisteredInputBuffer inputBuffer,
        Lyst<SecondaryInputBufferSpec> secondaryBuffers,
        ref CargoDeliveryJob __result)
    {
        if (!AutoDepthDesignation.IsActiveDumpingInput(inputBuffer))
            return true;

        AutoDepthDesignation.TryHandleActiveDumpingDelivery(truck, toDeliver, inputBuffer);
        __result = null!;
        return false;
    }
}

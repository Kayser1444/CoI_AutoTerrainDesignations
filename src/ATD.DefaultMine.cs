// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Products;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Excavators;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Core.Vehicles.Trucks.JobProviders;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static DefaultMine? s_defaultMine;
        private static int s_defaultMineRequested = -1;
        internal static bool DefaultMineEnabled => s_defaultMine?.Enabled ?? false;

        // UI callbacks enqueue intent only. UpdateAfterCmdProc applies it on the
        // simulation thread, including while the simulation is paused.
        internal static void SetDefaultMineEnabled(bool enabled)
            => Interlocked.Exchange(ref s_defaultMineRequested, enabled ? 1 : 0);

        internal static void ConfigureDefaultMine(DependencyResolver resolver)
        {
            try
            {
                s_defaultMine = new DefaultMine(
                    resolver.Resolve<IEntitiesManager>(),
                    resolver.Resolve<ITerrainMiningManager>(),
                    resolver.Resolve<TerrainDesignationsManager>(),
                    resolver.Resolve<MineTowersManager>(),
                    resolver.Resolve<ILogisticsZonesManager>(),
                    resolver.Resolve<ITruckJobsFilterManager>(),
                    resolver.Resolve<MiningJob.Factory>(),
                    resolver.Resolve<VehicleQueueJobFactory>(),
                    resolver.Resolve<TruckJobProviderContext>());
            }
            catch (Exception ex)
            {
                s_defaultMine = null;
                s_log.Exception(ex, "Default Mine dependencies unavailable; feature remains disabled");
            }
        }

        internal static void ApplyPendingDefaultMineSetting()
        {
            int pending = Interlocked.Exchange(ref s_defaultMineRequested, -1);
            if (pending >= 0)
                s_defaultMine?.SetEnabled(pending == 1);
        }

        internal static void TickDefaultMine() => s_defaultMine?.Tick();
        internal static void PrepareDefaultMineForSave() => s_defaultMine?.PrepareForSave();
        internal static void ResumeDefaultMineAfterSave() => s_defaultMine?.ResumeAfterSave();
        internal static void ResetDefaultMine()
        {
            s_defaultMine?.Dispose();
            s_defaultMine = null;
            Interlocked.Exchange(ref s_defaultMineRequested, -1);
        }

        internal static string DescribeDefaultMine() => s_defaultMine?.Describe()
            ?? "[ATD Default Mine] No world loaded.";

        /// <summary>
        /// Owns transient work and identity records. Only vanilla vehicle jobs
        /// enter game queues; mining work is removed before serialization.
        /// </summary>
        private sealed partial class DefaultMine
        {
            internal sealed class Session
            {
                internal readonly Excavator Excavator;
                internal MiningJob? Mining;
                internal VehicleJob? Unloading;
                internal Pair? Truck;
                internal long LastRequestTick = -1;
                internal Session(Excavator excavator) => Excavator = excavator;
            }

            internal sealed class Pair
            {
                internal readonly Truck Truck;
                internal readonly int[] ZoneIds;
                internal Session? Owner;
                internal VehicleQueueJob<Truck>? Pickup;
                internal bool Delivering;
                internal bool DetachedByPlayer;
                internal Pair(Truck truck, Session? owner, int[] zoneIds)
                { Truck = truck; Owner = owner; ZoneIds = zoneIds; }
            }

            private readonly IEntitiesManager m_entities;
            private readonly ITerrainMiningManager m_mining;
            private readonly TerrainDesignationsManager m_designations;
            private readonly MineTowersManager m_towers;
            private readonly ILogisticsZonesManager m_zones;
            private readonly ITruckJobsFilterManager m_filters;
            private readonly MiningJob.Factory m_miningFactory;
            private readonly VehicleQueueJobFactory m_queueFactory;
            private readonly TruckJobProviderContext m_truckContext;
            private readonly Dictionary<EntityId, Session> m_sessions = new Dictionary<EntityId, Session>();
            private readonly Dictionary<EntityId, Pair> m_trucks = new Dictionary<EntityId, Pair>();
            private readonly Mafi.Collections.Set<VehicleGroupProto> m_allowedGroups = new Mafi.Collections.Set<VehicleGroupProto>();
            private bool m_saving;
            private int m_internalCancellation;
            private int m_preserveCargo;
            private long m_tick;
            internal bool Enabled { get; private set; }
            internal bool Saving => m_saving;

            internal DefaultMine(IEntitiesManager entities, ITerrainMiningManager mining,
                TerrainDesignationsManager designations, MineTowersManager towers,
                ILogisticsZonesManager zones, ITruckJobsFilterManager filters,
                MiningJob.Factory miningFactory, VehicleQueueJobFactory queueFactory,
                TruckJobProviderContext truckContext)
            {
                m_entities = entities; m_mining = mining; m_designations = designations;
                m_towers = towers; m_zones = zones; m_filters = filters;
                m_miningFactory = miningFactory; m_queueFactory = queueFactory;
                m_truckContext = truckContext;
            }

            internal bool Owns(Excavator excavator)
                => m_sessions.ContainsKey(excavator.Id);

            internal bool Owns(Truck truck) => m_trucks.ContainsKey(truck.Id);

            internal void PickupCancelled(VehicleQueueJob<Truck> pickup)
            {
                if (m_trucks.TryGetValue(pickup.Vehicle.Id, out Pair pair)
                    && ReferenceEquals(pair.Pickup, pickup)) TruckInterrupted(pickup.Vehicle, cancelPickup: false);
            }

            internal bool Owns(MiningJob job)
                => m_sessions.TryGetValue(job.m_excavator.Id, out Session session)
                    && ReferenceEquals(session.Mining, job);

            private static bool IsEligible(Excavator excavator)
                => excavator.IsSpawned && !excavator.IsDestroyed && excavator.IsEnabled
                    && excavator.AssignedTo.IsNone && !excavator.IsOnWayToDepotForScrap
                    && !excavator.IsOnWayToDepotForReplacement;

            private bool IsOutsideTowers(TerrainDesignation designation)
                => !designation.IsDestroyed && !m_towers.Towers.Any(tower =>
                    !tower.IsDestroyed && tower.Area.ContainsTile(designation.CenterTileCoord));

            private bool TrySelect(Excavator excavator, out TerrainDesignation designation)
            {
                var unreachables = m_truckContext.UnreachablesManager.GetUnreachableDesignationsFor(excavator);
                return m_designations.TryFindBestReadyToFulfill(
                    m_mining.MiningDesignations.Where(candidate => IsOutsideTowers(candidate)
                        && candidate.IsReadyToBeMined(excavator.Prototype)
                        && candidate.CanBeAssigned(tryIgnoreReservations: false)
                        && !unreachables.Contains(candidate)),
                    excavator.GroundPositionTile2i, excavator, out designation,
                    excavator.PrioritizedProduct, preferDesignationNearAssignedEntity: false);
            }

            internal void SetEnabled(bool value)
            {
                if (value && !DefaultMinePatches.Ready)
                {
                    s_log.Warning("Default Mine cannot be enabled: required native integrations are unavailable.");
                    return;
                }
                Enabled = value;
                if (!value)
                {
                    foreach (Session session in m_sessions.Values.ToArray())
                        Interrupt(session.Excavator);
                }
                else
                {
                    // Keep native fuel handling before the patched mining factory.
                    foreach (Excavator excavator in m_entities.GetAllEntitiesOfType<Excavator>())
                        Wake(excavator);
                }
            }

            private void Wake(Excavator excavator)
            {
                if (!IsEligible(excavator) || excavator.HasTrueJob || m_saving)
                    return;
                excavator.m_waitHelper.Reset();
                if (!excavator.HasJobs && excavator.m_jobProvider.TryGetJobFor(excavator))
                    excavator.State = ExcavatorState.DoJob;
            }

            internal bool TryAcquire(Excavator excavator)
            {
                if (!Enabled || m_saving || !IsEligible(excavator) || excavator.HasTrueJob)
                    return false;
                if (!m_sessions.TryGetValue(excavator.Id, out Session session))
                {
                    session = new Session(excavator);
                    m_sessions.Add(excavator.Id, session);
                }
                session.Mining = null;
                session.Unloading = null;
                if (excavator.IsNotEmpty)
                {
                    excavator.KeepTruckQueueEnabled(30.Seconds());
                    RequestTruck(session);
                    m_truckContext.WaitingJobFactory.EnqueueJob(excavator, Duration.OneSecond);
                    session.Unloading = excavator.Jobs.AllJobs.OfType<VehicleJob>().Last();
                    excavator.UnloadToTruck();
                    return true;
                }
                if (TrySelect(excavator, out TerrainDesignation designation))
                {
                    // Register ownership before the constructor assignment assertion.
                    m_miningFactory.EnqueueJob(excavator, designation, Array.Empty<TerrainDesignation>());
                    session.Mining = excavator.Jobs.AllJobs.OfType<MiningJob>().Last();
                    return true;
                }
                ReleasePickup(session);
                return false;
            }

            internal bool Controls(MiningJob job, TerrainDesignation designation)
                => Owns(job) && IsEligible(job.m_excavator) && IsOutsideTowers(designation);

            internal bool BeforeMiningStep(MiningJob job)
            {
                if (!Owns(job)) return true;
                Excavator excavator = job.m_excavator;
                if (!Enabled || !IsEligible(excavator))
                {
                    Interrupt(excavator);
                    return false;
                }
                // Coverage changes finish an active scoop, then stop the native
                // job before its ownership check would discard the bucket.
                TerrainDesignation? designation = job.m_designationToMine.ValueOrNull
                    ?? job.m_primaryDesignation.ValueOrNull;
                if (designation != null && !IsOutsideTowers(designation)
                    && job.m_state != MiningJob.State.Mining
                    && job.m_state != MiningJob.State.WaitingForShovel)
                {
                    PreserveCargo(() => job.cleanup());
                    return false;
                }
                return true;
            }

            internal void ScoopStarted(MiningJob job)
            {
                if (Owns(job) && m_sessions.TryGetValue(job.m_excavator.Id, out Session session))
                    RequestTruck(session);
            }

            private int[] CaptureZones(Tile2i position)
                => m_zones.PlayerZonesFast.Where(zone => zone.Contains(position))
                    .Select(zone => zone.Zone.Id.Value).OrderBy(id => id).ToArray();

            private ulong ResolveMask(Pair pair) => ResolveMask(pair.ZoneIds);

            private ulong ResolveMask(int[] zoneIds) => DefaultMineZoneSnapshot.Resolve(
                zoneIds, m_zones.AllZones.Where(zone => !zone.IsDefaultZone && !zone.IsDestroyed)
                    .Select(zone => new KeyValuePair<int, ulong>(zone.Id.Value, zone.Mask)),
                LogisticsZone.DEFAULT_ZONE_MASK);

            internal bool TryGetMask(Vehicle vehicle, out ulong mask)
            {
                if (vehicle is Truck && m_trucks.TryGetValue(vehicle.Id, out Pair pair)
                    && !pair.DetachedByPlayer)
                { mask = ResolveMask(pair); return true; }
                mask = 0;
                return false;
            }

            private void RequestTruck(Session session, Truck? excluded = null, bool immediate = false)
            {
                if (m_saving || !Enabled || !IsEligible(session.Excavator) || session.Truck != null)
                    return;
                if (!immediate && session.LastRequestTick == m_tick) return;
                session.LastRequestTick = m_tick;
                int[] zones = CaptureZones(session.Excavator.GroundPositionTile2i);
                ulong mask = ResolveMask(zones);
                m_allowedGroups.Clear();
                m_filters.GetAllowedTrucksFor(mask, m_allowedGroups);
                Truck? truck = m_entities.GetAllEntitiesOfType<Truck>()
                    .Where(candidate => candidate != excluded && !m_trucks.ContainsKey(candidate.Id)
                        && candidate.AssignedTo.IsNone && !TryGetOreSorterForAssignedTruck(candidate.Id, out _)
                        && !candidate.IsOnWayToDepotForReplacement && candidate.IsSpawned && !candidate.IsDestroyed
                        && candidate.IsAvailableToBalanceCargo()
                        && (candidate.ZoneMask & mask) != 0
                        && m_allowedGroups.Contains(candidate.Prototype.VehicleGroup)
                        && session.Excavator.Prototype.IsTruckSupported(candidate.Prototype)
                        && !m_truckContext.UnreachablesManager.GetUnreachableEntitiesFor(candidate).Contains(session.Excavator))
                    .OrderBy(candidate => candidate.Position2f.DistanceSqrTo(session.Excavator.Position2f))
                    .ThenBy(candidate => candidate.Id.Value).FirstOrDefault();
                if (truck == null) return;
                var pair = new Pair(truck, session, zones);
                m_trucks.Add(truck.Id, pair);
                session.Truck = pair;
                EnqueuePickup(pair);
                LogDebug($"Default Mine: excavator {session.Excavator.Id} requested truck {truck.Id}.");
            }

            private void EnqueuePickup(Pair pair)
            {
                if (pair.Owner == null || pair.Truck.IsDestroyed) return;
                InternallyCancel(() => pair.Truck.CancelAllJobsAndResetState());
                pair.Owner.Excavator.KeepTruckQueueEnabled(30.Seconds());
                pair.Pickup = m_queueFactory.CreateJobForVehicleOwnedQueue(pair.Truck, pair.Owner.Excavator.TruckQueue);
                pair.Truck.EnqueueJob(pair.Pickup);
            }

            private void ReleasePickup(Session session)
            {
                Pair? pair = session.Truck;
                if (pair == null) return;
                if (!pair.Delivering)
                {
                    pair.Delivering = true;
                    if (pair.Pickup != null && !pair.Pickup.IsDestroyed)
                        InternallyCancel(() => pair.Pickup.RequestCancel());
                    // Disable clears both arriving and waiting queue entries.
                    session.Excavator.TruckQueue.Disable();
                }
                if (pair.Truck.IsEmpty)
                {
                    InternallyCancel(() => pair.Truck.CancelAllJobsAndResetState());
                    m_trucks.Remove(pair.Truck.Id);
                    session.Truck = null;
                }
            }

            internal void Interrupt(Excavator excavator)
            {
                if (m_saving || m_internalCancellation > 0
                    || !m_sessions.TryGetValue(excavator.Id, out Session session)) return;
                m_sessions.Remove(excavator.Id);
                ReleasePickup(session);
                if (session.Truck != null) session.Truck.Owner = null;
                InternallyCancel(() =>
                {
                    session.Mining?.RequestCancel();
                    session.Unloading?.RequestCancel();
                    excavator.ClearCargoImmediately();
                    excavator.TruckQueue.Disable();
                });
            }

            internal void TruckInterrupted(Truck truck, bool cancelPickup = true)
            {
                if (m_saving || m_internalCancellation > 0
                    || !m_trucks.TryGetValue(truck.Id, out Pair pair)) return;
                Session? owner = pair.Owner;
                if (owner?.Truck == pair) owner.Truck = null;
                pair.Owner = null;
                pair.DetachedByPlayer = true;
                // Cancellation/reassignment transfers control to the player.
                // A paused loaded truck retains its native cargo and resumes
                // through the normal default provider after unpause.
                m_trucks.Remove(truck.Id);
                if (cancelPickup && pair.Pickup != null && !pair.Pickup.IsDestroyed && !pair.Pickup.IsBeingCancelled)
                    InternallyCancel(() => pair.Pickup.RequestCancel());
                if (owner != null && (owner.Excavator.IsNotEmpty
                    || (owner.Mining != null && !owner.Mining.IsDestroyed && !owner.Mining.IsBeingCancelled)))
                    RequestTruck(owner, truck, immediate: true);
            }

            internal void Tick()
            {
                if (m_saving) return;
                m_tick++;
                foreach (Session session in m_sessions.Values.ToArray())
                {
                    if (session.Mining?.IsDestroyed == true) session.Mining = null;
                    if (session.Unloading?.IsDestroyed == true) session.Unloading = null;
                    if (!IsEligible(session.Excavator))
                    { Interrupt(session.Excavator); continue; }
                    Pair? pair = session.Truck;
                    if (pair != null && (pair.Truck.IsDestroyed || !pair.Truck.IsEnabled
                        || pair.Truck.AssignedTo.HasValue))
                        TruckInterrupted(pair.Truck);
                    if (session.Truck != null)
                        session.Excavator.KeepTruckQueueEnabled(30.Seconds());
                    if (session.Excavator.IsNotEmpty)
                        RequestTruck(session);
                }
                foreach (Pair pair in m_trucks.Values.ToArray())
                {
                    if (pair.Truck.IsDestroyed || (pair.Delivering && pair.Truck.IsEmpty))
                    {
                        if (pair.Owner?.Truck == pair) pair.Owner.Truck = null;
                        m_trucks.Remove(pair.Truck.Id);
                        continue;
                    }
                    if (!pair.Delivering && pair.Pickup != null
                        && (pair.Pickup.IsDestroyed || pair.Pickup.IsCancelled
                            || !pair.Truck.Jobs.AllJobs.Contains(pair.Pickup)))
                    {
                        if (!pair.Truck.IsFull && pair.Owner?.Excavator.Jobs.ContainsJobOfType<RefuelSelfJob>() == true)
                            EnqueuePickup(pair);
                        else pair.Delivering = true;
                    }
                    if (pair.Delivering && pair.Owner?.Truck == pair && pair.Truck.IsDriving)
                    {
                        pair.Owner.Truck = null;
                        pair.Owner = null;
                    }
                }
            }

            internal bool TryTruckJob(Truck truck, out bool hasJob)
            {
                hasJob = false;
                if (m_saving || !m_trucks.TryGetValue(truck.Id, out Pair pair)) return false;
                if (truck.AssignedTo.HasValue || !truck.IsEnabled)
                { TruckInterrupted(truck); return false; }
                if (!pair.Delivering)
                {
                    if (!truck.IsFull && pair.Owner?.Excavator.Jobs.ContainsJobOfType<RefuelSelfJob>() == true)
                    { EnqueuePickup(pair); hasJob = true; return true; }
                    // The native queue was released (full or incompatible cargo).
                    pair.Delivering = true;
                }
                if (truck.IsEmpty)
                {
                    if (pair.Owner?.Truck == pair) pair.Owner.Truck = null;
                    m_trucks.Remove(truck.Id);
                    return false;
                }
                if (truck.NeedsRefueling)
                {
                    hasJob = m_truckContext.FuelStationsManager.TryRefuelSelf(truck);
                    if (hasJob || truck.CannotWorkDueToLowFuel) return true;
                }
                hasJob = TryDeliver(truck);
                return true;
            }

            private bool TryDeliver(Truck truck)
            {
                var context = m_truckContext;
                if (context.OreSortingPlantsManager.IsSortingRequiredFor(truck, isMineTruck: true))
                {
                    if (context.OreSortingPlantsManager.TryGetMixedDeliveryJobFor(truck, Option<MineTower>.None,
                        out bool hasMatchingPlant, out _))
                    { truck.DeactivateCannotDeliver(); return true; }
                    if (truck.Cargo.Count > 1)
                    {
                        if (context.DumpJobFactory.TryCreateAndEnqueueJob(truck, Option.None, truck.ZoneMask))
                        { truck.DeactivateCannotDeliver(); return true; }
                        truck.NotifyIffCannotDeliverMixed(!hasMatchingPlant);
                        return false;
                    }
                }
                foreach (var cargo in truck.Cargo)
                {
                    var quantity = new ProductQuantity(cargo.Key, cargo.Value);
                    var buffer = context.VehicleBuffersRegistry.TryGetProductInputForVehicle(
                        truck, quantity, Option.None, out _);
                    if (buffer.HasValue)
                    { context.DeliveryJobFactory.EnqueueJob(truck, quantity, buffer.Value); truck.DeactivateCannotDeliver(); return true; }
                }
                foreach (var cargo in truck.Cargo)
                    if (context.DumpJobFactory.TryCreateAndEnqueueJob(truck, cargo.Key, truck.ZoneMask))
                    { truck.DeactivateCannotDeliver(); return true; }
                foreach (var cargo in truck.Cargo)
                    if (context.SurfaceJobFactory.TryCreateAndEnqueuePlacementJob(cargo.Key, truck))
                    { truck.DeactivateCannotDeliver(); return true; }
                truck.NotifyIffCannotDeliver(true, truck.Cargo.FirstOrPhantom.Product);
                return false;
            }

            internal bool HoldScoop(Excavator excavator)
            {
                if (!Owns(excavator) || !IsEligible(excavator) || excavator.IsEmpty) return false;
                excavator.KeepTruckQueueEnabled(30.Seconds());
                var truck = excavator.TruckQueue.TryGetFirstTruckFor(excavator.Cargo);
                if (truck.HasValue) return false;
                RequestTruck(m_sessions[excavator.Id]);
                return true;
            }

            internal bool PreserveBucket(Excavator excavator)
                => Owns(excavator) && (m_saving || m_preserveCargo > 0);

            private void PreserveCargo(Action action)
            { m_preserveCargo++; try { action(); } finally { m_preserveCargo--; } }
            private void InternallyCancel(Action action)
            { m_internalCancellation++; try { action(); } finally { m_internalCancellation--; } }

            internal void Dispose()
            {
                Enabled = false;
                foreach (Session session in m_sessions.Values.ToArray()) Interrupt(session.Excavator);
                m_sessions.Clear(); m_trucks.Clear();
            }

            internal string Describe()
            {
                var text = new StringBuilder($"[ATD Default Mine] Enabled={Enabled}, patches={DefaultMinePatches.Ready}, saving={m_saving}, excavators={m_sessions.Count}, trucks={m_trucks.Count}\n");
                foreach (Session session in m_sessions.Values)
                    text.AppendLine($"Excavator {session.Excavator.Id}: job={session.Mining?.Id.ToString() ?? "none"}, scoop={session.Excavator.Cargo.Count}, truck={session.Truck?.Truck.Id.ToString() ?? "none"}");
                return text.ToString();
            }
        }
    }
}

// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Active soil import for farmland filling. This file intentionally delegates
// job creation, source reservation, partial-load behaviour, and continuation
// to vanilla's balancing/truck providers.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Products;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.Vehicles.Trucks;

namespace AutoTerrainDesignations;

public static partial class AutoDepthDesignation
{
    private static IVehicleBuffersRegistry? s_activeSoilImportBuffersRegistry;
    private static ITruckJobsFilterManager? s_activeSoilImportTruckJobsFilter;
    private static UnreachableTerrainDesignationsManager? s_activeSoilImportUnreachables;
    private static readonly ActiveSoilImportOutputAdapter s_activeSoilImportOutputAdapter =
        new ActiveSoilImportOutputAdapter();

    private sealed class ActiveSoilImportOutputAdapter
    {
        private FieldInfo? m_productBucketsField;
        private FieldInfo? m_outputBuffersField;
        private bool m_initialized;
        private bool m_available;
        private bool m_unavailableLogged;

        public bool IsAvailable => m_available;

        public void Configure(IVehicleBuffersRegistry? registry)
        {
            m_initialized = false;
            m_available = false;
            m_unavailableLogged = false;
            m_productBucketsField = null;
            m_outputBuffersField = null;

            if (registry is not VehicleBuffersRegistry concreteRegistry)
            {
                return;
            }

            try
            {
                m_productBucketsField = typeof(VehicleBuffersRegistry).GetField(
                    "m_registeredBuffersPerProduct",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Type? bucketType = m_productBucketsField?.FieldType.GetGenericArguments().LastOrDefault();
                m_outputBuffersField = bucketType?.GetField(
                    "OutputBuffers",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                m_available = m_productBucketsField != null && m_outputBuffersField != null;
                m_initialized = true;
                if (!m_available)
                    LogUnavailable("VehicleBuffersRegistry product-bucket shape is unavailable.");
            }
            catch (Exception ex)
            {
                LogUnavailable("VehicleBuffersRegistry product-bucket adapter failed: " + ex.Message);
            }
        }

        public bool TryCollect(
            ISet<LooseProductProto> allowedProducts,
            List<RegisteredOutputBuffer> result)
        {
            result.Clear();
            if (!m_initialized || !m_available || s_activeSoilImportBuffersRegistry is not VehicleBuffersRegistry registry)
                return false;

            try
            {
                object? productBuckets = m_productBucketsField!.GetValue(registry);
                if (productBuckets is not IEnumerable buckets)
                {
                    m_available = false;
                    LogUnavailable("VehicleBuffersRegistry product buckets are not enumerable.");
                    return false;
                }

                var seen = new HashSet<RegisteredOutputBuffer>();
                foreach (object? bucketEntry in buckets)
                {
                    if (bucketEntry == null)
                        continue;

                    PropertyInfo? valueProperty = bucketEntry.GetType().GetProperty("Value");
                    object? bucket = valueProperty?.GetValue(bucketEntry);
                    object? outputs = bucket == null ? null : m_outputBuffersField!.GetValue(bucket);
                    if (outputs is not IEnumerable outputEnumerable)
                        continue;

                    foreach (object? outputObject in outputEnumerable)
                    {
                        if (outputObject is not RegisteredOutputBuffer output || !seen.Add(output))
                            continue;
                        if (!TryGetFarmableProduct(output, out LooseProductProto product)
                            || !allowedProducts.Contains(product))
                            continue;
                        result.Add(output);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                m_available = false;
                LogUnavailable("VehicleBuffersRegistry product-bucket enumeration failed: " + ex.Message);
                result.Clear();
                return false;
            }
        }

        private void LogUnavailable(string detail)
        {
            if (m_unavailableLogged)
                return;
            m_unavailableLogged = true;
            s_log.Warning("[ATD Farming] Active soil import disabled: " + detail);
        }
    }

    private sealed class ActiveSoilImportCandidate
    {
        public RegisteredOutputBuffer Source { get; }
        public LooseProductProto Product { get; }
        public TerrainDesignation Target { get; }
        public MineTower TargetTower { get; }
        public Truck Truck { get; }
        public int Priority { get; }
        public int TargetDistance { get; }
        public int TruckDistance { get; }
        public string SourceId { get; }
        public string TruckId { get; }

        public ActiveSoilImportCandidate(
            RegisteredOutputBuffer source,
            LooseProductProto product,
            TerrainDesignation target,
            MineTower targetTower,
            Truck truck,
            int priority,
            int targetDistance,
            int truckDistance)
        {
            Source = source;
            Product = product;
            Target = target;
            TargetTower = targetTower;
            Truck = truck;
            Priority = priority;
            TargetDistance = targetDistance;
            TruckDistance = truckDistance;
            SourceId = source.Entity.Id.ToString();
            TruckId = truck.Id.ToString();
        }

        public string Key => SourceId + ":" + Target.OriginTileCoord + ":" + TruckId;
    }

    internal static void ConfigureActiveSoilImport(
        IVehicleBuffersRegistry? buffersRegistry,
        ITruckJobsFilterManager? truckJobsFilter,
        UnreachableTerrainDesignationsManager? unreachables)
    {
        s_activeSoilImportBuffersRegistry = buffersRegistry;
        s_activeSoilImportTruckJobsFilter = truckJobsFilter;
        s_activeSoilImportUnreachables = unreachables;
        s_activeSoilImportOutputAdapter.Configure(buffersRegistry);
        if (!ActiveSoilImportFixtures.ValidateAll(out string fixtureFailure))
            s_log.Warning("[ATD Farming] Active soil import policy fixture failed: " + fixtureFailure);
    }

    private static void ResetActiveSoilImportRuntime()
    {
        s_activeSoilImportBuffersRegistry = null;
        s_activeSoilImportTruckJobsFilter = null;
        s_activeSoilImportUnreachables = null;
        s_activeSoilImportOutputAdapter.Configure(null);
    }

    private static void DispatchActiveFarmingSoilImports(FarmingPreparationSession session)
    {
        session.LastActiveSoilImportDetail = string.Empty;
        if (!s_activeSoilImportOutputAdapter.IsAvailable
            || s_activeSoilImportTruckJobsFilter == null
            || s_activeSoilImportUnreachables == null
            || s_vehiclesManager == null
            || s_desigManager == null)
        {
            session.LastActiveSoilImportDetail = FarmingTr(
                "active_soil_import_unavailable",
                "Active soil import: unavailable (vanilla logistics adapter not available).");
            return;
        }

        var fillingOrigins = new HashSet<Tile2i>(session.Origins.Values
            .Where(origin => origin.Phase == FarmingOriginPhase.Filling)
            .Select(origin => origin.Origin));
        HashSet<TerrainDesignation> vanillaDumpingClaims = CollectVanillaDumpingClaims();

        PruneActiveSoilImportSlots(session, fillingOrigins, vanillaDumpingClaims);

        int ordinaryClaims = 0;
        int graceWaiting = 0;
        var eligibleOrigins = new List<FarmingOriginSession>();
        foreach (FarmingOriginSession originState in session.Origins.Values)
        {
            if (!fillingOrigins.Contains(originState.Origin))
                continue;
            if (session.ActiveSoilImportOrigins.Contains(originState.Origin))
                continue;

            var designationOption = s_desigManager.GetDesignationAt(originState.Origin);
            if (!designationOption.HasValue || !IsDumpingDesignation(designationOption.Value))
                continue;

            TerrainDesignation designation = designationOption.Value;
            if (designation.NumberOfJobsAssigned > 0 || vanillaDumpingClaims.Contains(designation))
            {
                session.ActiveSoilImportNoClaimTicks[originState.Origin] = 0;
                ordinaryClaims++;
                continue;
            }

            int noClaimTicks = session.ActiveSoilImportNoClaimTicks.TryGetValue(
                originState.Origin,
                out int previousNoClaimTicks)
                ? previousNoClaimTicks + 1
                : 1;
            session.ActiveSoilImportNoClaimTicks[originState.Origin] = noClaimTicks;
            if (noClaimTicks < 2)
            {
                graceWaiting++;
                continue;
            }

            eligibleOrigins.Add(originState);
        }

        if (eligibleOrigins.Count == 0)
        {
            session.LastActiveSoilImportDetail = FarmingTrFormat(
                "active_soil_import_waiting_claim",
                "Active soil import: dispatched=0, pending={0}, ordinaryClaims={1}, graceWaiting={2}.",
                fillingOrigins.Count,
                ordinaryClaims,
                graceWaiting);
            return;
        }

        var allowedProducts = new HashSet<LooseProductProto>(GetFarmableDumpProducts());
        var sources = new List<RegisteredOutputBuffer>();
        if (!s_activeSoilImportOutputAdapter.TryCollect(allowedProducts, sources))
        {
            session.LastActiveSoilImportDetail = FarmingTrFormat(
                "active_soil_import_source_discovery_unavailable",
                "Active soil import: pending={0}, source discovery unavailable.",
                fillingOrigins.Count);
            return;
        }

        var failedCandidates = new HashSet<string>(StringComparer.Ordinal);
        int dispatched = 0;
        int routeBlocked = 0;
        int unavailableSources = 0;
        int unreachable = 0;
        int noEligibleTruck = 0;

        while (true)
        {
            ActiveSoilImportCandidate? candidate = FindBestActiveSoilImportCandidate(
                eligibleOrigins,
                sources,
                allowedProducts,
                failedCandidates,
                ref routeBlocked,
                ref unavailableSources,
                ref unreachable,
                ref noEligibleTruck);
            if (candidate == null)
                break;

            if (!TryAssignActiveSoilImport(candidate))
            {
                failedCandidates.Add(candidate.Key);
                continue;
            }

            session.ActiveSoilImportOrigins.Add(candidate.Target.OriginTileCoord);
            session.ActiveSoilImportTrucks[candidate.Target.OriginTileCoord] = candidate.Truck;
            dispatched++;
            eligibleOrigins.RemoveAll(origin => origin.Origin == candidate.Target.OriginTileCoord);
            if (eligibleOrigins.Count == 0)
                break;
        }

        int pending = fillingOrigins.Count - dispatched;
        session.LastActiveSoilImportDetail = FarmingTrFormat(
            "active_soil_import_status",
            "Active soil import: dispatched={0}, pending={1}, routeBlocked={2}, sourceUnavailable={3}, noEligibleTruck={4}, unreachable={5}.",
            dispatched,
            pending,
            routeBlocked,
            unavailableSources,
            noEligibleTruck,
            unreachable);
        if (dispatched > 0)
            LogRuntimeDebug("[ATD Farming] " + session.LastActiveSoilImportDetail);
    }

    private static void PruneActiveSoilImportSlots(
        FarmingPreparationSession session,
        ISet<Tile2i> fillingOrigins,
        ISet<TerrainDesignation> vanillaDumpingClaims)
    {
        foreach (Tile2i origin in session.ActiveSoilImportOrigins.ToList())
        {
            if (!fillingOrigins.Contains(origin))
            {
                session.ActiveSoilImportOrigins.Remove(origin);
                session.ActiveSoilImportTrucks.Remove(origin);
                session.ActiveSoilImportNoClaimTicks.Remove(origin);
                continue;
            }

            if (s_desigManager == null)
                continue;
            var designationOption = s_desigManager.GetDesignationAt(origin);
            if (!designationOption.HasValue)
            {
                session.ActiveSoilImportOrigins.Remove(origin);
                session.ActiveSoilImportTrucks.Remove(origin);
                continue;
            }

            TerrainDesignation designation = designationOption.Value;
            if (designation.IsDumpingFulfilled)
            {
                session.ActiveSoilImportOrigins.Remove(origin);
                session.ActiveSoilImportTrucks.Remove(origin);
                continue;
            }

            // A reserved target or a truck still carrying cargo means the vanilla
            // chain is still live. Once both disappear, allow a later pass to
            // re-enter through the normal no-claim grace period.
            bool liveChain = designation.NumberOfJobsAssigned > 0
                || vanillaDumpingClaims.Contains(designation);
            if (!liveChain
                && session.ActiveSoilImportTrucks.TryGetValue(origin, out Truck? truck)
                && truck != null
                && !truck.IsDestroyed)
            {
                liveChain = truck.HasJobs || truck.IsNotEmpty;
            }

            if (!liveChain)
            {
                session.ActiveSoilImportOrigins.Remove(origin);
                session.ActiveSoilImportTrucks.Remove(origin);
                session.ActiveSoilImportNoClaimTicks[origin] = 0;
            }
        }
    }

    private static HashSet<TerrainDesignation> CollectVanillaDumpingClaims()
    {
        var claims = new HashSet<TerrainDesignation>();
        if (s_vehiclesManager == null)
            return claims;

        foreach (Truck truck in s_vehiclesManager.Trucks)
        {
            if (truck == null || truck.IsDestroyed)
                continue;
            for (int index = 0; index < truck.Jobs.Count; index++)
            {
                if (truck.Jobs[index] is DumpingJob dumpingJob
                    && !dumpingJob.PrimaryDesignation.IsDestroyed)
                    claims.Add(dumpingJob.PrimaryDesignation);
            }
        }
        return claims;
    }

    private static ActiveSoilImportCandidate? FindBestActiveSoilImportCandidate(
        IReadOnlyList<FarmingOriginSession> eligibleOrigins,
        IReadOnlyList<RegisteredOutputBuffer> sources,
        ISet<LooseProductProto> allowedProducts,
        ISet<string> failedCandidates,
        ref int routeBlocked,
        ref int unavailableSources,
        ref int unreachable,
        ref int noEligibleTruck)
    {
        ActiveSoilImportCandidate? best = null;
        foreach (RegisteredOutputBuffer source in sources)
        {
            try { source.RefreshPriorities(); }
            catch
            {
                unavailableSources++;
                continue;
            }
            if (!source.IsEnabled || !source.IsAvailableCached || !source.AvailableQuantityCached.IsPositive)
            {
                unavailableSources++;
                continue;
            }

            if (!TryGetFarmableProduct(source, out LooseProductProto product)
                || !allowedProducts.Contains(product))
                continue;

            bool routeMatched = false;
            bool truckMatched = false;
            foreach (FarmingOriginSession originState in eligibleOrigins)
            {
                if (s_desigManager == null)
                    continue;
                var designationOption = s_desigManager.GetDesignationAt(originState.Origin);
                if (!designationOption.HasValue || !IsDumpingDesignation(designationOption.Value))
                    continue;
                TerrainDesignation designation = designationOption.Value;
                if (designation.IsDumpingFulfilled || !designation.CanBeAssigned(false))
                    continue;

                var targetTowers = GetLiveFarmTargetTowers(designation, product);
                if (targetTowers.Count == 0)
                    continue;

                foreach (MineTower targetTower in targetTowers)
                {
                    if (!IsSourceAllowedForTarget(source, targetTower))
                    {
                        routeBlocked++;
                        continue;
                    }
                    routeMatched = true;

                    foreach (Truck truck in s_vehiclesManager!.Trucks)
                    {
                        if (IsKnownActiveSoilImportUnreachable(source, designation, truck))
                        {
                            unreachable++;
                            continue;
                        }
                        if (!IsEligibleActiveSoilImportTruck(source, designation, targetTower, truck))
                        {
                            continue;
                        }
                        truckMatched = true;
                        var candidate = new ActiveSoilImportCandidate(
                            source,
                            product,
                            designation,
                            targetTower,
                            truck,
                            source.CombinedPriorityCached,
                            source.Position2f.DistanceTo(designation.CenterTileCoord.CenterTile2f).ToIntFloored(),
                            source.Position2f.DistanceTo(truck.Position2f).ToIntFloored());
                        if (failedCandidates.Contains(candidate.Key))
                            continue;
                        if (best == null || CompareActiveSoilImportCandidates(candidate, best) < 0)
                            best = candidate;
                    }
                }
            }

            if (!routeMatched)
                routeBlocked++;
            else if (!truckMatched)
                noEligibleTruck++;
        }

        return best;
    }

    private static List<MineTower> GetLiveFarmTargetTowers(
        TerrainDesignation designation,
        LooseProductProto product)
    {
        var result = new List<MineTower>();
        TerrainDesignation.AssignedTowers.Enumerator enumerator = designation.ManagedByTowers.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current is MineTower tower
                && !tower.IsDestroyed
                && tower.IsEnabled
                && tower.DumpableProducts.Contains(product))
            {
                result.Add(tower);
            }
        }
        return result;
    }

    private static bool IsSourceAllowedForTarget(
        RegisteredOutputBuffer source,
        MineTower targetTower)
    {
        if (targetTower.AssignedOutputStorages.IsNotEmpty())
        {
            if (!source.EntityAsAssignee.HasValue)
                return false;
            if (!targetTower.AssignedOutputStorages.Contains(source.EntityAsAssignee.Value))
                return false;
        }
        else if (targetTower.AssignedOutputTowers.IsNotEmpty() && !targetTower.AllowNonAssignedOutput)
        {
            if (!source.EntityAsAssignee.HasValue
                || source.EntityAsAssignee.Value is not MineTower sourceTower
                || !targetTower.AssignedOutputTowers.Contains(sourceTower))
                return false;
        }

        if (source.EntityAsAssignee.HasValue
            && source.EntityAsAssignee.Value.AssignedInputs.IsNotEmpty()
            && !source.EntityAsAssignee.Value.AssignedInputs.Contains(targetTower))
        {
            return false;
        }

        return true;
    }

    private static bool IsEligibleActiveSoilImportTruck(
        RegisteredOutputBuffer source,
        TerrainDesignation target,
        MineTower targetTower,
        Truck truck)
    {
        try
        {
            if (truck == null || !truck.IsAvailableToBalanceCargo())
                return false;
            if (truck.ProductType.HasValue && !truck.ProductType.Value.Matches(source.Product.Type))
                return false;
            if (!source.IsTruckAllowed(truck, s_activeSoilImportTruckJobsFilter!))
                return false;
            if (!source.CanBeServedBy(truck, isBalancingJob: true))
                return false;

            ulong towerOrSourceZones = source.ZoneMask | targetTower.ZoneMask;
            if ((towerOrSourceZones & truck.ZoneMask) == 0L)
                return false;

            IEntityAssignedWithVehicles? assignedTo = truck.AssignedTo.ValueOrNull;
            // Mine-tower-assigned trucks are escort vehicles, not dumping
            // vehicles. A default-provider truck assigned to a source storage
            // remains eligible under vanilla's assigned-building rule.
            if (assignedTo != null && assignedTo != source.Entity)
                return false;

            if (!target.IsReadyToDump(truck.Prototype))
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsKnownActiveSoilImportUnreachable(
        RegisteredOutputBuffer source,
        TerrainDesignation target,
        Truck truck)
    {
        if (truck == null || s_activeSoilImportUnreachables == null)
            return false;
        try
        {
            return s_activeSoilImportUnreachables.GetUnreachableEntitiesFor(truck).Contains(source.Entity)
                || s_activeSoilImportUnreachables.GetUnreachableDesignationsFor(truck).Contains(target);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryAssignActiveSoilImport(ActiveSoilImportCandidate candidate)
    {
        try
        {
            candidate.Source.RefreshPriorities();
            if (!candidate.Source.IsAvailableCached || !candidate.Source.AvailableQuantityCached.IsPositive)
                return false;
            if (!candidate.Target.CanBeAssigned(false)
                || !candidate.Target.IsReadyToDump(candidate.Truck.Prototype)
                || !candidate.Truck.IsAvailableToBalanceCargo())
                return false;

            Quantity quantity = candidate.Source.AvailableQuantityCached.Min(candidate.Truck.Capacity);
            if (!quantity.IsPositive)
                return false;

            var spec = new BalancingJobSpec(
                candidate.Truck,
                candidate.Target,
                new Lyst<TerrainDesignation>(),
                candidate.Source,
                new ProductQuantity(candidate.Product, quantity));
            candidate.Truck.AssignBalancingJob(spec);
            return candidate.Truck.HasJobs;
        }
        catch (Exception ex)
        {
            LogRuntimeDebug("[ATD Farming] Active soil import assignment failed; vanilla reservation/job creation rejected candidate: " + ex.Message);
            return false;
        }
    }

    private static int CompareActiveSoilImportCandidates(
        ActiveSoilImportCandidate left,
        ActiveSoilImportCandidate right)
    {
        return CompareActiveSoilImportOrdering(
            left.Priority,
            left.TargetDistance,
            left.TruckDistance,
            left.SourceId,
            left.Target.OriginTileCoord.Y,
            left.Target.OriginTileCoord.X,
            left.TruckId,
            right.Priority,
            right.TargetDistance,
            right.TruckDistance,
            right.SourceId,
            right.Target.OriginTileCoord.Y,
            right.Target.OriginTileCoord.X,
            right.TruckId);
    }

    internal static int CompareActiveSoilImportOrdering(
        int leftPriority,
        int leftTargetDistance,
        int leftTruckDistance,
        string leftSourceId,
        int leftTargetY,
        int leftTargetX,
        string leftTruckId,
        int rightPriority,
        int rightTargetDistance,
        int rightTruckDistance,
        string rightSourceId,
        int rightTargetY,
        int rightTargetX,
        string rightTruckId)
    {
        int comparison = leftPriority.CompareTo(rightPriority);
        if (comparison != 0)
            return comparison;
        comparison = leftTargetDistance.CompareTo(rightTargetDistance);
        if (comparison != 0)
            return comparison;
        comparison = leftTruckDistance.CompareTo(rightTruckDistance);
        if (comparison != 0)
            return comparison;
        comparison = string.CompareOrdinal(leftSourceId, rightSourceId);
        if (comparison != 0)
            return comparison;
        comparison = leftTargetY.CompareTo(rightTargetY);
        if (comparison != 0)
            return comparison;
        comparison = leftTargetX.CompareTo(rightTargetX);
        if (comparison != 0)
            return comparison;
        return string.CompareOrdinal(leftTruckId, rightTruckId);
    }

    private static bool TryGetFarmableProduct(
        RegisteredOutputBuffer source,
        out LooseProductProto product)
    {
        Option<LooseProductProto> dumpableProduct = source.Product.DumpableProduct;
        if (dumpableProduct.HasValue
            && dumpableProduct.Value.TerrainMaterial.HasValue
            && dumpableProduct.Value.TerrainMaterial.Value.IsFarmable)
        {
            product = dumpableProduct.Value;
            return true;
        }

        product = null!;
        return false;
    }
}

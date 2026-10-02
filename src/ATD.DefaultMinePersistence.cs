// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Vehicles.Excavators;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.Vehicles.Trucks;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static void AppendDefaultMineState(StringBuilder json) => s_defaultMine?.AppendState(json);
        private static void LoadDefaultMineState(Dict<string, object> root) => s_defaultMine?.LoadState(root);

        private sealed partial class DefaultMine
        {
            internal void PrepareForSave()
            {
                if (m_saving) return;
                m_saving = true;
                // The bucket remains native cargo. No products are removed and
                // recreated, so this path cannot duplicate product accounting.
                foreach (Session session in m_sessions.Values.ToArray())
                    if (!session.Excavator.IsDestroyed)
                    {
                        InternallyCancel(() =>
                        {
                            // CancelAll skips jobs already marked cancelled.
                            // Clear also removes these before serialization.
                            session.Excavator.Jobs.CancelAllAndClear();
                            session.Excavator.CancelAllJobsAndResetState();
                        });
                        // Native cancellation does not reset forced unloading or
                        // the remembered truck. Save an ordinary idle excavator
                        // that vanilla can run without the Default Mine patches.
                        session.Excavator.m_forceUnloadToTruck = false;
                        session.Excavator.m_loadedTruck = Option<Truck>.None;
                        session.Excavator.State = ExcavatorState.Idle;
                        session.Excavator.m_previousState = ExcavatorState.Idle;
                        session.Excavator.ResetCabinTarget();
                        session.Excavator.ResetShovelState();
                        session.Excavator.TruckQueue.Disable();
                        session.Mining = null;
                        session.Unloading = null;
                    }
                foreach (Pair pair in m_trucks.Values.ToArray())
                    if (!pair.Truck.IsDestroyed && (!pair.Delivering
                        || (pair.Pickup != null && !pair.Pickup.IsDestroyed
                            && pair.Truck.Jobs.AllJobs.Contains(pair.Pickup))))
                    {
                        InternallyCancel(() =>
                        {
                            pair.Truck.Jobs.CancelAllAndClear();
                            pair.Truck.CancelAllJobsAndResetState();
                        });
                        pair.Pickup = null;
                    }
                foreach (Session session in m_sessions.Values)
                    if (session.Excavator.Jobs.AllJobs.Any(job => job is MiningJob))
                        throw new InvalidOperationException("Default Mine mining job survived save suspension.");
            }

            internal void ResumeAfterSave()
            {
                if (!m_saving) return;
                m_saving = false;
                foreach (Pair pair in m_trucks.Values.ToArray())
                    if (!pair.Delivering && pair.Owner != null
                        && IsEligible(pair.Owner.Excavator) && pair.Truck.IsEnabled
                        && !pair.Truck.IsDestroyed && pair.Truck.AssignedTo.IsNone)
                        EnqueuePickup(pair);
                foreach (Session session in m_sessions.Values.ToArray()) Wake(session.Excavator);
            }

            internal void AppendState(StringBuilder json)
            {
                json.Append(",\"defaultMine\":{\"schemaVersion\":1,\"excavators\":[");
                bool first = true;
                foreach (Session session in m_sessions.Values.OrderBy(item => item.Excavator.Id.Value))
                {
                    if (session.Excavator.IsDestroyed) continue;
                    if (!first) json.Append(',');
                    first = false;
                    json.Append("{\"excavator\":").Append(session.Excavator.Id.Value)
                        .Append(",\"releasedFromTower\":").Append(IdleReleasedTowerId(session.Excavator)).Append('}');
                }
                json.Append("],\"trucks\":[");
                first = true;
                foreach (Pair pair in m_trucks.Values.OrderBy(item => item.Truck.Id.Value))
                {
                    if (pair.Truck.IsDestroyed) continue;
                    if (!first) json.Append(',');
                    first = false;
                    json.Append("{\"truck\":").Append(pair.Truck.Id.Value)
                        .Append(",\"releasedFromTower\":").Append(IdleReleasedTowerId(pair.Truck))
                        .Append(",\"excavator\":").Append(pair.Owner?.Excavator.Id.Value ?? 0)
                        .Append(",\"delivering\":");
                    AppendJsonBool(json, pair.Delivering);
                    json.Append(",\"zones\":[").Append(string.Join(",", pair.ZoneIds)).Append("]}");
                }
                json.Append("]}");
            }

            internal void LoadState(Dict<string, object> root)
            {
                if (!root.TryGetValue("defaultMine", out object raw) || !(raw is Dict<string, object> state)) return;
                if (!TryGetInt(state, "schemaVersion", out int schema) || schema != 1)
                { s_log.Warning("Default Mine: unsupported state schema; using disabled defaults."); return; }
                Enabled = DefaultMinePatches.Ready
                    && root.TryGetValue("worldSettings", out object rawSettings)
                    && rawSettings is Dict<string, object> settings
                    && TryGetBool(settings, "defaultMineEnabled", out bool enabled) && enabled;
                if (Enabled && state.TryGetValue("excavators", out object rawExcavators) && rawExcavators is object[] excavators)
                    foreach (object entry in excavators)
                        if (entry is Dict<string, object> record
                            && TryGetInt(record, "excavator", out int id) && id > 0
                            && m_entities.TryGetEntity<Excavator>(new EntityId(id), out Excavator excavator)
                            && !excavator.IsDestroyed && !excavator.HasTrueJob && !m_sessions.ContainsKey(excavator.Id))
                        {
                            // Ownership guards preserve native bucket cargo while
                            // undoing the exact save-time idle-release assignment.
                            m_sessions.Add(excavator.Id, new Session(excavator));
                            if (TryGetInt(record, "releasedFromTower", out int towerId))
                                RestoreIdleRelease(excavator, towerId);
                            if (!IsEligible(excavator)) m_sessions.Remove(excavator.Id);
                        }
                if (state.TryGetValue("trucks", out object rawTrucks) && rawTrucks is object[] trucks)
                    foreach (object entry in trucks)
                    {
                        if (!(entry is Dict<string, object> record)
                            || !TryGetInt(record, "truck", out int id) || id <= 0
                            || !TryGetBool(record, "delivering", out bool delivering)
                            || !m_entities.TryGetEntity<Truck>(new EntityId(id), out Truck truck)
                            || truck.IsDestroyed
                            || TryGetOreSorterForAssignedTruck(truck.Id, out _) || m_trucks.ContainsKey(truck.Id)) continue;
                        Session? owner = null;
                        if (TryGetInt(record, "excavator", out int ownerId)) m_sessions.TryGetValue(new EntityId(ownerId), out owner);
                        if (!delivering && (owner == null || owner.Truck != null || truck.HasTrueJob || !truck.IsEnabled)) continue;
                        if (!record.TryGetValue("zones", out object rawZones) || !(rawZones is object[] zoneIds)) continue;
                        int[] zones = zoneIds.Select(item => TryGetIntValue(item, out int zone) ? zone : -1).ToArray();
                        if (zones.Any(zone => zone <= 0)) continue;
                        zones = zones.Distinct().ToArray();
                        if (TryGetInt(record, "releasedFromTower", out int towerId)) RestoreIdleRelease(truck, towerId);
                        if (truck.AssignedTo.HasValue) continue;
                        if (owner?.Truck != null) owner = null;
                        var pair = new Pair(truck, owner, zones) { Delivering = delivering };
                        m_trucks.Add(truck.Id, pair);
                        if (owner != null && owner.Truck == null) owner.Truck = pair;
                        if (!delivering) EnqueuePickup(pair);
                    }
                // Wake through the native provider on the next simulation callback.
                InterlockedDefaultMineRestore();
            }

            private void InterlockedDefaultMineRestore()
                => System.Threading.Interlocked.Exchange(ref s_defaultMineRequested, Enabled ? 1 : 0);

            private static int IdleReleasedTowerId(Vehicle vehicle)
                => s_idleReleasedVehiclesByTower.FirstOrDefault(item => item.Value.Contains(vehicle)).Key.Value;

            private void RestoreIdleRelease(Vehicle vehicle, int savedTowerId)
            {
                if (savedTowerId <= 0 || !(vehicle.AssignedTo.ValueOrNull is MineTower tower)
                    || tower.Id.Value != savedTowerId || tower.IsDestroyed || !tower.IsConstructed
                    || tower.ConstructionState == ConstructionState.PendingDeconstruction
                    || tower.ConstructionState == ConstructionState.InDeconstruction
                    || tower.ConstructionState == ConstructionState.Deconstructed) return;
                GetIdleVehicleReleaseFlagsForId(tower.Id, out bool releaseExcavators, out bool releaseTrucks);
                if (!ShouldReleaseVehicle(vehicle, releaseExcavators, releaseTrucks)
                    || HasPendingWork(tower, tower.Id)) return;
                bool wasSaving = m_saving;
                m_saving = true;
                try
                {
                    tower.UnassignVehicle(vehicle, true);
                    if (!s_idleReleasedVehiclesByTower.TryGetValue(tower.Id, out List<Vehicle> released))
                    {
                        released = new List<Vehicle>();
                        s_idleReleasedVehiclesByTower.Add(tower.Id, released);
                    }
                    if (!released.Contains(vehicle)) released.Add(vehicle);
                }
                finally { m_saving = wasSaving; }
            }
        }
    }
}

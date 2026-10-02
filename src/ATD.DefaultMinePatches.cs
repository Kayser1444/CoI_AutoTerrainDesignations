// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mafi;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Vehicles.Excavators;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Core.Vehicles.Trucks.JobProviders;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static class DefaultMinePatches
        {
            internal static bool Ready { get; private set; }

            internal static void Apply(Harmony harmony)
            {
                Ready = false;
                try
                {
                    Patch(harmony, typeof(MiningJob), ".ctor", null, null, nameof(AssignmentAssertionTranspiler));
                    Patch(harmony, typeof(MiningJob), "DoJobInternal", nameof(MiningStepPrefix), null, nameof(AssignmentAssertionTranspiler));
                    Patch(harmony, typeof(MiningJob), "cleanup", null, null, nameof(CleanupCargoTranspiler));
                    Patch(harmony, typeof(MiningJob), "isControlledByAssignedTower", nameof(OwnershipPrefix));
                    Patch(harmony, typeof(MiningJob.Factory), "TryCreateAndEnqueueJob", nameof(AcquirePrefix));
                    Patch(harmony, typeof(MiningJob), "handleMining", nameof(ScoopPrefix));
                    Patch(harmony, typeof(Excavator), "handleWaitingForTruck", nameof(WaitingForTruckPrefix));
                    Patch(harmony, typeof(Excavator), "ClearCargoImmediately", nameof(ClearBucketPrefix));
                    Patch(harmony, typeof(MiningJob), "RequestCancelReturnDeadline", nameof(MiningCancelPrefix));
                    Patch(harmony, typeof(Vehicle), "OnAssignTo", nameof(VehicleInterruptPrefix));
                    Patch(harmony, typeof(Vehicle), "GoTo", nameof(VehicleInterruptPrefix));
                    Patch(harmony, typeof(Vehicle), "OnEnabledChanged", nameof(VehicleEnabledPrefix));
                    Patch(harmony, typeof(Excavator), "SkipNoMovementMonitoring", null, nameof(WaitingMonitoringPostfix));
                    Patch(harmony, typeof(Vehicle), "get_ZoneMask", null, nameof(ZoneMaskPostfix));
                    Patch(harmony, typeof(Truck), "IsAvailableToBalanceCargo", null, nameof(AvailabilityPostfix));
                    Patch(harmony, typeof(DefaultTruckJobProvider), "TryGetJobFor", nameof(TruckJobPrefix));
                    Patch(harmony, typeof(VehicleJob), "RequestCancel", nameof(JobCancelPrefix));
                    Patch(harmony, typeof(PathFindingEntity), "CancelAllJobsAndResetState", nameof(CancelAllPrefix));
                    Ready = true;
                    LogInfo("Default Mine native integrations installed.");
                }
                catch (Exception ex)
                {
                    s_log.Exception(ex, "Default Mine integrations unavailable; feature remains disabled");
                }
            }

            private static void Patch(Harmony harmony, Type type, string method,
                string? prefix = null, string? postfix = null, string? transpiler = null)
            {
                MethodBase? target = method == ".ctor"
                    ? type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .SingleOrDefault(ctor => ctor.GetParameters().Length == 7)
                    : AccessTools.Method(type, method);
                if (target == null) throw new MissingMethodException(type.FullName, method);
                HarmonyMethod? Hook(string? name) => name == null ? null
                    : new HarmonyMethod(typeof(DefaultMinePatches), name);
                harmony.Patch(target, Hook(prefix), Hook(postfix), Hook(transpiler));
            }

            /// <summary>
            /// Replace only the verified assignment assertion sequence. The
            /// excavator stays genuinely unassigned, including during this call.
            /// A changed native sequence disables the feature instead of silently
            /// removing unrelated assertions.
            /// </summary>
            private static IEnumerable<CodeInstruction> AssignmentAssertionTranspiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList();
                MethodInfo getter = AccessTools.PropertyGetter(typeof(Vehicle), nameof(Vehicle.AssignedTo));
                int count = 0;
                for (int i = 0; i + 3 < code.Count; i++)
                {
                    if (!code[i].Calls(getter)
                        || !(code[i + 1].operand is MethodInfo assertion) || assertion.DeclaringType != typeof(Assert)
                        || assertion.Name != nameof(Assert.That)
                        || code[i + 2].opcode != OpCodes.Ldstr
                        || !(code[i + 3].operand is MethodInfo hasValue)
                        || hasValue.DeclaringType != typeof(OptionAssertionExtensions)
                        || hasValue.Name != "HasValue") continue;
                    if (code.Skip(i + 1).Take(3).Any(item => item.labels.Count != 0 || item.blocks.Count != 0))
                        throw new InvalidOperationException("Native mining assertion has unexpected control flow.");
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = AccessTools.Method(typeof(DefaultMinePatches), nameof(AssertMiningAssignment));
                    code.RemoveRange(i + 1, 3);
                    count++;
                }
                if (count != 1) throw new InvalidOperationException($"Expected one mining assignment assertion, found {count}.");
                return code;
            }

            private static void AssertMiningAssignment(Excavator excavator)
            {
                if (s_defaultMine?.Owns(excavator) == true && excavator.AssignedTo.IsNone) return;
                Assert.That(excavator.AssignedTo).HasValue();
            }

            private static IEnumerable<CodeInstruction> CleanupCargoTranspiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList();
                MethodInfo clear = AccessTools.Method(typeof(Excavator), nameof(Excavator.ClearCargoImmediately));
                int count = 0;
                foreach (CodeInstruction instruction in code)
                    if (instruction.Calls(clear))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = AccessTools.Method(typeof(DefaultMinePatches), nameof(ClearMiningCargo));
                        count++;
                    }
                if (count != 1) throw new InvalidOperationException($"Expected one mining cargo cleanup, found {count}.");
                return code;
            }

            private static void ClearMiningCargo(Excavator excavator)
            {
                // Normal completion/coverage handoff must not discard a finished
                // scoop. Player interruption removes ownership before cleanup.
                if (s_defaultMine?.Owns(excavator) != true) excavator.ClearCargoImmediately();
            }

            private static bool AcquirePrefix(Excavator excavator, ref bool __result)
            {
                if (excavator.AssignedTo.HasValue || s_defaultMine == null) return true;
                __result = s_defaultMine.TryAcquire(excavator);
                return false;
            }

            private static bool MiningStepPrefix(MiningJob __instance, ref bool __result)
            {
                if (s_defaultMine?.BeforeMiningStep(__instance) != false) return true;
                __result = false;
                return false;
            }

            private static bool OwnershipPrefix(MiningJob __instance,
                Mafi.Core.Terrain.Designation.TerrainDesignation designation, ref bool __result)
            {
                if (s_defaultMine?.Owns(__instance) != true) return true;
                __result = s_defaultMine.Controls(__instance, designation);
                return false;
            }

            private static void ScoopPrefix(MiningJob __instance)
            {
                if (__instance.StateChanged) s_defaultMine?.ScoopStarted(__instance);
            }

            private static bool WaitingForTruckPrefix(Excavator __instance, ref ExcavatorState __result)
            {
                if (s_defaultMine?.HoldScoop(__instance) != true) return true;
                __result = ExcavatorState.WaitingForTruck;
                return false;
            }

            private static bool ClearBucketPrefix(Excavator __instance)
                => s_defaultMine?.PreserveBucket(__instance) != true;

            private static void MiningCancelPrefix(MiningJob __instance)
            {
                if (s_defaultMine?.Owns(__instance) == true)
                    s_defaultMine.Interrupt(__instance.m_excavator);
            }

            private static void VehicleInterruptPrefix(Vehicle __instance)
            {
                if (__instance is Excavator excavator) s_defaultMine?.Interrupt(excavator);
                else if (__instance is Truck truck) s_defaultMine?.TruckInterrupted(truck);
            }

            private static void VehicleEnabledPrefix(Vehicle __instance)
            { if (!__instance.IsEnabled) VehicleInterruptPrefix(__instance); }

            private static void CancelAllPrefix(PathFindingEntity __instance)
            { if (__instance is Vehicle vehicle) VehicleInterruptPrefix(vehicle); }

            private static void WaitingMonitoringPostfix(Excavator __instance, ref bool __result)
            {
                if (s_defaultMine?.Owns(__instance) == true
                    && __instance.State == ExcavatorState.WaitingForTruck) __result = true;
            }

            private static void ZoneMaskPostfix(Vehicle __instance, ref ulong __result)
            {
                if (s_defaultMine != null && s_defaultMine.TryGetMask(__instance, out ulong mask)) __result = mask;
            }

            private static void AvailabilityPostfix(Truck __instance, ref bool __result)
            {
                if (s_defaultMine?.Owns(__instance) == true) __result = false;
            }

            private static bool TruckJobPrefix(Truck truck, ref bool __result)
            {
                if (s_defaultMine == null || !s_defaultMine.TryTruckJob(truck, out bool job)) return true;
                __result = job;
                return false;
            }

            private static void JobCancelPrefix(VehicleJob __instance)
            {
                if (__instance is VehicleQueueJob<Truck> pickup) s_defaultMine?.PickupCancelled(pickup);
            }
        }

        internal static void ApplyDefaultMinePatches(Harmony harmony) => DefaultMinePatches.Apply(harmony);
    }
}

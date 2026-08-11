// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository
// is intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Truck idle policy
using System;
using System.Reflection;
using HarmonyLib;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.Vehicles.Trucks;

namespace AutoTerrainDesignations;

internal static class TruckIdlePolicyPatches
{
    internal static void Apply(Harmony harmony)
    {
        try
        {
            MethodInfo? parkingMethod = typeof(ParkAndWaitJobFactory).GetMethod(
                nameof(ParkAndWaitJobFactory.TryEnqueueParkingJobIfNeeded),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (parkingMethod == null)
            {
                AutoDepthDesignation.s_log.Warning("ParkAndWaitJobFactory.TryEnqueueParkingJobIfNeeded not found; Stay put is unavailable.");
                return;
            }

            harmony.Patch(
                parkingMethod,
                prefix: new HarmonyMethod(
                    typeof(TruckIdlePolicyPatches),
                    nameof(TryEnqueueParkingJobIfNeeded_Prefix)));
            LogDebug("Patched mine-tower truck parking for the Stay put policy.");
        }
        catch (Exception ex)
        {
            AutoDepthDesignation.s_log.Error("Exception applying truck idle policy patch: " + ex);
        }
    }

    private static bool TryEnqueueParkingJobIfNeeded_Prefix(
        Vehicle vehicle,
        ILayoutEntity staticEntity)
    {
        if (vehicle is Truck
            && staticEntity is MineTower mineTower
            && AutoDepthDesignation.GetTowerTruckIdlePolicy(mineTower)
                == TruckIdleBehavior.StayPut)
        {
            return false;
        }

        return true;
    }

    private static void LogDebug(string message)
    {
        AtdDiagnostics.Debug(AutoDepthDesignation.s_log, message);
    }
}

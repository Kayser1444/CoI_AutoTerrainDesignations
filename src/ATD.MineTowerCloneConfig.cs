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
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Products;
using Mafi.Core.Terrain;
using Mafi.Serialization;

namespace AutoTerrainDesignations;

/// <summary>
/// Adds ATD's player-authored Mine Tower configuration to the vanilla
/// <see cref="IEntityWithCloneableConfig"/> seam.
///
/// The values are stored in the vanilla <see cref="EntityConfigData"/> bag,
/// using only core scalar/proto/byte-array fields. This is deliberate: a
/// blueprint must remain loadable if ATD is later uninstalled. Vanilla simply
/// ignores these unknown keys, while an installed ATD can apply them during
/// every normal configuration workflow (copy, blueprint placement, cut, and
/// copy-settings).
/// </summary>
internal static class MineTowerCloneConfigPatches
{
    private const string KeyPrefix = "ATD.MineTower.CloneConfig.";
    private const string AreaPresentKey = KeyPrefix + "AreaPresent";
    private const string AreaKey = KeyPrefix + "Area";
    private const string SettingsPresentKey = KeyPrefix + "SettingsPresent";
    private const string MaxHeightDiffKey = KeyPrefix + "MaxHeightDiff";
    private const string VehicleClearanceKey = KeyPrefix + "VehicleClearance";
    private const string MaxLayersToExcavateKey = KeyPrefix + "MaxLayersToExcavate";
    private const string MaxDepthToDigToKey = KeyPrefix + "MaxDepthToDigTo";
    private const string MaxDepthHasValueKey = KeyPrefix + "MaxDepthHasValue";
    private const string OrePurityLevelKey = KeyPrefix + "OrePurityLevel";
    private const string AutoReleaseExcavatorsKey = KeyPrefix + "AutoReleaseExcavators";
    private const string TruckIdlePolicyKey = KeyPrefix + "TruckIdlePolicy";
    private const string DumpingPriorityKey = KeyPrefix + "DumpingPriority";
    private const string SelectedOrePresentKey = KeyPrefix + "SelectedOrePresent";
    private const string SelectedOreKey = KeyPrefix + "SelectedOre";
    private const string ExcavatorPriorityPresentKey = KeyPrefix + "ExcavatorPriorityPresent";
    private const string ExcavatorPriorityKey = KeyPrefix + "ExcavatorPriority";

    internal static IReadOnlyCollection<string> SchemaKeys { get; } = new[]
    {
        AreaPresentKey,
        AreaKey,
        SettingsPresentKey,
        MaxHeightDiffKey,
        VehicleClearanceKey,
        MaxLayersToExcavateKey,
        MaxDepthToDigToKey,
        MaxDepthHasValueKey,
        OrePurityLevelKey,
        AutoReleaseExcavatorsKey,
        TruckIdlePolicyKey,
        DumpingPriorityKey,
        SelectedOrePresentKey,
        SelectedOreKey,
        ExcavatorPriorityPresentKey,
        ExcavatorPriorityKey,
    };

    internal static void Apply(Harmony harmony)
    {
        if (!MineTowerCloneConfigFixtures.ValidateAll(out string fixtureFailure))
            AutoDepthDesignation.s_log.Warning("[ATD Clone] Configuration schema fixture failed: " + fixtureFailure);

        PatchCloneMethod(
            harmony,
            "AddToConfig",
            nameof(AddToConfigPostfix),
            "capture");
        PatchCloneMethod(
            harmony,
            "ApplyConfig",
            nameof(ApplyConfigPostfix),
            "apply");
    }

    private static void PatchCloneMethod(
        Harmony harmony,
        string interfaceMethodName,
        string postfixName,
        string operation)
    {
        try
        {
            MethodInfo? target = FindCloneMethod(interfaceMethodName);
            MethodInfo? postfix = AccessTools.Method(
                typeof(MineTowerCloneConfigPatches), postfixName);
            if (target == null || postfix == null)
            {
                AutoDepthDesignation.s_log.Warning(
                    $"[ATD Clone] Mine Tower clone-config {operation} target not found.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }
        catch (Exception ex)
        {
            // A vanilla method rename must not prevent the rest of ATD from loading.
            AutoDepthDesignation.s_log.Warning(
                $"[ATD Clone] Mine Tower clone-config {operation} patch failed: {ex.Message}");
        }
    }

    private static MethodInfo? FindCloneMethod(string interfaceMethodName)
    {
        InterfaceMapping mapping = typeof(MineTower).GetInterfaceMap(
            typeof(IEntityWithCloneableConfig));
        for (int index = 0; index < mapping.InterfaceMethods.Length; index++)
        {
            if (string.Equals(
                    mapping.InterfaceMethods[index].Name,
                    interfaceMethodName,
                    StringComparison.Ordinal))
                return mapping.TargetMethods[index];
        }

        return null;
    }

    private static void AddToConfigPostfix(MineTower __instance, EntityConfigData data)
    {
        try
        {
            if (__instance == null || data == null)
                return;

            // Preserve the polygon in absolute world coordinates. The destination
            // transform never translates, rotates, or reflects this payload.
            data.SetBool(AreaPresentKey, true);
            data.Set<PolygonTerrainArea2i>(
                AreaKey,
                __instance.Area,
                PolygonTerrainArea2i.Serialize);

            data.SetBool(SettingsPresentKey, true);
            data.SetInt(
                MaxHeightDiffKey,
                AutoDepthDesignation.GetTowerMaxHeightDiff(__instance));
            data.SetInt(
                VehicleClearanceKey,
                (int)AutoDepthDesignation.GetTowerVehicleClearance(__instance));
            data.SetInt(
                MaxLayersToExcavateKey,
                AutoDepthDesignation.GetTowerMaxLayersToExcavate(__instance));

            int? maxDepth = AutoDepthDesignation.GetTowerMaxDepthToDigTo(__instance);
            data.SetBool(MaxDepthHasValueKey, maxDepth.HasValue);
            data.SetInt(MaxDepthToDigToKey, maxDepth);
            data.SetInt(
                OrePurityLevelKey,
                AutoDepthDesignation.GetTowerOrePurityLevel(__instance));
            data.SetBool(
                AutoReleaseExcavatorsKey,
                AutoDepthDesignation.GetTowerAutoReleaseExcavatorsWhenIdle(__instance));
            data.SetInt(
                TruckIdlePolicyKey,
                (int)AutoDepthDesignation.GetTowerTruckIdlePolicy(__instance));
            data.SetInt(
                DumpingPriorityKey,
                AutoDepthDesignation.GetTowerDumpingPriority(__instance));

            ProductProto? selectedOre = AutoDepthDesignation.GetSelectedOre(__instance);
            data.SetBool(SelectedOrePresentKey, true);
            data.SetProto(
                SelectedOreKey,
                selectedOre == null
                    ? Option<ProductProto>.None
                    : Option.Some(selectedOre));

            LooseProductProto? excavatorPriority =
                AutoDepthDesignation.GetTowerExcavatorPriority(__instance);
            data.SetBool(ExcavatorPriorityPresentKey, true);
            data.SetProto(
                ExcavatorPriorityKey,
                excavatorPriority == null
                    ? Option<LooseProductProto>.None
                    : Option.Some(excavatorPriority));
        }
        catch (Exception ex)
        {
            // Configuration capture is an enhancement. Never break vanilla Copy,
            // Cut, or blueprint creation because an ATD value is unavailable.
            AutoDepthDesignation.s_log.Warning(
                "[ATD Clone] Mine Tower configuration capture failed: " + ex.Message);
        }
    }

    private static void ApplyConfigPostfix(MineTower __instance, EntityConfigData data)
    {
        try
        {
            if (__instance == null || data == null || __instance.IsDestroyed)
                return;

            if (data.GetBool(AreaPresentKey) == true)
            {
                PolygonTerrainArea2i? area =
                    data.Get(AreaKey, PolygonTerrainArea2i.Deserialize);
                if (area.HasValue)
                    __instance.SetNewArea(area.Value);
            }

            if (data.GetBool(SettingsPresentKey) == true)
            {
                AutoDepthDesignation.SetTowerMaxHeightDiff(
                    __instance,
                    data.GetInt(MaxHeightDiffKey)
                        ?? AutoTerrainDesignationsMod.MaxHeightDiff);
                AutoDepthDesignation.SetTowerVehicleClearance(
                    __instance,
                    (AccessVehicleClearanceMode)(data.GetInt(VehicleClearanceKey)
                        ?? (int)AutoTerrainDesignationsMod.VehicleClearance));
                AutoDepthDesignation.SetTowerMaxLayersToExcavate(
                    __instance,
                    data.GetInt(MaxLayersToExcavateKey)
                        ?? AutoTerrainDesignationsMod.MaxLayersToExcavate);

                bool hasMaxDepth = data.GetBool(MaxDepthHasValueKey) == true;
                AutoDepthDesignation.SetTowerMaxDepthToDigTo(
                    __instance,
                    hasMaxDepth ? data.GetInt(MaxDepthToDigToKey) : null);
                AutoDepthDesignation.SetTowerOrePurityLevel(
                    __instance,
                    data.GetInt(OrePurityLevelKey)
                        ?? AutoTerrainDesignationsMod.OrePurityLevel);
                AutoDepthDesignation.SetTowerAutoReleaseExcavatorsWhenIdle(
                    __instance,
                    data.GetBool(AutoReleaseExcavatorsKey)
                        ?? AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle);
                AutoDepthDesignation.SetTowerTruckIdlePolicy(
                    __instance,
                    (TruckIdleBehavior)(data.GetInt(TruckIdlePolicyKey)
                        ?? (int)AutoTerrainDesignationsMod.TruckIdlePolicy));
                AutoDepthDesignation.SetTowerDumpingPriority(
                    __instance,
                    data.GetInt(DumpingPriorityKey)
                        ?? AutoDepthDesignation.DumpingPriorityWorldDefault);
            }

            if (data.GetBool(SelectedOrePresentKey) == true)
            {
                AutoDepthDesignation.SetSelectedOre(
                    __instance,
                    data.GetProto<ProductProto>(SelectedOreKey, unlockedOnly: false).ValueOrNull);
            }

            if (data.GetBool(ExcavatorPriorityPresentKey) == true)
            {
                AutoDepthDesignation.SetTowerExcavatorPriority(
                    __instance,
                    data.GetProto<LooseProductProto>(
                        ExcavatorPriorityKey,
                        unlockedOnly: false).ValueOrNull);
            }
        }
        catch (Exception ex)
        {
            // A malformed/old optional payload must degrade to vanilla behavior.
            AutoDepthDesignation.s_log.Warning(
                "[ATD Clone] Mine Tower configuration apply failed: " + ex.Message);
        }
    }
}

/// <summary>Pure schema fixtures for the optional Mine Tower clone payload.</summary>
internal static class MineTowerCloneConfigFixtures
{
    internal static bool ValidateAll(out string failure)
    {
        InterfaceMapping mapping;
        try
        {
            mapping = typeof(MineTower).GetInterfaceMap(
                typeof(IEntityWithCloneableConfig));
        }
        catch (Exception ex)
        {
            failure = "The Mine Tower cloneable-config seam could not be inspected: " + ex.Message;
            return false;
        }
        bool hasCapture = false;
        bool hasApply = false;
        for (int index = 0; index < mapping.InterfaceMethods.Length; index++)
        {
            hasCapture |= string.Equals(
                mapping.InterfaceMethods[index].Name,
                "AddToConfig",
                StringComparison.Ordinal);
            hasApply |= string.Equals(
                mapping.InterfaceMethods[index].Name,
                "ApplyConfig",
                StringComparison.Ordinal);
        }
        if (!hasCapture || !hasApply)
        {
            failure = "Vanilla Mine Tower no longer exposes the cloneable-config seam.";
            return false;
        }

        var keys = new HashSet<string>(
            MineTowerCloneConfigPatches.SchemaKeys,
            StringComparer.Ordinal);
        if (keys.Count != MineTowerCloneConfigPatches.SchemaKeys.Count)
        {
            failure = "The clone payload contains duplicate keys.";
            return false;
        }

        foreach (string key in keys)
        {
            if (!key.StartsWith("ATD.MineTower.CloneConfig.", StringComparison.Ordinal))
            {
                failure = "The clone payload contains a key outside its ATD namespace.";
                return false;
            }
        }

        // Farmland Preparation is intentionally absent. It remains a runtime
        // session and new towers therefore retain vanilla's disabled default.
        foreach (string key in keys)
        {
            if (key.IndexOf("Farm", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                failure = "Farmland Preparation state must not be cloned.";
                return false;
            }
        }

        failure = string.Empty;
        return true;
    }
}

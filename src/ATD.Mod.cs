// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using System;
using System.IO;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Entities;
using Mafi.Core.Buildings.VehicleDepots;
using Mafi.Core.Game;
using Mafi.Core.GameLoop;
using Mafi.Core.Mods;
using Mafi.Core.Notifications;
using Mafi.Core.Input;
using Mafi.Core.PathFinding;
using Mafi.Core.Prototypes;
using Mafi.Core.SaveGame;
using Mafi.Core.Simulation;
using Mafi.Core.Console;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.Terrain.Trees;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.World;
using Mafi.Collections.ImmutableCollections;
using Mafi.Unity.InputControl;
using Mafi.Unity.InputControl.GameMenu.Settings;
using Mafi.Unity.Terrain.Designation;
using Mafi.Localization;
using Mafi.Unity.UiStatic;
using Mafi.Unity.UiStatic.Cursors;
using UnityEngine;
using CoI.AutoHelpers.Localization;
using CoI.AutoHelpers.Logging;
using CoI.AutoHelpers.Persistence;
using CoI.AutoHelpers.Settings;
using Mafi.Unity;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.UiToolkit;

namespace AutoTerrainDesignations;

internal enum SafetyPolicy { Min = 0, Low = 1, Med = 2, High = 3, Max = 4 }
internal enum QuickRemoveDebrisPolicy { Always = 0, Restrictive = 1, Never = 2 }

public sealed class AutoTerrainDesignationsMod : IMod, IDisposable
{
    private Harmony? m_harmony;
    private IGameLoopEvents? m_gameLoopEvents;
    private ISimLoopEvents? m_simLoopEvents;
    private ISaveManager? m_saveManager;
    private SimStep m_lastSimTick;
    private IModStateJsonStore? m_towerSettingsStateStore;
    private IModStateJsonStore? m_preAllocationsStateStore;
    private IEntitiesManager? m_entitiesManager;

    public string Name => "Auto Terrain Designations";

    public int Version => 1;

    public bool IsUiOnly => false;

    public Option<IConfig> ModConfig { get; set; }

    public ModManifest Manifest { get; }

    public static string ModVersion { get; private set; } = "?";

    public static string ModMarker => $"Kayser's AutoTerrainDesignations v{ModVersion}";

    /// <summary>Returns <paramref name="text"/> with the mod sign-off appended, for use in tooltips.</summary>
public static string Tt(string text) => text;

    public ModJsonConfig JsonConfig { get; }

    public AutoTerrainDesignationsMod(ModManifest manifest)
    {
        Manifest = manifest;
        ModVersion = manifest.Version.ToString();
        JsonConfig = new ModJsonConfig(this);
    }

    public void RegisterPrototypes(ProtoRegistrator registrator)
    {
        m_harmony = new Harmony("com.auto-terrain-designations.mod");
        bool migrateLegacyCornerKey = AutoDepthDesignation.TryLoadCornerDesignationKeyFromSettings(out KeyCode cornerKey);
        if (migrateLegacyCornerKey)
        {
            SetCornerDesignationMode(FromPrimaryKeys(cornerKey));
        }
        AutoDepthDesignation.ApplyInspectorPatches(m_harmony);
        AutoDepthDesignation.ApplyCornerPatches(m_harmony);
        AutoDepthDesignation.ApplyVehicleDepotPatches(m_harmony);
        PreAllocationPatches.Apply(m_harmony);
        AutoDepthDesignation.ApplyFarmPlacementAssistPatches(m_harmony);
        CoI.AutoHelpers.InputControl.CustomKeybindsInjector.ApplyPatches(
            m_harmony,
            Manifest.DisplayName,
            typeof(AutoTerrainDesignationsMod),
            persistInitialBindings: migrateLegacyCornerKey);

        AtdNotifications.RegisterPrototypes(registrator);
    }

    public void RegisterDependencies(DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool gameWasLoaded)
    {
    }

    public void EarlyInit(DependencyResolver resolver)
    {
    }

    public static int MaxHeightDiff { get; private set; } = 1;

    public static void ResetGlobalDefaults()
    {
        SetMaxHeightDiff(1);
        SetVehicleClearance(AccessVehicleClearanceMode.Auto);
        SetMaxLayersToExcavate(30);
        SetMaxDepthToDigTo(null);
        SetOrePurityLevel(0);
        SetBottomFlatteningEnabled(true);
        SetBottomFlatteningStrength(5);
        SetMinCorridorClearance(2);
        SetTerrainDesignationsPanelCollapsed(false);
        SetOreCompositionPanelCollapsed(false);
        SetFarmingPanelCollapsed(true);
        SetExcavatorCompletionNotificationsEnabled(true);
        SetRampNotificationsEnabled(true);
        SetAutoReleaseVehiclesWhenIdle(false);
        SetTurningRampsExperimental(true);
        SetSuppressLegacyAccessRamps(false);
        SetExperimentalAccessUseAStar(true);
        SetExperimentalAccessUsefulHeightEnvelope(true);
        TrySetExperimentalAccessV1HeightEnvelopeLowerAllowance(1f);
        TrySetExperimentalAccessV2HeightEnvelopeLowerAllowance(1.5f);
        TrySetExperimentalAccessV1HeightEnvelopeUpperAllowance(1f);
        TrySetExperimentalAccessV2HeightEnvelopeUpperAllowance(1.5f);
        SetAccessAvoidOcean(true);
        SetAccessAvoidBuildings(true);
        SetAccessHarvestDisruptedTrees(true);
        SetAccessAllowDigToRemoveDebris(true);
        SetAccessQuickRemoveDebrisPolicy(QuickRemoveDebrisPolicy.Restrictive);
        SetAccessLandscapingCostDistanceScale(1f);
        SetAccessPropCleanupLandscapingCost(8f);
        SetAccessLandslideRunPerHeight(1f);
        SetAccessGeneratedVFixedCost(1f);
        SetAccessDirectWorkWeight(1f);
        SetAccessSideRayWeight(1f);
        SetAccessRaySlopeConservatism(1f);
        SetAccessRayEndBuffer(3);
        SetAccessCandidateRayMaxDistance(16);
        SetAccessRayMaxCost(500f);
        SetAccessRayUnresolvedPenalty(200f);
        SetAccessMaxVisitedNodes(250000);
        SetAccessSearchTimeoutSeconds(60);
        SetAccessSearchFrameBudgetMs(30);
        SetCornerDesignationMode(FromPrimaryKeys(KeyCode.K));
    }

    public static void SetMaxHeightDiff(int value)
    {
        MaxHeightDiff = Math.Max(1, Math.Min(3, value));
    }

    /// <summary>Ramp width in tiles. Allowed range: 0..5. 0 disables ramp generation.</summary>
    public static int RampWidth { get; private set; } = 1;
    internal static AccessVehicleClearanceMode VehicleClearance { get; private set; } = AccessVehicleClearanceMode.Auto;
    internal static void SetVehicleClearance(AccessVehicleClearanceMode value)
    {
        VehicleClearance = value < AccessVehicleClearanceMode.Off || value > AccessVehicleClearanceMode.LegacyWidth5
            ? AccessVehicleClearanceMode.Auto
            : value;
        RampWidth = AutoDepthDesignation.RampWidthForMode(VehicleClearance);
        MinCorridorClearance = AutoDepthDesignation.CorridorClearanceForMode(VehicleClearance);
    }

    public static void SetRampWidth(int value)
    {
        SetVehicleClearance(AutoDepthDesignation.ModeForRampWidth(value));
    }

    /// <summary>Maximum number of layers to excavate from the surface. 0 = no limit.</summary>
    public static int MaxLayersToExcavate { get; private set; } = 30;

    public static void SetMaxLayersToExcavate(int value)
    {
        MaxLayersToExcavate = Math.Max(0, value);
    }

    /// <summary>Absolute minimum terrain elevation to excavate to. null = no limit.</summary>
    public static int? MaxDepthToDigTo { get; private set; } = null;

    public static void SetMaxDepthToDigTo(int? value)
    {
        MaxDepthToDigTo = value;
    }

    /// <summary>
    /// Ore purity threshold level (0=Off, 1=Low, 2=Medium, 3=High, 4=Max).
    /// Controls how aggressively poor-quality tiles and deep sparse ore are excluded.
    /// </summary>
    public static int OrePurityLevel { get; private set; } = 0;

    public static void SetOrePurityLevel(int value)
    {
        OrePurityLevel = Math.Max(0, Math.Min(4, value));
    }

    /// <summary>Whether ATD applies the extra bottom-flattening pass before placing designations.</summary>
    public static bool BottomFlatteningEnabled { get; private set; } = true;

    public static void SetBottomFlatteningEnabled(bool value)
    {
        BottomFlatteningEnabled = value;
    }

    /// <summary>
    /// Bottom-flattening aggressiveness (1–10). Controls which depth-percentile of a connected ore
    /// component is chosen as the flattening target.
    /// <list type="bullet">
    ///   <item>1 = mildest — targets the 90th-percentile depth (shallow target; only extreme outliers affected).</item>
    ///   <item>5 = moderate — targets the 50th-percentile depth (median; default).</item>
    ///   <item>10 = strongest — targets the deepest tile (all other tiles pulled down to match).</item>
    /// </list>
    /// In lower-only mode (purity = Off) tiles are only ever pulled deeper; in leveling mode they are
    /// set to the target regardless of direction.
    /// </summary>
    public static int BottomFlatteningStrength { get; private set; } = 5;

    public static void SetBottomFlatteningStrength(int value)
    {
        BottomFlatteningStrength = Math.Max(1, Math.Min(10, value));
    }

    /// <summary>
    /// Minimum corridor clearance for designation connectivity.
    /// 0 = disabled — no corridors drawn, components left separate (for vehicle-less excavation);
    /// 1 = 1-tile corridors (small/medium vehicles);
    /// 2 = 2-tile corridors (mega vehicles, current default).
    /// </summary>
    public static int MinCorridorClearance { get; private set; } = 1;

    public static void SetMinCorridorClearance(int value)
    {
        // Superseded by VehicleClearance. Retained as a no-op for source/API compatibility.
    }

    /// <summary>Default collapsed state for the Mining designations inspector panel.</summary>
    public static bool TerrainDesignationsPanelCollapsed { get; private set; } = false;

    public static void SetTerrainDesignationsPanelCollapsed(bool value)
    {
        TerrainDesignationsPanelCollapsed = value;
    }

    /// <summary>Default collapsed state for the Ore composition inspector panel.</summary>
    public static bool OreCompositionPanelCollapsed { get; private set; } = false;

    public static void SetOreCompositionPanelCollapsed(bool value)
    {
        OreCompositionPanelCollapsed = value;
    }

    /// <summary>Whether ATD shows a green notification when a vehicle depot completes an excavator.</summary>
    public static bool ExcavatorCompletionNotificationsEnabled { get; private set; } = true;

    public static void SetExcavatorCompletionNotificationsEnabled(bool value)
    {
        ExcavatorCompletionNotificationsEnabled = value;
    }

    /// <summary>Whether ATD shows ramp access warning notifications on mine towers.</summary>
    public static bool RampNotificationsEnabled { get; private set; } = true;

    public static void SetRampNotificationsEnabled(bool value)
    {
        RampNotificationsEnabled = value;
    }

    /// <summary>Default collapsed state for the Farming panel when a mine tower inspector is created.</summary>
    public static bool FarmingPanelCollapsed { get; private set; } = true;

    public static void SetFarmingPanelCollapsed(bool value)
    {
        FarmingPanelCollapsed = value;
    }

    /// <summary>Whether ATD automatically releases excavators from a tower when there are no pending excavation jobs.</summary>
    public static bool AutoReleaseExcavatorsWhenIdle { get; private set; } = false;

    /// <summary>Whether ATD automatically releases trucks from a tower when there are no pending excavation jobs.</summary>
    public static bool AutoReleaseTrucksWhenIdle { get; private set; } = false;

    /// <summary>Legacy combined view retained for old console output and config migration.</summary>
    public static bool AutoReleaseVehiclesWhenIdle => AutoReleaseExcavatorsWhenIdle || AutoReleaseTrucksWhenIdle;

    public static void SetAutoReleaseExcavatorsWhenIdle(bool value)
    {
        AutoReleaseExcavatorsWhenIdle = value;
    }

    public static void SetAutoReleaseTrucksWhenIdle(bool value)
    {
        AutoReleaseTrucksWhenIdle = value;
    }

    public static void SetAutoReleaseVehiclesWhenIdle(bool value)
    {
        SetAutoReleaseExcavatorsWhenIdle(value);
        SetAutoReleaseTrucksWhenIdle(value);
    }

    /// <summary>Enables the V1 turning-ramp search. Experimental and off by default.</summary>
    public static bool TurningRampsExperimental { get; private set; }

    public static void SetTurningRampsExperimental(bool value)
    {
        TurningRampsExperimental = value;
    }

    /// <summary>Debug/experimental switch that disables the legacy straight-ramp fallback.</summary>
    public static bool SuppressLegacyAccessRamps { get; private set; }

    public static void SetSuppressLegacyAccessRamps(bool value)
    {
        SuppressLegacyAccessRamps = value;
    }

    /// <summary>Uses A* instead of reference Dijkstra for the experimental access search.</summary>
    public static bool ExperimentalAccessUseAStar { get; private set; }

    public static void SetExperimentalAccessUseAStar(bool value)
    {
        ExperimentalAccessUseAStar = value;
    }

    /// <summary>
    /// Builds the access useful-height hull and enables experimental V1
    /// generated-center pruning. This session-only switch defaults to on.
    /// </summary>
    public static bool ExperimentalAccessUsefulHeightEnvelope { get; private set; } = true;

    public static void SetExperimentalAccessUsefulHeightEnvelope(bool value)
    {
        ExperimentalAccessUsefulHeightEnvelope = value;
    }

    public static float ExperimentalAccessV1HeightEnvelopeLowerAllowance
        => s_experimentalAccessV1HeightEnvelopeLowerAllowance32 / 32f;

    public static float ExperimentalAccessV2HeightEnvelopeLowerAllowance
        => s_experimentalAccessV2HeightEnvelopeLowerAllowance32 / 32f;

    public static float ExperimentalAccessV1HeightEnvelopeUpperAllowance
        => s_experimentalAccessV1HeightEnvelopeUpperAllowance32 / 32f;

    public static float ExperimentalAccessV2HeightEnvelopeUpperAllowance
        => s_experimentalAccessV2HeightEnvelopeUpperAllowance32 / 32f;

    internal static int ExperimentalAccessV1HeightEnvelopeLowerAllowance32
        => s_experimentalAccessV1HeightEnvelopeLowerAllowance32;

    internal static int ExperimentalAccessV2HeightEnvelopeLowerAllowance32
        => s_experimentalAccessV2HeightEnvelopeLowerAllowance32;

    internal static int ExperimentalAccessV1HeightEnvelopeUpperAllowance32
        => s_experimentalAccessV1HeightEnvelopeUpperAllowance32;

    internal static int ExperimentalAccessV2HeightEnvelopeUpperAllowance32
        => s_experimentalAccessV2HeightEnvelopeUpperAllowance32;

    private static int s_experimentalAccessV1HeightEnvelopeLowerAllowance32 = 32;
    private static int s_experimentalAccessV2HeightEnvelopeLowerAllowance32 = 48;
    private static int s_experimentalAccessV1HeightEnvelopeUpperAllowance32 = 32;
    private static int s_experimentalAccessV2HeightEnvelopeUpperAllowance32 = 48;

    public static bool TrySetExperimentalAccessV1HeightEnvelopeLowerAllowance(
        float value)
        => TrySetExperimentalAccessHeightEnvelopeAllowance(
            value, ref s_experimentalAccessV1HeightEnvelopeLowerAllowance32);

    public static bool TrySetExperimentalAccessV2HeightEnvelopeLowerAllowance(
        float value)
        => TrySetExperimentalAccessHeightEnvelopeAllowance(
            value, ref s_experimentalAccessV2HeightEnvelopeLowerAllowance32);

    public static bool TrySetExperimentalAccessV1HeightEnvelopeUpperAllowance(
        float value)
        => TrySetExperimentalAccessHeightEnvelopeAllowance(
            value, ref s_experimentalAccessV1HeightEnvelopeUpperAllowance32);

    public static bool TrySetExperimentalAccessV2HeightEnvelopeUpperAllowance(
        float value)
        => TrySetExperimentalAccessHeightEnvelopeAllowance(
            value, ref s_experimentalAccessV2HeightEnvelopeUpperAllowance32);

    private static bool TrySetExperimentalAccessHeightEnvelopeAllowance(
        float value,
        ref int destination32)
    {
        double scaled = Math.Round(
            (double)value * 32d, MidpointRounding.AwayFromZero);
        if (double.IsNaN(scaled)
            || double.IsInfinity(scaled)
            || scaled < 0d
            || scaled > int.MaxValue)
            return false;
        int converted = (int)scaled;
        if (destination32 != converted)
            AutoDepthDesignation.MarkAllMiningPlansDirty();
        destination32 = converted;
        return true;
    }

    /// <summary>Rejects accessway rays whose projected disturbance reaches ocean.</summary>
    public static bool AccessAvoidOcean { get; private set; } = true;

    public static void SetAccessAvoidOcean(bool value)
    {
        if (AccessAvoidOcean != value)
            AutoDepthDesignation.MarkAllMiningPlansDirty();
        AccessAvoidOcean = value;
    }

    /// <summary>Rejects accessway rays whose projected disturbance reaches a building safety footprint.</summary>
    public static bool AccessAvoidBuildings { get; private set; } = true;

    public static void SetAccessAvoidBuildings(bool value)
    {
        if (AccessAvoidBuildings != value)
            AutoDepthDesignation.MarkAllMiningPlansDirty();
        AccessAvoidBuildings = value;
    }

    /// <summary>Marks every tree in the finalized accessway disturbance zone for harvest.</summary>
    public static bool AccessHarvestDisruptedTrees { get; private set; } = true;

    public static void SetAccessHarvestDisruptedTrees(bool value)
    {
        if (AccessHarvestDisruptedTrees != value)
            AutoDepthDesignation.MarkAllMiningPlansDirty();
        AccessHarvestDisruptedTrees = value;
    }

    public static bool AccessAllowDigToRemoveDebris { get; private set; } = true;

    public static void SetAccessAllowDigToRemoveDebris(bool value)
    {
        AccessAllowDigToRemoveDebris = value;
    }

    internal static QuickRemoveDebrisPolicy AccessQuickRemoveDebrisPolicy { get; private set; }
        = QuickRemoveDebrisPolicy.Restrictive;

    internal static void SetAccessQuickRemoveDebrisPolicy(QuickRemoveDebrisPolicy value)
    {
        AccessQuickRemoveDebrisPolicy = value < QuickRemoveDebrisPolicy.Always
            ? QuickRemoveDebrisPolicy.Always
            : value > QuickRemoveDebrisPolicy.Never
                ? QuickRemoveDebrisPolicy.Never
                : value;
    }

    /// <summary>Tile-distance cost assigned to one unit of landscaping cost.</summary>
    public static float AccessLandscapingCostDistanceScale { get; private set; } = 1f;

    public static void SetAccessLandscapingCostDistanceScale(float value)
    {
        AccessLandscapingCostDistanceScale = Math.Max(0f, Math.Min(100f, value));
    }

    /// <summary>Landscaping cost charged once per cleanup origin used by experimental access routing.</summary>
    public static float AccessPropCleanupLandscapingCost { get; private set; } = 8f;

    public static void SetAccessPropCleanupLandscapingCost(float value)
    {
        AccessPropCleanupLandscapingCost = Math.Max(0f, Math.Min(100f, value));
    }

    /// <summary>Horizontal landslide-envelope run per vertical terrain level. 1 = 45 degrees.</summary>
    public static float AccessLandslideRunPerHeight { get; private set; } = 1f;

    public static void SetAccessLandslideRunPerHeight(float value)
    {
        AccessLandslideRunPerHeight = Math.Max(0.05f, Math.Min(2f, value));
    }

    public static float AccessGeneratedVFixedCost { get; private set; } = 1f;
    public static void SetAccessGeneratedVFixedCost(float value) => AccessGeneratedVFixedCost = Math.Max(0f, Math.Min(100f, value));
    public static float AccessDirectWorkWeight { get; private set; } = 1f;
    public static void SetAccessDirectWorkWeight(float value) => AccessDirectWorkWeight = Math.Max(0f, Math.Min(100f, value));
    public static float AccessSideRayWeight { get; private set; } = 1f;
    public static void SetAccessSideRayWeight(float value) => AccessSideRayWeight = Math.Max(0f, Math.Min(100f, value));
    public static float AccessRaySlopeConservatism { get; private set; } = 1f;
    public static void SetAccessRaySlopeConservatism(float value)
    {
        float clamped = Math.Max(0f, Math.Min(1.5f, value));
        if (Math.Abs(AccessRaySlopeConservatism - clamped) > 0.0001f)
            AutoDepthDesignation.MarkAllMiningPlansDirty();
        AccessRaySlopeConservatism = clamped;
    }
    public static int AccessRayEndBuffer { get; private set; } = 3;
    public static void SetAccessRayEndBuffer(int value)
    {
        int clamped = Math.Max(0, Math.Min(16, value));
        if (AccessRayEndBuffer != clamped)
            AutoDepthDesignation.MarkAllMiningPlansDirty();
        AccessRayEndBuffer = clamped;
    }
    internal static SafetyPolicy GetSafetyPolicy()
    {
        SafetyPolicy closest = SafetyPolicy.Med;
        float closestDistance = float.MaxValue;
        for (int value = (int)SafetyPolicy.Min; value <= (int)SafetyPolicy.Max; value++)
        {
            SafetyPolicy candidate = (SafetyPolicy)value;
            GetSafetyPolicyParameters(candidate, out float slope, out int buffer);
            float slopeDelta = (AccessRaySlopeConservatism - slope) / 0.1f;
            float bufferDelta = AccessRayEndBuffer - buffer;
            float distance = slopeDelta * slopeDelta + bufferDelta * bufferDelta;
            if (distance < closestDistance)
            {
                closest = candidate;
                closestDistance = distance;
            }
        }
        return closest;
    }
    internal static void SetSafetyPolicy(SafetyPolicy policy)
    {
        if (policy < SafetyPolicy.Min || policy > SafetyPolicy.Max)
            policy = SafetyPolicy.Med;
        GetSafetyPolicyParameters(policy, out float slope, out int buffer);
        SetAccessRaySlopeConservatism(slope);
        SetAccessRayEndBuffer(buffer);
    }
    internal static void GetSafetyPolicyParameters(
        SafetyPolicy policy, out float slope, out int buffer)
    {
        switch (policy)
        {
            case SafetyPolicy.Max: slope = 1.5f; buffer = 5; break;
            case SafetyPolicy.High: slope = 1.25f; buffer = 4; break;
            case SafetyPolicy.Low: slope = 0.9f; buffer = 2; break;
            case SafetyPolicy.Min: slope = 0.8f; buffer = 1; break;
            default: slope = 1f; buffer = 3; break;
        }
    }
    public static int AccessCandidateRayMaxDistance { get; private set; } = 16;
    public static void SetAccessCandidateRayMaxDistance(int value)
    {
        int clamped = Math.Max(4, Math.Min(128, value));
        if (AccessCandidateRayMaxDistance != clamped)
            AutoDepthDesignation.MarkAllMiningPlansDirty();
        AccessCandidateRayMaxDistance = clamped;
    }
    public static float AccessRayMaxCost { get; private set; } = 500f;
    public static void SetAccessRayMaxCost(float value) => AccessRayMaxCost = Math.Max(1f, Math.Min(10000f, value));
    public static float AccessRayUnresolvedPenalty { get; private set; } = 200f;
    public static void SetAccessRayUnresolvedPenalty(float value) => AccessRayUnresolvedPenalty = Math.Max(0f, Math.Min(10000f, value));
    public static int AccessMaxVisitedNodes { get; private set; } = 250000;
    public static void SetAccessMaxVisitedNodes(int value) => AccessMaxVisitedNodes = Math.Max(1000, Math.Min(2000000, value));
    public static int AccessSearchTimeoutSeconds { get; private set; } = 60;
    public static void SetAccessSearchTimeoutSeconds(int value) => AccessSearchTimeoutSeconds = Math.Max(5, Math.Min(600, value));
    public static int AccessSearchFrameBudgetMs { get; private set; } = 30;
    public static void SetAccessSearchFrameBudgetMs(int value) => AccessSearchFrameBudgetMs = Math.Max(1, Math.Min(100, value));

    /// <summary>Keybinding used to enter and toggle corner designation mode. Default: K.</summary>
    [Kb(KbCategory.Designation, "Atd_CornerDesignationMode", "Corner designations mode", "Enters and toggles corner designation mode", false, false, null)]
    public static KeyBindings CornerDesignationMode { get; set; } = FromPrimaryKeys(KeyCode.K);

    public static void SetCornerDesignationMode(KeyBindings value)
    {
        CornerDesignationMode = value;
    }

    public static bool IsPressed(KeyBindings bindings)
    {
        return IsPressed(bindings.Primary) || IsPressed(bindings.Secondary);
    }

    private static bool IsPressed(KeyBinding binding)
    {
        if (binding.IsEmpty)
            return false;

        ImmutableArray<KeyCode> keys = binding.Keys;
        if (keys.Length == 0)
            return false;

        KeyCode trigger = keys[keys.Length - 1];
        if (!Input.GetKeyDown(trigger))
            return false;

        for (int i = 0; i < keys.Length - 1; i++)
        {
            if (!Input.GetKey(keys[i]))
                return false;
        }

        return true;
    }

    internal static KeyBindings FromPrimaryKeys(params KeyCode[] keys)
    {
        return new KeyBindings(
            ShortcutMode.Game,
            new KeyBinding(KbCategory.Designation, keys.ToImmutableArray()),
            KeyBinding.Empty(KbCategory.Designation));
    }

    public void Initialize(DependencyResolver resolver, bool gameWasLoaded)
    {
        try
        {
            AutoDepthDesignation.s_log.EnableConsoleLogging();
            AutoDepthDesignation.s_log.RegisterAutoConsoleMirroring(this, resolver.Resolve<IGameLoopEvents>(), resolver.Resolve<GameConsoleCommandsExecutor>());
            AutoTerrainDesignationsTicker.DestroyActive();

            RegisterAutoHelpersLocalizationLateApply(resolver);

            m_gameLoopEvents = resolver.Resolve<IGameLoopEvents>();
            m_simLoopEvents = resolver.Resolve<ISimLoopEvents>();
            m_saveManager = resolver.Resolve<ISaveManager>();
            m_gameLoopEvents.Terminate.AddNonSaveable(this, onGameTerminated);
            m_simLoopEvents.BeforeSave.AddNonSaveable(this, beforeSave);
            m_simLoopEvents.Update.AddNonSaveable(this, onSimUpdate);
            m_saveManager.OnSaveDone += onSaveDone;

            ITerrainDesignationsManager desigManager = resolver.Resolve<ITerrainDesignationsManager>();
            ProtosDb protosDb = resolver.Resolve<ProtosDb>();
            IWorldMapManager worldMapManager = resolver.Resolve<IWorldMapManager>();
            IEntitiesManager entitiesManager = resolver.Resolve<IEntitiesManager>();
            Mafi.Core.Vehicles.IVehiclesManager vehiclesManager = resolver.Resolve<Mafi.Core.Vehicles.IVehiclesManager>();
            TerrainPropsManager terrainPropsManager = resolver.Resolve<TerrainPropsManager>();
            PropsRemovalProcessor propsRemovalProcessor = resolver.Resolve<PropsRemovalProcessor>();
            TreesManager treesManager = resolver.Resolve<TreesManager>();
            IVehiclePathFindingManager vehiclePathFindingManager = resolver.Resolve<IVehiclePathFindingManager>();
            ParkAndWaitJobFactory parkAndWaitJobFactory = resolver.Resolve<ParkAndWaitJobFactory>();
            INotificationsManager notificationsManager = resolver.Resolve<INotificationsManager>();
            IInputScheduler inputScheduler = resolver.Resolve<IInputScheduler>();
            ConfigSerializationContext configSerializationContext = resolver.Resolve<ConfigSerializationContext>();
            AutoTerrainDesignationsTicker ticker = AutoTerrainDesignationsTicker.CreateForWorld(AutoDepthDesignation.CurrentWorldGeneration + 1);
            AutoDepthDesignation.SetModRootDirectoryPath(Manifest.RootDirectoryPath);
            m_entitiesManager = entitiesManager;
            m_entitiesManager.EntityRemoved.AddNonSaveable(this, onEntityRemoved);
            AutoDepthDesignation.Initialize(desigManager, protosDb, worldMapManager, ticker, entitiesManager, terrainPropsManager, propsRemovalProcessor, treesManager, vehiclePathFindingManager, parkAndWaitJobFactory, notificationsManager, inputScheduler, configSerializationContext, vehiclesManager);
            m_towerSettingsStateStore = ModStateJsonStores.CreateDefault(JsonConfig, AutoDepthDesignation.TowerSettingsConfigKey);
            AutoDepthDesignation.LoadTowerSettingsFromJsonStore(m_towerSettingsStateStore);
            AutoDepthDesignation.PropRemovalManager?.ResumeLoadedRequests();
            m_preAllocationsStateStore = ModStateJsonStores.CreateDefault(JsonConfig, "atdPendingVehicleAllocations");
            PendingVehicleAllocations.LoadFromJsonStore(m_preAllocationsStateStore);
            PendingVehicleAllocations.ReconcileQueues(entitiesManager);
            // Corner designation mode — TerrainCursor, TerrainDesignationsRenderer and
            // CursorManager may only be available on the Unity side; fail gracefully if not resolvable.
            TerrainCursor? terrainCursor = null;
            TerrainDesignationsRenderer? desigRenderer = null;
            CursorManager? cursorManager = null;
            ShortcutsManager? shortcutsManager = null;
            try { terrainCursor = resolver.Resolve<TerrainCursor>(); }
            catch (Exception ex2) { AutoDepthDesignation.s_log.Warning("TerrainCursor not available: " + ex2.Message); }
            try { desigRenderer = resolver.Resolve<TerrainDesignationsRenderer>(); }
            catch (Exception ex3) { AutoDepthDesignation.s_log.Warning("TerrainDesignationsRenderer not available: " + ex3.Message); }
            try { cursorManager = resolver.Resolve<CursorManager>(); }
            catch (Exception ex4) { AutoDepthDesignation.s_log.Warning("CursorManager not available: " + ex4.Message); }
            try { shortcutsManager = resolver.Resolve<ShortcutsManager>(); }
            catch (Exception ex5) { AutoDepthDesignation.s_log.Warning("ShortcutsManager not available: " + ex5.Message); }
            AutoDepthDesignation.InitializeCornerMode(terrainCursor, desigRenderer, cursorManager, shortcutsManager);
        }
        catch (Exception ex)
        {
            unsubscribeWorldEvents();
            AutoTerrainDesignationsTicker.DestroyActive();
            AutoDepthDesignation.ResetWorldRuntimeState();
            AutoDepthDesignation.s_log.Exception(ex, "AutoTerrainDesignations init");
        }
    }

    // Runs on the simulation thread — safe to call game simulation APIs.
    private void onSimUpdate()
    {
        if (m_simLoopEvents == null)
            return;
        // Run once per game-second (10 sim steps = 1 game-second).
        SimStep current = m_simLoopEvents.CurrentStep;
        if (current - m_lastSimTick < Duration.OneSecond)
            return;
        m_lastSimTick = current;
        try { AutoDepthDesignation.TickFarmingPreparationSessions(); }
        catch (Exception ex) { AutoDepthDesignation.s_log.Exception(ex, "TickFarmingPreparationSessions"); }
        try { AutoDepthDesignation.TickIdleVehicleRelease(); }
        catch (Exception ex) { AutoDepthDesignation.s_log.Exception(ex, "TickIdleVehicleRelease"); }
        try { AutoDepthDesignation.PropRemovalManager?.Tick(); }
        catch (Exception ex) { AutoDepthDesignation.s_log.Exception(ex, "ATDPropRemovalManager.Tick"); }
    }

    private void beforeSave()
    {
        AutoDepthDesignation.PropRemovalManager?.PrepareForSave();
        IModStateJsonStore store = m_towerSettingsStateStore
            ?? ModStateJsonStores.CreateDefault(JsonConfig, AutoDepthDesignation.TowerSettingsConfigKey);
        m_towerSettingsStateStore = store;
        AutoDepthDesignation.SaveTowerSettingsToJsonStore(store);
        if (m_preAllocationsStateStore != null)
        {
            if (m_entitiesManager != null)
                PendingVehicleAllocations.ReconcileQueues(m_entitiesManager);
            PendingVehicleAllocations.SaveToJsonStore(m_preAllocationsStateStore);
        }
        AutoDepthDesignation.PurgeTransientNotificationsForSave();
        AutoDepthDesignation.RestoreFarmingRuntimeForSave();
        AutoDepthDesignation.RestoreIdleReleasedVehiclesForSave();
    }

    private void onSaveDone(SaveResult result)
    {
        AutoDepthDesignation.PropRemovalManager?.ResumeAfterSave();
        AutoDepthDesignation.ResumeFarmingRuntimeAfterSave();
        AutoDepthDesignation.RestoreTransientNotificationsAfterSave();
        AutoDepthDesignation.ReReleaseIdleVehiclesAfterSave();
    }

    private void onGameTerminated()
    {
        unsubscribeWorldEvents();
        AutoTerrainDesignationsTicker.DestroyActive();
        AutoDepthDesignation.ResetWorldRuntimeState();
        PendingVehicleAllocations.ClearAll();
    }

    private void unsubscribeWorldEvents()
    {
        if (m_gameLoopEvents != null)
        {
            try { m_gameLoopEvents.Terminate.RemoveNonSaveable(this, onGameTerminated); }
            catch { }
            m_gameLoopEvents = null;
        }

        if (m_simLoopEvents != null)
        {
            try { m_simLoopEvents.BeforeSave.RemoveNonSaveable(this, beforeSave); }
            catch { }
            try { m_simLoopEvents.Update.RemoveNonSaveable(this, onSimUpdate); }
            catch { }
            m_simLoopEvents = null;
        }

        if (m_saveManager != null)
        {
            try { m_saveManager.OnSaveDone -= onSaveDone; }
            catch { }
            m_saveManager = null;
        }
        if (m_entitiesManager != null)
        {
            try { m_entitiesManager.EntityRemoved.RemoveNonSaveable(this, onEntityRemoved); }
            catch { }
            m_entitiesManager = null;
        }
    }

    private void onEntityRemoved(IEntity entity)
    {
        AutoDepthDesignation.OnFarmingTowerRemoved(entity.Id);

        if (entity is IEntityAssignedWithVehicles)
            PendingVehicleAllocations.OnTowerDestroyed(entity.Id);
        else if (entity is VehicleDepotBase)
            PendingVehicleAllocations.OnDepotDestroyed(entity.Id);
    }

    private void RegisterAutoHelpersLocalizationLateApply(DependencyResolver resolver)
    {
        IGameLoopEvents gameLoopEvents = resolver.Resolve<IGameLoopEvents>();
        gameLoopEvents.RegisterRendererInitState(this, () =>
        {
            AutoDepthDesignation.s_log.Info($"AutoTerrainDesignations v{ModVersion} | dll: {ModLogger.GetDllBuildTimestamp(typeof(AutoTerrainDesignationsMod).Assembly)}");
            AtdDiagnostics.Info(AutoDepthDesignation.s_log, $"Diagnostics: {AtdDiagnostics.Describe()}.");
            AtdDiagnostics.Debug(AutoDepthDesignation.s_log, "Localization: late apply at renderer init state.");
            ApplyAutoHelpersLocalization();
            RegisterSettingsTabs(resolver);
        });
    }

    private static void RegisterSettingsTabs(DependencyResolver resolver)
    {
        try
        {
            ModSettings.EnsureInitialized(
                resolver.Resolve<HudController>(),
                resolver.Resolve<UiRoot>(),
                resolver.Resolve<IRootEscapeManager>());
            AutoDepthDesignation.SetUiRoot(resolver.Resolve<UiRoot>());

            ModSettings.RegisterTab(AtdModSettingsTab.BuildDefaultsTab());
            ModSettings.RegisterTab(AtdModSettingsTab.BuildWorldSettingsTab());
            ModSettings.RegisterTab(AtdModSettingsTab.BuildOreQualityTab());
            ModSettings.RegisterTab(AtdModSettingsTab.BuildPathfinderTab());
        }
        catch (Exception ex)
        {
            AutoDepthDesignation.s_log.Exception(ex, "ATD settings tab registration");
        }
    }

    private void ApplyAutoHelpersLocalization()
    {
        string translationsDirectory = Path.Combine(Manifest.RootDirectoryPath, "translations");
        AtdDiagnostics.Debug(AutoDepthDesignation.s_log, $"Localization: probing directory '{translationsDirectory}'.");

        if (!Directory.Exists(translationsDirectory))
        {
            AutoDepthDesignation.s_log.Warning("Localization: translations directory does not exist; skipping.");
            return;
        }

        string[] jsonFiles = Directory.GetFiles(translationsDirectory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(jsonFiles, StringComparer.OrdinalIgnoreCase);
        if (jsonFiles.Length == 0)
            AutoDepthDesignation.s_log.Warning("Localization: no translation JSON files found.");
        else
            AtdDiagnostics.Debug(AutoDepthDesignation.s_log, $"Localization: discovered {jsonFiles.Length} file(s): {string.Join(", ", jsonFiles)}");

        string currentCulture;
        try { currentCulture = LocalizationManager.CurrentLangInfo.CultureInfoId; }
        catch { currentCulture = "<unavailable>"; }
        AtdDiagnostics.Debug(AutoDepthDesignation.s_log, $"Localization: current game culture before apply = '{currentCulture}'.");

        ModTranslationsApplyResult result = new ModTranslations().Apply(new ModTranslationsApplyOptions(
            translationsDirectory,
            typeof(AutoTerrainDesignationsMod).Assembly,
            Array.Empty<string>()));

        AtdDiagnostics.Info(AutoDepthDesignation.s_log,
            $"Localization: applied locale='{result.AppliedLocaleCode}', upserted={result.UpsertedEntryCount}, scannedFields={result.ScannedFieldCount}, reboundFields={result.ReboundFieldCount}, readonlySkipped={result.SkippedReadonlyFieldCount}, missingTranslationSkipped={result.SkippedMissingTranslationFieldCount}, failedWrites={result.FailedFieldCount}, diagnostics={result.Diagnostics.Count}.");

        foreach (TranslationDiagnostic diagnostic in result.Diagnostics)
        {
            string itemInfo = diagnostic.ItemIndex.HasValue ? $", itemIndex={diagnostic.ItemIndex.Value}" : string.Empty;
            string message = $"Localization diagnostic [{diagnostic.Severity}] source='{diagnostic.SourcePath}'{itemInfo}: {diagnostic.Message}";
            if (diagnostic.Severity == TranslationDiagnosticSeverity.Info)
                AtdDiagnostics.Debug(AutoDepthDesignation.s_log, message);
            else
                AutoDepthDesignation.s_log.Warning(message);
        }
    }

    public void MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues)
    {
    }

    public void Dispose()
    {
        unsubscribeWorldEvents();
        AutoTerrainDesignationsTicker.DestroyActive();
        AutoDepthDesignation.ResetWorldRuntimeState();
        m_harmony?.UnpatchAll("com.auto-terrain-designations.mod");
    }
}

// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using Mafi.Localization;

namespace AutoTerrainDesignations
{
    /// <summary>
    /// Static localization fields for ATD. All LocStr fields are rebound by
    /// <see cref="CoI.AutoHelpers.Localization.ModTranslations.Apply"/> at renderer init state.
    /// </summary>
    internal static class AtdLocalization
    {
        /// <summary>
        /// Returns a <see cref="LocStrFormatted"/> for general tooltip text without the mod marker.
        /// </summary>
        public static LocStrFormatted Tip(LocStr s) =>
            new LocStrFormatted(AutoTerrainDesignationsMod.Tt(s.TranslatedString));

        /// <summary>
        /// Returns a <see cref="LocStrFormatted"/> for inspector-panel tooltip text with the mod marker.
        /// </summary>
        public static LocStrFormatted PanelTip(LocStr s) =>
            new LocStrFormatted($"{AutoTerrainDesignationsMod.ModMarker}\n\n{AutoTerrainDesignationsMod.Tt(s.TranslatedString)}");

        // ------------------------------------------------------------------ //
        // Common levels
        // ------------------------------------------------------------------ //
        public static LocStr LevelOff  = Loc.Str("common.level.off",  "Off",  "Common level setting label: off/disabled.");
        public static LocStr LevelLow  = Loc.Str("common.level.low",  "Low",  "Common level setting label: low.");
        public static LocStr LevelMed  = Loc.Str("common.level.med",  "Med",  "Common level setting label: medium.");
        public static LocStr LevelHigh = Loc.Str("common.level.high", "High", "Common level setting label: high.");
        public static LocStr LevelMax  = Loc.Str("common.level.max",  "Max",  "Common level setting label: maximum.");

        // ------------------------------------------------------------------ //
        // Terrain designation panel
        // ------------------------------------------------------------------ //
        public static LocStr DesigTitle =
            Loc.Str("panel.designations.title", "Mining designations", "Title of the mining designations inspector panel.");
        public static LocStr DesigDescription =
            Loc.Str("panel.designations.description", "Create automatic terrain designations for this tower.", "Tooltip on the terrain designations panel title.");
        public static LocStr DesigCreateBtn =
            Loc.Str("panel.designations.create_button", "Create Designations", "Label on the Create Designations button.");
        public static LocStr DesigCreateTip =
            Loc.Str("panel.designations.create_tooltip", "Scan and place mining designations in this tower's area.", "Tooltip on the Create Designations button.");
        public static LocStr DesigDebrisTip =
            Loc.Str("panel.designations.debris_tooltip", "Request excavator removal of reachable debris through ATD's prop-removal manager. Existing designations are temporarily suspended and restored after removal. This button never spends Unity on Quick remove and follows Landscape to remove debris. Ctrl-click includes unreachable debris.", "Tooltip on the Debris button.");
        public static LocStr DesigClearTip =
            Loc.Str("panel.designations.clear_tooltip", "Clear this tower's ATD-generated terrain and tree-harvest designations. Shift-click to clear all terrain designations in the tower's area plus only this tower's generated tree-harvest designations.", "Tooltip on the Clear button.");
        public static LocStr DesigClearTipWithShiftClick =
            Loc.Str("panel.designations.clear_tooltip.shift_click", "Clear this tower's ATD-generated terrain and tree-harvest designations. Shift-click to clear all terrain designations in the tower's area plus only this tower's generated tree-harvest designations.", "Tooltip on the Clear button when generated designations exist.");
        public static LocStr DesigOreFilterAuto =
            Loc.Str("panel.designations.ore_filter.auto", "AUTO", "Label for automatic scanning behavior in the ore picker.");
        public static LocStr DesigAccesswayModeLabel =
            Loc.Str("panel.designations.accessway_mode.label", "Accessway mode", "Label for the accessway mode selector.");
        public static LocStr AccesswayModeLegacy3 =
            Loc.Str("panel.designations.accessway_mode.legacy_3", "Legacy 3", "Accessway mode option for a legacy straight ramp three designation cells wide.");
        public static LocStr AccesswayModeLegacy4 =
            Loc.Str("panel.designations.accessway_mode.legacy_4", "Legacy 4", "Accessway mode option for a legacy straight ramp four designation cells wide.");
        public static LocStr AccesswayModeLegacy5 =
            Loc.Str("panel.designations.accessway_mode.legacy_5", "Legacy 5", "Accessway mode option for a legacy straight ramp five designation cells wide.");
        public static LocStr AccesswayModeOffTip =
            Loc.Str("panel.designations.accessway_mode.off.tooltip", "Disable generated accessways and ramps.", "Tooltip for the OFF accessway mode.");
        public static LocStr AccesswayModeAutoTip =
            Loc.Str("panel.designations.accessway_mode.auto.tooltip", "Use the largest excavator assigned or pre-assigned to this tower, then the largest excavator present on the map. With no excavators, AUTO behaves as OFF.", "Tooltip for the AUTO accessway mode.");
        public static LocStr AccesswayModeT1Tip =
            Loc.Str("panel.designations.accessway_mode.t1.tooltip", "Generate routed accessways using T1 excavator pathability.", "Tooltip for the T1 accessway mode.");
        public static LocStr AccesswayModeT2Tip =
            Loc.Str("panel.designations.accessway_mode.t2.tooltip", "Generate routed accessways using T2 excavator pathability.", "Tooltip for the T2 accessway mode.");
        public static LocStr AccesswayModeT3Tip =
            Loc.Str("panel.designations.accessway_mode.t3.tooltip", "Generate two-lane routed accessways using T3/Mega excavator pathability.", "Tooltip for the T3 accessway mode.");
        public static LocStr AccesswayModeLegacy3Tip =
            Loc.Str("panel.designations.accessway_mode.legacy_3.tooltip", "Generate only a legacy straight ramp three designation cells wide, validated for T3/Mega excavators.", "Tooltip for legacy ramp width 3.");
        public static LocStr AccesswayModeLegacy4Tip =
            Loc.Str("panel.designations.accessway_mode.legacy_4.tooltip", "Generate only a legacy straight ramp four designation cells wide, validated for T3/Mega excavators.", "Tooltip for legacy ramp width 4.");
        public static LocStr AccesswayModeLegacy5Tip =
            Loc.Str("panel.designations.accessway_mode.legacy_5.tooltip", "Generate only a legacy straight ramp five designation cells wide, validated for T3/Mega excavators.", "Tooltip for legacy ramp width 5.");
        public static LocStr DesigMaxLayersLabel =
            Loc.Str("panel.designations.max_layers.label", "Max layers to excavate", "Label for the max layers setting row.");
        public static LocStr DesigMaxLayersTip =
            Loc.Str("panel.designations.max_layers.tooltip", "Maximum layers to excavate from the surface. (\u221e = no limit.)", "Tooltip for the max layers setting.");
        public static LocStr DesigElevLimitLabel =
            Loc.Str("panel.designations.elevation_limit.label", "Elevation limit", "Label for the elevation limit setting row.");
        public static LocStr DesigElevLimitTip =
            Loc.Str("panel.designations.elevation_limit.tooltip", "Maximum (absolute) excavation depth (-\u221e = no limit.)", "Tooltip for the elevation limit setting.");
        public static LocStr DesigOrePurityLabel =
            Loc.Str("panel.designations.ore_purity.label", "Ore quality", "Label for the ore quality setting row.");
        public static LocStr DesigOrePurityTip =
            Loc.Str("panel.designations.ore_purity.tooltip",
                "How strictly the scan filters terrain columns for ore quality. This does not predict the material mix excavators will produce.\n" +
                "Off: include all tiles, dig to full depth.\n" +
                "Low: exclude very sparse tiles, trim thin trailing ore at the bottom.\n" +
                "Med: moderate quality \u2014 skip tiles with heavy overburden or little ore.\n" +
                "High: only rich tiles with a clean ore column.\n" +
                "Max: apply the strictest overburden, depth, and ore-density filters.",
                "Tooltip for the ore purity setting.");
        public static LocStr OreQualityValueTooltip =
            Loc.Str("settings.ore_quality.value_tooltip", "{0}: minimum ore height {1:0.##} terrain tiles; minimum bottom density {2:P0}; minimum ore purity {3:P0}; minimum component size {4} connected tiles.", "Tooltip for an individual Ore quality slider value. {0} = quality name, {1} = minimum ore height, {2} = minimum bottom density, {3} = minimum ore purity, {4} = minimum component size.");
        public static LocStr OreQualityOff =
            Loc.Str("settings.ore_quality.value.off", "Off", "Full name for the Off ore quality value.");
        public static LocStr OreQualityLow =
            Loc.Str("settings.ore_quality.value.low", "Low", "Full name for the Low ore quality value.");
        public static LocStr OreQualityMedium =
            Loc.Str("settings.ore_quality.value.medium", "Medium", "Full name for the Medium ore quality value.");
        public static LocStr OreQualityHigh =
            Loc.Str("settings.ore_quality.value.high", "High", "Full name for the High ore quality value.");
        public static LocStr OreQualityMaximum =
            Loc.Str("settings.ore_quality.value.maximum", "Maximum", "Full name for the Maximum ore quality value.");
        public static LocStr DesigCorridorClearanceLabel =
            Loc.Str("panel.designations.corridor_clearance.label", "Corridor clearance", "Label for the corridor clearance setting row.");
        public static LocStr DesigCorridorClearanceTip =
            Loc.Str("panel.designations.corridor_clearance.tooltip",
                "Minimum corridor width for connecting ore regions and enforcing passability.\n" +
                "0 = disabled (regions left separate, no corridors or hole-filling).\n" +
                "1 = 1-tile corridors (small and medium vehicles).\n" +
                "2 = 2-tile corridors (mega vehicles).\n",
                "Tooltip for the corridor clearance setting.");
        public static LocStr DesigScanningFilterLabel =
            Loc.Str("panel.designations.scanning_filter.label", "Scanning filter:", "Label for the scanning filter ore picker row.");
        public static LocStr DesigScanningFilterTip =
            Loc.Str("panel.designations.scanning_filter.tooltip", "Choose what Create Designations scans for.\n\nAUTO:\n• If terrain designations already exist, preserve them and use unfinished terrain-work clusters as pathfinding starts. When an eligible unstarted mine control tower ghost exists, its entrance replaces the active tower as the pathfinding target.\n• Otherwise, use an eligible tower ghost as a natural accessway marker and commit the first valid route. A recognized ghost suppresses the product scan even when no route can be placed.\n• If neither terrain work nor an eligible ghost exists, scan for useful products and generate their mining plan.\n\nAUTO never falls back to debris or dirt. Select Dirt explicitly, or use the debris-clearance button, when that work is wanted.", "Tooltip for the scanning filter ore picker.");

        // ------------------------------------------------------------------ //
        // Mod settings window
        // ------------------------------------------------------------------ //
        public static LocStr SettingsModName =
            Loc.Str("settings.mod.name", "Auto Terrain Designations", "Mod name in the shared Mod Settings window.");
        public static LocStr SettingsTabDefaults =
            Loc.Str("settings.tab.defaults", "Defaults", "Settings tab title for ATD defaults.");
        public static LocStr SettingsTabWorldSettings =
            Loc.Str("settings.tab.world_settings", "World settings", "Settings tab title for ATD world settings.");
        public static LocStr SettingsTabOreQuality =
            Loc.Str("settings.tab.ore_quality", "Ore quality", "Settings tab title for ATD ore quality settings.");
        public static LocStr SettingsHeadingMiningDefaults =
            Loc.Str("settings.heading.mining_defaults", "Mine control tower defaults", "Settings section heading for mine control tower defaults.");
        public static LocStr SettingsHeadingPanelDefaults =
            Loc.Str("settings.heading.panel_defaults", "Panel defaults", "Settings section heading for panel defaults.");
        public static LocStr SettingsHeadingDesignations =
            Loc.Str("settings.heading.designations", "Designations", "Settings section heading for designation behavior.");
        public static LocStr SettingsHeadingScanPerformance =
            Loc.Str("settings.heading.scan_performance", "Scan performance", "Settings section heading for scan performance.");
        public static LocStr SettingsHeadingKeyboardShortcuts =
            Loc.Str("settings.heading.keyboard_shortcuts", "Keyboard shortcuts", "Settings section heading for keyboard shortcuts.");
        public static LocStr SettingsHeadingNotifications =
            Loc.Str("settings.heading.notifications", "Notifications", "Settings section heading for notification settings.");
        public static LocStr SettingsHeadingExperimentalAccess =
            Loc.Str("settings.heading.experimental_access", "Experimental accessways", "Settings section heading for experimental accessway settings.");
        public static LocStr SettingsHeadingWorldSafety =
            Loc.Str("settings.world.safety", "Terrain safety", "Settings section heading for world terrain-safety settings.");
        public static LocStr SettingsAvoidOceanLabel =
            Loc.Str("settings.world.avoid_ocean.label", "Avoid ocean", "World safety setting label for ocean avoidance.");
        public static LocStr SettingsAvoidOceanTooltip =
            Loc.Str("settings.world.avoid_ocean.tooltip", "Avoid ocean in generated accessways and mining plans in this world. Mining cells that overlap ocean are excluded. Projected cutting below sea level is also avoided; dumping into ocean remains allowed. Turn this off to allow risky shoreline work.", "World safety setting tooltip for ocean avoidance.");
        public static LocStr SettingsAvoidBuildingsLabel =
            Loc.Str("settings.world.avoid_buildings.label", "Avoid buildings", "World safety setting label for building avoidance.");
        public static LocStr SettingsAvoidBuildingsTooltip =
            Loc.Str("settings.world.avoid_buildings.tooltip", "Avoid buildings in generated accessways and mining plans in this world. Mining cells that overlap a building or its safety perimeter are excluded. Projected terrain disturbance near buildings is also avoided.", "World safety setting tooltip for building avoidance.");
        public static LocStr SettingsHarvestDisruptedTreesLabel =
            Loc.Str("settings.world.harvest_disrupted_trees.label", "Harvest disrupted trees", "World safety setting label for disrupted-tree harvesting.");
        public static LocStr SettingsHarvestDisruptedTreesTooltip =
            Loc.Str("settings.world.harvest_disrupted_trees.tooltip", "Mark trees in finalized accessway and mining-designation disturbance zones for harvest. When disabled, ATD creates no tree harvest orders. Harvest orders placed by ATD are removed by either Clear action; unrelated player harvest orders are preserved.", "World safety setting tooltip for disrupted-tree harvesting.");
        public static LocStr SettingsAllowDigToRemoveDebrisLabel =
            Loc.Str("settings.world.allow_dig_to_remove_debris.label", "Landscape to remove debris", "World safety setting label for terrain-altering debris removal.");
        public static LocStr SettingsAllowDigToRemoveDebrisTooltip =
            Loc.Str("settings.world.allow_dig_to_remove_debris.tooltip", "Allow debris removal to make the smallest workable terrain change when a prop cannot be excavated without landscaping. When disabled, the removal request fails and the requesting workflow is notified.", "World safety setting tooltip for terrain-altering debris removal.");
        public static LocStr SettingsQuickRemoveDebrisLabel =
            Loc.Str("settings.world.quick_remove_debris.label", "Quick remove debris", "Accessway setting label for Quick Remove policy.");
        public static LocStr SettingsQuickRemoveDebrisTooltip =
            Loc.Str("settings.world.quick_remove_debris.tooltip", "Controls when ATD uses the game's Quick remove action for routed accessway debris. Quick remove spends Unity only when the game is unpaused. This policy does not apply to the mine-tower Clear debris button.", "Accessway setting tooltip explaining that Quick remove costs Unity only while unpaused.");
        public static LocStr SettingsQuickRemoveDebrisAlways =
            Loc.Str("settings.world.quick_remove_debris.always", "Always", "Quick Remove policy value.");
        public static LocStr SettingsQuickRemoveDebrisAlwaysTooltip =
            Loc.Str("settings.world.quick_remove_debris.always.tooltip", "Use Quick remove for all accessway debris except props that the planned dumping designation will sufficiently bury. This speeds up landscaping but spends Unity.", "Tooltip for Always Quick remove policy.");
        public static LocStr SettingsQuickRemoveDebrisRestrictive =
            Loc.Str("settings.world.quick_remove_debris.restrictive", "Restrictive", "Quick Remove policy value.");
        public static LocStr SettingsQuickRemoveDebrisRestrictiveTooltip =
            Loc.Str("settings.world.quick_remove_debris.restrictive.tooltip", "Prefer normal excavation but use Quick remove when excavation cannot guarantee post-work pathability.", "Tooltip for Restrictive Quick Remove policy.");
        public static LocStr SettingsQuickRemoveDebrisNever =
            Loc.Str("settings.world.quick_remove_debris.never", "Never", "Quick Remove policy value.");
        public static LocStr SettingsQuickRemoveDebrisNeverTooltip =
            Loc.Str("settings.world.quick_remove_debris.never.tooltip", "Never spend Unity on Quick remove for accessway debris. Removal relies on excavation and the Landscape to remove debris setting, and may need the player to provide Quick remove assistance.", "Tooltip for Never Quick Remove policy.");
        public static LocStr SettingsSafetyPolicyLabel =
            Loc.Str("settings.world.safety_policy.label", "Safety policy", "World safety policy setting label.");
        public static LocStr SettingsAllowRampsOutsideTowerAreasLabel =
            Loc.Str("settings.world.allow_ramps_outside_tower_areas.label", "Allow ramps outside tower areas", "World setting label for bounded out-of-area accessway fallback.");
        public static LocStr SettingsAllowRampsOutsideTowerAreasTooltip =
            Loc.Str("settings.world.allow_ramps_outside_tower_areas.tooltip", "If an experimental ramp search exhausts the available routes inside a tower area, retry within 16 tiles beyond its boundary. This fallback applies to both narrow and T3/Mega accessways, does not run after a timeout or other search interruption, and may trigger the game's normal outside-area alarm. Default: enabled.", "Tooltip for bounded out-of-area accessway fallback.");
        public static LocStr SettingsSafetyPolicyTooltip =
            Loc.Str("settings.world.safety_policy.tooltip", "Controls how cautiously ATD predicts landslides and how much distance it keeps from protected oceans and buildings. Higher policies reserve more space; lower policies allow terrain work closer to hazards. Default: BAL.", "World safety policy setting tooltip.");
        public static LocStr SettingsSafetyPolicyValueTooltip =
            Loc.Str("settings.world.safety_policy.value_tooltip", "{0}: landslide predictor slope factor {1}; protected-area buffer {2} tiles.", "Tooltip for an individual World safety policy slider value. {0} = policy, {1} = landslide predictor slope factor, {2} = protected-area buffer in tiles.");
        public static LocStr SettingsSafetyPolicyMinimum =
            Loc.Str("settings.world.safety_policy.minimum", "Minimum", "Full name for the Minimum safety policy value.");
        public static LocStr SettingsSafetyPolicyLow =
            Loc.Str("settings.world.safety_policy.low", "Low", "Full name for the Low safety policy value.");
        public static LocStr SettingsSafetyPolicyBalanced =
            Loc.Str("settings.world.safety_policy.balanced", "Balanced", "Full name for the Balanced safety policy value.");
        public static LocStr SettingsSafetyPolicyHigh =
            Loc.Str("settings.world.safety_policy.high", "High", "Full name for the High safety policy value.");
        public static LocStr SettingsSafetyPolicyMaximum =
            Loc.Str("settings.world.safety_policy.maximum", "Maximum", "Full name for the Maximum safety policy value.");
        public static LocStr SettingsTabPathfinder =
            Loc.Str("settings.tab.pathfinder", "Accessways", "Settings tab title for ATD accessway settings.");
        public static LocStr SettingsPathfinderCostsHeading =
            Loc.Str("settings.pathfinder.costs", "Route costs", "Pathfinder cost settings heading.");
        public static LocStr SettingsTerrainDesignationCostLabel =
            Loc.Str("settings.pathfinder.generated_v_fixed.label", "Terrain designation cost", "Pathfinder cost setting label for generated terrain designations.");
        public static LocStr SettingsTerrainDesignationCostTooltip =
            Loc.Str("settings.pathfinder.generated_v_fixed.tooltip", "Fixed cost charged for every generated terrain designation. Higher values favor fewer terrain designations, even when that increases travel distance and total landscaping cost.", "Pathfinder cost setting tooltip for generated terrain designations.");
        public static LocStr SettingsDirectTerrainWorkWeightLabel =
            Loc.Str("settings.pathfinder.direct_work_weight.label", "Direct terrain-work weight", "Pathfinder cost setting label for direct terrain work.");
        public static LocStr SettingsDirectTerrainWorkWeightTooltip =
            Loc.Str("settings.pathfinder.direct_work_weight.tooltip", "Weight applied to digging or dumping terrain directly above or below generated terrain designations.", "Pathfinder cost setting tooltip for direct terrain work.");
        public static LocStr SettingsSideRayWorkWeightLabel =
            Loc.Str("settings.pathfinder.side_ray_weight.label", "Side-ray work weight", "Pathfinder cost setting label for lateral and turn-corner landscaping rays.");
        public static LocStr SettingsSideRayWorkWeightTooltip =
            Loc.Str("settings.pathfinder.side_ray_weight.tooltip", "Weight applied to lateral and turn-corner landscaping rays.", "Pathfinder cost setting tooltip for lateral and turn-corner landscaping rays.");
        public static LocStr SettingsCandidateRayMaxDistanceLabel =
            Loc.Str("settings.pathfinder.candidate_distance.label", "Candidate ray max distance", "Pathfinder setting label for candidate ray maximum distance.");
        public static LocStr SettingsCandidateRayMaxDistanceTooltip =
            Loc.Str("settings.pathfinder.candidate_distance.tooltip", "Maximum candidate ray trace distance. Higher values protect and price very large side wedges but cost more search time. Default: 16.", "Pathfinder setting tooltip for candidate ray maximum distance.");
        public static LocStr SettingsPathfinderLimitsHeading =
            Loc.Str("settings.pathfinder.limits", "Search limits", "Pathfinder search-limit settings heading.");
        public static LocStr SettingsMaximumVisitedNodesLabel =
            Loc.Str("settings.pathfinder.visited.label", "Maximum visited nodes", "Pathfinder setting label for the maximum number of states examined.");
        public static LocStr SettingsMaximumVisitedNodesTooltip =
            Loc.Str("settings.pathfinder.visited.tooltip", "Maximum states examined. Higher values can solve harder routes but use more time and memory.", "Pathfinder setting tooltip for the maximum number of states examined.");
        public static LocStr SettingsSearchTimeoutLabel =
            Loc.Str("settings.pathfinder.timeout.label", "Search timeout (seconds)", "Pathfinder setting label for the per-search wall-clock timeout.");
        public static LocStr SettingsSearchTimeoutTooltip =
            Loc.Str("settings.pathfinder.timeout.tooltip", "Maximum wall-clock time for one accessway search.", "Pathfinder setting tooltip for the per-search wall-clock timeout.");
        public static LocStr SettingsFrameBudgetLabel =
            Loc.Str("settings.pathfinder.frame_budget.label", "Frame budget (ms)", "Pathfinder setting label for the per-frame work budget.");
        public static LocStr SettingsFrameBudgetTooltip =
            Loc.Str("settings.pathfinder.frame_budget.tooltip", "Approximate search work budget per game frame. Higher values finish sooner but cause longer stalls.", "Pathfinder setting tooltip for the per-frame work budget.");
        public static LocStr SettingsRayMaximumCostLabel =
            Loc.Str("settings.pathfinder.ray_max_cost.label", "Maximum cost per ray", "Pathfinder setting label for ray cost cap.");
        public static LocStr SettingsRayMaximumCostTooltip =
            Loc.Str("settings.pathfinder.ray_max_cost.tooltip", "Caps the landscaping cost contributed by one unresolved ray.", "Pathfinder setting tooltip for ray cost cap.");
        public static LocStr SettingsUnresolvedRayPenaltyLabel =
            Loc.Str("settings.pathfinder.ray_unresolved.label", "Unresolved-ray penalty", "Pathfinder setting label for unresolved ray penalty.");
        public static LocStr SettingsUnresolvedRayPenaltyTooltip =
            Loc.Str("settings.pathfinder.ray_unresolved.tooltip", "Penalty when a ray does not meet terrain inside its trace range.", "Pathfinder setting tooltip for unresolved ray penalty.");
        public static LocStr SettingsTurningRampsLabel =
            Loc.Str("settings.experimental_access.turning_ramps.label", "Turning ramps (experimental)", "Settings toggle label for experimental turning ramps.");
        public static LocStr SettingsTurningRampsTooltip =
            Loc.Str("settings.experimental_access.turning_ramps.tooltip", "When enabled, ATD may select and place routed turning or switchback accessways using vanilla flat and slope designations. AUTO and T1-T3 use routed accessways; Legacy 3-5 use only the straight-ramp generator. Corner and saddle designations are not included.", "Tooltip for experimental turning ramps.");
        public static LocStr SettingsSuppressLegacyRampsLabel =
            Loc.Str("settings.experimental_access.suppress_legacy_ramps.label", "Suppress legacy ramps", "Settings toggle label for suppressing legacy access ramps.");
        public static LocStr SettingsSuppressLegacyRampsTooltip =
            Loc.Str("settings.experimental_access.suppress_legacy_ramps.tooltip", "Disable the legacy straight-ramp generator so experimental accessway results and failures can be tested directly. Leave off for normal fallback behavior.", "Tooltip for suppressing legacy access ramps.");
        public static LocStr SettingsAccessAStarLabel =
            Loc.Str("settings.experimental_access.astar.label", "Use A* search", "Settings toggle label for experimental A* access search.");
        public static LocStr SettingsAccessAStarTooltip =
            Loc.Str("settings.experimental_access.astar.tooltip", "Use A* instead of reference Dijkstra for experimental accessway dry runs. Dijkstra is the safer validation baseline. A* is faster.", "Tooltip for experimental A* access search.");
        public static LocStr SettingsAccessLandscapingCostScaleLabel =
            Loc.Str("settings.experimental_access.landscaping_cost_scale.label", "Landscaping cost vs. distance", "Settings row label for experimental access landscaping cost scale.");
        public static LocStr SettingsAccessLandscapingCostScaleTooltip =
            Loc.Str("settings.experimental_access.landscaping_cost_scale.tooltip", "Tile-distance cost assigned to one unit of landscaping cost. One landscaping-cost unit is equivalent to dumping or digging one unit of rock. Range: 0-100; default: 1. A higher value promotes routes with less terraforming.", "Tooltip for experimental access landscaping cost distance scale.");
        public static LocStr SettingsAccessPropCleanupLandscapingCostLabel =
            Loc.Str("settings.experimental_access.prop_cleanup_landscaping_cost.label", "Prop cleanup landscaping cost", "Settings row label for experimental access prop cleanup cost.");
        public static LocStr SettingsAccessPropCleanupLandscapingCostTooltip =
            Loc.Str("settings.experimental_access.prop_cleanup_landscaping_cost.tooltip", "Landscaping cost charged once per prop cleanup origin used by experimental access search. One unit equals one dumped or dug rock unit; default 8 reflects observed excavator cleanup effort. Higher values make cleanup routes less attractive.", "Tooltip for experimental access prop cleanup cost.");
        public static LocStr SettingsAccessLandslideRunLabel =
            Loc.Str("settings.experimental_access.landslide_run.label", "Landslide protection slope factor", "Settings row label for the experimental landslide envelope scale.");
        public static LocStr SettingsAccessLandslideRunTooltip =
            Loc.Str("settings.experimental_access.landslide_run.tooltip", "Horizontal exclusion distance per vertical terrain level. 1 translates to a 45-degree slope; higher values widen the exclusion zone (use in e.g. pure sand), while lower values narrow it. Range: 0.05-2; default: 1.", "Tooltip for experimental access landslide run setting.");
        public static LocStr SettingsMaxSlopeLabel =
            Loc.Str("settings.max_slope.label", "Max slope", "Settings row label for maximum designation slope.");
        public static LocStr SettingsMaxSlopeTooltip =
            Loc.Str("settings.max_slope.tooltip", "Maximum allowed height difference between adjacent designation corners. Range: 1-3.", "Tooltip for maximum designation slope setting.");
        public static LocStr SettingsBottomFlatteningLabel =
            Loc.Str("settings.bottom_flattening.label", "Bottom flattening", "Settings row label for bottom flattening.");
        public static LocStr SettingsBottomFlatteningTooltip =
            Loc.Str("settings.bottom_flattening.tooltip", "Bottom-flattening strength from 0 to 10. 0 disables the bottom-flattening pass.", "Tooltip for bottom flattening setting.");
        public static LocStr SettingsBatchSizeLabel =
            Loc.Str("settings.batch_size.label", "Batch size", "Settings row label for scan batch size.");
        public static LocStr SettingsBatchSizeTooltip =
            Loc.Str("settings.batch_size.tooltip", "Designations placed per coroutine frame while the game is unpaused. Range: 1-200.", "Tooltip for scan batch size setting.");
        public static LocStr SettingsMiningPanelCollapsedLabel =
            Loc.Str("settings.panel_defaults.mining_collapsed.label", "Mining panel collapsed", "Settings toggle label for default mining panel collapsed state.");
        public static LocStr SettingsMiningPanelCollapsedTooltip =
            Loc.Str("settings.panel_defaults.mining_collapsed.tooltip", "Whether the Mining designations panel starts collapsed by default.", "Tooltip for default mining panel collapsed state.");
        public static LocStr SettingsOrePanelCollapsedLabel =
            Loc.Str("settings.panel_defaults.ore_collapsed.label", "Ore panel collapsed", "Settings toggle label for default ore panel collapsed state.");
        public static LocStr SettingsOrePanelCollapsedTooltip =
            Loc.Str("settings.panel_defaults.ore_collapsed.tooltip", "Whether the Ore composition panel starts collapsed by default.", "Tooltip for default ore panel collapsed state.");
        public static LocStr SettingsFarmingPanelCollapsedLabel =
            Loc.Str("settings.panel_defaults.farming_collapsed.label", "Farming panel collapsed", "Settings toggle label for default farming panel collapsed state.");
        public static LocStr SettingsFarmingPanelCollapsedTooltip =
            Loc.Str("settings.panel_defaults.farming_collapsed.tooltip", "Whether the Farmland preparation panel starts collapsed by default.", "Tooltip for default farming panel collapsed state.");
        public static LocStr SettingsExcavatorNotificationsLabel =
            Loc.Str("settings.notifications.excavator_completion.label", "Excavator completion notifications", "Settings toggle label for excavator completion notifications.");
        public static LocStr SettingsExcavatorNotificationsTooltip =
            Loc.Str("settings.notifications.excavator_completion.tooltip", "Whether ATD shows a green notification when any vehicle depot completes an unassigned (free) excavator.", "Tooltip for excavator completion notifications.");
        public static LocStr SettingsRampNotificationsLabel =
            Loc.Str("settings.notifications.ramp_warning.label", "Ramp warning notifications", "Settings toggle label for ramp warning notifications.");
        public static LocStr SettingsRampNotificationsTooltip =
            Loc.Str("settings.notifications.ramp_warning.tooltip", "Whether ATD shows ramp access warning notifications on mine towers.", "Tooltip for ramp warning notifications.");
        public static LocStr SettingsMinOreHeightLabel =
            Loc.Str("settings.ore_quality.min_ore_height.label", "Minimum ore height", "Settings row label for minimum ore height threshold.");
        public static LocStr SettingsMinOreHeightTooltip =
            Loc.Str("settings.ore_quality.min_ore_height.tooltip", "Minimum ore thickness in terrain tiles for this quality level.", "Tooltip for minimum ore height threshold.");
        public static LocStr SettingsMinBottomDensityLabel =
            Loc.Str("settings.ore_quality.min_bottom_density.label", "Minimum bottom density", "Settings row label for minimum bottom density threshold.");
        public static LocStr SettingsMinBottomDensityTooltip =
            Loc.Str("settings.ore_quality.min_bottom_density.tooltip", "Minimum ore density from the previous ore bottom to this ore bottom. Clamped from 0 to 1.", "Tooltip for minimum bottom density threshold.");
        public static LocStr SettingsMinOrePurityLabel =
            Loc.Str("settings.ore_quality.min_ore_purity.label", "Minimum ore purity", "Settings row label for minimum ore purity threshold.");
        public static LocStr SettingsMinOrePurityTooltip =
            Loc.Str("settings.ore_quality.min_ore_purity.tooltip", "Minimum ore-to-column ratio for this quality level. Clamped from 0 to 1.", "Tooltip for minimum ore purity threshold.");
        public static LocStr SettingsMinComponentSizeLabel =
            Loc.Str("settings.ore_quality.min_component_size.label", "Minimum component size", "Settings row label for minimum component size threshold.");
        public static LocStr SettingsMinComponentSizeTooltip =
            Loc.Str("settings.ore_quality.min_component_size.tooltip", "Minimum connected designation tile count for a cluster to survive the isolation filter.", "Tooltip for minimum component size threshold.");
        public static LocStr SettingsCornerModeLabel =
            Loc.Str("settings.corner_mode.label", "Corner designations mode", "Settings row label for corner designations mode shortcut.");
        public static LocStr SettingsCornerModeTooltip =
            Loc.Str("settings.corner_mode.tooltip", "Key used to enter and toggle corner designation mode while a terrain designation tool is active.", "Tooltip for corner designations mode shortcut.");
        public static LocStr SettingsCornerModeInvalidTooltip =
            Loc.Str("settings.corner_mode.invalid_tooltip", "Use a single key such as K, 1, F1, Space, or Escape.", "Validation error tooltip for corner designations mode shortcut.");
        public static LocStr SettingsApplied =
            Loc.Str("settings.status.applied", "Applied", "Status message after applying a setting.");
        public static LocStr SettingsInvalidKey =
            Loc.Str("settings.status.invalid_key", "Invalid key", "Status message for an invalid shortcut key.");
        public static LocStr SettingsSaveAsGlobal =
            Loc.Str("settings.action.save_as_global", "Save as config", "Button label for saving settings as config default.");
        public static LocStr SettingsSaveAsGlobalTooltip =
            Loc.Str("settings.action.save_as_global.tooltip", "Save these settings to ATDsettings.json. They will be used as the defaults for all new games.", "Tooltip for saving settings as config default.");
        public static LocStr SettingsRestoreDefaults =
            Loc.Str("settings.action.restore_defaults", "Restore defaults", "Button label for restoring default settings.");
        public static LocStr SettingsRestoreDefaultsTooltip =
            Loc.Str("settings.action.restore_defaults.tooltip", "Restore the global mod defaults for all settings. (Does not automatically save them as config.)", "Tooltip for restoring default settings.");
        public static LocStr SettingsSavedToFile =
            Loc.Str("settings.status.saved_to_file", "Saved to ATDsettings.json.", "Status message after settings are saved.");
        public static LocStr SettingsSaveFailed =
            Loc.Str("settings.status.save_failed", "Save failed; check the log.", "Status message after settings save fails.");
        public static LocStr SettingsRestoredDefaults =
            Loc.Str("settings.status.restored_defaults", "Restored built-in defaults in memory.", "Status message after settings are restored to defaults.");

        // ------------------------------------------------------------------ //
        // Ore composition panel
        // ------------------------------------------------------------------ //
        public static LocStr OreTitle =
            Loc.Str("panel.ore.title", "Ore composition", "Title of the ore composition inspector panel.");
        public static LocStr OreDescription =
            Loc.Str("panel.ore.description", "Ore resources within this tower's current mining designations. (Does not account for potential landslides.)", "Tooltip on the ore composition panel title.");
        public static LocStr OrePromptScan =
            Loc.Str("panel.ore.prompt_scan", "Press \u21ba to scan ore composition.", "Prompt shown before a scan is run.");
        public static LocStr OreScanTip =
            Loc.Str("panel.ore.scan_tooltip", "Scan ore composition", "Tooltip on the scan/refresh button in the ore composition panel.");
        public static LocStr OreNoTower =
            Loc.Str("panel.ore.no_tower", "No tower selected.", "Message shown when no tower is selected in the ore panel.");
        public static LocStr OreNoMinableDesig =
            Loc.Str("panel.ore.no_minable_designations", "No minable designations found.", "Message shown when the scan finds no minable designations.");
        public static LocStr OrePrioritySelectedTipFmt =
            Loc.Str("panel.ore.priority_selected_tooltip", "Tower mining priority set to {0}. Click to unset.", "Tooltip on a priority button when that product is already prioritized. {0} = colored product name.");
        public static LocStr OrePrioritySetTipFmt =
            Loc.Str("panel.ore.priority_set_tooltip", "Set tower mining priority to {0}.", "Tooltip on a priority button. {0} = colored product name.");

        // ------------------------------------------------------------------ //
        // Farming analysis panel
        // ------------------------------------------------------------------ //
        public static LocStr FarmingTitle =
            Loc.Str("panel.farming.title", "Farmland preparation", "Title of the farmland preparation inspector panel.");
        public static LocStr FarmingDescription =
            Loc.Str("panel.farming.description", "Automates the preparation and final filling of flat level designations so their top layer becomes farmable.", "Tooltip on the farmland preparation panel title.");
        public static LocStr FarmingToggleLabel =
            Loc.Str("panel.farming.automation_toggle.label", "Farmland preparation automation", "Label on the farming automation toggle.");
        public static LocStr FarmingToggleTip =
            Loc.Str("panel.farming.automation_toggle.tooltip", "Prepare flat level designations for farmland by clearing unsuitable top material, then restoring the final fill orders.", "Tooltip on the farming automation toggle.");
        public static LocStr FarmingIdleReleaseExcavatorsLabel =
            Loc.Str("panel.farming.idle_release_excavators.label", "Auto-release excavators when idle", "Label on the auto-release excavators when idle toggle.");
        public static LocStr FarmingIdleReleaseExcavatorsTip =
            Loc.Str("panel.farming.idle_release_excavators.tooltip",
                "Automatically unassign excavators from this tower when no designation has pending excavation work, or while the tower is paused.\n" +
                "Excavators are tracked and re-assigned when excavation work returns.",
                "Tooltip on the auto-release excavators when idle toggle.");
        public static LocStr FarmingIdleReleaseTrucksLabel =
            Loc.Str("panel.farming.idle_release_trucks.label", "Auto-release trucks when idle", "Label on the auto-release trucks when idle toggle.");
        public static LocStr FarmingIdleReleaseTrucksTip =
            Loc.Str("panel.farming.idle_release_trucks.tooltip",
                "Automatically unassign trucks from this tower when no designation has pending excavation work, or while the tower is paused.\n" +
                "Trucks are tracked and re-assigned when excavation work returns.",
                "Tooltip on the auto-release trucks when idle toggle.");
        public static LocStr FarmingVehicleStatusAssigned =
            Loc.Str("panel.farming.vehicle_status.assigned", "Assigned: {0}", "Prefix for the mine-tower assigned-vehicle status summary. {0} is the vehicle list or the localized none value.");
        public static LocStr FarmingVehicleStatusNone =
            Loc.Str("panel.farming.vehicle_status.none", "none", "Value shown when no vehicles are assigned to a mine tower.");
        public static LocStr FarmingVehicleStatusReleased =
            Loc.Str("panel.farming.vehicle_status.released", "ATD-released: ", "Prefix for vehicles temporarily released by ATD.");
        public static LocStr FarmingVehicleStatusDestroyed =
            Loc.Str("panel.farming.vehicle_status.destroyed", "<destroyed>", "Title shown for a destroyed vehicle in the mine-tower status summary.");
        // ------------------------------------------------------------------ //
        // Toolbox items
        // ------------------------------------------------------------------ //
        public static LocStr CornerOuterTip =
            Loc.Str("toolbox.corner_outer.tooltip", "Corner (outer): place convex corner ramps.", "Tooltip on the outer corner toolbox item.");
        public static LocStr CornerInnerTip =
            Loc.Str("toolbox.corner_inner.tooltip", "Corner (inner): place concave corner ramps.", "Tooltip on the inner corner toolbox item.");

        // ------------------------------------------------------------------ //
        // Notifications
        // ------------------------------------------------------------------ //
        public static LocStr NotifRampFailed =
            Loc.Str("notification.ramp_access_failed", "[ATD] {entity} could not start an access ramp", "Notification: ramp generation failed. {entity} is substituted by the game.");
        public static LocStr NotifRampTruncated =
            Loc.Str("notification.ramp_access_truncated", "[ATD] {entity} could not fit a full access ramp", "Notification: ramp was truncated. {entity} is substituted by the game.");
        public static LocStr NotifRampNotAccessible =
            Loc.Str("notification.ramp_access_not_accessible", "[ATD] {entity} could not path to the ramp", "Notification: ramp not accessible. {entity} is substituted by the game.");
        public static LocStr NotifFarmingComplete =
            Loc.Str("notification.farming_complete", "[ATD] {entity} farming preparation and filling complete", "Notification: farming complete. {entity} is substituted by the game.");
        public static LocStr NotifExcavatorCompleted =
            Loc.Str("notification.excavator_completed", "[ATD] {entity} completed an unassigned {0}", "Notification: excavator built. {entity} is substituted by the game, {0} is vehicle type description.");
        public static LocStr NotifDebrisCleanupQueued =
            Loc.Str("notification.debris_cleanup_queued", "[ATD] {entity} has debris cleanup queued", "Notification: Clear debris requests are queued or active. {entity} is substituted by the game.");
        public static LocStr NotifDebrisCleanupNoneFound =
            Loc.Str("notification.debris_cleanup_none_found", "[ATD] {entity} found no debris to clear", "Notification: Clear debris found no blocking props. {entity} is substituted by the game.");
        public static LocStr NotifDebrisCleanupNoneReachable =
            Loc.Str("notification.debris_cleanup_none_reachable", "[ATD] {entity} found no reachable debris to clear. Ctrl-click to include unreachable debris", "Notification: Clear debris found blocking props but none passed reachability filtering. {entity} is substituted by the game.");

        public static LocStr EnqueueConfirmPromptSingular =
            Loc.Str("vehicle_order.confirm.singular", "Order a new {0} for {2} at {1}?", "Vehicle construction confirmation. Vehicle, depot, tower.");
        public static LocStr EnqueueConfirmPromptPlural =
            Loc.Str("vehicle_order.confirm.plural", "Order {0} new {1}s for {3} at {2}?", "Vehicle construction confirmation. Count, vehicle, depot, tower.");
        public static LocStr EnqueueConfirmBtnText =
            Loc.Str("vehicle_order.confirm.button", "Order", "Vehicle construction confirmation button.");
        public static LocStr ZoomToDepotTooltip =
            Loc.Str("vehicle_order.zoom.tooltip", "Zoom to {0}", "Tooltip for zooming to the selected vehicle depot.");
        public static LocStr PreAssignedTooltipFmt =
            Loc.Str("vehicle_order.preassigned.tooltip", "Pre-assigned to {0}", "Tooltip for a queued vehicle pre-assigned to a tower.");
        public static LocStr OrderConstructionShortcutHint =
            Loc.Str("vehicle_order.shortcut.tooltip", "Shift-Alt-click to order a new {0} for {2} at {1}", "Hint appended to the vanilla assign-vehicle floater. Vehicle, depot, target.");

    }
}

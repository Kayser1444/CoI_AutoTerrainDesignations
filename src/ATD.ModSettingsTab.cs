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
using System.Globalization;
using CoI.AutoHelpers.Settings;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;
using Display = Mafi.Unity.Ui.Library.Display;
using SliderWithIncrements = Mafi.Unity.Ui.Library.SliderWithIncrements;
using Row = Mafi.Unity.UiToolkit.Library.Row;

namespace AutoTerrainDesignations
{
    internal static class AtdModSettingsTab
    {
        private const string MOD_ID = "auto-terrain-designations";
        private const string MOD_ICON = "Assets/Unity/UserInterface/Toolbar/Flatten.svg";
        private const string DEFAULTS_ICON = "Assets/Unity/UserInterface/Toolbar/Copy.svg";
        private const string GAME_SETTINGS_ICON = "Assets/Unity/UserInterface/EntityIcons/Gears.png";
        private const string ORE_QUALITY_ICON = "Assets/Unity/UserInterface/General/SwapVertical.svg";
        private const string PATHFINDER_ICON = "Assets/Unity/UserInterface/General/Connect128.png";

        internal static ModSettingsTab BuildDefaultsTab()
        {
            return new ModSettingsTab(
                MOD_ID,
                AtdLocalization.SettingsModName.AsFormatted,
                AtdLocalization.SettingsTabDefaults.AsFormatted,
                100,
                BuildDefaultsContent,
                DEFAULTS_ICON,
                MOD_ICON);
        }

        internal static ModSettingsTab BuildOreQualityTab()
        {
            return new ModSettingsTab(
                MOD_ID,
                AtdLocalization.SettingsModName.AsFormatted,
                AtdLocalization.SettingsTabOreQuality.AsFormatted,
                120,
                BuildOreQualityContent,
                ORE_QUALITY_ICON);
        }

        internal static ModSettingsTab BuildWorldSettingsTab()
        {
            return new ModSettingsTab(
                MOD_ID,
                AtdLocalization.SettingsModName.AsFormatted,
                AtdLocalization.SettingsTabWorldSettings.AsFormatted,
                110,
                BuildWorldSettingsContent,
                GAME_SETTINGS_ICON);
        }

        internal static ModSettingsTab BuildPathfinderTab()
        {
            return new ModSettingsTab(
                MOD_ID,
                AtdLocalization.SettingsModName.AsFormatted,
                Loc.Str("settings.tab.pathfinder", "Accessways", "Settings tab title for ATD accessway settings.").AsFormatted,
                130,
                BuildPathfinderContent,
                PATHFINDER_ICON);
        }

        private static UiComponent BuildDefaultsContent()
        {
            var refreshers = new List<Action>();
            var content = BuildSettingsColumn();

            AddMiningDefaultsSection(content, refreshers);
            AddPanelDefaultsSection(content, refreshers);

            content.Add(BuildFooter(refreshers));

            return content;
        }

        private static UiComponent BuildWorldSettingsContent()
        {
            var refreshers = new List<Action>();
            var content = BuildSettingsColumn();

            AddScanBehaviorSection(content, refreshers);
            AddWorldSafetySection(content, refreshers);
            AddNotificationsSection(content, refreshers);

            content.Add(BuildFooter(refreshers));

            return content;
        }

        private static UiComponent BuildPathfinderContent()
        {
            var refreshers = new List<Action>();
            var content = BuildSettingsColumn();
            AddExperimentalAccessSection(content, refreshers);
            content.Add(BuildFooter(refreshers));
            return content;
        }

        private static UiComponent BuildOreQualityContent()
        {
            var refreshers = new List<Action>();
            var content = BuildSettingsColumn();

            for (int level = 0; level < AutoDepthDesignation.PurityLevelCount; level++)
            {
                int capturedLevel = level;
                content.Add(BuildSectionHeading(L(LevelName(capturedLevel))));
                content.Add(BuildFloatStepRow(
                    AtdLocalization.SettingsMinOreHeightLabel.AsFormatted,
                    AtdLocalization.SettingsMinOreHeightTooltip.AsFormatted,
                    () => AutoDepthDesignation.GetMinOreHeightForLevel(capturedLevel),
                    value => AutoDepthDesignation.TrySetMinOreHeightForLevel(capturedLevel, Math.Max(0f, value)),
                    FormatFloat,
                    refreshers));
                content.Add(BuildFloatStepRow(
                    AtdLocalization.SettingsMinBottomDensityLabel.AsFormatted,
                    AtdLocalization.SettingsMinBottomDensityTooltip.AsFormatted,
                    () => AutoDepthDesignation.GetMinBottomOreDensityForLevel(capturedLevel),
                    value => AutoDepthDesignation.TrySetMinBottomOreDensityForLevel(capturedLevel, value),
                    FormatRatio,
                    refreshers));
                content.Add(BuildFloatStepRow(
                    AtdLocalization.SettingsMinOrePurityLabel.AsFormatted,
                    AtdLocalization.SettingsMinOrePurityTooltip.AsFormatted,
                    () => AutoDepthDesignation.GetMinOrePurityForLevel(capturedLevel),
                    value => AutoDepthDesignation.TrySetMinOrePurityForLevel(capturedLevel, value),
                    FormatRatio,
                    refreshers));
                content.Add(BuildIntStepRow(
                    AtdLocalization.SettingsMinComponentSizeLabel.AsFormatted,
                    AtdLocalization.SettingsMinComponentSizeTooltip.AsFormatted,
                    () => AutoDepthDesignation.GetMinComponentSizeForLevel(capturedLevel),
                    value => AutoDepthDesignation.TrySetMinComponentSizeForLevel(capturedLevel, value),
                    value => value.ToString(CultureInfo.InvariantCulture),
                    refreshers));
            }

            content.Add(BuildFooter(refreshers));

            return content;
        }

        private static Column BuildSettingsColumn()
        {
            return new Column(2.pt())
                .AlignItemsStretch()
                .PaddingLeft(1.pt())
                .PaddingRight(1.pt());
        }

        private static Title BuildSectionHeading(LocStrFormatted title)
        {
            return new Title(title)
                .Color(Theme.PrimaryColor)
                .MarginTop(2.pt())
                .MarginLeft(-1.pt());
        }

        private static void AddMiningDefaultsSection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingMiningDefaults.AsFormatted));

            content.Add(BuildAccesswayModeRow(refreshers));
            content.Add(BuildIntStepRow(
                AtdLocalization.DesigMaxLayersLabel.AsFormatted,
                AtdLocalization.DesigMaxLayersTip.AsFormatted,
                () => AutoTerrainDesignationsMod.MaxLayersToExcavate,
                value => AutoTerrainDesignationsMod.SetMaxLayersToExcavate(value),
                FormatNoLimitZero,
                refreshers));
            content.Add(BuildNullableDepthRow(refreshers));
            content.Add(BuildOreQualitySliderRow(refreshers));
        }

        private static Row BuildAccesswayModeRow(List<Action> refreshers)
        {
            var dropdown = new Dropdown<AccessVehicleClearanceMode>(DesignationPanel.AccesswayModeOption)
                .SetOptions(
                    AccessVehicleClearanceMode.Off,
                    AccessVehicleClearanceMode.Auto,
                    AccessVehicleClearanceMode.T1,
                    AccessVehicleClearanceMode.T2,
                    AccessVehicleClearanceMode.T3,
                    AccessVehicleClearanceMode.LegacyWidth3,
                    AccessVehicleClearanceMode.LegacyWidth4,
                    AccessVehicleClearanceMode.LegacyWidth5)
                .SetValue(AutoTerrainDesignationsMod.VehicleClearance)
                .OnValueChanged((mode, _) => AutoTerrainDesignationsMod.SetVehicleClearance(mode));
            dropdown.Width(130.px());
            refreshers.Add(() => dropdown.SetValue(AutoTerrainDesignationsMod.VehicleClearance));

            var row = new Row().MarginTop(1.pt()).AlignItemsCenter();
            row.Add(new Label(AtdLocalization.DesigAccesswayModeLabel.AsFormatted));
            row.Add(new UiComponent().FlexGrow(1f));
            row.Add(dropdown);
            return row;
        }

        private static Row BuildOreQualitySliderRow(List<Action> refreshers)
        {
            var display = new Display(L(FormatOrePurityLevel(
                AutoTerrainDesignationsMod.OrePurityLevel)))
                .MinDigits(6).AlignSelfStretch().MarginTopBottom(2.px());
            var slider = new SliderWithIncrements()
                .Range(0, 4)
                .Value(AutoTerrainDesignationsMod.OrePurityLevel)
                .OnValueChangedForPreview(value =>
                    display.SetValue(L(FormatOrePurityLevel(value))))
                .OnValueChanged(value =>
                {
                    AutoTerrainDesignationsMod.SetOrePurityLevel(value);
                    display.SetValue(L(FormatOrePurityLevel(value)));
                });
            slider.FlexGrow(0f).Width(150.px());
            refreshers.Add(() =>
            {
                int value = AutoTerrainDesignationsMod.OrePurityLevel;
                slider.Value(value);
                display.SetValue(L(FormatOrePurityLevel(value)));
            });

            var row = new Row().MarginTop(1.pt()).AlignItemsCenter().Gap(1.pt());
            row.Add(new Label(AtdLocalization.DesigOrePurityLabel.AsFormatted)
                .Tooltip(AtdLocalization.DesigOrePurityTip.AsFormatted));
            row.Add(new UiComponent().FlexGrow(1f));
            row.Add(slider);
            row.Add(display);
            return row;
        }

        private static void AddScanBehaviorSection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingDesignations.AsFormatted));

            content.Add(BuildIntStepRow(
                AtdLocalization.SettingsMaxSlopeLabel.AsFormatted,
                AtdLocalization.SettingsMaxSlopeTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.MaxHeightDiff,
                value => AutoTerrainDesignationsMod.SetMaxHeightDiff(value),
                value => value.ToString(CultureInfo.InvariantCulture),
                refreshers));
            content.Add(BuildIntStepRow(
                AtdLocalization.SettingsBottomFlatteningLabel.AsFormatted,
                AtdLocalization.SettingsBottomFlatteningTooltip.AsFormatted,
                GetBottomFlatteningValue,
                SetBottomFlatteningValue,
                value => value.ToString(CultureInfo.InvariantCulture),
                refreshers));
        }


        private static void AddExperimentalAccessSection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingExperimentalAccess.AsFormatted));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsTurningRampsLabel.AsFormatted,
                AtdLocalization.SettingsTurningRampsTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.TurningRampsExperimental,
                AutoTerrainDesignationsMod.SetTurningRampsExperimental,
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsSuppressLegacyRampsLabel.AsFormatted,
                AtdLocalization.SettingsSuppressLegacyRampsTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.SuppressLegacyAccessRamps,
                AutoTerrainDesignationsMod.SetSuppressLegacyAccessRamps,
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsAccessAStarLabel.AsFormatted,
                AtdLocalization.SettingsAccessAStarTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.ExperimentalAccessUseAStar,
                AutoTerrainDesignationsMod.SetExperimentalAccessUseAStar,
                refreshers));
            content.Add(BuildQuickRemoveDebrisPolicyRow(refreshers));
            content.Add(BuildFloatStepRow(
                AtdLocalization.SettingsAccessLandscapingCostScaleLabel.AsFormatted,
                AtdLocalization.SettingsAccessLandscapingCostScaleTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.AccessLandscapingCostDistanceScale,
                value =>
                {
                    AutoTerrainDesignationsMod.SetAccessLandscapingCostDistanceScale(value);
                    return true;
                },
                FormatFloat,
                refreshers));
            content.Add(BuildFloatStepRow(
                AtdLocalization.SettingsAccessPropCleanupLandscapingCostLabel.AsFormatted,
                AtdLocalization.SettingsAccessPropCleanupLandscapingCostTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.AccessPropCleanupLandscapingCost,
                value =>
                {
                    AutoTerrainDesignationsMod.SetAccessPropCleanupLandscapingCost(value);
                    return true;
                },
                FormatFloat,
                refreshers));
            content.Add(BuildFloatStepRow(
                AtdLocalization.SettingsAccessLandslideRunLabel.AsFormatted,
                AtdLocalization.SettingsAccessLandslideRunTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.AccessLandslideRunPerHeight,
                value =>
                {
                    AutoTerrainDesignationsMod.SetAccessLandslideRunPerHeight(value);
                    return true;
                },
                FormatFloat,
                refreshers));
            content.Add(BuildSectionHeading(Loc.Str("settings.pathfinder.costs", "Route costs", "Pathfinder cost settings heading.").AsFormatted));
            AddPathfinderFloat(content, refreshers, AtdLocalization.SettingsTerrainDesignationCostLabel, AtdLocalization.SettingsTerrainDesignationCostTooltip, () => AutoTerrainDesignationsMod.AccessGeneratedVFixedCost, AutoTerrainDesignationsMod.SetAccessGeneratedVFixedCost);
            AddPathfinderFloat(content, refreshers, AtdLocalization.SettingsDirectTerrainWorkWeightLabel, AtdLocalization.SettingsDirectTerrainWorkWeightTooltip, () => AutoTerrainDesignationsMod.AccessDirectWorkWeight, AutoTerrainDesignationsMod.SetAccessDirectWorkWeight);
            AddPathfinderFloat(content, refreshers, "side_ray_weight", "Side-ray work weight", "Weight applied to lateral and turn-corner landscaping rays.", () => AutoTerrainDesignationsMod.AccessSideRayWeight, AutoTerrainDesignationsMod.SetAccessSideRayWeight);

            AddPathfinderInt(content, refreshers, AtdLocalization.SettingsCandidateRayMaxDistanceLabel, AtdLocalization.SettingsCandidateRayMaxDistanceTooltip, () => AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance, AutoTerrainDesignationsMod.SetAccessCandidateRayMaxDistance);

            content.Add(BuildSectionHeading(Loc.Str("settings.pathfinder.limits", "Search limits", "Pathfinder search-limit settings heading.").AsFormatted));
            AddPathfinderInt(content, refreshers, "visited", "Maximum visited nodes", "Maximum states examined. Higher values can solve harder routes but use more time and memory.", () => AutoTerrainDesignationsMod.AccessMaxVisitedNodes, AutoTerrainDesignationsMod.SetAccessMaxVisitedNodes, 10000, 50000, 100000);
            AddPathfinderInt(content, refreshers, "timeout", "Search timeout (seconds)", "Maximum wall-clock time for one accessway search.", () => AutoTerrainDesignationsMod.AccessSearchTimeoutSeconds, AutoTerrainDesignationsMod.SetAccessSearchTimeoutSeconds, 5, 15, 60);
            AddPathfinderInt(content, refreshers, "frame_budget", "Frame budget (ms)", "Approximate search work budget per game frame. Higher values finish sooner but cause longer stalls.", () => AutoTerrainDesignationsMod.AccessSearchFrameBudgetMs, AutoTerrainDesignationsMod.SetAccessSearchFrameBudgetMs, 1, 5, 10);
            AddPathfinderFloat(content, refreshers, AtdLocalization.SettingsRayMaximumCostLabel, AtdLocalization.SettingsRayMaximumCostTooltip, () => AutoTerrainDesignationsMod.AccessRayMaxCost, AutoTerrainDesignationsMod.SetAccessRayMaxCost, 10f, 50f, 100f);
            AddPathfinderFloat(content, refreshers, AtdLocalization.SettingsUnresolvedRayPenaltyLabel, AtdLocalization.SettingsUnresolvedRayPenaltyTooltip, () => AutoTerrainDesignationsMod.AccessRayUnresolvedPenalty, AutoTerrainDesignationsMod.SetAccessRayUnresolvedPenalty, 10f, 50f, 100f);
        }

        private static void AddWorldSafetySection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingWorldSafety.AsFormatted));
            content.Add(BuildIntStepRow(
                AtdLocalization.SettingsSafetyPolicyLabel.AsFormatted,
                AtdLocalization.SettingsSafetyPolicyTooltip.AsFormatted,
                () => (int)AutoTerrainDesignationsMod.GetSafetyPolicy(),
                value => AutoTerrainDesignationsMod.SetSafetyPolicy(
                    (SafetyPolicy)Math.Max((int)SafetyPolicy.Min,
                        Math.Min((int)SafetyPolicy.Max, value))),
                value => FormatSafetyPolicy((SafetyPolicy)value),
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsAvoidOceanLabel.AsFormatted,
                AtdLocalization.SettingsAvoidOceanTooltip.AsFormatted,
                () => AutoDepthDesignation.AccessAvoidOcean,
                AutoDepthDesignation.SetAccessAvoidOcean,
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsAvoidBuildingsLabel.AsFormatted,
                AtdLocalization.SettingsAvoidBuildingsTooltip.AsFormatted,
                () => AutoDepthDesignation.AccessAvoidBuildings,
                AutoDepthDesignation.SetAccessAvoidBuildings,
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsHarvestDisruptedTreesLabel.AsFormatted,
                AtdLocalization.SettingsHarvestDisruptedTreesTooltip.AsFormatted,
                () => AutoDepthDesignation.AccessHarvestDisruptedTrees,
                AutoDepthDesignation.SetAccessHarvestDisruptedTrees,
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsAllowDigToRemoveDebrisLabel.AsFormatted,
                AtdLocalization.SettingsAllowDigToRemoveDebrisTooltip.AsFormatted,
                () => AutoDepthDesignation.AccessAllowDigToRemoveDebris,
                AutoDepthDesignation.SetAccessAllowDigToRemoveDebris,
                refreshers));
        }

        private static Row BuildQuickRemoveDebrisPolicyRow(List<Action> refreshers)
        {
            var dropdown = new Dropdown<QuickRemoveDebrisPolicy>(QuickRemoveDebrisOption)
                .SetOptions(
                    QuickRemoveDebrisPolicy.Always,
                    QuickRemoveDebrisPolicy.Restrictive,
                    QuickRemoveDebrisPolicy.Never)
                .SetValue(AutoDepthDesignation.AccessQuickRemoveDebrisPolicy)
                .OnValueChanged((policy, _) =>
                    AutoDepthDesignation.SetAccessQuickRemoveDebrisPolicy(policy));
            dropdown.Width(110.px());
            refreshers.Add(() => dropdown.SetValue(
                AutoDepthDesignation.AccessQuickRemoveDebrisPolicy));

            var row = new Row().MarginTop(1.pt()).AlignItemsCenter();
            row.Add(new Label(AtdLocalization.SettingsQuickRemoveDebrisLabel.AsFormatted)
                .Tooltip(AtdLocalization.SettingsQuickRemoveDebrisTooltip.AsFormatted));
            row.Add(new UiComponent().FlexGrow(1f));
            row.Add(dropdown);
            return row;
        }

        private static UiComponent QuickRemoveDebrisOption(
            QuickRemoveDebrisPolicy policy, int index, bool isInDropdown)
        {
            switch (policy)
            {
                case QuickRemoveDebrisPolicy.Always:
                    return new Label(AtdLocalization.SettingsQuickRemoveDebrisAlways.AsFormatted)
                        .Tooltip(AtdLocalization.SettingsQuickRemoveDebrisAlwaysTooltip.AsFormatted);
                case QuickRemoveDebrisPolicy.Never:
                    return new Label(AtdLocalization.SettingsQuickRemoveDebrisNever.AsFormatted)
                        .Tooltip(AtdLocalization.SettingsQuickRemoveDebrisNeverTooltip.AsFormatted);
                default:
                    return new Label(AtdLocalization.SettingsQuickRemoveDebrisRestrictive.AsFormatted)
                        .Tooltip(AtdLocalization.SettingsQuickRemoveDebrisRestrictiveTooltip.AsFormatted);
            }
        }

        private static void AddPathfinderFloat(Column content, List<Action> refreshers,
            string key, string label, string tooltip, Func<float> getter, Action<float> setter)
            => content.Add(BuildFloatStepRow(
                Loc.Str("settings.pathfinder." + key + ".label", label, label).AsFormatted,
                Loc.Str("settings.pathfinder." + key + ".tooltip", tooltip, tooltip).AsFormatted,
                getter, value => { setter(value); return true; }, FormatFloat, refreshers));

        private static void AddPathfinderFloat(Column content, List<Action> refreshers,
            LocStr label, LocStr tooltip, Func<float> getter, Action<float> setter,
            float step = 0.05f, float shiftStep = 0.10f, float ctrlStep = 0.25f)
            => content.Add(BuildFloatStepRow(
                label.AsFormatted, tooltip.AsFormatted,
                getter, value => { setter(value); return true; }, FormatFloat, refreshers,
                step, shiftStep, ctrlStep));

        private static void AddPathfinderInt(Column content, List<Action> refreshers,
            string key, string label, string tooltip, Func<int> getter, Action<int> setter,
            int step = 1, int shiftStep = 5, int ctrlStep = 10)
            => content.Add(BuildIntStepRow(
                Loc.Str("settings.pathfinder." + key + ".label", label, label).AsFormatted,
                Loc.Str("settings.pathfinder." + key + ".tooltip", tooltip, tooltip).AsFormatted,
                getter, setter, value => value.ToString(CultureInfo.InvariantCulture), refreshers,
                step, shiftStep, ctrlStep));

        private static void AddPathfinderInt(Column content, List<Action> refreshers,
            LocStr label, LocStr tooltip, Func<int> getter, Action<int> setter,
            int baseStep = 1)
            => content.Add(BuildIntStepRow(
                label.AsFormatted, tooltip.AsFormatted,
                getter, setter, value => value.ToString(CultureInfo.InvariantCulture), refreshers, baseStep));

        private static void AddPanelDefaultsSection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingPanelDefaults.AsFormatted));

            content.Add(BuildToggleRow(
                AtdLocalization.SettingsMiningPanelCollapsedLabel.AsFormatted,
                AtdLocalization.SettingsMiningPanelCollapsedTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed,
                value => AutoTerrainDesignationsMod.SetTerrainDesignationsPanelCollapsed(value),
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsOrePanelCollapsedLabel.AsFormatted,
                AtdLocalization.SettingsOrePanelCollapsedTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.OreCompositionPanelCollapsed,
                value => AutoTerrainDesignationsMod.SetOreCompositionPanelCollapsed(value),
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsFarmingPanelCollapsedLabel.AsFormatted,
                AtdLocalization.SettingsFarmingPanelCollapsedTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.FarmingPanelCollapsed,
                value => AutoTerrainDesignationsMod.SetFarmingPanelCollapsed(value),
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.FarmingIdleReleaseExcavatorsLabel.AsFormatted,
                AtdLocalization.FarmingIdleReleaseExcavatorsTip.AsFormatted,
                () => AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle,
                value => AutoTerrainDesignationsMod.SetAutoReleaseExcavatorsWhenIdle(value),
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.FarmingIdleReleaseTrucksLabel.AsFormatted,
                AtdLocalization.FarmingIdleReleaseTrucksTip.AsFormatted,
                () => AutoTerrainDesignationsMod.AutoReleaseTrucksWhenIdle,
                value => AutoTerrainDesignationsMod.SetAutoReleaseTrucksWhenIdle(value),
                refreshers));
        }

        private static void AddNotificationsSection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingNotifications.AsFormatted));

            content.Add(BuildToggleRow(
                AtdLocalization.SettingsExcavatorNotificationsLabel.AsFormatted,
                AtdLocalization.SettingsExcavatorNotificationsTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.ExcavatorCompletionNotificationsEnabled,
                value => AutoTerrainDesignationsMod.SetExcavatorCompletionNotificationsEnabled(value),
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsRampNotificationsLabel.AsFormatted,
                AtdLocalization.SettingsRampNotificationsTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.RampNotificationsEnabled,
                value => AutoTerrainDesignationsMod.SetRampNotificationsEnabled(value),
                refreshers));
        }

        private static Row BuildIntStepRow(
            LocStrFormatted label,
            LocStrFormatted tooltip,
            Func<int> getValue,
            Action<int> setValue,
            Func<int, string> format,
            List<Action> refreshers,
            int step = 1,
            int shiftStep = 5,
            int ctrlStep = 10)
        {
            var display = new Display(L(format(getValue()))).MinDigits(4).AlignSelfStretch().MarginTopBottom(2.px());
            void Refresh() => display.SetValue(L(format(getValue())));
            refreshers.Add(Refresh);

            return BuildStepRow(
                label,
                tooltip,
                display,
                () =>
                {
                    setValue(getValue() + IntStepSize(step, shiftStep, ctrlStep));
                    Refresh();
                },
                () =>
                {
                    setValue(getValue() - IntStepSize(step, shiftStep, ctrlStep));
                    Refresh();
                });
        }

        private static int GetBottomFlatteningValue()
        {
            return AutoTerrainDesignationsMod.BottomFlatteningEnabled
                ? AutoTerrainDesignationsMod.BottomFlatteningStrength
                : 0;
        }

        private static void SetBottomFlatteningValue(int value)
        {
            int clamped = Math.Max(0, Math.Min(10, value));
            AutoTerrainDesignationsMod.SetBottomFlatteningEnabled(clamped > 0);
            if (clamped > 0)
                AutoTerrainDesignationsMod.SetBottomFlatteningStrength(clamped);
        }

        private static Row BuildFloatStepRow(
            LocStrFormatted label,
            LocStrFormatted tooltip,
            Func<float> getValue,
            Func<float, bool> setValue,
            Func<float, string> format,
            List<Action> refreshers,
            float step = 0.05f,
            float shiftStep = 0.10f,
            float ctrlStep = 0.25f)
        {
            var display = new Display(L(format(getValue()))).MinDigits(5).AlignSelfStretch().MarginTopBottom(2.px());
            void Refresh() => display.SetValue(L(format(getValue())));
            refreshers.Add(Refresh);

            return BuildStepRow(
                label,
                tooltip,
                display,
                () =>
                {
                    setValue(getValue() + FloatStepSize(step, shiftStep, ctrlStep));
                    Refresh();
                },
                () =>
                {
                    setValue(getValue() - FloatStepSize(step, shiftStep, ctrlStep));
                    Refresh();
                });
        }

        private static Row BuildNullableDepthRow(List<Action> refreshers)
        {
            var display = new Display(L(FormatDepth(AutoTerrainDesignationsMod.MaxDepthToDigTo)))
                .MinDigits(4)
                .AlignSelfStretch()
                .MarginTopBottom(2.px());
            void Refresh() => display.SetValue(L(FormatDepth(AutoTerrainDesignationsMod.MaxDepthToDigTo)));
            refreshers.Add(Refresh);

            var row = BuildStepRow(
                AtdLocalization.DesigElevLimitLabel.AsFormatted,
                AtdLocalization.DesigElevLimitTip.AsFormatted,
                display,
                () =>
                {
                    int? current = AutoTerrainDesignationsMod.MaxDepthToDigTo;
                    AutoTerrainDesignationsMod.SetMaxDepthToDigTo(current == null ? -50 : current.Value + ModifierStepSize());
                    Refresh();
                },
                () =>
                {
                    int? current = AutoTerrainDesignationsMod.MaxDepthToDigTo;
                    if (current != null)
                    {
                        int next = current.Value - ModifierStepSize();
                        AutoTerrainDesignationsMod.SetMaxDepthToDigTo(next < -50 ? (int?)null : next);
                    }
                    Refresh();
                });

            return row;
        }

        private static Row BuildToggleRow(
            LocStrFormatted label,
            LocStrFormatted tooltip,
            Func<bool> getValue,
            Action<bool> setValue,
            List<Action> refreshers)
        {
            var toggle = new Toggle(standalone: true)
                .Label(label)
                .Value(getValue())
                .OnValueChanged(value => setValue(value))
                .Tooltip(tooltip);
            refreshers.Add(() => toggle.Value(getValue()));
            var row = new Row().MarginTop(1.pt()).AlignItemsCenter();
            row.Add(toggle);
            return row;
        }

        private static Row BuildStepRow(
            LocStrFormatted label,
            LocStrFormatted tooltip,
            Display display,
            Action onPlus,
            Action onMinus)
        {
            var plusBtn = new ButtonIcon(Button.General, "Assets/Unity/UserInterface/General/Plus128.png")
                .Compact().IconSize(14.px()).OnClick(onPlus, allowKeyPresses: true);
            var minusBtn = new ButtonIcon(Button.General, "Assets/Unity/UserInterface/General/Minus128.png")
                .Compact().IconSize(14.px()).OnClick(onMinus, allowKeyPresses: true);
            var row = new Row().MarginTop(1.pt()).AlignItemsCenter();
            row.Add(new Label(label).Tooltip(tooltip));
            row.Add(new UiComponent().FlexGrow(1f));
            row.Add(minusBtn);
            row.Add(display);
            row.Add(plusBtn);
            return row;
        }

        private static PanelFooterRow BuildFooter(List<Action> refreshers)
        {
            var status = new Label(L(string.Empty)).MarginTopBottom(1.pt());
            var save = new ButtonText(Button.Primary, AtdLocalization.SettingsSaveAsGlobal.AsFormatted, () =>
            {
                AutoDepthDesignation.SaveWorldPathfinderSettingsAsGlobalDefaults();
                if (AutoDepthDesignation.TrySaveSettings(out string _))
                    status.Value(AtdLocalization.SettingsSavedToFile.AsFormatted);
                else
                    status.Value(AtdLocalization.SettingsSaveFailed.AsFormatted);
            }).Tooltip(AtdLocalization.SettingsSaveAsGlobalTooltip.AsFormatted);

            var reset = new ButtonText(Button.General, AtdLocalization.SettingsRestoreDefaults.AsFormatted, () =>
            {
                AutoDepthDesignation.ResetSettingsToDefaults();
                foreach (Action refresh in refreshers)
                    refresh();
                status.Value(AtdLocalization.SettingsRestoredDefaults.AsFormatted);
            }).Tooltip(AtdLocalization.SettingsRestoreDefaultsTooltip.AsFormatted);

            return new PanelFooterRow().BodyAdd(
                row => row.Gap(2.pt()).AlignItemsCenter(),
                status,
                new UiComponent().FlexGrow(1f),
                reset,
                save);
        }

        private static int ModifierStepSize()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return 10;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return 5;
            return 1;
        }

        private static int IntStepSize(int step, int shiftStep, int ctrlStep)
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return ctrlStep;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return shiftStep;
            return step;
        }

        private static float FloatStepSize(float step = 0.05f, float shiftStep = 0.10f, float ctrlStep = 0.25f)
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return ctrlStep;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return shiftStep;
            return step;
        }

        private static string FormatNoLimitZero(int value)
        {
            return value == 0 ? "∞" : value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatDepth(int? value)
        {
            if (value == null)
                return "-∞";
            return value.Value > 0
                ? "+" + value.Value.ToString(CultureInfo.InvariantCulture)
                : value.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatOrePurityLevel(int value)
        {
            switch (value)
            {
                case 0: return "Off";
                case 1: return "Low";
                case 2: return "Med";
                case 3: return "High";
                case 4: return "Max";
                default: return value.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string FormatSafetyPolicy(SafetyPolicy policy)
        {
            return policy == SafetyPolicy.Med
                ? "BAL"
                : policy.ToString().ToUpperInvariant();
        }

        private static string FormatClearance(int value)
        {
            return value == 0 ? "Off" : value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string FormatRatio(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string LevelName(int level)
        {
            return level + " - " + FormatOrePurityLevel(level);
        }

        private static LocStrFormatted L(string text)
        {
            return new LocStrFormatted(text ?? string.Empty);
        }
    }
}

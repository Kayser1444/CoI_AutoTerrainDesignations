// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Mining Designation Panel
using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.Ui.Library;
using Row = Mafi.Unity.UiToolkit.Library.Row;
using UnityEngine;

namespace AutoTerrainDesignations
{
    /// <summary>
    /// Builds the "Mining designations" inspector panel independently of any specific
    /// inspector type. Call <see cref="Build"/> and insert the returned panel wherever
    /// needed. Can be used by external mods via <see cref="AutoTerrainDesignationsApi"/>.
    /// </summary>
    internal static class DesignationPanel
    {
        private static ProtosDb? s_protosDb;
        private static readonly string s_debrisIconPath = Mafi.Unity.Assets.Unity.UserInterface.Toolbar.Sweep_svg;

        private sealed class Bindings
        {
            public Func<IAreaManagingTower?> GetTower { get; }
            public PanelWithHeader Panel { get; }
            public Dropdown<AccessVehicleClearanceMode> AccesswayModeDropdown { get; }
            public Mafi.Unity.Ui.Library.Display MaxLayersDisplay { get; }
            public Mafi.Unity.Ui.Library.Display MinElevDisplay { get; }
            public SliderWithIncrements OrePuritySlider { get; }
            public ButtonIcon ClearBtn { get; }

            public Bindings(
                Func<IAreaManagingTower?> getTower,
                PanelWithHeader panel,
                Dropdown<AccessVehicleClearanceMode> accesswayModeDropdown,
                Mafi.Unity.Ui.Library.Display maxLayersDisplay,
                Mafi.Unity.Ui.Library.Display minElevDisplay,
                SliderWithIncrements orePuritySlider,
                ButtonIcon clearBtn)
            {
                GetTower = getTower;
                Panel = panel;
                AccesswayModeDropdown = accesswayModeDropdown;
                MaxLayersDisplay = maxLayersDisplay;
                MinElevDisplay = minElevDisplay;
                OrePuritySlider = orePuritySlider;
                ClearBtn = clearBtn;
            }
        }

        private static readonly Dictionary<object, Bindings> s_bindings =
            new Dictionary<object, Bindings>();

        internal static bool HasBindings(object key) => s_bindings.ContainsKey(key);

        internal static void Initialize(ProtosDb? protosDb)
        {
            s_protosDb = protosDb;
        }

        /// <summary>
        /// Refreshes the display values of a previously built panel.
        /// Call this when the inspector switches to a different tower.
        /// </summary>
        internal static void RefreshDisplays(object key)
        {
            if (!s_bindings.TryGetValue(key, out var b)) return;
            var tower = b.GetTower();
            if (tower == null) return;
            b.Panel.Collapsed(AutoDepthDesignation.GetTowerTerrainPanelCollapsed(tower));
            b.AccesswayModeDropdown.SetValue(AutoDepthDesignation.GetTowerVehicleClearance(tower));
            b.MaxLayersDisplay.SetValue(new LocStrFormatted(MaxLayersText(AutoDepthDesignation.GetTowerMaxLayersToExcavate(tower))));
            b.MinElevDisplay.SetValue(new LocStrFormatted(MinElevText(AutoDepthDesignation.GetTowerMaxDepthToDigTo(tower))));
            int orePurity = AutoDepthDesignation.GetTowerOrePurityLevel(tower);
            b.OrePuritySlider.Value(orePurity);
            b.OrePuritySlider.Tooltip(AtdModSettingsTab.BuildOreQualityValueTooltip(orePurity));
            bool hasGen = AutoDepthDesignation.HasGeneratedDesignationsForTower(tower);
            b.ClearBtn.Tooltip(AtdLocalization.Tip(hasGen ? AtdLocalization.DesigClearTipWithShiftClick : AtdLocalization.DesigClearTip));
        }

        /// <summary>
        /// Builds the full "Mining designations" panel and returns it. Insert the result
        /// at any position in any inspector's <c>Column</c>.
        /// </summary>
        /// <param name="getTower">
        /// Delegate that returns the currently active tower, called lazily inside button
        /// handlers and display refresh. May return null between inspector activations.
        /// </param>
        /// <param name="key">
        /// Opaque key (typically the inspector instance) used to route
        /// <see cref="RefreshDisplays"/> calls back to this panel. Pass the same key to
        /// <see cref="AutoDepthDesignation.CreateDesignationsForTower(IAreaManagingTower, object?)"/>
        /// so the Ore composition panel auto-refreshes after a scan.
        /// </param>
        internal static PanelWithHeader Build(Func<IAreaManagingTower?> getTower, object key)
        {
            var initialTower = getTower();

            // --- Ore picker ---
            SingleProductPickerUi? orePicker = null;
            if (s_protosDb != null)
            {
                try
                {
                    var minableProducts = s_protosDb.All<TerrainMaterialProto>()
                        .Select(m => (ProductProto)m.MinedProduct)
                        .Distinct()
                        .OrderBy(product => AutoDepthDesignation.GetProductPickerSortRank(product))
                        .ThenBy(product => product.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    orePicker = new SingleProductPickerUi(
                        () => minableProducts,
                        delegate(ProductProto selected)
                        {
                            var tower = getTower();
                            if (tower == null) return;
                            if (selected is LooseProductProto loose)
                            {
                                AutoDepthDesignation.SetSelectedOre(tower, loose);
                            }
                        },
                        () =>
                        {
                            var tower = getTower();
                            if (tower == null) return Option.None;
                            var sel = AutoDepthDesignation.GetSelectedOre(tower);
                            return sel != null ? Option.Some((ProductProto)sel) : Option.None;
                        },
                        delegate
                        {
                            var tower = getTower();
                            if (tower == null) return;
                            AutoDepthDesignation.SetSelectedOre(tower, null);
                        },
                        AtdLocalization.DesigOreFilterAuto,
                        compact: true,
                        primaryButtonIfNoProtoSet: false
                    );
                }



                catch (Exception ex)
                {
                    Log.Warning($"[ATD] EXCEPTION creating ore picker in DesignationPanel: {ex}");
                }
            }

            // --- Dig button ---
            var digBtn = new ButtonIconText(
                Button.Primary,
                "Assets/Unity/UserInterface/EntityIcons/Designation.png",
                AtdLocalization.DesigCreateBtn)
                .OnClick((Action)delegate
                {
                    try
                    {
                        var tower = getTower();
                        if (tower == null) return;
                        AutoDepthDesignation.CreateDesignationsForTower(tower, key);
                    }
                    catch (Exception ex) { Debug.Log($"[ATD] Dig button click EXCEPTION: {ex}"); }
                });
            digBtn.Tooltip(AtdLocalization.Tip(AtdLocalization.DesigCreateTip));
            digBtn.Icon.Size(Px.Auto, 24.px());

            var debrisBtn = new ButtonIcon(Button.General, s_debrisIconPath)
                .OnClick((Action)delegate
                {
                    try
                    {
                        var tower = getTower();
                        if (tower == null) return;
                        AutoDepthDesignation.ClearTowerLastRampOutcome(tower);
                        bool markUnreachable = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                        // The prop-removal manager now suspends and restores any
                        // existing designation, so cleanup no longer needs an
                        // override modifier to work in an already-designated cell.
                        AutoDepthDesignation.MarkDebrisForRemovalForTower(tower,
                            overrideExisting: true, markUnreachable);
                    }
                    catch (Exception ex) { Debug.Log($"[ATD] Debris button click EXCEPTION: {ex}"); }
                }, allowKeyPresses: true)
                .Tooltip(AtdLocalization.Tip(AtdLocalization.DesigDebrisTip));

            // --- Clear button ---
            bool initialHasGen = initialTower != null && AutoDepthDesignation.HasGeneratedDesignationsForTower(initialTower);
            var clearBtn = new ButtonIcon(Button.General, "Assets/Unity/UserInterface/General/Trash128.png")
                .OnClick((Action)delegate
                {
                    try
                    {
                        var tower = getTower();
                        if (tower == null) return;
                        AutoDepthDesignation.ClearTowerLastRampOutcome(tower);
                        AutoDepthDesignation.ClearDiagnosticOverlays();
                        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                            AutoDepthDesignation.ClearDesignationsForTower(tower);
                        else
                            AutoDepthDesignation.ClearGeneratedDesignationsForTower(tower);
                        RefreshDisplays(key);
                    }
                    catch (Exception ex) { Debug.Log($"[ATD] Clear button click EXCEPTION: {ex}"); }
                }, allowKeyPresses: true)
                .Tooltip(AtdLocalization.Tip(initialHasGen ? AtdLocalization.DesigClearTipWithShiftClick : AtdLocalization.DesigClearTip));

            digBtn.MarginTopBottom(1.pt());
            debrisBtn.MarginTopBottom(1.pt()).AlignSelfEnd();
            clearBtn.MarginTopBottom(1.pt()).AlignSelfEnd();

            var contentRow = new Row().Gap(3.pt()).AlignItemsEnd();
            contentRow.Add(new UiComponent().FlexGrow(1f));
            contentRow.Add(digBtn);
            contentRow.Add(new UiComponent().FlexGrow(1f));
            contentRow.Add(clearBtn);
            contentRow.Add(debrisBtn);

            var panel = new PanelWithHeader()
                .Title(AtdLocalization.DesigTitle,
                       AtdLocalization.PanelTip(AtdLocalization.DesigDescription));
            panel.Collapsed(initialTower != null
                ? AutoDepthDesignation.GetTowerTerrainPanelCollapsed(initialTower)
                : AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed);
            panel.Header.OnClick((Action)delegate
            {
                panel.Collapsed(!panel.IsCollapsed);
                var t = getTower();
                if (t != null) AutoDepthDesignation.SetTowerTerrainPanelCollapsed(t, panel.IsCollapsed);
            });
            panel.BodyAdd(contentRow);

            // --- Accessway mode ---
            AccessVehicleClearanceMode initVehicleClearance = initialTower != null
                ? AutoDepthDesignation.GetTowerVehicleClearance(initialTower)
                : AccessVehicleClearanceMode.Auto;
            var accesswayModeDropdown = new Dropdown<AccessVehicleClearanceMode>(AccesswayModeOption)
                .SetOptions(
                    AccessVehicleClearanceMode.Off,
                    AccessVehicleClearanceMode.Auto,
                    AccessVehicleClearanceMode.T1,
                    AccessVehicleClearanceMode.T2,
                    AccessVehicleClearanceMode.T3,
                    AccessVehicleClearanceMode.LegacyWidth3,
                    AccessVehicleClearanceMode.LegacyWidth4,
                    AccessVehicleClearanceMode.LegacyWidth5)
                .SetValue(initVehicleClearance)
                .OnValueChanged((mode, _) =>
                {
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerVehicleClearance(tower, mode);
                });
            accesswayModeDropdown.Width(130.px());
            var accesswayModeRow = new Row().MarginTop(1.pt());
            accesswayModeRow.Add(new Label(AtdLocalization.DesigAccesswayModeLabel));
            accesswayModeRow.Add(new UiComponent().FlexGrow(1f));
            accesswayModeRow.Add(accesswayModeDropdown);
            panel.BodyAdd(accesswayModeRow);

            // --- Max layers ---
            int initLayers = initialTower != null
                ? AutoDepthDesignation.GetTowerMaxLayersToExcavate(initialTower)
                : AutoTerrainDesignationsMod.MaxLayersToExcavate;
            var maxLayersDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(MaxLayersText(initLayers)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            panel.BodyAdd(BuildStepRow(
                AtdLocalization.DesigMaxLayersLabel,
                AtdLocalization.DesigMaxLayersTip,
                maxLayersDisplay,
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    int cur = AutoDepthDesignation.GetTowerMaxLayersToExcavate(tower);
                    int step = ModifierStepSize();
                    if (cur != 0)
                        AutoDepthDesignation.SetTowerMaxLayersToExcavate(tower, cur + step > 50 ? 0 : cur + step);
                    maxLayersDisplay.SetValue(new LocStrFormatted(MaxLayersText(AutoDepthDesignation.GetTowerMaxLayersToExcavate(tower))));
                },
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    int cur = AutoDepthDesignation.GetTowerMaxLayersToExcavate(tower);
                    AutoDepthDesignation.SetTowerMaxLayersToExcavate(tower, cur == 0 ? 50 : Math.Max(1, cur - ModifierStepSize()));
                    maxLayersDisplay.SetValue(new LocStrFormatted(MaxLayersText(AutoDepthDesignation.GetTowerMaxLayersToExcavate(tower))));
                }));

            // --- Elevation limit ---
            int? initElev = initialTower != null
                ? AutoDepthDesignation.GetTowerMaxDepthToDigTo(initialTower)
                : AutoTerrainDesignationsMod.MaxDepthToDigTo;
            var minElevDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(MinElevText(initElev)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            panel.BodyAdd(BuildStepRow(
                AtdLocalization.DesigElevLimitLabel,
                AtdLocalization.DesigElevLimitTip,
                minElevDisplay,
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    int? cur = AutoDepthDesignation.GetTowerMaxDepthToDigTo(tower);
                    AutoDepthDesignation.SetTowerMaxDepthToDigTo(tower, cur == null ? -50 : cur.Value + ModifierStepSize());
                    minElevDisplay.SetValue(new LocStrFormatted(MinElevText(AutoDepthDesignation.GetTowerMaxDepthToDigTo(tower))));
                },
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    int? cur = AutoDepthDesignation.GetTowerMaxDepthToDigTo(tower);
                    if (cur != null)
                    {
                        int next = cur.Value - ModifierStepSize();
                        AutoDepthDesignation.SetTowerMaxDepthToDigTo(tower, next < -50 ? (int?)null : next);
                    }
                    minElevDisplay.SetValue(new LocStrFormatted(MinElevText(AutoDepthDesignation.GetTowerMaxDepthToDigTo(tower))));
                }));

            // --- Ore quality ---
            int initPurity = initialTower != null
                ? AutoDepthDesignation.GetTowerOrePurityLevel(initialTower)
                : AutoTerrainDesignationsMod.OrePurityLevel;
            var orePuritySlider = new SliderWithIncrements();
            void UpdateOrePurityTooltip(int value) =>
                orePuritySlider.Tooltip(AtdModSettingsTab.BuildOreQualityValueTooltip(value));
            orePuritySlider
                .Range(0, 4)
                .Value(initPurity)
                .OnValueChangedForPreview(value =>
                    UpdateOrePurityTooltip(value))
                .OnValueChanged(value =>
                {
                    UpdateOrePurityTooltip(value);
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerOrePurityLevel(tower, value);
                });
            orePuritySlider.FlexGrow(0f).Width(140.px());
            UpdateOrePurityTooltip(initPurity);
            var orePurityRow = new Row().MarginTop(1.pt()).AlignItemsCenter().Gap(1.pt());
            orePurityRow.Add(new Label(AtdLocalization.DesigOrePurityLabel)
                .Tooltip(AtdLocalization.DesigOrePurityTip));
            orePurityRow.Add(new UiComponent().FlexGrow(1f));
            orePurityRow.Add(orePuritySlider);
            panel.BodyAdd(orePurityRow);

            // --- Ore picker row ---
            if (orePicker != null)
            {
                var oreRow = new Row().MarginTop(1.pt());
                oreRow.Add(new Label(AtdLocalization.DesigScanningFilterLabel)
                    .Tooltip(AtdLocalization.DesigScanningFilterTip));
                oreRow.Add(new UiComponent().FlexGrow(1f));
                oreRow.Add(orePicker);
                panel.BodyAdd(oreRow);
            }

            s_bindings[key] = new Bindings(getTower, panel, accesswayModeDropdown,
                maxLayersDisplay, minElevDisplay, orePuritySlider, clearBtn);
            return panel;
        }

        private static Row BuildStepRow(
            LocStrFormatted label,
            LocStrFormatted tooltip,
            Mafi.Unity.Ui.Library.Display display,
            Action onPlus,
            Action onMinus)
        {
            var plusBtn = new ButtonIcon(Button.General, "Assets/Unity/UserInterface/General/Plus128.png")
                .Compact().IconSize(14.px()).OnClick(onPlus, allowKeyPresses: true);
            var minusBtn = new ButtonIcon(Button.General, "Assets/Unity/UserInterface/General/Minus128.png")
                .Compact().IconSize(14.px()).OnClick(onMinus, allowKeyPresses: true);
            var row = new Row().MarginTop(1.pt());
            row.Add(new Label(label).Tooltip(tooltip));
            row.Add(new UiComponent().FlexGrow(1f));
            row.Add(minusBtn);
            row.Add(display);
            row.Add(plusBtn);
            return row;
        }

        private static int ModifierStepSize()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return 10;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return 5;
            return 1;
        }

        internal static UiComponent AccesswayModeOption(
            AccessVehicleClearanceMode mode,
            int index,
            bool isInDropdown) => new Label(AccesswayModeText(mode))
                .Tooltip(AccesswayModeTooltip(mode));

        internal static LocStrFormatted AccesswayModeText(AccessVehicleClearanceMode mode)
        {
            switch (mode)
            {
                case AccessVehicleClearanceMode.Off: return new LocStrFormatted("OFF");
                case AccessVehicleClearanceMode.Auto: return new LocStrFormatted("AUTO");
                case AccessVehicleClearanceMode.T1: return new LocStrFormatted("T1");
                case AccessVehicleClearanceMode.T2: return new LocStrFormatted("T2");
                case AccessVehicleClearanceMode.T3: return new LocStrFormatted("T3");
                case AccessVehicleClearanceMode.LegacyWidth3: return AtdLocalization.AccesswayModeLegacy3.AsFormatted;
                case AccessVehicleClearanceMode.LegacyWidth4: return AtdLocalization.AccesswayModeLegacy4.AsFormatted;
                case AccessVehicleClearanceMode.LegacyWidth5: return AtdLocalization.AccesswayModeLegacy5.AsFormatted;
                default: return new LocStrFormatted(mode.ToString());
            }
        }

        internal static LocStrFormatted AccesswayModeTooltip(AccessVehicleClearanceMode mode)
        {
            switch (mode)
            {
                case AccessVehicleClearanceMode.Off: return AtdLocalization.AccesswayModeOffTip.AsFormatted;
                case AccessVehicleClearanceMode.Auto: return AtdLocalization.AccesswayModeAutoTip.AsFormatted;
                case AccessVehicleClearanceMode.T1: return AtdLocalization.AccesswayModeT1Tip.AsFormatted;
                case AccessVehicleClearanceMode.T2: return AtdLocalization.AccesswayModeT2Tip.AsFormatted;
                case AccessVehicleClearanceMode.T3: return AtdLocalization.AccesswayModeT3Tip.AsFormatted;
                case AccessVehicleClearanceMode.LegacyWidth3: return AtdLocalization.AccesswayModeLegacy3Tip.AsFormatted;
                case AccessVehicleClearanceMode.LegacyWidth4: return AtdLocalization.AccesswayModeLegacy4Tip.AsFormatted;
                case AccessVehicleClearanceMode.LegacyWidth5: return AtdLocalization.AccesswayModeLegacy5Tip.AsFormatted;
                default: return LocStrFormatted.Empty;
            }
        }

        private static string MaxLayersText(int value) => value == 0 ? "\u221e" : value.ToString();

        private static string MinElevText(int? value)
        {
            if (value == null) return "-\u221e";
            return value.Value > 0 ? "+" + value.Value : value.Value.ToString();
        }

    }
}

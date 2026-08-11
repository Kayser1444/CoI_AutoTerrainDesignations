// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farming Preparation Analysis Panel
using System;
using System.Reflection;
using Mafi;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Vehicles.Excavators;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Localization;
using Mafi.Unity.Ui;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.UiToolkit.Library.FloatingPanel;

namespace AutoTerrainDesignations
{
    internal static class FarmingAnalysisPanel
    {
        private static readonly System.Collections.Generic.Dictionary<object, Action> s_resetContentCallbacks =
            new System.Collections.Generic.Dictionary<object, Action>();
        private static readonly System.Collections.Generic.Dictionary<object, Action> s_idleVehicleTooltipRefreshCallbacks =
            new System.Collections.Generic.Dictionary<object, Action>();

        private sealed class IdleVehicleReleaseTooltip
        {
            private readonly Func<IAreaManagingTower?> m_towerProvider;
            private readonly Func<IAreaManagingTower?, bool> m_showReleaseSection;
            private readonly Func<IAreaManagingTower?, Vehicle[]> m_releasedVehicleProvider;
            private readonly UiContext? m_context;
            private readonly LocStr m_baseTooltip;
            private readonly LocStr m_releaseLabel;
            private readonly Column m_content = new Column(2.pt());
            private Vehicle[] m_releasedVehicles = Array.Empty<Vehicle>();
            private bool m_lastShowReleaseSection;
            private bool m_hasSnapshot;
            private int m_nextVehicleIndex;

            internal IdleVehicleReleaseTooltip(
                Func<IAreaManagingTower?> towerProvider,
                Func<IAreaManagingTower?, bool> showReleaseSection,
                Func<IAreaManagingTower?, Vehicle[]> releasedVehicleProvider,
                UiContext? context,
                LocStr baseTooltip,
                LocStr releaseLabel)
            {
                m_towerProvider = towerProvider;
                m_showReleaseSection = showReleaseSection;
                m_releasedVehicleProvider = releasedVehicleProvider;
                m_context = context;
                m_baseTooltip = baseTooltip;
                m_releaseLabel = releaseLabel;
            }

            internal Option<UiComponent> GetContent()
            {
                Refresh();
                return m_content;
            }

            internal void Refresh()
            {
                IAreaManagingTower? tower = m_towerProvider();
                bool showReleaseSection = m_showReleaseSection(tower);
                Vehicle[] releasedVehicles = showReleaseSection
                    ? m_releasedVehicleProvider(tower)
                    : Array.Empty<Vehicle>();

                if (m_hasSnapshot
                    && showReleaseSection == m_lastShowReleaseSection
                    && SameVehicleSnapshot(releasedVehicles))
                    return;

                m_hasSnapshot = true;
                m_lastShowReleaseSection = showReleaseSection;
                m_releasedVehicles = releasedVehicles;
                m_nextVehicleIndex = 0;
                m_content.Clear();
                m_content.Add(new Label(m_baseTooltip.AsFormatted).IncFontSize());

                if (!showReleaseSection)
                    return;

                if (m_releasedVehicles.Length == 0)
                {
                    m_content.Add(new Label(
                        (m_releaseLabel.TranslatedString
                        + AtdLocalization.FarmingVehicleStatusNone.TranslatedString).AsLoc()));
                    return;
                }

                m_content.Add(new Label(m_releaseLabel.AsFormatted));
                var card = new Row(1.pt()).AlignItemsCenter();
                var vehicleIcon = new VehicleIcon()
                    .Value(m_releasedVehicles[0].Prototype.SomeOption())
                    .SizeProtoLarge();
                vehicleIcon.AttachClickToGoToIcon(
                    () => OnVehicleCardClicked(vehicleIcon),
                    out Icon _);
                card.Add(vehicleIcon, new Display().Value(m_releasedVehicles.Length).MinDigits(2));
                m_content.Add(card);
            }

            private void OnVehicleCardClicked(VehicleIcon vehicleIcon)
            {
                if (m_releasedVehicles.Length == 0)
                    return;

                int index = m_nextVehicleIndex % m_releasedVehicles.Length;
                Vehicle vehicle = m_releasedVehicles[index];
                if (vehicle != null && !vehicle.IsDestroyed)
                {
                    m_context?.CameraController.PanTo(vehicle.Position2f);
                    vehicleIcon.Value(vehicle.Prototype.SomeOption()).SizeProtoLarge();
                }
                m_nextVehicleIndex = index + 1;
            }

            private bool SameVehicleSnapshot(Vehicle[] vehicles)
            {
                if (vehicles.Length != m_releasedVehicles.Length)
                    return false;

                for (int i = 0; i < vehicles.Length; i++)
                {
                    if (vehicles[i].Id != m_releasedVehicles[i].Id)
                        return false;
                }

                return true;
            }
        }

        internal static void ResetContent(object inspectorInstance)
        {
            if (s_resetContentCallbacks.TryGetValue(inspectorInstance, out Action cb))
            {
                try { cb(); } catch { }
            }
        }

        internal static void RefreshIdleVehicleTooltips()
        {
            foreach (Action cb in s_idleVehicleTooltipRefreshCallbacks.Values)
            {
                try { cb(); } catch { }
            }
        }

        internal static void Inject(Column mainBody, PropertyInfo entityProp, object inspector)
        {
            try
            {
                AutoDepthDesignation.EnsureFarmingAutomationDefaultEnabledForTower(
                    entityProp.GetValue(inspector) as IAreaManagingTower);
                var contentCol = new Column(2.pt());
                var initialTower = entityProp.GetValue(inspector) as IAreaManagingTower;
                var inspectorContext = GetInspectorContext(inspector);
                var idleReleaseExcavatorsTooltip = new IdleVehicleReleaseTooltip(
                    () => entityProp.GetValue(inspector) as IAreaManagingTower,
                    tower => tower == null
                        ? AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle
                        : AutoDepthDesignation.GetTowerAutoReleaseExcavatorsWhenIdle(tower),
                    AutoDepthDesignation.GetSoftReleasedExcavators,
                    inspectorContext,
                    AtdLocalization.FarmingIdleReleaseExcavatorsTip,
                    AtdLocalization.FarmingSoftReleasedExcavatorsLabel);
                var idleTruckPolicyTooltip = new IdleVehicleReleaseTooltip(
                    () => entityProp.GetValue(inspector) as IAreaManagingTower,
                    tower => (tower == null
                        ? AutoTerrainDesignationsMod.TruckIdlePolicy
                        : AutoDepthDesignation.GetTowerTruckIdlePolicy(tower)) == TruckIdleBehavior.SoftRelease,
                    AutoDepthDesignation.GetSoftReleasedTrucks,
                    inspectorContext,
                    AtdLocalization.FarmingTruckIdlePolicyTip,
                    AtdLocalization.FarmingSoftReleasedTrucksLabel);
                Action refreshIdleVehicleTooltips = delegate
                {
                    idleReleaseExcavatorsTooltip.Refresh();
                    idleTruckPolicyTooltip.Refresh();
                };
                s_idleVehicleTooltipRefreshCallbacks[inspector] = refreshIdleVehicleTooltips;
                var automationToggle = new Toggle(standalone: true)
                    .Label(AtdLocalization.FarmingToggleLabel)
                    .ObserveValue(() =>
                    {
                        var tower = entityProp.GetValue(inspector) as IAreaManagingTower;
                        return AutoDepthDesignation.IsFarmingAutomationEnabledForTower(tower);
                    })
                    .OnValueChanged((Action<bool>)delegate(bool isOn)
                    {
                        var tower = entityProp.GetValue(inspector) as IAreaManagingTower;
                        AutoDepthDesignation.SetFarmingAutomationEnabledForTower(tower, isOn);
                    })
                    .Tooltip(AtdLocalization.FarmingToggleTip);

                contentCol.Add(automationToggle);

                var idleReleaseExcavatorsToggle = new Toggle(standalone: true)
                    .Label(AtdLocalization.FarmingIdleReleaseExcavatorsLabel)
                    .ObserveValue(() =>
                    {
                        var tower = entityProp.GetValue(inspector) as IAreaManagingTower;
                        if (tower == null) return AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle;
                        return AutoDepthDesignation.GetTowerAutoReleaseExcavatorsWhenIdle(tower);
                    })
                    .OnValueChanged((Action<bool>)delegate(bool isOn)
                    {
                        var tower = entityProp.GetValue(inspector) as IAreaManagingTower;
                        if (tower == null) return;
                        AutoDepthDesignation.SetTowerAutoReleaseExcavatorsWhenIdle(tower, isOn);
                        refreshIdleVehicleTooltips();
                    })
                    .FloaterInteractive(idleReleaseExcavatorsTooltip.GetContent);

                contentCol.Add(idleReleaseExcavatorsToggle);

                var idleTruckPolicyLabel = new Label(AtdLocalization.FarmingTruckIdlePolicyLabel.AsFormatted)
                    .InfoIconPosition(Label.InfoIconPos.Right)
                    .FloaterInteractive(idleTruckPolicyTooltip.GetContent);
                var idleTruckPolicyDropdown = new Dropdown<TruckIdleBehavior>(TruckIdlePolicyUi.Option)
                    .SetOptions(
                        TruckIdleBehavior.ParkAtTower,
                        TruckIdleBehavior.StayPut,
                        TruckIdleBehavior.SoftRelease)
                    .SetValue(initialTower != null
                        ? AutoDepthDesignation.GetTowerTruckIdlePolicy(initialTower)
                        : AutoTerrainDesignationsMod.TruckIdlePolicy)
                    .OnValueChanged((policy, _) =>
                    {
                        var tower = entityProp.GetValue(inspector) as IAreaManagingTower;
                        if (tower != null)
                            AutoDepthDesignation.SetTowerTruckIdlePolicy(tower, policy);
                        refreshIdleVehicleTooltips();
                    });
                idleTruckPolicyDropdown.Width(130.px());
                var idleTruckPolicyRow = new Row()
                    .AlignSelfStretch()
                    .AlignItemsCenter();
                idleTruckPolicyRow.Add(idleTruckPolicyLabel);
                idleTruckPolicyRow.Add(new UiComponent().FlexGrow(1f));
                idleTruckPolicyRow.Add(idleTruckPolicyDropdown);
                contentCol.Add(idleTruckPolicyRow);

                var farmingInitTower = entityProp.GetValue(inspector) as IAreaManagingTower;
                var panel = new PanelWithHeader()
                    .Title(
                        AtdLocalization.FarmingTitle,
                        AtdLocalization.PanelTip(AtdLocalization.FarmingDescription));
                panel.Collapsed(farmingInitTower != null
                    ? AutoDepthDesignation.GetTowerFarmingPanelCollapsed(farmingInitTower)
                    : AutoTerrainDesignationsMod.FarmingPanelCollapsed);
                panel.Header.OnClick((Action)delegate
                {
                    panel.Collapsed(!panel.IsCollapsed);
                    var t = entityProp.GetValue(inspector) as IAreaManagingTower;
                    if (t != null) AutoDepthDesignation.SetTowerFarmingPanelCollapsed(t, panel.IsCollapsed);
                });

                s_resetContentCallbacks[inspector] = (Action)delegate
                {
                    var tower = entityProp.GetValue(inspector) as IAreaManagingTower;
                    AutoDepthDesignation.EnsureFarmingAutomationDefaultEnabledForTower(tower);
                    automationToggle.Value(AutoDepthDesignation.IsFarmingAutomationEnabledForTower(tower));
                    idleReleaseExcavatorsToggle.Value(tower == null
                        ? AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle
                        : AutoDepthDesignation.GetTowerAutoReleaseExcavatorsWhenIdle(tower));
                    idleTruckPolicyDropdown.SetValue(tower == null
                        ? AutoTerrainDesignationsMod.TruckIdlePolicy
                        : AutoDepthDesignation.GetTowerTruckIdlePolicy(tower));
                    refreshIdleVehicleTooltips();
                    if (tower != null)
                        panel.Collapsed(AutoDepthDesignation.GetTowerFarmingPanelCollapsed(tower));
                };

                panel.BodyAdd(contentCol);
                mainBody.InsertAt(2, panel);
            }
            catch (Exception ex)
            {
                Log.Warning($"[ATD] FarmingAnalysisPanel.Inject EXCEPTION: {ex}");
            }
        }

        private static UiContext? GetInspectorContext(object inspector)
        {
            try
            {
                PropertyInfo? contextProp = inspector.GetType().GetProperty(
                    "Context",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return contextProp?.GetValue(inspector) as UiContext;
            }
            catch
            {
                return null;
            }
        }

    }
}

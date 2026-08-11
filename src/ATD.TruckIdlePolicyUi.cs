using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;

namespace AutoTerrainDesignations;

internal static class TruckIdlePolicyUi
{
    internal static UiComponent Option(
        TruckIdleBehavior policy,
        int index,
        bool isInDropdown)
    {
        switch (policy)
        {
            case TruckIdleBehavior.ParkAtTower:
                return new Label(AtdLocalization.FarmingTruckIdlePolicyParkAtTower.AsFormatted)
                    .Tooltip(AtdLocalization.FarmingTruckIdlePolicyParkAtTowerTip.AsFormatted);
            case TruckIdleBehavior.SoftRelease:
                return new Label(AtdLocalization.FarmingTruckIdlePolicySoftRelease.AsFormatted)
                    .Tooltip(AtdLocalization.FarmingTruckIdlePolicySoftReleaseTip.AsFormatted);
            default:
                return new Label(AtdLocalization.FarmingTruckIdlePolicyStayPut.AsFormatted)
                    .Tooltip(AtdLocalization.FarmingTruckIdlePolicyStayPutTip.AsFormatted);
        }
    }
}

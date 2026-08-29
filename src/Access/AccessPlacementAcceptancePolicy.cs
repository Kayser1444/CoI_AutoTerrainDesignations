namespace AutoTerrainDesignations.Access
{
    /// <summary>
    /// Keeps authoritative post-placement validation ahead of the provisional
    /// reachability state produced with the candidate provider present.
    /// </summary>
    internal static class AccessPlacementAcceptancePolicy
    {
        internal static bool ShouldCommit(
            bool requiresV2Validation,
            bool v2ProviderValid,
            bool overrideProviderValid,
            AccessClusterState evaluatedState)
        {
            bool reachable =
                evaluatedState == AccessClusterState.AccessibleViaProvider
                || evaluatedState == AccessClusterState.AccessibleDirect;
            bool authoritativeValidationPassed = !requiresV2Validation
                || v2ProviderValid
                || overrideProviderValid;
            return reachable && authoritativeValidationPassed;
        }

        internal static bool AllowsSmoothLevelingReclassification(
            AccessHandoffOperation expectedOperation)
        {
            return expectedOperation == AccessHandoffOperation.Leveling;
        }

        internal static bool RequiresIndependentLaneCornerCrest(
            bool isBoundedTerminal,
            bool isGroundToV,
            bool recordedLaneRequiresCrest)
        {
            return !isBoundedTerminal
                && !isGroundToV
                && recordedLaneRequiresCrest;
        }
    }

    internal static class AccessPlacementAcceptancePolicyFixtures
    {
        internal static bool ValidateAll(out string failure)
        {
            if (AccessPlacementAcceptancePolicy.ShouldCommit(
                    requiresV2Validation: true,
                    v2ProviderValid: false,
                    overrideProviderValid: false,
                    AccessClusterState.AccessibleViaProvider))
            {
                failure =
                    "A failed authoritative V2 validation must not be accepted from provisional provider reachability.";
                return false;
            }
            if (!AccessPlacementAcceptancePolicy.ShouldCommit(
                    requiresV2Validation: true,
                    v2ProviderValid: true,
                    overrideProviderValid: false,
                    AccessClusterState.AccessibleViaProvider)
                || !AccessPlacementAcceptancePolicy.ShouldCommit(
                    requiresV2Validation: false,
                    v2ProviderValid: false,
                    overrideProviderValid: false,
                    AccessClusterState.AccessibleViaProvider)
                || AccessPlacementAcceptancePolicy.ShouldCommit(
                    requiresV2Validation: true,
                    v2ProviderValid: true,
                    overrideProviderValid: false,
                    AccessClusterState.NeedsAccessway))
            {
                failure =
                    "Placement acceptance policy rejected a valid provider or accepted an unreachable one.";
                return false;
            }
            if (AccessPlacementAcceptancePolicy
                    .AllowsSmoothLevelingReclassification(
                        AccessHandoffOperation.Mining)
                || AccessPlacementAcceptancePolicy
                    .AllowsSmoothLevelingReclassification(
                        AccessHandoffOperation.Dumping)
                || !AccessPlacementAcceptancePolicy
                    .AllowsSmoothLevelingReclassification(
                        AccessHandoffOperation.Leveling))
            {
                failure =
                    "Live validation must not reclassify a recorded mining or dumping crest as quick leveling.";
                return false;
            }
            if (AccessPlacementAcceptancePolicy
                    .RequiresIndependentLaneCornerCrest(
                        isBoundedTerminal: true,
                        isGroundToV: false,
                        recordedLaneRequiresCrest: true)
                || !AccessPlacementAcceptancePolicy
                    .RequiresIndependentLaneCornerCrest(
                        isBoundedTerminal: false,
                        isGroundToV: false,
                        recordedLaneRequiresCrest: true)
                || AccessPlacementAcceptancePolicy
                    .RequiresIndependentLaneCornerCrest(
                        isBoundedTerminal: false,
                        isGroundToV: true,
                        recordedLaneRequiresCrest: true))
            {
                failure =
                    "A bounded terminal's coherent operation must not be re-derived independently per lane.";
                return false;
            }

            failure = string.Empty;
            return true;
        }
    }
}

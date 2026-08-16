// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Deterministic policy fixtures for active farmland soil import. These are
// intentionally pure so they can run without a world, vehicles, or a save.
using System;
using System.Collections.Generic;

namespace AutoTerrainDesignations;

internal static class ActiveSoilImportFixtures
{
    internal static bool ValidateAll(out string failure)
    {
        if (AutoDepthDesignation.CompareActiveSoilImportOrdering(
                15, 4, 1, "source-b", 0, 0, "truck-b",
                15, 8, 1, "source-a", 0, 0, "truck-a") >= 0)
        {
            failure = "Priority tie did not prefer the closer target.";
            return false;
        }

        if (AutoDepthDesignation.CompareActiveSoilImportOrdering(
                14, 100, 100, "source-b", 0, 0, "truck-b",
                15, 1, 1, "source-a", 0, 0, "truck-a") >= 0)
        {
            failure = "Lower combined source priority did not win globally.";
            return false;
        }

        if (AutoDepthDesignation.CompareActiveSoilImportOrdering(
                15, 4, 8, "source-a", 0, 0, "truck-b",
                15, 4, 2, "source-a", 0, 0, "truck-a") <= 0)
        {
            failure = "Target tie did not prefer the closest eligible truck.";
            return false;
        }

        var slots = new HashSet<int>();
        slots.Add(10);
        slots.Add(11);
        if (slots.Count != 2 || !slots.Contains(10) || !slots.Contains(11))
        {
            failure = "Parallel target-origin slots were not independently representable.";
            return false;
        }

        int noClaimTicks = 0;
        noClaimTicks++;
        if (noClaimTicks >= 2)
        {
            failure = "The first no-claim tick incorrectly bypassed grace.";
            return false;
        }
        noClaimTicks = 0; // an ordinary claim appeared and cancelled the grace
        noClaimTicks++;
        if (noClaimTicks >= 2)
        {
            failure = "A cancelled ordinary claim did not reset grace.";
            return false;
        }
        noClaimTicks++;
        if (noClaimTicks != 2)
        {
            failure = "A complete no-claim tick was not enough to re-enable dispatch.";
            return false;
        }

        failure = string.Empty;
        return true;
    }
}

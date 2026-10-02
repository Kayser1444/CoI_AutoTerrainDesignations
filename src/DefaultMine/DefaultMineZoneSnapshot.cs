// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
using System.Collections.Generic;
using System.Linq;

namespace AutoTerrainDesignations
{
    internal static class DefaultMineZoneSnapshot
    {
        // Resolve identities against live masks: deleted bits must never grant
        // permission to a newly-created zone that reuses the same bit.
        internal static ulong Resolve(IEnumerable<int> capturedIds,
            IEnumerable<KeyValuePair<int, ulong>> liveZones, ulong defaultMask)
        {
            var identities = new HashSet<int>(capturedIds);
            ulong mask = liveZones.Where(zone => identities.Contains(zone.Key))
                .Aggregate(0UL, (current, zone) => current | zone.Value);
            return mask == 0 ? defaultMask : mask;
        }
    }
}

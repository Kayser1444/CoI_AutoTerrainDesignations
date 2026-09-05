using System;
using Mafi;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations.Access
{
    internal static class AccessPropTerrainRemovalFixtures
    {
        internal static bool Validate(out string failure)
        {
            var prop = new AccessPropSample(new Tile2i(1074, 982), false, true, true,
                dumpBurialProbeTile: new Tile2i(1074, 982),
                dumpBurialProbeOffsetX: 0.057f, dumpBurialProbeOffsetY: 0.167f,
                placedHeight: 46.17676f, dumpBurialThreshold: 0.3671875f);
            var work = new[] { new AccessPropPlannedTerrainWork(
                new DesignationData(new Tile2i(1068, 980), new HeightTilesI(42)),
                AccessHandoffOperation.Mining) };
            var cut = new AccessProjectedTerrainEffect
                { HasCutWork = true, CutCeiling = 42.53333f, HasCutSafety = true };
            if (AccessPropTerrainRemoval.TryGetRemovalStrategy(
                    QuickRemoveDebrisPolicy.Always, prop, -2f, work, _ => cut, out _))
            {
                failure = "Adjacent prop still requests Always cleanup despite projected undermining beyond the internal margin";
                return false;
            }

            var empty = Array.Empty<AccessPropPlannedTerrainWork>();
            var origin = new Tile2i(8, 8);
            var probe = new AccessPropSample(origin, false, true, true,
                dumpBurialProbeTile: origin, dumpBurialProbeOffsetX: 0.25f,
                dumpBurialProbeOffsetY: 0.75f, placedHeight: 10f,
                dumpBurialThreshold: 0.4f);
            bool Needs(AccessProjectedTerrainEffect effect, float offset = 0f)
                => AccessPropTerrainRemoval.TryGetRemovalStrategy(
                    QuickRemoveDebrisPolicy.Always, probe, offset, empty,
                    _ => effect, out _);
            AccessProjectedTerrainEffect Cut(float height)
                => new AccessProjectedTerrainEffect { HasCutWork = true, CutCeiling = height };
            AccessProjectedTerrainEffect Fill(float height)
                => new AccessProjectedTerrainEffect { HasFillWork = true, FillFloor = height };

            if (!Needs(Cut(9.5f)) || Needs(Cut(9.49f))
                || !Needs(Fill(10.9f)) || Needs(Fill(10.91f))
                || !Needs(Cut(8f), -2f) || Needs(Cut(7.49f), -2f)
                || !Needs(Cut(9.6845f)))
            {
                failure = "Prop removal must require the strict internal margin beyond the live cut/burial threshold";
                return false;
            }
            if (!Needs(default)
                || !Needs(new AccessProjectedTerrainEffect { HasCutSafety = true })
                || !Needs(new AccessProjectedTerrainEffect { HasFillSafety = true })
                || !Needs(new AccessProjectedTerrainEffect
                    { HasCutWork = true, CutCeiling = 2f, HasFillWork = true, FillFloor = 20f })
                || !Needs(Cut(float.NaN)) || !Needs(Fill(float.PositiveInfinity))
                || !Needs(Cut(2f), float.NaN)
                || !AccessPropTerrainRemoval.TryGetRemovalStrategy(
                    QuickRemoveDebrisPolicy.Always, probe, 0f, empty,
                    tile => tile == origin ? default : Cut(2f), out _)
                || !AccessPropTerrainRemoval.TryGetRemovalStrategy(
                    QuickRemoveDebrisPolicy.Always, probe, 0f, empty,
                    tile => tile == origin ? Fill(20f) : Cut(2f), out _))
            {
                failure = "Unknown, mixed-direction, safety-only and non-finite prop projections must retain removal";
                return false;
            }
            foreach (QuickRemoveDebrisPolicy policy in new[] {
                QuickRemoveDebrisPolicy.Always, QuickRemoveDebrisPolicy.Restrictive,
                QuickRemoveDebrisPolicy.Never })
            {
                if (AccessPropTerrainRemoval.TryGetRemovalStrategy(
                        policy, probe, 0f, empty, _ => Cut(2f), out bool skippedQuick)
                    || skippedQuick
                    || !AccessPropTerrainRemoval.TryGetRemovalStrategy(
                        policy, probe, 0f, empty, _ => default, out bool quick)
                    || quick != (policy != QuickRemoveDebrisPolicy.Never))
                {
                    failure = "Terrain destruction must override every removal mode without changing the method for surviving props";
                    return false;
                }
            }
            foreach (AccessHandoffOperation operation in new[] {
                AccessHandoffOperation.Mining, AccessHandoffOperation.Dumping,
                AccessHandoffOperation.Leveling })
            {
                int target = operation == AccessHandoffOperation.Dumping ? 12 : 8;
                var direct = new[] { new AccessPropPlannedTerrainWork(
                    new DesignationData(origin, new HeightTilesI(target)), operation) };
                if (AccessPropTerrainRemoval.TryGetRemovalStrategy(
                        QuickRemoveDebrisPolicy.Always, probe, 0f, direct, _ => default, out _))
                {
                    failure = "Explicit terrain destruction must also take precedence over Always";
                    return false;
                }
            }
            var noChange = new[] { new AccessPropPlannedTerrainWork(
                new DesignationData(origin, new HeightTilesI(10)), AccessHandoffOperation.Leveling) };
            var unknown = new[] { new AccessPropPlannedTerrainWork(
                new DesignationData(origin, new HeightTilesI(2)), AccessHandoffOperation.None) };
            var supportedCorner = new[] { new AccessPropPlannedTerrainWork(
                new DesignationData(new Tile2i(4, 8), new HeightTilesI(10)),
                AccessHandoffOperation.Leveling) };
            var levelingFill = new[] { new AccessPropPlannedTerrainWork(
                new DesignationData(origin, new HeightTilesI(12)), AccessHandoffOperation.Leveling) };
            var noProbe = new AccessPropSample(origin, false, true, true);
            if (!AccessPropTerrainRemoval.TryGetRemovalStrategy(
                    QuickRemoveDebrisPolicy.Always, probe, 0f, noChange, _ => Cut(2f), out _)
                || !AccessPropTerrainRemoval.TryGetRemovalStrategy(
                    QuickRemoveDebrisPolicy.Always, probe, 0f, unknown, _ => Cut(2f), out _)
                || !AccessPropTerrainRemoval.TryGetRemovalStrategy(
                    QuickRemoveDebrisPolicy.Always, probe, 0f, supportedCorner, _ => Cut(2f), out _)
                || AccessPropTerrainRemoval.TryGetRemovalStrategy(
                    QuickRemoveDebrisPolicy.Always, probe, 0f, levelingFill, _ => default, out _)
                || !AccessPropTerrainRemoval.TryGetRemovalStrategy(
                    QuickRemoveDebrisPolicy.Always, noProbe, 0f, empty, _ => Cut(2f), out _))
            {
                failure = "Direct work takes precedence over rays; unknown operations and absent probes must retain cleanup";
                return false;
            }
            failure = string.Empty;
            return true;
        }
    }
}

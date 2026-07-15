using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal readonly struct AccessV2SyntheticValidation
    {
        public bool IsValid { get; }
        public string Reason { get; }

        public AccessV2SyntheticValidation(bool isValid, string reason)
        {
            IsValid = isValid;
            Reason = reason ?? string.Empty;
        }

        public static AccessV2SyntheticValidation Valid
            => new AccessV2SyntheticValidation(true, string.Empty);
    }

    internal sealed class AccessV2StartFrontage
    {
        public AccessV2BandState State { get; }
        public Tile2i FixedSeedOrigin { get; }
        public Tile2i? SyntheticCompanionOrigin { get; }

        public bool HasSyntheticCompanion => SyntheticCompanionOrigin.HasValue;

        public AccessV2StartFrontage(
            AccessV2BandState state,
            Tile2i fixedSeedOrigin,
            Tile2i? syntheticCompanionOrigin)
        {
            State = state;
            FixedSeedOrigin = fixedSeedOrigin;
            SyntheticCompanionOrigin = syntheticCompanionOrigin;
        }
    }

    internal sealed class AccessV2FixedFrontage
    {
        public AccessV2BandState State { get; }
        public Tile2i ExposedDirection { get; }
        public float TerminalCost { get; }

        public AccessV2FixedFrontage(
            AccessV2BandState state,
            Tile2i exposedDirection,
            float terminalCost = 0f)
        {
            State = state;
            ExposedDirection = exposedDirection;
            TerminalCost = Math.Max(0f, terminalCost);
        }
    }

    internal sealed class AccessV2FrontageDiagnostics
    {
        private readonly Dictionary<string, int> m_rejections =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public int SeedCount { get; internal set; }
        public int SyntheticStartCount { get; internal set; }
        public int ExistingPairStartCount { get; internal set; }
        public int FixedGoalOriginCount { get; internal set; }
        public int FixedFrontageCount { get; internal set; }
        public IReadOnlyDictionary<string, int> Rejections => m_rejections;

        internal void Reject(string reason)
        {
            string key = string.IsNullOrEmpty(reason) ? "Unknown" : reason;
            m_rejections.TryGetValue(key, out int count);
            m_rejections[key] = count + 1;
        }
    }

    internal sealed class AccessV2EndpointSet
    {
        public IReadOnlyList<AccessV2StartFrontage> Starts { get; }
        public IReadOnlyList<AccessV2FixedFrontage> FixedGoals { get; }
        public AccessV2FrontageDiagnostics Diagnostics { get; }

        public AccessV2EndpointSet(
            IReadOnlyList<AccessV2StartFrontage> starts,
            IReadOnlyList<AccessV2FixedFrontage> fixedGoals,
            AccessV2FrontageDiagnostics diagnostics)
        {
            Starts = starts;
            FixedGoals = fixedGoals;
            Diagnostics = diagnostics;
        }
    }

    /// <summary>
    /// Converts one-origin work seeds and local fixed-provider pairs into the
    /// canonical two-origin V2 frontage representation. Discovery is
    /// side-effect free: a synthetic companion is merely a candidate delta.
    /// </summary>
    internal static class AccessV2FrontageDiscovery
    {
        public static AccessV2EndpointSet Build(
            Tile2i boundsMin,
            Tile2i boundsMax,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            IEnumerable<Tile2i> startSeedOrigins,
            IEnumerable<Tile2i> fixedGoalOrigins,
            Func<Tile2i, AccessHeightProfile, AccessV2SyntheticValidation>
                syntheticValidator)
        {
            var diagnostics = new AccessV2FrontageDiagnostics();
            var starts = new Dictionary<AccessV2BandState, AccessV2StartFrontage>();
            var goals = new Dictionary<AccessV2BandState, AccessV2FixedFrontage>();
            var seeds = new HashSet<Tile2i>(startSeedOrigins);
            var allowedGoalOrigins = new HashSet<Tile2i>(fixedGoalOrigins);
            diagnostics.SeedCount = seeds.Count;
            diagnostics.FixedGoalOriginCount = allowedGoalOrigins.Count;

            foreach (Tile2i seed in seeds.OrderBy(item => item.X).ThenBy(item => item.Y))
            {
                if (!fixedProfiles.TryGetValue(seed, out AccessHeightProfile seedProfile))
                {
                    diagnostics.Reject("MissingSeedProfile");
                    continue;
                }
                IReadOnlyList<AccessV2TravelAxis> axes = GetEnabledAxes(seedProfile);
                if (axes.Count == 0)
                {
                    diagnostics.Reject("SeedProfileDisabled");
                    continue;
                }
                for (int axisIndex = 0; axisIndex < axes.Count; axisIndex++)
                    AddStartsForAxis(
                        axes[axisIndex], seed, seedProfile,
                        boundsMin, boundsMax, fixedProfiles,
                        syntheticValidator, starts, diagnostics);
            }

            foreach (Tile2i origin in allowedGoalOrigins
                .OrderBy(item => item.X).ThenBy(item => item.Y))
            {
                if (!fixedProfiles.TryGetValue(origin, out AccessHeightProfile profile))
                {
                    diagnostics.Reject("MissingFixedGoalProfile");
                    continue;
                }
                AddFixedGoalPair(
                    AccessV2TravelAxis.X, origin, profile,
                    boundsMin, boundsMax, fixedProfiles,
                    allowedGoalOrigins, goals, diagnostics);
                AddFixedGoalPair(
                    AccessV2TravelAxis.Y, origin, profile,
                    boundsMin, boundsMax, fixedProfiles,
                    allowedGoalOrigins, goals, diagnostics);
            }

            diagnostics.SyntheticStartCount = starts.Values.Count(
                item => item.HasSyntheticCompanion);
            diagnostics.ExistingPairStartCount = starts.Count
                - diagnostics.SyntheticStartCount;
            diagnostics.FixedFrontageCount = goals.Count;
            return new AccessV2EndpointSet(
                starts.Values.ToList(), goals.Values.ToList(), diagnostics);
        }

        public static AccessV2EndpointSet Build(
            AccessSearchSnapshot snapshot,
            IEnumerable<Tile2i> startSeedOrigins,
            IEnumerable<Tile2i> fixedGoalOrigins)
            => Build(
                snapshot.BoundsMin,
                snapshot.BoundsMax,
                snapshot.FixedProfiles,
                startSeedOrigins,
                fixedGoalOrigins,
                (origin, profile) => snapshot.IsCandidateProfileFeasible(
                        origin, profile, out string reason)
                    ? AccessV2SyntheticValidation.Valid
                    : new AccessV2SyntheticValidation(false, reason));

        private static void AddStartsForAxis(
            AccessV2TravelAxis axis,
            Tile2i seed,
            AccessHeightProfile seedProfile,
            Tile2i boundsMin,
            Tile2i boundsMax,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            Func<Tile2i, AccessHeightProfile, AccessV2SyntheticValidation>
                syntheticValidator,
            IDictionary<AccessV2BandState, AccessV2StartFrontage> starts,
            AccessV2FrontageDiagnostics diagnostics)
        {
            Tile2i laneDirection = AccessV2BandProfile.GetLaneDirection(axis);
            for (int side = -1; side <= 1; side += 2)
            {
                Tile2i companion = AccessV2Geometry.Add(
                    seed, AccessV2Geometry.Scale(laneDirection, side));
                bool companionIsFixed = fixedProfiles.TryGetValue(
                    companion, out AccessHeightProfile companionProfile);
                if (!companionIsFixed)
                    companionProfile = seedProfile;

                Tile2i anchor = side < 0 ? companion : seed;
                AccessHeightProfile lane0 = side < 0 ? companionProfile : seedProfile;
                AccessHeightProfile lane1 = side < 0 ? seedProfile : companionProfile;
                if (!AccessV2BandProfile.TryCreateEnabled(
                        axis, lane0, lane1,
                        out AccessV2BandProfile band, out string bandReason))
                {
                    diagnostics.Reject(bandReason);
                    continue;
                }

                AccessV2SyntheticValidation validation = companionIsFixed
                    ? AccessV2SyntheticValidation.Valid
                    : syntheticValidator(companion, companionProfile);
                if (!validation.IsValid)
                {
                    diagnostics.Reject(validation.Reason);
                    continue;
                }

                for (int directionSign = -1; directionSign <= 1; directionSign += 2)
                {
                    Tile2i direction = axis == AccessV2TravelAxis.X
                        ? new Tile2i(4 * directionSign, 0)
                        : new Tile2i(0, 4 * directionSign);
                    var state = new AccessV2BandState(anchor, band, direction);
                    if (!AccessV2Geometry.IsInsideBounds(state, boundsMin, boundsMax))
                    {
                        diagnostics.Reject("OutOfAreaFrontage");
                        continue;
                    }
                    if (!IsExposed(state, direction, fixedProfiles))
                    {
                        diagnostics.Reject("StartFrontageNotExposed");
                        continue;
                    }
                    if (!starts.ContainsKey(state))
                        starts.Add(
                            state,
                            new AccessV2StartFrontage(
                                state,
                                seed,
                                companionIsFixed ? (Tile2i?)null : companion));
                }
            }
        }

        private static void AddFixedGoalPair(
            AccessV2TravelAxis axis,
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i boundsMin,
            Tile2i boundsMax,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            ISet<Tile2i> allowedGoalOrigins,
            IDictionary<AccessV2BandState, AccessV2FixedFrontage> goals,
            AccessV2FrontageDiagnostics diagnostics)
        {
            Tile2i companion = AccessV2Geometry.Add(
                origin, AccessV2BandProfile.GetLaneDirection(axis));
            if (!allowedGoalOrigins.Contains(companion)) return;
            if (!fixedProfiles.TryGetValue(
                    companion, out AccessHeightProfile companionProfile))
            {
                diagnostics.Reject("MissingFixedGoalProfile");
                return;
            }
            if (!AccessV2BandProfile.TryCreateEnabled(
                    axis, profile, companionProfile,
                    out AccessV2BandProfile band, out string bandReason))
            {
                diagnostics.Reject(bandReason);
                return;
            }

            for (int exposedSign = -1; exposedSign <= 1; exposedSign += 2)
            {
                Tile2i exposedDirection = axis == AccessV2TravelAxis.X
                    ? new Tile2i(4 * exposedSign, 0)
                    : new Tile2i(0, 4 * exposedSign);
                var state = new AccessV2BandState(
                    origin, band,
                    AccessV2Geometry.Scale(exposedDirection, -1));
                if (!AccessV2Geometry.IsInsideBounds(state, boundsMin, boundsMax))
                {
                    diagnostics.Reject("OutOfAreaFixedFrontage");
                    continue;
                }
                if (!IsExposed(state, exposedDirection, fixedProfiles))
                {
                    diagnostics.Reject("FixedFrontageNotExposed");
                    continue;
                }
                if (!goals.ContainsKey(state))
                    goals.Add(
                        state,
                        new AccessV2FixedFrontage(state, exposedDirection));
            }
        }

        private static bool IsExposed(
            AccessV2BandState state,
            Tile2i outwardDirection,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> fixedProfiles)
            => !fixedProfiles.ContainsKey(AccessV2Geometry.Add(
                    state.GetLaneOrigin(0), outwardDirection))
                && !fixedProfiles.ContainsKey(AccessV2Geometry.Add(
                    state.GetLaneOrigin(1), outwardDirection));

        private static IReadOnlyList<AccessV2TravelAxis> GetEnabledAxes(
            AccessHeightProfile profile)
        {
            if (!AccessV2BandProfile.TryGetProfileMode(
                    profile, out AccessSearchMode mode))
                return Array.Empty<AccessV2TravelAxis>();
            if (mode == AccessSearchMode.Flat)
                return new[] { AccessV2TravelAxis.X, AccessV2TravelAxis.Y };
            if (mode == AccessSearchMode.XPositive
                || mode == AccessSearchMode.XNegative)
                return new[] { AccessV2TravelAxis.X };
            if (mode == AccessSearchMode.YPositive
                || mode == AccessSearchMode.YNegative)
                return new[] { AccessV2TravelAxis.Y };
            return Array.Empty<AccessV2TravelAxis>();
        }
    }
}

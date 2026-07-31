using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal sealed class AccessV2StartFrontage
    {
        public AccessV2BandState State { get; }
        public Tile2i FixedSeedOrigin { get; }
        public AccessV2Transition? InitialTransition { get; }
        public AccessV2Transition? LaunchSuccessor { get; }

        public bool IsSourceLaunch => LaunchSuccessor != null;

        public AccessV2StartFrontage(
            AccessV2BandState state,
            Tile2i fixedSeedOrigin)
        {
            State = state;
            FixedSeedOrigin = fixedSeedOrigin;
        }

        public AccessV2StartFrontage(
            AccessV2BandState state,
            Tile2i fixedSeedOrigin,
            AccessV2Transition? initialTransition,
            AccessV2Transition launchSuccessor)
        {
            State = state;
            FixedSeedOrigin = fixedSeedOrigin;
            InitialTransition = initialTransition;
            LaunchSuccessor = launchSuccessor;
        }
    }

    internal sealed class AccessV2FrontageDiagnostics
    {
        private readonly Dictionary<string, int> m_rejections =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public int SeedCount { get; internal set; }
        public int SourceLaunchCount { get; internal set; }
        public int DirectFixtureStartCount { get; internal set; }
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
        public IReadOnlyList<IReadOnlyList<AccessV2StartFrontage>> StartTiers { get; }
        public AccessV2FrontageDiagnostics Diagnostics { get; }

        public AccessV2EndpointSet(
            IReadOnlyList<AccessV2StartFrontage> starts,
            AccessV2FrontageDiagnostics diagnostics)
            : this(
                new[] { starts },
                diagnostics)
        {
        }

        public AccessV2EndpointSet(
            IReadOnlyList<IReadOnlyList<AccessV2StartFrontage>> startTiers,
            AccessV2FrontageDiagnostics diagnostics)
        {
            StartTiers = startTiers;
            Starts = startTiers.Count == 0
                ? Array.Empty<AccessV2StartFrontage>()
                : startTiers[0];
            Diagnostics = diagnostics;
        }

        public AccessV2EndpointSet ForStartTier(int tierIndex)
            => new AccessV2EndpointSet(
                new[] { StartTiers[tierIndex] },
                Diagnostics);
    }

    /// <summary>
    /// Enumerates complete two-slice source launches from arithmetic-center
    /// distance tiers.
    /// Discovery is side-effect free: generated launch origins remain
    /// candidate deltas until the search evaluates them.
    /// </summary>
    internal static class AccessV2FrontageDiscovery
    {
        public static AccessV2EndpointSet Build(
            Tile2i boundsMin,
            Tile2i boundsMax,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            IEnumerable<Tile2i> startSeedOrigins)
        {
            var diagnostics = new AccessV2FrontageDiagnostics();
            var seeds = new HashSet<Tile2i>(startSeedOrigins);
            diagnostics.SeedCount = seeds.Count;

            var startTiers =
                new List<IReadOnlyList<AccessV2StartFrontage>>();
            foreach (IReadOnlyList<Tile2i> seedTier
                in BuildSourceCenterDistanceTiers(seeds))
            {
                var starts =
                    new Dictionary<SourceLaunchKey, AccessV2StartFrontage>();
                foreach (Tile2i seed in seedTier)
                {
                    if (!fixedProfiles.TryGetValue(
                            seed, out AccessHeightProfile seedProfile))
                    {
                        diagnostics.Reject("MissingSeedProfile");
                        continue;
                    }
                    IReadOnlyList<AccessV2TravelAxis> axes =
                        GetEnabledAxes(seedProfile);
                    if (axes.Count == 0)
                    {
                        diagnostics.Reject("SeedProfileDisabled");
                        continue;
                    }
                    for (int axisIndex = 0;
                        axisIndex < axes.Count;
                        axisIndex++)
                    {
                        AddSourceLaunchesForAxis(
                            axes[axisIndex], seed, seedProfile,
                            boundsMin, boundsMax, fixedProfiles,
                            starts, diagnostics);
                    }
                }
                if (starts.Count > 0)
                    startTiers.Add(starts.Values.ToList());
            }

            IEnumerable<AccessV2StartFrontage> allStarts =
                startTiers.SelectMany(tier => tier);
            diagnostics.SourceLaunchCount = allStarts.Count(
                item => item.IsSourceLaunch);
            diagnostics.DirectFixtureStartCount = allStarts.Count()
                - diagnostics.SourceLaunchCount;
            return new AccessV2EndpointSet(
                startTiers, diagnostics);
        }

        public static AccessV2EndpointSet Build(
            AccessSearchSnapshot snapshot,
            IEnumerable<Tile2i> startSeedOrigins)
            => Build(
                snapshot.BoundsMin,
                snapshot.BoundsMax,
                snapshot.FixedProfiles,
                startSeedOrigins);

        private static void AddSourceLaunchesForAxis(
            AccessV2TravelAxis axis,
            Tile2i seed,
            AccessHeightProfile seedProfile,
            Tile2i boundsMin,
            Tile2i boundsMax,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            IDictionary<SourceLaunchKey, AccessV2StartFrontage> starts,
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

                for (int directionSign = -1; directionSign <= 1; directionSign += 2)
                {
                    Tile2i direction = axis == AccessV2TravelAxis.X
                        ? new Tile2i(4 * directionSign, 0)
                        : new Tile2i(0, 4 * directionSign);
                    var state = new AccessV2BandState(anchor, band, direction);
                    if (!AccessV2Geometry.IsInsideBounds(state, boundsMin, boundsMax))
                    {
                        diagnostics.Reject("OutOfAreaSourceLaunch");
                        continue;
                    }

                    AccessV2Transition? initial = null;
                    if (!companionIsFixed)
                    {
                        int companionLane =
                            state.GetLaneOrigin(0) == companion ? 0 : 1;
                        initial = new AccessV2Transition(
                            AccessV2TransitionKind.SourceLaunch,
                            state,
                            new[] { state.GetLane(companionLane) },
                            new[] { seed },
                            scoreOnlyGeneratedExteriorRays: true);
                    }

                    foreach (AccessV2Transition candidate
                        in AccessV2Geometry.EnumerateStraight(state))
                    {
                        if (!TryResolveSuccessor(
                                candidate, fixedProfiles,
                                out AccessV2Transition successor))
                        {
                            diagnostics.Reject(
                                "SourceLaunchSuccessorFixedConflict");
                            continue;
                        }
                        if (!AccessV2Geometry.IsInsideBounds(
                                successor, boundsMin, boundsMax))
                        {
                            diagnostics.Reject("OutOfAreaSourceLaunch");
                            continue;
                        }
                        var key = new SourceLaunchKey(
                            seed, state, successor.Next);
                        if (starts.ContainsKey(key)) continue;
                        starts.Add(
                            key,
                            new AccessV2StartFrontage(
                                state,
                                seed,
                                initial,
                                successor));
                    }
                }
            }
        }

        private static bool TryResolveSuccessor(
            AccessV2Transition candidate,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            out AccessV2Transition resolved)
        {
            var generated = new List<AccessV2OriginProfile>(2);
            var context = new List<Tile2i>(
                candidate.LocalContextOrigins);
            for (int lane = 0; lane < 2; lane++)
            {
                AccessV2OriginProfile item = candidate.Next.GetLane(lane);
                if (fixedProfiles.TryGetValue(
                        item.Origin, out AccessHeightProfile fixedProfile))
                {
                    if (!ProfilesEqual(item.Profile, fixedProfile))
                    {
                        resolved = null!;
                        return false;
                    }
                    context.Add(item.Origin);
                }
                else
                {
                    generated.Add(item);
                }
            }
            resolved = new AccessV2Transition(
                AccessV2TransitionKind.Straight,
                candidate.Next,
                generated,
                context,
                scoreOnlyGeneratedExteriorRays: true);
            return true;
        }

        private static bool ProfilesEqual(
            AccessHeightProfile left,
            AccessHeightProfile right)
            => left.Nw2 == right.Nw2
                && left.Ne2 == right.Ne2
                && left.Se2 == right.Se2
                && left.Sw2 == right.Sw2;

        private static IReadOnlyList<IReadOnlyList<Tile2i>>
            BuildSourceCenterDistanceTiers(ISet<Tile2i> seeds)
        {
            if (seeds.Count == 0)
                return Array.Empty<IReadOnlyList<Tile2i>>();
            long sumCenterX = 0;
            long sumCenterY = 0;
            foreach (Tile2i seed in seeds)
            {
                sumCenterX += seed.X + 2;
                sumCenterY += seed.Y + 2;
            }
            int count = seeds.Count;
            return seeds
                .GroupBy(seed =>
                    Math.Abs((long)(seed.X + 2) * count - sumCenterX)
                    + Math.Abs((long)(seed.Y + 2) * count - sumCenterY))
                .OrderBy(group => group.Key)
                .Select(group => (IReadOnlyList<Tile2i>)group
                    .OrderBy(seed => seed.X)
                    .ThenBy(seed => seed.Y)
                    .ToList())
                .ToList();
        }

        private readonly struct SourceLaunchKey : IEquatable<SourceLaunchKey>
        {
            private readonly Tile2i m_sourceRoot;
            private readonly AccessV2BandState m_initial;
            private readonly AccessV2BandState m_successor;

            public SourceLaunchKey(
                Tile2i sourceRoot,
                AccessV2BandState initial,
                AccessV2BandState successor)
            {
                m_sourceRoot = sourceRoot;
                m_initial = initial;
                m_successor = successor;
            }

            public bool Equals(SourceLaunchKey other)
                => m_sourceRoot == other.m_sourceRoot
                    && m_initial.Equals(other.m_initial)
                    && m_successor.Equals(other.m_successor);

            public override bool Equals(object? obj)
                => obj is SourceLaunchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = m_sourceRoot.GetHashCode();
                    hash = (hash * 397) ^ m_initial.GetHashCode();
                    return (hash * 397) ^ m_successor.GetHashCode();
                }
            }
        }

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

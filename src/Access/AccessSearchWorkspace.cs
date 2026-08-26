using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Mafi;
using AutoTerrainDesignations.Access.V2;

namespace AutoTerrainDesignations.Access
{
    /// <summary>
    /// Pure-search evaluator boundary. Implementations may be cooperative
    /// adapters today; the worker backend will provide an evaluator backed by
    /// captured primitive facts rather than live game objects.
    /// </summary>
    internal interface IAccessSearchEvaluator
    {
        bool HasWorkableHandoffEvaluator { get; }
        bool HasWorkableHandoffSpanEvaluator { get; }
        IReadOnlyList<AccessGroundHandoff> GetWorkableHandoffs(
            Tile2i origin, AccessHeightProfile profile,
            Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile);
        IReadOnlyList<AccessGroundHandoff> GetWorkableHandoffSpans(
            IReadOnlyList<AccessHandoffSpanCell> cells);
        bool HasV2WorkableHandoffEvaluator { get; }
        AccessV2TerminalCrestEvidence ClassifyV2TerminalCrest(
            AccessV2BandState next,
            byte activeLanes,
            AccessHandoffOperation expectedOperation);
        IReadOnlyList<AccessGroundHandoff> GetV2WorkableHandoffs(
            Tile2i origin, AccessHeightProfile profile,
            Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile);
        IReadOnlyList<AccessGroundHandoff> GetV2WorkableHandoffSpans(
            IReadOnlyList<AccessHandoffSpanCell> cells);
    }

    internal sealed class EmptyAccessSearchEvaluator : IAccessSearchEvaluator
    {
        public static readonly EmptyAccessSearchEvaluator Instance =
            new EmptyAccessSearchEvaluator();

        private EmptyAccessSearchEvaluator() { }

        public bool HasWorkableHandoffEvaluator => false;
        public bool HasWorkableHandoffSpanEvaluator => false;
        public bool HasV2WorkableHandoffEvaluator => false;
        public AccessV2TerminalCrestEvidence ClassifyV2TerminalCrest(
            AccessV2BandState next,
            byte activeLanes,
            AccessHandoffOperation expectedOperation)
            => new AccessV2TerminalCrestEvidence(
                AccessHandoffOperation.None,
                AccessV2TerminalCrestState.Uncrested,
                AccessV2TerminalCrestState.Uncrested,
                "TerminalCrestEvaluatorUnavailable", 0);
        public IReadOnlyList<AccessGroundHandoff> GetWorkableHandoffs(
            Tile2i origin, AccessHeightProfile profile,
            Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile)
            => Array.Empty<AccessGroundHandoff>();
        public IReadOnlyList<AccessGroundHandoff> GetWorkableHandoffSpans(
            IReadOnlyList<AccessHandoffSpanCell> cells)
            => Array.Empty<AccessGroundHandoff>();
        public IReadOnlyList<AccessGroundHandoff> GetV2WorkableHandoffs(
            Tile2i origin, AccessHeightProfile profile,
            Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile)
            => Array.Empty<AccessGroundHandoff>();
        public IReadOnlyList<AccessGroundHandoff> GetV2WorkableHandoffSpans(
            IReadOnlyList<AccessHandoffSpanCell> cells)
            => Array.Empty<AccessGroundHandoff>();
    }

    /// <summary>
    /// Compatibility adapter for focused fixtures that inject a deliberately
    /// synthetic handoff result. Production workspaces reconstruct their
    /// evaluator from the captured snapshot instead.
    /// </summary>
    internal sealed class CooperativeAccessSearchEvaluator : IAccessSearchEvaluator
    {
        private readonly Func<Tile2i, AccessHeightProfile, Tile2i,
            AccessHeightProfile, IReadOnlyList<AccessGroundHandoff>>?
            m_workableHandoffs;
        private readonly Func<IReadOnlyList<AccessHandoffSpanCell>,
            IReadOnlyList<AccessGroundHandoff>>? m_workableHandoffSpans;
        private readonly Func<Tile2i, AccessHeightProfile, Tile2i,
            AccessHeightProfile, IReadOnlyList<AccessGroundHandoff>>?
            m_v2WorkableHandoffs;
        private readonly Func<IReadOnlyList<AccessHandoffSpanCell>,
            IReadOnlyList<AccessGroundHandoff>>? m_v2WorkableHandoffSpans;
        private readonly AccessV2TerminalCrestEvaluator?
            m_v2TerminalCrestEvaluator;

        public CooperativeAccessSearchEvaluator(
            Func<Tile2i, AccessHeightProfile, Tile2i, AccessHeightProfile,
                IReadOnlyList<AccessGroundHandoff>>? workableHandoffs = null,
            Func<IReadOnlyList<AccessHandoffSpanCell>,
                IReadOnlyList<AccessGroundHandoff>>? workableHandoffSpans = null,
            Func<Tile2i, AccessHeightProfile, Tile2i, AccessHeightProfile,
                IReadOnlyList<AccessGroundHandoff>>? v2WorkableHandoffs = null,
            Func<IReadOnlyList<AccessHandoffSpanCell>,
                IReadOnlyList<AccessGroundHandoff>>? v2WorkableHandoffSpans = null,
            AccessV2TerminalCrestEvaluator? v2TerminalCrestEvaluator = null)
        {
            m_workableHandoffs = workableHandoffs;
            m_workableHandoffSpans = workableHandoffSpans;
            m_v2WorkableHandoffs = v2WorkableHandoffs;
            m_v2WorkableHandoffSpans = v2WorkableHandoffSpans;
            m_v2TerminalCrestEvaluator = v2TerminalCrestEvaluator;
        }

        public bool HasWorkableHandoffEvaluator => m_workableHandoffs != null;
        public bool HasWorkableHandoffSpanEvaluator => m_workableHandoffSpans != null;
        public bool HasV2WorkableHandoffEvaluator =>
            m_v2WorkableHandoffs != null && m_v2WorkableHandoffSpans != null;
        public IReadOnlyList<AccessGroundHandoff> GetWorkableHandoffs(
            Tile2i origin, AccessHeightProfile profile,
            Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile)
            => m_workableHandoffs?.Invoke(
                origin, profile, predecessorOrigin, predecessorProfile)
                ?? Array.Empty<AccessGroundHandoff>();
        public IReadOnlyList<AccessGroundHandoff> GetWorkableHandoffSpans(
            IReadOnlyList<AccessHandoffSpanCell> cells)
            => m_workableHandoffSpans?.Invoke(cells)
                ?? Array.Empty<AccessGroundHandoff>();
        public IReadOnlyList<AccessGroundHandoff> GetV2WorkableHandoffs(
            Tile2i origin, AccessHeightProfile profile,
            Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile)
            => m_v2WorkableHandoffs?.Invoke(
                origin, profile, predecessorOrigin, predecessorProfile)
                ?? Array.Empty<AccessGroundHandoff>();
        public AccessV2TerminalCrestEvidence ClassifyV2TerminalCrest(
            AccessV2BandState next,
            byte activeLanes,
            AccessHandoffOperation expectedOperation)
            => m_v2TerminalCrestEvaluator?.Invoke(
                next, activeLanes, expectedOperation)
                ?? new AccessV2TerminalCrestEvidence(
                    AccessHandoffOperation.None,
                    AccessV2TerminalCrestState.Uncrested,
                    AccessV2TerminalCrestState.Uncrested,
                    "TerminalCrestEvaluatorUnavailable", 0);
        public IReadOnlyList<AccessGroundHandoff> GetV2WorkableHandoffSpans(
            IReadOnlyList<AccessHandoffSpanCell> cells)
            => m_v2WorkableHandoffSpans?.Invoke(cells)
                ?? Array.Empty<AccessGroundHandoff>();
    }

    /// <summary>
    /// Pure evaluator reconstructed by the workspace from value-owned snapshot
    /// facts. No caller-built closure or live game object crosses the search
    /// boundary. The cooperative and future worker backends therefore use the
    /// same evaluator construction path.
    /// </summary>
    internal sealed class SnapshotAccessSearchEvaluator : IAccessSearchEvaluator
    {
        private readonly AccessSearchSnapshot m_snapshot;
        private readonly Dictionary<string, IReadOnlyList<AccessGroundHandoff>>
            m_workableHandoffCache =
                new Dictionary<string, IReadOnlyList<AccessGroundHandoff>>();
        private readonly Dictionary<string, IReadOnlyList<AccessGroundHandoff>>
            m_workableHandoffSpanCache =
                new Dictionary<string, IReadOnlyList<AccessGroundHandoff>>();
        private readonly Dictionary<string, IReadOnlyList<AccessGroundHandoff>>
            m_v2WorkableHandoffCache =
                new Dictionary<string, IReadOnlyList<AccessGroundHandoff>>();
        private readonly Dictionary<string, IReadOnlyList<AccessGroundHandoff>>
            m_v2WorkableHandoffSpanCache =
                new Dictionary<string, IReadOnlyList<AccessGroundHandoff>>();

        public SnapshotAccessSearchEvaluator(AccessSearchSnapshot snapshot)
        {
            m_snapshot = snapshot
                ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public bool HasWorkableHandoffEvaluator => true;
        public bool HasWorkableHandoffSpanEvaluator => true;
        public bool HasV2WorkableHandoffEvaluator => true;

        public AccessV2TerminalCrestEvidence ClassifyV2TerminalCrest(
            AccessV2BandState next,
            byte activeLanes,
            AccessHandoffOperation expectedOperation)
            => AccessV2TerminalCrestClassifier.Classify(
                next, activeLanes, expectedOperation,
                m_snapshot.PreciseTerrainHeights);

        public IReadOnlyList<AccessGroundHandoff> GetWorkableHandoffs(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i predecessorOrigin,
            AccessHeightProfile predecessorProfile)
            => AccessHandoffEvaluator.BuildProspectiveWorkableHandoffs(
                origin,
                profile,
                predecessorOrigin,
                predecessorProfile,
                m_snapshot.PreciseTerrainHeights,
                m_snapshot.DesignationReadinessFacts,
                m_snapshot.GroundNodes,
                m_snapshot.GoalGroundNodeSet,
                m_snapshot.PropCleanupByTile,
                m_snapshot.TerrainPathableWithoutBlockers,
                m_snapshot.VehicleWidth,
                m_workableHandoffCache,
                propCleanupByOrigin: m_snapshot.PropCleanupByOrigin);

        public IReadOnlyList<AccessGroundHandoff> GetWorkableHandoffSpans(
            IReadOnlyList<AccessHandoffSpanCell> cells)
            => AccessHandoffEvaluator.BuildProspectiveWorkableHandoffSpan(
                cells,
                m_snapshot.PreciseTerrainHeights,
                m_snapshot.DesignationReadinessFacts,
                m_snapshot.GroundNodes,
                m_snapshot.GoalGroundNodeSet,
                m_snapshot.PropCleanupByTile,
                m_snapshot.TerrainPathableWithoutBlockers,
                m_snapshot.VehicleWidth,
                validateEverySpanCell: true,
                handoffCache: m_workableHandoffSpanCache,
                propCleanupByOrigin: m_snapshot.PropCleanupByOrigin);

        public IReadOnlyList<AccessGroundHandoff> GetV2WorkableHandoffs(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i predecessorOrigin,
            AccessHeightProfile predecessorProfile)
            => AccessHandoffEvaluator.BuildProspectiveWorkableHandoffs(
                origin,
                profile,
                predecessorOrigin,
                predecessorProfile,
                m_snapshot.PreciseTerrainHeights,
                m_snapshot.DesignationReadinessFacts,
                m_snapshot.GroundNodes,
                m_snapshot.GoalGroundNodeSet,
                m_snapshot.PropCleanupByTile,
                m_snapshot.TerrainPathableWithoutBlockers,
                vehicleClearance: 0,
                handoffCache: m_v2WorkableHandoffCache,
                useV2CornerCrestRule: true,
                propCleanupByOrigin: m_snapshot.PropCleanupByOrigin);

        public IReadOnlyList<AccessGroundHandoff> GetV2WorkableHandoffSpans(
            IReadOnlyList<AccessHandoffSpanCell> cells)
            => AccessHandoffEvaluator.BuildProspectiveWorkableHandoffSpan(
                cells,
                m_snapshot.PreciseTerrainHeights,
                m_snapshot.DesignationReadinessFacts,
                m_snapshot.GroundNodes,
                m_snapshot.GoalGroundNodeSet,
                m_snapshot.PropCleanupByTile,
                m_snapshot.TerrainPathableWithoutBlockers,
                vehicleClearance: 0,
                validateEverySpanCell: false,
                handoffCache: m_v2WorkableHandoffSpanCache,
                useV2CornerCrestRule: true,
                propCleanupByOrigin: m_snapshot.PropCleanupByOrigin);
    }

    /// <summary>
    /// Request-local mutable execution state. It owns evaluator dispatch and
    /// search caches; the captured snapshot and request remain data-only.
    /// </summary>
    internal sealed class AccessSearchWorkspace
    {
        private static readonly ConditionalWeakTable<AccessSearchSnapshot,
            AccessSearchWorkspace> s_bySnapshot =
            new ConditionalWeakTable<AccessSearchSnapshot, AccessSearchWorkspace>();
        private readonly Dictionary<AccessSideRayCacheKey, AccessSideRayResult>
            m_sideRayCache = new Dictionary<AccessSideRayCacheKey, AccessSideRayResult>();
        internal V2.AccessV2History? ProjectedV2CachedHistory { get; set; }
        internal IReadOnlyDictionary<Tile2i, AccessHeightProfile>
            ProjectedV2CachedProfiles { get; set; } =
            new Dictionary<Tile2i, AccessHeightProfile>();

        public AccessSearchSnapshot Snapshot { get; }
        public IAccessSearchEvaluator Evaluator { get; }

        public AccessSearchWorkspace(
            AccessSearchSnapshot snapshot,
            IAccessSearchEvaluator? evaluator = null)
            : this(snapshot, evaluator, register: true)
        {
        }

        private AccessSearchWorkspace(
            AccessSearchSnapshot snapshot,
            IAccessSearchEvaluator? evaluator,
            bool register)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Evaluator = evaluator ?? new SnapshotAccessSearchEvaluator(Snapshot);
            if (register)
            {
                s_bySnapshot.Remove(Snapshot);
                s_bySnapshot.Add(Snapshot, this);
            }
        }

        internal static AccessSearchWorkspace For(AccessSearchSnapshot snapshot)
        {
            if (s_bySnapshot.TryGetValue(snapshot, out AccessSearchWorkspace workspace))
                return workspace;
            var created = new AccessSearchWorkspace(
                snapshot, null, register: false);
            return s_bySnapshot.GetValue(snapshot, _ => created);
        }

        public bool TryGetCachedSideRay(
            AccessSideRayCacheKey key, out AccessSideRayResult result)
            => m_sideRayCache.TryGetValue(key, out result);

        public void CacheSideRay(
            AccessSideRayCacheKey key, AccessSideRayResult result)
            => m_sideRayCache[key] = result;
    }
}

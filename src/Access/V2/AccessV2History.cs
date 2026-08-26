using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    /// <summary>
    /// Persistent parent-plus-delta geometry history with a lazily memoized
    /// projected-terrain overlay. Work heights and safety-only disruption are
    /// retained separately per operation and owner.
    /// </summary>
    internal sealed class AccessV2History
    {
        private static readonly IReadOnlyDictionary<Tile2i, RayTileDelta>
            s_emptyRayDelta = new Dictionary<Tile2i, RayTileDelta>();
        public static readonly AccessV2History Empty = new AccessV2History();

        private readonly AccessV2History? m_parent;
        private readonly int m_sequence;
        private readonly byte m_travelDirectionMask;
        private readonly IReadOnlyList<AccessV2OriginProfile> m_delta;
        private readonly IReadOnlyDictionary<Tile2i, RayTileDelta> m_rayDelta;
        private readonly IReadOnlyCollection<string> m_cleanupKeyDelta;
        private readonly IReadOnlyCollection<Tile2i>
            m_snapshotSafetyExemptOriginDelta;
        private Dictionary<RayEnvelopeCacheKey, RayEnvelope>? m_rayEnvelopeCache;
        private HashSet<Tile2i>? m_cachedRayTiles;
        private HashSet<Tile2i>? m_cachedHandoffRayTiles;
        private HashSet<Tile2i>? m_cachedOrigins;
        private HashSet<Tile2i>? m_cachedSnapshotSafetyExemptOrigins;

        public int OriginCount { get; }
        public int RayConstraintCount { get; }
        public int CollapsedRayEntryCount { get; }
        public int CleanupKeyCount { get; }
        public int Signature { get; }
        public bool RequiresStrictSelfDisruptionChecks
            => CountBits(m_travelDirectionMask) > 2;
        public bool WillRequireStrictSelfDisruptionChecks(
            Tile2i longitudinalDirection)
            => CountBits((byte)(m_travelDirectionMask
                | DirectionBit(longitudinalDirection))) > 2;

        private AccessV2History()
        {
            m_delta = Array.Empty<AccessV2OriginProfile>();
            m_rayDelta = s_emptyRayDelta;
            m_cleanupKeyDelta = Array.Empty<string>();
            m_snapshotSafetyExemptOriginDelta = Array.Empty<Tile2i>();
            m_cachedSnapshotSafetyExemptOrigins = new HashSet<Tile2i>();
        }

        private AccessV2History(
            AccessV2History parent,
            IReadOnlyList<AccessV2OriginProfile> delta,
            IReadOnlyList<AccessRayHeightConstraint> rayDelta,
            IReadOnlyCollection<string> cleanupKeyDelta,
            IReadOnlyCollection<Tile2i> snapshotSafetyExemptOriginDelta,
            Tile2i? longitudinalDirection = null)
        {
            m_parent = parent;
            m_sequence = parent.m_sequence + 1;
            m_travelDirectionMask = (byte)(parent.m_travelDirectionMask
                | DirectionBit(longitudinalDirection));
            m_delta = delta;
            m_rayDelta = BuildRayDelta(rayDelta, m_sequence);
            m_cleanupKeyDelta = cleanupKeyDelta;
            m_snapshotSafetyExemptOriginDelta =
                snapshotSafetyExemptOriginDelta;
            if (snapshotSafetyExemptOriginDelta.Count == 0)
            {
                m_cachedSnapshotSafetyExemptOrigins =
                    (HashSet<Tile2i>)parent.GetSnapshotSafetyExemptOrigins();
            }
            else
            {
                m_cachedSnapshotSafetyExemptOrigins = new HashSet<Tile2i>(
                    parent.GetSnapshotSafetyExemptOrigins());
                m_cachedSnapshotSafetyExemptOrigins.UnionWith(
                    snapshotSafetyExemptOriginDelta);
            }
            OriginCount = parent.OriginCount + delta.Count;
            RayConstraintCount = parent.RayConstraintCount + rayDelta.Count;
            CollapsedRayEntryCount = parent.CollapsedRayEntryCount
                + CountRayEntries(m_rayDelta);
            CleanupKeyCount = parent.CleanupKeyCount + cleanupKeyDelta.Count;
            int signature = parent.Signature;
            for (int index = 0; index < delta.Count; index++)
                signature ^= OriginProfileHash(delta[index]);
            foreach (string key in cleanupKeyDelta)
                signature ^= StringComparer.Ordinal.GetHashCode(key);
            Signature = signature;
        }

        private AccessV2History(
            AccessV2History parent,
            bool resetDirectionScope)
        {
            m_parent = parent;
            m_sequence = parent.m_sequence + 1;
            m_travelDirectionMask = resetDirectionScope
                ? (byte)0
                : parent.m_travelDirectionMask;
            m_delta = Array.Empty<AccessV2OriginProfile>();
            m_rayDelta = s_emptyRayDelta;
            m_cleanupKeyDelta = Array.Empty<string>();
            m_snapshotSafetyExemptOriginDelta = Array.Empty<Tile2i>();
            m_cachedSnapshotSafetyExemptOrigins =
                (HashSet<Tile2i>)parent.GetSnapshotSafetyExemptOrigins();
            OriginCount = parent.OriginCount;
            RayConstraintCount = parent.RayConstraintCount;
            CollapsedRayEntryCount = parent.CollapsedRayEntryCount;
            CleanupKeyCount = parent.CleanupKeyCount;
            Signature = parent.Signature;
        }

        public AccessV2History ResetDirectionScope()
            => m_travelDirectionMask == 0
                ? this
                : new AccessV2History(this, resetDirectionScope: true);

        public bool ContainsOrigin(Tile2i origin)
            => TryGetProfile(origin, out _);

        public bool ContainsGeneratedTile(Tile2i tile)
        {
            if (m_cachedOrigins == null)
            {
                m_cachedOrigins = new HashSet<Tile2i>();
                for (AccessV2History? history = this;
                    history != null;
                    history = history.m_parent)
                    for (int index = 0;
                        index < history.m_delta.Count;
                        index++)
                        m_cachedOrigins.Add(
                            history.m_delta[index].Origin);
            }
            return m_cachedOrigins.Contains(
                new Tile2i(tile.X & -4, tile.Y & -4));
        }

        public IReadOnlyCollection<Tile2i>
            GetSnapshotSafetyExemptOrigins()
        {
            return m_cachedSnapshotSafetyExemptOrigins!;
        }

        public bool HasGeneratedProfileAt(
            Tile2i tile,
            IReadOnlyCollection<Tile2i>? exemptOrigins = null)
        {
            if (m_cachedOrigins == null)
            {
                m_cachedOrigins = new HashSet<Tile2i>();
                for (AccessV2History? history = this;
                    history != null;
                    history = history.m_parent)
                    for (int index = 0;
                        index < history.m_delta.Count;
                        index++)
                        m_cachedOrigins.Add(
                            history.m_delta[index].Origin);
            }
            Tile2i canonical = new Tile2i(tile.X & -4, tile.Y & -4);
            Tile2i[] candidates =
            {
                canonical,
                canonical + new RelTile2i(-4, 0),
                canonical + new RelTile2i(0, -4),
                canonical + new RelTile2i(-4, -4),
            };
            for (int index = 0; index < candidates.Length; index++)
            {
                Tile2i origin = candidates[index];
                if (m_cachedOrigins.Contains(origin)
                    && !IsExcluded(origin, exemptOrigins)
                    && tile.X >= origin.X && tile.X <= origin.X + 4
                    && tile.Y >= origin.Y && tile.Y <= origin.Y + 4)
                    return true;
            }
            return false;
        }

        public bool TryGetProfile(
            Tile2i origin,
            out AccessHeightProfile profile)
        {
            for (AccessV2History? history = this;
                history != null;
                history = history.m_parent)
            {
                for (int index = 0; index < history.m_delta.Count; index++)
                {
                    AccessV2OriginProfile item = history.m_delta[index];
                    if (item.Origin == origin)
                    {
                        profile = item.Profile;
                        return true;
                    }
                }
            }
            profile = default;
            return false;
        }

        public bool TryApply(
            AccessV2Transition transition,
            out AccessV2History next,
            out string reason)
            => TryApply(
                transition.Delta,
                transition.LocalContextOrigins,
                Array.Empty<AccessRayHeightConstraint>(),
                Array.Empty<string>(),
                AllowsEmptyDelta(transition.Kind),
                out next,
                out reason);

        public bool TryApply(
            AccessV2Transition transition,
            IReadOnlyList<AccessRayHeightConstraint> rayDelta,
            IReadOnlyCollection<string> cleanupKeyDelta,
            out AccessV2History next,
            out string reason)
            => TryApply(
                transition.Delta,
                transition.LocalContextOrigins,
                rayDelta,
                cleanupKeyDelta,
                AllowsEmptyDelta(transition.Kind),
                out next,
                out reason);

        public bool TryApply(
            IReadOnlyList<AccessV2OriginProfile> delta,
            IReadOnlyCollection<Tile2i> localContextOrigins,
            out AccessV2History next,
            out string reason)
            => TryApply(
                delta, localContextOrigins,
                Array.Empty<AccessRayHeightConstraint>(),
                Array.Empty<string>(),
                false,
                out next, out reason);

        public bool TryApply(
            IReadOnlyList<AccessV2OriginProfile> delta,
            IReadOnlyCollection<Tile2i> localContextOrigins,
            IReadOnlyList<AccessRayHeightConstraint> rayDelta,
            IReadOnlyCollection<string> cleanupKeyDelta,
            out AccessV2History next,
            out string reason)
            => TryApply(
                delta, localContextOrigins, rayDelta, cleanupKeyDelta,
                false, out next, out reason);

        private bool TryApply(
            IReadOnlyList<AccessV2OriginProfile> delta,
            IReadOnlyCollection<Tile2i> localContextOrigins,
            IReadOnlyList<AccessRayHeightConstraint> rayDelta,
            IReadOnlyCollection<string> cleanupKeyDelta,
            bool allowEmptyDelta,
            out AccessV2History next,
            out string reason)
        {
            next = this;
            if (!TryValidateApply(
                    delta, localContextOrigins, allowEmptyDelta, out reason))
                return false;

            next = ApplyValidated(
                delta, rayDelta, cleanupKeyDelta,
                Array.Empty<Tile2i>(),
                transitionDirection: null);
            return true;
        }

        /// <summary>
        /// Checks immutable geometry without constructing the temporary sets,
        /// dictionaries, copied deltas, and history node that TryApply needs.
        /// Search uses this before expensive terrain evaluation, then commits
        /// the already-validated delta exactly once if the label survives.
        /// </summary>
        public bool TryValidateApply(
            IReadOnlyList<AccessV2OriginProfile> delta,
            IReadOnlyCollection<Tile2i> localContextOrigins,
            out string reason)
            => TryValidateApply(
                delta, localContextOrigins, false, out reason);

        public bool TryValidateApply(
            AccessV2Transition transition,
            out string reason)
            => TryValidateApply(
                transition.Delta, transition.LocalContextOrigins,
                AllowsEmptyDelta(transition.Kind), out reason);

        private static bool AllowsEmptyDelta(AccessV2TransitionKind kind)
            => kind == AccessV2TransitionKind.Turn
                || kind == AccessV2TransitionKind.ProjectedGroundAdapter;

        private bool TryValidateApply(
            IReadOnlyList<AccessV2OriginProfile> delta,
            IReadOnlyCollection<Tile2i> localContextOrigins,
            bool allowEmptyDelta,
            out string reason)
        {
            if (delta.Count == 0 && !allowEmptyDelta)
            {
                reason = "EmptyTransitionDelta";
                return false;
            }

            for (int index = 0; index < delta.Count; index++)
            {
                AccessV2OriginProfile item = delta[index];
                if (!AccessV2Geometry.IsOriginAligned(item.Origin))
                {
                    reason = "UnalignedDeltaOrigin";
                    return false;
                }
                if (!item.Profile.HasIntegerCorners)
                {
                    reason = "HalfLevelCorner";
                    return false;
                }
                for (int prior = 0; prior < index; prior++)
                {
                    if (delta[prior].Origin == item.Origin)
                    {
                        reason = "DuplicateDeltaOrigin";
                        return false;
                    }
                }
                if (ContainsOrigin(item.Origin))
                {
                    reason = "OriginRevisit";
                    return false;
                }
            }

            for (int index = 0; index < delta.Count; index++)
                if (!ValidateContacts(
                        delta[index], delta, localContextOrigins,
                        out reason))
                    return false;

            reason = string.Empty;
            return true;
        }

        public AccessV2History ApplyValidated(
            IReadOnlyList<AccessV2OriginProfile> delta,
            IReadOnlyList<AccessRayHeightConstraint> rayDelta,
            IReadOnlyCollection<string> cleanupKeyDelta)
            => ApplyValidated(
                delta, rayDelta, cleanupKeyDelta,
                Array.Empty<Tile2i>(),
                transitionDirection: null);

        public AccessV2History ApplyValidated(
            AccessV2Transition transition,
            IReadOnlyList<AccessRayHeightConstraint> rayDelta,
            IReadOnlyCollection<string> cleanupKeyDelta)
            => ApplyValidated(
                transition.Delta, rayDelta, cleanupKeyDelta,
                Array.Empty<Tile2i>(),
                transition.Next.EntryDirection);

        public AccessV2History ApplyValidated(
            AccessV2Transition transition,
            IReadOnlyList<AccessRayHeightConstraint> rayDelta,
            IReadOnlyCollection<string> cleanupKeyDelta,
            IReadOnlyCollection<Tile2i> snapshotSafetyExemptOrigins)
            => ApplyValidated(
                transition.Delta, rayDelta, cleanupKeyDelta,
                snapshotSafetyExemptOrigins,
                transition.Next.EntryDirection);

        private AccessV2History ApplyValidated(
            IReadOnlyList<AccessV2OriginProfile> delta,
            IReadOnlyList<AccessRayHeightConstraint> rayDelta,
            IReadOnlyCollection<string> cleanupKeyDelta,
            IReadOnlyCollection<Tile2i> snapshotSafetyExemptOrigins,
            Tile2i? transitionDirection)
        {

            var copiedDelta = new AccessV2OriginProfile[delta.Count];
            for (int index = 0; index < delta.Count; index++)
                copiedDelta[index] = delta[index];
            var copiedRays = new AccessRayHeightConstraint[rayDelta.Count];
            for (int index = 0; index < rayDelta.Count; index++)
                copiedRays[index] = rayDelta[index];
            var copiedCleanup = new HashSet<string>(
                cleanupKeyDelta, StringComparer.Ordinal);
            var copiedSnapshotSafetyExemptOrigins = new HashSet<Tile2i>(
                snapshotSafetyExemptOrigins);
            return new AccessV2History(
                this, copiedDelta, copiedRays, copiedCleanup,
                copiedSnapshotSafetyExemptOrigins,
                transitionDirection);
        }

        public bool ContainsCleanupKey(string key)
        {
            for (AccessV2History? history = this;
                history != null;
                history = history.m_parent)
                if (history.m_cleanupKeyDelta.Contains(key))
                    return true;
            return false;
        }

        public AccessV2History ApplyCleanupKeys(
            IReadOnlyCollection<string> cleanupKeys)
        {
            if (cleanupKeys.Count == 0) return this;
            var added = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in cleanupKeys)
                if (!ContainsCleanupKey(key)) added.Add(key);
            return added.Count == 0
                ? this
                : new AccessV2History(
                    this,
                    Array.Empty<AccessV2OriginProfile>(),
                    Array.Empty<AccessRayHeightConstraint>(),
                    added,
                    Array.Empty<Tile2i>());
        }

        public bool IsProfileBlockedByRayEnvelope(
            Tile2i origin,
            AccessHeightProfile profile,
            IReadOnlyCollection<Tile2i>? supersededRayOwners,
            out AccessSideRayOperation operation)
            => IsProfileBlockedByRayEnvelopeCore(
                null, origin, profile, supersededRayOwners, out operation);

        private bool IsProfileBlockedByRayEnvelopeCore(
            AccessSearchSnapshot? snapshot,
            Tile2i origin,
            AccessHeightProfile profile,
            IReadOnlyCollection<Tile2i>? supersededRayOwners,
            out AccessSideRayOperation operation,
            AccessSearchDiagnostics? diagnostics = null)
        {
            const float epsilon = 0.0001f;
            long cutPriority = long.MinValue;
            long fillPriority = long.MinValue;
            for (int y = 0; y <= 4; y++)
            {
                for (int x = 0; x <= 4; x++)
                {
                    Tile2i tile = origin + new RelTile2i(x, y);
                    RayEnvelope envelope = GetRayEnvelope(
                        tile, supersededRayOwners, diagnostics);
                    if (!envelope.HasCut && !envelope.HasFill)
                    {
                        if (envelope.HasCutSafety)
                            cutPriority = Math.Max(
                                cutPriority, envelope.CutPriority);
                        if (envelope.HasFillSafety)
                            fillPriority = Math.Max(
                                fillPriority, envelope.FillPriority);
                        continue;
                    }
                    float profileHeight =
                        profile.GetHeight2NumeratorAt(x, y) / 32f;
                    if (envelope.HasCutSafety)
                        cutPriority = Math.Max(
                            cutPriority, envelope.CutPriority);
                    if (envelope.HasFillSafety)
                        fillPriority = Math.Max(
                            fillPriority, envelope.FillPriority);
                    if (envelope.HasCut
                        && profileHeight > envelope.CutCeiling + epsilon)
                        cutPriority = Math.Max(
                            cutPriority, envelope.CutPriority);
                    if (envelope.HasFill
                        && profileHeight < envelope.FillFloor - epsilon)
                        fillPriority = Math.Max(
                            fillPriority, envelope.FillPriority);
                }
            }
            if (cutPriority != long.MinValue
                || fillPriority != long.MinValue)
            {
                operation = cutPriority >= fillPriority
                    ? AccessSideRayOperation.Cut
                    : AccessSideRayOperation.Fill;
                return true;
            }
            operation = AccessSideRayOperation.None;
            return false;
        }

        public bool IsProfileBlockedByRayEnvelope(
            Tile2i origin,
            AccessHeightProfile profile,
            out AccessSideRayOperation operation)
            => IsProfileBlockedByRayEnvelope(
                origin, profile, null, out operation);

        public bool IsProfileBlockedByRayEnvelope(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile,
            IReadOnlyCollection<Tile2i>? supersededRayOwners,
            out AccessSideRayOperation operation,
            AccessSearchDiagnostics? diagnostics = null)
            => IsProfileBlockedByRayEnvelopeCore(
                snapshot, origin, profile, supersededRayOwners, out operation,
                diagnostics);

        public bool HasRayAt(
            Tile2i tile,
            AccessSideRayOperation operation,
            IReadOnlyCollection<Tile2i>? supersededRayOwners = null,
            AccessSearchDiagnostics? diagnostics = null)
        {
            RayEnvelope envelope = GetRayEnvelope(
                tile, supersededRayOwners, diagnostics);
            return operation == AccessSideRayOperation.Cut
                ? envelope.HasCut
                : operation == AccessSideRayOperation.Fill
                    && envelope.HasFill;
        }

        public AccessProjectedTerrainEffect GetProjectedTerrainEffect(
            Tile2i tile,
            IReadOnlyCollection<Tile2i>? exemptSafetyOwners = null,
            AccessSearchDiagnostics? diagnostics = null,
            bool includeSafety = true)
        {
            RayEnvelope envelope = GetRayEnvelope(
                tile, exemptSafetyOwners, diagnostics);
            return new AccessProjectedTerrainEffect
            {
                HasCutWork = envelope.HasCut,
                CutCeiling = envelope.CutCeiling,
                HasFillWork = envelope.HasFill,
                FillFloor = envelope.FillFloor,
                HasCutSafety = includeSafety && envelope.HasCutSafety,
                HasFillSafety = includeSafety && envelope.HasFillSafety,
            };
        }

        private RayEnvelope GetRayEnvelope(
            Tile2i tile,
            IReadOnlyCollection<Tile2i>? excludedOwners,
            AccessSearchDiagnostics? diagnostics)
        {
            bool cacheable = RayEnvelopeCacheKey.TryCreate(
                tile, excludedOwners, out RayEnvelopeCacheKey cacheKey);
            if (cacheable
                && m_rayEnvelopeCache != null
                && m_rayEnvelopeCache.TryGetValue(
                    cacheKey, out RayEnvelope cached))
            {
                if (diagnostics != null)
                    diagnostics.V2RayOverlayCacheHits++;
                return cached;
            }

            if (diagnostics != null)
                diagnostics.V2RayOverlayCacheMisses++;
            RayEnvelope result = default;
            bool first = true;
            for (AccessV2History? history = this;
                history != null;
                history = history.m_parent)
            {
                if (!first && diagnostics != null)
                    diagnostics.V2RayOverlayParentSteps++;
                first = false;
                if (history.m_rayDelta.TryGetValue(
                        tile, out RayTileDelta delta))
                    delta.MergeInto(ref result, excludedOwners);
            }

            // Empty is shared across requests and owns no delta. Caching misses
            // there would retain arbitrary world coordinates for process life.
            if (cacheable && m_parent != null)
            {
                if (m_rayEnvelopeCache == null)
                    m_rayEnvelopeCache =
                        new Dictionary<RayEnvelopeCacheKey, RayEnvelope>();
                m_rayEnvelopeCache[cacheKey] = result;
                if (diagnostics != null)
                    diagnostics.V2RayOverlayCacheEntries++;
            }
            return result;
        }

        private static IReadOnlyDictionary<Tile2i, RayTileDelta> BuildRayDelta(
            IReadOnlyList<AccessRayHeightConstraint> constraints,
            int sequence)
        {
            if (constraints.Count == 0)
                return s_emptyRayDelta;
            var result = new Dictionary<Tile2i, RayTileDelta>();
            for (int index = 0; index < constraints.Count; index++)
            {
                AccessRayHeightConstraint constraint = constraints[index];
                result.TryGetValue(
                    constraint.Tile, out RayTileDelta delta);
                delta.Add(constraint, sequence, index);
                result[constraint.Tile] = delta;
            }
            return result;
        }

        private static int CountRayEntries(
            IReadOnlyDictionary<Tile2i, RayTileDelta> delta)
        {
            int count = 0;
            foreach (RayTileDelta tile in delta.Values)
                count += tile.ContributionCount;
            return count;
        }

        public IReadOnlyDictionary<Tile2i, AccessHeightProfile> Flatten()
        {
            var stack = new Stack<AccessV2History>();
            for (AccessV2History? history = this;
                history != null;
                history = history.m_parent)
                stack.Push(history);
            var result = new Dictionary<Tile2i, AccessHeightProfile>();
            while (stack.Count > 0)
            {
                AccessV2History history = stack.Pop();
                for (int index = 0; index < history.m_delta.Count; index++)
                {
                    AccessV2OriginProfile item = history.m_delta[index];
                    result.Add(item.Origin, item.Profile);
                }
            }
            return result;
        }

        public IReadOnlyCollection<Tile2i> CollectRayTiles()
        {
            if (m_cachedRayTiles != null)
                return m_cachedRayTiles;
            var result = new HashSet<Tile2i>();
            for (AccessV2History? history = this;
                history != null;
                history = history.m_parent)
                foreach (Tile2i tile in history.m_rayDelta.Keys)
                    result.Add(tile);
            m_cachedRayTiles = result;
            return result;
        }

        public IReadOnlyCollection<Tile2i> CollectHandoffRayTiles()
        {
            if (m_cachedHandoffRayTiles != null)
                return m_cachedHandoffRayTiles;
            var currentOwners = new HashSet<Tile2i>();
            for (AccessV2History? history = this;
                history != null && currentOwners.Count == 0;
                history = history.m_parent)
                for (int index = 0; index < history.m_delta.Count; index++)
                    currentOwners.Add(history.m_delta[index].Origin);

            if (currentOwners.Count == 0)
            {
                m_cachedHandoffRayTiles =
                    (HashSet<Tile2i>)CollectRayTiles();
                return m_cachedHandoffRayTiles;
            }

            var result = new HashSet<Tile2i>();
            for (AccessV2History? history = this;
                history != null;
                history = history.m_parent)
                foreach (KeyValuePair<Tile2i, RayTileDelta> pair
                    in history.m_rayDelta)
                    if (pair.Value.HasContributionOutside(currentOwners))
                        result.Add(pair.Key);
            m_cachedHandoffRayTiles = result;
            return result;
        }

        public bool ContainsHandoffRayTile(Tile2i tile)
        {
            CollectHandoffRayTiles();
            return m_cachedHandoffRayTiles!.Contains(tile);
        }

        private bool ValidateContacts(
            AccessV2OriginProfile candidate,
            IReadOnlyList<AccessV2OriginProfile> deltaProfiles,
            IReadOnlyCollection<Tile2i> localContext,
            out string reason)
        {
            for (int dx = -4; dx <= 4; dx += 4)
            {
                for (int dy = -4; dy <= 4; dy += 4)
                {
                    if (dx == 0 && dy == 0) continue;
                    Tile2i neighbor = new Tile2i(
                        candidate.Origin.X + dx,
                        candidate.Origin.Y + dy);
                    bool inDelta = TryGetDeltaProfile(
                        deltaProfiles, neighbor,
                        out AccessHeightProfile neighborProfile);
                    bool inHistory = !inDelta && TryGetProfile(
                        neighbor, out neighborProfile);
                    if (!inDelta && !inHistory) continue;

                    bool cardinal = dx == 0 || dy == 0;
                    if (cardinal && inHistory && !localContext.Contains(neighbor))
                    {
                        reason = "NonlocalEdgeContact";
                        return false;
                    }

                    if (!SharedContactMatches(
                            candidate.Origin, candidate.Profile,
                            neighbor, neighborProfile))
                    {
                        reason = cardinal
                            ? "SharedEdgeMismatch"
                            : "SharedCornerMismatch";
                        return false;
                    }
                }
            }
            reason = string.Empty;
            return true;
        }

        private static bool TryGetDeltaProfile(
            IReadOnlyList<AccessV2OriginProfile> delta,
            Tile2i origin,
            out AccessHeightProfile profile)
        {
            for (int index = 0; index < delta.Count; index++)
            {
                if (delta[index].Origin != origin) continue;
                profile = delta[index].Profile;
                return true;
            }
            profile = default;
            return false;
        }

        private static bool SharedContactMatches(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i neighbor,
            AccessHeightProfile neighborProfile)
        {
            int dx = neighbor.X - origin.X;
            int dy = neighbor.Y - origin.Y;
            if (dx == 4 && dy == 0)
                return AccessPathSearch.EdgesMatch(
                    profile, neighborProfile, new Tile2i(4, 0));
            if (dx == -4 && dy == 0)
                return AccessPathSearch.EdgesMatch(
                    profile, neighborProfile, new Tile2i(-4, 0));
            if (dx == 0 && dy == 4)
                return AccessPathSearch.EdgesMatch(
                    profile, neighborProfile, new Tile2i(0, 4));
            if (dx == 0 && dy == -4)
                return AccessPathSearch.EdgesMatch(
                    profile, neighborProfile, new Tile2i(0, -4));

            return GetCorner(profile, dx > 0 ? 4 : 0, dy > 0 ? 4 : 0)
                == GetCorner(
                    neighborProfile,
                    dx > 0 ? 0 : 4,
                    dy > 0 ? 0 : 4);
        }

        private static int GetCorner(
            AccessHeightProfile profile,
            int x,
            int y)
        {
            if (x == 0 && y == 0) return profile.Nw2;
            if (x == 4 && y == 0) return profile.Ne2;
            if (x == 4 && y == 4) return profile.Se2;
            return profile.Sw2;
        }

        private struct RayEnvelope
        {
            public bool HasCut;
            public float CutCeiling;
            public long CutPriority;
            public bool HasFill;
            public float FillFloor;
            public long FillPriority;
            public bool HasCutSafety;
            public bool HasFillSafety;

            public void Merge(
                RayOwnerEnvelope contribution,
                bool includeSafety)
            {
                if (contribution.HasCut
                    && (!HasCut
                        || contribution.CutCeiling < CutCeiling))
                {
                    HasCut = true;
                    CutCeiling = contribution.CutCeiling;
                }
                if (contribution.HasCut
                    && (!HasCut
                        || contribution.CutPriority > CutPriority))
                    CutPriority = contribution.CutPriority;
                if (contribution.HasFill
                    && (!HasFill
                        || contribution.FillFloor > FillFloor))
                {
                    HasFill = true;
                    FillFloor = contribution.FillFloor;
                }
                if (contribution.HasFill
                    && (!HasFill
                        || contribution.FillPriority > FillPriority))
                    FillPriority = contribution.FillPriority;
                if (includeSafety)
                {
                    HasCutSafety |= contribution.HasCutSafety;
                    HasFillSafety |= contribution.HasFillSafety;
                    if (contribution.HasCutSafety
                        && contribution.CutPriority > CutPriority)
                        CutPriority = contribution.CutPriority;
                    if (contribution.HasFillSafety
                        && contribution.FillPriority > FillPriority)
                        FillPriority = contribution.FillPriority;
                }
            }
        }

        private struct RayOwnerEnvelope
        {
            public Tile2i? Owner;
            public bool HasCut;
            public float CutCeiling;
            public long CutPriority;
            public bool HasFill;
            public float FillFloor;
            public long FillPriority;
            public bool HasCutSafety;
            public bool HasFillSafety;

            public RayOwnerEnvelope(Tile2i? owner)
            {
                Owner = owner;
                HasCut = false;
                CutCeiling = 0f;
                CutPriority = long.MinValue;
                HasFill = false;
                FillFloor = 0f;
                FillPriority = long.MinValue;
                HasCutSafety = false;
                HasFillSafety = false;
            }

            public void Add(
                AccessRayHeightConstraint constraint,
                long priority)
            {
                if (constraint.IsSafetyOnly)
                {
                    if (constraint.Operation == AccessSideRayOperation.Cut)
                    {
                        HasCutSafety = true;
                        if (priority > CutPriority)
                            CutPriority = priority;
                    }
                    else if (constraint.Operation == AccessSideRayOperation.Fill)
                    {
                        HasFillSafety = true;
                        if (priority > FillPriority)
                            FillPriority = priority;
                    }
                    return;
                }
                if (constraint.Operation == AccessSideRayOperation.Cut)
                {
                    if (!HasCut || constraint.Height < CutCeiling)
                        CutCeiling = constraint.Height;
                    HasCut = true;
                    if (priority > CutPriority)
                        CutPriority = priority;
                }
                else if (constraint.Operation == AccessSideRayOperation.Fill)
                {
                    if (!HasFill || constraint.Height > FillFloor)
                        FillFloor = constraint.Height;
                    HasFill = true;
                    if (priority > FillPriority)
                        FillPriority = priority;
                }
            }
        }

        private struct RayTileDelta
        {
            private RayOwnerEnvelope m_first;
            private RayOwnerEnvelope[]? m_additional;
            private int m_count;

            public int ContributionCount => m_count;

            public void Add(
                AccessRayHeightConstraint constraint,
                int sequence,
                int constraintIndex)
            {
                long priority = ((long)sequence << 32) - constraintIndex;
                if (m_count == 0)
                {
                    m_first = new RayOwnerEnvelope(constraint.OwnerOrigin);
                    m_first.Add(constraint, priority);
                    m_count = 1;
                    return;
                }
                if (m_first.Owner == constraint.OwnerOrigin)
                {
                    m_first.Add(constraint, priority);
                    return;
                }
                if (m_additional != null)
                {
                    for (int index = 0;
                        index < m_additional.Length;
                        index++)
                    {
                        RayOwnerEnvelope contribution = m_additional[index];
                        if (contribution.Owner != constraint.OwnerOrigin)
                            continue;
                        contribution.Add(constraint, priority);
                        m_additional[index] = contribution;
                        return;
                    }
                }
                int previousCount = m_additional?.Length ?? 0;
                var expanded = new RayOwnerEnvelope[previousCount + 1];
                if (m_additional != null)
                    Array.Copy(m_additional, expanded, previousCount);
                expanded[previousCount] =
                    new RayOwnerEnvelope(constraint.OwnerOrigin);
                expanded[previousCount].Add(constraint, priority);
                m_additional = expanded;
                m_count++;
            }

            public void MergeInto(
                ref RayEnvelope target,
                IReadOnlyCollection<Tile2i>? excludedOwners)
            {
                MergeContribution(
                    ref target, m_first, excludedOwners);
                if (m_additional == null)
                    return;
                for (int index = 0; index < m_additional.Length; index++)
                    MergeContribution(
                        ref target, m_additional[index], excludedOwners);
            }

            public bool HasContributionOutside(
                IReadOnlyCollection<Tile2i> excludedOwners)
            {
                if (!m_first.Owner.HasValue
                    || !IsExcluded(
                        m_first.Owner.Value, excludedOwners))
                    return true;
                if (m_additional == null)
                    return false;
                for (int index = 0; index < m_additional.Length; index++)
                {
                    Tile2i? owner = m_additional[index].Owner;
                    if (!owner.HasValue
                        || !IsExcluded(owner.Value, excludedOwners))
                        return true;
                }
                return false;
            }

            private static void MergeContribution(
                ref RayEnvelope target,
                RayOwnerEnvelope contribution,
                IReadOnlyCollection<Tile2i>? excludedOwners)
            {
                bool includeSafety = !contribution.Owner.HasValue
                    || !IsExcluded(
                        contribution.Owner.Value, excludedOwners);
                // Connected-predecessor exemptions waive clearance only. Its
                // projected work remains physical ground for termination,
                // conflict detection, and work credit.
                target.Merge(contribution, includeSafety);
            }
        }

        private readonly struct RayEnvelopeCacheKey
            : IEquatable<RayEnvelopeCacheKey>
        {
            private readonly Tile2i m_tile;
            private readonly byte m_excludedCount;
            private readonly Tile2i m_excluded0;
            private readonly Tile2i m_excluded1;

            private RayEnvelopeCacheKey(
                Tile2i tile,
                byte excludedCount,
                Tile2i excluded0,
                Tile2i excluded1)
            {
                m_tile = tile;
                m_excludedCount = excludedCount;
                m_excluded0 = excluded0;
                m_excluded1 = excluded1;
            }

            public static bool TryCreate(
                Tile2i tile,
                IReadOnlyCollection<Tile2i>? excludedOwners,
                out RayEnvelopeCacheKey key)
            {
                Tile2i first = default;
                Tile2i second = default;
                byte count = 0;
                if (excludedOwners != null)
                {
                    foreach (Tile2i owner in excludedOwners)
                    {
                        if (count > 0 && owner == first)
                            continue;
                        if (count > 1 && owner == second)
                            continue;
                        if (count == 0)
                            first = owner;
                        else if (count == 1)
                            second = owner;
                        else
                        {
                            key = default;
                            return false;
                        }
                        count++;
                    }
                }
                if (count == 2 && CompareTiles(second, first) < 0)
                {
                    Tile2i swap = first;
                    first = second;
                    second = swap;
                }
                key = new RayEnvelopeCacheKey(
                    tile, count, first, second);
                return true;
            }

            public bool Equals(RayEnvelopeCacheKey other)
                => m_tile == other.m_tile
                    && m_excludedCount == other.m_excludedCount
                    && (m_excludedCount == 0
                        || m_excluded0 == other.m_excluded0)
                    && (m_excludedCount < 2
                        || m_excluded1 == other.m_excluded1);

            public override bool Equals(object? obj)
                => obj is RayEnvelopeCacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = m_tile.GetHashCode();
                    hash = (hash * 397) ^ m_excludedCount;
                    if (m_excludedCount > 0)
                        hash = (hash * 397) ^ m_excluded0.GetHashCode();
                    if (m_excludedCount > 1)
                        hash = (hash * 397) ^ m_excluded1.GetHashCode();
                    return hash;
                }
            }
        }

        private static bool IsExcluded(
            Tile2i owner,
            IReadOnlyCollection<Tile2i>? excludedOwners)
        {
            if (excludedOwners == null)
                return false;
            foreach (Tile2i excluded in excludedOwners)
                if (owner == excluded)
                    return true;
            return false;
        }

        private static int CompareTiles(Tile2i left, Tile2i right)
        {
            int x = left.X.CompareTo(right.X);
            return x != 0 ? x : left.Y.CompareTo(right.Y);
        }

        private static byte DirectionBit(Tile2i? direction)
        {
            if (!direction.HasValue) return 0;
            Tile2i value = direction.Value;
            if (value.X > 0 && value.Y == 0) return 1;
            if (value.X < 0 && value.Y == 0) return 2;
            if (value.Y > 0 && value.X == 0) return 4;
            if (value.Y < 0 && value.X == 0) return 8;
            return 0;
        }

        private static int CountBits(byte value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= (byte)(value - 1);
                count++;
            }
            return count;
        }

        private static int OriginProfileHash(AccessV2OriginProfile item)
        {
            unchecked
            {
                int hash = item.Origin.GetHashCode();
                hash = (hash * 397) ^ item.Profile.Nw2;
                hash = (hash * 397) ^ item.Profile.Ne2;
                hash = (hash * 397) ^ item.Profile.Se2;
                hash = (hash * 397) ^ item.Profile.Sw2;
                return hash;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    /// <summary>
    /// Persistent parent-plus-delta geometry history. Stage 1 deliberately
    /// stores only origin/profile ownership; cleanup and ray deltas are added
    /// with production costing in Stage 4.
    /// </summary>
    internal sealed class AccessV2History
    {
        public static readonly AccessV2History Empty = new AccessV2History();

        private readonly AccessV2History? m_parent;
        private readonly IReadOnlyList<AccessV2OriginProfile> m_delta;
        private readonly IReadOnlyList<AccessRayHeightConstraint> m_rayDelta;
        private readonly IReadOnlyCollection<string> m_cleanupKeyDelta;
        private HashSet<Tile2i>? m_cachedRayTiles;
        private HashSet<Tile2i>? m_cachedHandoffRayTiles;
        private HashSet<Tile2i>? m_cachedOrigins;

        public int OriginCount { get; }
        public int RayConstraintCount { get; }
        public int CleanupKeyCount { get; }
        public int Signature { get; }

        private AccessV2History()
        {
            m_delta = Array.Empty<AccessV2OriginProfile>();
            m_rayDelta = Array.Empty<AccessRayHeightConstraint>();
            m_cleanupKeyDelta = Array.Empty<string>();
        }

        private AccessV2History(
            AccessV2History parent,
            IReadOnlyList<AccessV2OriginProfile> delta,
            IReadOnlyList<AccessRayHeightConstraint> rayDelta,
            IReadOnlyCollection<string> cleanupKeyDelta)
        {
            m_parent = parent;
            m_delta = delta;
            m_rayDelta = rayDelta;
            m_cleanupKeyDelta = cleanupKeyDelta;
            OriginCount = parent.OriginCount + delta.Count;
            RayConstraintCount = parent.RayConstraintCount + rayDelta.Count;
            CleanupKeyCount = parent.CleanupKeyCount + cleanupKeyDelta.Count;
            int signature = parent.Signature;
            for (int index = 0; index < delta.Count; index++)
                signature ^= OriginProfileHash(delta[index]);
            foreach (string key in cleanupKeyDelta)
                signature ^= StringComparer.Ordinal.GetHashCode(key);
            Signature = signature;
        }

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
                transition.Kind == AccessV2TransitionKind.Turn,
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
                transition.Kind == AccessV2TransitionKind.Turn,
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

            next = ApplyValidated(delta, rayDelta, cleanupKeyDelta);
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
                transition.Kind == AccessV2TransitionKind.Turn, out reason);

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
        {

            var copiedDelta = new AccessV2OriginProfile[delta.Count];
            for (int index = 0; index < delta.Count; index++)
                copiedDelta[index] = delta[index];
            var copiedRays = new AccessRayHeightConstraint[rayDelta.Count];
            for (int index = 0; index < rayDelta.Count; index++)
                copiedRays[index] = rayDelta[index];
            var copiedCleanup = new HashSet<string>(
                cleanupKeyDelta, StringComparer.Ordinal);
            return new AccessV2History(
                this, copiedDelta, copiedRays, copiedCleanup);
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
                    added);
        }

        public bool IsProfileBlockedByRayEnvelope(
            Tile2i origin,
            AccessHeightProfile profile,
            IReadOnlyCollection<Tile2i>? supersededRayOwners,
            out AccessSideRayOperation operation)
        {
            const float epsilon = 0.0001f;
            // Straight continuations supersede the newest lane-owned fringe.
            // Handoff evaluation has already built this filtered tile set for
            // the current label, so most candidates can reject a history scan
            // with only 25 hash lookups.
            if (supersededRayOwners != null)
            {
                IReadOnlyCollection<Tile2i> activeTiles =
                    CollectHandoffRayTiles();
                bool canOverlap = false;
                for (int y = 0; y <= 4 && !canOverlap; y++)
                    for (int x = 0; x <= 4; x++)
                        if (activeTiles.Contains(
                                origin + new RelTile2i(x, y)))
                        {
                            canOverlap = true;
                            break;
                        }
                if (!canOverlap)
                {
                    operation = AccessSideRayOperation.None;
                    return false;
                }
            }
            for (AccessV2History? history = this;
                history != null;
                history = history.m_parent)
            {
                for (int index = 0; index < history.m_rayDelta.Count; index++)
                {
                    AccessRayHeightConstraint constraint = history.m_rayDelta[index];
                    if (constraint.OwnerOrigin.HasValue
                        && supersededRayOwners != null
                        && supersededRayOwners.Contains(
                            constraint.OwnerOrigin.Value))
                        continue;
                    int localX = constraint.Tile.X - origin.X;
                    int localY = constraint.Tile.Y - origin.Y;
                    if (localX < 0 || localX > 4 || localY < 0 || localY > 4)
                        continue;
                    float profileHeight =
                        profile.GetHeight2NumeratorAt(localX, localY) / 32f;
                    if (constraint.Operation == AccessSideRayOperation.Cut
                        && profileHeight > constraint.Height + epsilon)
                    {
                        operation = AccessSideRayOperation.Cut;
                        return true;
                    }
                    if (constraint.Operation == AccessSideRayOperation.Fill
                        && profileHeight < constraint.Height - epsilon)
                    {
                        operation = AccessSideRayOperation.Fill;
                        return true;
                    }
                }
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
                for (int index = 0; index < history.m_rayDelta.Count; index++)
                    result.Add(history.m_rayDelta[index].Tile);
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
                return CollectRayTiles();

            var result = new HashSet<Tile2i>();
            for (AccessV2History? history = this;
                history != null;
                history = history.m_parent)
                for (int index = 0; index < history.m_rayDelta.Count; index++)
                {
                    AccessRayHeightConstraint constraint = history.m_rayDelta[index];
                    if (!constraint.OwnerOrigin.HasValue
                        || !currentOwners.Contains(constraint.OwnerOrigin.Value))
                        result.Add(constraint.Tile);
                }
            m_cachedHandoffRayTiles = result;
            return result;
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

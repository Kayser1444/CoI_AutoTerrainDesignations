using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal enum AccessV2TravelAxis
    {
        X,
        Y,
    }

    internal enum AccessV2BandProfileKind
    {
        Invalid,
        Flat,
        UniformRamp,
        MechanicallyValidDeferred,
    }

    /// <summary>
    /// Concrete profiles for the two origins transverse to V2 travel. Lane 0
    /// is always the lower-coordinate lane on the transverse axis.
    /// </summary>
    internal readonly struct AccessV2BandProfile : IEquatable<AccessV2BandProfile>
    {
        public AccessHeightProfile Lane0 { get; }
        public AccessHeightProfile Lane1 { get; }
        public AccessV2TravelAxis Axis { get; }
        public AccessV2BandProfileKind Kind { get; }

        private AccessV2BandProfile(
            AccessV2TravelAxis axis,
            AccessHeightProfile lane0,
            AccessHeightProfile lane1,
            AccessV2BandProfileKind kind)
        {
            Axis = axis;
            Lane0 = lane0;
            Lane1 = lane1;
            Kind = kind;
        }

        public bool IsEnabled => Kind == AccessV2BandProfileKind.Flat
            || Kind == AccessV2BandProfileKind.UniformRamp;

        public bool IsCompletelyFlat => Kind == AccessV2BandProfileKind.Flat;

        public static bool TryCreate(
            AccessV2TravelAxis axis,
            AccessHeightProfile lane0,
            AccessHeightProfile lane1,
            bool includeDeferred,
            out AccessV2BandProfile band,
            out string reason)
        {
            if (!lane0.HasIntegerCorners || !lane1.HasIntegerCorners)
            {
                band = default;
                reason = "HalfLevelCorner";
                return false;
            }

            Tile2i seamDirection = GetLaneDirection(axis);
            if (!AccessPathSearch.EdgesMatch(lane0, lane1, seamDirection))
            {
                band = default;
                reason = "LaneSeamMismatch";
                return false;
            }

            AccessV2BandProfileKind kind = Classify(axis, lane0, lane1);
            if (kind == AccessV2BandProfileKind.Invalid
                || (!includeDeferred
                    && kind == AccessV2BandProfileKind.MechanicallyValidDeferred))
            {
                band = default;
                reason = kind == AccessV2BandProfileKind.Invalid
                    ? "UnrecognizedLaneProfile"
                    : "DeferredLaneProfilePair";
                return false;
            }

            band = new AccessV2BandProfile(axis, lane0, lane1, kind);
            reason = string.Empty;
            return true;
        }

        public static bool TryCreateEnabled(
            AccessV2TravelAxis axis,
            AccessHeightProfile lane0,
            AccessHeightProfile lane1,
            out AccessV2BandProfile band,
            out string reason)
            => TryCreate(axis, lane0, lane1, false, out band, out reason);

        public bool TryAdvance(
            Tile2i travelDirection,
            out AccessV2BandProfile successor,
            out string reason)
        {
            if (!AccessV2Geometry.IsDirectionAlongAxis(travelDirection, Axis))
            {
                successor = default;
                reason = "DirectionAxisMismatch";
                return false;
            }
            if (!TryGetProfileMode(Lane0, out AccessSearchMode lane0Mode)
                || !TryGetProfileMode(Lane1, out AccessSearchMode lane1Mode)
                || !AccessPathSearch.TrySolveSuccessor(
                    Lane0, travelDirection, lane0Mode,
                    out AccessHeightProfile nextLane0)
                || !AccessPathSearch.TrySolveSuccessor(
                    Lane1, travelDirection, lane1Mode,
                    out AccessHeightProfile nextLane1))
            {
                successor = default;
                reason = "NoStraightSuccessor";
                return false;
            }
            return TryCreateEnabled(
                Axis, nextLane0, nextLane1, out successor, out reason);
        }

        public AccessHeightProfile GetLane(int lane)
        {
            if (lane == 0) return Lane0;
            if (lane == 1) return Lane1;
            throw new ArgumentOutOfRangeException(nameof(lane));
        }

        public static Tile2i GetLaneDirection(AccessV2TravelAxis axis)
            => axis == AccessV2TravelAxis.X
                ? new Tile2i(0, 4)
                : new Tile2i(4, 0);

        public static bool TryGetProfileMode(
            AccessHeightProfile profile,
            out AccessSearchMode mode)
        {
            if (profile.Nw2 == profile.Ne2
                && profile.Nw2 == profile.Se2
                && profile.Nw2 == profile.Sw2)
            {
                mode = AccessSearchMode.Flat;
                return true;
            }
            if (profile.Ne2 == profile.Se2
                && profile.Nw2 == profile.Sw2
                && profile.Ne2 == profile.Nw2 + 2)
            {
                mode = AccessSearchMode.XPositive;
                return true;
            }
            if (profile.Nw2 == profile.Sw2
                && profile.Ne2 == profile.Se2
                && profile.Nw2 == profile.Ne2 + 2)
            {
                mode = AccessSearchMode.XNegative;
                return true;
            }
            if (profile.Sw2 == profile.Se2
                && profile.Nw2 == profile.Ne2
                && profile.Sw2 == profile.Nw2 + 2)
            {
                mode = AccessSearchMode.YPositive;
                return true;
            }
            if (profile.Nw2 == profile.Ne2
                && profile.Sw2 == profile.Se2
                && profile.Nw2 == profile.Sw2 + 2)
            {
                mode = AccessSearchMode.YNegative;
                return true;
            }
            mode = default;
            return false;
        }

        public static bool IsCanonicalVPrime(
            AccessHeightProfile profile)
        {
            int[] corners =
            {
                profile.Nw2,
                profile.Ne2,
                profile.Se2,
                profile.Sw2,
            };
            for (int offsetCorner = 0;
                offsetCorner < corners.Length;
                offsetCorner++)
            {
                int baseHeight2 = corners[(offsetCorner + 1) & 3];
                if (Math.Abs(corners[offsetCorner] - baseHeight2) != 2)
                    continue;
                bool othersMatch = true;
                for (int corner = 0; corner < corners.Length; corner++)
                {
                    if (corner != offsetCorner
                        && corners[corner] != baseHeight2)
                    {
                        othersMatch = false;
                        break;
                    }
                }
                if (othersMatch)
                    return true;
            }
            return false;
        }

        private static AccessV2BandProfileKind Classify(
            AccessV2TravelAxis axis,
            AccessHeightProfile lane0,
            AccessHeightProfile lane1)
        {
            bool lane0IsRouteProfile =
                TryGetProfileMode(lane0, out AccessSearchMode mode0);
            bool lane1IsRouteProfile =
                TryGetProfileMode(lane1, out AccessSearchMode mode1);
            if (!lane0IsRouteProfile || !lane1IsRouteProfile)
                return (lane0IsRouteProfile || IsCanonicalVPrime(lane0))
                    && (lane1IsRouteProfile || IsCanonicalVPrime(lane1))
                        ? AccessV2BandProfileKind.MechanicallyValidDeferred
                        : AccessV2BandProfileKind.Invalid;

            if (mode0 == AccessSearchMode.Flat
                && mode1 == AccessSearchMode.Flat)
                return ProfilesEqual(lane0, lane1)
                    ? AccessV2BandProfileKind.Flat
                    : AccessV2BandProfileKind.MechanicallyValidDeferred;

            bool modeMatchesAxis = axis == AccessV2TravelAxis.X
                ? mode0 == AccessSearchMode.XPositive
                    || mode0 == AccessSearchMode.XNegative
                : mode0 == AccessSearchMode.YPositive
                    || mode0 == AccessSearchMode.YNegative;
            if (mode0 == mode1 && modeMatchesAxis && ProfilesEqual(lane0, lane1))
                return AccessV2BandProfileKind.UniformRamp;

            return AccessV2BandProfileKind.MechanicallyValidDeferred;
        }

        public bool Equals(AccessV2BandProfile other)
            => Axis == other.Axis
                && ProfilesEqual(Lane0, other.Lane0)
                && ProfilesEqual(Lane1, other.Lane1)
                && Kind == other.Kind;

        public override bool Equals(object? obj)
            => obj is AccessV2BandProfile other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Axis;
                hash = (hash * 397) ^ ProfileHash(Lane0);
                hash = (hash * 397) ^ ProfileHash(Lane1);
                hash = (hash * 397) ^ (int)Kind;
                return hash;
            }
        }

        internal static bool ProfilesEqual(
            AccessHeightProfile left,
            AccessHeightProfile right)
            => left.Nw2 == right.Nw2
                && left.Ne2 == right.Ne2
                && left.Se2 == right.Se2
                && left.Sw2 == right.Sw2;

        private static int ProfileHash(AccessHeightProfile profile)
        {
            unchecked
            {
                int hash = profile.Nw2;
                hash = (hash * 397) ^ profile.Ne2;
                hash = (hash * 397) ^ profile.Se2;
                hash = (hash * 397) ^ profile.Sw2;
                return hash;
            }
        }
    }
}

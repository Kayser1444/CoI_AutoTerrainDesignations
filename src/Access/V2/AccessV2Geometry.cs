using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    internal enum AccessV2TransitionKind
    {
        Straight,
        Strafe,
        Turn,
    }

    internal readonly struct AccessV2OriginProfile
    {
        public Tile2i Origin { get; }
        public AccessHeightProfile Profile { get; }

        public AccessV2OriginProfile(Tile2i origin, AccessHeightProfile profile)
        {
            Origin = origin;
            Profile = profile;
        }
    }

    internal readonly struct AccessV2TurnRay
    {
        public Tile2i Source { get; }
        public Tile2i Direction { get; }

        public AccessV2TurnRay(Tile2i source, Tile2i direction)
        {
            Source = source;
            Direction = direction;
        }
    }

    internal readonly struct AccessV2BandState : IEquatable<AccessV2BandState>
    {
        public Tile2i Anchor { get; }
        public AccessV2BandProfile Band { get; }
        public Tile2i EntryDirection { get; }

        public AccessV2TravelAxis Axis => Band.Axis;

        public AccessV2BandState(
            Tile2i anchor,
            AccessV2BandProfile band,
            Tile2i entryDirection)
        {
            if (!AccessV2Geometry.IsOriginAligned(anchor))
                throw new ArgumentException("V2 anchor must be aligned to the four-tile origin grid.", nameof(anchor));
            if (!AccessV2Geometry.IsDirectionAlongAxis(entryDirection, band.Axis))
                throw new ArgumentException("V2 entry direction must follow the travel axis.", nameof(entryDirection));
            Anchor = anchor;
            Band = band;
            EntryDirection = entryDirection;
        }

        public Tile2i GetLaneOrigin(int lane)
        {
            if (lane == 0) return Anchor;
            if (lane == 1)
            {
                Tile2i laneDirection = AccessV2BandProfile.GetLaneDirection(Axis);
                return new Tile2i(
                    Anchor.X + laneDirection.X,
                    Anchor.Y + laneDirection.Y);
            }
            throw new ArgumentOutOfRangeException(nameof(lane));
        }

        public AccessV2OriginProfile GetLane(int lane)
            => new AccessV2OriginProfile(GetLaneOrigin(lane), Band.GetLane(lane));

        public bool Equals(AccessV2BandState other)
            => Anchor == other.Anchor
                && Band.Equals(other.Band)
                && EntryDirection == other.EntryDirection;

        public override bool Equals(object? obj)
            => obj is AccessV2BandState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Anchor.GetHashCode();
                hash = (hash * 397) ^ Band.GetHashCode();
                hash = (hash * 397) ^ EntryDirection.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
            => $"{Axis}@{Anchor}/entry={EntryDirection}/kind={Band.Kind}";
    }

    internal sealed class AccessV2Transition
    {
        public AccessV2TransitionKind Kind { get; }
        public AccessV2BandState Next { get; }
        public IReadOnlyList<AccessV2OriginProfile> Delta { get; }
        public IReadOnlyCollection<Tile2i> LocalContextOrigins { get; }
        public IReadOnlyList<AccessV2TurnRay> OldDirectionTurnRays { get; }
        public AccessHandoffOperation WorkOperation { get; }

        public AccessV2Transition(
            AccessV2TransitionKind kind,
            AccessV2BandState next,
            IReadOnlyList<AccessV2OriginProfile> delta,
            IReadOnlyCollection<Tile2i> localContextOrigins,
            IReadOnlyList<AccessV2TurnRay>? oldDirectionTurnRays = null,
            AccessHandoffOperation workOperation =
                AccessHandoffOperation.Leveling)
        {
            Kind = kind;
            Next = next;
            Delta = delta;
            LocalContextOrigins = localContextOrigins;
            OldDirectionTurnRays = oldDirectionTurnRays
                ?? Array.Empty<AccessV2TurnRay>();
            WorkOperation = workOperation;
        }
    }

    internal static class AccessV2Geometry
    {
        internal static bool IsOriginAligned(Tile2i origin)
            => (origin.X & 3) == 0 && (origin.Y & 3) == 0;

        internal static bool IsDirectionAlongAxis(
            Tile2i direction,
            AccessV2TravelAxis axis)
            => axis == AccessV2TravelAxis.X
                ? Math.Abs(direction.X) == 4 && direction.Y == 0
                : Math.Abs(direction.Y) == 4 && direction.X == 0;

        internal static Tile2i GetCanonicalLaneDirection(AccessV2TravelAxis axis)
            => AccessV2BandProfile.GetLaneDirection(axis);

        internal static AccessV2TravelAxis OtherAxis(AccessV2TravelAxis axis)
            => axis == AccessV2TravelAxis.X
                ? AccessV2TravelAxis.Y
                : AccessV2TravelAxis.X;

        public static bool TryStraight(
            AccessV2BandState current,
            out AccessV2Transition transition,
            out string reason)
        {
            if (!current.Band.TryAdvance(
                    current.EntryDirection,
                    out AccessV2BandProfile nextBand,
                    out reason))
            {
                transition = null!;
                return false;
            }

            Tile2i nextAnchor = Add(current.Anchor, current.EntryDirection);
            var next = new AccessV2BandState(
                nextAnchor, nextBand, current.EntryDirection);
            transition = new AccessV2Transition(
                AccessV2TransitionKind.Straight,
                next,
                new[] { next.GetLane(0), next.GetLane(1) },
                new[] { current.GetLaneOrigin(0), current.GetLaneOrigin(1) });
            reason = string.Empty;
            return true;
        }

        public static IReadOnlyList<AccessV2Transition> EnumerateStraight(
            AccessV2BandState current)
        {
            AccessSearchMode positive = current.Axis == AccessV2TravelAxis.X
                ? AccessSearchMode.XPositive
                : AccessSearchMode.YPositive;
            AccessSearchMode negative = current.Axis == AccessV2TravelAxis.X
                ? AccessSearchMode.XNegative
                : AccessSearchMode.YNegative;
            var modes = new[] { AccessSearchMode.Flat, positive, negative };
            var result = new List<AccessV2Transition>(modes.Length);
            for (int index = 0; index < modes.Length; index++)
            {
                AccessSearchMode mode = modes[index];
                if (!AccessPathSearch.TrySolveSuccessor(
                        current.Band.Lane0, current.EntryDirection, mode,
                        out AccessHeightProfile lane0)
                    || !AccessPathSearch.TrySolveSuccessor(
                        current.Band.Lane1, current.EntryDirection, mode,
                        out AccessHeightProfile lane1)
                    || !AccessV2BandProfile.TryCreateEnabled(
                        current.Axis, lane0, lane1,
                        out AccessV2BandProfile nextBand, out _))
                    continue;
                Tile2i nextAnchor = Add(current.Anchor, current.EntryDirection);
                var next = new AccessV2BandState(
                    nextAnchor, nextBand, current.EntryDirection);
                result.Add(new AccessV2Transition(
                    AccessV2TransitionKind.Straight,
                    next,
                    new[] { next.GetLane(0), next.GetLane(1) },
                    new[]
                    {
                        current.GetLaneOrigin(0),
                        current.GetLaneOrigin(1),
                    }));
            }
            return result;
        }

        /// <summary>
        /// Shifts the band one lane transversely while preserving the previous
        /// longitudinal slice. The newly exposed lane is generated beside both
        /// the current and predecessor slices, producing a complete 2x3 swept
        /// footprint. EntryDirection remains the longitudinal corridor
        /// direction; the lateral movement is transition metadata rather than
        /// a change of travel orientation.
        /// </summary>
        public static bool TryStrafe(
            AccessV2BandState current,
            int transverseSign,
            out AccessV2Transition transition,
            out string reason)
        {
            if (transverseSign != -1 && transverseSign != 1)
            {
                transition = null!;
                reason = "InvalidStrafeSign";
                return false;
            }
            Tile2i reverseDirection = Scale(current.EntryDirection, -1);
            if (!current.Band.TryAdvance(
                    reverseDirection,
                    out AccessV2BandProfile predecessorBand,
                    out reason))
            {
                transition = null!;
                return false;
            }
            int newLane = transverseSign < 0 ? 0 : 1;
            return TryStrafe(
                current, transverseSign, predecessorBand.GetLane(newLane),
                out transition, out reason);
        }

        public static bool TryStrafe(
            AccessV2BandState current,
            int transverseSign,
            AccessHeightProfile predecessorOuterProfile,
            out AccessV2Transition transition,
            out string reason)
        {
            if (transverseSign != -1 && transverseSign != 1)
            {
                transition = null!;
                reason = "InvalidStrafeSign";
                return false;
            }
            if (!current.Band.IsEnabled
                || !AccessV2BandProfile.ProfilesEqual(
                    current.Band.Lane0, current.Band.Lane1))
            {
                transition = null!;
                reason = "StrafeRequiresUniformBand";
                return false;
            }

            Tile2i laneDirection = GetCanonicalLaneDirection(current.Axis);
            Tile2i shift = Scale(laneDirection, transverseSign);
            Tile2i nextAnchor = Add(current.Anchor, shift);
            var next = new AccessV2BandState(
                nextAnchor, current.Band, current.EntryDirection);
            int newLane = transverseSign < 0 ? 0 : 1;
            int retainedCurrentLane = transverseSign < 0 ? 0 : 1;
            AccessV2OriginProfile currentOuter = next.GetLane(newLane);
            var predecessorOuter = new AccessV2OriginProfile(
                Subtract(currentOuter.Origin, current.EntryDirection),
                predecessorOuterProfile);
            transition = new AccessV2Transition(
                AccessV2TransitionKind.Strafe,
                next,
                new[] { predecessorOuter, currentOuter },
                new[]
                {
                    Subtract(
                        current.GetLaneOrigin(retainedCurrentLane),
                        current.EntryDirection),
                    current.GetLaneOrigin(retainedCurrentLane),
                });
            reason = string.Empty;
            return true;
        }

        public static bool TryTurn(
            AccessV2BandState predecessor,
            AccessV2BandState current,
            int transverseSign,
            out AccessV2Transition transition,
            out string reason)
        {
            transition = null!;
            if (transverseSign != -1 && transverseSign != 1)
            {
                reason = "InvalidTurnSign";
                return false;
            }
            if (predecessor.Axis != current.Axis
                || predecessor.EntryDirection != current.EntryDirection
                || predecessor.Anchor != Subtract(
                    current.Anchor, current.EntryDirection))
            {
                reason = "TurnRequiresPredecessorSlice";
                return false;
            }
            if (!predecessor.Band.IsCompletelyFlat
                || !current.Band.IsCompletelyFlat)
            {
                reason = "TurnRequiresFlatLanding";
                return false;
            }
            int landingHeight2 = current.Band.Lane0.Center2;
            if (predecessor.Band.Lane0.Center2 != landingHeight2)
            {
                reason = "TurnLandingHeightMismatch";
                return false;
            }

            Tile2i laneDirection = GetCanonicalLaneDirection(current.Axis);
            Tile2i newDirection = Scale(laneDirection, transverseSign);
            Tile2i exitOffset = transverseSign > 0
                ? Scale(laneDirection, 2)
                : Scale(laneDirection, -1);
            Tile2i exit0 = Add(predecessor.Anchor, exitOffset);
            Tile2i exit1 = Add(current.Anchor, exitOffset);
            Tile2i nextAnchor = CanonicalAnchor(
                OtherAxis(current.Axis), exit0, exit1);

            if (!AccessHeightProfile.TryForMode(
                    AccessSearchMode.Flat, landingHeight2,
                    out AccessHeightProfile flat))
            {
                reason = "TurnLandingHeightInvalid";
                return false;
            }
            if (!AccessV2BandProfile.TryCreateEnabled(
                    OtherAxis(current.Axis), flat, flat,
                    out AccessV2BandProfile nextBand, out reason))
                return false;

            var next = new AccessV2BandState(
                nextAnchor, nextBand, newDirection);
            Tile2i boundaryOffset = transverseSign > 0
                ? laneDirection
                : Tile2i.Zero;
            Tile2i boundary0 = Add(predecessor.Anchor, boundaryOffset);
            Tile2i boundary1 = Add(current.Anchor, boundaryOffset);

            Tile2i forwardFaceBase = IsPositive(current.EntryDirection)
                ? Add(current.Anchor, current.EntryDirection)
                : current.Anchor;
            var rays = new AccessV2TurnRay[3];
            for (int index = 0; index < rays.Length; index++)
                rays[index] = new AccessV2TurnRay(
                    Add(forwardFaceBase, Scale(laneDirection, index)),
                    current.EntryDirection);

            transition = new AccessV2Transition(
                AccessV2TransitionKind.Turn,
                next,
                new[] { next.GetLane(0), next.GetLane(1) },
                new[]
                {
                    predecessor.GetLaneOrigin(0),
                    predecessor.GetLaneOrigin(1),
                    current.GetLaneOrigin(0),
                    current.GetLaneOrigin(1),
                    boundary0,
                    boundary1,
                },
                rays);
            reason = string.Empty;
            return true;
        }

        public static bool IsInsideBounds(
            AccessV2BandState state,
            Tile2i boundsMin,
            Tile2i boundsMax)
        {
            for (int lane = 0; lane < 2; lane++)
            {
                Tile2i origin = state.GetLaneOrigin(lane);
                if (origin.X < boundsMin.X || origin.Y < boundsMin.Y
                    || origin.X + 4 > boundsMax.X
                    || origin.Y + 4 > boundsMax.Y)
                    return false;
            }
            return true;
        }

        public static bool IsInsideBounds(
            AccessV2Transition transition,
            Tile2i boundsMin,
            Tile2i boundsMax)
        {
            if (!IsInsideBounds(transition.Next, boundsMin, boundsMax))
                return false;
            for (int index = 0; index < transition.Delta.Count; index++)
            {
                Tile2i origin = transition.Delta[index].Origin;
                if (origin.X < boundsMin.X || origin.Y < boundsMin.Y
                    || origin.X + 4 > boundsMax.X
                    || origin.Y + 4 > boundsMax.Y)
                    return false;
            }
            return true;
        }

        internal static Tile2i Add(Tile2i left, Tile2i right)
            => new Tile2i(left.X + right.X, left.Y + right.Y);

        internal static Tile2i Subtract(Tile2i left, Tile2i right)
            => new Tile2i(left.X - right.X, left.Y - right.Y);

        internal static Tile2i Scale(Tile2i value, int scale)
            => new Tile2i(value.X * scale, value.Y * scale);

        private static bool IsPositive(Tile2i direction)
            => direction.X > 0 || direction.Y > 0;

        private static Tile2i CanonicalAnchor(
            AccessV2TravelAxis axis,
            Tile2i first,
            Tile2i second)
        {
            if (axis == AccessV2TravelAxis.X)
                return first.Y <= second.Y ? first : second;
            return first.X <= second.X ? first : second;
        }
    }
}

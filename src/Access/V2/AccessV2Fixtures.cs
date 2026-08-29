using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations.Access.V2
{
    /// <summary>
    /// Pure geometry fixture entry point. It has no world or manager
    /// dependencies and is intentionally separate from the fatal V1 gate.
    /// </summary>
    internal static class AccessV2Fixtures
    {
        public static bool ValidateAll(out string failure)
        {
            if (!ValidateProfilePairs(out failure)) return false;
            if (!ValidateStraightAndStrafe(out failure)) return false;
            if (!ValidateTurns(out failure)) return false;
            if (!ValidateHistory(out failure)) return false;
            if (!ValidateGroundGraph(out failure)) return false;
            if (!ValidateSlicedGroundGraphBuild(out failure)) return false;
            if (!ValidateFixedNavigationGraph(out failure)) return false;
            if (!ValidateHandoffs(out failure)) return false;
            if (!ValidateFrontages(out failure)) return false;
            if (!ValidateUsefulHeightEnvelope(out failure)) return false;
            if (!ValidateSearch(out failure)) return false;
            if (!ValidateBounds(out failure)) return false;
            if (!AccessV2TerminalFixtures.ValidateAll(out failure))
                return false;
            failure = string.Empty;
            return true;
        }

        private static bool ValidateProfilePairs(out string failure)
        {
            if (!AccessHeightProfile.TryForMode(
                    AccessSearchMode.Flat, 0,
                    out AccessHeightProfile flat)
                || !AccessHeightProfile.TryForMode(
                    AccessSearchMode.XPositive, 1,
                    out AccessHeightProfile xPositive)
                || !AccessHeightProfile.TryForMode(
                    AccessSearchMode.XNegative, 1,
                    out AccessHeightProfile xNegative)
                || !AccessHeightProfile.TryForMode(
                    AccessSearchMode.YPositive, 1,
                    out AccessHeightProfile yPositive0)
                || !AccessHeightProfile.TryForMode(
                    AccessSearchMode.YPositive, 3,
                    out AccessHeightProfile yPositive1)
                || !AccessHeightProfile.TryForMode(
                    AccessSearchMode.YNegative, 1,
                    out AccessHeightProfile yNegative))
            {
                failure = "Profile templates unavailable";
                return false;
            }

            if (!AccessV2BandProfile.TryCreateEnabled(
                    AccessV2TravelAxis.X, flat, flat,
                    out AccessV2BandProfile flatBand, out _)
                || flatBand.Kind != AccessV2BandProfileKind.Flat)
            {
                failure = "Enabled flat pair rejected";
                return false;
            }
            if (!AccessV2BandProfile.TryCreateEnabled(
                    AccessV2TravelAxis.X, xPositive, xPositive,
                    out AccessV2BandProfile rampBand, out _)
                || rampBand.Kind != AccessV2BandProfileKind.UniformRamp)
            {
                failure = "Enabled uniform ramp pair rejected";
                return false;
            }
            if (AccessV2BandProfile.TryCreateEnabled(
                    AccessV2TravelAxis.X, xPositive, xNegative,
                    out _, out string opposedAlongReason)
                || opposedAlongReason != "LaneSeamMismatch")
            {
                failure = "Opposed along-axis ramps must fight at their seam";
                return false;
            }
            if (AccessV2BandProfile.TryCreateEnabled(
                    AccessV2TravelAxis.X, yPositive0, yPositive1,
                    out _, out string deferredReason)
                || deferredReason != "DeferredLaneProfilePair"
                || !AccessV2BandProfile.TryCreate(
                    AccessV2TravelAxis.X, yPositive0, yPositive1, true,
                    out AccessV2BandProfile deferred, out _)
                || deferred.Kind != AccessV2BandProfileKind.MechanicallyValidDeferred)
            {
                failure = "Compatible transverse pair must remain mechanically known but disabled";
                return false;
            }
            if (AccessV2BandProfile.TryCreateEnabled(
                    AccessV2TravelAxis.X, yPositive0, yNegative,
                    out _, out string opposedReason)
                || opposedReason != "DeferredLaneProfilePair")
            {
                failure = "Compatible opposed transverse pair must remain disabled";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateStraightAndStrafe(out string failure)
        {
            Tile2i[] directions =
            {
                new Tile2i(4, 0),
                new Tile2i(-4, 0),
                new Tile2i(0, 4),
                new Tile2i(0, -4),
            };
            for (int index = 0; index < directions.Length; index++)
            {
                Tile2i direction = directions[index];
                AccessV2TravelAxis axis = direction.X != 0
                    ? AccessV2TravelAxis.X
                    : AccessV2TravelAxis.Y;
                AccessSearchMode rampMode = direction.X > 0
                    ? AccessSearchMode.XPositive
                    : direction.X < 0
                        ? AccessSearchMode.XNegative
                        : direction.Y > 0
                            ? AccessSearchMode.YPositive
                            : AccessSearchMode.YNegative;
                if (!TryCreateUniformState(
                        new Tile2i(20, 20), axis, direction,
                        rampMode, 1, out AccessV2BandState state,
                        out failure))
                    return false;
                if (!AccessV2Geometry.TryStraight(
                        state, out AccessV2Transition straight,
                        out failure)
                    || straight.Delta.Count != 2
                    || straight.Next.Anchor != AccessV2Geometry.Add(
                        state.Anchor, direction)
                    || straight.Next.EntryDirection != direction)
                {
                    failure = "Straight symmetry failed for " + direction;
                    return false;
                }
                if (!AccessV2Geometry.TryStrafe(
                        straight.Next, -1, out AccessV2Transition strafeLow,
                        out failure)
                    || strafeLow.Delta.Count != 2
                    || strafeLow.LocalContextOrigins.Count != 2
                    || !ValidateStrafeFootprint(
                        straight.Next, strafeLow, -1, out failure)
                    || !AccessV2Geometry.TryStrafe(
                        straight.Next, 1, out AccessV2Transition strafeHigh,
                        out failure)
                    || strafeHigh.Delta.Count != 2
                    || !ValidateStrafeFootprint(
                        straight.Next, strafeHigh, 1, out failure)
                    || strafeHigh.Next.EntryDirection != direction)
                {
                    failure = "Strafe symmetry failed for " + direction;
                    return false;
                }

                if (!CreateHistoryForState(state, out AccessV2History history, out failure)
                    || !history.TryApply(straight, out history, out failure)
                    || !history.TryApply(strafeLow, out AccessV2History strafedHistory, out failure)
                    || strafedHistory.OriginCount != 6
                    || !ValidateSweptCorridor(
                        strafedHistory, straight.Next, -1, 3, out failure)
                    || !AccessV2Geometry.TryStrafe(
                        strafeHigh.Next, 1,
                        out AccessV2Transition consecutiveStrafe, out failure)
                    || !history.TryApply(strafeHigh, out AccessV2History firstStrafeHistory, out failure)
                    || !firstStrafeHistory.TryApply(
                        consecutiveStrafe,
                        out AccessV2History secondStrafeHistory,
                        out failure)
                    || secondStrafeHistory.OriginCount != 8
                    || !ValidateSweptCorridor(
                        secondStrafeHistory, straight.Next, 0, 4, out failure)
                    || !AccessV2Geometry.TryStrafe(
                        consecutiveStrafe.Next, 1,
                        out AccessV2Transition thirdStrafe, out failure)
                    || !secondStrafeHistory.TryApply(
                        thirdStrafe,
                        out AccessV2History thirdStrafeHistory,
                        out failure)
                    || thirdStrafeHistory.OriginCount != 10
                    || !ValidateSweptCorridor(
                        thirdStrafeHistory, straight.Next, 0, 5, out failure))
                {
                    failure = "Strafe swept-footprint ownership failed for " + direction
                        + ": " + failure;
                    return false;
                }
            }

            if (!TryCreateUniformState(
                    new Tile2i(24, 24), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState strafeStart, out failure)
                || !AccessV2Geometry.TryStraight(
                    strafeStart, out AccessV2Transition strafeAdvance,
                    out failure)
                || !CreateHistoryForState(
                    strafeStart, out AccessV2History strafeHistory,
                    out failure)
                || !strafeHistory.TryApply(
                    strafeAdvance, out strafeHistory, out failure)
                || !AccessV2Geometry.TryStrafe(
                    strafeAdvance.Next, 1,
                    out AccessV2Transition strafeTurnAdvance,
                    out failure)
                || !strafeHistory.TryApply(
                    strafeTurnAdvance, out strafeHistory, out failure)
                || !AccessV2Geometry.TryTurn(
                    strafeTurnAdvance.Next, strafeHistory, 1,
                    out AccessV2Transition strafeTurn, out failure)
                || !strafeTurn.Next.IsTurnPending
                || strafeTurn.Delta.Count != 0)
            {
                failure = "Turn after a lateral flat strafe was not admitted: "
                    + failure;
                return false;
            }

            if (!TryCreateUniformState(
                    new Tile2i(20, 20), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState flatState, out failure))
                return false;
            AccessV2Transition? flatToRamp =
                AccessV2Geometry.EnumerateStraight(flatState)
                    .FirstOrDefault(item => item.Next.Band.Kind
                        == AccessV2BandProfileKind.UniformRamp);
            if (flatToRamp == null
                || !CreateHistoryForState(
                    flatState, out AccessV2History transitionHistory,
                    out failure)
                || !transitionHistory.TryApply(
                    flatToRamp, out transitionHistory, out failure)
                || !AccessV2Geometry.TryStrafe(
                    flatToRamp.Next, 1, flatState.Band.Lane1,
                    out AccessV2Transition transitionStrafe, out failure)
                || !transitionHistory.TryApply(
                    transitionStrafe, out transitionHistory, out failure)
                || transitionHistory.OriginCount != 6)
            {
                failure = "Strafe did not copy the concrete predecessor profile: "
                    + failure;
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool ValidateSweptCorridor(
            AccessV2History history,
            AccessV2BandState current,
            int firstLaneOffset,
            int laneCount,
            out string failure)
        {
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> profiles =
                history.Flatten();
            Tile2i laneDirection =
                AccessV2Geometry.GetCanonicalLaneDirection(current.Axis);
            for (int slice = -1; slice <= 0; slice++)
            for (int lane = 0; lane < laneCount; lane++)
            {
                Tile2i origin = AccessV2Geometry.Add(
                    AccessV2Geometry.Add(
                        current.Anchor,
                        AccessV2Geometry.Scale(current.EntryDirection, slice)),
                    AccessV2Geometry.Scale(
                        laneDirection, firstLaneOffset + lane));
                if (!profiles.ContainsKey(origin))
                {
                    failure = "Strafe history does not contain a full swept corridor";
                    return false;
                }
            }
            failure = string.Empty;
            return true;
        }

        private static bool ValidateStrafeFootprint(
            AccessV2BandState current,
            AccessV2Transition transition,
            int transverseSign,
            out string failure)
        {
            Tile2i laneDirection =
                AccessV2Geometry.GetCanonicalLaneDirection(current.Axis);
            Tile2i shift = AccessV2Geometry.Scale(
                laneDirection, transverseSign);
            Tile2i expectedNext = AccessV2Geometry.Add(
                current.Anchor, shift);
            if (transition.Next.Anchor != expectedNext)
            {
                failure = "Strafe endpoint changed its longitudinal position";
                return false;
            }

            var actual = new HashSet<Tile2i>(
                transition.Delta.Select(item => item.Origin));
            int newLane = transverseSign < 0 ? 0 : 1;
            Tile2i currentOuter = transition.Next.GetLaneOrigin(newLane);
            Tile2i predecessorOuter = AccessV2Geometry.Subtract(
                currentOuter, current.EntryDirection);
            if (!actual.Remove(currentOuter)
                || !actual.Remove(predecessorOuter))
            {
                failure = "Strafe did not copy the new lane across both slices";
                return false;
            }
            if (actual.Count != 0)
            {
                failure = "Strafe generated an unexpected footprint origin";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool ValidateTurns(out string failure)
        {
            Tile2i[] directions =
            {
                new Tile2i(4, 0),
                new Tile2i(0, 4),
                new Tile2i(-4, 0),
                new Tile2i(0, -4),
            };
            for (int index = 0; index < directions.Length; index++)
            {
                Tile2i direction = directions[index];
                AccessV2TravelAxis axis = direction.X != 0
                    ? AccessV2TravelAxis.X
                    : AccessV2TravelAxis.Y;
                int clockwiseSign = direction.X > 0 || direction.Y < 0 ? 1 : -1;
                if (!TryCreateUniformState(
                        new Tile2i(24, 24), axis, direction,
                        AccessSearchMode.Flat, 0,
                        out AccessV2BandState predecessor,
                        out failure)
                    || !AccessV2Geometry.TryStraight(
                        predecessor, out AccessV2Transition straight,
                        out failure))
                    return false;
                AccessV2BandState current = straight.Next;
                if (!AccessV2Geometry.TryTurn(
                        predecessor, current, clockwiseSign,
                        out AccessV2Transition turn, out failure))
                {
                    failure = "Clockwise turn rejected for " + direction
                        + ": " + failure;
                    return false;
                }
                if (turn.Delta.Count != 0
                    || turn.OldDirectionTurnRays.Count != 3
                    || turn.Next.Axis == axis
                    || !turn.Next.IsTurnPending
                    || AccessV2Geometry.EnumerateStraight(turn.Next)
                        .Any(item => item.Next.Band.Kind
                            != AccessV2BandProfileKind.UniformRamp))
                {
                    failure = "Turn orientation/ray successor contract failed for "
                        + direction;
                    return false;
                }
                AccessSearchMode expectedRampUpMode = turn.Next.Axis
                    == AccessV2TravelAxis.X
                    ? AccessSearchMode.XPositive
                    : AccessSearchMode.YPositive;
                if (!AccessV2Geometry.EnumerateStraight(turn.Next).Any(
                        item => AccessV2BandProfile.TryGetProfileMode(
                            item.Next.Band.Lane0,
                            out AccessSearchMode mode)
                            && mode == expectedRampUpMode))
                {
                    failure = "Ramp-up successor after turn missing for "
                        + direction;
                    return false;
                }
                AccessV2Transition? rampUp =
                    AccessV2Geometry.EnumerateStraight(turn.Next)
                        .FirstOrDefault(item =>
                            AccessV2BandProfile.TryGetProfileMode(
                                item.Next.Band.Lane0,
                                out AccessSearchMode mode)
                                && mode == expectedRampUpMode);
                if (rampUp == null)
                {
                    failure = "Ramp-up successor after turn missing for "
                        + direction;
                    return false;
                }
                Tile2i laneDirection =
                    AccessV2Geometry.GetCanonicalLaneDirection(axis);
                Tile2i rayStep0 = AccessV2Geometry.Subtract(
                    turn.OldDirectionTurnRays[1].Source,
                    turn.OldDirectionTurnRays[0].Source);
                Tile2i rayStep1 = AccessV2Geometry.Subtract(
                    turn.OldDirectionTurnRays[2].Source,
                    turn.OldDirectionTurnRays[1].Source);
                for (int ray = 0; ray < 3; ray++)
                    if (turn.OldDirectionTurnRays[ray].Direction != direction)
                    {
                        failure = "Turn ray direction failed for " + direction;
                        return false;
                    }
                if (rayStep0 != laneDirection || rayStep1 != laneDirection)
                {
                    failure = "Turn ray frontage spacing failed for " + direction;
                    return false;
                }

                if (!CreateHistoryForState(
                        predecessor, out AccessV2History history, out failure)
                    || !history.TryApply(straight, out history, out failure)
                    || !history.TryApply(turn, out history, out failure)
                    || !history.TryApply(
                        rampUp, out history, out failure)
                    || history.OriginCount != 6)
                {
                    failure = "Turn landing history failed for " + direction
                        + ": " + failure;
                    return false;
                }
            }

            if (!TryCreateUniformState(
                    new Tile2i(20, 20), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.XPositive, 1,
                    out AccessV2BandState sloped, out failure)
                || !AccessV2Geometry.TryStraight(
                    sloped, out AccessV2Transition slopedStraight, out failure))
                return false;
            if (AccessV2Geometry.TryTurn(
                    sloped, slopedStraight.Next, 1,
                    out _, out string slopedTurnReason)
                || slopedTurnReason != "TurnRequiresFlatLanding")
            {
                failure = "Sloped turn must require a flat 2x2 landing";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateHistory(out string failure)
        {
            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 0, out AccessHeightProfile flat);
            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 2, out AccessHeightProfile highFlat);

            var first = new AccessV2OriginProfile(new Tile2i(8, 8), flat);
            if (!AccessV2History.Empty.TryApply(
                    new[] { first }, Array.Empty<Tile2i>(),
                    out AccessV2History history, out failure))
                return false;
            if (history.TryApply(
                    new[] { first }, Array.Empty<Tile2i>(),
                    out _, out string revisitReason)
                || revisitReason != "OriginRevisit")
            {
                failure = "Identical origin revisit not rejected";
                return false;
            }
            if (history.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(first.Origin, highFlat),
                    },
                    Array.Empty<Tile2i>(), out _, out string conflictReason)
                || conflictReason != "OriginRevisit")
            {
                failure = "Conflicting origin revisit not rejected";
                return false;
            }
            if (!history.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(new Tile2i(12, 12), flat),
                    },
                    Array.Empty<Tile2i>(), out _, out failure))
            {
                failure = "Compatible diagonal contact rejected: " + failure;
                return false;
            }

            var validatedDelta = new[]
            {
                new AccessV2OriginProfile(new Tile2i(12, 8), flat),
            };
            var validatedContext = new[] { first.Origin };
            if (!history.TryValidateApply(
                    validatedDelta, validatedContext,
                    out string validationReason))
            {
                failure = "Allocation-free V2 history preflight rejected valid geometry: "
                    + validationReason;
                return false;
            }
            AccessV2History validatedHistory = history.ApplyValidated(
                validatedDelta,
                Array.Empty<AccessRayHeightConstraint>(),
                Array.Empty<string>());
            if (validatedHistory.OriginCount != 2
                || !validatedHistory.TryGetProfile(
                    validatedDelta[0].Origin, out AccessHeightProfile applied)
                || !AccessV2BandProfile.ProfilesEqual(
                    applied, validatedDelta[0].Profile)
                || history.TryValidateApply(
                    new[] { first }, Array.Empty<Tile2i>(), out _))
            {
                failure = "Allocation-free V2 history preflight and single commit diverged from TryApply";
                return false;
            }

            if (!history.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(new Tile2i(12, 8), flat),
                    },
                    new[] { first.Origin }, out AccessV2History cornerHistory,
                    out failure)
                || !cornerHistory.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(new Tile2i(12, 12), flat),
                    },
                    new[] { new Tile2i(12, 8) }, out cornerHistory,
                    out failure))
            {
                failure = "Local predecessor contact rejected: " + failure;
                return false;
            }
            if (cornerHistory.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(new Tile2i(8, 12), flat),
                    },
                    new[] { new Tile2i(12, 12) }, out _,
                    out string nonlocalReason)
                || nonlocalReason != "NonlocalEdgeContact")
            {
                failure = "Nonlocal edge contact not rejected";
                return false;
            }

            if (!history.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(new Tile2i(12, 8), flat),
                    },
                    new[] { first.Origin }, out AccessV2History spanHistory,
                    out failure)
                || !spanHistory.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(new Tile2i(16, 8), flat),
                    },
                    new[] { new Tile2i(12, 8), first.Origin },
                    out spanHistory, out failure)
                || spanHistory.OriginCount != 3)
            {
                failure = "Bounded local handoff-span contact rejected: " + failure;
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateBounds(out string failure)
        {
            if (!TryCreateUniformState(
                    new Tile2i(4, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState state, out failure))
                return false;
            if (!AccessV2Geometry.IsInsideBounds(
                    state, new Tile2i(0, 0), new Tile2i(16, 16))
                || AccessV2Geometry.IsInsideBounds(
                    state, new Tile2i(0, 0), new Tile2i(8, 8)))
            {
                failure = "Band bounds must include both complete 4x4 origins";
                return false;
            }
            if (!TryCreateUniformState(
                    new Tile2i(0, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState edgeState, out failure)
                || !AccessV2Geometry.TryStrafe(
                    edgeState, 1,
                    out AccessV2Transition edgeStrafe, out failure))
                return false;
            if (AccessV2Geometry.IsInsideBounds(
                    edgeStrafe, new Tile2i(0, 0), new Tile2i(16, 16)))
            {
                failure = "Strafe bounds omitted the copied predecessor origin";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool ValidateFrontages(out string failure)
        {
            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 0, out AccessHeightProfile flat);
            AccessHeightProfile.TryForMode(
                AccessSearchMode.XPositive, 1,
                out AccessHeightProfile xPositive);
            Tile2i seed = new Tile2i(8, 8);
            Tile2i boundsMin = Tile2i.Zero;
            Tile2i boundsMax = new Tile2i(32, 32);

            var oneWide = new Dictionary<Tile2i, AccessHeightProfile>
            {
                [seed] = flat,
            };
            AccessV2EndpointSet synthetic = AccessV2FrontageDiscovery.Build(
                boundsMin, boundsMax, oneWide, new[] { seed });
            if (synthetic.Starts.Count == 0)
            {
                failure = "One-origin flat source must enumerate V2 source launches";
                return false;
            }

            oneWide[seed] = xPositive;
            AccessV2EndpointSet ramp = AccessV2FrontageDiscovery.Build(
                boundsMin, boundsMax, oneWide, new[] { seed });
            if (ramp.Starts.Count == 0
                || ramp.Starts.Any(start =>
                    start.State.Axis != AccessV2TravelAxis.X))
            {
                failure = "One-origin ramp source must launch only along its enabled profile axis";
                return false;
            }

            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 2,
                out AccessHeightProfile raisedFlat);
            oneWide[seed] = raisedFlat;
            AccessV2EndpointSet raised = AccessV2FrontageDiscovery.Build(
                boundsMin, boundsMax, oneWide, new[] { seed });
            IReadOnlyList<AccessV2StartFrontage> downLaunches =
                raised.Starts.Where(start =>
                    start.IsSourceLaunch
                    && start.InitialTransition?.Delta.Count == 1
                    && start.InitialTransition.Delta[0].Profile.Center2 == 2
                    && start.LaunchSuccessor?.Delta.Count == 2
                    && start.LaunchSuccessor.Next.Band.Lane0.Center2 == 1
                    && start.LaunchSuccessor.Next.Band.Lane1.Center2 == 1)
                .ToList();
            if (downLaunches.Count != 8)
            {
                failure = "Raised one-origin source must enumerate all eight flat-companion/down-ramp launches";
                return false;
            }

            Tile2i centerLeft = new Tile2i(8, 20);
            Tile2i centerRight = new Tile2i(12, 20);
            Tile2i outerLeft = new Tile2i(4, 20);
            Tile2i outerRight = new Tile2i(16, 20);
            var tierProfiles = new Dictionary<Tile2i, AccessHeightProfile>
            {
                [outerLeft] = flat,
                [centerLeft] = flat,
                [centerRight] = flat,
                [outerRight] = flat,
            };
            AccessV2EndpointSet tiered = AccessV2FrontageDiscovery.Build(
                boundsMin, boundsMax, tierProfiles,
                new[] { outerRight, centerLeft, outerLeft, centerRight });
            if (tiered.StartTiers.Count != 2
                || !tiered.StartTiers[0]
                    .Select(start => start.FixedSeedOrigin)
                    .Distinct()
                    .OrderBy(origin => origin.X)
                    .SequenceEqual(new[] { centerLeft, centerRight })
                || !tiered.StartTiers[1]
                    .Select(start => start.FixedSeedOrigin)
                    .Distinct()
                    .OrderBy(origin => origin.X)
                    .SequenceEqual(new[] { outerLeft, outerRight }))
            {
                failure = "Source launch tiers must retain every arithmetic-center tie before outer roots";
                return false;
            }

            Tile2i paired = new Tile2i(8, 12);
            var fixedPair = new Dictionary<Tile2i, AccessHeightProfile>
            {
                [seed] = flat,
                [paired] = flat,
            };
            AccessV2EndpointSet existing = AccessV2FrontageDiscovery.Build(
                boundsMin, boundsMax, fixedPair, new[] { seed, paired });
            if (!existing.Starts.Any(start =>
                    start.IsSourceLaunch
                    && start.InitialTransition == null))
            {
                failure = "Existing compatible companion must be reusable in a source launch";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateGroundGraph(out string failure)
        {
            Tile2i cleanupA = new Tile2i(1, 0);
            Tile2i cleanupB = new Tile2i(2, 0);
            Tile2i unrelatedDebris = new Tile2i(3, 0);
            Tile2i blocked = new Tile2i(4, 0);
            Tile2i treeNw = new Tile2i(20, 20);
            Tile2i treeNe = new Tile2i(21, 20);
            Tile2i treeSw = new Tile2i(20, 21);
            Tile2i treeSe = new Tile2i(21, 21);
            var cleanupByTile = new Dictionary<Tile2i, AccessPropCleanupInfo>
            {
                [cleanupA] = AccessPropCleanupPolicy.BuildOriginInfo(
                    new Tile2i(0, 0),
                    new[]
                    {
                        new AccessPropSample(
                            cleanupA, false, true, true, "prop:shared"),
                    }),
                [cleanupB] = AccessPropCleanupPolicy.BuildOriginInfo(
                    new Tile2i(0, 0),
                    new[]
                    {
                        new AccessPropSample(
                            cleanupB, false, true, true, "prop:shared"),
                    }),
                [unrelatedDebris] = AccessPropCleanupPolicy.BuildOriginInfo(
                    new Tile2i(0, 0),
                    new[]
                    {
                        new AccessPropSample(
                            unrelatedDebris, false, true, true, "prop:other"),
                    }),
                [blocked] = AccessPropCleanupPolicy.BuildOriginInfo(
                    new Tile2i(4, 0),
                    new[]
                    {
                        new AccessPropSample(
                            blocked, false, true, true, "prop:blocked"),
                    },
                    AccessPropBlockerKind.Building),
                [treeNw] = TreeCenter(treeNw, "tree:nw"),
                [treeNe] = TreeCenter(treeNe, "tree:ne"),
                [treeSw] = TreeCenter(treeSw, "tree:sw"),
                [treeSe] = TreeCenter(treeSe, "tree:se"),
            };
            var graph = new AccessV2GroundGraph(
                new[] { new Tile2i(0, 0), new Tile2i(10, 10) },
                new[] { new Tile2i(0, 0) },
                cleanupByTile);
            if (!graph.IsGround(new Tile2i(0, 0))
                || !graph.IsCleanupGround(cleanupA)
                || graph.IsCleanupGround(blocked)
                || !graph.CanTraverse(new Tile2i(0, 0), cleanupA)
                || !graph.CanTraverse(cleanupA, cleanupB)
                || !graph.CanTraverse(cleanupB, unrelatedDebris))
            {
                failure = "G traversal must cross adjacent footprints of independently removable debris";
                return false;
            }
            if (!graph.TryValidateLocalEscape(
                    new[] { cleanupA }, AccessV2History.Empty,
                    cleanupCostScale: 1f,
                    out IReadOnlyCollection<string> firstDebrisKeys,
                    out float firstDebrisCost)
                || firstDebrisKeys.Count != 1
                || !firstDebrisKeys.Contains("prop:shared")
                || Math.Abs(firstDebrisCost - 8f) > 0.0001f)
            {
                failure = "Entering removable debris ground must charge its cleanup object exactly once";
                return false;
            }
            AccessV2History sharedDebrisCleared = AccessV2History.Empty
                .ApplyCleanupKeys(firstDebrisKeys);
            if (!graph.TryValidateLocalEscape(
                    new[] { cleanupB }, sharedDebrisCleared,
                    cleanupCostScale: 1f,
                    out IReadOnlyCollection<string> repeatedDebrisKeys,
                    out float repeatedDebrisCost)
                || repeatedDebrisKeys.Count != 0
                || Math.Abs(repeatedDebrisCost) > 0.0001f
                || !graph.TryValidateLocalEscape(
                    new[] { unrelatedDebris }, sharedDebrisCleared,
                    cleanupCostScale: 1f,
                    out IReadOnlyCollection<string> independentDebrisKeys,
                    out float independentDebrisCost)
                || independentDebrisKeys.Count != 1
                || !independentDebrisKeys.Contains("prop:other")
                || Math.Abs(independentDebrisCost - 8f) > 0.0001f)
            {
                failure = "Adjacent removable debris must retain independent deduplicated cleanup costs";
                return false;
            }
            Tile2i handoffPropCenter = new Tile2i(6, 0);
            Tile2i handoffEscapeGround = new Tile2i(7, 0);
            const string handoffPropKey = "prop:handoff";
            AccessPropCleanupInfo handoffProp =
                AccessPropCleanupPolicy.BuildOriginInfo(
                    new Tile2i(4, 0),
                    new[]
                    {
                        new AccessPropSample(
                            handoffPropCenter,
                            false, true, true, handoffPropKey),
                    },
                    AccessPropBlockerKind.UnderlyingTerrain);
            var handoffPropGraph = new AccessV2GroundGraph(
                new[] { handoffEscapeGround },
                new[] { handoffEscapeGround },
                new Dictionary<Tile2i, AccessPropCleanupInfo>
                {
                    [handoffPropCenter] = handoffProp,
                });
            AccessV2History handoffClearedHistory =
                AccessV2History.Empty.ApplyCleanupKeys(
                    new[] { handoffPropKey });
            if (handoffPropGraph.IsTraversable(handoffPropCenter)
                || handoffPropGraph.IsTraversable(
                    handoffPropCenter, AccessV2History.Empty)
                || !handoffPropGraph.IsTraversable(
                    handoffPropCenter, handoffClearedHistory)
                || !handoffPropGraph.CanTraverse(
                    handoffPropCenter, handoffEscapeGround,
                    handoffClearedHistory)
                || !handoffPropGraph.TryValidateLocalEscape(
                    new[] { handoffPropCenter, handoffEscapeGround },
                    handoffClearedHistory, cleanupCostScale: 9f,
                    out IReadOnlyCollection<string> handoffCleanupKeys,
                    out float handoffCleanupCost)
                || handoffCleanupKeys.Count != 0
                || Math.Abs(handoffCleanupCost) > 0.0001f)
            {
                failure = "Mining/leveling handoff work must clear an intersecting non-tree prop for the local G escape";
                return false;
            }
            IReadOnlyList<Tile2i> treeDiagonalCenters =
                AccessV2GroundGraph.GetSweptCenters(treeNw, treeSe);
            if (!graph.CanTraverse(treeNw, treeSe)
                || !graph.TryValidateLocalEscape(
                    treeDiagonalCenters, AccessV2History.Empty,
                    cleanupCostScale: 9f,
                    out IReadOnlyCollection<string> treeCleanupKeys,
                    out float treeCleanupCost)
                || treeCleanupKeys.Count != 3
                || Math.Abs(treeCleanupCost) > 0.0001f)
            {
                failure = "Tree G diagonal must record both orthogonal corridors at zero cost";
                return false;
            }
            AccessPropCleanupInfo blockedTreeOrigin =
                AccessPropCleanupInfo.HardBlocked(
                    new Tile2i(20, 20),
                    AccessPropBlockerKind.UnderlyingTerrain);
            if (blockedTreeOrigin.IsEligible
                || !global::AutoTerrainDesignations.Access.AccessHandoffEvaluator
                    .IsExperimentalAccessGroundOrCleanupCenter(
                        new HashSet<Tile2i>(),
                        new Dictionary<Tile2i, AccessPropCleanupInfo>
                        {
                            [treeNw] = TreeCenter(treeNw, "tree:perimeter"),
                        },
                        treeNw))
            {
                failure = "V/G seam must use eligible tree center, not blocked origin aggregate";
                return false;
            }
            HashSet<Tile2i> reached = graph.Flood(new Tile2i(0, 0));
            if (!reached.Contains(cleanupB)
                || !reached.Contains(unrelatedDebris)
                || graph.Flood(new Tile2i(10, 10)).Count != 1)
            {
                failure = "G flood must connect adjacent independently removable debris without connecting isolated ground";
                return false;
            }
            var openGround = new List<Tile2i>();
            for (int y = 0; y <= 2; y++)
                for (int x = 0; x <= 2; x++)
                    openGround.Add(new Tile2i(x, y));
            var diagonalGraph = new AccessV2GroundGraph(
                openGround,
                new[] { new Tile2i(2, 2) },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());

            var shortcutGround = new AccessV2GroundGraph(
                new[]
                {
                    new Tile2i(0, 0), new Tile2i(1, 0),
                    new Tile2i(2, 0), new Tile2i(2, 1),
                    new Tile2i(3, 1), new Tile2i(4, 1),
                    new Tile2i(4, 0), new Tile2i(10, 0),
                },
                Array.Empty<Tile2i>(),
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            AccessV2PotentialOwner shortcutOwner =
                AccessV2PotentialOwner.FromGround(
                    shortcutGround, new Tile2i(0, 0));
            AccessV2PotentialOwner stillCommitted = shortcutOwner.Advance(
                shortcutGround, new Tile2i(0, 0), new Tile2i(2, 0));
            AccessV2PotentialOwner afterShortcut = stillCommitted.Advance(
                shortcutGround, new Tile2i(2, 0), new Tile2i(4, 0));
            if (shortcutOwner.IsGlobal
                || stillCommitted.IsGlobal
                || stillCommitted.CanReturnTo(
                    shortcutGround, new Tile2i(4, 0))
                || !stillCommitted.CanReturnTo(
                    shortcutGround, new Tile2i(10, 0))
                || !afterShortcut.IsGlobal
                || !afterShortcut.CanReturnTo(
                    shortcutGround, new Tile2i(4, 0)))
            {
                failure = "V commitment must suppress only a pre-shortcut return to its source G component and must restore a same-component return after crossing a non-G-equivalent center edge";
                return false;
            }
            if (!diagonalGraph.CanTraverse(
                    new Tile2i(0, 0), new Tile2i(1, 1))
                || !diagonalGraph.TryGetGoalDistance(
                    new Tile2i(0, 0), out float diagonalDistance)
                || Math.Abs(diagonalDistance
                    - 2f * AccessV2GroundGraph.DiagonalCost) > 0.0001f)
            {
                failure = "Mega ground graph must use conservative octile travel";
                return false;
            }
            var cornerBlockedGraph = new AccessV2GroundGraph(
                openGround.Where(tile => tile != new Tile2i(1, 0)),
                new[] { new Tile2i(2, 2) },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            if (cornerBlockedGraph.CanTraverse(
                    new Tile2i(0, 0), new Tile2i(1, 1)))
            {
                failure = "Mega diagonal ground edge must not cut a blocked corner";
                return false;
            }

            // A tree-shadow center is admitted by the game's terrain-only Mega
            // mask before the immutable graph is built. Search must not erase
            // that captured decision with a second, approximate static slope
            // scan. Generated V history touching the footprint still requires
            // projected-height validation.
            Tile2i treeOrigin = new Tile2i(4, 4);
            Tile2i treeCenter = new Tile2i(6, 6);
            AccessPropCleanupInfo treeCleanup =
                AccessPropCleanupPolicy.BuildOriginInfo(
                    treeOrigin,
                    new[]
                    {
                        new AccessPropSample(
                            treeCenter, true, false, true, "tree:fixture"),
                    });
            var treeHeights2 = new Dictionary<Tile2i, int>();
            var treePreciseHeights = new Dictionary<Tile2i, float>();
            for (int y = 0; y <= 12; y++)
                for (int x = 0; x <= 12; x++)
                {
                    var tile = new Tile2i(x, y);
                    treeHeights2[tile] = 0;
                    treePreciseHeights[tile] = 0f;
                }
            // Deliberately disagree with the approximate slope scan: the graph
            // represents the authoritative mask result captured from the game.
            treePreciseHeights[new Tile2i(7, 6)] = 2f;
            var treeSnapshot = new AccessSearchSnapshot(
                Tile2i.Zero, new Tile2i(12, 12), new Tile2i(10, 10),
                -2, 2, true, true, false, 1f, 1f,
                treeHeights2,
                new Dictionary<Tile2i, int>(),
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>(),
                propCleanupByOrigin:
                    new Dictionary<Tile2i, AccessPropCleanupInfo>
                    {
                        [treeOrigin] = treeCleanup,
                    },
                preciseTerrainHeights: treePreciseHeights,
                physicalTerrainMin: Tile2i.Zero,
                physicalTerrainMax: new Tile2i(12, 12),
                vehicleWidth: 5,
                vehicleMaxSteepnessDelta: 0.1f);
            if (treeSnapshot.V2GroundGraph == null
                || !treeSnapshot.V2GroundGraph.IsCleanupGround(treeCenter)
                || !treeSnapshot.IsProjectedV2CenterPathable(
                    treeCenter, AccessV2History.Empty))
            {
                failure = "Captured terrain-only tree center must remain valid G";
                return false;
            }
            if (!AccessHeightProfile.TryForMode(
                    AccessSearchMode.XPositive, 1,
                    out AccessHeightProfile generatedRamp))
            {
                failure = "Tree-center projected ramp profile unavailable";
                return false;
            }
            if (!AccessV2History.Empty.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(treeOrigin, generatedRamp),
                    },
                    Array.Empty<Tile2i>(),
                    out AccessV2History generatedHistory,
                    out string generatedHistoryReason))
            {
                failure = "Tree-center projected-history fixture failed: "
                    + generatedHistoryReason;
                return false;
            }
            if (treeSnapshot.IsProjectedV2CenterPathable(
                    treeCenter, generatedHistory))
            {
                failure = "Generated V history must still revalidate a G tree center";
                return false;
            }

            Tile2i generatedPropOrigin = new Tile2i(4, 4);
            Tile2i generatedPropCenter = new Tile2i(6, 6);
            const string generatedPropKey = "prop:8,6";
            AccessPropCleanupInfo generatedPropCleanup =
                AccessPropCleanupPolicy.BuildOriginInfo(
                    generatedPropOrigin,
                    new[]
                    {
                        new AccessPropSample(
                            generatedPropCenter,
                            false, true, true, generatedPropKey,
                            new[] { new Tile2i(8, 4) },
                            dumpBurialProbeTile: generatedPropCenter,
                            placedHeight: 0f,
                            dumpBurialThreshold: 0.5f),
                    },
                    AccessPropBlockerKind.UnderlyingTerrain);
            var propHeights2 = new Dictionary<Tile2i, int>();
            var propPreciseHeights = new Dictionary<Tile2i, float>();
            for (int y = 0; y <= 12; y++)
                for (int x = 0; x <= 12; x++)
                {
                    var tile = new Tile2i(x, y);
                    propHeights2[tile] = 0;
                    propPreciseHeights[tile] = 0f;
                }
            var propSnapshot = new AccessSearchSnapshot(
                Tile2i.Zero, new Tile2i(12, 12), new Tile2i(10, 10),
                -2, 2, true, true, false, 1f, 1f,
                propHeights2,
                new Dictionary<Tile2i, int>(),
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                new[] { new Tile2i(10, 10) },
                new[] { new Tile2i(10, 10) },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>(),
                propCleanupByOrigin:
                    new Dictionary<Tile2i, AccessPropCleanupInfo>
                    {
                        [generatedPropOrigin] = generatedPropCleanup,
                    },
                preciseTerrainHeights: propPreciseHeights,
                physicalTerrainMin: Tile2i.Zero,
                physicalTerrainMax: new Tile2i(12, 12),
                vehicleWidth: 5,
                propCleanupByTile:
                    new Dictionary<Tile2i, AccessPropCleanupInfo>
                    {
                        [generatedPropCenter] = generatedPropCleanup,
                    });
            string generatedPropHistoryReason = string.Empty;
            if (!AccessHeightProfile.TryForMode(
                    AccessSearchMode.Flat, 0,
                    out AccessHeightProfile generatedPropFlat)
                || !AccessV2History.Empty.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(
                            generatedPropOrigin, generatedPropFlat),
                    },
                    Array.Empty<Tile2i>(),
                    out AccessV2History generatedPropHistory,
                    out generatedPropHistoryReason))
            {
                failure = "Generated-prop handoff history fixture failed: "
                    + generatedPropHistoryReason;
                return false;
            }
            generatedPropHistory = generatedPropHistory.ApplyCleanupKeys(
                new[] { generatedPropKey });
            if (propSnapshot.IsProjectedV2CenterPathable(
                    generatedPropCenter, AccessV2History.Empty)
                || !propSnapshot.IsProjectedV2CenterPathable(
                    generatedPropCenter, generatedPropHistory))
            {
                failure = "Projected Mega pathability must treat a generated-handoff prop as cleared only after its cleanup key is owned";
                return false;
            }

            string handoffLevelReason = string.Empty;
            string handoffCutReason = string.Empty;
            string handoffFillReason = string.Empty;
            if (!AccessHeightProfile.TryForMode(
                    AccessSearchMode.Flat, -2,
                    out AccessHeightProfile handoffCut)
                || !AccessHeightProfile.TryForMode(
                    AccessSearchMode.Flat, 2,
                    out AccessHeightProfile handoffFill)
                || !AccessV2History.Empty.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(
                            generatedPropOrigin, generatedPropFlat),
                    },
                    Array.Empty<Tile2i>(),
                    out AccessV2History handoffLevelHistory,
                    out handoffLevelReason)
                || !AccessV2History.Empty.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(
                            generatedPropOrigin, handoffCut),
                    },
                    Array.Empty<Tile2i>(),
                    out AccessV2History handoffCutHistory,
                    out handoffCutReason)
                || !AccessV2History.Empty.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(
                            generatedPropOrigin, handoffFill),
                    },
                    Array.Empty<Tile2i>(),
                    out AccessV2History handoffFillHistory,
                    out handoffFillReason))
            {
                failure = "V2 post-work operation fixture history failed: "
                    + handoffLevelReason + "/"
                    + handoffCutReason + "/" + handoffFillReason;
                return false;
            }
            if (!propSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Leveling,
                    generatedPropCenter, handoffLevelHistory)
                || propSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Mining,
                    generatedPropCenter, handoffLevelHistory)
                || propSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Dumping,
                    generatedPropCenter, handoffLevelHistory)
                || !propSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Mining,
                    generatedPropCenter, handoffCutHistory)
                || !propSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Dumping,
                    generatedPropCenter, handoffFillHistory))
            {
                failure = "V2 post-work operation classifier must distinguish unconditional leveling, vanilla terrain, cut work, and fill work";
                return false;
            }

            AccessPropCleanupInfo eligibleDenseCleanup =
                AccessPropCleanupPolicy.BuildOriginInfo(
                    generatedPropOrigin,
                    new[]
                    {
                        new AccessPropSample(
                            generatedPropCenter,
                            false, true, true, generatedPropKey),
                    });
            var eligibleDenseSnapshot = new AccessSearchSnapshot(
                Tile2i.Zero, new Tile2i(12, 12), new Tile2i(10, 10),
                -2, 2, true, true, false, 1f, 1f,
                propHeights2,
                new Dictionary<Tile2i, int>(),
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>(),
                propCleanupByOrigin:
                    new Dictionary<Tile2i, AccessPropCleanupInfo>
                    {
                        [generatedPropOrigin] = eligibleDenseCleanup,
                    },
                preciseTerrainHeights: propPreciseHeights,
                physicalTerrainMin: Tile2i.Zero,
                physicalTerrainMax: new Tile2i(12, 12),
                vehicleWidth: 5,
                propCleanupByTile:
                    new Dictionary<Tile2i, AccessPropCleanupInfo>
                    {
                        [generatedPropCenter] = eligibleDenseCleanup,
                    });
            if (!eligibleDenseSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Dumping,
                    generatedPropCenter, handoffLevelHistory)
                || !eligibleDenseSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Dumping,
                    generatedPropCenter, generatedPropHistory)
                || !eligibleDenseSnapshot.IsV2HandoffCorridorCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Dumping,
                    generatedPropCenter, handoffLevelHistory,
                    new[] { generatedPropOrigin })
                || !eligibleDenseSnapshot.IsV2HandoffGroundEntryPathable(
                    generatedPropCenter,
                    new[] { generatedPropOrigin },
                    handoffLevelHistory))
            {
                failure = "V2 handoff feasibility must assume intrinsically removable debris can be cleared without a terrain cleanup origin";
                return false;
            }

            if (!AccessV2History.Empty.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(
                            generatedPropOrigin, generatedPropFlat),
                        new AccessV2OriginProfile(
                            new Tile2i(8, 4), generatedPropFlat),
                    },
                    Array.Empty<Tile2i>(),
                    out AccessV2History occupiedSideHistory,
                    out string occupiedSideReason))
            {
                failure = "V2 occupied side-cleanup fixture history failed: "
                    + occupiedSideReason;
                return false;
            }
            occupiedSideHistory = occupiedSideHistory.ApplyCleanupKeys(
                new[] { generatedPropKey });
            if (!eligibleDenseSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Dumping,
                    generatedPropCenter, occupiedSideHistory))
            {
                failure = "V2 dumping handoff must not make removable debris depend on a neighboring cleanup origin";
                return false;
            }
            if (!eligibleDenseSnapshot.IsV2HandoffGroundEntryPathable(
                    generatedPropCenter,
                    Array.Empty<Tile2i>(), generatedPropHistory)
                || !eligibleDenseSnapshot.IsV2HandoffGroundEntryPathable(
                    generatedPropCenter,
                    Array.Empty<Tile2i>(), occupiedSideHistory)
                || !eligibleDenseSnapshot.IsV2HandoffGroundEntryPathable(
                    generatedPropCenter,
                    new[] { new Tile2i(8, 4) }, occupiedSideHistory))
            {
                failure = "V2 G entry must treat removable debris as provisionally cleared independently of cleanup-origin ownership";
                return false;
            }

            const string sameOriginPropKey = "prop:6,6";
            AccessPropCleanupInfo sameOriginDenseCleanup =
                AccessPropCleanupPolicy.BuildOriginInfo(
                    generatedPropOrigin,
                    new[]
                    {
                        new AccessPropSample(
                            generatedPropCenter,
                            false, true, true, sameOriginPropKey,
                            new[] { generatedPropOrigin }),
                    });
            var sameOriginDenseSnapshot = new AccessSearchSnapshot(
                Tile2i.Zero, new Tile2i(12, 12), new Tile2i(10, 10),
                -2, 2, true, true, false, 1f, 1f,
                propHeights2,
                new Dictionary<Tile2i, int>(),
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>(),
                propCleanupByOrigin:
                    new Dictionary<Tile2i, AccessPropCleanupInfo>
                    {
                        [generatedPropOrigin] = sameOriginDenseCleanup,
                    },
                preciseTerrainHeights: propPreciseHeights,
                physicalTerrainMin: Tile2i.Zero,
                physicalTerrainMax: new Tile2i(12, 12),
                vehicleWidth: 5,
                propCleanupByTile:
                    new Dictionary<Tile2i, AccessPropCleanupInfo>
                    {
                        [generatedPropCenter] = sameOriginDenseCleanup,
                    });
            if (!sameOriginDenseSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Dumping,
                    generatedPropCenter,
                    handoffLevelHistory.ApplyCleanupKeys(
                        new[] { sameOriginPropKey })))
            {
                failure = "V2 dumping handoff must allow player-assisted removal when cleanup shares the generated origin";
                return false;
            }
            if (!sameOriginDenseSnapshot.IsV2HandoffGroundEntryPathable(
                    generatedPropCenter,
                    Array.Empty<Tile2i>(),
                    handoffLevelHistory.ApplyCleanupKeys(
                        new[] { sameOriginPropKey })))
            {
                failure = "V2 G entry must allow intrinsically removable same-origin debris";
                return false;
            }

            if (!AccessV2History.Empty.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(
                            treeOrigin, generatedPropFlat),
                    },
                    Array.Empty<Tile2i>(),
                    out AccessV2History treeHandoffHistory,
                    out string treeHandoffReason)
                || !treeSnapshot.IsV2HandoffCenterPathable(
                    treeOrigin, AccessHandoffOperation.Dumping,
                    treeCenter, treeHandoffHistory))
            {
                failure = "V2 dumping handoff must ignore trees in its vanilla post-work pathability test: "
                    + treeHandoffReason;
                return false;
            }

            var disconnectedGround = new[]
            {
                new Tile2i(0, 0), new Tile2i(1, 0), new Tile2i(2, 0),
                new Tile2i(10, 0), new Tile2i(11, 0),
            };
            var disconnectedGraph = new AccessV2GroundGraph(
                disconnectedGround,
                new[] { new Tile2i(10, 0) },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            var sparseOrigins = new[]
            {
                new Tile2i(0, 0),
                new Tile2i(4, 0),
                new Tile2i(8, 0),
            };
            var disconnectedVPotential = new AccessV2PotentialField(
                Tile2i.Zero, new Tile2i(8, 0),
                sparseOrigins,
                Array.Empty<Tile2i>(),
                disconnectedGraph,
                fixedNavigation: null,
                generatedFixedCost: 5f,
                centerSpokeCost: 4f);
            var disconnectedEscapePotential =
                new AccessV2GroundEscapePotentialField(
                    disconnectedGraph, disconnectedVPotential);
            var blockedEscapePotential =
                new AccessV2GroundEscapePotentialField(
                    disconnectedGraph, disconnectedVPotential,
                    canExitToGeneratedV:
                        tile => tile == new Tile2i(0, 0));
            IReadOnlyList<AccessV2PotentialSample> potentialSamples =
                disconnectedVPotential.GetDiagnosticSamples();
            if (disconnectedGraph.TryGetGoalDistance(
                    new Tile2i(0, 0), out _)
                || disconnectedVPotential.NodeCount != 3
                || potentialSamples.Count != 3
                || potentialSamples.Any(sample => !sample.IsGenerated)
                || !potentialSamples.Any(sample =>
                    sample.Center == new Tile2i(2, 2)
                    && Math.Abs(sample.Cost - 22f) < 0.0001f)
                || Math.Abs(disconnectedVPotential.GetPotential(
                    new Tile2i(8, 0)) - 4f) > 0.0001f
                || Math.Abs(disconnectedVPotential.GetPotential(
                    new Tile2i(4, 0)) - 13f) > 0.0001f
                || Math.Abs(disconnectedVPotential.GetPotential(
                    new Tile2i(0, 0)) - 22f) > 0.0001f
                || disconnectedVPotential.GetPotential(
                    new Tile2i(1, 0)) != 0f
                || disconnectedEscapePotential.BuiltComponentCount != 0
                || Math.Abs(disconnectedEscapePotential.GetPotential(
                    new Tile2i(0, 0)) - 33f) > 0.0001f
                || disconnectedEscapePotential.BuiltComponentCount != 1
                || Math.Abs(disconnectedEscapePotential.GetPotential(
                    new Tile2i(1, 0)) - 32f) > 0.0001f
                || disconnectedEscapePotential.BuiltComponentCount != 1
                || blockedEscapePotential.GetPotential(
                    new Tile2i(0, 0)) != 0f)
            {
                failure = "Sparse V2 P must charge generated origins on the 4x4 lattice and build disconnected G escape fields lazily per component";
                return false;
            }
            IReadOnlyCollection<string> keys = graph.CollectUnchargedCleanupKeys(
                new[] { cleanupA, cleanupB, cleanupA },
                new HashSet<string>(StringComparer.Ordinal));
            if (keys.Count != 1)
            {
                failure = "Cleanup object cost keys must deduplicate across footprint centers";
                return false;
            }
            IReadOnlyCollection<string> chargedKeys = graph.CollectUnchargedCleanupKeys(
                new[] { cleanupA, cleanupB },
                new HashSet<string>(StringComparer.Ordinal) { "prop:shared" });
            if (chargedKeys.Count != 0)
            {
                failure = "Previously charged cleanup object must not be charged again";
                return false;
            }

            failure = string.Empty;
            return true;

            AccessPropCleanupInfo TreeCenter(Tile2i center, string key)
                => AccessPropCleanupPolicy.BuildOriginInfo(
                    new Tile2i(center.X & -4, center.Y & -4),
                    new[]
                    {
                        new AccessPropSample(
                            center, true, false, true, key),
                    });
        }

        private static bool TryCreateUniformState(
            Tile2i anchor,
            AccessV2TravelAxis axis,
            Tile2i direction,
            AccessSearchMode mode,
            int center2,
            out AccessV2BandState state,
            out string failure)
        {
            state = default;
            if (!AccessHeightProfile.TryForMode(
                    mode, center2, out AccessHeightProfile profile))
            {
                failure = "Profile template unavailable";
                return false;
            }
            if (!AccessV2BandProfile.TryCreateEnabled(
                    axis, profile, profile,
                    out AccessV2BandProfile band, out failure))
                return false;
            state = new AccessV2BandState(anchor, band, direction);
            failure = string.Empty;
            return true;
        }

        private static bool ValidateSlicedGroundGraphBuild(out string failure)
        {
            var ground = new HashSet<Tile2i>();
            for (int y = 0; y < 40; y++)
                for (int x = 0; x < 40; x++)
                    if (x != 19 || y < 12 || y > 27)
                        ground.Add(new Tile2i(x, y));
            var goals = new[]
            {
                new Tile2i(39, 39),
                new Tile2i(39, 0),
            };
            var cleanup = new Dictionary<Tile2i, AccessPropCleanupInfo>
            {
                [new Tile2i(19, 20)] = new AccessPropCleanupInfo(
                    new Tile2i(19, 20),
                    AccessPropCleanupClass.DenseDebris,
                    AccessPropBlockerKind.None,
                    true),
            };
            var projected = new HashSet<Tile2i>
            {
                new Tile2i(10, 10),
                new Tile2i(11, 10),
            };
            var expected = new AccessV2GroundGraph(
                ground, goals, cleanup, projected, 8f);
            var build = new AccessV2GroundGraph.BuildSession(
                ground, goals, cleanup, projected, 8f);
            int advances = 0;
            while (!build.IsComplete)
            {
                build.Advance(1);
                advances++;
            }
            AccessV2GroundGraph actual = build.Result;
            if (advances <= ground.Count
                || actual.GroundNodeCount != expected.GroundNodeCount
                || actual.CleanupNodeCount != expected.CleanupNodeCount
                || actual.GoalCount != expected.GoalCount)
            {
                failure = "sliced V2 ground build did not retain incremental cardinality";
                return false;
            }
            var probes = new HashSet<Tile2i>(ground)
            {
                new Tile2i(19, 20),
            };
            foreach (Tile2i tile in probes)
            {
                bool expectedDistance = expected.TryGetGoalDistance(
                    tile, out float expectedValue);
                bool actualDistance = actual.TryGetGoalDistance(
                    tile, out float actualValue);
                if (expectedDistance != actualDistance
                    || expectedDistance
                        && Math.Abs(expectedValue - actualValue) > 0.0001f
                    || expected.IsGoalConnected(tile)
                        != actual.IsGoalConnected(tile)
                    || expected.TryGetComponentId(tile, out int expectedComponent)
                        != actual.TryGetComponentId(tile, out int actualComponent)
                    || expectedComponent != actualComponent)
                {
                    failure = "sliced V2 ground build diverged from synchronous graph";
                    return false;
                }
            }
            failure = string.Empty;
            return true;
        }

        private static bool ValidateFixedNavigationGraph(out string failure)
        {
            if (!AccessHeightProfile.TryForMode(
                    AccessSearchMode.Flat, 0,
                    out AccessHeightProfile flat))
            {
                failure = "FV fixture flat profile unavailable";
                return false;
            }

            var fixedProfiles =
                new Dictionary<Tile2i, AccessHeightProfile>();
            for (int y = 0; y < 16; y += 4)
                for (int x = 0; x < 16; x += 4)
                    fixedProfiles.Add(new Tile2i(x, y), flat);

            var projectedCenters = new List<Tile2i>();
            for (int y = 0; y <= 16; y++)
                for (int x = 0; x <= 16; x++)
                    projectedCenters.Add(new Tile2i(x, y));
            var exact = new AccessV2GroundGraph(
                projectedCenters,
                Array.Empty<Tile2i>(),
                new Dictionary<Tile2i, AccessPropCleanupInfo>(),
                projectedCenters);
            var fv = new AccessV2FixedNavigationGraph(
                fixedProfiles, exact);

            var providerOrigins = new HashSet<Tile2i>
            {
                new Tile2i(0, 0),
                new Tile2i(0, 4),
            };
            AccessV2GroundGraph providerGoalGraph =
                AccessPathSearch.BuildV2RequestGroundGraph(
                    exact,
                    fv,
                    providerOrigins,
                    out HashSet<Tile2i> providerGoalCenters);
            Tile2i providerCenter = new Tile2i(2, 4);
            if (!providerGoalCenters.Contains(providerCenter)
                || !providerGoalGraph.IsGoal(providerCenter)
                || exact.IsGoal(providerCenter)
                || !providerGoalGraph.IsGoalConnected(providerCenter)
                || !providerGoalGraph.TryGetGoalDistance(
                    providerCenter + new RelTile2i(1, 0),
                    out float providerNeighborDistance)
                || Math.Abs(providerNeighborDistance - 1f) > 0.0001f)
            {
                failure =
                    "A request-scoped fixed provider band did not become a V2 terminal ground center.";
                return false;
            }

            Tile2i start = new Tile2i(2, 4);
            Tile2i goal = new Tile2i(14, 12);
            var existingGoalGraph = new AccessV2GroundGraph(
                projectedCenters,
                new[] { goal },
                new Dictionary<Tile2i, AccessPropCleanupInfo>(),
                projectedCenters);
            AccessV2GroundGraph combinedGoalGraph =
                existingGoalGraph.WithAdditionalGoals(new[] { providerCenter });
            if (!combinedGoalGraph.IsGoal(providerCenter)
                || !combinedGoalGraph.IsGoal(goal)
                || !combinedGoalGraph.TryGetGoalDistance(
                    providerCenter + new RelTile2i(1, 0),
                    out float combinedNeighborDistance)
                || Math.Abs(combinedNeighborDistance - 1f) > 0.0001f
                || !combinedGoalGraph.TryGetGoalDistance(
                    goal, out float combinedExistingGoalDistance)
                || Math.Abs(combinedExistingGoalDistance) > 0.0001f)
            {
                failure =
                    "A request-scoped goal overlay must preserve existing goal distances while adding the nearer fixed-provider distance.";
                return false;
            }
            if (fv.NodeCount != 24
                || !fv.TryGetShortestPath(
                    AccessV2TravelAxis.X, start, goal,
                    out IReadOnlyList<Tile2i> fvPath,
                    out float fvCost)
                || !TryGetGroundShortestCost(
                    exact, start, goal, out float exactCost)
                || Math.Abs(fvCost - exactCost) > 0.0001f
                || Math.Abs(fvCost
                    - (8f * AccessV2GroundGraph.DiagonalCost + 4f))
                    > 0.0001f
                || fvPath.Count != 13
                || fvPath.First() != start
                || fvPath.Last() != goal)
            {
                failure = "FV flat-interior connectivity or exact shortest cost diverged from the vehicle-center graph";
                return false;
            }

            var goalExact = new AccessV2GroundGraph(
                projectedCenters,
                new[] { goal },
                new Dictionary<Tile2i, AccessPropCleanupInfo>(),
                projectedCenters);
            var goalFv = new AccessV2FixedNavigationGraph(
                fixedProfiles, goalExact);
            var fvPotential = new AccessV2PotentialField(
                Tile2i.Zero, new Tile2i(12, 12),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                goalExact, goalFv,
                generatedFixedCost: 5f,
                centerSpokeCost: 4f);
            if (!TryCreateUniformState(
                    Tile2i.Zero,
                    AccessV2TravelAxis.X,
                    new Tile2i(4, 0),
                    AccessSearchMode.Flat,
                    0,
                    out AccessV2BandState fvStartState,
                    out string fvPotentialFailure)
                || fvPotential.GeneratedNodeCount != 0
                || fvPotential.FixedNodeCount != 24
                || Math.Abs(fvPotential.GetPotential(fvStartState)
                    - exactCost) > 0.0001f
                || Math.Abs(fvPotential.GetGroundLaunchPotential(start)
                    - exactCost) > 0.0001f)
            {
                failure = "Sparse P must index reusable FV nodes by axis and preserve their exact goal-connected suffix: "
                    + fvPotentialFailure;
                return false;
            }

            var blockedProjected = projectedCenters
                .Where(center => center != new Tile2i(3, 4))
                .ToList();
            var cornerBlockedExact = new AccessV2GroundGraph(
                blockedProjected,
                Array.Empty<Tile2i>(),
                new Dictionary<Tile2i, AccessPropCleanupInfo>(),
                blockedProjected);
            var cornerBlockedFv = new AccessV2FixedNavigationGraph(
                fixedProfiles, cornerBlockedExact);
            if (cornerBlockedFv.CanTraverse(
                    AccessV2TravelAxis.X,
                    new Tile2i(2, 4),
                    new Tile2i(6, 8)))
            {
                failure = "FV diagonal macro edge must require both exact cardinal swept corridors";
                return false;
            }

            Tile2i xTurnCenter = new Tile2i(2, 4);
            Tile2i yTurnCenter = new Tile2i(4, 2);
            if (!fv.CanTraverse(
                    AccessV2TravelAxis.X, xTurnCenter,
                    AccessV2TravelAxis.Y, yTurnCenter)
                || !fv.TryGetShortestPath(
                    AccessV2TravelAxis.X, xTurnCenter,
                    AccessV2TravelAxis.Y, yTurnCenter,
                    out IReadOnlyList<Tile2i> turnPath,
                    out float turnCost)
                || !TryGetGroundShortestCost(
                    exact, xTurnCenter, yTurnCenter,
                    out float exactTurnCost)
                || Math.Abs(turnCost - exactTurnCost) > 0.0001f
                || Math.Abs(turnCost
                    - 2f * AccessV2GroundGraph.DiagonalCost) > 0.0001f
                || turnPath.Count != 3)
            {
                failure = "FV directionless navigation must connect perpendicular fixed-band lattices through the exact local center path";
                return false;
            }
            Tile2i crossAxisGoal = new Tile2i(12, 14);
            if (!fv.TryGetShortestPath(
                    AccessV2TravelAxis.X, xTurnCenter,
                    AccessV2TravelAxis.Y, crossAxisGoal,
                    out IReadOnlyList<Tile2i> crossAxisPath,
                    out float crossAxisCost)
                || !TryGetGroundShortestCost(
                    exact, xTurnCenter, crossAxisGoal,
                    out float exactCrossAxisCost)
                || Math.Abs(crossAxisCost - exactCrossAxisCost) > 0.0001f
                || Math.Abs(crossAxisCost
                    - 10f * AccessV2GroundGraph.DiagonalCost) > 0.0001f
                || crossAxisPath.First() != xTurnCenter
                || crossAxisPath.Last() != crossAxisGoal)
            {
                failure = "FV mixed-orientation shortest path must retain exact vehicle-center graph cost";
                return false;
            }
            if (cornerBlockedFv.CanTraverse(
                    AccessV2TravelAxis.X, xTurnCenter,
                    AccessV2TravelAxis.Y, yTurnCenter))
            {
                failure = "FV perpendicular-lattice connector must enforce strict swept diagonal clearance";
                return false;
            }

            Tile2i cleanupBoundary = new Tile2i(3, 4);
            var cleanupBoundaryGraph = new AccessV2GroundGraph(
                projectedCenters.Where(
                    center => center != cleanupBoundary),
                Array.Empty<Tile2i>(),
                new Dictionary<Tile2i, AccessPropCleanupInfo>
                {
                    [cleanupBoundary] =
                        AccessPropCleanupPolicy.BuildOriginInfo(
                            Tile2i.Zero,
                            new[]
                            {
                                new AccessPropSample(
                                    cleanupBoundary,
                                    true, false, true,
                                    "tree:fv-portal"),
                            }),
                },
                projectedCenters.Where(
                    center => center != cleanupBoundary));
            var cleanupBoundaryFv = new AccessV2FixedNavigationGraph(
                fixedProfiles, cleanupBoundaryGraph);
            if (cleanupBoundaryFv.CanTraverse(
                    AccessV2TravelAxis.X,
                    new Tile2i(2, 4),
                    new Tile2i(6, 4))
                || !cleanupBoundaryFv.RequiresPortal(
                    AccessV2TravelAxis.X,
                    new Tile2i(2, 4),
                    new Tile2i(6, 4))
                || cornerBlockedFv.RequiresPortal(
                    AccessV2TravelAxis.X,
                    new Tile2i(2, 4),
                    new Tile2i(6, 8)))
            {
                failure = "FV must stop at cleanup changes, expose an exact portal requirement, and not classify a hard-blocked diagonal as a portal";
                return false;
            }

            var cornerProfiles =
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [new Tile2i(0, 0)] =
                        new AccessHeightProfile(0, 0, 1, 0),
                    [new Tile2i(0, 4)] =
                        new AccessHeightProfile(0, 1, 0, 0),
                };
            var cornerFv = new AccessV2FixedNavigationGraph(
                cornerProfiles, exact);
            if (!cornerFv.ContainsNode(
                    AccessV2TravelAxis.X, new Tile2i(2, 4))
                || AccessV2BandProfile.TryCreate(
                    AccessV2TravelAxis.X,
                    cornerProfiles[new Tile2i(0, 0)],
                    cornerProfiles[new Tile2i(0, 4)],
                    includeDeferred: true,
                    out _, out _))
            {
                failure = "FV eligibility must accept exact compatible fixed corner profiles without applying the generated-V profile whitelist";
                return false;
            }

            var largeProfiles =
                new Dictionary<Tile2i, AccessHeightProfile>();
            for (int y = 0; y < 32; y += 4)
                for (int x = 0; x < 32; x += 4)
                    largeProfiles.Add(new Tile2i(x, y), flat);
            var largeProjectedCenters = new List<Tile2i>();
            for (int y = 0; y <= 32; y++)
                for (int x = 0; x <= 32; x++)
                    largeProjectedCenters.Add(new Tile2i(x, y));
            var largeExact = new AccessV2GroundGraph(
                largeProjectedCenters,
                Array.Empty<Tile2i>(),
                new Dictionary<Tile2i, AccessPropCleanupInfo>(),
                largeProjectedCenters);
            var largeFv = new AccessV2FixedNavigationGraph(
                largeProfiles, largeExact);
            AccessV2BandProfile.TryCreateEnabled(
                AccessV2TravelAxis.X, flat, flat,
                out AccessV2BandProfile largeBand, out _);
            var largeStart = new AccessV2BandState(
                new Tile2i(12, 12), largeBand,
                new Tile2i(4, 0));
            var largeEndpoints = new AccessV2EndpointSet(
                new[]
                {
                    new AccessV2StartFrontage(
                        largeStart, largeStart.GetLaneOrigin(0)),
                },
                new AccessV2FrontageDiagnostics());
            var sparseSession = new AccessV2SearchSession(
                largeEndpoints,
                Tile2i.Zero, new Tile2i(32, 32),
                (current, transition, history, connected) =>
                    AccessV2TransitionEvaluation.Reject(
                        "FixtureNoGeneratedExit"),
                maxVisited: 5000,
                maxCost: float.MaxValue,
                handoffEvaluator: (recent, history, required) =>
                    Array.Empty<AccessV2HandoffCandidate>(),
                groundGraph: largeExact,
                fixedNavigationGraph: largeFv);
            int sparseGroundExplorations = 0;
            sparseSession.NodeExplored =
                (position, height2, isGround, groundHeight2) =>
                {
                    if (isGround)
                        sparseGroundExplorations++;
                };
            while (!sparseSession.IsComplete)
                sparseSession.Step(5000);
            if (sparseSession.Result.Success
                || sparseGroundExplorations >= 113)
            {
                failure =
                    "FV production search must keep projected-boundary portal probing bounded instead of restoring a per-center body scan: "
                    + $"groundExplorations={sparseGroundExplorations}";
                return false;
            }

            var corridorGround =
                new List<Tile2i>(largeProjectedCenters);
            for (int x = 33; x <= 36; x++)
                corridorGround.Add(new Tile2i(x, 16));
            Tile2i corridorGoal = new Tile2i(36, 16);
            var corridorExact = new AccessV2GroundGraph(
                corridorGround,
                new[] { corridorGoal },
                new Dictionary<Tile2i, AccessPropCleanupInfo>(),
                largeProjectedCenters);
            var corridorFv = new AccessV2FixedNavigationGraph(
                largeProfiles, corridorExact);
            var corridorSession = new AccessV2SearchSession(
                largeEndpoints,
                Tile2i.Zero, new Tile2i(36, 32),
                (current, transition, history, connected) =>
                    AccessV2TransitionEvaluation.Reject(
                        "FixtureNoGeneratedExit"),
                maxVisited: 5000,
                maxCost: float.MaxValue,
                handoffEvaluator: (recent, history, required) =>
                    Array.Empty<AccessV2HandoffCandidate>(),
                groundGraph: corridorExact,
                fixedNavigationGraph: corridorFv);
            while (!corridorSession.IsComplete)
                corridorSession.Step(5000);
            bool hasNonAdjacentGroundStep = false;
            for (int index = 1;
                index < corridorSession.Result.GroundPath.Count;
                index++)
            {
                Tile2i left =
                    corridorSession.Result.GroundPath[index - 1];
                Tile2i right =
                    corridorSession.Result.GroundPath[index];
                if (Math.Max(
                        Math.Abs(left.X - right.X),
                        Math.Abs(left.Y - right.Y)) != 1)
                {
                    hasNonAdjacentGroundStep = true;
                    break;
                }
            }
            if (!corridorSession.Result.Success
                || corridorSession.Result.GeneratedProfiles.Count != 0
                || Math.Abs(corridorSession.Result.Cost - 22f) > 0.0001f
                || corridorSession.Result.GroundPath.Count != 23
                || hasNonAdjacentGroundStep)
            {
                failure =
                    "FV production route must expand sparse macro edges and its physical-G portal into an exact adjacent replay path: "
                    + $"success={corridorSession.Result.Success} "
                    + $"reason={corridorSession.Result.FailureReason} "
                    + $"cost={corridorSession.Result.Cost:0.###} "
                    + $"ground={corridorSession.Result.GroundPath.Count}";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool TryGetGroundShortestCost(
            AccessV2GroundGraph graph,
            Tile2i start,
            Tile2i goal,
            out float cost)
        {
            var distances = new Dictionary<Tile2i, float>
            {
                [start] = 0f,
            };
            var queue = new SortedDictionary<float, Queue<Tile2i>>();
            queue.Add(0f, new Queue<Tile2i>(new[] { start }));
            var directions = new[]
            {
                new RelTile2i(1, 0), new RelTile2i(-1, 0),
                new RelTile2i(0, 1), new RelTile2i(0, -1),
                new RelTile2i(1, 1), new RelTile2i(1, -1),
                new RelTile2i(-1, 1), new RelTile2i(-1, -1),
            };
            while (queue.Count > 0)
            {
                KeyValuePair<float, Queue<Tile2i>> first = queue.First();
                Tile2i current = first.Value.Dequeue();
                if (first.Value.Count == 0) queue.Remove(first.Key);
                if (!distances.TryGetValue(current, out float known)
                    || Math.Abs(known - first.Key) > 0.0001f)
                    continue;
                if (current == goal)
                {
                    cost = known;
                    return true;
                }
                for (int index = 0; index < directions.Length; index++)
                {
                    Tile2i next = current + directions[index];
                    if (!graph.CanTraverse(current, next))
                        continue;
                    float nextCost = known
                        + AccessV2GroundGraph.GetStepCost(current, next);
                    if (distances.TryGetValue(next, out float old)
                        && old <= nextCost + 0.0001f)
                        continue;
                    distances[next] = nextCost;
                    if (!queue.TryGetValue(
                            nextCost, out Queue<Tile2i> bucket))
                    {
                        bucket = new Queue<Tile2i>();
                        queue.Add(nextCost, bucket);
                    }
                    bucket.Enqueue(next);
                }
            }
            cost = 0f;
            return false;
        }

        private static bool ValidateUsefulHeightEnvelope(out string failure)
        {
            var terrain = new Dictionary<Tile2i, float>();
            for (int y = 0; y <= 24; y++)
                for (int x = 0; x <= 24; x++)
                    terrain.Add(new Tile2i(x, y), 0f);
            if (!AccessUsefulHeightEnvelope.TryCreate(
                    terrain, Array.Empty<Tile2i>(),
                    new Dictionary<Tile2i, AccessHeightProfile>(),
                    out AccessUsefulHeightEnvelope? envelope,
                    out string envelopeFailure)
                || envelope == null)
            {
                failure = "V2 envelope fixture build failed: " + envelopeFailure;
                return false;
            }

            if (!TryCreateUniformState(
                    new Tile2i(4, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState flat, out failure))
                return false;
            AccessV2Transition? ramp = AccessV2Geometry
                .EnumerateStraight(flat)
                .FirstOrDefault(transition => transition.Delta.Any(
                    item => item.Profile.Center2 > 0));
            if (ramp == null
                || AccessV2SearchSession.IsTransitionWithinUsefulHeightEnvelope(
                    envelope, ramp, out string rampRejection)
                || rampRejection != "HeightEnvelopeAbove")
            {
                failure = "Strict flat-map hull must reject unsupported rising ramp-lane centers";
                return false;
            }

            if (!TryCreateUniformState(
                    new Tile2i(4, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 2,
                    out AccessV2BandState highPredecessor, out failure)
                || !TryCreateUniformState(
                    new Tile2i(8, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 2,
                    out AccessV2BandState highCurrent, out failure)
                || !AccessV2Geometry.TryTurn(
                    highPredecessor, highCurrent, 1,
                    out AccessV2Transition highTurn, out _)
                || !AccessV2SearchSession.IsTransitionWithinUsefulHeightEnvelope(
                    envelope, highTurn, out _))
            {
                failure = "V2 in-place turns must inherit their already admitted centers";
                return false;
            }

            Tile2i allowanceSample = new Tile2i(12, 12);
            if (envelope.IsV1CenterHeightUseful(
                    allowanceSample, -1, out string strictV1Lower)
                || strictV1Lower != "HeightEnvelopeBelow"
                || envelope.IsV2CenterHeightUseful(
                    allowanceSample, -1, out string strictV2Lower)
                || strictV2Lower != "HeightEnvelopeBelow"
                || envelope.IsV1CenterHeightUseful(
                    allowanceSample, 1, out string strictV1Upper)
                || strictV1Upper != "HeightEnvelopeAbove"
                || envelope.IsV2CenterHeightUseful(
                    allowanceSample, 1, out string strictV2Upper)
                || strictV2Upper != "HeightEnvelopeAbove")
            {
                failure = "Useful-height hull candidate checks must remain strict away from request targets";
                return false;
            }

            Tile2i startOrigin = new Tile2i(0, 16);
            Tile2i targetOrigin = new Tile2i(8, 8);
            var endpointProfiles = new Dictionary<Tile2i, AccessHeightProfile>
            {
                [startOrigin] = new AccessHeightProfile(0, 0, 0, 0),
                [targetOrigin] = new AccessHeightProfile(0, 0, 0, 0),
            };
            AccessUsefulHeightEnvelope v1TargetEnvelope =
                envelope.WithExtendedFixedEndpoints(
                    endpointProfiles, new[] { startOrigin, targetOrigin },
                    useV2: false);
            AccessUsefulHeightEnvelope v2TargetEnvelope =
                envelope.WithExtendedFixedEndpoints(
                    endpointProfiles, new[] { startOrigin, targetOrigin },
                    useV2: true);
            Tile2i startCenter = startOrigin + new RelTile2i(2, 2);
            Tile2i targetCenter = targetOrigin + new RelTile2i(2, 2);
            Tile2i v2UpperExtensionEdge = targetCenter + new RelTile2i(5, 0);
            Tile2i outsideV2UpperExtension = targetCenter + new RelTile2i(6, 0);
            Tile2i v2LowerExtensionEdge = targetCenter + new RelTile2i(9, 0);
            Tile2i outsideV2LowerExtension = targetCenter + new RelTile2i(10, 0);
            if (!v1TargetEnvelope.IsV1CenterHeightUseful(
                    startCenter, -32, out _)
                || !v1TargetEnvelope.IsV1CenterHeightUseful(
                    startCenter, 16, out _)
                || !v2TargetEnvelope.IsV2CenterHeightUseful(
                    startCenter, -64, out _)
                || !v2TargetEnvelope.IsV2CenterHeightUseful(
                    startCenter, 32, out _)
                || !v1TargetEnvelope.IsV1CenterHeightUseful(
                    targetCenter, -32, out _)
                || !v1TargetEnvelope.IsV1CenterHeightUseful(
                    targetCenter, 16, out _)
                || !v2TargetEnvelope.IsV2CenterHeightUseful(
                    targetCenter, -64, out _)
                || !v2TargetEnvelope.IsV2CenterHeightUseful(
                    targetCenter, 32, out _)
                || !v2TargetEnvelope.IsV2CenterHeightUseful(
                    v2LowerExtensionEdge, -1, out _)
                || !v2TargetEnvelope.IsV2CenterHeightUseful(
                    v2UpperExtensionEdge, 1, out _)
                || v2TargetEnvelope.IsV2CenterHeightUseful(
                    outsideV2LowerExtension, -1, out _)
                || v2TargetEnvelope.IsV2CenterHeightUseful(
                    outsideV2UpperExtension, 1, out _))
            {
                failure = "Endpoint extensions must localize V1/V2 turn-landing room to every potential fixed start and goal cone";
                return false;
            }

            if (!AccessUsefulHeightEnvelope.TryCreate(
                    terrain, Array.Empty<Tile2i>(),
                    new Dictionary<Tile2i, AccessHeightProfile>(),
                    out AccessUsefulHeightEnvelope? customEnvelope,
                    out string customEnvelopeFailure,
                    v1LowerAllowance32: 0,
                    v2LowerAllowance32: 48,
                    v1UpperAllowance32: 8,
                    v2UpperAllowance32: 64)
                || customEnvelope == null
                || customEnvelope.V1LowerAllowance32 != 0
                || customEnvelope.V2LowerAllowance32 != 48
                || customEnvelope.V1UpperAllowance32 != 8
                || customEnvelope.V2UpperAllowance32 != 64
                || customEnvelope.IsV1CenterHeightUseful(
                    allowanceSample, -1, out _)
                || customEnvelope.IsV2CenterHeightUseful(
                    allowanceSample, -1, out _)
                || customEnvelope.IsV1CenterHeightUseful(
                    allowanceSample, 1, out _)
                || customEnvelope.IsV2CenterHeightUseful(
                    allowanceSample, 1, out _))
            {
                failure = "Useful-height hull must capture custom endpoint extensions while retaining strict base checks: "
                    + customEnvelopeFailure;
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateSearch(out string failure)
        {
            var slantedLane0 = new AccessHeightProfile(2, 0, 0, 0);
            var slantedLane1 = new AccessHeightProfile(0, 0, 0, 2);
            if (!AccessV2BandProfile.TryCreate(
                    AccessV2TravelAxis.X,
                    slantedLane0,
                    slantedLane1,
                    includeDeferred: true,
                    out AccessV2BandProfile slantedAdapterBand,
                    out string slantedAdapterReason)
                || slantedAdapterBand.Kind
                    != AccessV2BandProfileKind.MechanicallyValidDeferred)
            {
                failure =
                    "A slanted fixed fringe must admit two compatible canonical V-prime slices as one bounded transition adapter: "
                    + slantedAdapterReason;
                return false;
            }
            Tile2i slantedAnchor = new Tile2i(8, 8);
            Tile2i slantedCompanion = new Tile2i(8, 12);
            AccessV2SearchSession.GroundToVProfileCandidate[]
                slantedCandidates =
                AccessV2SearchSession.EnumerateGroundToVBandProfiles(
                    slantedAnchor,
                    terrainHeight: 0f,
                    AccessV2TravelAxis.X,
                    new Tile2i(4, 0),
                    fixedProfileProvider: null,
                    generatedVPrimeOriginValidator:
                        origin => origin == slantedAnchor
                            || origin == slantedCompanion)
                .ToArray();
            AccessV2SearchSession.GroundToVProfileCandidate
                slantedCandidate =
                slantedCandidates.FirstOrDefault(candidate =>
                    AccessV2BandProfile.ProfilesEqual(
                        candidate.Lane0, slantedLane0)
                    && AccessV2BandProfile.ProfilesEqual(
                        candidate.Lane1, slantedLane1));
            if (!AccessV2BandProfile.ProfilesEqual(
                    slantedCandidate.Lane0, slantedLane0)
                || !AccessV2BandProfile.ProfilesEqual(
                    slantedCandidate.Lane1, slantedLane1)
                || AccessV2SearchSession
                    .EnumerateGroundToVBandProfiles(
                        slantedAnchor,
                        terrainHeight: 0f,
                        AccessV2TravelAxis.X,
                        new Tile2i(4, 0),
                        fixedProfileProvider: null,
                        generatedVPrimeOriginValidator:
                            origin => origin == slantedAnchor)
                    .Any(candidate =>
                        AccessV2BandProfile.IsCanonicalVPrime(
                            candidate.Lane1)))
            {
                failure =
                    "Lazy G-to-V resolution must enumerate the compatible two-lane V-prime adapter only when both origins are catalogued";
                return false;
            }
            var slantedFixedProfiles =
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [new Tile2i(4, 8)] =
                        new AccessHeightProfile(2, 2, 0, 0),
                    [new Tile2i(4, 12)] =
                        new AccessHeightProfile(0, 0, 2, 2),
                };
            if (!AccessV2SearchSession
                    .EnumerateGroundToVBandProfiles(
                        slantedAnchor,
                        terrainHeight: 10f,
                        AccessV2TravelAxis.X,
                        new Tile2i(4, 0),
                        origin => slantedFixedProfiles.TryGetValue(
                            origin, out AccessHeightProfile profile)
                                ? profile
                                : (AccessHeightProfile?)null,
                        generatedVPrimeOriginValidator:
                            origin => origin == slantedAnchor
                                || origin == slantedCompanion)
                    .Any(candidate =>
                        AccessV2BandProfile.ProfilesEqual(
                            candidate.Lane0, slantedLane0)
                        && AccessV2BandProfile.ProfilesEqual(
                            candidate.Lane1, slantedLane1)))
            {
                failure =
                    "Slanted adapter levels must derive from the projected fixed fringe rather than unrelated physical terrain";
                return false;
            }

            Tile2i slantedProjectedGround = new Tile2i(8, 10);
            bool SlantedGeneratedOrigin(Tile2i origin)
                => origin == slantedAnchor
                    || origin == slantedCompanion;
            AccessHeightProfile? SlantedFixedProfile(Tile2i origin)
                => slantedFixedProfiles.TryGetValue(
                    origin, out AccessHeightProfile profile)
                        ? profile
                        : (AccessHeightProfile?)null;
            if (AccessV2SearchSession.CanExitGroundComponentToV(
                    new Tile2i(6, 10),
                    SlantedGeneratedOrigin,
                    SlantedFixedProfile)
                || !AccessV2SearchSession
                    .CanUseCanonicalGroundToVLaunchPosition(
                        slantedProjectedGround, new Tile2i(4, 0)))
            {
                failure =
                    "G-to-V fixed-fringe exits must reject noncanonical centers while retaining the corresponding canonical launch";
                return false;
            }
            if (!AccessV2SearchSession.CanExitGroundComponentToV(
                    slantedProjectedGround,
                    SlantedGeneratedOrigin,
                    SlantedFixedProfile))
            {
                failure =
                    "Projected G on a mechanically incompatible slanted fixed fringe must resolve the immediately outward two-lane V-prime adapter";
                return false;
            }
            Tile2i slantedFixedBandAnchor =
                AccessV2SearchSession.GetGroundToVBandAnchor(
                    slantedProjectedGround, new Tile2i(4, 0));
            Tile2i slantedOutwardAdapterAnchor =
                AccessV2Geometry.Add(
                    slantedFixedBandAnchor, new Tile2i(4, 0));
            Tile2i[] slantedResolvedAnchors =
                AccessV2SearchSession.EnumerateGroundToVBandAnchors(
                    slantedProjectedGround,
                    new Tile2i(4, 0),
                    includeOutwardFringe: true)
                .ToArray();
            if (!slantedResolvedAnchors.Contains(
                    slantedOutwardAdapterAnchor))
            {
                failure =
                    "Projected G on a fixed fringe must examine the immediately outward band where a slanted V-prime adapter is generated";
                return false;
            }

            var realSlantedFixedProfiles =
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [new Tile2i(4, 4)] =
                        new AccessHeightProfile(0, 2, 2, 0),
                    [new Tile2i(4, 8)] =
                        new AccessHeightProfile(0, 2, 2, 0),
                    [new Tile2i(4, 12)] =
                        new AccessHeightProfile(0, 2, 2, 0),
                };
            Tile2i realSlantedAnchor = new Tile2i(8, 8);
            Tile2i realSlantedCompanion = new Tile2i(8, 12);
            AccessV2SearchSession.GroundToVProfileCandidate[]
                realSlantedCandidates =
                AccessV2SearchSession.EnumerateGroundToVBandProfiles(
                    realSlantedAnchor,
                    terrainHeight: 1f,
                    AccessV2TravelAxis.X,
                    new Tile2i(4, 0),
                    origin => realSlantedFixedProfiles.TryGetValue(
                        origin, out AccessHeightProfile profile)
                            ? profile
                            : (AccessHeightProfile?)null,
                    origin => origin == realSlantedAnchor
                        || origin == realSlantedCompanion)
                .ToArray();
            AccessV2BandState realSlantedState = default;
            AccessV2Transition realSlantedPair = null!;
            foreach (AccessV2SearchSession.GroundToVProfileCandidate
                candidate in realSlantedCandidates)
            {
                if ((!AccessV2BandProfile.IsCanonicalVPrime(
                            candidate.Lane0)
                        && !AccessV2BandProfile.IsCanonicalVPrime(
                            candidate.Lane1))
                    || !AccessV2BandProfile.TryCreate(
                        AccessV2TravelAxis.X,
                        candidate.Lane0,
                        candidate.Lane1,
                        includeDeferred: true,
                        out AccessV2BandProfile candidateBand,
                        out _))
                    continue;
                var candidateState = new AccessV2BandState(
                    realSlantedAnchor,
                    candidateBand,
                    new Tile2i(4, 0));
                AccessV2Transition? pair = AccessV2SearchSession
                    .EnumerateVPrimeAdapterExtensions(candidateState)
                    .FirstOrDefault();
                if (pair == null)
                    continue;
                realSlantedState = candidateState;
                realSlantedPair = pair;
                break;
            }
            if (realSlantedPair == null)
            {
                failure =
                    "A slanted [0,2,2,0]/2 fixed fringe must resolve the opposing raised/lowered V-prime pair whose outbound face admits straight ordinary V";
                return false;
            }
            bool RealSlantedGenerated(Tile2i origin)
                => origin == realSlantedState.GetLaneOrigin(0)
                    || origin == realSlantedState.GetLaneOrigin(1)
                    || origin == realSlantedPair.Next.GetLaneOrigin(0)
                    || origin == realSlantedPair.Next.GetLaneOrigin(1);
            string realSlantedHistoryReason = "transition resolution failed";
            if (!AccessV2SearchSession.TryResolveGroundToVTransition(
                    realSlantedState,
                    _ => null,
                    RealSlantedGenerated,
                    out AccessV2Transition realSlantedFirst)
                || !AccessV2History.Empty.TryApply(
                    realSlantedFirst,
                    out AccessV2History realSlantedHistory,
                    out _)
                || !AccessV2SearchSession.TryResolveGroundToVTransition(
                    realSlantedPair,
                    _ => null,
                    RealSlantedGenerated,
                    out AccessV2Transition realSlantedResolvedPair)
                || !realSlantedHistory.TryValidateApply(
                    realSlantedResolvedPair,
                    out realSlantedHistoryReason))
            {
                failure =
                    "A paired slanted V-prime adapter must retain its first slice as local history context: "
                    + realSlantedHistoryReason;
                return false;
            }

            AccessV2BandProfile.TryCreateEnabled(
                AccessV2TravelAxis.X,
                realSlantedFixedProfiles[new Tile2i(4, 8)],
                realSlantedFixedProfiles[new Tile2i(4, 12)],
                out AccessV2BandProfile realSlantedStartBand,
                out _);
            var realSlantedStart = new AccessV2BandState(
                new Tile2i(4, 8),
                realSlantedStartBand,
                new Tile2i(4, 0));
            AccessV2Transition realSlantedOrdinary =
                AccessV2Geometry.EnumerateStraight(
                    realSlantedPair.Next).First();
            AccessV2BandState realSlantedGoal =
                AccessV2Geometry.EnumerateStraight(
                    realSlantedOrdinary.Next).First().Next;
            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 2,
                out AccessHeightProfile realSlantedExpensiveFlat);
            var realSlantedSelectionFixedProfiles =
                new Dictionary<Tile2i, AccessHeightProfile>(
                    realSlantedFixedProfiles)
                {
                    [new Tile2i(4, 20)] =
                        realSlantedExpensiveFlat,
                    [new Tile2i(4, 24)] =
                        realSlantedExpensiveFlat,
                };
            AccessV2BandProfile.TryCreateEnabled(
                AccessV2TravelAxis.X,
                realSlantedExpensiveFlat,
                realSlantedExpensiveFlat,
                out AccessV2BandProfile realSlantedExpensiveStartBand,
                out _);
            var realSlantedExpensiveStart =
                new AccessV2BandState(
                    new Tile2i(4, 20),
                    realSlantedExpensiveStartBand,
                    new Tile2i(4, 0));
            AccessV2Transition realSlantedExpensive = null!;
            AccessV2Transition realSlantedExpensiveSuccessor = null!;
            Tile2i realSlantedGround =
                AccessV2PotentialField.GetCanonicalCenter(
                    realSlantedStart);
            Tile2i realSlantedExpensiveGround =
                AccessV2PotentialField.GetCanonicalCenter(
                    realSlantedExpensiveStart);
            foreach (Tile2i travel in new[] { new Tile2i(4, 0) })
            {
                AccessV2TravelAxis axis = travel.X != 0
                    ? AccessV2TravelAxis.X
                    : AccessV2TravelAxis.Y;
                foreach (Tile2i anchor in AccessV2SearchSession
                    .EnumerateGroundToVBandAnchors(
                        realSlantedExpensiveGround, travel,
                        includeOutwardFringe: true))
                {
                    foreach (AccessV2SearchSession
                        .GroundToVProfileCandidate candidate
                        in AccessV2SearchSession
                            .EnumerateGroundToVBandProfiles(
                                anchor, 1f, axis, travel,
                                origin => realSlantedSelectionFixedProfiles
                                    .TryGetValue(
                                        origin,
                                        out AccessHeightProfile fixedProfile)
                                            ? fixedProfile
                                            : (AccessHeightProfile?)null,
                                _ => false))
                    {
                        if (!AccessV2BandProfile.TryCreate(
                                axis,
                                candidate.Lane0,
                                candidate.Lane1,
                                includeDeferred: true,
                                out AccessV2BandProfile band,
                                out _)
                            || !band.IsEnabled)
                            continue;
                        var state = new AccessV2BandState(
                            anchor, band, travel);
                        if (!AccessV2SearchSession
                                .TryResolveGroundToVTransition(
                                    state,
                                    origin => realSlantedSelectionFixedProfiles
                                        .TryGetValue(
                                            origin,
                                            out AccessHeightProfile
                                                fixedProfile)
                                                    ? fixedProfile
                                                    : (AccessHeightProfile?)null,
                                    null,
                                    out AccessV2Transition firstTransition))
                            continue;
                        AccessV2Transition? successor =
                            AccessV2Geometry.EnumerateStraight(state)
                                .FirstOrDefault();
                        if (successor == null)
                            continue;
                        realSlantedExpensive = firstTransition;
                        realSlantedExpensiveSuccessor = successor;
                        break;
                    }
                    if (realSlantedExpensive != null)
                        break;
                }
                if (realSlantedExpensive != null)
                    break;
            }
            if (realSlantedExpensive == null)
            {
                failure =
                    "The slanted adapter selection fixture requires an ordinary competing exit";
                return false;
            }
            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 0,
                out AccessHeightProfile jaggedFlat);
            var jaggedFixedCorner =
                new AccessHeightProfile(0, 0, 2, 0);
            var jaggedAdapterCorner =
                new AccessHeightProfile(0, 0, 0, 2);
            var jaggedFixedProfiles =
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [new Tile2i(4, 8)] = jaggedFlat,
                    [new Tile2i(4, 12)] = jaggedFixedCorner,
                };
            AccessV2BandProfile.TryCreate(
                AccessV2TravelAxis.X,
                jaggedFlat,
                jaggedFixedCorner,
                includeDeferred: true,
                out AccessV2BandProfile jaggedStartBand,
                out _);
            var jaggedStart = new AccessV2BandState(
                new Tile2i(4, 8),
                jaggedStartBand,
                new Tile2i(4, 0));
            Tile2i jaggedGround =
                AccessV2PotentialField.GetCanonicalCenter(jaggedStart);
            AccessV2SearchSession.GroundToVProfileCandidate
                jaggedCandidate =
                AccessV2SearchSession.EnumerateGroundToVBandProfiles(
                    new Tile2i(8, 8),
                    0f,
                    AccessV2TravelAxis.X,
                    new Tile2i(4, 0),
                    origin => jaggedFixedProfiles.TryGetValue(
                        origin,
                        out AccessHeightProfile fixedProfile)
                            ? fixedProfile
                            : (AccessHeightProfile?)null,
                    origin => origin == new Tile2i(8, 12))
                .FirstOrDefault(candidate =>
                    AccessV2BandProfile.ProfilesEqual(
                        candidate.Lane0, jaggedFlat)
                    && AccessV2BandProfile.ProfilesEqual(
                        candidate.Lane1, jaggedAdapterCorner));
            if (!AccessV2BandProfile.ProfilesEqual(
                    jaggedCandidate.Lane0, jaggedFlat)
                || !AccessV2BandProfile.ProfilesEqual(
                    jaggedCandidate.Lane1, jaggedAdapterCorner)
                || !AccessV2BandProfile.TryCreate(
                    AccessV2TravelAxis.X,
                    jaggedCandidate.Lane0,
                    jaggedCandidate.Lane1,
                    includeDeferred: true,
                    out AccessV2BandProfile jaggedAdapterBand,
                    out _))
            {
                failure =
                    "Jagged fixed fringe must expose the one-lane V-prime adapter used by the A*/Dijkstra fixture";
                return false;
            }
            var jaggedAdapter = new AccessV2BandState(
                new Tile2i(8, 8),
                jaggedAdapterBand,
                new Tile2i(4, 0));
            Tile2i mixedGround = new Tile2i(8, 8);
            Tile2i mixedTravel = new Tile2i(4, 0);
            Tile2i mixedAnchor = AccessV2SearchSession
                .GetGroundToVBandAnchor(mixedGround, mixedTravel);
            Tile2i mixedCompanion = AccessV2Geometry.Add(
                mixedAnchor,
                AccessV2BandProfile.GetLaneDirection(
                    AccessV2TravelAxis.X));
            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 0,
                out AccessHeightProfile mixedFlat);
            AccessV2BandProfile.TryCreateEnabled(
                AccessV2TravelAxis.X, mixedFlat, mixedFlat,
                out AccessV2BandProfile mixedBand, out _);
            var mixedState = new AccessV2BandState(
                mixedAnchor, mixedBand, mixedTravel);
            if (!AccessV2SearchSession.CanLaunchGroundToV(
                    mixedGround,
                    origin => origin == mixedCompanion,
                    origin => origin == mixedAnchor)
                || !AccessV2SearchSession.TryResolveGroundToVTransition(
                    mixedState,
                    origin => origin == mixedAnchor
                        ? mixedFlat
                        : (AccessHeightProfile?)null,
                    origin => origin == mixedCompanion,
                    out AccessV2Transition mixedTransition)
                || mixedTransition.Delta.Count != 1
                || mixedTransition.Delta[0].Origin != mixedCompanion
                || !mixedTransition.LocalContextOrigins.Contains(
                    mixedAnchor)
                || !mixedTransition.ScoreOnlyGeneratedExteriorRays)
            {
                failure = "Projected G must resolve a mixed fixed/generated fringe band without regenerating the fixed lane";
                return false;
            }

            Tile2i fixedAdapterGround = new Tile2i(8, 10);
            Tile2i fixedAdapterTravel = new Tile2i(4, 0);
            Tile2i fixedAdapterAnchor = AccessV2SearchSession
                .GetGroundToVBandAnchor(
                    fixedAdapterGround, fixedAdapterTravel);
            Tile2i fixedAdapterCompanion = AccessV2Geometry.Add(
                fixedAdapterAnchor,
                AccessV2BandProfile.GetLaneDirection(
                    AccessV2TravelAxis.X));
            Tile2i fixedAdapterNext0 = AccessV2Geometry.Add(
                fixedAdapterAnchor, fixedAdapterTravel);
            Tile2i fixedAdapterNext1 = AccessV2Geometry.Add(
                fixedAdapterCompanion, fixedAdapterTravel);
            AccessHeightProfile? FixedAdapterProfile(Tile2i origin)
                => origin == fixedAdapterAnchor
                    || origin == fixedAdapterCompanion
                        ? mixedFlat
                        : (AccessHeightProfile?)null;
            bool FixedAdapterGenerated(Tile2i origin)
                => origin == fixedAdapterNext0
                    || origin == fixedAdapterNext1;
            if (!AccessV2SearchSession
                    .TryCreateFixedGroundToVAdapter(
                        fixedAdapterAnchor,
                        AccessV2TravelAxis.X,
                        fixedAdapterTravel,
                        FixedAdapterProfile,
                        out AccessV2Transition fixedAdapter)
                || fixedAdapter.Kind
                    != AccessV2TransitionKind.ProjectedGroundAdapter
                || fixedAdapter.Delta.Count != 0
                || !AccessV2History.Empty.TryApply(
                    fixedAdapter, out _, out _)
                || !AccessV2Replay.IsProjectedGroundEntryValid(
                    fixedAdapter.Next,
                    AccessV2PotentialField.GetCanonicalCenter(
                        fixedAdapter.Next),
                    FixedAdapterProfile,
                    _ => true,
                    _ => true)
                || AccessV2Replay.IsProjectedGroundEntryValid(
                    fixedAdapter.Next,
                    AccessV2PotentialField.GetCanonicalCenter(
                        fixedAdapter.Next) + new RelTile2i(1, 0),
                    FixedAdapterProfile,
                    _ => true,
                    _ => true)
                || !AccessV2SearchSession
                    .HasGeneratedGroundToVSuccessor(
                        fixedAdapter.Next,
                        FixedAdapterProfile,
                        FixedAdapterGenerated)
                || !AccessV2SearchSession
                    .CanExitGroundComponentToV(
                        fixedAdapterGround,
                        FixedAdapterGenerated,
                        FixedAdapterProfile))
            {
                failure = "Projected G must admit a fully fixed fringe adapter only when its immediate outward successor generates work";
                return false;
            }

            if (!TryCreateUniformState(
                    new Tile2i(4, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState start, out failure)
                || !TryCreateUniformState(
                    new Tile2i(16, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState goalState, out failure))
                return false;

            AccessV2TransitionEvaluation UnitEvaluator(
                AccessV2BandState? current,
                AccessV2Transition transition,
                AccessV2History history,
                Tile2i? connectedFixedOrigin)
                => new AccessV2TransitionEvaluation(
                    true, string.Empty,
                    current.HasValue ? 4f : 0f,
                    transition.Delta.Count,
                    0f);

            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 2,
                out AccessHeightProfile raisedLaunchFlat);
            Tile2i raisedLaunchOrigin = new Tile2i(8, 8);
            AccessV2EndpointSet raisedLaunchCandidates =
                AccessV2FrontageDiscovery.Build(
                    Tile2i.Zero, new Tile2i(32, 32),
                    new Dictionary<Tile2i, AccessHeightProfile>
                    {
                        [raisedLaunchOrigin] = raisedLaunchFlat,
                    },
                    new[] { raisedLaunchOrigin });
            AccessV2StartFrontage raisedLaunch =
                raisedLaunchCandidates.Starts.First(candidate =>
                    candidate.LaunchSuccessor?.Next.Band.Lane0.Center2 == 1
                    && candidate.LaunchSuccessor.Next.Band.Lane1.Center2 == 1);
            Tile2i launchGroundGoal = new Tile2i(24, 24);
            var launchGroundGraph = new AccessV2GroundGraph(
                new[] { launchGroundGoal },
                new[] { launchGroundGoal },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            AccessV2TransitionEvaluation LaunchGroundEvaluator(
                AccessV2BandState? current,
                AccessV2Transition transition,
                AccessV2History history,
                Tile2i? connectedFixedOrigin)
                => new AccessV2TransitionEvaluation(
                    true, string.Empty,
                    current.HasValue ? 4f : 0f,
                    transition.Delta.Count,
                    0f,
                    directWorkCost: 0f,
                    generatedFixedCost: transition.Delta.Count);
            var launchGroundSession = new AccessV2SearchSession(
                new AccessV2EndpointSet(
                    new[] { raisedLaunch },
                    new AccessV2FrontageDiagnostics()),
                Tile2i.Zero, new Tile2i(32, 32),
                LaunchGroundEvaluator, 10000, float.MaxValue,
                (recent, _, requiredGroundEntry) =>
                {
                    AccessV2BandState terminal = recent[0];
                    float spokeCost = terminal.Equals(
                        raisedLaunch.LaunchSuccessor!.Next)
                            ? 20f
                            : 2f;
                    return new[]
                    {
                        new AccessV2HandoffCandidate(
                            terminal.EntryDirection, 1,
                            new AccessGroundHandoff(
                                launchGroundGoal,
                                AccessHandoffOperation.Leveling),
                            new AccessGroundHandoff(
                                launchGroundGoal,
                                AccessHandoffOperation.Leveling),
                            new[] { terminal.GetLaneOrigin(0) },
                            new[] { terminal.GetLaneOrigin(1) },
                            new[] { launchGroundGoal },
                            new[] { launchGroundGoal },
                            Array.Empty<string>(), 0f,
                            isQuickPath: true,
                            centerSpokeCost: spokeCost),
                    };
                },
                groundGraph: launchGroundGraph);
            while (!launchGroundSession.IsComplete)
                launchGroundSession.Step(7);
            if (!launchGroundSession.Result.Success
                || launchGroundSession.Result.States.Count != 3
                || launchGroundSession.Result.GeneratedProfiles.Count != 5)
            {
                failure =
                    "A valid but expensive ramp-to-G handoff must not suppress a cheaper exact-terrain V continuation";
                return false;
            }
            if (!TryCreateUniformState(
                    new Tile2i(16, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState outerTierStart, out failure)
                || !TryCreateUniformState(
                    new Tile2i(24, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState outerTierGoal, out failure))
                return false;
            var tierFallbackEndpoints = new AccessV2EndpointSet(
                new IReadOnlyList<AccessV2StartFrontage>[]
                {
                    new[]
                    {
                        new AccessV2StartFrontage(
                            start, start.Anchor),
                    },
                    new[]
                    {
                        new AccessV2StartFrontage(
                            outerTierStart, outerTierStart.Anchor),
                    },
                },
                new AccessV2FrontageDiagnostics());
            Tile2i tierGroundGoal = new Tile2i(28, 8);
            var tierGroundGraph = new AccessV2GroundGraph(
                new[] { tierGroundGoal },
                new[] { tierGroundGoal },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            IReadOnlyList<AccessV2HandoffCandidate> TierHandoff(
                IReadOnlyList<AccessV2BandState> recent,
                AccessV2History history,
                Tile2i? requiredGroundEntry)
            {
                AccessV2BandState terminal = recent[0];
                if (!terminal.Equals(outerTierGoal))
                    return Array.Empty<AccessV2HandoffCandidate>();
                var handoff = new AccessGroundHandoff(
                    tierGroundGoal, AccessHandoffOperation.Leveling);
                return new[]
                {
                    new AccessV2HandoffCandidate(
                        terminal.EntryDirection, 1,
                        handoff, handoff,
                        new[] { terminal.GetLaneOrigin(0) },
                        new[] { terminal.GetLaneOrigin(1) },
                        new[] { tierGroundGoal },
                        new[] { tierGroundGoal },
                        Array.Empty<string>(), 0f),
                };
            }
            var tierFallback = new AccessV2SearchSession(
                tierFallbackEndpoints,
                Tile2i.Zero, new Tile2i(32, 32),
                (current, transition, history, connectedFixedOrigin) =>
                    current.HasValue && current.Value.Anchor.X < 16
                        ? AccessV2TransitionEvaluation.Reject(
                            "FixtureCentralTierBlocked")
                        : UnitEvaluator(
                            current, transition, history,
                            connectedFixedOrigin),
                10000, float.MaxValue,
                TierHandoff,
                groundGraph: tierGroundGraph);
            while (!tierFallback.IsComplete) tierFallback.Step(7);
            if (!tierFallback.Result.Success
                || tierFallback.Result.States.Count == 0
                || tierFallback.Result.States[0].Anchor
                    != outerTierStart.Anchor)
            {
                failure = "V2 search must advance outward only after the central tier exhausts";
                return false;
            }

            var redundantTierEndpoints = new AccessV2EndpointSet(
                new IReadOnlyList<AccessV2StartFrontage>[]
                {
                    new[]
                    {
                        new AccessV2StartFrontage(
                            start, start.Anchor),
                    },
                    new[]
                    {
                        new AccessV2StartFrontage(
                            start, start.Anchor),
                    },
                },
                new AccessV2FrontageDiagnostics());
            var redundantTierDiagnostics = new AccessSearchDiagnostics();
            var redundantTierSearch = new AccessV2SearchSession(
                redundantTierEndpoints,
                Tile2i.Zero, new Tile2i(32, 32),
                (_, _, _, _) =>
                    AccessV2TransitionEvaluation.Reject(
                        "FixtureAllRoutesBlocked"),
                10000, float.MaxValue,
                TierHandoff,
                groundGraph: tierGroundGraph,
                diagnostics: redundantTierDiagnostics);
            int redundantTierStartPops = 0;
            Tile2i redundantTierStartCenter =
                AccessV2PotentialField.GetCanonicalCenter(start);
            redundantTierSearch.NodeExplored =
                (center, _, _, _) =>
                {
                    if (center == redundantTierStartCenter)
                        redundantTierStartPops++;
                };
            while (!redundantTierSearch.IsComplete)
                redundantTierSearch.Step(7);
            if (redundantTierSearch.Result.Success
                || redundantTierSearch.Result.FailureReason != "NoPath"
                || redundantTierStartPops != 1
                || redundantTierDiagnostics.V2StartTiersAttempted != 2
                || redundantTierDiagnostics.V2RedundantStartTiersSkipped != 1
                || redundantTierDiagnostics.V2RedundantStartSeedsSkipped != 1)
            {
                failure =
                    "A backup source tier containing only an already-explored launch state must be skipped";
                return false;
            }

            var noResourceFallback = new AccessV2SearchSession(
                tierFallbackEndpoints,
                Tile2i.Zero, new Tile2i(32, 32),
                UnitEvaluator,
                maxVisited: 1,
                maxCost: float.MaxValue,
                handoffEvaluator: TierHandoff,
                groundGraph: tierGroundGraph);
            while (!noResourceFallback.IsComplete)
                noResourceFallback.Step(7);
            if (noResourceFallback.Result.Success
                || noResourceFallback.Result.FailureReason
                    != "VisitedLimit")
            {
                failure = "V2 search must not advance to an outer tier after a visited-limit failure";
                return false;
            }

            var noCostFallback = new AccessV2SearchSession(
                tierFallbackEndpoints,
                Tile2i.Zero, new Tile2i(32, 32),
                (current, transition, history, connectedFixedOrigin) =>
                    new AccessV2TransitionEvaluation(
                        true, string.Empty,
                        current.HasValue
                            && current.Value.Anchor.X < 16
                                ? 100f
                                : 0f,
                        0f, 0f),
                maxVisited: 10000,
                maxCost: 1f,
                handoffEvaluator: TierHandoff,
                groundGraph: tierGroundGraph);
            while (!noCostFallback.IsComplete)
                noCostFallback.Step(7);
            if (noCostFallback.Result.Success
                || noCostFallback.Result.FailureReason
                    != "CostLimitExceeded")
            {
                failure = "V2 search must not advance to an outer tier after a cost-limit failure";
                return false;
            }

            const float fixtureFixedOriginCost = 5f;
            float minimumVRate =
                AccessV2CostModel.GetMinimumVTravelCostPerTile(
                    fixtureFixedOriginCost);
            float centerSpoke = AccessV2CostModel.GetCenterSpokeCost(
                fixtureFixedOriginCost);
            if (Math.Abs(minimumVRate - 2.25f) > 0.0001f
                || Math.Abs(centerSpoke - 4.5f) > 0.0001f)
            {
                failure =
                    "V2 minimum travel and center-spoke rates must share the fixed-origin cost";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateHandoffs(out string failure)
        {
            if (!TryCreateUniformState(
                    new Tile2i(4, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState first, out failure)
                || !AccessV2Geometry.TryStraight(
                    first, out AccessV2Transition secondStep, out failure)
                || !AccessV2Geometry.TryStraight(
                    secondStep.Next, out AccessV2Transition thirdStep, out failure)
                || !AccessV2Geometry.TryStraight(
                    thirdStep.Next, out AccessV2Transition fourthStep, out failure))
                return false;

            var groundTiles = new List<Tile2i>();
            for (int y = 0; y <= 20; y++)
                for (int x = 0; x <= 28; x++)
                    if (x != 8 || y != 8)
                        groundTiles.Add(new Tile2i(x, y));
            Tile2i cleanupTile = new Tile2i(8, 8);
            var cleanup = new AccessPropCleanupInfo(
                cleanupTile,
                AccessPropCleanupClass.DenseDebris,
                AccessPropBlockerKind.None,
                false,
                new[]
                {
                    new AccessPropSample(
                        cleanupTile, false, true, true, "debris:seam"),
                });
            var graph = new AccessV2GroundGraph(
                groundTiles,
                new[] { new Tile2i(28, 10) },
                new Dictionary<Tile2i, AccessPropCleanupInfo>
                {
                    [cleanupTile] = cleanup,
                });

            IReadOnlyList<AccessGroundHandoff> Single(
                Tile2i origin,
                AccessHeightProfile profile,
                Tile2i predecessor,
                AccessHeightProfile predecessorProfile)
            {
                Tile2i outward = new Tile2i(
                    Math.Sign(origin.X - predecessor.X),
                    Math.Sign(origin.Y - predecessor.Y));
                AccessHandoffOperation operation = (origin.X + origin.Y) % 8 == 0
                    ? AccessHandoffOperation.Mining
                    : AccessHandoffOperation.Dumping;
                return Enumerable.Range(0, 5)
                    .Select(offset => outward.X != 0
                        ? new Tile2i(
                            origin.X + (outward.X > 0 ? 4 : 0),
                            origin.Y + offset)
                        : new Tile2i(
                            origin.X + offset,
                            origin.Y + (outward.Y > 0 ? 4 : 0)))
                    .Select(contact => new AccessGroundHandoff(
                        contact, operation, new[] { contact }))
                    .ToArray();
            }

            IReadOnlyList<AccessGroundHandoff> Span(
                IReadOnlyList<AccessHandoffSpanCell> cells)
            {
                AccessHandoffSpanCell last = cells[cells.Count - 1];
                return Enumerable.Range(0, 5)
                    .Select(offset => new Tile2i(
                        last.Origin.X + 4, last.Origin.Y + offset))
                    .Select(contact => new AccessGroundHandoff(
                        contact, AccessHandoffOperation.Mining,
                        new[] { contact }, cells.Count))
                    .ToArray();
            }

            IReadOnlyList<AccessGroundHandoff> UniformSingle(
                Tile2i origin,
                AccessHeightProfile profile,
                Tile2i predecessor,
                AccessHeightProfile predecessorProfile)
                => Single(origin, profile, predecessor, predecessorProfile)
                    .Select(candidate => new AccessGroundHandoff(
                        candidate.Tile, AccessHandoffOperation.Mining,
                        candidate.EscapeTiles, candidate.SpanLength))
                    .ToArray();

            var mixedDiagnostics = new AccessSearchDiagnostics();
            IReadOnlyList<AccessV2HandoffCandidate> candidates =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, Single, Span,
                    centerSpokeCost: 4.5f,
                    diagnostics: mixedDiagnostics);
            if (candidates.Count != 0
                || mixedDiagnostics.V2MixedLanePairRejects == 0)
            {
                failure = "V2 handoffs must reject mixed mining/dumping lane pairs before corridor evaluation";
                return false;
            }
            IReadOnlyList<AccessV2HandoffCandidate> mixedQuick =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, Single, Span, vehicleWidth: 5);
            if (mixedQuick.Count != 0)
            {
                failure = "V2 legacy quick handoffs must also reject mixed mining/dumping lanes";
                return false;
            }

            candidates = AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, UniformSingle, Span,
                    centerSpokeCost: 4.5f);
            AccessV2HandoffCandidate? forward = candidates.FirstOrDefault(
                item => item.ExitDirection == new Tile2i(4, 0));
            if (forward == null
                || forward.Lane0Operation != forward.Lane1Operation
                || Math.Abs(forward.CenterSpokeCost - 4.5f) > 0.0001f
                || Math.Abs(forward.CleanupCost - 8f) > 0.0001f)
            {
                failure = "V2 forward seam must retain uniform lane operation, cleanup, and the configured center spoke"
                    + $": candidates={candidates.Count}"
                    + $" forward={(forward == null ? "none" : forward.ToString())}"
                    + $" cleanup={(forward == null ? -1f : forward.CleanupCost)}"
                    + $" spoke={(forward == null ? -1f : forward.CenterSpokeCost)}";
                return false;
            }

            IReadOnlyList<AccessV2HandoffCandidate> quickCandidates =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, UniformSingle, Span,
                    vehicleWidth: 5);
            if (quickCandidates.Count != 1
                || !quickCandidates[0].IsQuickPath
                || quickCandidates[0].EscapeCenters.Count != 1
                || !graph.IsTraversable(quickCandidates[0].EscapeCenters[0]))
            {
                failure = "V2 quick handoff must accept a local situation-pathable Mega center lane without requiring goal reachability";
                return false;
            }

            var disconnectedLocalGraph = new AccessV2GroundGraph(
                new[] { new Tile2i(8, 6) },
                Array.Empty<Tile2i>(),
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            IReadOnlyList<AccessV2HandoffCandidate> disconnectedQuick =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    disconnectedLocalGraph, UniformSingle, Span,
                    vehicleWidth: 5);
            if (disconnectedQuick.Count != 1
                || !disconnectedQuick[0].IsQuickPath)
            {
                failure = "V2 quick handoff must be a local V-to-G transition and must not require a finite tower-goal distance";
                return false;
            }

            if (!AccessV2History.Empty.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(
                            new Tile2i(24, 24), first.Band.Lane0),
                    },
                    Array.Empty<Tile2i>(),
                    new[]
                    {
                        new AccessRayHeightConstraint(
                            new Tile2i(8, 8),
                            AccessSideRayOperation.Cut, 0f),
                    },
                    Array.Empty<string>(),
                    out AccessV2History rayBlockedQuickHistory,
                    out string quickHistoryReason))
            {
                failure = "V2 quick handoff ray fixture history failed: "
                    + quickHistoryReason;
                return false;
            }

            Tile2i ownedRayOrigin = first.GetLaneOrigin(0);
            if (!AccessV2History.Empty.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(
                            ownedRayOrigin, first.Band.Lane0),
                    },
                    Array.Empty<Tile2i>(),
                    new[]
                    {
                        new AccessRayHeightConstraint(
                            new Tile2i(8, 4),
                            AccessSideRayOperation.Fill, 1f,
                            ownedRayOrigin),
                        new AccessRayHeightConstraint(
                            new Tile2i(8, 8),
                            AccessSideRayOperation.Cut, 0f,
                            ownedRayOrigin),
                    },
                    Array.Empty<string>(),
                    out AccessV2History ownedRayHistory,
                    out string ownedRayHistoryReason))
            {
                failure = "V2 owned-ray fixture history failed: "
                    + ownedRayHistoryReason;
                return false;
            }
            if (!ownedRayHistory.IsProfileBlockedByRayEnvelope(
                    secondStep.Next.GetLaneOrigin(0),
                    secondStep.Next.Band.Lane0,
                    out AccessSideRayOperation ownedBlock)
                || ownedBlock != AccessSideRayOperation.Fill
                || !ownedRayHistory.IsProfileBlockedByRayEnvelope(
                    secondStep.Next.GetLaneOrigin(0),
                    secondStep.Next.Band.Lane0,
                    new[] { ownedRayOrigin },
                    out _))
            {
                failure = "V2 predecessor clearance waivers must retain the predecessor work surface";
                return false;
            }
            Tile2i collapsedRayTile = new Tile2i(18, 18);
            Tile2i olderRayOwner = new Tile2i(4, 4);
            Tile2i strongerRayOwner = new Tile2i(8, 8);
            AccessV2History collapsedRayHistory =
                AccessV2History.Empty.ApplyValidated(
                    Array.Empty<AccessV2OriginProfile>(),
                    new[]
                    {
                        new AccessRayHeightConstraint(
                            collapsedRayTile,
                            AccessSideRayOperation.Cut, 4f,
                            olderRayOwner),
                        new AccessRayHeightConstraint(
                            collapsedRayTile,
                            AccessSideRayOperation.Cut, 3f,
                            olderRayOwner),
                        new AccessRayHeightConstraint(
                            collapsedRayTile,
                            AccessSideRayOperation.Cut, 1f,
                            strongerRayOwner),
                    },
                    Array.Empty<string>());
            var rayOverlayDiagnostics = new AccessSearchDiagnostics();
            if (collapsedRayHistory.RayConstraintCount != 3
                || collapsedRayHistory.CollapsedRayEntryCount != 2
                || !collapsedRayHistory.IsProfileBlockedByRayEnvelope(
                    new Tile2i(16, 16),
                    new AccessHeightProfile(4, 4, 4, 4),
                    out AccessSideRayOperation collapsedBlock)
                || collapsedBlock != AccessSideRayOperation.Cut
                || !collapsedRayHistory.IsProfileBlockedByRayEnvelope(
                    new Tile2i(16, 16),
                    new AccessHeightProfile(4, 4, 4, 4),
                    new[] { strongerRayOwner }, out _)
                || !collapsedRayHistory.HasRayAt(
                    collapsedRayTile, AccessSideRayOperation.Cut,
                    new[] { strongerRayOwner }, rayOverlayDiagnostics)
                || !collapsedRayHistory.HasRayAt(
                    collapsedRayTile, AccessSideRayOperation.Cut,
                    new[] { strongerRayOwner }, rayOverlayDiagnostics)
                || rayOverlayDiagnostics.V2RayOverlayCacheHits == 0
                || !collapsedRayHistory.HasRayAt(
                    collapsedRayTile, AccessSideRayOperation.Cut,
                    new[] { olderRayOwner, strongerRayOwner }))
            {
                failure = "V2 ray overlay must collapse owner extrema, retain work across safety waivers, and memoize repeated tile queries";
                return false;
            }
            var outsideRayBoundsDiagnostics =
                new AccessSearchDiagnostics();
            if (collapsedRayHistory.HasRayAt(
                    new Tile2i(200, 200),
                    AccessSideRayOperation.Cut,
                    diagnostics: outsideRayBoundsDiagnostics)
                || outsideRayBoundsDiagnostics
                    .V2RayOverlayParentSteps != 0
                || outsideRayBoundsDiagnostics
                    .V2RayOverlayCacheEntries != 0)
            {
                failure = "V2 ray overlay must reject tiles outside the accumulated ray bounds without scanning or caching ancestry";
                return false;
            }
            Tile2i safetyTile = new Tile2i(22, 22);
            var eastScopeTransition = new AccessV2Transition(
                AccessV2TransitionKind.SourceLaunch,
                first,
                new[] { first.GetLane(0) },
                Array.Empty<Tile2i>());
            AccessV2History directionScopeHistory =
                AccessV2History.Empty.ApplyValidated(
                    eastScopeTransition,
                    new[]
                    {
                        new AccessRayHeightConstraint(
                            safetyTile,
                            AccessSideRayOperation.Cut,
                            0f,
                            first.GetLaneOrigin(0),
                            isSafetyOnly: true),
                    },
                    Array.Empty<string>());
            AccessProjectedTerrainEffect safetyEffect =
                directionScopeHistory.GetProjectedTerrainEffect(safetyTile);
            AccessProjectedTerrainEffect waivedSafetyEffect =
                directionScopeHistory.GetProjectedTerrainEffect(
                    safetyTile, includeSafety: false);
            if (!safetyEffect.HasCutSafety
                || safetyEffect.HasCutWork
                || waivedSafetyEffect.HasCutSafety
                || directionScopeHistory.HasRayAt(
                    safetyTile, AccessSideRayOperation.Cut)
                || !directionScopeHistory.HasGeneratedProfileAt(
                    first.GetLaneOrigin(0) + new RelTile2i(2, 2))
                || directionScopeHistory.HasGeneratedProfileAt(
                    first.GetLaneOrigin(0) + new RelTile2i(2, 2),
                    new[] { first.GetLaneOrigin(0) })
                || directionScopeHistory.RequiresStrictSelfDisruptionChecks)
            {
                failure = "V2 safety-only overlay cells must carry no height and remain waived inside the initial direction scope";
                return false;
            }
            if (!TryCreateUniformState(
                    new Tile2i(20, 20), AccessV2TravelAxis.Y,
                    new Tile2i(0, 4), AccessSearchMode.Flat, 0,
                    out AccessV2BandState northScopeState,
                    out failure)
                || !TryCreateUniformState(
                    new Tile2i(20, 20), AccessV2TravelAxis.X,
                    new Tile2i(-4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState westScopeState,
                    out failure))
                return false;
            directionScopeHistory = directionScopeHistory.ApplyValidated(
                new AccessV2Transition(
                    AccessV2TransitionKind.Turn,
                    northScopeState,
                    Array.Empty<AccessV2OriginProfile>(),
                    Array.Empty<Tile2i>()),
                Array.Empty<AccessRayHeightConstraint>(),
                Array.Empty<string>());
            if (directionScopeHistory.RequiresStrictSelfDisruptionChecks)
            {
                failure = "V2 self-disruption must remain waived through two longitudinal directions";
                return false;
            }
            directionScopeHistory = directionScopeHistory.ApplyValidated(
                new AccessV2Transition(
                    AccessV2TransitionKind.Turn,
                    westScopeState,
                    Array.Empty<AccessV2OriginProfile>(),
                    Array.Empty<Tile2i>()),
                Array.Empty<AccessRayHeightConstraint>(),
                Array.Empty<string>());
            if (!directionScopeHistory.RequiresStrictSelfDisruptionChecks)
            {
                failure = "V2 self-disruption checks must become strict only after a third longitudinal direction";
                return false;
            }
            AccessV2History resetScopeHistory =
                directionScopeHistory.ResetDirectionScope();
            if (resetScopeHistory.RequiresStrictSelfDisruptionChecks
                || !resetScopeHistory.GetProjectedTerrainEffect(
                    safetyTile).HasCutSafety
                || !resetScopeHistory.HasGeneratedProfileAt(
                    first.GetLaneOrigin(0) + new RelTile2i(2, 2)))
            {
                failure = "V2 G/FV scope reset must clear travel directions without discarding route-wide projected terrain";
                return false;
            }
            IReadOnlyList<AccessV2HandoffCandidate> ownedRayQuick =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, ownedRayHistory,
                    graph, UniformSingle, Span,
                    vehicleWidth: 5);
            if (ownedRayQuick.Count != 1
                || !ownedRayQuick[0].IsQuickPath)
            {
                failure = "V2 quick handoff must ignore the current generated lane's own fringe ray";
                return false;
            }
            IReadOnlyList<AccessV2HandoffCandidate> rayBlockedQuick =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, rayBlockedQuickHistory,
                    graph, UniformSingle, Span,
                    vehicleWidth: 5);
            if (rayBlockedQuick.Count == 0
                || rayBlockedQuick.Any(item => item.IsQuickPath))
            {
                failure = "V2 quick handoff must defer to the general seam when a history ray crosses every pre-approved center lane";
                return false;
            }

            IReadOnlyList<AccessV2HandoffCandidate> projectedRejected =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, UniformSingle, Span, 1f,
                    (center, _) => center != cleanupTile);
            if (projectedRejected.Any(
                    item => item.ExitDirection == new Tile2i(4, 0)))
            {
                failure = "V2 seam must reject a contact whose resolved-vehicle footprint is unpathable after projected landscaping";
                return false;
            }

            IReadOnlyList<AccessV2HandoffCandidate> projectedExtended =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, UniformSingle, Span, 1f,
                    (center, _) => true,
                    (center, _) => center.X < 10);
            AccessV2HandoffCandidate? extendedForward =
                projectedExtended.FirstOrDefault(
                    item => item.ExitDirection == new Tile2i(4, 0));
            if (extendedForward == null
                || !extendedForward.EscapeCenters.Contains(
                    new Tile2i(10, 7))
                || !extendedForward.EscapeCenters.Contains(
                    new Tile2i(10, 8)))
            {
                failure = "V2 seam must extend each escape until the complete resolved-vehicle mask clears projected work"
                    + $": forward={(extendedForward == null ? "none" : extendedForward.ToString())}"
                    + $" centers={(extendedForward == null ? "none" : string.Join(",", extendedForward.EscapeCenters))}";
                return false;
            }

            IReadOnlyList<AccessV2HandoffCandidate> postWorkCorridor =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, UniformSingle, Span,
                    projectedCenterValidator: (center, history) => false,
                    postWorkCenterValidator:
                        (origin, operation, center, history, handoffOrigins) => true);
            AccessV2HandoffCandidate? corridorForward = postWorkCorridor
                .FirstOrDefault(item =>
                    item.ExitDirection == new Tile2i(4, 0));
            int corridorMinY = Math.Min(
                first.GetLaneOrigin(0).Y,
                first.GetLaneOrigin(1).Y);
            if (corridorForward == null
                || corridorForward.IsQuickPath
                || corridorForward.EscapeCenters.Count != 4
                || corridorForward.EscapeCenters[0].X
                    != first.GetLaneOrigin(0).X + 1
                || corridorForward.EscapeCenters[0].Y < corridorMinY + 2
                || corridorForward.EscapeCenters[0].Y > corridorMinY + 5
                || corridorForward.GroundEntryCenters.Count != 1
                || corridorForward.GroundEntryCenters[0].X
                    != first.GetLaneOrigin(0).X + 4)
            {
                failure = "V2 post-work handoff must start on rank two inside files three through six and end on G without reapplying the projected-target slope test"
                    + $": candidate={(corridorForward == null ? "none" : corridorForward.ToString())}"
                    + $" escape={(corridorForward == null ? "none" : string.Join(",", corridorForward.EscapeCenters))}";
                return false;
            }

            var delayedGroundGraph = new AccessV2GroundGraph(
                groundTiles.Where(tile => tile.X != 8 && tile.X != 9),
                new[] { new Tile2i(28, 10) },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            IReadOnlyList<AccessV2HandoffCandidate> delayedGroundCorridor =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    delayedGroundGraph, UniformSingle, Span,
                    postWorkCenterValidator:
                        (origin, operation, center, history, handoffOrigins) => true,
                    groundEntryValidator:
                        (center, handoffOrigins, history) => true);
            AccessV2HandoffCandidate? delayedForward = delayedGroundCorridor
                .FirstOrDefault(item =>
                    item.ExitDirection == new Tile2i(4, 0));
            if (delayedForward == null
                || delayedForward.GroundEntryCenters.Count != 1
                || delayedForward.GroundEntryCenters[0].X != 10
                || !delayedForward.EscapeCenters.Any(tile => tile.X == 8)
                || !delayedForward.EscapeCenters.Any(tile => tile.X == 9)
                || Math.Abs(delayedForward.CenterSpokeCost - 4f) > 0.0001f)
            {
                failure = "V2 post-work handoff must keep and cost its center spoke in V until the resolved vehicle mask reaches captured G"
                    + $": candidate={(delayedForward == null ? "none" : delayedForward.ToString())}"
                    + $" escape={(delayedForward == null ? "none" : string.Join(",", delayedForward.EscapeCenters))}"
                    + $" spoke={(delayedForward == null ? -1f : delayedForward.CenterSpokeCost)}";
                return false;
            }

            Tile2i lane1GroundEntry = new Tile2i(
                first.GetLaneOrigin(0).X + 4,
                corridorMinY + 5);
            IReadOnlyList<AccessV2HandoffCandidate> wrongOwnerCorridor =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, UniformSingle, Span,
                    postWorkCenterValidator:
                        (origin, operation, center, history, handoffOrigins) =>
                            center.X < first.GetLaneOrigin(0).X + 4
                                || origin == first.GetLaneOrigin(0),
                    requiredGroundEntry: lane1GroundEntry);
            if (wrongOwnerCorridor.Any(candidate =>
                    candidate.ExitDirection == new Tile2i(4, 0)))
            {
                failure =
                    "V2 post-work handoff must classify an outside spoke by the lane containing its vehicle center";
                return false;
            }

            Tile2i alternateGroundEntry = new Tile2i(
                first.GetLaneOrigin(0).X + 4,
                corridorMinY + 5);
            IReadOnlyList<AccessV2HandoffCandidate> requiredCorridorEntry =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, UniformSingle, Span,
                    postWorkCenterValidator:
                        (origin, operation, center, history, handoffOrigins) => true,
                    requiredGroundEntry: alternateGroundEntry);
            if (!requiredCorridorEntry.Any(candidate =>
                    candidate.ExitDirection == new Tile2i(4, 0)
                    && candidate.GroundEntryCenters.Count == 1
                    && candidate.GroundEntryCenters[0]
                        == alternateGroundEntry))
            {
                failure = "V2 reverse G-to-V proof must target the requested reachable G entry instead of a different middle file";
                return false;
            }

            IReadOnlyList<AccessGroundHandoff> LevelingSingle(
                Tile2i origin,
                AccessHeightProfile profile,
                Tile2i predecessor,
                AccessHeightProfile predecessorProfile)
            {
                // Only one terminal cell exposes a level bridge. The paired
                // surface must still be approved for leveling as a whole.
                if (origin != first.GetLaneOrigin(0))
                    return Array.Empty<AccessGroundHandoff>();
                Tile2i outward = new Tile2i(
                    Math.Sign(origin.X - predecessor.X),
                    Math.Sign(origin.Y - predecessor.Y));
                return Enumerable.Range(0, 5)
                    .Select(offset => outward.X != 0
                        ? new Tile2i(
                            origin.X + (outward.X > 0 ? 4 : 0),
                            origin.Y + offset)
                        : new Tile2i(
                            origin.X + offset,
                            origin.Y + (outward.Y > 0 ? 4 : 0)))
                    .Select(contact => new AccessGroundHandoff(
                        contact, AccessHandoffOperation.Leveling,
                        new[] { contact }))
                    .ToArray();
            }
            int levelingCenterChecks = 0;
            Tile2i levelingEntry = new Tile2i(
                first.GetLaneOrigin(0).X + 4,
                first.GetLaneOrigin(0).Y + 3);
            IReadOnlyList<AccessV2HandoffCandidate> levelingBridge =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, LevelingSingle, Span,
                    postWorkCenterValidator:
                        (origin, operation, center, history, handoffOrigins) =>
                        {
                            levelingCenterChecks++;
                            return false;
                        },
                    requiredGroundEntry: levelingEntry);
            AccessV2HandoffCandidate? leveled = levelingBridge.FirstOrDefault(
                candidate =>
                    candidate.ExitDirection == new Tile2i(4, 0)
                    && candidate.GroundEntryCenters.Contains(levelingEntry));
            if (leveled == null
                || !leveled.IsQuickPath
                || leveled.Lane0Operation != AccessHandoffOperation.Leveling
                || leveled.Lane1Operation != AccessHandoffOperation.Leveling
                || leveled.EscapeCenters.Count != 4
                || leveled.EscapeCenters[leveled.EscapeCenters.Count - 1]
                    != levelingEntry
                || levelingCenterChecks != 0)
            {
                failure = "V2 leveling bridge must bypass post-work center classification and BFS"
                    + $": candidate={(leveled == null ? "none" : leveled.ToString())}"
                    + $" checks={levelingCenterChecks}";
                return false;
            }
            IReadOnlyList<AccessV2HandoffCandidate> delayedLevelingBridge =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    delayedGroundGraph, LevelingSingle, Span,
                    postWorkCenterValidator:
                        (origin, operation, center, history, handoffOrigins) =>
                            false,
                    groundEntryValidator:
                        (center, handoffOrigins, history) => true,
                    vehicleWidth: 5);
            AccessV2HandoffCandidate? delayedLeveling =
                delayedLevelingBridge.FirstOrDefault(candidate =>
                    candidate.ExitDirection == new Tile2i(4, 0));
            if (delayedLeveling == null
                || delayedLeveling.GroundEntryCenters.Count != 1
                || delayedLeveling.GroundEntryCenters[0].X != 10
                || delayedLeveling.EscapeCenters[
                    delayedLeveling.EscapeCenters.Count - 1].X != 10)
            {
                failure =
                    "V2 leveling handoff must advance outward across its proven seam to the first captured G center"
                    + $": candidate={(delayedLeveling == null ? "none" : delayedLeveling.ToString())}"
                    + $" escape={(delayedLeveling == null ? "none" : string.Join(",", delayedLeveling.EscapeCenters))}";
                return false;
            }

            IReadOnlyList<AccessV2HandoffCandidate> brokenPostWorkCorridor =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, UniformSingle, Span,
                    postWorkCenterValidator:
                        (origin, operation, center, history, handoffOrigins) =>
                            center.X != origin.X + 2);
            if (brokenPostWorkCorridor.Any(item =>
                    item.ExitDirection == new Tile2i(4, 0)))
            {
                failure = "V2 post-work handoff must reject a full-rank break between rank two and G";
                return false;
            }

            IReadOnlyList<AccessV2HandoffCandidate> recent =
                AccessV2Handoffs.Evaluate(
                    new[]
                    {
                        fourthStep.Next, thirdStep.Next,
                        secondStep.Next, first,
                    },
                    AccessV2History.Empty,
                    graph, UniformSingle, Span);
            if (!recent.Any(item => item.SpanLength == 2)
                || !recent.Any(item => item.SpanLength == 3)
                || !recent.Any(item => item.SpanLength == 4)
                || !recent.Any(item => item.ExitDirection.Y != 0))
            {
                failure = "V2 seam must expose lateral exits and common two-/three-/four-row spans";
                return false;
            }

            var splitGround = new AccessV2GroundGraph(
                new[]
                {
                    new Tile2i(8, 6), new Tile2i(9, 6),
                    new Tile2i(8, 10), new Tile2i(9, 10),
                },
                new[] { new Tile2i(9, 6), new Tile2i(9, 10) },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            if (AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    splitGround, Single, Span).Count != 0)
            {
                failure = "V2 seam must reject lane contacts split across disconnected Mega components";
                return false;
            }

            var endpoints = new AccessV2EndpointSet(
                new[] { new AccessV2StartFrontage(first, first.Anchor) },
                new AccessV2FrontageDiagnostics());

            IReadOnlyList<AccessGroundHandoff> PartialMiningSingle(
                Tile2i origin,
                AccessHeightProfile profile,
                Tile2i predecessor,
                AccessHeightProfile predecessorProfile)
                => origin == secondStep.Next.GetLaneOrigin(0)
                    ? new[]
                    {
                        new AccessGroundHandoff(
                            origin + new RelTile2i(4, 2),
                            AccessHandoffOperation.Mining),
                    }
                    : Array.Empty<AccessGroundHandoff>();
            if (!PartialMiningSingle(
                    secondStep.Next.GetLaneOrigin(0),
                    secondStep.Next.GetLane(0).Profile,
                    first.GetLaneOrigin(0),
                    first.GetLane(0).Profile).Any(item =>
                        item.Operation == AccessHandoffOperation.Mining))
            {
                failure = "V2 one-lane mining crest must remain visible to the bounded terminal evaluator";
                return false;
            }

            AccessV2TransitionEvaluation UnitEvaluator(
                AccessV2BandState? current,
                AccessV2Transition transition,
                AccessV2History history,
                Tile2i? connectedFixedOrigin)
                => new AccessV2TransitionEvaluation(
                    true, string.Empty,
                    current.HasValue ? 4f : 0f,
                    transition.Delta.Count, 0f);

            var session = new AccessV2SearchSession(
                endpoints, Tile2i.Zero, new Tile2i(32, 32),
                UnitEvaluator, 10000, float.MaxValue,
                (states, history, requiredGroundEntry) =>
                    states[0].Anchor == first.Anchor
                    ? new[] { forward }
                    : Array.Empty<AccessV2HandoffCandidate>(),
                groundGraph: graph);
            while (!session.IsComplete) session.Step(7);
            float expectedGroundDistance = forward.GroundEntryCenters
                .Select(center => graph.TryGetGoalDistance(center, out float distance)
                    ? distance : float.PositiveInfinity)
                .Min();
            float expectedTerminalCost = forward.TotalCost + expectedGroundDistance;
            float expectedTerminalTravel =
                forward.CenterSpokeCost + expectedGroundDistance;
            if (!session.Result.Success
                || session.Result.Handoff == null
                || session.Result.UsedAStar
                || Math.Abs(session.Result.Cost - expectedTerminalCost) > 0.0001f
                || Math.Abs(session.Result.TraversalCost
                    - expectedTerminalTravel) > 0.0001f)
            {
                failure = "V2 Dijkstra must cost exact G travel and retain a ground handoff terminal: "
                    + $"success={session.Result.Success} reason={session.Result.FailureReason} "
                    + $"cost={session.Result.Cost}/{expectedTerminalCost} "
                    + $"travel={session.Result.TraversalCost}/{expectedTerminalTravel} "
                    + $"ground={session.Result.GroundPath.Count} "
                    + $"visited={session.Result.Visited} pending={session.Result.Pending} "
                    + $"handoffs={session.Result.HandoffEvaluations}/{session.Result.QuickHandoffAccepts} "
                    + $"rejects={string.Join(",", session.Result.Rejections.Select(pair => pair.Key + ":" + pair.Value))}";
                return false;
            }

            // Ground feasibility is history-qualified. A cheaper arrival can
            // project work across the only goal suffix while a later arrival
            // at the same concrete center leaves that suffix usable. The
            // latter must not be erased by center-only label dominance.
            Tile2i sharedHistoryGround = new Tile2i(20, 20);
            Tile2i historyBlockedGround = new Tile2i(21, 20);
            Tile2i historyGroundGoal = new Tile2i(22, 20);
            var historyGroundGraph = new AccessV2GroundGraph(
                new[]
                {
                    sharedHistoryGround,
                    historyBlockedGround,
                    historyGroundGoal,
                },
                new[] { historyGroundGoal },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            AccessV2HandoffCandidate HistoryHandoff(
                AccessV2BandState state,
                float cleanupCost,
                string historyKey)
                => new AccessV2HandoffCandidate(
                    state.EntryDirection, 1,
                    new AccessGroundHandoff(
                        state.GetLaneOrigin(0),
                        AccessHandoffOperation.Leveling),
                    new AccessGroundHandoff(
                        state.GetLaneOrigin(1),
                        AccessHandoffOperation.Leveling),
                    new[] { state.GetLaneOrigin(0) },
                    new[] { state.GetLaneOrigin(1) },
                    new[] { sharedHistoryGround },
                    new[] { sharedHistoryGround },
                    new[] { historyKey }, cleanupCost,
                    centerSpokeCost: 2f);
            AccessV2BandState validHistoryState = secondStep.Next;
            var historyQualifiedEndpoints = new AccessV2EndpointSet(
                new[]
                {
                    new AccessV2StartFrontage(first, first.Anchor),
                    new AccessV2StartFrontage(
                        validHistoryState, validHistoryState.Anchor),
                },
                new AccessV2FrontageDiagnostics());
            var historyQualifiedGroundSession = new AccessV2SearchSession(
                historyQualifiedEndpoints,
                Tile2i.Zero, new Tile2i(32, 32),
                (current, transition, history, connectedFixedOrigin) =>
                    AccessV2TransitionEvaluation.Reject(
                        "FixtureOnlyGroundHandoffs"),
                1000, float.MaxValue,
                (states, history, requiredGroundEntry) =>
                    states[0].Equals(first)
                        ? new[]
                        {
                            HistoryHandoff(first, 0f, "blocked-history"),
                        }
                        : states[0].Equals(validHistoryState)
                            ? new[]
                            {
                                HistoryHandoff(
                                    validHistoryState, 10f,
                                    "valid-history"),
                            }
                            : Array.Empty<AccessV2HandoffCandidate>(),
                groundGraph: historyGroundGraph,
                groundValidator: (center, history) =>
                    center != historyBlockedGround
                    || history.ContainsCleanupKey("valid-history"));
            while (!historyQualifiedGroundSession.IsComplete)
                historyQualifiedGroundSession.Step(7);
            if (!historyQualifiedGroundSession.Result.Success
                || !historyQualifiedGroundSession.Result.GroundPath.Contains(
                    historyGroundGoal))
            {
                failure =
                    "V2 ground dominance must retain a later history-qualified arrival when the cheaper history blocks the only goal suffix"
                    + $": success={historyQualifiedGroundSession.Result.Success}"
                    + $" reason={historyQualifiedGroundSession.Result.FailureReason}"
                    + $" ground=[{string.Join(",", historyQualifiedGroundSession.Result.GroundPath)}]";
                return false;
            }

            // A later history-qualified arrival may be retained at its entry
            // center while every ordinary-G successor is already cheaper.
            // Cost dominance must reject that successor before invoking the
            // history-sensitive validator, while the earlier productive
            // arrival still exercises the real ground-expansion seam.
            Tile2i dominanceGround = new Tile2i(24, 20);
            Tile2i dominanceNeighbor = new Tile2i(25, 20);
            Tile2i isolatedDominanceGoal = new Tile2i(30, 30);
            var dominanceGroundGraph = new AccessV2GroundGraph(
                new[]
                {
                    dominanceGround,
                    dominanceNeighbor,
                    isolatedDominanceGoal,
                },
                new[] { isolatedDominanceGoal },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            AccessV2HandoffCandidate DominanceHandoff(
                AccessV2BandState state,
                float cleanupCost,
                string historyKey)
                => new AccessV2HandoffCandidate(
                    state.EntryDirection, 1,
                    new AccessGroundHandoff(
                        state.GetLaneOrigin(0),
                        AccessHandoffOperation.Leveling),
                    new AccessGroundHandoff(
                        state.GetLaneOrigin(1),
                        AccessHandoffOperation.Leveling),
                    new[] { state.GetLaneOrigin(0) },
                    new[] { state.GetLaneOrigin(1) },
                    new[] { dominanceGround },
                    new[] { dominanceGround },
                    new[] { historyKey }, cleanupCost,
                    centerSpokeCost: 2f);
            int firstGroundValidationCalls = 0;
            int dominatedGroundValidationCalls = 0;
            int handoffGroundExpansions = 0;
            var optimisticDominanceSession = new AccessV2SearchSession(
                endpoints,
                Tile2i.Zero, new Tile2i(32, 32),
                UnitEvaluator,
                1000, float.MaxValue,
                (states, history, requiredGroundEntry) =>
                    states[0].Equals(first)
                        ? new[]
                        {
                            DominanceHandoff(
                                first, 0f, "first-dominance-history"),
                        }
                        : states[0].Equals(validHistoryState)
                            ? new[]
                            {
                                DominanceHandoff(
                                    validHistoryState, 10f,
                                    "dominated-history"),
                            }
                            : Array.Empty<AccessV2HandoffCandidate>(),
                groundGraph: dominanceGroundGraph,
                groundValidator: (center, history) =>
                {
                    if (center != dominanceNeighbor)
                        return true;
                    if (history.ContainsCleanupKey("dominated-history"))
                        dominatedGroundValidationCalls++;
                    else if (history.ContainsCleanupKey(
                        "first-dominance-history"))
                        firstGroundValidationCalls++;
                    return true;
                });
            optimisticDominanceSession.ExpansionTraced = trace =>
            {
                if (trace.IsGround
                    && trace.HasHandoff
                    && trace.Center == dominanceGround)
                    handoffGroundExpansions++;
            };
            while (!optimisticDominanceSession.IsComplete)
                optimisticDominanceSession.Step(7);
            if (firstGroundValidationCalls == 0
                || dominatedGroundValidationCalls != 0
                || handoffGroundExpansions != 1)
            {
                failure =
                    "V2 history-qualified ground entries must be rejected before expansion when every possible ordinary-G consequence is optimistically dominated"
                    + $": first={firstGroundValidationCalls}"
                    + $" dominated={dominatedGroundValidationCalls}"
                    + $" handoffExpansions={handoffGroundExpansions}";
                return false;
            }
            var wrappedV2Session =
                new AccessPathSearch.AccessPathSearchSession(
                    session, first.GetLaneOrigin(0),
                    new AccessSearchDiagnostics());
            if (!wrappedV2Session.Result.Success
                || wrappedV2Session.Result.V2Route == null
                || wrappedV2Session.Result.V2Route.VehicleWidth != 5)
            {
                failure =
                    "The AccessPathSearch V2 wrapper must convert a completed V2 session without consulting the uninitialized V1 snapshot";
                return false;
            }

            var cacheDiagnostics = new AccessSearchDiagnostics();
            var cacheSession = new AccessV2SearchSession(
                endpoints, Tile2i.Zero, new Tile2i(32, 32),
                UnitEvaluator, 10000, float.MaxValue,
                (states, history, requiredGroundEntry) =>
                    states[0].Anchor == first.Anchor
                    ? new[] { forward }
                    : Array.Empty<AccessV2HandoffCandidate>(),
                groundGraph: graph,
                terrainCenterHeightProvider: _ => 0,
                preciseTerrainHeightProvider: _ => 0.25f,
                diagnostics: cacheDiagnostics,
                groundToVHandoffEvaluator:
                    (state, groundEntry, operation, history) =>
                        AccessV2Handoffs
                            .TryCreateDeterministicGroundToVBridge(
                                state, groundEntry, operation, 5, 2f,
                                (Tile2i tile, out float height) =>
                                {
                                    height = 0f;
                                    return true;
                                },
                                _ => false,
                                out AccessV2HandoffCandidate seam,
                                cacheDiagnostics)
                                    ? seam
                                    : null);
            while (!cacheSession.IsComplete) cacheSession.Step(31);
            if (!cacheSession.Result.Success
                || cacheDiagnostics.V2GroundToVCacheInsertions == 0
                || cacheDiagnostics.V2GroundToVCacheHits == 0)
            {
                failure = "V2 G-to-V success cache must suppress repeated evaluation of the same concrete paired state";
                return false;
            }

            if (!TryCreateUniformState(
                    new Tile2i(8, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState sharedGroundLaunch,
                    out failure))
                return false;
            Tile2i expensiveGround = new Tile2i(8, 6);
            Tile2i cheapGround = new Tile2i(8, 7);
            Tile2i relaunchGoal = new Tile2i(24, 24);
            var relaunchGroundGraph = new AccessV2GroundGraph(
                new[] { expensiveGround, cheapGround, relaunchGoal },
                new[] { relaunchGoal },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            AccessV2HandoffCandidate RelaunchHandoff(
                AccessV2BandState state,
                Tile2i entry,
                AccessHandoffOperation operation,
                float cleanupCost)
            {
                var lane0 = new AccessGroundHandoff(
                    state.GetLaneOrigin(0), operation);
                var lane1 = new AccessGroundHandoff(
                    state.GetLaneOrigin(1), operation);
                return new AccessV2HandoffCandidate(
                    state.EntryDirection, 1,
                    lane0, lane1,
                    new[] { state.GetLaneOrigin(0) },
                    new[] { state.GetLaneOrigin(1) },
                    new[] { entry }, new[] { entry },
                    Array.Empty<string>(), cleanupCost,
                    centerSpokeCost: 2f);
            }
            var cheaperRelaunchDiagnostics = new AccessSearchDiagnostics();
            var cheaperRelaunchSession = new AccessV2SearchSession(
                endpoints, Tile2i.Zero, new Tile2i(32, 32),
                (current, transition, history, connectedFixedOrigin) =>
                    current.HasValue
                        ? AccessV2TransitionEvaluation.Reject(
                            "FixtureRequiresGroundRelaunch")
                        : new AccessV2TransitionEvaluation(
                            true, string.Empty, 0f,
                            transition.Delta.Count, 0f),
                10000, float.MaxValue,
                (states, history, requiredGroundEntry) =>
                {
                    AccessV2BandState terminal = states[0];
                    if (terminal.Equals(first))
                        return new[]
                        {
                            RelaunchHandoff(
                                terminal, expensiveGround,
                                AccessHandoffOperation.Leveling, 0f),
                        };
                    if (terminal.Equals(sharedGroundLaunch))
                        return new[]
                        {
                            RelaunchHandoff(
                                terminal, relaunchGoal,
                                AccessHandoffOperation.Leveling, 0f),
                        };
                    return Array.Empty<AccessV2HandoffCandidate>();
                },
                groundGraph: relaunchGroundGraph,
                terrainCenterHeightProvider: _ => 0,
                preciseTerrainHeightProvider: _ => 0.25f,
                generatedOriginValidator: _ => true,
                diagnostics: cheaperRelaunchDiagnostics,
                evaluateDirectGroundReplacementDominance: true,
                groundToVHandoffEvaluator:
                    (state, groundEntry, operation, history) =>
                    {
                        if (!state.Equals(sharedGroundLaunch))
                            return null;
                        float cleanupCost = groundEntry == expensiveGround
                            ? 50f
                            : groundEntry == cheapGround ? 0f : 100f;
                        return RelaunchHandoff(
                            state, groundEntry, operation, cleanupCost);
                    });
            while (!cheaperRelaunchSession.IsComplete)
                cheaperRelaunchSession.Step(7);
            if (!cheaperRelaunchSession.Result.Success
                || cheaperRelaunchSession.Result.Cost >= 20f
                || !cheaperRelaunchSession.Result.GroundPath.Contains(
                    cheapGround)
                || cheaperRelaunchDiagnostics
                    .V2OrdinaryGroundReplacementPrunes == 0)
            {
                failure =
                    "V2 G-to-V dominance must allow a later cheaper ground arrival to replace an earlier expensive arrival at the same concrete V state"
                    + $": success={cheaperRelaunchSession.Result.Success}"
                    + $" reason={cheaperRelaunchSession.Result.FailureReason}"
                    + $" cost={cheaperRelaunchSession.Result.Cost}"
                    + $" replacements={cheaperRelaunchDiagnostics.V2OrdinaryGroundReplacementPrunes}"
                    + $" ground=[{string.Join(",", cheaperRelaunchSession.Result.GroundPath)}]";
                return false;
            }

            var sparsePotentialOrigins = new List<Tile2i>();
            for (int y = 0; y <= 32; y += 4)
                for (int x = 0; x <= 32; x += 4)
                    sparsePotentialOrigins.Add(new Tile2i(x, y));
            var sparseRoutePotential = new AccessV2PotentialField(
                Tile2i.Zero, new Tile2i(32, 32),
                sparsePotentialOrigins,
                Array.Empty<Tile2i>(),
                graph,
                fixedNavigation: null,
                generatedFixedCost: 0f,
                centerSpokeCost: 0f);
            var aStarDiagnostics = new AccessSearchDiagnostics();
            var aStarSession = new AccessV2SearchSession(
                endpoints, Tile2i.Zero, new Tile2i(32, 32),
                UnitEvaluator, 10000, float.MaxValue,
                (states, history, requiredGroundEntry) =>
                    states[0].Anchor == first.Anchor
                    ? new[] { forward }
                    : Array.Empty<AccessV2HandoffCandidate>(),
                heuristicEvaluator: null,
                groundGraph: graph,
                potentialField: sparseRoutePotential,
                diagnostics: aStarDiagnostics);
            while (!aStarSession.IsComplete) aStarSession.Step(7);
            if (sparseRoutePotential.GetPotential(first) <= 0f
                || !aStarSession.Result.Success
                || !aStarSession.Result.UsedAStar
                || Math.Abs(aStarSession.Result.Cost
                    - session.Result.Cost) > 0.0001f
                || !aStarSession.Result.States.SequenceEqual(
                    session.Result.States)
                || aStarDiagnostics.V2LabelFirstExpansions
                    + aStarDiagnostics.V2LabelReexpansions
                        != aStarSession.Result.Visited
                || aStarDiagnostics.V2InitialVExpansions
                    + aStarDiagnostics.V2GroundRelaunchedVExpansions
                        != aStarDiagnostics.V2BandExpansions
                || aStarDiagnostics.V2UniqueExpansionCenters <= 0
                || aStarDiagnostics.V2GroundSuffixSuccesses == 0
                || aStarDiagnostics.V2GroundSuffixSteps == 0)
            {
                failure = "V2 A* must retain the exact Dijkstra result while completing a validated potential-field G suffix"
                    + $": suffix={aStarDiagnostics.V2GroundSuffixSuccesses}/"
                    + $"{aStarDiagnostics.V2GroundSuffixSteps} "
                    + $"labels={aStarDiagnostics.V2LabelFirstExpansions}+"
                    + $"{aStarDiagnostics.V2LabelReexpansions}/"
                    + $"{aStarSession.Result.Visited} "
                    + $"vSources={aStarDiagnostics.V2InitialVExpansions}+"
                    + $"{aStarDiagnostics.V2GroundRelaunchedVExpansions}/"
                    + $"{aStarDiagnostics.V2BandExpansions}";
                return false;
            }

            Tile2i[] canonicalDirections =
            {
                new Tile2i(4, 0), new Tile2i(-4, 0),
                new Tile2i(0, 4), new Tile2i(0, -4),
            };
            foreach (int baseX in new[] { 0, -8 })
            foreach (int baseY in new[] { 0, -8 })
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                var center = new Tile2i(baseX + x, baseY + y);
                Tile2i[] actual = canonicalDirections
                    .Where(direction =>
                        AccessV2SearchSession.CanUseCanonicalGroundToVLaunchPosition(
                            center, direction))
                    .ToArray();
                var expected = new List<Tile2i>(2);
                if (x == 0)
                {
                    expected.Add(new Tile2i(4, 0));
                    expected.Add(new Tile2i(-4, 0));
                }
                if (y == 0)
                {
                    expected.Add(new Tile2i(0, 4));
                    expected.Add(new Tile2i(0, -4));
                }
                if (!actual.SequenceEqual(expected))
                {
                    failure = "G-to-V canonical-grid direction filter failed "
                        + $"at {center}: actual=[{string.Join(",", actual)}] "
                        + $"expected=[{string.Join(",", expected)}]";
                    return false;
                }
            }
            Tile2i xBandAnchor = new Tile2i(8, 12);
            Tile2i yBandAnchor = new Tile2i(12, 8);
            var eligibleOrigins = new HashSet<Tile2i>
            {
                xBandAnchor,
                xBandAnchor + new RelTile2i(0, 4),
                yBandAnchor,
                yBandAnchor + new RelTile2i(4, 0),
            };
            if (!AccessV2SearchSession.AreGroundToVBandOriginsEligible(
                    xBandAnchor, AccessV2TravelAxis.X,
                    eligibleOrigins.Contains)
                || !AccessV2SearchSession.AreGroundToVBandOriginsEligible(
                    yBandAnchor, AccessV2TravelAxis.Y,
                    eligibleOrigins.Contains)
                || AccessV2SearchSession.AreGroundToVBandOriginsEligible(
                    xBandAnchor, AccessV2TravelAxis.X,
                    origin => origin == xBandAnchor)
                || AccessV2SearchSession.AreGroundToVBandOriginsEligible(
                    yBandAnchor, AccessV2TravelAxis.Y,
                    origin => origin != yBandAnchor))
            {
                failure = "G-to-V managed-area prefilter must require both width-two lane origins";
                return false;
            }
            Tile2i mirroredGround = new Tile2i(756, 1526);
            Tile2i mirroredAnchor = new Tile2i(756, 1524);
            if (!AccessV2SearchSession.CanUseCanonicalGroundToVLaunchPosition(
                    mirroredGround, new Tile2i(4, 0))
                || AccessV2SearchSession.GetGroundToVBandAnchor(
                    mirroredGround, new Tile2i(4, 0)) != mirroredAnchor)
            {
                failure = "G-to-V companion ownership must select the unique positive-side band for residues 2/3";
                return false;
            }
            Tile2i negativeGround = new Tile2i(-6, -8);
            if (AccessV2SearchSession.GetGroundToVBandAnchor(
                    negativeGround, new Tile2i(0, -4))
                != new Tile2i(-8, -12))
            {
                failure = "G-to-V companion ownership must preserve negative-coordinate residue semantics";
                return false;
            }
            Tile2i[] residueAnchors = Enumerable.Range(0, 4)
                .Select(y => AccessV2SearchSession.GetGroundToVBandAnchor(
                    new Tile2i(756, 1524 + y), new Tile2i(4, 0)))
                .ToArray();
            if (!residueAnchors.Take(2).All(anchor =>
                    anchor == new Tile2i(756, 1520))
                || !residueAnchors.Skip(2).All(anchor =>
                    anchor == new Tile2i(756, 1524)))
            {
                failure = "V2 G-to-V companion must be Y- for residues 0/1 and Y+ for residues 2/3";
                return false;
            }

            AccessV2SearchSession.GroundToVProfileCandidate[] levelProfiles =
                AccessV2SearchSession.EnumerateDirectLevelingProfiles(
                    3, AccessV2TravelAxis.X, new Tile2i(4, 0))
                .ToArray();
            AccessV2SearchSession.GroundToVProfileCandidate[] unevenProfiles =
                AccessV2SearchSession.EnumerateGroundToVProfiles(
                    3.25f, AccessV2TravelAxis.X, new Tile2i(4, 0))
                .ToArray();
            AccessSearchMode[] levelModes =
            {
                AccessSearchMode.Flat,
                AccessSearchMode.XPositive,
                AccessSearchMode.XNegative,
            };
            AccessSearchMode[] unevenModes =
            {
                AccessSearchMode.Flat,
                AccessSearchMode.XPositive,
                AccessSearchMode.XNegative,
                AccessSearchMode.Flat,
                AccessSearchMode.XPositive,
                AccessSearchMode.XNegative,
            };
            int[] unevenLevels = { 4, 4, 4, 3, 3, 3 };
            if (levelProfiles.Length != 3
                || levelProfiles.Any(item => item.ExpectedOperation
                    != AccessHandoffOperation.Leveling)
                || unevenProfiles.Length != 6
                || unevenProfiles.Take(3).Any(item =>
                    item.ExpectedOperation != AccessHandoffOperation.Mining)
                || unevenProfiles.Skip(3).Any(item =>
                    item.ExpectedOperation != AccessHandoffOperation.Dumping))
            {
                failure = "G-to-V profile derivation must emit three leveling profiles and exactly six rough candidates";
                return false;
            }
            for (int index = 0; index < levelProfiles.Length; index++)
            {
                int center2 = index == 0 ? 6 : index == 1 ? 7 : 5;
                AccessHeightProfile.TryForMode(
                    levelModes[index], center2, out AccessHeightProfile expected);
                if (!levelProfiles[index].Profile.Equals(expected)
                    || levelProfiles[index].Profile
                        .GetHeight2NumeratorAt(0, 2) != 3 * 32)
                {
                    failure = "Level G-to-V profiles must be flat/up/down at the bridge level";
                    return false;
                }
            }
            for (int index = 0; index < unevenProfiles.Length; index++)
            {
                if (unevenProfiles[index].Profile
                        .GetHeight2NumeratorAt(0, 2)
                    != unevenLevels[index] * 32)
                {
                    failure = "Rough G-to-V profiles must start mining at ceil(G) and dumping at floor(G)";
                    return false;
                }
                AccessSearchMode mode = unevenModes[index];
                bool modeMatches = mode == AccessSearchMode.Flat
                    ? unevenProfiles[index].Profile.Nw2
                        == unevenProfiles[index].Profile.Ne2
                    : mode == AccessSearchMode.XPositive
                        ? unevenProfiles[index].Profile.Nw2
                            < unevenProfiles[index].Profile.Ne2
                        : unevenProfiles[index].Profile.Nw2
                            > unevenProfiles[index].Profile.Ne2;
                if (!modeMatches)
                {
                    failure = "Each rough operation must enumerate flat, rising, and falling profiles";
                    return false;
                }
            }

            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 6, out AccessHeightProfile bridgeProfile);
            AccessV2BandProfile.TryCreateEnabled(
                AccessV2TravelAxis.X, bridgeProfile, bridgeProfile,
                out AccessV2BandProfile bridgeBand, out _);
            var bridgeState = new AccessV2BandState(
                mirroredAnchor, bridgeBand, new Tile2i(4, 0));
            var atBurialThreshold = new AccessPropSample(
                mirroredGround, false, true, true, "debris:threshold",
                dumpBurialProbeTile: new Tile2i(758, 1526),
                dumpBurialProbeOffsetX: 0.5f,
                dumpBurialProbeOffsetY: 0.5f,
                placedHeight: 2.5f,
                dumpBurialThreshold: 0.5f);
            var beyondBurialThreshold = new AccessPropSample(
                mirroredGround, false, true, true, "debris:buried",
                dumpBurialProbeTile: new Tile2i(758, 1526),
                dumpBurialProbeOffsetX: 0.5f,
                dumpBurialProbeOffsetY: 0.5f,
                placedHeight: 2.4999f,
                dumpBurialThreshold: 0.5f);
            if (AccessSearchSnapshot.DoesV2DumpingBuryProp(
                    bridgeState, atBurialThreshold)
                || !AccessSearchSnapshot.DoesV2DumpingBuryProp(
                    bridgeState, beyondBurialThreshold))
            {
                failure = "V2 dumping must bury a prop only when its exact-position target is strictly above the scaled threshold";
                return false;
            }
            if (!AccessV2Handoffs.TryCreateDirectLevelingBridge(
                    bridgeState, mirroredGround, 7f,
                    out AccessV2HandoffCandidate directBridge)
                || !directBridge.IsQuickPath
                || directBridge.SpanLength != 1
                || directBridge.Lane0Operation
                    != AccessHandoffOperation.Leveling
                || directBridge.Lane1Operation
                    != AccessHandoffOperation.Leveling
                || !directBridge.GroundEntryCenters.SequenceEqual(
                    new[] { mirroredGround })
                || directBridge.CleanupKeys.Count != 0
                || Math.Abs(directBridge.CenterSpokeCost - 7f) > 0.0001f)
            {
                failure = "Level G-to-V bridge must synthesize a direct, cleanup-free leveling seam";
                return false;
            }

            bool TryRoughBridge(
                Func<Tile2i, float> height,
                AccessHandoffOperation operation,
                Func<Tile2i, bool>? blocker,
                out AccessV2HandoffCandidate seam,
                AccessSearchDiagnostics? diagnostics = null)
                => AccessV2Handoffs.TryCreateDeterministicGroundToVBridge(
                    bridgeState, mirroredGround, operation, 5, 7f,
                    (Tile2i tile, out float value) =>
                    {
                        value = height(tile);
                        return true;
                    },
                    tile => blocker?.Invoke(tile) == true,
                    out seam, diagnostics);

            float FlatBridge(Tile2i _) => 3f;
            float MiningBridge(Tile2i tile)
                => tile.X == 760 ? 3.25f : 3f;
            if (!AccessV2Handoffs.TryValidatePlacedGroundToVBridge(
                    bridgeState, directBridge, 5,
                    (Tile2i tile, out float value) =>
                    {
                        value = FlatBridge(tile);
                        return true;
                    },
                    _ => false,
                    out string directBridgeValidationFailure))
            {
                failure =
                    "Placed direct-leveling G-to-V validation must preserve the quick bridge representation while rechecking live continuity: "
                    + directBridgeValidationFailure;
                return false;
            }
            if (!TryRoughBridge(
                    FlatBridge,
                    AccessHandoffOperation.Leveling, null, out _)
                || !TryRoughBridge(
                    MiningBridge,
                    AccessHandoffOperation.Mining, null,
                    out AccessV2HandoffCandidate miningBridge)
                || TryRoughBridge(
                    tile => tile.X == 760 ? 3.251f : 3f,
                    AccessHandoffOperation.Mining, null, out _)
                || !TryRoughBridge(
                    tile => 3f - (760 - tile.X) * 0.5f,
                    AccessHandoffOperation.Mining, null, out _)
                || TryRoughBridge(
                    tile => tile.X == 758 ? 1.999f
                        : 3f - (760 - tile.X) * 0.5f,
                    AccessHandoffOperation.Mining, null, out _)
                || TryRoughBridge(
                    tile => tile.X == mirroredGround.X
                            && tile.Y == mirroredGround.Y + 2
                        ? 2.4f
                        : FlatBridge(tile),
                    AccessHandoffOperation.Mining, null, out _))
            {
                failure = "Deterministic G-to-V proof must accept an exact projected-target adapter and enforce inclusive 0.25 face and 0.5 step limits, including the reached G mask";
                return false;
            }
            if (AccessHandoffEvaluator.TrySelectV2CornerCrestOperation(
                    new[] { -1, -1 }, new[] { -1, -1 },
                    smoothLevelingAvailable: false, out _))
            {
                failure =
                    "The placed G-to-V regression fixture must remain incompatible with the V-to-G corner-crest rule";
                return false;
            }
            if (!AccessV2Handoffs.TryValidatePlacedGroundToVBridge(
                    bridgeState, miningBridge, 5,
                    (Tile2i tile, out float value) =>
                    {
                        value = MiningBridge(tile);
                        return true;
                    },
                    _ => false,
                    out string placedBridgeFailure)
                || AccessV2Handoffs.TryValidatePlacedGroundToVBridge(
                    bridgeState, miningBridge, 5,
                    (Tile2i tile, out float value) =>
                    {
                        value = tile.X == 760 ? 3.251f : 3f;
                        return true;
                    },
                    _ => false,
                    out _))
            {
                failure =
                    "Placed G-to-V mining validation must replay its deterministic bridge, accept the recorded live seam, reject an excessive live face, and never apply the V-to-G corner-crest rule: "
                    + placedBridgeFailure;
                return false;
            }
            bool TryResolveProjectedBridgeHeight(
                Tile2i tile,
                out float value)
                => AccessV2Handoffs.TryResolvePlacedGroundToVPostWorkHeight(
                    bridgeState,
                    AccessHandoffOperation.Mining,
                    tile,
                    naturalHeight: 4f,
                    projectedHeight: candidate =>
                        MiningBridge(candidate),
                    out value);
            if (!AccessV2Handoffs.TryValidatePlacedGroundToVBridge(
                    bridgeState, miningBridge, 5,
                    TryResolveProjectedBridgeHeight,
                    _ => false,
                    out placedBridgeFailure))
            {
                failure =
                    "Placed G-to-V validation must resolve the fixed designation's projected target outside the new adapter instead of raw terrain: "
                    + placedBridgeFailure;
                return false;
            }
            var mismatchedBridge = new AccessV2HandoffCandidate(
                new Tile2i(-miningBridge.ExitDirection.X,
                    -miningBridge.ExitDirection.Y),
                miningBridge.SpanLength,
                new AccessGroundHandoff(
                    miningBridge.Lane0Contact,
                    miningBridge.Lane0Operation),
                new AccessGroundHandoff(
                    miningBridge.Lane1Contact,
                    miningBridge.Lane1Operation),
                miningBridge.Lane0TerminalOrigins,
                miningBridge.Lane1TerminalOrigins,
                miningBridge.EscapeCenters,
                miningBridge.GroundEntryCenters,
                miningBridge.CleanupKeys,
                miningBridge.CleanupCost,
                miningBridge.IsQuickPath,
                miningBridge.CenterSpokeCost,
                miningBridge.IsStaggeredExtension,
                miningBridge.NonCrestLane);
            if (AccessV2Handoffs.TryValidatePlacedGroundToVBridge(
                    bridgeState, mismatchedBridge, 5,
                    TryResolveProjectedBridgeHeight,
                    _ => false,
                    out string mismatchDiagnostic)
                || !mismatchDiagnostic.StartsWith(
                    "GroundToVDeterministicBridgeMismatch[",
                    StringComparison.Ordinal)
                || mismatchDiagnostic.IndexOf(
                    "exitDirection recorded=", StringComparison.Ordinal) < 0
                || mismatchDiagnostic.IndexOf(
                    " replayed=", StringComparison.Ordinal) < 0)
            {
                failure =
                    "Placed G-to-V deterministic mismatch diagnostics must identify the differing field and both values: "
                    + mismatchDiagnostic;
                return false;
            }
            Tile2i propTile = new Tile2i(759, mirroredGround.Y);
            var roughDiagnostics = new AccessSearchDiagnostics();
            if (!TryRoughBridge(
                    FlatBridge, AccessHandoffOperation.Mining,
                    tile => tile == propTile, out _, roughDiagnostics)
                || TryRoughBridge(
                    FlatBridge, AccessHandoffOperation.Dumping,
                    tile => tile == propTile, out _, roughDiagnostics)
                || roughDiagnostics.V2CorridorAttempts != 0
                || roughDiagnostics.V2CorridorBfsPops != 0
                || roughDiagnostics.V2LocalEscapeTicks != 0)
            {
                failure = "Rough G-to-V proof must reject dumping props, ignore them for mining, and never invoke corridor BFS";
                return false;
            }

            foreach (AccessV2TravelAxis axis in new[]
            {
                AccessV2TravelAxis.X,
                AccessV2TravelAxis.Y,
            })
            {
                Tile2i canonical = axis == AccessV2TravelAxis.X
                    ? new Tile2i(2, 4)
                    : new Tile2i(4, 2);
                for (int offset = -2; offset <= 2; offset++)
                {
                    Tile2i sample = axis == AccessV2TravelAxis.X
                        ? new Tile2i(2, 4 + offset)
                        : new Tile2i(4 + offset, 2);
                    int spoke = Math.Abs(canonical.X - sample.X)
                        + Math.Abs(canonical.Y - sample.Y);
                    if (spoke > 2)
                    {
                        failure = "V2 canonical-center heuristic exceeds the paid center spoke";
                        return false;
                    }
                }
            }

            var heightByTile = new Dictionary<Tile2i, int>();
            var preciseByTile = new Dictionary<Tile2i, float>();
            for (int y = 0; y <= 32; y++)
                for (int x = 0; x <= 32; x++)
                {
                    var tile = new Tile2i(x, y);
                    heightByTile[tile] = 0;
                    preciseByTile[tile] = 0f;
                }
            var centerByOrigin = new Dictionary<Tile2i, int>();
            for (int y = 0; y <= 28; y += 4)
                for (int x = 0; x <= 28; x += 4)
                    centerByOrigin[new Tile2i(x, y)] = 0;
            Tile2i syntheticOrigin = first.GetLaneOrigin(1);
            var syntheticCleanup = new AccessPropCleanupInfo(
                syntheticOrigin,
                AccessPropCleanupClass.DenseDebris,
                AccessPropBlockerKind.None,
                false,
                new[]
                {
                    new AccessPropSample(
                        syntheticOrigin + new RelTile2i(1, 1),
                        false, true, true, "debris:synthetic"),
                });
            var replayGroundTiles = new List<Tile2i>(groundTiles)
            {
                cleanupTile,
            };
            AccessSearchSnapshot CreateReplaySnapshot(
                IEnumerable<Tile2i>? levelingRayOrigins = null,
                IDictionary<Tile2i, float>? preciseTerrain = null,
                IDictionary<Tile2i, AccessHeightProfile>? fixedProfiles = null,
                IEnumerable<Tile2i>? projectedFillTiles = null,
                IDictionary<Tile2i, HashSet<Tile2i>>?
                    projectedFillSources = null,
                IDictionary<Tile2i, float>? projectedFillFloors = null,
                IEnumerable<Tile2i>? projectedCutTiles = null,
                IDictionary<Tile2i, HashSet<Tile2i>>?
                    projectedCutSources = null,
                float replayDumpingSlope = 1f)
                => new AccessSearchSnapshot(
                    Tile2i.Zero, new Tile2i(32, 32),
                    new Tile2i(28, 10),
                    -2, 2, true, true, false, 1f, 1f,
                    heightByTile,
                    centerByOrigin,
                    fixedProfiles
                        ?? new Dictionary<Tile2i, AccessHeightProfile>
                        {
                            [first.GetLaneOrigin(0)] = first.GetLane(0).Profile,
                        },
                    Array.Empty<Tile2i>(),
                    replayGroundTiles,
                    new[] { new Tile2i(28, 10) },
                    Array.Empty<Tile2i>(),
                    Array.Empty<Tile2i>(),
                    Array.Empty<AccessDurabilityCorner>(),
                    propCleanupByOrigin:
                        new Dictionary<Tile2i, AccessPropCleanupInfo>
                        {
                            [syntheticOrigin] = syntheticCleanup,
                        },
                    preciseTerrainHeights: preciseTerrain ?? preciseByTile,
                    physicalTerrainMin: Tile2i.Zero,
                    physicalTerrainMax: new Tile2i(32, 32),
                    rayLevelingDesignationOrigins: levelingRayOrigins,
                    projectedCutDisturbedTiles: projectedCutTiles,
                    projectedFillDisturbedTiles: projectedFillTiles,
                    projectedFillSurfaceFloors: projectedFillFloors,
                    projectedCutSourcesByTile: projectedCutSources,
                    projectedFillSourcesByTile: projectedFillSources,
                    vehicleWidth: 5,
                    dumpingMaterialSlope: replayDumpingSlope);
            AccessSearchWorkspace CreateReplayWorkspace(
                AccessSearchSnapshot snapshot)
                => new AccessSearchWorkspace(
                    snapshot,
                    new CooperativeAccessSearchEvaluator(
                        v2WorkableHandoffs: UniformSingle,
                        v2WorkableHandoffSpans: Span));
            AccessSearchSnapshot replaySnapshot = CreateReplaySnapshot();
            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 2,
                out AccessHeightProfile singleRaisedSourceProfile);
            Tile2i singleRaisedSourceOrigin = new Tile2i(12, 12);
            var singleRaisedProfiles =
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [singleRaisedSourceOrigin] = singleRaisedSourceProfile,
                };
            AccessV2EndpointSet singleRaisedEndpoints =
                AccessV2FrontageDiscovery.Build(
                    Tile2i.Zero, new Tile2i(32, 32),
                    singleRaisedProfiles,
                    new[] { singleRaisedSourceOrigin });
            var singleRaisedFillFloors =
                new Dictionary<Tile2i, float>();
            var singleRaisedFillSources =
                new Dictionary<Tile2i, HashSet<Tile2i>>();
            for (int y = 0; y <= 32; y++)
            {
                for (int x = 0; x <= 32; x++)
                {
                    int outsideX = x < singleRaisedSourceOrigin.X
                        ? singleRaisedSourceOrigin.X - x
                        : x > singleRaisedSourceOrigin.X + 4
                            ? x - (singleRaisedSourceOrigin.X + 4)
                            : 0;
                    int outsideY = y < singleRaisedSourceOrigin.Y
                        ? singleRaisedSourceOrigin.Y - y
                        : y > singleRaisedSourceOrigin.Y + 4
                            ? y - (singleRaisedSourceOrigin.Y + 4)
                            : 0;
                    int distance = Math.Max(outsideX, outsideY);
                    if (distance < 1 || distance > 2)
                        continue;
                    float projectedHeight = 1f - distance * 0.34f;
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            Tile2i blocked = new Tile2i(x + dx, y + dy);
                            if (blocked.X < 0 || blocked.X > 32
                                || blocked.Y < 0 || blocked.Y > 32)
                                continue;
                            if (!singleRaisedFillFloors.TryGetValue(
                                    blocked, out float existingFloor)
                                || projectedHeight > existingFloor)
                                singleRaisedFillFloors[blocked] =
                                    projectedHeight;
                            if (!singleRaisedFillSources.TryGetValue(
                                    blocked, out HashSet<Tile2i> sources))
                            {
                                sources = new HashSet<Tile2i>();
                                singleRaisedFillSources[blocked] = sources;
                            }
                            sources.Add(singleRaisedSourceOrigin);
                        }
                    }
                }
            }
            AccessV2StartFrontage singleRaisedLaunch =
                singleRaisedEndpoints.Starts.First(candidate =>
                    candidate.InitialTransition != null
                    && candidate.LaunchSuccessor != null
                    && candidate.State.EntryDirection == new Tile2i(4, 0)
                    && candidate.LaunchSuccessor.Next.Band.Lane0.Center2 == 1
                    && candidate.LaunchSuccessor.Next.Band.Lane1.Center2 == 1);
            AccessSearchSnapshot singleRaisedSnapshot =
                CreateReplaySnapshot(
                    levelingRayOrigins: Array.Empty<Tile2i>(),
                    fixedProfiles: singleRaisedProfiles,
                    projectedFillTiles: singleRaisedFillFloors.Keys,
                    projectedFillSources: singleRaisedFillSources,
                    projectedFillFloors: singleRaisedFillFloors,
                    replayDumpingSlope: 0.34f);
            AccessV2TransitionEvaluation singleRaisedInitialEvaluation =
                AccessPathSearch.EvaluateV2Transition(
                    singleRaisedSnapshot, null,
                    singleRaisedLaunch.InitialTransition!,
                    AccessV2History.Empty,
                    singleRaisedSourceOrigin);
            if (!singleRaisedInitialEvaluation.IsValid)
            {
                failure = "V2 raised single-origin fixture initial launch failed: "
                    + singleRaisedInitialEvaluation.RejectionReason;
                return false;
            }
            AccessV2History singleRaisedInitialHistory =
                AccessV2History.Empty.ApplyValidated(
                    singleRaisedLaunch.InitialTransition!,
                    singleRaisedInitialEvaluation.RayConstraints,
                    singleRaisedInitialEvaluation.CleanupKeys);
            AccessV2TransitionEvaluation singleRaisedSuccessorEvaluation =
                AccessPathSearch.EvaluateV2Transition(
                    singleRaisedSnapshot, singleRaisedLaunch.State,
                    singleRaisedLaunch.LaunchSuccessor!,
                    singleRaisedInitialHistory,
                    singleRaisedSourceOrigin);
            if (!singleRaisedSuccessorEvaluation.IsValid)
            {
                failure = "V2 raised single-origin fixture launch successor failed: "
                    + singleRaisedSuccessorEvaluation.RejectionReason;
                return false;
            }
            AccessV2History singleRaisedSuccessorHistory =
                singleRaisedInitialHistory.ApplyValidated(
                    singleRaisedLaunch.LaunchSuccessor!,
                    singleRaisedSuccessorEvaluation.RayConstraints,
                    singleRaisedSuccessorEvaluation.CleanupKeys);
            AccessV2Transition singleRaisedSuccessor =
                singleRaisedLaunch.LaunchSuccessor!;
            bool hasNonRisingContinuation = false;
            string singleRaisedContinuationReasons = string.Empty;
            foreach (AccessV2Transition continuation
                in AccessV2Geometry.EnumerateStraight(
                    singleRaisedSuccessor.Next))
            {
                if (continuation.Next.Band.Lane0.Center2 > 1
                    || continuation.Next.Band.Lane1.Center2 > 1)
                    continue;
                AccessV2TransitionEvaluation continuationEvaluation =
                    AccessPathSearch.EvaluateV2Transition(
                        singleRaisedSnapshot,
                        singleRaisedSuccessor.Next,
                        continuation,
                        singleRaisedSuccessorHistory, null);
                if (continuationEvaluation.IsValid)
                {
                    hasNonRisingContinuation = true;
                    break;
                }
                singleRaisedContinuationReasons +=
                    (singleRaisedContinuationReasons.Length == 0 ? "" : ",")
                    + continuationEvaluation.RejectionReason;
            }
            if (!hasNonRisingContinuation)
            {
                failure = "V2 raised single-origin downhill launch must retain a flat or descending continuation: "
                    + singleRaisedContinuationReasons;
                return false;
            }
            Tile2i sameSortOrigin = new Tile2i(16, 16);
            Tile2i sameSortTile = new Tile2i(18, 18);
            var sameSortProfile = new AccessHeightProfile(4, 4, 4, 4);
            var deeperCutProfile = new AccessHeightProfile(2, 2, 2, 2);
            var higherFillProfile = new AccessHeightProfile(6, 6, 6, 6);
            if (!AccessV2History.Empty.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(
                            new Tile2i(4, 4),
                            new AccessHeightProfile(0, 0, 0, 0)),
                    },
                    Array.Empty<Tile2i>(),
                    new[]
                    {
                        new AccessRayHeightConstraint(
                            sameSortTile,
                            AccessSideRayOperation.Cut,
                            1.5f,
                            new Tile2i(4, 4)),
                    },
                    Array.Empty<string>(),
                    out AccessV2History sameSortCutHistory,
                    out string sameSortHistoryReason))
            {
                failure = "V2 same-sort ray fixture history failed: "
                    + sameSortHistoryReason;
                return false;
            }
            var sameSortCutTerrain = new Dictionary<Tile2i, float>(preciseByTile)
            {
                [sameSortTile] = 3f,
            };
            var opposingFillTerrain = new Dictionary<Tile2i, float>(preciseByTile)
            {
                [sameSortTile] = 1f,
            };
            if (sameSortCutHistory.IsProfileBlockedByRayEnvelope(
                    CreateReplaySnapshot(preciseTerrain: sameSortCutTerrain),
                    sameSortOrigin, deeperCutProfile, null, out _)
                || !sameSortCutHistory.HasRayAt(
                    sameSortTile, AccessSideRayOperation.Cut)
                || sameSortCutHistory.HasRayAt(
                    sameSortTile, AccessSideRayOperation.Fill)
                || !sameSortCutHistory.IsProfileBlockedByRayEnvelope(
                    CreateReplaySnapshot(preciseTerrain: opposingFillTerrain),
                    sameSortOrigin, sameSortProfile, null,
                    out AccessSideRayOperation opposingRayBlock)
                || opposingRayBlock != AccessSideRayOperation.Cut)
            {
                failure =
                    "V2 cut rays must merge with new cut work while still blocking opposing fill work";
                return false;
            }
            if (!AccessV2History.Empty.TryApply(
                    new[]
                    {
                        new AccessV2OriginProfile(
                            new Tile2i(4, 4),
                            new AccessHeightProfile(0, 0, 0, 0)),
                    },
                    Array.Empty<Tile2i>(),
                    new[]
                    {
                        new AccessRayHeightConstraint(
                            sameSortTile,
                            AccessSideRayOperation.Fill,
                            2.5f,
                            new Tile2i(4, 4)),
                    },
                    Array.Empty<string>(),
                    out AccessV2History sameSortFillHistory,
                    out sameSortHistoryReason))
            {
                failure = "V2 same-sort fill-ray fixture history failed: "
                    + sameSortHistoryReason;
                return false;
            }
            if (sameSortFillHistory.IsProfileBlockedByRayEnvelope(
                    CreateReplaySnapshot(preciseTerrain: opposingFillTerrain),
                    sameSortOrigin, higherFillProfile, null, out _)
                || !sameSortFillHistory.IsProfileBlockedByRayEnvelope(
                    CreateReplaySnapshot(preciseTerrain: sameSortCutTerrain),
                    sameSortOrigin, sameSortProfile, null,
                    out opposingRayBlock)
                || opposingRayBlock != AccessSideRayOperation.Fill)
            {
                failure =
                    "V2 fill rays must merge with new fill work while still blocking opposing cut work";
                return false;
            }
            Tile2i lane0Source = first.GetLaneOrigin(0);
            Tile2i lane1Source = first.GetLaneOrigin(1);
            Tile2i firstLaneRayTile = new Tile2i(12, 3);
            var sourcePairProfiles = new Dictionary<Tile2i, AccessHeightProfile>
            {
                [lane0Source] = first.GetLane(0).Profile,
                [lane1Source] = first.GetLane(1).Profile,
            };
            var sourcePairTerrain = new Dictionary<Tile2i, float>(preciseByTile)
            {
                [new Tile2i(12, 4)] = 1f,
                [firstLaneRayTile] = 1f,
            };
            AccessV2TransitionEvaluation fixedPairFirstStraight =
                AccessPathSearch.EvaluateV2Transition(
                    CreateReplaySnapshot(
                        levelingRayOrigins: Array.Empty<Tile2i>(),
                        preciseTerrain: sourcePairTerrain,
                        fixedProfiles: sourcePairProfiles,
                        projectedFillTiles: new[] { firstLaneRayTile },
                        projectedFillSources:
                            new Dictionary<Tile2i, HashSet<Tile2i>>
                            {
                                [firstLaneRayTile] =
                                    new HashSet<Tile2i> { lane0Source },
                            }),
                    first, secondStep, AccessV2History.Empty, null);
            if (!fixedPairFirstStraight.IsValid)
            {
                failure = "V2 fixed-pair frontage must exempt its own source lanes on the first straight transition: "
                    + fixedPairFirstStraight.RejectionReason;
                return false;
            }
            Tile2i connectedPairSafetyTile =
                secondStep.Next.GetLaneOrigin(0)
                    + new RelTile2i(2, 2);
            AccessV2TransitionEvaluation connectedPairSafety =
                AccessPathSearch.EvaluateV2Transition(
                    CreateReplaySnapshot(
                        levelingRayOrigins: Array.Empty<Tile2i>(),
                        preciseTerrain: sourcePairTerrain,
                        fixedProfiles: sourcePairProfiles,
                        projectedFillTiles:
                            new[] { connectedPairSafetyTile },
                        projectedFillSources:
                            new Dictionary<Tile2i, HashSet<Tile2i>>
                            {
                                [connectedPairSafetyTile] =
                                    new HashSet<Tile2i> { lane1Source },
                            }),
                    first, secondStep,
                    AccessV2History.Empty,
                    lane0Source);
            if (!connectedPairSafety.IsValid)
            {
                failure = "V2 generated source exits must waive immutable safety from the complete connected predecessor band: "
                    + connectedPairSafety.RejectionReason;
                return false;
            }
            AccessV2History retainedPredecessorHistory =
                AccessV2History.Empty.ApplyValidated(
                    secondStep,
                    connectedPairSafety.RayConstraints,
                    connectedPairSafety.CleanupKeys,
                    new[] { lane0Source, lane1Source });
            Tile2i retainedPairSafetyTile =
                thirdStep.Next.GetLaneOrigin(0)
                    + new RelTile2i(2, 2);
            AccessV2TransitionEvaluation retainedPairSafety =
                AccessPathSearch.EvaluateV2Transition(
                    CreateReplaySnapshot(
                        levelingRayOrigins: Array.Empty<Tile2i>(),
                        preciseTerrain: sourcePairTerrain,
                        fixedProfiles: sourcePairProfiles,
                        projectedFillTiles:
                            new[] { retainedPairSafetyTile },
                        projectedFillSources:
                            new Dictionary<Tile2i, HashSet<Tile2i>>
                            {
                                [retainedPairSafetyTile] =
                                    new HashSet<Tile2i> { lane1Source },
                            }),
                    secondStep.Next, thirdStep,
                    retainedPredecessorHistory, null);
            if (!retainedPairSafety.IsValid)
            {
                failure = "V2 generated fringe continuation must retain immutable safety ownership from its fixed predecessor band: "
                    + retainedPairSafety.RejectionReason;
                return false;
            }
            AccessSearchSnapshot retainedPairSnapshot =
                CreateReplaySnapshot(
                    levelingRayOrigins: Array.Empty<Tile2i>(),
                    preciseTerrain: sourcePairTerrain,
                    fixedProfiles: sourcePairProfiles,
                    projectedFillTiles:
                        new[] { retainedPairSafetyTile },
                    projectedFillSources:
                        new Dictionary<Tile2i, HashSet<Tile2i>>
                        {
                            [retainedPairSafetyTile] =
                                new HashSet<Tile2i> { lane1Source },
                        });
            var retainedGeneratedProfiles = secondStep.Delta
                .Concat(thirdStep.Delta)
                .ToDictionary(item => item.Origin, item => item.Profile);
            var retainedPairRoute = new AccessV2RouteData(
                new[] { first, secondStep.Next, thirdStep.Next },
                retainedGeneratedProfiles,
                null,
                Array.Empty<Tile2i>(),
                new[]
                {
                    new AccessV2RouteStep(first, null, null, null),
                    new AccessV2RouteStep(
                        secondStep.Next, secondStep, null, null),
                    new AccessV2RouteStep(
                        thirdStep.Next, thirdStep, null, null),
                });
            var retainedPairResult = new AccessSearchResult(
                true, string.Empty, lane0Source,
                Array.Empty<AccessSearchNode>(),
                10f, 1,
                new Dictionary<string, int>(),
                10f, 0f, 0f, 0f, 0f,
                reachedGoalKind: AccessReachedGoalKind.FixedNetwork,
                diagnostics: new AccessSearchDiagnostics(),
                v2Route: retainedPairRoute);
            AccessDesignationPlan retainedPairPlan =
                AccessPathMaterializer.Materialize(
                    CreateReplayWorkspace(retainedPairSnapshot),
                    retainedPairResult);
            if (!retainedPairPlan.IsValid)
            {
                failure = "V2 materialization replay must retain fixed predecessor safety ownership exactly as search does: "
                    + retainedPairPlan.FailureReason;
                return false;
            }
            Tile2i connectedFixedNeighbor = AccessV2Geometry.Add(
                lane1Source,
                AccessV2BandProfile.GetLaneDirection(first.Axis));
            var connectedStructureProfiles =
                new Dictionary<Tile2i, AccessHeightProfile>(sourcePairProfiles)
                {
                    [connectedFixedNeighbor] = first.GetLane(1).Profile,
                };
            AccessV2TransitionEvaluation connectedStructureSafety =
                AccessPathSearch.EvaluateV2Transition(
                    CreateReplaySnapshot(
                        levelingRayOrigins: Array.Empty<Tile2i>(),
                        preciseTerrain: sourcePairTerrain,
                        fixedProfiles: connectedStructureProfiles,
                        projectedFillTiles:
                            new[] { retainedPairSafetyTile },
                        projectedFillSources:
                            new Dictionary<Tile2i, HashSet<Tile2i>>
                            {
                                [retainedPairSafetyTile] =
                                    new HashSet<Tile2i>
                                    {
                                        connectedFixedNeighbor,
                                    },
                            }),
                    secondStep.Next, thirdStep,
                    retainedPredecessorHistory, null);
            if (!connectedStructureSafety.IsValid)
            {
                failure = "V2 generated fringe continuation must waive safety from the connected fixed predecessor structure, not only its first two origins: "
                    + connectedStructureSafety.RejectionReason;
                return false;
            }
            Tile2i disconnectedFixedOrigin = new Tile2i(28, 28);
            var disconnectedStructureProfiles =
                new Dictionary<Tile2i, AccessHeightProfile>(sourcePairProfiles)
                {
                    [disconnectedFixedOrigin] = first.GetLane(1).Profile,
                };
            AccessV2TransitionEvaluation disconnectedStructureSafety =
                AccessPathSearch.EvaluateV2Transition(
                    CreateReplaySnapshot(
                        levelingRayOrigins: Array.Empty<Tile2i>(),
                        preciseTerrain: sourcePairTerrain,
                        fixedProfiles: disconnectedStructureProfiles,
                        projectedFillTiles:
                            new[] { retainedPairSafetyTile },
                        projectedFillSources:
                            new Dictionary<Tile2i, HashSet<Tile2i>>
                            {
                                [retainedPairSafetyTile] =
                                    new HashSet<Tile2i>
                                    {
                                        disconnectedFixedOrigin,
                                    },
                            }),
                    secondStep.Next, thirdStep,
                    retainedPredecessorHistory, null);
            if (disconnectedStructureSafety.IsValid
                || disconnectedStructureSafety.RejectionReason
                    != "ProjectedTerrainSafety")
            {
                failure = "V2 predecessor safety ownership must not waive projections from disconnected fixed structures";
                return false;
            }
            var undercutTerrain =
                new Dictionary<Tile2i, float>(sourcePairTerrain)
                {
                    [retainedPairSafetyTile] = 4f,
                };
            AccessV2TransitionEvaluation undercutCutSafety =
                AccessPathSearch.EvaluateV2Transition(
                    CreateReplaySnapshot(
                        levelingRayOrigins: Array.Empty<Tile2i>(),
                        preciseTerrain: undercutTerrain,
                        fixedProfiles: sourcePairProfiles,
                        projectedCutTiles:
                            new[] { retainedPairSafetyTile },
                        projectedCutSources:
                            new Dictionary<Tile2i, HashSet<Tile2i>>
                            {
                                [retainedPairSafetyTile] =
                                    new HashSet<Tile2i>
                                    {
                                        disconnectedFixedOrigin,
                                    },
                            }),
                    secondStep.Next, thirdStep,
                    retainedPredecessorHistory, null);
            if (!undercutCutSafety.IsValid)
            {
                failure = "V2 profiles performing a deeper cut must be able to enter a cut ray's safety tail: "
                    + undercutCutSafety.RejectionReason;
                return false;
            }
            AccessV2TransitionEvaluation ownedExactInternalBand =
                AccessPathSearch.EvaluateV2Transition(
                    replaySnapshot, first, secondStep,
                    AccessV2History.Empty, null);
            if (!ownedExactInternalBand.IsValid
                || ownedExactInternalBand.RequiresGroundTransition
                || Math.Abs(
                    ownedExactInternalBand.DirectWorkCost) > 0.0001f
                || ownedExactInternalBand.GeneratedFixedCost <= 0f)
            {
                failure =
                    "V2 internal exact-terrain bands must remain explicit accessway-owned designations with zero direct work";
                return false;
            }
            Tile2i exactGroundGoal = new Tile2i(24, 24);
            var exactGroundGraph = new AccessV2GroundGraph(
                new[] { exactGroundGoal },
                new[] { exactGroundGoal },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            var exactGroundEndpoints = new AccessV2EndpointSet(
                new[]
                {
                    new AccessV2StartFrontage(
                        first, first.GetLaneOrigin(0)),
                },
                new AccessV2FrontageDiagnostics());
            var exactGroundSession = new AccessV2SearchSession(
                exactGroundEndpoints,
                Tile2i.Zero, new Tile2i(32, 32),
                (current, transition, history, connected) =>
                    transition.Next.Equals(secondStep.Next)
                        ? AccessPathSearch.EvaluateV2Transition(
                            replaySnapshot, current, transition,
                            history, connected)
                        : AccessV2TransitionEvaluation.Reject(
                            "FixtureOnlyExactSuccessor"),
                1000, float.MaxValue,
                (recent, _, requiredGroundEntry) =>
                    recent[0].Equals(secondStep.Next)
                    ? new[]
                    {
                        new AccessV2HandoffCandidate(
                            secondStep.Next.EntryDirection, 1,
                            new AccessGroundHandoff(
                                new Tile2i(20, 20),
                                AccessHandoffOperation.None),
                            new AccessGroundHandoff(
                                new Tile2i(20, 21),
                                AccessHandoffOperation.None),
                            new[] { secondStep.Next.GetLaneOrigin(0) },
                            new[] { secondStep.Next.GetLaneOrigin(1) },
                            new[] { exactGroundGoal },
                            new[] { exactGroundGoal },
                            Array.Empty<string>(), 0f,
                            isQuickPath: true),
                    }
                    : Array.Empty<AccessV2HandoffCandidate>(),
                groundGraph: exactGroundGraph);
            while (!exactGroundSession.IsComplete)
                exactGroundSession.Step(10);
            if (!exactGroundSession.Result.Success
                || exactGroundSession.Result.RouteSteps.Count != 3
                || exactGroundSession.Result.RouteSteps[1].IsGround
                || !exactGroundSession.Result.RouteSteps[2].IsGround
                || exactGroundSession.Result.RouteSteps[2].GroundCenter
                    != exactGroundGoal)
            {
                failure =
                    "V2 owned exact successor must remain eligible for an immediate G handoff";
                return false;
            }
            if (!AccessV2Geometry.TryStrafe(
                    first, 1, out AccessV2Transition exactStrafe,
                    out failure))
                return false;
            AccessV2TransitionEvaluation ownedExactStrafe =
                AccessPathSearch.EvaluateV2Transition(
                    replaySnapshot, first, exactStrafe,
                    AccessV2History.Empty, null);
            if (!ownedExactStrafe.IsValid
                || Math.Abs(ownedExactStrafe.DirectWorkCost) > 0.0001f
                || ownedExactStrafe.GeneratedFixedCost <= 0f)
            {
                failure =
                    "V2 strafe must retain its complete exact-terrain swept delta as owned designations";
                return false;
            }
            if (!TryCreateUniformState(
                    new Tile2i(12, 12), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState rearClearanceState,
                    out failure)
                || !AccessV2Geometry.TryStrafe(
                    rearClearanceState, 1,
                    out AccessV2Transition rearClearanceStrafe,
                    out failure))
                return false;
            var rearClearanceTerrain =
                new Dictionary<Tile2i, float>(preciseByTile)
                {
                    // The newly introduced predecessor-outer origin is
                    // (8,20). Its rear face is x=8 and must launch toward -X.
                    [new Tile2i(8, 20)] = 4f,
                    [new Tile2i(8, 24)] = 4f,
                };
            AccessV2TransitionEvaluation rearBlockedStrafe =
                AccessPathSearch.EvaluateV2Transition(
                    CreateReplaySnapshot(
                        levelingRayOrigins:
                            new[] { new Tile2i(4, 20) },
                        preciseTerrain: rearClearanceTerrain),
                    rearClearanceState, rearClearanceStrafe,
                    AccessV2History.Empty, null);
            if (rearBlockedStrafe.IsValid
                || rearBlockedStrafe.RejectionReason
                    != "SideRayDesignation")
            {
                failure = "V2 strafe must validate the exposed rear face of its newly introduced predecessor-outer origin: "
                    + rearBlockedStrafe.RejectionReason;
                return false;
            }
            if (!AccessV2Geometry.TryStrafe(
                    rearClearanceStrafe.Next, 1,
                    out AccessV2Transition adjacentStrafe,
                    out failure))
                return false;
            var adjacentStrafeTerrain =
                new Dictionary<Tile2i, float>(preciseByTile);
            for (int y = 8; y <= 32; y++)
                for (int x = 4; x <= 24; x++)
                    adjacentStrafeTerrain[new Tile2i(x, y)] = 20f;
            AccessSearchSnapshot adjacentStrafeSnapshot =
                CreateReplaySnapshot(
                    levelingRayOrigins: Array.Empty<Tile2i>(),
                    preciseTerrain: adjacentStrafeTerrain,
                    fixedProfiles:
                        new Dictionary<Tile2i, AccessHeightProfile>());
            AccessV2TransitionEvaluation firstAdjacentStrafe =
                AccessPathSearch.EvaluateV2Transition(
                    adjacentStrafeSnapshot,
                    rearClearanceState, rearClearanceStrafe,
                    AccessV2History.Empty, null);
            if (!firstAdjacentStrafe.IsValid)
            {
                failure = "V2 adjacent-strafe cost fixture first move failed: "
                    + firstAdjacentStrafe.RejectionReason;
                return false;
            }
            var adjacentProjectedWork =
                new Dictionary<Tile2i, AccessProjectedTerrainEffect>();
            for (int index = 0;
                index < firstAdjacentStrafe.RayConstraints.Count;
                index++)
            {
                AccessRayHeightConstraint constraint =
                    firstAdjacentStrafe.RayConstraints[index];
                if (constraint.IsSafetyOnly)
                    continue;
                adjacentProjectedWork.TryGetValue(
                    constraint.Tile,
                    out AccessProjectedTerrainEffect effect);
                if (constraint.Operation == AccessSideRayOperation.Cut
                    && (!effect.HasCutWork
                        || constraint.Height < effect.CutCeiling))
                {
                    effect.HasCutWork = true;
                    effect.CutCeiling = constraint.Height;
                }
                else if (constraint.Operation
                        == AccessSideRayOperation.Fill
                    && (!effect.HasFillWork
                        || constraint.Height > effect.FillFloor))
                {
                    effect.HasFillWork = true;
                    effect.FillFloor = constraint.Height;
                }
                adjacentProjectedWork[constraint.Tile] = effect;
            }
            float adjacentProjectedVolume = 0f;
            foreach (KeyValuePair<Tile2i, AccessProjectedTerrainEffect> pair
                in adjacentProjectedWork)
            {
                if (!adjacentStrafeSnapshot.TryGetPreciseTerrainHeight(
                        pair.Key, out float projectedTerrainHeight))
                    continue;
                if (pair.Value.HasCutWork)
                    adjacentProjectedVolume += Math.Max(
                        0f, projectedTerrainHeight - pair.Value.CutCeiling);
                if (pair.Value.HasFillWork)
                    adjacentProjectedVolume += Math.Max(
                        0f, pair.Value.FillFloor - projectedTerrainHeight);
            }
            if (adjacentProjectedVolume
                > firstAdjacentStrafe.ExteriorRayCost + 0.0001f)
            {
                failure = "V2 ray charge must cover the unique projected work volume made creditable to later profiles: ray="
                    + firstAdjacentStrafe.ExteriorRayCost
                    + ", projected=" + adjacentProjectedVolume;
                return false;
            }
            AccessV2History adjacentHistoryWithoutRays =
                AccessV2History.Empty.ApplyValidated(
                    rearClearanceStrafe,
                    Array.Empty<AccessRayHeightConstraint>(),
                    firstAdjacentStrafe.CleanupKeys);
            AccessV2History adjacentHistoryWithRays =
                AccessV2History.Empty.ApplyValidated(
                    rearClearanceStrafe,
                    firstAdjacentStrafe.RayConstraints,
                    firstAdjacentStrafe.CleanupKeys);
            AccessV2TransitionEvaluation secondAdjacentWithoutCredit =
                AccessPathSearch.EvaluateV2Transition(
                    adjacentStrafeSnapshot,
                    rearClearanceStrafe.Next, adjacentStrafe,
                    adjacentHistoryWithoutRays, null);
            AccessV2TransitionEvaluation secondAdjacentWithCredit =
                AccessPathSearch.EvaluateV2Transition(
                    adjacentStrafeSnapshot,
                    rearClearanceStrafe.Next, adjacentStrafe,
                    adjacentHistoryWithRays, null);
            if (!secondAdjacentWithoutCredit.IsValid
                || !secondAdjacentWithCredit.IsValid)
            {
                failure = "V2 adjacent-strafe cost fixture second move failed: "
                    + secondAdjacentWithoutCredit.RejectionReason + "/"
                    + secondAdjacentWithCredit.RejectionReason;
                return false;
            }
            if (!AccessV2Geometry.TryStrafe(
                    adjacentStrafe.Next, 1,
                    out AccessV2Transition thirdAdjacentStrafe,
                    out failure))
                return false;
            AccessV2History secondAdjacentHistoryWithoutRays =
                adjacentHistoryWithoutRays.ApplyValidated(
                    adjacentStrafe,
                    Array.Empty<AccessRayHeightConstraint>(),
                    secondAdjacentWithoutCredit.CleanupKeys);
            AccessV2History secondAdjacentHistoryWithFirstRays =
                adjacentHistoryWithRays.ApplyValidated(
                    adjacentStrafe,
                    Array.Empty<AccessRayHeightConstraint>(),
                    secondAdjacentWithCredit.CleanupKeys);
            AccessV2TransitionEvaluation thirdAdjacentWithoutCredit =
                AccessPathSearch.EvaluateV2Transition(
                    adjacentStrafeSnapshot,
                    adjacentStrafe.Next, thirdAdjacentStrafe,
                    secondAdjacentHistoryWithoutRays, null);
            AccessV2TransitionEvaluation thirdAdjacentWithCredit =
                AccessPathSearch.EvaluateV2Transition(
                    adjacentStrafeSnapshot,
                    adjacentStrafe.Next, thirdAdjacentStrafe,
                    secondAdjacentHistoryWithFirstRays, null);
            if (!thirdAdjacentWithoutCredit.IsValid
                || !thirdAdjacentWithCredit.IsValid)
            {
                failure = "V2 adjacent-strafe cost fixture third move failed: "
                    + thirdAdjacentWithoutCredit.RejectionReason + "/"
                    + thirdAdjacentWithCredit.RejectionReason;
                return false;
            }
            float adjacentCredit =
                secondAdjacentWithoutCredit.DirectWorkCost
                - secondAdjacentWithCredit.DirectWorkCost
                + thirdAdjacentWithoutCredit.DirectWorkCost
                - thirdAdjacentWithCredit.DirectWorkCost;
            if (adjacentCredit
                > firstAdjacentStrafe.ExteriorRayCost + 0.0001f)
            {
                failure = "V2 projected ray work must not credit a following adjacent strafe more than the ray work was charged: ray="
                    + firstAdjacentStrafe.ExteriorRayCost
                    + ", credit=" + adjacentCredit;
                return false;
            }
            var syntheticTransition = new AccessV2Transition(
                AccessV2TransitionKind.Strafe,
                first,
                new[] { first.GetLane(1) },
                new[] { first.GetLaneOrigin(0) });
            AccessV2TransitionEvaluation exactStartCompanion =
                AccessPathSearch.EvaluateV2Transition(
                    replaySnapshot, null, syntheticTransition,
                    AccessV2History.Empty, first.GetLaneOrigin(0));
            if (!exactStartCompanion.IsValid)
            {
                failure = "V2 exact-terrain synthetic start companion must remain admissible: "
                    + exactStartCompanion.RejectionReason;
                return false;
            }
            Tile2i fixedLaneOuterNeighbor = AccessV2Geometry.Add(
                first.GetLaneOrigin(0), new Tile2i(0, -4));
            var nonzeroOuterRayTerrain =
                new Dictionary<Tile2i, float>(preciseByTile)
                {
                    // X-positive travel exposes lane 0's north ray from
                    // (8,4). Lowering the corner and its first outward sample
                    // makes the designation intersect an active fill ray,
                    // independent of any configured termination-buffer size.
                    [new Tile2i(8, 4)] = -1f,
                    [new Tile2i(8, 3)] = -2f,
                };
            AccessV2TransitionEvaluation blockedSourceMouth =
                AccessPathSearch.EvaluateV2Transition(
                    CreateReplaySnapshot(
                        new[] { fixedLaneOuterNeighbor },
                        nonzeroOuterRayTerrain),
                    null, syntheticTransition,
                    AccessV2History.Empty, first.GetLaneOrigin(0));
            if (blockedSourceMouth.IsValid
                || blockedSourceMouth.RejectionReason
                    != "SideRayDesignation")
            {
                failure = "V2 synthetic source mouth must validate the reused cluster lane's outer Mega clearance: "
                    + blockedSourceMouth.RejectionReason;
                return false;
            }
            var materializationRoute = new AccessV2RouteData(
                new[] { first },
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [syntheticOrigin] = first.GetLane(1).Profile,
                },
                session.Result.Handoff,
                session.Result.GroundPath);
            var materializationResult = new AccessSearchResult(
                true, string.Empty, first.GetLaneOrigin(0),
                Array.Empty<AccessSearchNode>(),
                10f, 1,
                new Dictionary<string, int>(),
                2f, 0f, 1f, 0f, 8f,
                AccessReachedGoalKind.TowerGround,
                diagnostics: new AccessSearchDiagnostics(),
                v2Route: materializationRoute);
            AccessDesignationPlan materialized =
                AccessPathMaterializer.Materialize(
                    CreateReplayWorkspace(replaySnapshot),
                    materializationResult);
            if (!materialized.IsValid
                || materialized.Designations.Count != 1
                || materialized.Designations[0].Origin != syntheticOrigin
                || materialized.CleanupOrigins.Count != 1
                || materialized.HandoffOperationsByOrigin.Count != 1)
            {
                failure = "V2 replay must retain exact-terrain owned work together with cleanup and terminal ownership metadata: "
                    + materialized.FailureReason;
                return false;
            }

            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 0,
                out AccessHeightProfile providerFlat);
            var providerProfiles = new Dictionary<Tile2i, AccessHeightProfile>
            {
                [new Tile2i(4, 4)] = providerFlat,
                [new Tile2i(4, 8)] = providerFlat,
            };
            var providerGround = new[]
            {
                new Tile2i(9, 8), new Tile2i(10, 8),
                new Tile2i(11, 8), new Tile2i(12, 8),
            };
            var providerSnapshot = new AccessSearchSnapshot(
                Tile2i.Zero, new Tile2i(32, 32), new Tile2i(12, 8),
                -2, 2, true, true, true, 1f, 1f,
                heightByTile, centerByOrigin, providerProfiles,
                Array.Empty<Tile2i>(), providerGround,
                new[] { new Tile2i(12, 8) },
                Array.Empty<Tile2i>(), Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>(),
                preciseTerrainHeights: preciseByTile,
                physicalTerrainMin: Tile2i.Zero,
                physicalTerrainMax: new Tile2i(32, 32),
                vehicleWidth: 5);
            if (providerSnapshot.V2FixedNavigationGraph == null)
            {
                failure =
                    "V2 snapshots with projected fixed work must build FV navigation";
                return false;
            }
            AccessV2BandProfile.TryCreateEnabled(
                AccessV2TravelAxis.X, providerFlat, providerFlat,
                out AccessV2BandProfile providerBand, out _);
            var providerGoalState = new AccessV2BandState(
                new Tile2i(4, 4), providerBand, new Tile2i(4, 0));
            var projectedChainEndpoints = new AccessV2EndpointSet(
                new[]
                {
                    new AccessV2StartFrontage(
                        providerGoalState, new Tile2i(4, 4)),
                },
                new AccessV2FrontageDiagnostics());
            var projectedChainSearch = new AccessV2SearchSession(
                projectedChainEndpoints,
                Tile2i.Zero, new Tile2i(32, 32),
                (current, transition, history, connectedFixedOrigin) =>
                    AccessV2TransitionEvaluation.Reject(
                        "ProjectedChainMustNotGenerateV"),
                maxVisited: 1000,
                maxCost: float.MaxValue,
                handoffEvaluator: (recent, history, requiredGroundEntry) =>
                    Array.Empty<AccessV2HandoffCandidate>(),
                groundGraph: providerSnapshot.V2GroundGraph,
                fixedNavigationGraph:
                    providerSnapshot.V2FixedNavigationGraph,
                groundValidator:
                    providerSnapshot.IsProjectedV2CenterPathable,
                groundHeightProvider: tile =>
                    providerSnapshot.TryGetGroundHeight2(
                        tile, out int height2) ? height2 : (int?)null);
            while (!projectedChainSearch.IsComplete)
                projectedChainSearch.Step(1000);
            if (!projectedChainSearch.Result.Success
                || projectedChainSearch.Result.GeneratedProfiles.Count != 0
                || projectedChainSearch.Result.GroundPath.Count < 2
                || Math.Abs(projectedChainSearch.Result.Cost - 6f) > 0.0001f)
            {
                failure =
                    "V2 projected fixed provider chain must reach tower ground "
                    + "with exact travel and zero generated origins: "
                    + $"success={projectedChainSearch.Result.Success} "
                    + $"reason={projectedChainSearch.Result.FailureReason} "
                    + $"generated={projectedChainSearch.Result.GeneratedProfiles.Count} "
                    + $"ground={projectedChainSearch.Result.GroundPath.Count} "
                    + $"cost={projectedChainSearch.Result.Cost:0.###}";
                return false;
            }
            Tile2i projectedSourceCenter =
                AccessV2PotentialField.GetCanonicalCenter(
                    providerGoalState);
            var deadProjectedCenters = new[]
            {
                projectedSourceCenter,
                projectedSourceCenter + new RelTile2i(1, 0),
                projectedSourceCenter + new RelTile2i(2, 0),
            };
            var deadProjectedGraph = new AccessV2GroundGraph(
                deadProjectedCenters,
                Array.Empty<Tile2i>(),
                new Dictionary<Tile2i, AccessPropCleanupInfo>(),
                deadProjectedCenters);
            var deadProjectedSearch = new AccessV2SearchSession(
                projectedChainEndpoints,
                Tile2i.Zero, new Tile2i(32, 32),
                (current, transition, history, connectedFixedOrigin) =>
                    AccessV2TransitionEvaluation.Reject(
                        "FixtureVRouteUnavailable"),
                maxVisited: 100,
                maxCost: float.MaxValue,
                handoffEvaluator: (recent, history, requiredGroundEntry) =>
                    Array.Empty<AccessV2HandoffCandidate>(),
                groundGraph: deadProjectedGraph,
                generatedOriginValidator: _ => false);
            int deadProjectedGroundExplorations = 0;
            deadProjectedSearch.NodeExplored =
                (position, height2, isGround, groundHeight2) =>
                {
                    if (isGround)
                        deadProjectedGroundExplorations++;
                };
            while (!deadProjectedSearch.IsComplete)
                deadProjectedSearch.Step(100);
            if (deadProjectedSearch.Result.Success
                || deadProjectedGroundExplorations != 0)
            {
                failure =
                    "A projected source component with neither a goal nor a "
                    + "legal G-to-V exit must not enqueue a dead ground branch: "
                    + $"groundExplorations={deadProjectedGroundExplorations}";
                return false;
            }
            if (AccessReachability.ClassifyProjectedProvider(
                    isLiveReady: true)
                    != AccessClusterState.AccessibleViaProvider
                || AccessReachability.ClassifyProjectedProvider(
                    isLiveReady: false)
                    != AccessClusterState.WaitingForProviderCompletion)
            {
                failure =
                    "V2 projected provider must distinguish live access from "
                    + "waiting for fixed terrain work to complete";
                return false;
            }
            var overlayIntent = new GenericWorkIntent("overlay-fixture");
            var overlayCluster = new AccessOriginCluster(
                7,
                new[]
                {
                    new AccessWorkOrigin(
                        new Tile2i(0, 0), overlayIntent, false),
                    new AccessWorkOrigin(
                        new Tile2i(8, 0), overlayIntent, false),
                },
                new[] { overlayIntent });
            IReadOnlyList<AccessClusterOverlayRecord> overlayRecords =
                AccessReachability.BuildOverlayRecords(
                    new[] { overlayCluster },
                    new Dictionary<AccessOriginCluster, AccessClusterState>
                    {
                        [overlayCluster] =
                            AccessClusterState.WaitingForProviderCompletion,
                    });
            if (overlayRecords.Count != 1
                || overlayRecords[0].ClusterId != 7
                || overlayRecords[0].State
                    != AccessClusterState.WaitingForProviderCompletion
                || overlayRecords[0].OriginCount != 2
                || Math.Abs(overlayRecords[0].CenterX - 6f) > 0.0001f
                || Math.Abs(overlayRecords[0].CenterY - 2f) > 0.0001f
                || overlayRecords[0].CenterRoots.Count != 2
                || overlayRecords[0].CenterRoots[0] != new Tile2i(0, 0)
                || overlayRecords[0].CenterRoots[1] != new Tile2i(8, 0))
            {
                failure =
                    "Cluster overlay records must retain stable identity, "
                    + "state, arithmetic center, and tied center roots";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool CreateHistoryForState(
            AccessV2BandState state,
            out AccessV2History history,
            out string failure)
            => AccessV2History.Empty.TryApply(
                new[] { state.GetLane(0), state.GetLane(1) },
                Array.Empty<Tile2i>(), out history, out failure);
    }
}

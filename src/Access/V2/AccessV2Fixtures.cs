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
            if (!ValidateHandoffs(out failure)) return false;
            if (!ValidateFrontages(out failure)) return false;
            if (!ValidateUsefulHeightEnvelope(out failure)) return false;
            if (!ValidateSearch(out failure)) return false;
            if (!ValidateBounds(out failure)) return false;
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
                if (turn.Delta.Count != 2
                    || turn.OldDirectionTurnRays.Count != 3
                    || turn.Next.Axis == axis)
                {
                    failure = "Turn footprint/ray count failed for " + direction;
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
                boundsMin, boundsMax, oneWide, new[] { seed },
                Array.Empty<Tile2i>(),
                (origin, profile) => AccessV2SyntheticValidation.Valid);
            if (synthetic.Starts.Count != 8
                || synthetic.Diagnostics.SyntheticStartCount != 8
                || synthetic.Diagnostics.ExistingPairStartCount != 0)
            {
                failure = "One-wide flat seed must produce eight synthetic frontage orientations";
                return false;
            }

            oneWide[seed] = xPositive;
            AccessV2EndpointSet ramp = AccessV2FrontageDiscovery.Build(
                boundsMin, boundsMax, oneWide, new[] { seed },
                Array.Empty<Tile2i>(),
                (origin, profile) => AccessV2SyntheticValidation.Valid);
            if (ramp.Starts.Count != 4
                || ramp.Starts.Any(start => start.State.Axis != AccessV2TravelAxis.X))
            {
                failure = "One-wide ramp seed must produce only along-axis companions";
                return false;
            }

            AccessV2EndpointSet blocked = AccessV2FrontageDiscovery.Build(
                boundsMin, boundsMax, oneWide, new[] { seed },
                Array.Empty<Tile2i>(),
                (origin, profile) => new AccessV2SyntheticValidation(
                    false, "Building"));
            if (blocked.Starts.Count != 0
                || !blocked.Diagnostics.Rejections.ContainsKey("Building"))
            {
                failure = "Blocked synthetic companions must fail diagnostically";
                return false;
            }

            Tile2i paired = new Tile2i(8, 12);
            var fixedPair = new Dictionary<Tile2i, AccessHeightProfile>
            {
                [seed] = flat,
                [paired] = flat,
            };
            AccessV2EndpointSet existing = AccessV2FrontageDiscovery.Build(
                boundsMin, boundsMax, fixedPair, new[] { seed },
                Array.Empty<Tile2i>(),
                (origin, profile) => AccessV2SyntheticValidation.Valid);
            if (!existing.Starts.Any(start => !start.HasSyntheticCompanion))
            {
                failure = "Existing compatible companion must form a start frontage";
                return false;
            }

            AccessV2EndpointSet exposedGoals = AccessV2FrontageDiscovery.Build(
                boundsMin, boundsMax, fixedPair, Array.Empty<Tile2i>(),
                new[] { seed, paired },
                (origin, profile) => AccessV2SyntheticValidation.Valid);
            if (exposedGoals.FixedGoals.Count != 2)
            {
                failure = "Adjacent fixed pair must expose both open outer frontages";
                return false;
            }

            Tile2i yAxisPair = new Tile2i(12, 8);
            var verticalPair = new Dictionary<Tile2i, AccessHeightProfile>
            {
                [seed] = flat,
                [yAxisPair] = flat,
            };
            AccessV2EndpointSet verticalGoals = AccessV2FrontageDiscovery.Build(
                boundsMin, boundsMax, verticalPair, Array.Empty<Tile2i>(),
                new[] { seed, yAxisPair },
                (origin, profile) => AccessV2SyntheticValidation.Valid);
            if (verticalGoals.FixedGoals.Count != 2
                || verticalGoals.FixedGoals.Any(
                    goal => goal.State.Axis != AccessV2TravelAxis.Y))
            {
                failure = "Fixed frontage discovery must be symmetric across both axes";
                return false;
            }

            fixedPair[new Tile2i(12, 8)] = flat;
            fixedPair[new Tile2i(12, 12)] = flat;
            AccessV2EndpointSet oneSideExposed = AccessV2FrontageDiscovery.Build(
                boundsMin, boundsMax, fixedPair, Array.Empty<Tile2i>(),
                new[] { seed, paired },
                (origin, profile) => AccessV2SyntheticValidation.Valid);
            if (oneSideExposed.FixedGoals.Count != 1
                || oneSideExposed.FixedGoals[0].ExposedDirection
                    != new Tile2i(-4, 0))
            {
                failure = "Fixed goal frontage must reject the occupied outer edge only";
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
                || graph.CanTraverse(cleanupB, unrelatedDebris))
            {
                failure = "Mega cleanup ground topology classification failed";
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
                || !global::AutoTerrainDesignations.AutoDepthDesignation
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
                || reached.Contains(unrelatedDebris)
                || graph.Flood(new Tile2i(10, 10)).Count != 1)
            {
                failure = "Mega cleanup flood or isolated-pocket fixture failed";
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
                            false, true, true, generatedPropKey,
                            new[] { new Tile2i(8, 4) }),
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
            if (eligibleDenseSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Dumping,
                    generatedPropCenter, handoffLevelHistory)
                || !eligibleDenseSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Dumping,
                    generatedPropCenter, generatedPropHistory))
            {
                failure = "V2 dumping handoff must keep dense props blocking until a side/generated cleanup key clears them";
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
            if (eligibleDenseSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Dumping,
                    generatedPropCenter, occupiedSideHistory))
            {
                failure = "V2 dumping handoff must treat a protruding prop as hard when its only neighboring cleanup origin is occupied by V";
                return false;
            }
            Tile2i sideCleanupOrigin = new Tile2i(8, 4);
            if (!eligibleDenseSnapshot.IsV2HandoffGroundEntryPathable(
                    generatedPropCenter,
                    Array.Empty<Tile2i>(), generatedPropHistory)
                || eligibleDenseSnapshot.IsV2HandoffGroundEntryPathable(
                    generatedPropCenter,
                    Array.Empty<Tile2i>(), occupiedSideHistory)
                || !eligibleDenseSnapshot.IsV2HandoffGroundEntryPathable(
                    generatedPropCenter,
                    new[] { sideCleanupOrigin }, occupiedSideHistory))
            {
                failure = "V2 G entry must require either a genuinely free occupied neighbor or mining/leveling work that owns that neighbor";
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
            if (sameOriginDenseSnapshot.IsV2HandoffCenterPathable(
                    generatedPropOrigin, AccessHandoffOperation.Dumping,
                    generatedPropCenter,
                    handoffLevelHistory.ApplyCleanupKeys(
                        new[] { sameOriginPropKey })))
            {
                failure = "V2 dumping handoff must not count a cleanup on its own origin as the required side prop clearing";
                return false;
            }
            if (sameOriginDenseSnapshot.IsV2HandoffGroundEntryPathable(
                    generatedPropCenter,
                    Array.Empty<Tile2i>(),
                    handoffLevelHistory.ApplyCleanupKeys(
                        new[] { sameOriginPropKey })))
            {
                failure = "V2 G entry must keep a same-origin dumping prop hard when no neighboring occupied cleanup origin exists";
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
                new[] { new Tile2i(11, 0) },
                new Dictionary<Tile2i, AccessPropCleanupInfo>());
            var disconnectedVPotential = new AccessV2PotentialField(
                Tile2i.Zero, new Tile2i(11, 0),
                disconnectedGraph,
                Array.Empty<AccessV2FixedFrontage>(),
                minimumVTravelCostPerTile: 2.25f);
            var disconnectedEscapePotential =
                new AccessV2GroundEscapePotentialField(
                    disconnectedGraph, disconnectedVPotential,
                    minimumGeneratedEntryCost: 10f);
            if (disconnectedGraph.TryGetGoalDistance(
                    new Tile2i(0, 0), out _)
                || Math.Abs(disconnectedVPotential.GetPotential(
                    new Tile2i(0, 0)) - 23.5f) > 0.0001f
                || Math.Abs(disconnectedEscapePotential.GetPotential(
                    new Tile2i(0, 0)) - 31f) > 0.0001f)
            {
                failure = "Disconnected V2 G components must retain a component-aware V escape heuristic";
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
                failure = "V2 hull must retain its upper bound for each newly reached ramp-lane center";
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
            if (!envelope.IsV1CenterHeightUseful(
                    allowanceSample, -16, out _)
                || envelope.IsV1CenterHeightUseful(
                    allowanceSample, -17, out string v1Rejection)
                || v1Rejection != "HeightEnvelopeBelow"
                || !envelope.IsV2CenterHeightUseful(
                    allowanceSample, -32, out _)
                || envelope.IsV2CenterHeightUseful(
                    allowanceSample, -33, out string v2Rejection)
                || v2Rejection != "HeightEnvelopeBelow")
            {
                failure = "Useful-height hull must allow V1 centers 0.5 and V2 centers 1.0 below its lower bound";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ValidateSearch(out string failure)
        {
            if (!TryCreateUniformState(
                    new Tile2i(4, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState start, out failure)
                || !TryCreateUniformState(
                    new Tile2i(16, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState goalState, out failure))
                return false;

            var endpoints = new AccessV2EndpointSet(
                new[]
                {
                    new AccessV2StartFrontage(start, start.Anchor, null),
                },
                new[]
                {
                    new AccessV2FixedFrontage(
                        goalState, new Tile2i(-4, 0), terminalCost: 7f),
                },
                new AccessV2FrontageDiagnostics());
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

            var straight = new AccessV2SearchSession(
                endpoints, Tile2i.Zero, new Tile2i(32, 32),
                UnitEvaluator, 10000, float.MaxValue);
            while (!straight.IsComplete) straight.Step(7);
            if (!straight.Result.Success
                || straight.Result.States.Count != 3
                || straight.Result.GeneratedProfiles.Count != 4
                || Math.Abs(straight.Result.Cost - 19f) > 0.0001f
                || Math.Abs(straight.Result.TraversalCost - 15f) > 0.0001f
                || !straight.Result.Rejections.ContainsKey(
                    "FlatStrafeDominatedByTurn"))
            {
                failure = "V2 Dijkstra fixed-provider cost or flat-strafe dominance failed";
                return false;
            }

            var fixedPotential = new AccessV2PotentialField(
                Tile2i.Zero, new Tile2i(32, 32),
                ground: null, fixedGoals: endpoints.FixedGoals);
            Tile2i fixedMatchCenter =
                AccessV2PotentialField.GetCanonicalCenter(goalState)
                + new RelTile2i(-4, 0);
            if (Math.Abs(fixedPotential.GetPotential(start) - 15f) > 0.0001f
                || Math.Abs(fixedPotential.GetPotential(fixedMatchCenter) - 7f)
                    > 0.0001f)
            {
                failure = "V2 fixed-frontage charged potential seed or propagation failed";
                return false;
            }
            const float fixtureFixedOriginCost = 5f;
            float minimumVRate =
                AccessV2CostModel.GetMinimumVTravelCostPerTile(
                    fixtureFixedOriginCost);
            float centerSpoke = AccessV2CostModel.GetCenterSpokeCost(
                fixtureFixedOriginCost);
            var weightedPotential = new AccessV2PotentialField(
                Tile2i.Zero, new Tile2i(32, 32),
                ground: null, fixedGoals: endpoints.FixedGoals,
                minimumVTravelCostPerTile: minimumVRate);
            if (Math.Abs(minimumVRate - 2.25f) > 0.0001f
                || Math.Abs(centerSpoke - 4.5f) > 0.0001f
                || Math.Abs(weightedPotential.GetPotential(start) - 25f)
                    > 0.0001f)
            {
                failure = "V2 weighted cardinal potential and center spoke must share the minimum V rate";
                return false;
            }
            var straightAStar = new AccessV2SearchSession(
                endpoints, Tile2i.Zero, new Tile2i(32, 32),
                UnitEvaluator, 10000, float.MaxValue,
                potentialField: fixedPotential);
            while (!straightAStar.IsComplete) straightAStar.Step(7);
            if (!straightAStar.Result.Success
                || !straightAStar.Result.UsedAStar
                || Math.Abs(straightAStar.Result.Cost
                    - straight.Result.Cost) > 0.0001f
                || !straightAStar.Result.States.SequenceEqual(
                    straight.Result.States))
            {
                failure = "V2 fixed-frontage potential A* must reproduce Dijkstra";
                return false;
            }

            if (!TryCreateUniformState(
                    new Tile2i(24, 4), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.Flat, 0,
                    out AccessV2BandState farGoalState, out failure))
                return false;
            var feeChoiceEndpoints = new AccessV2EndpointSet(
                endpoints.Starts,
                new[]
                {
                    new AccessV2FixedFrontage(
                        goalState, new Tile2i(-4, 0), terminalCost: 20f),
                    new AccessV2FixedFrontage(
                        farGoalState, new Tile2i(-4, 0), terminalCost: 0f),
                },
                new AccessV2FrontageDiagnostics());
            var feeChoiceDijkstra = new AccessV2SearchSession(
                feeChoiceEndpoints, Tile2i.Zero, new Tile2i(36, 32),
                UnitEvaluator, 10000, float.MaxValue);
            while (!feeChoiceDijkstra.IsComplete) feeChoiceDijkstra.Step(7);
            var feeChoicePotential = new AccessV2PotentialField(
                Tile2i.Zero, new Tile2i(36, 32),
                ground: null, fixedGoals: feeChoiceEndpoints.FixedGoals);
            var feeChoiceAStar = new AccessV2SearchSession(
                feeChoiceEndpoints, Tile2i.Zero, new Tile2i(36, 32),
                UnitEvaluator, 10000, float.MaxValue,
                potentialField: feeChoicePotential);
            while (!feeChoiceAStar.IsComplete) feeChoiceAStar.Step(7);
            if (!feeChoiceDijkstra.Result.Success
                || !feeChoiceAStar.Result.Success
                || Math.Abs(feeChoiceDijkstra.Result.Cost - 24f) > 0.0001f
                || Math.Abs(feeChoiceAStar.Result.Cost - 24f) > 0.0001f
                || feeChoiceDijkstra.Result.States.Last().Anchor
                    != new Tile2i(20, 4)
                || !feeChoiceAStar.Result.States.SequenceEqual(
                    feeChoiceDijkstra.Result.States))
            {
                failure = "V2 must prefer lower total cost over nearer fixed frontage geometry";
                return false;
            }

            if (!TryCreateUniformState(
                    new Tile2i(4, 20), AccessV2TravelAxis.X,
                    new Tile2i(4, 0), AccessSearchMode.XPositive, 1,
                    out AccessV2BandState rampStart, out failure)
                || !AccessV2Geometry.TryStraight(
                    rampStart, out AccessV2Transition rampStep1, out failure)
                || !AccessV2Geometry.TryStraight(
                    rampStep1.Next, out AccessV2Transition rampStep2, out failure))
                return false;
            var rampGoal = new AccessV2BandState(
                rampStep2.Next.Anchor,
                rampStep2.Next.Band,
                rampStep2.Next.EntryDirection);
            var rampEndpoints = new AccessV2EndpointSet(
                new[]
                {
                    new AccessV2StartFrontage(
                        rampStart, rampStart.Anchor, null),
                },
                new[]
                {
                    new AccessV2FixedFrontage(
                        rampGoal, new Tile2i(-4, 0)),
                },
                new AccessV2FrontageDiagnostics());
            var ramp = new AccessV2SearchSession(
                rampEndpoints, Tile2i.Zero, new Tile2i(40, 36),
                UnitEvaluator, 10000, float.MaxValue);
            while (!ramp.IsComplete) ramp.Step(9);
            if (!ramp.Result.Success
                || ramp.Result.States.Count != 2
                || ramp.Result.States[1].Band.Kind
                    != AccessV2BandProfileKind.UniformRamp)
            {
                failure = "V2 Dijkstra uniform-ramp route failed";
                return false;
            }

            if (!AccessV2Geometry.TryStrafe(
                    rampStep1.Next, 1, out AccessV2Transition desiredStrafe,
                    out failure)
                || !AccessV2Geometry.TryStraight(
                    desiredStrafe.Next,
                    out AccessV2Transition strafeExit, out failure))
                return false;
            var strafeGoal = new AccessV2BandState(
                strafeExit.Next.Anchor,
                strafeExit.Next.Band,
                strafeExit.Next.EntryDirection);
            var strafeEndpoints = new AccessV2EndpointSet(
                rampEndpoints.Starts,
                new[]
                {
                    new AccessV2FixedFrontage(
                        strafeGoal, new Tile2i(-4, 0)),
                },
                new AccessV2FrontageDiagnostics());
            var strafe = new AccessV2SearchSession(
                strafeEndpoints, Tile2i.Zero, new Tile2i(40, 36),
                UnitEvaluator, 10000, float.MaxValue);
            while (!strafe.IsComplete) strafe.Step(5);
            if (!strafe.Result.Success
                || strafe.Result.States.Count != 3
                || strafe.Result.States[2].Anchor
                    != desiredStrafe.Next.Anchor
                || strafe.Result.GeneratedProfiles.Count != 4)
            {
                failure = "V2 Dijkstra swept-width strafe or delta ownership failed"
                    + $": success={strafe.Result.Success}"
                    + $" states={strafe.Result.States.Count}"
                    + $" generated={strafe.Result.GeneratedProfiles.Count}"
                    + $" reason={strafe.Result.FailureReason}";
                return false;
            }

            if (!AccessV2Geometry.TryStraight(
                    start, out AccessV2Transition landingAdvance, out failure)
                || !AccessV2Geometry.TryTurn(
                    start, landingAdvance.Next, 1,
                    out AccessV2Transition turn, out failure))
                return false;
            Tile2i turnGoalAnchor = AccessV2Geometry.Add(
                turn.Next.Anchor, turn.Next.EntryDirection);
            var turnGoalState = new AccessV2BandState(
                turnGoalAnchor, turn.Next.Band, turn.Next.EntryDirection);
            var turnEndpoints = new AccessV2EndpointSet(
                endpoints.Starts,
                new[]
                {
                    new AccessV2FixedFrontage(
                        turnGoalState,
                        AccessV2Geometry.Scale(turn.Next.EntryDirection, -1)),
                },
                new AccessV2FrontageDiagnostics());
            var switchback = new AccessV2SearchSession(
                turnEndpoints, Tile2i.Zero, new Tile2i(40, 40),
                UnitEvaluator, 50000, float.MaxValue);
            while (!switchback.IsComplete) switchback.Step(11);
            if (!switchback.Result.Success
                || switchback.Result.States.Count < 3
                || !switchback.Result.States.Any(
                    state => state.Axis == AccessV2TravelAxis.Y))
            {
                failure = "V2 Dijkstra flat 2x2 landing turn route failed";
                return false;
            }

            AccessV2TransitionEvaluation BlockForward(
                AccessV2BandState? current,
                AccessV2Transition transition,
                AccessV2History history,
                Tile2i? connectedFixedOrigin)
                => transition.Delta.Any(item => item.Origin.X >= 8)
                    ? AccessV2TransitionEvaluation.Reject("InjectedDurability")
                    : UnitEvaluator(current, transition, history, connectedFixedOrigin);
            var blocked = new AccessV2SearchSession(
                endpoints, new Tile2i(0, 0), new Tile2i(20, 16),
                BlockForward, 10000, float.MaxValue);
            while (!blocked.IsComplete) blocked.Step(13);
            if (blocked.Result.Success
                || !blocked.Result.Rejections.ContainsKey("InjectedDurability"))
            {
                failure = "V2 Dijkstra durability/no-path rejection failed";
                return false;
            }

            string sharedCleanup = "prop:shared";
            AccessV2TransitionEvaluation CleanupEvaluator(
                AccessV2BandState? current,
                AccessV2Transition transition,
                AccessV2History history,
                Tile2i? connectedFixedOrigin)
            {
                bool charge = !history.ContainsCleanupKey(sharedCleanup);
                return new AccessV2TransitionEvaluation(
                    true, string.Empty,
                    current.HasValue ? 4f : 0f,
                    transition.Delta.Count,
                    charge ? 8f : 0f,
                    cleanupKeys: charge
                        ? new[] { sharedCleanup }
                        : Array.Empty<string>());
            }
            var cleanup = new AccessV2SearchSession(
                endpoints, Tile2i.Zero, new Tile2i(32, 32),
                CleanupEvaluator, 10000, float.MaxValue);
            while (!cleanup.IsComplete) cleanup.Step(17);
            if (!cleanup.Result.Success
                || Math.Abs(cleanup.Result.Cost - 27f) > 0.0001f)
            {
                failure = "V2 Dijkstra cleanup keys were charged more than once";
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
                    postWorkCenterValidator:
                        (origin, operation, center, history) => true);
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
                failure = "V2 post-work handoff must start on rank two inside files three through six and end on G"
                    + $": candidate={(corridorForward == null ? "none" : corridorForward.ToString())}"
                    + $" escape={(corridorForward == null ? "none" : string.Join(",", corridorForward.EscapeCenters))}";
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
                        (origin, operation, center, history) => true,
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
                        (origin, operation, center, history) =>
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

            IReadOnlyList<AccessV2HandoffCandidate> brokenPostWorkCorridor =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, UniformSingle, Span,
                    postWorkCenterValidator:
                        (origin, operation, center, history) =>
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
                new[] { new AccessV2StartFrontage(first, first.Anchor, null) },
                Array.Empty<AccessV2FixedFrontage>(),
                new AccessV2FrontageDiagnostics());
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

            Tile2i sparseGoal = new Tile2i(28, 10);
            var aStarDiagnostics = new AccessSearchDiagnostics();
            var aStarSession = new AccessV2SearchSession(
                endpoints, Tile2i.Zero, new Tile2i(32, 32),
                UnitEvaluator, 10000, float.MaxValue,
                (states, history, requiredGroundEntry) =>
                    states[0].Anchor == first.Anchor
                    ? new[] { forward }
                    : Array.Empty<AccessV2HandoffCandidate>(),
                state => Math.Abs(
                        AccessPathSearch.GetV2CanonicalCenter(state).X
                        - sparseGoal.X)
                    + Math.Abs(
                        AccessPathSearch.GetV2CanonicalCenter(state).Y
                        - sparseGoal.Y),
                graph,
                diagnostics: aStarDiagnostics);
            while (!aStarSession.IsComplete) aStarSession.Step(7);
            if (!aStarSession.Result.Success
                || !aStarSession.Result.UsedAStar
                || Math.Abs(aStarSession.Result.Cost
                    - session.Result.Cost) > 0.0001f
                || !aStarSession.Result.States.SequenceEqual(
                    session.Result.States)
                || aStarDiagnostics.V2GroundSuffixSuccesses == 0
                || aStarDiagnostics.V2GroundSuffixSteps == 0)
            {
                failure = "V2 A* must retain the exact Dijkstra result while completing a validated potential-field G suffix"
                    + $": suffix={aStarDiagnostics.V2GroundSuffixSuccesses}/"
                    + $"{aStarDiagnostics.V2GroundSuffixSteps}";
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
                        AccessV2SearchSession.CanUseDirectGroundToVLevelingBridge(
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
            if (!AccessV2SearchSession.CanUseDirectGroundToVLevelingBridge(
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
            if (!TryRoughBridge(
                    tile => tile.X == 760 ? 3.25f : 3f,
                    AccessHandoffOperation.Mining, null, out _)
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
                failure = "Deterministic G-to-V proof must enforce inclusive 0.25 face and 0.5 step limits, including the reached G mask";
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
            var replaySnapshot = new AccessSearchSnapshot(
                Tile2i.Zero, new Tile2i(32, 32), new Tile2i(28, 10),
                -2, 2, true, true, false, 1f, 1f,
                heightByTile,
                centerByOrigin,
                new Dictionary<Tile2i, AccessHeightProfile>
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
                preciseTerrainHeights: preciseByTile,
                physicalTerrainMin: Tile2i.Zero,
                physicalTerrainMax: new Tile2i(32, 32),
                vehicleWidth: 5,
                v2WorkableHandoffs: UniformSingle,
                v2WorkableHandoffSpans: Span);
            AccessV2TransitionEvaluation omittedInternalBand =
                AccessPathSearch.EvaluateV2Transition(
                    replaySnapshot, first, secondStep,
                    AccessV2History.Empty, null);
            if (!omittedInternalBand.IsValid
                || !omittedInternalBand.RequiresGroundTransition
                || Math.Abs(omittedInternalBand.GeneratedWorkCost) > 0.0001f)
            {
                failure = "V2 internal exact-terrain bands must become zero-work terminal G passages";
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
                        first, first.GetLaneOrigin(0), null),
                },
                Array.Empty<AccessV2FixedFrontage>(),
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
                    != exactGroundGoal
                || Math.Abs(
                    exactGroundSession.Result.GeneratedWorkCost) > 0.0001f)
            {
                failure = "V2 exact successor must hand off to G without expanding another V transition";
                return false;
            }
            if (!AccessV2Geometry.TryStrafe(
                    first, 1, out AccessV2Transition exactStrafe,
                    out failure))
                return false;
            AccessV2TransitionEvaluation omittedStrafeCell =
                AccessPathSearch.EvaluateV2Transition(
                    replaySnapshot, first, exactStrafe,
                    AccessV2History.Empty, null);
            if (omittedStrafeCell.IsValid
                || omittedStrafeCell.RejectionReason
                    != "StrafeRequiresCompleteMaterializedDelta")
            {
                failure = "V2 strafe must materialize its complete swept two-origin delta";
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
                    replaySnapshot, materializationResult);
            if (!materialized.IsValid
                || materialized.Designations.Count != 0
                || materialized.CleanupOrigins.Count != 1
                || materialized.HandoffOperationsByOrigin.Count != 1)
            {
                failure = "V2 replay must omit exact-terrain work while retaining cleanup and terminal ownership metadata: "
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
            AccessV2BandProfile.TryCreateEnabled(
                AccessV2TravelAxis.X, providerFlat, providerFlat,
                out AccessV2BandProfile providerBand, out _);
            var providerGoalState = new AccessV2BandState(
                new Tile2i(4, 4), providerBand, new Tile2i(4, 0));
            var providerEndpoints = new AccessV2EndpointSet(
                Array.Empty<AccessV2StartFrontage>(),
                new[]
                {
                    new AccessV2FixedFrontage(
                        providerGoalState, new Tile2i(-4, 0)),
                },
                new AccessV2FrontageDiagnostics());
            var providerField = new AccessV2ProviderDistanceField(
                providerSnapshot, providerProfiles.Keys);
            AccessV2EndpointSet chargedProviderEndpoints =
                providerField.ApplyTerminalCosts(providerEndpoints);
            if (providerField.ProviderNodeCount != 45
                || providerField.ConnectedNodeCount != 45
                || chargedProviderEndpoints.FixedGoals.Count != 1
                || Math.Abs(chargedProviderEndpoints.FixedGoals[0].TerminalCost
                    - 10f) > 0.0001f)
            {
                failure = "V2 accepted-provider field did not charge entry plus exact provider/G suffix";
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

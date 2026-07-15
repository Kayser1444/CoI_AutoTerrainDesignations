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
                        state, -1, out AccessV2Transition strafeLow,
                        out failure)
                    || strafeLow.Delta.Count != 1
                    || strafeLow.LocalContextOrigins.Count != 1
                    || !AccessV2Geometry.TryStrafe(
                        state, 1, out AccessV2Transition strafeHigh,
                        out failure)
                    || strafeHigh.Delta.Count != 1
                    || strafeHigh.Next.EntryDirection != direction)
                {
                    failure = "Strafe symmetry failed for " + direction;
                    return false;
                }

                if (!CreateHistoryForState(state, out AccessV2History history, out failure)
                    || !history.TryApply(strafeLow, out AccessV2History strafedHistory, out failure)
                    || strafedHistory.OriginCount != 3)
                {
                    failure = "Strafe retained-lane ownership failed for " + direction
                        + ": " + failure;
                    return false;
                }
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
            HashSet<Tile2i> reached = graph.Flood(new Tile2i(0, 0));
            if (!reached.Contains(cleanupB)
                || reached.Contains(unrelatedDebris)
                || graph.Flood(new Tile2i(10, 10)).Count != 1)
            {
                failure = "Mega cleanup flood or isolated-pocket fixture failed";
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
                        goalState, new Tile2i(-4, 0)),
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
                || Math.Abs(straight.Result.Cost - 12f) > 0.0001f)
            {
                failure = "V2 Dijkstra flat-straight route or delta-only cost failed";
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
                    start, 1, out AccessV2Transition desiredStrafe,
                    out failure))
                return false;
            Tile2i strafeGoalAnchor = AccessV2Geometry.Add(
                desiredStrafe.Next.Anchor,
                desiredStrafe.Next.EntryDirection);
            var strafeGoal = new AccessV2BandState(
                strafeGoalAnchor,
                desiredStrafe.Next.Band,
                desiredStrafe.Next.EntryDirection);
            var strafeEndpoints = new AccessV2EndpointSet(
                endpoints.Starts,
                new[]
                {
                    new AccessV2FixedFrontage(
                        strafeGoal, new Tile2i(-4, 0)),
                },
                new AccessV2FrontageDiagnostics());
            var strafe = new AccessV2SearchSession(
                strafeEndpoints, Tile2i.Zero, new Tile2i(32, 32),
                UnitEvaluator, 10000, float.MaxValue);
            while (!strafe.IsComplete) strafe.Step(5);
            if (!strafe.Result.Success
                || strafe.Result.States.Count != 2
                || strafe.Result.States[1].Anchor
                    != desiredStrafe.Next.Anchor
                || strafe.Result.GeneratedProfiles.Count != 1)
            {
                failure = "V2 Dijkstra retained-lane strafe or delta ownership failed";
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
                || Math.Abs(cleanup.Result.Cost - 20f) > 0.0001f)
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
                    secondStep.Next, out AccessV2Transition thirdStep, out failure))
                return false;

            var groundTiles = new List<Tile2i>();
            for (int y = 0; y <= 20; y++)
                for (int x = 0; x <= 28; x++)
                    if (x != 8 || y != 6)
                        groundTiles.Add(new Tile2i(x, y));
            Tile2i cleanupTile = new Tile2i(8, 6);
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
                Tile2i contact = outward.X != 0
                    ? new Tile2i(origin.X + (outward.X > 0 ? 4 : 0), origin.Y + 2)
                    : new Tile2i(origin.X + 2, origin.Y + (outward.Y > 0 ? 4 : 0));
                AccessHandoffOperation operation = (origin.X + origin.Y) % 8 == 0
                    ? AccessHandoffOperation.Mining
                    : AccessHandoffOperation.Dumping;
                return new[]
                {
                    new AccessGroundHandoff(contact, operation, new[] { contact }),
                };
            }

            IReadOnlyList<AccessGroundHandoff> Span(
                IReadOnlyList<AccessHandoffSpanCell> cells)
            {
                AccessHandoffSpanCell last = cells[cells.Count - 1];
                Tile2i contact = new Tile2i(
                    last.Origin.X + 4, last.Origin.Y + 2);
                return new[]
                {
                    new AccessGroundHandoff(
                        contact, AccessHandoffOperation.Mining,
                        new[] { contact }, cells.Count),
                };
            }

            IReadOnlyList<AccessV2HandoffCandidate> candidates =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, Single, Span);
            AccessV2HandoffCandidate? forward = candidates.FirstOrDefault(
                item => item.ExitDirection == new Tile2i(4, 0));
            if (forward == null
                || forward.Lane0Operation == forward.Lane1Operation
                || Math.Abs(forward.CenterSpokeCost - 2f) > 0.0001f
                || Math.Abs(forward.CleanupCost - 8f) > 0.0001f)
            {
                failure = "V2 forward seam must retain mixed lane operations, cleanup, and the two-cost center spoke";
                return false;
            }

            IReadOnlyList<AccessV2HandoffCandidate> quickCandidates =
                AccessV2Handoffs.Evaluate(
                    new[] { first }, AccessV2History.Empty,
                    graph, Single, Span,
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
                    disconnectedLocalGraph, Single, Span,
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
                    graph, Single, Span,
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
                    graph, Single, Span, 1f,
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
                    graph, Single, Span, 1f,
                    (center, _) => true,
                    (center, _) => center.X < 10);
            AccessV2HandoffCandidate? extendedForward =
                projectedExtended.FirstOrDefault(
                    item => item.ExitDirection == new Tile2i(4, 0));
            if (extendedForward == null
                || !extendedForward.EscapeCenters.Contains(
                    new Tile2i(10, 6))
                || !extendedForward.EscapeCenters.Contains(
                    new Tile2i(10, 10)))
            {
                failure = "V2 seam must extend each escape until the complete resolved-vehicle mask clears projected work";
                return false;
            }

            IReadOnlyList<AccessV2HandoffCandidate> recent =
                AccessV2Handoffs.Evaluate(
                    new[] { thirdStep.Next, secondStep.Next, first },
                    AccessV2History.Empty,
                    graph, Single, Span);
            if (!recent.Any(item => item.SpanLength == 2)
                || !recent.Any(item => item.SpanLength == 3)
                || !recent.Any(item => item.ExitDirection.Y != 0))
            {
                failure = "V2 seam must expose lateral exits and common two-/three-row spans";
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
                (states, history) => states[0].Anchor == first.Anchor
                    ? new[] { forward }
                    : Array.Empty<AccessV2HandoffCandidate>(),
                groundGraph: graph);
            while (!session.IsComplete) session.Step(7);
            int expectedGroundDistance = forward.GroundEntryCenters
                .Select(center => graph.TryGetGoalDistance(center, out int distance)
                    ? distance : int.MaxValue)
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

            Tile2i sparseGoal = new Tile2i(28, 10);
            var aStarSession = new AccessV2SearchSession(
                endpoints, Tile2i.Zero, new Tile2i(32, 32),
                UnitEvaluator, 10000, float.MaxValue,
                (states, history) => states[0].Anchor == first.Anchor
                    ? new[] { forward }
                    : Array.Empty<AccessV2HandoffCandidate>(),
                state => Math.Abs(
                        AccessPathSearch.GetV2CanonicalCenter(state).X
                        - sparseGoal.X)
                    + Math.Abs(
                        AccessPathSearch.GetV2CanonicalCenter(state).Y
                        - sparseGoal.Y),
                graph);
            while (!aStarSession.IsComplete) aStarSession.Step(7);
            if (!aStarSession.Result.Success
                || !aStarSession.Result.UsedAStar
                || Math.Abs(aStarSession.Result.Cost
                    - session.Result.Cost) > 0.0001f
                || !aStarSession.Result.States.SequenceEqual(
                    session.Result.States))
            {
                failure = "V2 A* and Dijkstra must retain the same ground-terminal route and exact cost";
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
                v2WorkableHandoffs: Single,
                v2WorkableHandoffSpans: Span);
            var materializationRoute = new AccessV2RouteData(
                new[] { first },
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [syntheticOrigin] = first.GetLane(1).Profile,
                },
                forward,
                forward?.GroundEntryCenters ?? Array.Empty<Tile2i>());
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

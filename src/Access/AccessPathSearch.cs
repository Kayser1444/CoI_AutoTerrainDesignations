using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mafi;
using AutoTerrainDesignations.Access.V2;

namespace AutoTerrainDesignations.Access
{
    internal static class AccessPathSearch
    {
        private static readonly Tile2i[] s_originDirections =
        {
            new Tile2i(4, 0), new Tile2i(-4, 0), new Tile2i(0, 4), new Tile2i(0, -4)
        };

        private static readonly RelTile2i[] s_tileDirections =
        {
            new RelTile2i(1, 0), new RelTile2i(-1, 0),
            new RelTile2i(0, 1), new RelTile2i(0, -1),
            new RelTile2i(1, 1), new RelTile2i(1, -1),
            new RelTile2i(-1, 1), new RelTile2i(-1, -1),
        };
        private const float GroundDiagonalCost = 1.41421356237f;

        private static readonly AccessSearchMode[] s_vModes =
        {
            AccessSearchMode.Flat,
            AccessSearchMode.XPositive,
            AccessSearchMode.XNegative,
            AccessSearchMode.YPositive,
            AccessSearchMode.YNegative
        };

        private static int MaxVisitedNodes => AutoTerrainDesignationsMod.AccessMaxVisitedNodes;
        private static float GeneratedVFixedOverhead => AutoTerrainDesignationsMod.AccessGeneratedVFixedCost;
        internal static float DirectWorkWeight => AutoTerrainDesignationsMod.AccessDirectWorkWeight;
        internal static float SideRayWeight => AutoTerrainDesignationsMod.AccessSideRayWeight;

        internal static int GetVehicleDisturbanceRadius(int vehicleClearance)
            => vehicleClearance >= 5 ? 2 : 1;

        internal static int GetMaxHandoffSpanLength(int vehicleWidth)
            => 1 + (Math.Max(1, vehicleWidth) + 3) / 4;

        public static bool ValidateCoreTransitions(out string failure)
        {
            if (Math.Abs(AutoDepthDesignation.InterpolateRaySlopeBounds(
                        0.8f, 1f, 1f) - 0.53333336f) > 0.0001f
                || Math.Abs(AutoDepthDesignation.InterpolateRaySlopeBounds(
                        0.8f, 1f, 0.5f) - 0.6f) > 0.0001f
                || Math.Abs(AutoDepthDesignation.InterpolateRaySlopeBounds(
                        0.8f, 1f, 0f) - 0.6666667f) > 0.0001f)
            { failure = "ray conservatism must interpolate intact material slope bounds from runniest to steepest"; return false; }
            if (Math.Abs(AutoDepthDesignation.InterpolateRaySlopeBounds(
                        0.8f, 1f, 1.5f) - 0.46666667f) > 0.0001f)
            { failure = "ray conservatism above one must extrapolate beyond the runniest stable bound up to 1.5"; return false; }

            AccessHeightProfile.TryForMode(AccessSearchMode.Flat, 0, out AccessHeightProfile flat);
            AccessHeightProfile.TryForMode(AccessSearchMode.XPositive, 1, out AccessHeightProfile xPositive);

            if (!TrySolveSuccessor(flat, new Tile2i(4, 0), AccessSearchMode.XPositive, out AccessHeightProfile rise)
                || rise.Center2 != 1)
            { failure = "F-to-X+ should rise by half a level"; return false; }
            if (!TrySolveSuccessor(xPositive, new Tile2i(4, 0), AccessSearchMode.XPositive, out AccessHeightProfile continueRise)
                || continueRise.Center2 != 3)
            { failure = "X+ continuation should rise by one level"; return false; }
            if (!TrySolveSuccessor(xPositive, new Tile2i(4, 0), AccessSearchMode.Flat, out AccessHeightProfile landing)
                || landing.Center2 != 2)
            { failure = "X+ should terminate on a flat landing"; return false; }
            if (!TrySolveSuccessor(xPositive, new Tile2i(0, 4), AccessSearchMode.XPositive, out AccessHeightProfile strafe)
                || strafe.Center2 != xPositive.Center2)
            { failure = "perpendicular X+ strafe should preserve height"; return false; }
            if (TrySolveSuccessor(xPositive, new Tile2i(0, 4), AccessSearchMode.XNegative, out _))
            { failure = "opposite signed perpendicular slopes must fight"; return false; }
            if (TrySolveSuccessor(xPositive, new Tile2i(4, 0), AccessSearchMode.YPositive, out _))
            { failure = "axis turn must require a flat landing"; return false; }

            var groundHeights = new Dictionary<Tile2i, int>();
            for (int x = 0; x <= 20; x++)
                for (int y = 0; y <= 20; y++)
                    groundHeights[new Tile2i(x, y)] = 0;
            var terrainCenters = new Dictionary<Tile2i, int>();
            for (int x = 0; x <= 16; x += 4)
                for (int y = 0; y <= 16; y += 4)
                    terrainCenters[new Tile2i(x, y)] = 0;
            Tile2i fixtureStart = new Tile2i(4, 4);
            Tile2i fixtureWorkNeighbor = new Tile2i(8, 4);
            Tile2i fixtureGoal = new Tile2i(10, 6);
            var fixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [fixtureStart] = flat,
                    [fixtureWorkNeighbor] = flat,
                },
                new[] { fixtureStart, fixtureWorkNeighbor },
                new[] { fixtureGoal },
                new[] { fixtureGoal },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                new[] { new AccessDurabilityCorner(new Tile2i(16, 16), 0) });
            if (!fixture.IsDurabilityBlocked(new Tile2i(17, 17), 4))
            { failure = "nearby higher point should be durability-blocked"; return false; }
            if (!fixture.IsDurabilityBlocked(new Tile2i(17, 17), -4))
            { failure = "nearby lower point should be durability-blocked"; return false; }
            if (fixture.IsDurabilityBlocked(new Tile2i(16, 0), 4))
            { failure = "distant same-axis point must not be durability-blocked"; return false; }
            if (fixture.IsDurabilityBlocked(new Tile2i(17, 17), 0))
            { failure = "equal-height point must not be durability-blocked"; return false; }
            Tile2i projectedFillTile = new Tile2i(6, 6);
            Tile2i connectedDesignation = new Tile2i(8, 8);
            Tile2i unrelatedDesignation = new Tile2i(12, 12);
            var projectedSourceFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, true, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>(),
                projectedFillDisturbedTiles: new[] { projectedFillTile },
                projectedFillSourcesByTile:
                    new Dictionary<Tile2i, HashSet<Tile2i>>
                    {
                        [projectedFillTile] = new HashSet<Tile2i>
                        {
                            connectedDesignation,
                        },
                    });
            if (projectedSourceFixture.GetSideRayBlockerReason(
                    projectedFillTile, AccessSideRayOperation.Cut)
                    != "SideRayOpposingDesignationWork"
                || projectedSourceFixture.GetSideRayBlockerReason(
                    projectedFillTile, AccessSideRayOperation.Cut,
                    connectedDesignation) != null)
            { failure = "connected predecessor must alone exempt its source-attributed projected disturbance"; return false; }
            var mixedProjectedSourceFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, true, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>(),
                projectedFillDisturbedTiles: new[] { projectedFillTile },
                projectedFillSourcesByTile:
                    new Dictionary<Tile2i, HashSet<Tile2i>>
                    {
                        [projectedFillTile] = new HashSet<Tile2i>
                        {
                            connectedDesignation,
                            unrelatedDesignation,
                        },
                    });
            if (mixedProjectedSourceFixture.GetSideRayBlockerReason(
                    projectedFillTile, AccessSideRayOperation.Cut,
                    connectedDesignation) != "SideRayOpposingDesignationWork")
            { failure = "connected predecessor exemption must retain unrelated projected-work blockers"; return false; }
            var footprintSource = new AccessDurabilityCorner(new Tile2i(16, 16), 0);
            if (!footprintSource.BlocksVehicleFootprint(new Tile2i(19, 16), 4, 1f, 2)
                || footprintSource.BlocksVehicleFootprint(new Tile2i(20, 16), 4, 1f, 2)
                || footprintSource.BlocksVehicleFootprint(new Tile2i(16, 16), 0, 1f, 2))
            { failure = "durability hourglass must expand by vehicle footprint without blocking equal-height traversal"; return false; }
            var scaleSource = new AccessDurabilityCorner(new Tile2i(16, 16), 0);
            if (scaleSource.Blocks(new Tile2i(18, 18), 4, 1f)
                || !scaleSource.Blocks(new Tile2i(18, 18), 4, 2f))
            { failure = "landslide horizontal-run scale should widen the exclusion envelope"; return false; }
            AccessHeightProfile.TryForMode(AccessSearchMode.Flat, 2, out AccessHeightProfile raisedFlat);
            if (fixture.IsCandidateProfileFeasible(new Tile2i(12, 12), raisedFlat, out string miningMismatch)
                || miningMismatch != "RequiresDumping")
            { failure = "mining candidate must reject profiles that require dumping"; return false; }
            var depressedGround = new Dictionary<Tile2i, int>(groundHeights)
            {
                [new Tile2i(14, 14)] = -2,
            };
            var depressedCenterFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -4, 4, true, false, false, 1f, 1f,
                depressedGround,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>());
            if (depressedCenterFixture.IsCandidateProfileFeasible(new Tile2i(12, 12), flat, out string interiorMismatch)
                || interiorMismatch != "RequiresDumping")
            { failure = "mining candidate must reject a target plane above an interior terrain sample"; return false; }
            var mixedWorkFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -4, 4, true, true, false, 1f, 1f,
                depressedGround,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>());
            if (!mixedWorkFixture.IsCandidateProfileFeasible(new Tile2i(12, 12), flat, out string mixedWorkMismatch))
            { failure = "leveling accessway profiles must allow mixed cut and fill: " + mixedWorkMismatch; return false; }
            var halfLevelProfile = new AccessHeightProfile(1, 1, 1, 1);
            if (mixedWorkFixture.IsCandidateProfileFeasible(
                    new Tile2i(12, 12), halfLevelProfile, out string halfLevelMismatch)
                || halfLevelMismatch != "HalfLevelCorner")
            { failure = "generated profiles with half-level corners must be rejected during expansion"; return false; }
            if (!AutoDepthDesignation.TrySelectHandoffOperationForProfile(
                    0, 1.5f, out AccessHandoffOperation singleOriginOperation)
                || singleOriginOperation != AccessHandoffOperation.Mining)
            { failure = "single-origin handoff should infer mining from the terminal profile when no predecessor exists"; return false; }
            if (AutoDepthDesignation.TrySelectHandoffOperationForProfile(
                    32, 16.1f, out AccessHandoffOperation flatOperation)
                || flatOperation != AccessHandoffOperation.None)
            { failure = "a profile matching quantized ground height must not create a topology handoff"; return false; }
            if (!AutoDepthDesignation.TrySelectHandoffOperationFromEdge(
                    new[] { -1, -1, 0, -1, -1 },
                    out AccessHandoffOperation edgeMining)
                || edgeMining != AccessHandoffOperation.Mining
                || !AutoDepthDesignation.TrySelectHandoffOperationFromEdge(
                    new[] { 0, 1, 1, 1, 0 },
                    out AccessHandoffOperation edgeDumping)
                || edgeDumping != AccessHandoffOperation.Dumping
                || !AutoDepthDesignation.TrySelectHandoffOperationFromEdge(
                    new[] { 0, 0, 0, 0, 0 },
                    out AccessHandoffOperation levelEdgeDumping)
                || levelEdgeDumping != AccessHandoffOperation.Dumping
                || AutoDepthDesignation.TrySelectHandoffOperationFromEdge(
                    new[] { -1, -1, 0, 1, 1 }, out _))
            { failure = "handoff operation must come only from a single-operation ground-facing edge"; return false; }
            if (!AutoDepthDesignation.IsHandoffOperationCompatibleWithProfileSigns(
                    new[] { -1, 0, -1, 0 }, AccessHandoffOperation.Mining)
                || AutoDepthDesignation.IsHandoffOperationCompatibleWithProfileSigns(
                    new[] { -1, 0, 1, 0 }, AccessHandoffOperation.Mining)
                || !AutoDepthDesignation.IsHandoffOperationCompatibleWithProfileSigns(
                    new[] { 1, 0, 1, 0 }, AccessHandoffOperation.Dumping)
                || AutoDepthDesignation.IsHandoffOperationCompatibleWithProfileSigns(
                    new[] { 1, 0, -1, 0 }, AccessHandoffOperation.Dumping))
            { failure = "handoff proto must be able to create every work cell in the terminal profile"; return false; }
            if (AutoDepthDesignation.IsInteriorHandoffEdgeTile(0, 0, 0)
                || AutoDepthDesignation.IsInteriorHandoffEdgeTile(0, 4, 0)
                || !AutoDepthDesignation.IsInteriorHandoffEdgeTile(0, 2, 0)
                || AutoDepthDesignation.IsInteriorHandoffEdgeTile(0, 0, 2)
                || AutoDepthDesignation.IsInteriorHandoffEdgeTile(4, 0, 2)
                || !AutoDepthDesignation.IsInteriorHandoffEdgeTile(2, 0, 2))
            { failure = "handoff contact must cross the interior of an edge, not touch only a corner"; return false; }
            if (!AutoDepthDesignation.IsClearanceValidHandoffLane(1, 3)
                || !AutoDepthDesignation.IsClearanceValidHandoffLane(2, 3)
                || AutoDepthDesignation.IsClearanceValidHandoffLane(0, 3)
                || AutoDepthDesignation.IsClearanceValidHandoffLane(3, 3)
                || AutoDepthDesignation.IsClearanceValidHandoffLane(4, 3)
                || AutoDepthDesignation.IsClearanceValidHandoffLane(2, 5)
                || AutoDepthDesignation.IsClearanceValidHandoffLane(1, 5)
                || AutoDepthDesignation.IsClearanceValidHandoffLane(3, 5))
            { failure = "one-cell handoffs must reject mega vehicle clearance"; return false; }
            if (GetMaxHandoffSpanLength(1) != 2
                || GetMaxHandoffSpanLength(3) != 2
                || GetMaxHandoffSpanLength(5) != 3)
            { failure = "handoff span bound must be 1 + ceil(vehicle width / 4)"; return false; }
            if (GetRisingMode(new Tile2i(4, 0)) != AccessSearchMode.XPositive
                || GetRisingMode(new Tile2i(-4, 0)) != AccessSearchMode.XNegative
                || GetRisingMode(new Tile2i(0, 4)) != AccessSearchMode.YPositive
                || GetRisingMode(new Tile2i(0, -4)) != AccessSearchMode.YNegative)
            { failure = "forward terminal handoffs must extend flat or continue rising in the travel direction"; return false; }
            Tile2i spanFirstOrigin = new Tile2i(8, 8);
            Tile2i spanSecondOrigin = new Tile2i(12, 8);
            Tile2i spanEscapeTile = new Tile2i(9, 9);
            GeneratedPathHistory spanHistory = GeneratedPathHistory.Empty
                .WithGenerated(spanFirstOrigin, flat, Array.Empty<Tile2i>())
                .WithGenerated(spanSecondOrigin, flat, Array.Empty<Tile2i>());
            var spanCells = new[]
            {
                new AccessHandoffSpanCell(
                    spanFirstOrigin, flat, new Tile2i(4, 0)),
                new AccessHandoffSpanCell(
                    spanSecondOrigin, flat, new Tile2i(4, 0)),
            };
            var spanRays = new IReadOnlyList<Tile2i>[]
            {
                Array.Empty<Tile2i>(), Array.Empty<Tile2i>(),
            };
            if (!spanHistory.TryReplaceLatestGeneratedSpan(
                    spanCells, spanRays, new[] { spanEscapeTile },
                    out GeneratedPathHistory replacedSpanHistory)
                || replacedSpanHistory.IsGroundDisturbed(spanEscapeTile)
                || !replacedSpanHistory.IsGroundDisturbed(new Tile2i(10, 10)))
            { failure = "multi-cell handoff history must preserve an escape corridor across every reclassified cell"; return false; }
            AutoDepthDesignation.GetHandoffLaneCoordinates(
                3, 2, out int northEdgeX, out int northEdgeY,
                out int northInsideX, out int northInsideY);
            AutoDepthDesignation.GetHandoffLaneCoordinates(
                1, 2, out int eastEdgeX, out int eastEdgeY,
                out int eastInsideX, out int eastInsideY);
            if (northEdgeX != 2 || northEdgeY != 4
                || northInsideX != 2 || northInsideY != 3
                || eastEdgeX != 4 || eastEdgeY != 2
                || eastInsideX != 3 || eastInsideY != 2)
            { failure = "+X/+Y handoffs must compare ground at the boundary while entering from the inside footprint tile"; return false; }
            Tile2i laneOrigin = new Tile2i(8, 8);
            if (!IsV1HandoffLaneEligible(laneOrigin, new Tile2i(7, 9), new Tile2i(4, 0))
                || !IsV1HandoffLaneEligible(laneOrigin, new Tile2i(12, 10), new Tile2i(-4, 0))
                || IsV1HandoffLaneEligible(laneOrigin, new Tile2i(7, 8), new Tile2i(4, 0))
                || IsV1HandoffLaneEligible(laneOrigin, new Tile2i(12, 11), new Tile2i(-4, 0))
                || !IsV1HandoffLaneEligible(laneOrigin, new Tile2i(9, 7), new Tile2i(0, 4))
                || !IsV1HandoffLaneEligible(laneOrigin, new Tile2i(10, 12), new Tile2i(0, -4))
                || IsV1HandoffLaneEligible(laneOrigin, new Tile2i(8, 7), new Tile2i(0, 4))
                || IsV1HandoffLaneEligible(laneOrigin, new Tile2i(11, 12), new Tile2i(0, -4)))
            { failure = "V1 handoffs must use only middle lanes 1 and 2"; return false; }
            var mixedCleanup = AccessPropCleanupPolicy.BuildOriginInfo(
                new Tile2i(0, 0),
                new[]
                {
                    new AccessPropSample(new Tile2i(0, 0), true, false, true),
                    new AccessPropSample(new Tile2i(1, 0), false, true, true),
                });
            if (!mixedCleanup.IsEligible || !mixedCleanup.HasTreeCleanup || !mixedCleanup.HasDenseDebrisCleanup)
            { failure = "prop cleanup helper must preserve mixed tree and dense-debris classes"; return false; }
            if (AccessPropCleanupPolicy.GetCleanupLandscapingCost(isTree: true) != 0f
                || AccessPropCleanupPolicy.GetCleanupLandscapingCost(isTree: false)
                    != AutoTerrainDesignationsMod.AccessPropCleanupLandscapingCost)
            { failure = "trees must remain cost-free while dense prop cleanup retains its configured route cost"; return false; }
            AccessPropCleanupInfo treeOne = AccessPropCleanupPolicy.BuildOriginInfo(
                new Tile2i(1, 0), new[]
                {
                    new AccessPropSample(new Tile2i(1, 0), true, false, true),
                });
            AccessPropCleanupInfo treeTwo = AccessPropCleanupPolicy.BuildOriginInfo(
                new Tile2i(2, 0), new[]
                {
                    new AccessPropSample(new Tile2i(2, 0), true, false, true),
                });
            var groundGoalDistance = new AccessV1GroundGoalDistance(
                new[] { Tile2i.Zero, new Tile2i(3, 0), new Tile2i(10, 10) },
                new Dictionary<Tile2i, AccessPropCleanupInfo>
                {
                    [treeOne.Origin] = treeOne,
                    [treeTwo.Origin] = treeTwo,
                },
                new[] { new Tile2i(3, 0) });
            if (!groundGoalDistance.TryGetDistance(Tile2i.Zero, out float treeCorridorDistance)
                || Math.Abs(treeCorridorDistance - 3f) > 0.0001f
                || groundGoalDistance.TryGetDistance(new Tile2i(10, 10), out _))
            { failure = "V1 ground potential must follow tree cleanup corridors without crossing disconnected G"; return false; }
            AccessPropCleanupInfo durabilityCleanup =
                AccessPropCleanupPolicy.BuildOriginInfo(
                    new Tile2i(0, 0),
                    new[] { new AccessPropSample(
                        new Tile2i(0, 0), false, true, true) },
                    AccessPropBlockerKind.Durability);
            if (durabilityCleanup.IsEligible
                || !durabilityCleanup.IsEligibleWithinGeneratedV
                || !durabilityCleanup.HasDenseDebrisCleanup
                || durabilityCleanup.Samples.Count != 1)
            { failure = "durability-blocked cleanup must retain removable samples for an exact V profile without becoming G-eligible"; return false; }
            AccessPropCleanupInfo terrainBlockedCleanup =
                AccessPropCleanupPolicy.BuildOriginInfo(
                    new Tile2i(0, 0),
                    new[] { new AccessPropSample(
                        new Tile2i(0, 0), false, true, true) },
                    AccessPropBlockerKind.UnderlyingTerrain);
            if (terrainBlockedCleanup.IsEligible
                || !terrainBlockedCleanup.IsEligibleWithinGeneratedV
                || !terrainBlockedCleanup.HasDenseDebrisCleanup)
            { failure = "terrain-blocked cleanup must remain unavailable to G while retaining removable samples for generated V terrain repair"; return false; }
            var hardCleanup = AccessPropCleanupPolicy.BuildOriginInfo(
                new Tile2i(0, 0),
                new[] { new AccessPropSample(new Tile2i(0, 0), false, true, false) });
            if (hardCleanup.IsEligible || hardCleanup.BlockerKind != AccessPropBlockerKind.HardBlocker)
            { failure = "non-removable prop sample must classify as a hard blocker"; return false; }
            if (!AccessPropCleanupPolicy.OperationRemovesNonTreeProp(AccessHandoffOperation.Mining, 4, 4)
                || !AccessPropCleanupPolicy.OperationRemovesNonTreeProp(AccessHandoffOperation.Leveling, 4, 4)
                || !AccessPropCleanupPolicy.OperationRemovesNonTreeProp(AccessHandoffOperation.Dumping, 2, 4)
                || AccessPropCleanupPolicy.OperationRemovesNonTreeProp(AccessHandoffOperation.Dumping, 2, 3)
                || !AccessPropCleanupPolicy.DoesTerrainDeltaDestroyTree(AccessHandoffOperation.Mining, 4, 3)
                || AccessPropCleanupPolicy.DoesTerrainDeltaDestroyTree(AccessHandoffOperation.Dumping, 2, 3))
            { failure = "prop terrain-removal policy helper failed"; return false; }
            for (int fixedCoordinate = -5; fixedCoordinate <= 5; fixedCoordinate++)
            {
                for (int start = -5; start <= 5; start++)
                {
                    bool horizontalRetained = false;
                    bool verticalRetained = false;
                    for (int offset = 0; offset <= 4; offset++)
                    {
                        horizontalRetained |= AccessSearchSnapshot.IsDiagonalGoalTile(
                            new Tile2i(start + offset, fixedCoordinate));
                        verticalRetained |= AccessSearchSnapshot.IsDiagonalGoalTile(
                            new Tile2i(fixedCoordinate, start + offset));
                    }
                    if (!horizontalRetained || !verticalRetained)
                    { failure = "every five-tile V/G handoff edge must retain a diagonal goal"; return false; }
                }
            }
            AccessHeightProfile.TryForMode(AccessSearchMode.YNegative, 1, out AccessHeightProfile edgeContactSlope);
            Tile2i handoffOrigin = new Tile2i(12, 12);
            Tile2i handoffCenter = new Tile2i(14, 14);
            Tile2i handoffSw = new Tile2i(12, 16);
            Tile2i handoffSe = new Tile2i(16, 16);
            var handoffFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -4, 4, true, true, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                new[] { handoffCenter, handoffSw, handoffSe },
                new[] { handoffSw, handoffSe },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>());
            if (ContainsHandoffTile(handoffFixture, handoffOrigin, edgeContactSlope, handoffCenter)
                || !ContainsHandoffTile(handoffFixture, handoffOrigin, edgeContactSlope, handoffSw)
                || !ContainsHandoffTile(handoffFixture, handoffOrigin, edgeContactSlope, handoffSe))
            { failure = "V/G handoff must use matching edge contacts, not a mismatched center"; return false; }
            var oceanPreciseHeights = new Dictionary<Tile2i, float>();
            foreach (KeyValuePair<Tile2i, int> pair in groundHeights)
                oceanPreciseHeights[pair.Key] = pair.Value / 2f;
            oceanPreciseHeights[new Tile2i(14, 14)] = 1f;
            var oceanFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -4, 4, true, false, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                new[] { new Tile2i(14, 14) },
                Array.Empty<AccessDurabilityCorner>(),
                preciseTerrainHeights: oceanPreciseHeights);
            if (oceanFixture.IsCandidateProfileFeasible(new Tile2i(12, 12), flat, out string oceanMismatch)
                || oceanMismatch != "OceanBelowMinimum")
            { failure = "V profiles below height 1 must not visit ocean"; return false; }
            var allowedOceanFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                new[] { new Tile2i(14, 14) },
                Array.Empty<AccessDurabilityCorner>(),
                preciseTerrainHeights: oceanPreciseHeights,
                avoidOcean: false);
            if (!allowedOceanFixture.IsCandidateProfileFeasible(
                new Tile2i(12, 12), flat, out string allowedOceanMismatch))
            { failure = "disabling ocean avoidance must permit V profiles to overlap the ocean envelope: " + allowedOceanMismatch; return false; }
            var dumpingFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -4, 4, false, false, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>());
            AccessHeightProfile.TryForMode(AccessSearchMode.Flat, -2, out AccessHeightProfile loweredFlat);
            if (dumpingFixture.IsCandidateProfileFeasible(new Tile2i(12, 12), loweredFlat, out string dumpingMismatch)
                || dumpingMismatch != "RequiresMining")
            { failure = "dumping candidate must reject profiles that require mining"; return false; }
            float baselineGeneratedEntryCost = CalculateGeneratedEntryCost(
                fixture, new Tile2i(12, 12), raisedFlat,
                Tile2i.Zero, AccessHandoffOperation.Leveling,
                out AccessLandscapingCost baselineLandscapingCost,
                out float baselineFixedCost, out _);
            float miningOnlyInterior = EstimateDirectWorkCost(
                fixture, new Tile2i(12, 12), raisedFlat,
                AccessHandoffOperation.Mining);
            float dumpingOnlyInterior = EstimateDirectWorkCost(
                fixture, new Tile2i(12, 12), raisedFlat,
                AccessHandoffOperation.Dumping);
            if (Math.Abs(baselineLandscapingCost.DirectWorkCost - 16f) > 0.0001f
                || miningOnlyInterior != 0f
                || Math.Abs(dumpingOnlyInterior - 16f) > 0.0001f
                || baselineLandscapingCost.LeftSideRayCost != 0f
                || baselineLandscapingCost.RightSideRayCost != 0f
                || baselineLandscapingCost.UnresolvedPenalty != 0f
                || baselineLandscapingCost.IsFatal
                || Math.Abs(baselineFixedCost - GeneratedVFixedOverhead) > 0.0001f
                || Math.Abs(baselineGeneratedEntryCost
                    - (DirectWorkWeight * 16f * fixture.LandscapingCostDistanceScale
                        + GeneratedVFixedOverhead)) > 0.0001f)
            { failure = "generated interior cost must preserve flat-cell normalization and respect mining/dumping direction"; return false; }
            Tile2i preciseTerrainTile = new Tile2i(2, 2);
            Tile2i preciseOceanTile = new Tile2i(2, 3);
            var raySnapshotFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -4, 8, true, true, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                new[] { preciseOceanTile },
                Array.Empty<AccessDurabilityCorner>(),
                null,
                null,
                new Dictionary<Tile2i, float>
                {
                    [preciseTerrainTile] = 0.375f,
                    [preciseOceanTile] = -2.25f,
                },
                new Dictionary<Tile2i, AccessTerrainColumn>
                {
                    [preciseTerrainTile] = new AccessTerrainColumn(new[]
                    {
                        new AccessTerrainLayer(4f, 2f, 0.5f, "Topsoil"),
                        new AccessTerrainLayer(2f, -20f, 1.5f, "Rock"),
                    }),
                },
                new Tile2i(0, 0),
                new Tile2i(20, 20),
                0.4f,
                0.3f,
                false,
                false);
            if (raySnapshotFixture.GetSideRayTerrainSample(
                    new Tile2i(-1, 2), out _) != AccessTerrainSampleKind.PhysicalMapEdge
                || raySnapshotFixture.GetSideRayTerrainSample(
                    new Tile2i(3, 3), out _) != AccessTerrainSampleKind.MissingSnapshot
                || raySnapshotFixture.GetSideRayTerrainSample(
                    preciseOceanTile, out float oceanHeight) != AccessTerrainSampleKind.Ocean
                || Math.Abs(oceanHeight - -2.25f) > 0.0001f
                || raySnapshotFixture.GetSideRayTerrainSample(
                    preciseTerrainTile, out float preciseHeight) != AccessTerrainSampleKind.Terrain
                || Math.Abs(preciseHeight - 0.375f) > 0.0001f)
            { failure = "side-ray snapshot must distinguish physical edge, missing capture, ocean, and precise terrain"; return false; }
            if (!raySnapshotFixture.TryGetMiningMaterialSlope(
                    preciseTerrainTile, 1f,
                    out float deepSlope, out string deepMaterial, out bool deepFallback)
                || deepFallback
                || Math.Abs(deepSlope - 1.5f) > 0.0001f
                || deepMaterial != "Rock"
                || !raySnapshotFixture.TryGetMiningMaterialSlope(
                    preciseTerrainTile, 3f,
                    out float surfaceSlope, out string surfaceMaterial, out bool surfaceFallback)
                || surfaceFallback
                || Math.Abs(surfaceSlope - 0.5f) > 0.0001f
                || surfaceMaterial != "Topsoil"
                || !raySnapshotFixture.TryGetMiningMaterialSlope(
                    new Tile2i(4, 4), 0f,
                    out float fallbackSlope, out _, out bool usedFallback)
                || !usedFallback
                || Math.Abs(fallbackSlope - 0.3f) > 0.0001f
                || Math.Abs(raySnapshotFixture.DumpingMaterialSlope - 0.4f) > 0.0001f
                || raySnapshotFixture.DumpingSlopeUsedFallback)
            { failure = "side-ray snapshot must select cut material at planned depth and preserve resolved/fallback slopes"; return false; }
            AccessSideRayResult noDumpingMaterial = ScoreExitCorner(
                raySnapshotFixture,
                preciseTerrainTile,
                1f,
                new Tile2i(1, 0),
                AccessHandoffOperation.Leveling);
            if (raySnapshotFixture.HasDumpingMaterial
                || noDumpingMaterial.FatalReason != "SideRayNoDumpingMaterial")
            { failure = "an explicit empty dumping rule set must reject fill, including leveling fill"; return false; }
            AccessSideRayResult noOpRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 0f),
                Tile2i.Zero, new Tile2i(1, 0), 0f,
                AccessSideRayOperation.None, 1f);
            AccessSideRayResult zeroLengthBufferedRay =
                AccessSideRayCost.ScoreZeroLengthBuffer(
                    _ => new AccessSideRayTerrainSample(
                        AccessTerrainSampleKind.Terrain, 0f),
                    Tile2i.Zero, new Tile2i(1, 0), 0f,
                    AccessSideRayOperation.Cut, 2);
            AccessSideRayResult zeroLengthOceanRay =
                AccessSideRayCost.ScoreZeroLengthBuffer(
                    _ => new AccessSideRayTerrainSample(
                        AccessTerrainSampleKind.Ocean, 0f),
                    Tile2i.Zero, new Tile2i(1, 0), 0f,
                    AccessSideRayOperation.Cut, 2);
            AccessSideRayResult zeroLengthBuildingBuffer =
                AccessSideRayCost.ScoreZeroLengthBuffer(
                    _ => new AccessSideRayTerrainSample(
                        AccessTerrainSampleKind.Terrain, 0f,
                        "SideRayBuilding"),
                    Tile2i.Zero, new Tile2i(1, 0), 0f,
                    AccessSideRayOperation.Cut, 2);
            AccessSideRayResult resolvedFillRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 0f),
                Tile2i.Zero, new Tile2i(1, 0), 4f,
                AccessSideRayOperation.Fill, 1f);
            AccessSideRayResult resolvedCutRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 4f),
                Tile2i.Zero, new Tile2i(1, 0), 0f,
                AccessSideRayOperation.Cut, 1f);
            AccessSideRayResult stepOneCutWithBufferedOcean =
                AccessSideRayCost.Score(
                    tile => tile.X == 3
                        ? new AccessSideRayTerrainSample(
                            AccessTerrainSampleKind.Ocean, 0f)
                        : new AccessSideRayTerrainSample(
                            AccessTerrainSampleKind.Terrain, 0f),
                    Tile2i.Zero, new Tile2i(1, 0), -1f,
                    AccessSideRayOperation.Cut, 1f,
                    postTerminationSafetyMargin: 2);
            AccessSideRayResult lowTerrainCutRay =
                AccessSideRayCost.Score(
                    _ => new AccessSideRayTerrainSample(
                        AccessTerrainSampleKind.Terrain, 0f),
                    Tile2i.Zero, new Tile2i(1, 0), -1f,
                    AccessSideRayOperation.Cut, 1f);
            AccessSideRayResult activeBuildingRay =
                AccessSideRayCost.Score(
                    _ => new AccessSideRayTerrainSample(
                        AccessTerrainSampleKind.Terrain, 4f,
                        "SideRayBuilding"),
                    Tile2i.Zero, new Tile2i(1, 0), 0f,
                    AccessSideRayOperation.Cut, 1f);
            AccessSideRayResult bufferedBuildingRay =
                AccessSideRayCost.Score(
                    tile => tile.X == 1
                        ? new AccessSideRayTerrainSample(
                            AccessTerrainSampleKind.Terrain, 0f)
                        : new AccessSideRayTerrainSample(
                            AccessTerrainSampleKind.Terrain, 0f,
                            "SideRayBuilding"),
                    Tile2i.Zero, new Tile2i(1, 0), 1f,
                    AccessSideRayOperation.Fill, 1f,
                    postTerminationSafetyMargin: 2);
            if (noOpRay.TotalCost != 0f || noOpRay.SampleCount != 0
                || noOpRay.IsFatal || noOpRay.IsUnresolved
                || zeroLengthBufferedRay.TotalCost != 0f
                || zeroLengthBufferedRay.SampleCount != 2
                || zeroLengthBufferedRay.DisturbedDistance != 2
                || zeroLengthBufferedRay.IsFatal
                || zeroLengthOceanRay.FatalReason != "SideRayCutOcean"
                || zeroLengthBuildingBuffer.IsFatal
                || zeroLengthBuildingBuffer.DisturbedDistance != 2
                || Math.Abs(resolvedFillRay.TotalCost - 6f) > 0.0001f
                || resolvedFillRay.SampleCount != 4
                || resolvedFillRay.IsFatal || resolvedFillRay.IsUnresolved
                || Math.Abs(resolvedCutRay.TotalCost - 6f) > 0.0001f
                || resolvedCutRay.SampleCount != 4
                || resolvedCutRay.IsFatal || resolvedCutRay.IsUnresolved
                || stepOneCutWithBufferedOcean.FatalReason != "SideRayCutOcean"
                || lowTerrainCutRay.IsFatal || lowTerrainCutRay.IsUnresolved
                || lowTerrainCutRay.SampleCount != 2
                || lowTerrainCutRay.DisturbedDistance != 2
                || activeBuildingRay.FatalReason != "SideRayBuilding"
                || bufferedBuildingRay.IsFatal
                || bufferedBuildingRay.DisturbedDistance != 3)
            { failure = "side-ray integrator must preserve no-op costs, protect zero-length termination buffers, and preserve resolved fill/cut rectangle costs"; return false; }

            AccessSideRayResult unresolvedRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 0f),
                Tile2i.Zero, new Tile2i(1, 0), 20f,
                AccessSideRayOperation.Fill, 0.1f,
                maxRayCost: 1000f,
                unresolvedPenalty: 7f);
            AccessSideRayResult cappedRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 0f),
                Tile2i.Zero, new Tile2i(1, 0), 100f,
                AccessSideRayOperation.Fill, 0.1f,
                maxRayCost: 10f,
                unresolvedPenalty: 7f);
            if (!unresolvedRay.IsUnresolved
                || unresolvedRay.ReachedCostCap
                || unresolvedRay.SampleCount != 16
                || Math.Abs(unresolvedRay.UnresolvedPenalty - 7f) > 0.0001f
                || !cappedRay.IsUnresolved
                || !cappedRay.ReachedCostCap
                || Math.Abs(cappedRay.TotalCost - 10f) > 0.0001f)
            { failure = "side-ray integrator must apply finite unresolved penalties and per-ray caps"; return false; }

            AccessSideRayResult fillMapEdgeRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.PhysicalMapEdge, 0f),
                Tile2i.Zero, new Tile2i(1, 0), 4f,
                AccessSideRayOperation.Fill, 1f);
            AccessSideRayResult cutMapEdgeRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.PhysicalMapEdge, 0f),
                Tile2i.Zero, new Tile2i(1, 0), 0f,
                AccessSideRayOperation.Cut, 1f);
            AccessSideRayResult missingRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.MissingSnapshot, 0f),
                Tile2i.Zero, new Tile2i(1, 0), 4f,
                AccessSideRayOperation.Fill, 1f);
            if (fillMapEdgeRay.FatalReason != "SideRayFillMapEdge"
                || cutMapEdgeRay.IsFatal
                || cutMapEdgeRay.TotalCost != 0f
                || missingRay.FatalReason != "SideRaySnapshotMissing")
            { failure = "side-ray integrator must distinguish fill/cut map boundaries and missing snapshot data"; return false; }

            AccessSideRayResult fillOceanRay = AccessSideRayCost.Score(
                tile => tile.X < 3
                    ? new AccessSideRayTerrainSample(AccessTerrainSampleKind.Ocean, 0f)
                    : new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 2f),
                Tile2i.Zero, new Tile2i(1, 0), 4f,
                AccessSideRayOperation.Fill, 1f);
            AccessSideRayResult allowedFillOceanRay = AccessSideRayCost.Score(
                tile => tile.X < 3
                    ? new AccessSideRayTerrainSample(AccessTerrainSampleKind.Ocean, 0f)
                    : new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 2f),
                Tile2i.Zero, new Tile2i(1, 0), 4f,
                AccessSideRayOperation.Fill, 1f,
                avoidOcean: false);
            AccessSideRayResult zeroWorkOceanRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Ocean, 3f),
                Tile2i.Zero, new Tile2i(1, 0), 4f,
                AccessSideRayOperation.Fill, 1f);
            AccessSideRayResult cutOceanRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Ocean, 4f),
                Tile2i.Zero, new Tile2i(1, 0), -1f,
                AccessSideRayOperation.Cut, 1f);
            AccessSideRayResult skippedDistanceOceanRay = AccessSideRayCost.Score(
                tile => tile.X == 4
                    ? new AccessSideRayTerrainSample(AccessTerrainSampleKind.Ocean, 4f)
                    : new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 4f),
                Tile2i.Zero, new Tile2i(1, 0), 0f,
                AccessSideRayOperation.Cut, 0.1f);
            AccessSideRayResult postTerminationOceanRay = AccessSideRayCost.Score(
                tile => tile.X == 4
                    ? new AccessSideRayTerrainSample(AccessTerrainSampleKind.Ocean, -2f)
                    : tile.X == 3
                        ? new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, -1f)
                        : new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 3f),
                Tile2i.Zero, new Tile2i(1, 0), -4f,
                AccessSideRayOperation.Cut, 1f,
                postTerminationSafetyMargin: 1);
            AccessSideRayResult dryOceanCutRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Ocean, 4f),
                Tile2i.Zero, new Tile2i(1, 0), 0f,
                AccessSideRayOperation.Cut, 1f);
            if (fillOceanRay.IsFatal
                || fillOceanRay.IsUnresolved
                || Math.Abs(fillOceanRay.TotalCost - 5f) > 0.0001f
                || allowedFillOceanRay.IsFatal
                || allowedFillOceanRay.IsUnresolved
                || Math.Abs(allowedFillOceanRay.TotalCost - 5f) > 0.0001f
                || zeroWorkOceanRay.IsFatal
                || zeroWorkOceanRay.TotalCost != 0f
                || cutOceanRay.FatalReason != "SideRayCutOcean"
                || skippedDistanceOceanRay.FatalReason != "SideRayCutOcean"
                || postTerminationOceanRay.FatalReason != "SideRayCutOcean"
                || dryOceanCutRay.IsFatal)
            { failure = "side-ray integrator must allow ocean fill and dry cuts, reject cuts below sea level including safety samples, permit zero-work termination, and trace cuts through ocean when disabled"; return false; }

            var directionalHeights = new Dictionary<Tile2i, float>();
            for (int x = -20; x <= 40; x++)
                for (int y = -20; y <= 40; y++)
                    directionalHeights[new Tile2i(x, y)] = 0f;
            int[] rayDistances = { 1, 2, 3, 5, 8, 13, 16 };
            foreach (int distance in rayDistances)
            {
                directionalHeights[new Tile2i(8 - distance, 12)] = 4f;
                directionalHeights[new Tile2i(12 + distance, 12)] = 4f;
            }
            directionalHeights[new Tile2i(8, 12)] = 0f;
            directionalHeights[new Tile2i(12, 12)] = 0f;
            var directionalSnapshot = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(24, 24), new Tile2i(20, 20),
                -10, 10, true, true, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>(),
                null,
                null,
                directionalHeights,
                null,
                new Tile2i(-20, -20),
                new Tile2i(40, 40),
                1f,
                1f,
                false);
            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, 8, out AccessHeightProfile highFlat);
            AccessHeightProfile.TryForMode(
                AccessSearchMode.Flat, -8, out AccessHeightProfile lowFlat);
            float alongXCost = CalculateGeneratedEntryCost(
                directionalSnapshot, new Tile2i(8, 8), highFlat,
                new Tile2i(4, 0), AccessHandoffOperation.Leveling,
                out AccessLandscapingCost alongX, out _, out string alongXRejection);
            float alongYCost = CalculateGeneratedEntryCost(
                directionalSnapshot, new Tile2i(8, 8), highFlat,
                new Tile2i(0, 4), AccessHandoffOperation.Leveling,
                out AccessLandscapingCost alongY, out _, out string alongYRejection);
            CalculateGeneratedEntryCost(
                directionalSnapshot, new Tile2i(8, 8), highFlat,
                new Tile2i(4, 0), AccessHandoffOperation.Mining,
                out AccessLandscapingCost miningIgnoresFill, out _, out _);
            CalculateGeneratedEntryCost(
                directionalSnapshot, new Tile2i(8, 8), lowFlat,
                new Tile2i(4, 0), AccessHandoffOperation.Dumping,
                out AccessLandscapingCost dumpingIgnoresCut, out _, out _);
            CalculateGeneratedEntryCost(
                directionalSnapshot, new Tile2i(8, 8), lowFlat,
                new Tile2i(4, 0), AccessHandoffOperation.Mining,
                out AccessLandscapingCost miningScoresCut, out _, out string miningCutRejection);
            if (!string.IsNullOrEmpty(alongXRejection)
                || !string.IsNullOrEmpty(alongYRejection)
                || !string.IsNullOrEmpty(miningCutRejection)
                || alongX.LeftSideRayCost <= 0f
                || alongX.RightSideRayCost <= 0f
                || Math.Abs(alongX.LeftSideRayCost - alongX.RightSideRayCost) > 0.0001f
                || alongY.LeftSideRayCost != 0f
                || alongY.RightSideRayCost != 0f
                || (SideRayWeight > 0f && alongXCost <= alongYCost)
                || miningIgnoresFill.LeftSideRayCost != 0f
                || miningIgnoresFill.RightSideRayCost != 0f
                || dumpingIgnoresCut.LeftSideRayCost != 0f
                || dumpingIgnoresCut.RightSideRayCost != 0f
                || miningScoresCut.LeftSideRayCost <= 0f
                || miningScoresCut.RightSideRayCost <= 0f
                || Math.Abs(miningScoresCut.LeftSideRayCost
                    - miningScoresCut.RightSideRayCost) > 0.0001f)
            { failure = "generated entry cost must be direction-aware and filter fill/cut by designation operation"; return false; }
            var directionStateX = new AccessSearchNode(
                new Tile2i(8, 8), 8, AccessSearchMode.Flat,
                entryDirection: new Tile2i(4, 0));
            var directionStateY = new AccessSearchNode(
                new Tile2i(8, 8), 8, AccessSearchMode.Flat,
                entryDirection: new Tile2i(0, 4));
            if (directionStateX.Equals(directionStateY))
            { failure = "generated search state must retain entry direction for direction-dependent cost"; return false; }
            Tile2i historyOrigin = new Tile2i(8, 8);
            Tile2i historyRayTile = new Tile2i(14, 10);
            GeneratedPathHistory disturbanceHistory =
                GeneratedPathHistory.Empty.WithGenerated(
                    historyOrigin,
                    highFlat,
                    new[] { historyRayTile });
            if (IsCompatibleWithPathHistory(
                    historyOrigin, highFlat, disturbanceHistory, null,
                    out string identicalRevisitReason)
                || identicalRevisitReason != "PathSelfContact")
            { failure = "generated history must reject an identical origin/profile revisit before search expansion"; return false; }
            Tile2i adjacentHistoryOrigin = historyOrigin + new RelTile2i(4, 0);
            if (IsCompatibleWithPathHistory(
                    adjacentHistoryOrigin, highFlat, disturbanceHistory, null,
                    out string adjacentContactReason)
                || adjacentContactReason != "PathAdjacentSelfContact")
            { failure = "generated history must reject nonlocal edge contact before search expansion"; return false; }
            if (!IsCompatibleWithPathHistory(
                    adjacentHistoryOrigin, highFlat, disturbanceHistory, historyOrigin,
                    out _))
            { failure = "generated history must allow the immediate predecessor edge"; return false; }
            if (!IsCompatibleWithPathHistory(
                    historyOrigin + new RelTile2i(4, 4), highFlat,
                    disturbanceHistory, null, out _))
            { failure = "generated history must allow diagonal corner contact"; return false; }
            if (!disturbanceHistory.IsGroundDisturbed(new Tile2i(10, 10))
                || !disturbanceHistory.IsGroundDisturbed(historyRayTile)
                || disturbanceHistory.IsGroundDisturbed(
                    new Tile2i(12, 10), historyOrigin)
                || !disturbanceHistory.IsGroundDisturbed(
                    historyRayTile, historyOrigin)
                || disturbanceHistory.IsGroundDisturbed(new Tile2i(20, 20)))
            { failure = "generated history must block V footprints and ray wedges while exempting only the current V footprint at handoff"; return false; }
            Tile2i rayEnvelopeTile = new Tile2i(18, 10);
            GeneratedPathHistory cutEnvelopeHistory =
                GeneratedPathHistory.Empty.WithGenerated(
                    historyOrigin, highFlat, Array.Empty<Tile2i>(),
                    rayHeightConstraints: new[]
                    {
                        new AccessRayHeightConstraint(
                            rayEnvelopeTile, AccessSideRayOperation.Cut, 1f),
                    });
            GeneratedPathHistory fillEnvelopeHistory =
                GeneratedPathHistory.Empty.WithGenerated(
                    historyOrigin, highFlat, Array.Empty<Tile2i>(),
                    rayHeightConstraints: new[]
                    {
                        new AccessRayHeightConstraint(
                            rayEnvelopeTile, AccessSideRayOperation.Fill, 1f),
                    });
            if (!cutEnvelopeHistory.IsProfileBlockedByRayEnvelope(
                    new Tile2i(16, 8), highFlat, out AccessSideRayOperation cutBlock)
                || cutBlock != AccessSideRayOperation.Cut
                || cutEnvelopeHistory.IsProfileBlockedByRayEnvelope(
                    new Tile2i(16, 8), lowFlat, out _)
                || !fillEnvelopeHistory.IsProfileBlockedByRayEnvelope(
                    new Tile2i(16, 8), lowFlat, out AccessSideRayOperation fillBlock)
                || fillBlock != AccessSideRayOperation.Fill)
            { failure = "generated cut rays must block V profiles above their ceiling and fill rays must block profiles below their floor"; return false; }
            if (GetVehicleDisturbanceRadius(3) != 1
                || GetVehicleDisturbanceRadius(5) != 2)
            { failure = "vehicle disturbance radius must map T1/T2 clearance to 1 and T3 clearance to 2"; return false; }
            IReadOnlyList<Tile2i> t3DisturbedTiles = BuildDisturbedRayTiles(
                new Tile2i(0, 0), new Tile2i(1, 0), 1,
                Tile2i.Zero, Tile2i.Zero, 0,
                GetVehicleDisturbanceRadius(5));
            var t3DisturbedTileSet = new HashSet<Tile2i>(t3DisturbedTiles);
            if (!t3DisturbedTileSet.Contains(new Tile2i(3, 0))
                || t3DisturbedTileSet.Contains(new Tile2i(4, 0)))
            { failure = "T3 disturbance rays must block a two-tile radius around each disturbed ray cell"; return false; }
            AccessSearchResult fixtureResult = FindPath(fixture, new[] { fixtureStart });
            if (!fixtureResult.Success || fixtureResult.Path.Count < 2
                || fixtureResult.Path[0].Position != fixtureWorkNeighbor
                || fixtureResult.Path[0].Mode != AccessSearchMode.Existing
                || fixtureResult.Path[fixtureResult.Path.Count - 1].Mode != AccessSearchMode.Ground)
            { failure = "synthetic work-origin traversal and V-to-G Dijkstra fixture failed"; return false; }
            var rootedRequest = new AccessPathRequest(
                "fixture-rooted-network",
                fixture,
                new AccessPathEndpoint(AccessPathEndpointKind.FixedProfiles, new[] { fixtureStart }),
                new AccessPathEndpoint(AccessPathEndpointKind.GroundTiles, fixture.GoalGroundNodes),
                1,
                AccessPathIntent.ConstructAccessway);
            AccessSearchResult requestResult = FindPath(rootedRequest);
            if (!requestResult.Success
                || requestResult.Path.Count != fixtureResult.Path.Count)
            { failure = "rooted access request adapter changed path-search behavior"; return false; }
            var pairedGoalIndex = HeightAwareGoalIndex.Build(
                fixture,
                new Dictionary<int, List<Tile2i>>
                {
                    [0] = new List<Tile2i> { new Tile2i(20, 0) },
                    [10] = new List<Tile2i> { new Tile2i(0, 0) },
                });
            if (Math.Abs(pairedGoalIndex.GetLowerBound(new Tile2i(0, 0), 0) - 10f) > 0.0001f)
            { failure = "height-aware heuristic must pair distance and height from the same goal"; return false; }
            var tieBreakQueue = new MinQueue();
            var highHeuristicNode = new AccessSearchNode(
                new Tile2i(4, 4), 0, AccessSearchMode.Ground);
            var lowHeuristicNode = new AccessSearchNode(
                new Tile2i(8, 8), 0, AccessSearchMode.Ground);
            tieBreakQueue.Push(new QueueEntry(
                highHeuristicNode, pathCost: 2f,
                priority: 10f, heuristic: 8f));
            tieBreakQueue.Push(new QueueEntry(
                lowHeuristicNode, pathCost: 8f,
                priority: 10f, heuristic: 2f));
            if (!tieBreakQueue.Pop().Node.Equals(lowHeuristicNode))
            { failure = "V1 A* equal-f queue entries must prefer lower remaining heuristic"; return false; }
            float[] diagonalGoalDistance = AccessSearchSnapshot.BuildGoalDistance(
                new Tile2i(0, 0), new Tile2i(2, 2),
                new HashSet<Tile2i> { new Tile2i(2, 2) });
            if (Math.Abs(diagonalGoalDistance[0]
                    - 2f * GroundDiagonalCost) > 0.0001f)
            { failure = "V1 A* ground heuristic must use octile distance"; return false; }
            Tile2i outsideAreaGround = new Tile2i(24, 18);
            var astarFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, true, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [fixtureStart] = flat,
                    [fixtureWorkNeighbor] = flat,
                },
                new[] { fixtureStart, fixtureWorkNeighbor },
                new[] { fixtureGoal, outsideAreaGround },
                new[] { fixtureGoal },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                new[] { new AccessDurabilityCorner(new Tile2i(16, 16), 0) });
            int fixtureGoalHeight2 = groundHeights[fixtureGoal];
            float outsideExpected = OctileDistance(outsideAreaGround, fixtureGoal);
            var outsideGoalIndex = HeightAwareGoalIndex.Build(
                astarFixture,
                new Dictionary<int, List<Tile2i>>
                {
                    [fixtureGoalHeight2] = new List<Tile2i> { fixtureGoal },
                });
            if (Math.Abs(astarFixture.GetGoalTravelLowerBound(
                    outsideAreaGround, fixtureGoalHeight2) - outsideExpected)
                    > 0.0001f
                || Math.Abs(outsideGoalIndex.GetLowerBound(
                    outsideAreaGround, fixtureGoalHeight2) - outsideExpected)
                    > 0.0001f)
            { failure = "V1 A* heuristic must cover captured G nodes outside the tower bounds"; return false; }
            var astarRequest = new AccessPathRequest(
                "fixture-height-aware-astar",
                astarFixture,
                rootedRequest.Start,
                new AccessPathEndpoint(
                    Array.Empty<Tile2i>(),
                    astarFixture.GoalGroundNodes),
                1,
                AccessPathIntent.ConstructAccessway);
            AccessSearchResult astarResult = FindPath(astarRequest);
            if (!astarResult.Success
                || Math.Abs(astarResult.Cost - requestResult.Cost) > 0.0001f
                || !CostBreakdownsMatch(astarResult, requestResult)
                || !CostBreakdownSumsToTotal(astarResult)
                || !CostBreakdownSumsToTotal(requestResult)
                || astarResult.Path.Count != requestResult.Path.Count)
            { failure = "height-aware A* must match Dijkstra fixture route, cost, and cost breakdown"; return false; }
            for (int i = 0; i < astarResult.Path.Count; i++)
            {
                if (!astarResult.Path[i].Equals(requestResult.Path[i]))
                { failure = "height-aware A* must reproduce the Dijkstra fixture path"; return false; }
            }
            Tile2i startHandoffGoal = fixtureStart + new RelTile2i(2, 2);
            var startHandoffFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile> { [fixtureStart] = flat },
                new[] { fixtureStart },
                new[] { startHandoffGoal },
                new[] { startHandoffGoal },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>());
            AccessSearchResult startHandoffResult = FindPath(startHandoffFixture, new[] { fixtureStart });
            AccessDesignationPlan startHandoffPlan = AccessPathMaterializer.Materialize(
                startHandoffFixture, startHandoffResult);
            if (!startHandoffResult.Success
                || startHandoffResult.Path.Count != 1
                || !startHandoffResult.Path[0].IsGround
                || !startHandoffPlan.IsValid
                || startHandoffPlan.Designations.Count != 0
                || startHandoffPlan.GroundNodeCount != 1)
            { failure = "start fixed profile must allow immediate V-to-G handoff"; return false; }
            var v2WidthRequest = new AccessPathRequest(
                "fixture-width-two",
                fixture,
                rootedRequest.Start,
                rootedRequest.Goal,
                2,
                AccessPathIntent.ConstructAccessway);
            AccessSearchResult v2WidthResult = FindPath(v2WidthRequest);
            if (v2WidthResult.Success
                || v2WidthResult.FailureReason != "V2FrontagesMissing")
            { failure = "Width-two request must dispatch to the V2 frontage boundary"; return false; }
            var unsupportedStartRequest = new AccessPathRequest(
                "fixture-ground-start",
                fixture,
                new AccessPathEndpoint(AccessPathEndpointKind.GroundTiles, new[] { fixtureGoal }),
                rootedRequest.Goal,
                1,
                AccessPathIntent.ConstructAccessway);
            AccessSearchResult unsupportedStartResult = FindPath(unsupportedStartRequest);
            if (unsupportedStartResult.Success
                || unsupportedStartResult.FailureReason != "UnsupportedStartEndpoint")
            { failure = "V1 rooted request must reject non-fixed-profile starts"; return false; }
            var fixedGoalRequest = new AccessPathRequest(
                "fixture-fixed-goal",
                fixture,
                rootedRequest.Start,
                new AccessPathEndpoint(AccessPathEndpointKind.FixedProfiles, new[] { fixtureWorkNeighbor }),
                1,
                AccessPathIntent.ConstructAccessway);
            AccessSearchResult fixedGoalResult = FindPath(fixedGoalRequest);
            AccessDesignationPlan fixedGoalPlan = AccessPathMaterializer.Materialize(fixture, fixedGoalResult);
            if (!fixedGoalResult.Success
                || !fixedGoalPlan.IsValid
                || fixedGoalPlan.Designations.Count != 0
                || fixedGoalPlan.ReusedNodeCount != 1)
            { failure = "V1 rooted request must support fixed-profile goals"; return false; }
            var mergedFixedWinnerRequest = new AccessPathRequest(
                "fixture-merged-fixed-winner",
                fixture,
                rootedRequest.Start,
                new AccessPathEndpoint(
                    new[] { fixtureWorkNeighbor },
                    fixture.GoalGroundNodes),
                1,
                AccessPathIntent.ConstructAccessway);
            AccessSearchResult mergedFixedWinner = FindPath(mergedFixedWinnerRequest);
            if (!mergedFixedWinner.Success
                || mergedFixedWinner.ReachedGoalKind != AccessReachedGoalKind.FixedNetwork)
            { failure = "merged goals must report a cheaper fixed-network terminal"; return false; }
            var mergedGroundWinnerRequest = new AccessPathRequest(
                "fixture-merged-ground-winner",
                fixture,
                rootedRequest.Start,
                new AccessPathEndpoint(
                    new[] { new Tile2i(16, 12) },
                    fixture.GoalGroundNodes),
                1,
                AccessPathIntent.ConstructAccessway);
            AccessSearchResult mergedGroundWinner = FindPath(mergedGroundWinnerRequest);
            if (!mergedGroundWinner.Success
                || mergedGroundWinner.ReachedGoalKind != AccessReachedGoalKind.TowerGround)
            { failure = "merged goals must retain reachable tower-ground terminals"; return false; }
            var unsupportedIntentRequest = new AccessPathRequest(
                "fixture-inspect-intent",
                fixture,
                rootedRequest.Start,
                rootedRequest.Goal,
                1,
                AccessPathIntent.InspectExistingRoute);
            AccessSearchResult unsupportedIntentResult = FindPath(unsupportedIntentRequest);
            if (unsupportedIntentResult.Success
                || unsupportedIntentResult.FailureReason != "UnsupportedIntent")
            { failure = "V1 rooted request must reject unsupported request intents"; return false; }
            AccessDesignationPlan reusedPlan = AccessPathMaterializer.Materialize(fixture, fixtureResult);
            if (!reusedPlan.IsValid || reusedPlan.Designations.Count != 0 || reusedPlan.ReusedNodeCount != 1)
            { failure = "synthetic reused-path materialization fixture failed"; return false; }

            Tile2i cleanupStart = new Tile2i(12, 4);
            Tile2i cleanupGoal = cleanupStart + new RelTile2i(2, 2);
            var cleanupFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile> { [cleanupStart] = flat },
                new[] { cleanupStart },
                new[] { fixtureGoal },
                new[] { cleanupGoal },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>(),
                propCleanupByOrigin: new Dictionary<Tile2i, AccessPropCleanupInfo>
                {
                    [cleanupStart] = new AccessPropCleanupInfo(cleanupStart,
                        AccessPropCleanupClass.DenseDebris, AccessPropBlockerKind.None, true),
                });
            if (!cleanupFixture.IsGroundOrCleanupNode(cleanupGoal)
                || !cleanupFixture.TryGetCleanupInfoForTile(cleanupGoal, out AccessPropCleanupInfo selectedCleanup)
                || !selectedCleanup.HasDenseDebrisCleanup)
            { failure = "snapshot cleanup overlay must admit eligible cleanup ground without ordinary G membership"; return false; }
            var cleanupResult = new AccessSearchResult(true, string.Empty, cleanupStart,
                new[]
                {
                    new AccessSearchNode(cleanupGoal, 0, AccessSearchMode.Ground),
                }, AccessPropCleanupPolicy.GetCleanupLandscapingCost() + 1f, 1,
                new Dictionary<string, int>());
            AccessDesignationPlan cleanupPlan = AccessPathMaterializer.Materialize(cleanupFixture, cleanupResult);
            if (!cleanupPlan.IsValid || cleanupPlan.CleanupOrigins.Count != 1
                || !cleanupPlan.CleanupOrigins[0].HasDenseDebrisCleanup)
            { failure = "cleanup ground metadata must materialize separately from generated V designations"; return false; }
            Tile2i cleanupGeneratedOrigin = cleanupStart + new RelTile2i(4, 0);
            Tile2i cleanupGeneratedGoal = cleanupGeneratedOrigin + new RelTile2i(2, 2);
            var cleanupGeneratedFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(24, 20), new Tile2i(22, 18),
                -2, 2, true, false, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile> { [cleanupStart] = flat },
                new[] { cleanupStart },
                new[] { fixtureGoal, cleanupGeneratedGoal },
                new[] { cleanupGeneratedGoal },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>(),
                propCleanupByOrigin: new Dictionary<Tile2i, AccessPropCleanupInfo>
                {
                    [cleanupGeneratedOrigin] = new AccessPropCleanupInfo(cleanupGeneratedOrigin,
                        AccessPropCleanupClass.DenseDebris, AccessPropBlockerKind.None, true),
                });
            var cleanupGeneratedResult = new AccessSearchResult(true, string.Empty, cleanupStart,
                new[]
                {
                    new AccessSearchNode(cleanupGeneratedOrigin, 0, AccessSearchMode.Flat),
                    new AccessSearchNode(cleanupGeneratedGoal, 0, AccessSearchMode.Ground),
                }, 2f, 2, new Dictionary<string, int>());
            AccessDesignationPlan cleanupGeneratedPlan =
                AccessPathMaterializer.Materialize(cleanupGeneratedFixture, cleanupGeneratedResult);
            if (!cleanupGeneratedPlan.IsValid
                || cleanupGeneratedPlan.Designations.Count != 0
                || cleanupGeneratedPlan.CleanupOrigins.Count != 1)
            { failure = "exact-terrain generated V over dense cleanup must omit no-op terrain work and materialize explicit cleanup"; return false; }

            Tile2i alternateGoal = fixtureGoal + new RelTile2i(1, 0);
            var goalContinuationFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [fixtureStart] = flat,
                    [fixtureWorkNeighbor] = flat,
                },
                new[] { fixtureStart, fixtureWorkNeighbor },
                new[] { fixtureGoal, alternateGoal },
                new[] { fixtureGoal, alternateGoal },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>());
            AccessSearchResult continuationResult = FindPath(
                goalContinuationFixture,
                new[] { fixtureStart },
                candidate => candidate.Path[candidate.Path.Count - 1].Position == fixtureGoal
                    ? "InjectedGoalRejection"
                    : null);
            if (!continuationResult.Success
                || continuationResult.Path[continuationResult.Path.Count - 1].Position != alternateGoal
                || !continuationResult.Rejections.ContainsKey("GoalInjectedGoalRejection"))
            { failure = "search must continue through a rejected goal to another valid goal"; return false; }

            Tile2i generatedOrigin = new Tile2i(4, 8);
            Tile2i generatedGoal = new Tile2i(6, 10);
            var generatedGroundHeights = new Dictionary<Tile2i, int>(groundHeights)
            {
                // Keep this as a genuine generated mining fixture. A flat
                // profile over perfectly flat terrain is now intentionally
                // omitted as a no-op during materialization.
                [generatedOrigin + new RelTile2i(2, 2)] = 2,
            };
            var generatedFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, false, 1f, 1f,
                generatedGroundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile> { [fixtureStart] = flat },
                new[] { fixtureStart },
                new[] { generatedGoal },
                new[] { generatedGoal },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>(),
                (origin, profile, predecessorOrigin, predecessorProfile) => new[]
                {
                    new AccessGroundHandoff(generatedGoal, AccessHandoffOperation.Mining),
                });
            var generatedResult = new AccessSearchResult(true, string.Empty, fixtureStart,
                new AccessSearchNode[]
                {
                    new AccessSearchNode(generatedOrigin, 0, AccessSearchMode.Flat),
                    new AccessSearchNode(generatedGoal, 0, AccessSearchMode.Ground,
                        AccessHandoffOperation.Mining),
                }, 6f, 2, new Dictionary<string, int>());
            AccessDesignationPlan generatedPlan = AccessPathMaterializer.Materialize(generatedFixture, generatedResult);
            if (!generatedPlan.IsValid || generatedPlan.Designations.Count != 1
                || generatedPlan.Designations[0].Origin != generatedOrigin
                || generatedPlan.Designations[0].Mode != AccessSearchMode.Flat
                || generatedPlan.HandoffOperation != AccessHandoffOperation.Mining)
            { failure = "synthetic generated-path materialization fixture failed"; return false; }

            Tile2i turnStart = new Tile2i(4, 12);
            var turnGroundHeights = new Dictionary<Tile2i, int>(groundHeights)
            {
                // Give every generated leg genuine cut work so this fixture
                // continues to exercise shared-corner materialization. Exact-
                // terrain legs are intentionally omitted as no-ops.
                [new Tile2i(5, 9)] = 2,
                [new Tile2i(5, 5)] = 2,
                [new Tile2i(9, 5)] = 2,
            };
            var turnFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, false, 1f, 1f,
                turnGroundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile> { [turnStart] = flat },
                new[] { turnStart },
                new[] { fixtureGoal },
                new[] { fixtureGoal },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>());
            var turnResult = new AccessSearchResult(true, string.Empty, turnStart,
                new AccessSearchNode[]
                {
                    new AccessSearchNode(new Tile2i(4, 8), 0, AccessSearchMode.Flat),
                    new AccessSearchNode(new Tile2i(4, 4), 0, AccessSearchMode.Flat),
                    new AccessSearchNode(new Tile2i(8, 4), 0, AccessSearchMode.Flat),
                    new AccessSearchNode(fixtureGoal, 0, AccessSearchMode.Ground),
                }, 14f, 4, new Dictionary<string, int>());
            AccessDesignationPlan turnPlan = AccessPathMaterializer.Materialize(turnFixture, turnResult);
            if (!turnPlan.IsValid || turnPlan.Designations.Count != 3)
            { failure = "legal diagonal self-contact at flat turn should materialize"; return false; }

            var repeatedOriginPath = new AccessSearchNode[]
            {
                new AccessSearchNode(new Tile2i(4, 8), 0, AccessSearchMode.Flat),
                new AccessSearchNode(new Tile2i(4, 4), 0, AccessSearchMode.Flat),
                new AccessSearchNode(new Tile2i(4, 8), 0, AccessSearchMode.Flat),
                new AccessSearchNode(fixtureGoal, 0, AccessSearchMode.Ground),
            };
            if (ValidateGeneratedPath(repeatedOriginPath, turnFixture, out string repeatedOriginReason)
                || !repeatedOriginReason.StartsWith("FinalOriginRevisit@", StringComparison.Ordinal))
            { failure = "a V path must never revisit an origin, even with the same profile"; return false; }
            var adjacentSelfContactPath = new AccessSearchNode[]
            {
                new AccessSearchNode(new Tile2i(4, 8), 0, AccessSearchMode.Flat),
                new AccessSearchNode(new Tile2i(4, 4), 0, AccessSearchMode.Flat),
                new AccessSearchNode(new Tile2i(8, 4), 0, AccessSearchMode.Flat),
                new AccessSearchNode(new Tile2i(8, 8), 0, AccessSearchMode.Flat),
                new AccessSearchNode(fixtureGoal, 0, AccessSearchMode.Ground),
            };
            if (ValidateGeneratedPath(
                    adjacentSelfContactPath, turnFixture,
                    out string adjacentSelfContactReason)
                || !adjacentSelfContactReason.StartsWith(
                    "FinalAdjacentSelfContact@", StringComparison.Ordinal))
            { failure = "a V path must reject nonlocal cardinal edge contact during final replay"; return false; }

            failure = string.Empty;
            return true;
        }

        private static bool CostBreakdownsMatch(
            AccessSearchResult left,
            AccessSearchResult right)
            => Math.Abs(left.TraversalCost - right.TraversalCost) <= 0.0001f
                && Math.Abs(left.GeneratedWorkCost - right.GeneratedWorkCost) <= 0.0001f
                && Math.Abs(left.GeneratedFixedCost - right.GeneratedFixedCost) <= 0.0001f
                && Math.Abs(left.TreeCleanupCost - right.TreeCleanupCost) <= 0.0001f
                && Math.Abs(left.DenseDebrisCleanupCost - right.DenseDebrisCleanupCost) <= 0.0001f
                && Math.Abs(left.GeneratedDirectWorkCost - right.GeneratedDirectWorkCost) <= 0.0001f
                && Math.Abs(left.LeftSideRayCost - right.LeftSideRayCost) <= 0.0001f
                && Math.Abs(left.RightSideRayCost - right.RightSideRayCost) <= 0.0001f
                && Math.Abs(left.SideRayUnresolvedPenalty - right.SideRayUnresolvedPenalty) <= 0.0001f
                && left.SideRaySampleCount == right.SideRaySampleCount;

        private static bool CostBreakdownSumsToTotal(AccessSearchResult result)
            => Math.Abs(result.Cost
                - (result.TraversalCost
                    + result.GeneratedWorkCost
                    + result.GeneratedFixedCost
                    + result.TreeCleanupCost
                    + result.DenseDebrisCleanupCost)) <= 0.0001f;

        public static AccessSearchResult FindPath(
            AccessSearchSnapshot snapshot,
            IReadOnlyList<Tile2i> clusterOrigins)
            => FindPath(snapshot, clusterOrigins, null);

        public static AccessSearchResult FindPath(AccessPathRequest request)
        {
            AccessPathSearchSession session = CreateSession(request);
            while (!session.IsComplete)
                session.Step(int.MaxValue);
            return session.Result;
        }

        public static AccessPathSearchSession CreateSession(AccessPathRequest request)
        {
            var rejections = new Dictionary<string, int>(StringComparer.Ordinal);
            var diagnostics = new AccessSearchDiagnostics();
            Tile2i start = request.Start.Nodes.Count > 0 ? request.Start.Nodes[0] : default;
            if (request.RequiredWidth == 2)
            {
                if (request.V2Endpoints == null)
                    return AccessPathSearchSession.Completed(Failed(
                        "V2FrontagesMissing", start, 0, rejections));
                if (request.V2Endpoints.Starts.Count == 0)
                    return AccessPathSearchSession.Completed(Failed(
                        "NoWidth2StartCompanion", start, 0, rejections));
                bool useV2AStar = ShouldUseV2AStar(request);
                AccessV2PotentialField? v2Potential = useV2AStar
                    ? new AccessV2PotentialField(
                        request.Snapshot.GoalDistanceMin,
                        request.Snapshot.GoalDistanceMax,
                        request.Snapshot.V2GroundGraph,
                        request.V2Endpoints.FixedGoals,
                        AccessV2CostModel.GetMinimumVTravelCostPerTile(
                            GeneratedVFixedOverhead))
                    : null;
                var v2Session = new AccessV2SearchSession(
                    request.V2Endpoints,
                    request.BoundsMin,
                    request.BoundsMax,
                    (current, transition, history, connectedFixedOrigin)
                        => EvaluateV2Transition(
                            request.Snapshot, current, transition,
                            history, connectedFixedOrigin),
                    MaxVisitedNodes,
                    request.MaxCostLimit,
                    request.Snapshot.V2GroundGraph != null
                        && request.Snapshot.HasV2WorkableHandoffEvaluator
                            ? (recent, history, requiredGroundEntry) =>
                                EvaluateV2Handoffs(
                                    request.Snapshot, recent, history,
                                    requiredGroundEntry, diagnostics)
                            : (AccessV2HandoffEvaluator?)null,
                    heuristicEvaluator: null,
                    groundGraph: request.Snapshot.V2GroundGraph,
                    groundValidator: request.Snapshot.IsProjectedV2CenterPathable,
                    cleanupCostScale:
                        request.Snapshot.LandscapingCostDistanceScale,
                    potentialField: v2Potential,
                    groundHeightProvider: tile => request.Snapshot.TryGetGroundHeight2(
                        tile, out int height2) ? height2 : (int?)null,
                    terrainCenterHeightProvider:
                        request.Snapshot.GetTerrainCenterHeight2,
                    groundToVMinimumGeneratedCost:
                        2f * GeneratedVFixedOverhead,
                    usefulHeightEnvelope: request.Snapshot.UsefulHeightEnvelope,
                    generatedOriginValidator: request.Snapshot.IsOriginInside,
                    diagnostics: diagnostics,
                    preciseTerrainHeightProvider: tile =>
                        request.Snapshot.TryGetPreciseTerrainHeight(
                            tile, out float height) ? height : (float?)null,
                    groundToVCenterSpokeCost:
                        AccessV2CostModel.GetCenterSpokeCost(
                            GeneratedVFixedOverhead));
                return new AccessPathSearchSession(
                    v2Session, start, diagnostics);
            }
            if (request.RequiredWidth != 1)
                return AccessPathSearchSession.Completed(Failed("UnsupportedWidth", start, 0, rejections));
            if (request.Start.Kind != AccessPathEndpointKind.FixedProfiles)
                return AccessPathSearchSession.Completed(Failed("UnsupportedStartEndpoint", start, 0, rejections));
            if (request.Goal.Kind != AccessPathEndpointKind.GroundTiles
                && request.Goal.Kind != AccessPathEndpointKind.FixedProfiles
                && request.Goal.Kind != AccessPathEndpointKind.CombinedGoals)
                return AccessPathSearchSession.Completed(Failed("UnsupportedGoalEndpoint", start, 0, rejections));
            if (request.Intent != AccessPathIntent.ConstructAccessway)
                return AccessPathSearchSession.Completed(Failed("UnsupportedIntent", start, 0, rejections));
            HashSet<Tile2i>? fixedGoalOrigins =
                request.Goal.FixedProfileNodes.Count > 0
                    ? new HashSet<Tile2i>(request.Goal.FixedProfileNodes)
                    : null;
            bool includeGroundGoals = request.Goal.GroundTileNodes.Count > 0;
            return CreateSession(request.Snapshot, request.Start.Nodes, null, fixedGoalOrigins,
                includeGroundGoals, useAStarHeuristic: request.Snapshot.UseAStar,
                maxCostLimit: request.MaxCostLimit);
        }

        private static AccessSearchResult FindPath(
            AccessSearchSnapshot snapshot,
            IReadOnlyList<Tile2i> clusterOrigins,
            Func<AccessSearchResult, string?>? rejectGoal,
            HashSet<Tile2i>? fixedGoalOrigins = null,
            bool useAStarHeuristic = true)
        {
            AccessPathSearchSession session = CreateSession(
                snapshot, clusterOrigins, rejectGoal, fixedGoalOrigins,
                includeGroundGoals: fixedGoalOrigins == null,
                useAStarHeuristic, float.MaxValue);
            while (!session.IsComplete)
                session.Step(int.MaxValue);
            return session.Result;
        }

        private static AccessPathSearchSession CreateSession(
            AccessSearchSnapshot snapshot,
            IReadOnlyList<Tile2i> clusterOrigins,
            Func<AccessSearchResult, string?>? rejectGoal,
            HashSet<Tile2i>? fixedGoalOrigins,
            bool includeGroundGoals,
            bool useAStarHeuristic,
            float maxCostLimit)
        {
            var rejections = new Dictionary<string, int>(StringComparer.Ordinal);
            var diagnostics = new AccessSearchDiagnostics();
            Tile2i startOrigin = SelectStart(clusterOrigins);
            if (clusterOrigins.Count == 0)
                return AccessPathSearchSession.Completed(Failed("NoStart", startOrigin, 0, rejections));
            int fixedGoalCount = fixedGoalOrigins?.Count ?? 0;
            int groundGoalCount = includeGroundGoals ? snapshot.GoalCount : 0;
            if (fixedGoalCount == 0 && groundGoalCount == 0)
                return AccessPathSearchSession.Completed(Failed("NoGoals", startOrigin, 0, rejections));
            if (!snapshot.TryGetFixedProfile(startOrigin, out AccessHeightProfile startProfile))
                return AccessPathSearchSession.Completed(Failed("NoStartProfile", startOrigin, 0, rejections));
            if (snapshot.IsProfileOceanBlocked(startOrigin, startProfile))
                return AccessPathSearchSession.Completed(Failed("OceanStartBelowMinimum", startOrigin, 0, rejections));

            var goalsByHeight2 = new Dictionary<int, List<Tile2i>>();
            if (includeGroundGoals)
            {
                foreach (Tile2i goal in snapshot.GoalGroundNodes)
                    if (snapshot.TryGetGroundHeight2(goal, out int height2))
                        AddGoal(height2, goal);
            }
            if (fixedGoalOrigins != null)
            {
                foreach (Tile2i goal in fixedGoalOrigins)
                {
                    if (snapshot.TryGetFixedProfile(goal, out AccessHeightProfile profile))
                        AddGoal(profile.Center2, goal + new RelTile2i(2, 2));
                }
            }
            HeightAwareGoalIndex goalIndex =
                snapshot.UseAStar && useAStarHeuristic
                    ? HeightAwareGoalIndex.Build(
                        snapshot, goalsByHeight2, includeGroundGoals)
                    : HeightAwareGoalIndex.Empty;

            void AddGoal(int height2, Tile2i goal)
            {
                if (!goalsByHeight2.TryGetValue(height2, out List<Tile2i> goals))
                {
                    goals = new List<Tile2i>();
                    goalsByHeight2.Add(height2, goals);
                }
                goals.Add(goal);
            }

            var distance = new Dictionary<AccessSearchNode, float>();
            var previous = new Dictionary<AccessSearchNode, AccessSearchNode>();
            var generatedHistory = new Dictionary<AccessSearchNode, GeneratedPathHistory>();
            var queue = new MinQueue();
            var startNode = new AccessSearchNode(startOrigin, startProfile.Center2, AccessSearchMode.Existing);
            distance[startNode] = 0f;
            generatedHistory[startNode] = GeneratedPathHistory.Empty;
            List<AccessSearchNode>? lastRejectedGoalPath = null;
            string lastGoalRejectionReason = string.Empty;
            float lastRejectedGoalCost = 0f;

            ExpandOrigin(snapshot, startNode, startProfile, 0f,
                distance, previous, generatedHistory, queue, rejections,
                useAStarHeuristic, goalIndex, diagnostics);

            if (queue.Count == 0)
                return AccessPathSearchSession.Completed(Failed("NoInitialSuccessor", startOrigin, 0, rejections));

            return new AccessPathSearchSession(snapshot, startOrigin, startNode,
                fixedGoalOrigins, includeGroundGoals, rejectGoal,
                useAStarHeuristic, goalIndex, maxCostLimit,
                distance, previous, generatedHistory, queue, rejections, diagnostics, lastRejectedGoalPath,
                lastGoalRejectionReason, lastRejectedGoalCost);
        }

        internal static bool ShouldUseV2AStar(AccessPathRequest request)
            => request.RequiredWidth == 2
                && request.Snapshot.UseAStar
                && request.V2Endpoints != null
                && (request.V2Endpoints.FixedGoals.Count > 0
                    || (request.Snapshot.V2GroundGraph != null
                        && request.Snapshot.HasV2WorkableHandoffEvaluator
                        && request.Snapshot.GoalCount > 0));

        public sealed class AccessPathSearchSession
        {
            private readonly AccessV2SearchSession? m_v2Session;
            private readonly AccessSearchSnapshot m_snapshot;
            private readonly Tile2i m_startOrigin;
            private readonly AccessSearchNode m_startNode;
            private readonly HashSet<Tile2i>? m_fixedGoalOrigins;
            private readonly bool m_includeGroundGoals;
            private readonly Func<AccessSearchResult, string?>? m_rejectGoal;
            private readonly bool m_useAStarHeuristic;
            private readonly Dictionary<AccessSearchNode, float> m_distance;
            private readonly Dictionary<AccessSearchNode, AccessSearchNode> m_previous;
            private readonly Dictionary<AccessSearchNode, GeneratedPathHistory> m_generatedHistory;
            private readonly MinQueue m_queue;
            private readonly Dictionary<string, int> m_rejections;
            private readonly AccessSearchDiagnostics m_diagnostics;
            private List<AccessSearchNode>? m_lastRejectedGoalPath;
            private string m_lastGoalRejectionReason;
            private float m_lastRejectedGoalCost;
            private int m_visited;
            private readonly HeightAwareGoalIndex m_goalIndex;
            private readonly float m_maxCostLimit;
            private Action<Tile2i, int, bool, int?>? m_nodeExplored;

            private bool m_isComplete;
            public bool IsComplete
            {
                get => m_v2Session?.IsComplete ?? m_isComplete;
                private set => m_isComplete = value;
            }
            public AccessSearchResult Result { get; private set; }
            public int VisitedNodes => m_v2Session?.Visited ?? m_visited;
            public int PendingNodes => m_v2Session != null
                ? m_v2Session.Pending
                : IsComplete || m_queue == null ? 0 : m_queue.Count;
            // Diagnostic-only hook used by the experimental search overlay.
            internal Action<Tile2i, int, bool, int?>? NodeExplored
            {
                get => m_nodeExplored;
                set
                {
                    m_nodeExplored = value;
                    if (m_v2Session != null)
                        m_v2Session.NodeExplored = value;
                }
            }
            public Dictionary<string, int> Rejections => m_rejections;
            internal AccessSearchDiagnostics Diagnostics => m_diagnostics;

            internal static AccessPathSearchSession Completed(AccessSearchResult result)
                => new AccessPathSearchSession(result);

            private AccessPathSearchSession(AccessSearchResult result)
            {
                m_v2Session = null;
                m_snapshot = null!;
                m_startOrigin = result.StartOrigin;
                m_startNode = default;
                m_fixedGoalOrigins = null;
                m_includeGroundGoals = false;
                m_rejectGoal = null;
                m_useAStarHeuristic = false;
                m_distance = null!;
                m_previous = null!;
                m_generatedHistory = null!;
                m_queue = null!;
                m_rejections = new Dictionary<string, int>(StringComparer.Ordinal);
                m_diagnostics = result.Diagnostics.Clone();
                foreach (KeyValuePair<string, int> pair in result.Rejections)
                    m_rejections[pair.Key] = pair.Value;
                m_lastGoalRejectionReason = string.Empty;
                m_goalIndex = HeightAwareGoalIndex.Empty;
                m_maxCostLimit = float.MaxValue;
                Result = result;
                IsComplete = true;
            }

            internal AccessPathSearchSession(
                AccessSearchSnapshot snapshot,
                Tile2i startOrigin,
                AccessSearchNode startNode,
                HashSet<Tile2i>? fixedGoalOrigins,
                bool includeGroundGoals,
                Func<AccessSearchResult, string?>? rejectGoal,
                bool useAStarHeuristic,
                HeightAwareGoalIndex goalIndex,
                float maxCostLimit,
                Dictionary<AccessSearchNode, float> distance,
                Dictionary<AccessSearchNode, AccessSearchNode> previous,
                Dictionary<AccessSearchNode, GeneratedPathHistory> generatedHistory,
                MinQueue queue,
                Dictionary<string, int> rejections,
                AccessSearchDiagnostics diagnostics,
                List<AccessSearchNode>? lastRejectedGoalPath,
                string lastGoalRejectionReason,
                float lastRejectedGoalCost)
            {
                m_v2Session = null;
                m_snapshot = snapshot;
                m_startOrigin = startOrigin;
                m_startNode = startNode;
                m_fixedGoalOrigins = fixedGoalOrigins;
                m_includeGroundGoals = includeGroundGoals;
                m_rejectGoal = rejectGoal;
                m_useAStarHeuristic = useAStarHeuristic;
                m_goalIndex = goalIndex;
                m_maxCostLimit = maxCostLimit;
                m_distance = distance;
                m_previous = previous;
                m_generatedHistory = generatedHistory;
                m_queue = queue;
                m_rejections = rejections;
                m_diagnostics = diagnostics;
                m_lastRejectedGoalPath = lastRejectedGoalPath;
                m_lastGoalRejectionReason = lastGoalRejectionReason;
                m_lastRejectedGoalCost = lastRejectedGoalCost;
                Result = Failed("SearchNotComplete", startOrigin, 0, rejections);
            }

            public int Step(int maxVisitedNodes)
            {
                if (m_v2Session != null)
                {
                    int visited = m_v2Session.Step(maxVisitedNodes);
                    if (m_v2Session.IsComplete)
                        Result = ConvertV2Result(m_v2Session.Result);
                    return visited;
                }
                if (IsComplete) return 0;
                if (maxVisitedNodes <= 0) maxVisitedNodes = 1;

                int visitedThisStep = 0;
                while (m_queue.Count > 0 && m_visited < MaxVisitedNodes && visitedThisStep < maxVisitedNodes)
                {
                    QueueEntry entry = m_queue.Pop();
                    if (entry.Priority > m_maxCostLimit)
                    {
                        CompleteFailed("CostLimitExceeded");
                        break;
                    }
                    if (!m_distance.TryGetValue(entry.Node, out float known) || entry.PathCost > known + 0.0001f)
                    {
                        m_diagnostics.QueueStalePops++;
                        continue;
                    }

                    AccessSearchNode current = entry.Node;
                    m_visited++;
                    visitedThisStep++;
                    m_nodeExplored?.Invoke(
                        current.Position,
                        current.Height2,
                        current.IsGround,
                        m_snapshot.TryGetGroundHeight2(current.Position, out int groundHeight2)
                            ? groundHeight2
                            : (int?)null);
                    AccessReachedGoalKind reachedGoalKind =
                        m_includeGroundGoals
                        && current.IsGround
                        && m_snapshot.IsGoalGroundNode(current.Position)
                            ? AccessReachedGoalKind.TowerGround
                            : m_fixedGoalOrigins != null
                                && current.Mode == AccessSearchMode.Existing
                                && m_fixedGoalOrigins.Contains(current.Position)
                                    ? AccessReachedGoalKind.FixedNetwork
                                    : AccessReachedGoalKind.None;
                    bool isGoal = reachedGoalKind != AccessReachedGoalKind.None;
                    if (isGoal)
                    {
                        m_diagnostics.GoalPops++;
                        List<AccessSearchNode> path = Reconstruct(current, m_startNode, m_previous);
                        var candidate = BuildResult(
                            true, string.Empty, m_startOrigin, m_startNode, path, known,
                            m_visited, m_rejections, m_snapshot, reachedGoalKind, m_diagnostics);
                        AccessDesignationPlan goalPlan = AccessPathMaterializer.Materialize(m_snapshot, candidate);
                        string goalFailure = goalPlan.IsValid
                            ? m_rejectGoal?.Invoke(candidate) ?? string.Empty
                            : string.IsNullOrEmpty(goalPlan.FailureReason)
                                ? "Materialization"
                                : goalPlan.FailureReason;
                        if (!string.IsNullOrEmpty(goalFailure))
                        {
                            m_diagnostics.GoalRejected++;
                            Reject(m_rejections, "Goal" + goalFailure);
                            m_lastRejectedGoalPath = path;
                            m_lastGoalRejectionReason = goalFailure;
                            m_lastRejectedGoalCost = known;
                        }
                        else
                        {
                            m_diagnostics.GoalAcceptedAtVisited = m_visited;
                            Result = candidate;
                            IsComplete = true;
                            return visitedThisStep;
                        }
                    }

                    if (current.IsGround)
                    {
                        long phaseStart = Stopwatch.GetTimestamp();
                        ExpandGround(m_snapshot, current, known, m_distance, m_previous, m_generatedHistory, m_queue, m_rejections,
                            m_useAStarHeuristic, m_goalIndex, m_diagnostics);
                        m_diagnostics.GroundExpansionTicks += Stopwatch.GetTimestamp() - phaseStart;
                    }
                    else if (TryGetProfile(m_snapshot, current, out AccessHeightProfile currentProfile))
                    {
                        long phaseStart = Stopwatch.GetTimestamp();
                        ExpandOrigin(m_snapshot, current, currentProfile, known, m_distance, m_previous, m_generatedHistory, m_queue, m_rejections,
                            m_useAStarHeuristic, m_goalIndex, m_diagnostics);
                        m_diagnostics.OriginExpansionTicks += Stopwatch.GetTimestamp() - phaseStart;
                    }
                    else
                        Reject(m_rejections, "MissingProfile");
                }

                if (!IsComplete && (m_queue.Count == 0 || m_visited >= MaxVisitedNodes))
                    CompleteFailed();

                return visitedThisStep;
            }

            private AccessSearchResult ConvertV2Result(
                AccessV2SearchResult v2Result)
            {
                string FormatLogDecimal(float value)
                    => value.ToString(
                        "0.##", System.Globalization.CultureInfo.InvariantCulture);

                float groundTravel = 0f;
                int vToG = 0;
                int gToV = 0;
                for (int index = 1; index < v2Result.RouteSteps.Count; index++)
                {
                    AccessV2RouteStep previous = v2Result.RouteSteps[index - 1];
                    AccessV2RouteStep current = v2Result.RouteSteps[index];
                    if (previous.IsGround && current.IsGround)
                        groundTravel += AccessV2GroundGraph.GetStepCost(
                            previous.GroundCenter!.Value,
                            current.GroundCenter!.Value);
                    else if (!previous.IsGround && current.IsGround)
                        vToG++;
                    else if (previous.IsGround && !current.IsGround)
                        gToV++;
                }

                m_rejections.Clear();
                foreach (KeyValuePair<string, int> pair in v2Result.Rejections)
                    m_rejections[pair.Key] = pair.Value;
                m_diagnostics.V2DryRunSummary =
                    $"algorithm={(v2Result.UsedAStar ? "A*" : "Dijkstra")} " +
                    $"success={v2Result.Success} states={v2Result.States.Count} " +
                    $"generatedOrigins={v2Result.GeneratedProfiles.Count} " +
                    $"cost={FormatLogDecimal(v2Result.Cost)} visited={v2Result.Visited} " +
                    $"costs=[travel:{FormatLogDecimal(v2Result.TraversalCost)}," +
                    $"work:{FormatLogDecimal(v2Result.GeneratedWorkCost)}," +
                    $"direct:{FormatLogDecimal(v2Result.DirectWorkCost)}," +
                    $"fixed:{FormatLogDecimal(v2Result.GeneratedFixedCost)}," +
                    $"rays:{FormatLogDecimal(v2Result.ExteriorRayCost)}," +
                    $"cleanup:{FormatLogDecimal(v2Result.CleanupCost)}] " +
                    $"transitions=[straight:{v2Result.StraightTransitions}," +
                    $"strafe:{v2Result.StrafeTransitions}," +
                    $"turn:{v2Result.TurnTransitions}] " +
                    $"pending={v2Result.Pending} " +
                    $"maxHistoryOrigins={v2Result.MaxHistoryOrigins} " +
                    $"maxRayConstraints={v2Result.MaxRayConstraints} " +
                    $"ground=[states:{v2Result.GroundPath.Count}," +
                    $"travel:{FormatLogDecimal(groundTravel)}," +
                    $"v2g:{vToG},g2v:{gToV}] " +
                    $"handoffs=[evaluated:{v2Result.HandoffEvaluations}," +
                    $"quickAccepted:{v2Result.QuickHandoffAccepts}," +
                    $"generalEvaluated:{v2Result.HandoffEvaluations - v2Result.QuickHandoffAccepts}] " +
                    $"handoff={(v2Result.Handoff == null ? "none" : v2Result.Handoff.ToString())}";
                m_diagnostics.V2DryRunPath = v2Result.RouteSteps.Count > 0
                    ? string.Join(" -> ", v2Result.RouteSteps.Select(step =>
                        step.IsGround
                            ? $"G@{step.GroundCenter!.Value}"
                            : $"V@{step.State}"))
                    : string.Join(" -> ",
                        v2Result.States.Select(state => state.ToString()));
                if (!v2Result.Success)
                    return Failed(
                        v2Result.FailureReason,
                        m_startOrigin,
                        v2Result.Visited,
                        m_rejections,
                        diagnostics: m_diagnostics);

                var route = new AccessV2RouteData(
                    v2Result.States,
                    v2Result.GeneratedProfiles,
                    v2Result.Handoff,
                    v2Result.GroundPath,
                    v2Result.RouteSteps);
                return new AccessSearchResult(
                    true, string.Empty, m_startOrigin,
                    Array.Empty<AccessSearchNode>(),
                    v2Result.Cost, v2Result.Visited,
                    m_rejections,
                    v2Result.TraversalCost,
                    v2Result.GeneratedWorkCost - v2Result.GeneratedFixedCost,
                    v2Result.GeneratedFixedCost,
                    0f,
                    v2Result.CleanupCost,
                    v2Result.Handoff != null
                        ? AccessReachedGoalKind.TowerGround
                        : AccessReachedGoalKind.FixedNetwork,
                    v2Result.DirectWorkCost,
                    0f,
                    v2Result.ExteriorRayCost,
                    0f,
                    0,
                    m_diagnostics,
                    route);
            }

            private void CompleteFailed(string? reason = null)
            {
                if (m_lastRejectedGoalPath != null)
                {
                    string finalReason = reason ?? (m_visited >= MaxVisitedNodes
                        ? "VisitedLimitAfterGoalRejection"
                        : m_lastGoalRejectionReason);
                    Result = new AccessSearchResult(false, finalReason, m_startOrigin, m_lastRejectedGoalPath,
                        m_lastRejectedGoalCost, m_visited, m_rejections,
                        m_lastRejectedGoalCost, 0f, 0f, 0f, 0f,
                        AccessReachedGoalKind.None, diagnostics: m_diagnostics);
                }
                else
                {
                    string finalReason = reason ?? (m_visited >= MaxVisitedNodes ? "VisitedLimit" : "NoPath");
                    Result = Failed(finalReason, m_startOrigin, m_visited, m_rejections, m_diagnostics);
                }
                IsComplete = true;
            }

            internal AccessPathSearchSession(
                AccessV2SearchSession v2Session,
                Tile2i startOrigin,
                AccessSearchDiagnostics diagnostics)
            {
                m_v2Session = v2Session;
                m_snapshot = null!;
                m_startOrigin = startOrigin;
                m_startNode = default;
                m_fixedGoalOrigins = null;
                m_includeGroundGoals = false;
                m_rejectGoal = null;
                m_useAStarHeuristic = false;
                m_distance = null!;
                m_previous = null!;
                m_generatedHistory = null!;
                m_queue = null!;
                m_rejections = v2Session.LiveRejections;
                m_diagnostics = diagnostics;
                m_lastGoalRejectionReason = string.Empty;
                m_goalIndex = HeightAwareGoalIndex.Empty;
                m_maxCostLimit = float.MaxValue;
                Result = Failed(
                    "V2SearchNotComplete", startOrigin, 0,
                    m_rejections, diagnostics: m_diagnostics);
                if (v2Session.IsComplete)
                    Result = ConvertV2Result(v2Session.Result);
            }
        }

        private static Tile2i SelectStart(IReadOnlyList<Tile2i> origins)
        {
            if (origins.Count == 0) return default;
            long sumX = 0, sumY = 0;
            foreach (Tile2i origin in origins) { sumX += origin.X + 2; sumY += origin.Y + 2; }
            Tile2i best = origins[0];
            long bestDistance = long.MaxValue;
            foreach (Tile2i origin in origins)
            {
                long dx = Math.Abs((long)(origin.X + 2) * origins.Count - sumX);
                long dy = Math.Abs((long)(origin.Y + 2) * origins.Count - sumY);
                long candidate = dx + dy;
                if (candidate < bestDistance
                    || (candidate == bestDistance && (origin.X < best.X || (origin.X == best.X && origin.Y < best.Y))))
                { best = origin; bestDistance = candidate; }
            }
            return best;
        }

        private static void ExpandOrigin(AccessSearchSnapshot snapshot, AccessSearchNode current,
            AccessHeightProfile currentProfile, float currentCost,
            Dictionary<AccessSearchNode, float> distance,
            Dictionary<AccessSearchNode, AccessSearchNode> previous,
            Dictionary<AccessSearchNode, GeneratedPathHistory> generatedHistory,
            MinQueue queue, Dictionary<string, int> rejections,
            bool useAStarHeuristic, HeightAwareGoalIndex goalIndex,
            AccessSearchDiagnostics diagnostics)
        {
            diagnostics.OriginExpansions++;
            var handoffs = new List<AccessGroundHandoff>();
            var emittedHandoffs = new HashSet<(Tile2i Tile, AccessHandoffOperation Operation, int SpanLength)>();
            if (!generatedHistory.TryGetValue(
                    current, out GeneratedPathHistory currentHistory))
                currentHistory = GeneratedPathHistory.Empty;
            List<AccessHandoffSpanCell> recentSpanCells =
                BuildRecentStraightGeneratedSpan(
                    snapshot, current, previous,
                    GetMaxHandoffSpanLength(snapshot.VehicleWidth));
            if (previous.TryGetValue(current, out AccessSearchNode immediatePredecessor))
            {
                AccessHeightProfile predecessorProfile = !immediatePredecessor.IsGround
                    && TryGetProfile(snapshot, immediatePredecessor, out AccessHeightProfile foundPredecessorProfile)
                        ? foundPredecessorProfile
                        : currentProfile;
                AddHandoffs(immediatePredecessor.Position, predecessorProfile);
            }
            else
            {
                foreach (Tile2i direction in s_originDirections)
                    AddHandoffs(
                        new Tile2i(current.Position.X + direction.X, current.Position.Y + direction.Y),
                        currentProfile);
            }
            if (snapshot.HasWorkableHandoffSpanEvaluator)
            {
                for (int spanLength = 2;
                    spanLength <= recentSpanCells.Count;
                    spanLength++)
                {
                    int start = recentSpanCells.Count - spanLength;
                    var span = recentSpanCells.GetRange(start, spanLength);
                    foreach (AccessGroundHandoff handoff in
                        snapshot.GetWorkableHandoffSpans(span))
                    {
                        if (emittedHandoffs.Add((
                            handoff.Tile, handoff.Operation, handoff.SpanLength)))
                            handoffs.Add(handoff);
                    }
                }
            }

            bool traceFirstGeneratedHandoff = current.Mode != AccessSearchMode.Existing
                && previous.TryGetValue(current, out AccessSearchNode firstPredecessor)
                && firstPredecessor.Mode == AccessSearchMode.Existing
                && !previous.ContainsKey(firstPredecessor);
            if (traceFirstGeneratedHandoff)
                diagnostics.RecordFirstGeneratedHandoff(
                    $"origin={current.Position} mode={current.Mode} " +
                    $"profile={FormatProfile(currentProfile)} raw={handoffs.Count}");

            foreach (AccessGroundHandoff handoff in handoffs)
            {
                float handoffBaseCost = currentCost;
                if (!snapshot.TryGetGroundHeight2(handoff.Tile, out int groundHeight2))
                {
                    if (traceFirstGeneratedHandoff)
                        diagnostics.RecordFirstGeneratedHandoff(
                            $"origin={current.Position} tile={handoff.Tile} reject=NoGroundHeight");
                    continue;
                }
                if (currentHistory.IsGroundDisturbed(
                    handoff.Tile, current.Position))
                {
                    Reject(rejections, "GroundOverlapsPriorGeneratedWork");
                    if (traceFirstGeneratedHandoff)
                        diagnostics.RecordFirstGeneratedHandoff(
                            $"origin={current.Position} tile={handoff.Tile} " +
                            "reject=GroundOverlapsPriorGeneratedWork");
                    continue;
                }
                var ground = new AccessSearchNode(handoff.Tile, groundHeight2,
                    AccessSearchMode.Ground, handoff.Operation,
                    handoffSpanLength: handoff.SpanLength);
                GeneratedPathHistory? correctedHandoffHistory = null;
                if (current.Mode != AccessSearchMode.Existing
                    && handoff.Operation != AccessHandoffOperation.None)
                {
                    int spanLength = Math.Max(1, handoff.SpanLength);
                    if (recentSpanCells.Count < spanLength)
                    {
                        Reject(rejections, "HandoffSpanHistoryMissing");
                        continue;
                    }
                    List<AccessHandoffSpanCell> span = recentSpanCells.GetRange(
                        recentSpanCells.Count - spanLength, spanLength);
                    var correctedRays = new List<IReadOnlyList<Tile2i>>(spanLength);
                    float handoffCostDelta = 0f;
                    bool spanRejected = false;
                    AccessSearchNode spanStartNode = current;
                    for (int back = 1; back < spanLength; back++)
                    {
                        if (!previous.TryGetValue(
                                spanStartNode, out AccessSearchNode priorSpanNode))
                        {
                            spanRejected = true;
                            break;
                        }
                        spanStartNode = priorSpanNode;
                    }
                    if (spanRejected)
                    {
                        Reject(rejections, "HandoffSpanHistoryMissing");
                        continue;
                    }
                    for (int spanIndex = 0; spanIndex < span.Count; spanIndex++)
                    {
                        AccessHandoffSpanCell cell = span[spanIndex];
                        float correctedEntryCost = CalculateGeneratedEntryCost(
                            snapshot, cell.Origin, cell.Profile,
                            cell.EntryDirection, handoff.Operation,
                            out AccessLandscapingCost correctedLandscapingCost,
                            out _, out string correctedRejection, diagnostics);
                        if (!string.IsNullOrEmpty(correctedRejection))
                        {
                            Reject(rejections, correctedRejection);
                            spanRejected = true;
                            break;
                        }
                        float levelingEntryCost = CalculateGeneratedEntryCost(
                            snapshot, cell.Origin, cell.Profile,
                            cell.EntryDirection, AccessHandoffOperation.Leveling,
                            out _, out _, out _, diagnostics);
                        float correctedPropCost = CalculateGeneratedPropCleanupCost(
                            snapshot, cell.Origin, cell.Profile, handoff.Operation,
                            out string correctedPropRejection, out _, out _);
                        if (!string.IsNullOrEmpty(correctedPropRejection))
                        {
                            Reject(rejections, correctedPropRejection);
                            spanRejected = true;
                            break;
                        }
                        float levelingPropCost = CalculateGeneratedPropCleanupCost(
                            snapshot, cell.Origin, cell.Profile,
                            AccessHandoffOperation.Leveling, out _, out _, out _);
                        // The queued V state has already paid its provisional
                        // leveling cost.  Never refund that cost here: a negative
                        // terminal edge would invalidate Dijkstra/A* ordering.
                        // Charge only any additional operation-specific work.
                        handoffCostDelta += Math.Max(0f,
                            correctedEntryCost - levelingEntryCost
                            + correctedPropCost - levelingPropCost);
                        IReadOnlyList<Tile2i> correctedDisturbedTiles =
                            correctedLandscapingCost.DisturbedRayTiles;
                        if (spanIndex == 0
                            && previous.TryGetValue(
                                spanStartNode, out AccessSearchNode turnPredecessor)
                            && !turnPredecessor.IsGround
                            && TryGetProfile(snapshot, turnPredecessor,
                                out AccessHeightProfile turnPredecessorProfile))
                        {
                            AccessSideRayResult turnOuterRay = ScoreTurnOuterCorner(
                                snapshot,
                                turnPredecessor.Position,
                                turnPredecessorProfile,
                                turnPredecessor.EntryDirection,
                                cell.EntryDirection,
                                GetGeneratedWorkOperation(
                                    turnPredecessor.HandoffOperation),
                                diagnostics,
                                out Tile2i turnCorner,
                                out Tile2i turnDirection);
                            correctedDisturbedTiles = MergeDisturbedRayTiles(
                                correctedDisturbedTiles,
                                turnCorner,
                                turnDirection,
                                turnOuterRay.DisturbedDistance,
                                snapshot.VehicleClearanceRadius);
                        }
                        correctedRays.Add(correctedDisturbedTiles);
                    }
                    if (spanRejected)
                        continue;
                    if (!currentHistory.TryReplaceLatestGeneratedSpan(
                            span, correctedRays, handoff.EscapeTiles,
                            out correctedHandoffHistory))
                    {
                        Reject(rejections, "HandoffSpanHistoryMismatch");
                        continue;
                    }
                    if (correctedHandoffHistory.IsGroundDisturbed(handoff.Tile))
                    {
                        Reject(rejections, "HandoffOverlapsFinalizedGeneratedWork");
                        continue;
                    }
                    handoffBaseCost += handoffCostDelta;
                }
                float handoffCleanupCost = GetCleanupEntryCost(
                    snapshot, current.Position, handoff.Tile);
                if (traceFirstGeneratedHandoff)
                    diagnostics.RecordFirstGeneratedHandoff(
                        $"origin={current.Position} tile={handoff.Tile} " +
                        $"operation={handoff.Operation} span={handoff.SpanLength} " +
                        $"acceptedCost={(handoffBaseCost + Manhattan(current.CostPosition, handoff.Tile) + handoffCleanupCost).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");
                Relax(snapshot, current, ground,
                    handoffBaseCost
                        + Manhattan(current.CostPosition, handoff.Tile)
                        + handoffCleanupCost,
                    distance, previous, generatedHistory, queue,
                    useAStarHeuristic, goalIndex, diagnostics,
                    nextHistoryOverride: correctedHandoffHistory);
            }

            AddForwardTerminalHandoffs();

            void AddForwardTerminalHandoffs()
            {
                int maxSpanLength = GetMaxHandoffSpanLength(snapshot.VehicleWidth);
                Tile2i direction = current.EntryDirection;
                if (maxSpanLength < 2
                    || !snapshot.HasWorkableHandoffSpanEvaluator
                    || current.Mode == AccessSearchMode.Existing
                    || !IsOriginStep(direction))
                    return;

                var span = new List<AccessHandoffSpanCell>(maxSpanLength)
                {
                    new AccessHandoffSpanCell(
                        current.Position, currentProfile, direction)
                };
                var syntheticNodes = new List<AccessSearchNode>(maxSpanLength - 1);
                Extend(current, currentProfile, currentHistory);

                void Extend(
                    AccessSearchNode predecessorNode,
                    AccessHeightProfile predecessorProfile,
                    GeneratedPathHistory provisionalHistory)
                {
                    if (span.Count >= maxSpanLength)
                        return;

                    Tile2i nextOrigin = new Tile2i(
                        predecessorNode.Position.X + direction.X,
                        predecessorNode.Position.Y + direction.Y);
                    if (!snapshot.IsOriginInside(nextOrigin)
                        || snapshot.TryGetFixedProfile(nextOrigin, out _))
                        return;

                    AccessSearchMode risingMode = GetRisingMode(direction);
                    AccessSearchMode[] modes = risingMode == AccessSearchMode.Flat
                        ? new[] { AccessSearchMode.Flat }
                        : new[] { AccessSearchMode.Flat, risingMode };
                    var emittedProfiles = new HashSet<AccessHeightProfile>();
                    foreach (AccessSearchMode mode in modes)
                    {
                        if (!TrySolveSuccessor(
                                predecessorProfile, direction, mode,
                                out AccessHeightProfile nextProfile)
                            || !emittedProfiles.Add(nextProfile))
                            continue;
                        if (!IsGeneratedProfileFeasible(
                                snapshot, nextOrigin, nextProfile,
                                predecessorNode, direction, out string reason))
                        {
                            Reject(rejections, "ForwardHandoff" + reason);
                            continue;
                        }
                        if (!IsCompatibleWithPathHistory(
                                nextOrigin, nextProfile, provisionalHistory,
                                predecessorNode.Position,
                                out string historyReason))
                        {
                            Reject(rejections, "ForwardHandoff" + historyReason);
                            continue;
                        }

                        var syntheticNode = new AccessSearchNode(
                            nextOrigin, nextProfile.Center2, mode,
                            entryDirection: direction);
                        span.Add(new AccessHandoffSpanCell(
                            nextOrigin, nextProfile, direction));
                        syntheticNodes.Add(syntheticNode);

                        IReadOnlyList<AccessGroundHandoff> spanHandoffs =
                            snapshot.GetWorkableHandoffSpans(span);
                        if (spanHandoffs.Count == 0)
                            Reject(rejections, "ForwardHandoffNoWorkableExit");
                        else
                            TryEmitSpanHandoffs(spanHandoffs);

                        if (span.Count < maxSpanLength)
                        {
                            CalculateGeneratedEntryCost(
                                snapshot, nextOrigin, nextProfile, direction,
                                AccessHandoffOperation.Leveling,
                                out AccessLandscapingCost levelingLandscaping,
                                out _, out string levelingRejection, diagnostics);
                            CalculateGeneratedPropCleanupCost(
                                snapshot, nextOrigin, nextProfile,
                                AccessHandoffOperation.Leveling,
                                out string propRejection, out _, out _);
                            if (!string.IsNullOrEmpty(levelingRejection))
                                Reject(rejections, "ForwardHandoff" + levelingRejection);
                            else if (!string.IsNullOrEmpty(propRejection))
                                Reject(rejections, "ForwardHandoff" + propRejection);
                            else
                            {
                                GeneratedPathHistory nextProvisionalHistory =
                                    provisionalHistory.WithGenerated(
                                        nextOrigin, nextProfile,
                                        levelingLandscaping.DisturbedRayTiles,
                                        rayHeightConstraints:
                                            levelingLandscaping.RayHeightConstraints);
                                Extend(syntheticNode, nextProfile,
                                    nextProvisionalHistory);
                            }
                        }

                        syntheticNodes.RemoveAt(syntheticNodes.Count - 1);
                        span.RemoveAt(span.Count - 1);
                    }
                }

                void TryEmitSpanHandoffs(
                    IReadOnlyList<AccessGroundHandoff> spanHandoffs)
                {
                    foreach (AccessGroundHandoff handoff in spanHandoffs)
                    {
                        if (handoff.SpanLength != span.Count
                            || !snapshot.TryGetGroundHeight2(
                                handoff.Tile, out int groundHeight2))
                            continue;
                        if (!TryBuildFinalHistory(
                                handoff, out GeneratedPathHistory finalHistory,
                                out float terminalCost,
                                out List<IReadOnlyList<Tile2i>> correctedRays))
                            continue;
                        if (finalHistory.IsGroundDisturbed(handoff.Tile))
                        {
                            Reject(rejections,
                                "ForwardHandoffOverlapsFinalizedGeneratedWork");
                            continue;
                        }

                        AccessSearchNode chainPredecessor = current;
                        float chainCost = terminalCost;
                        bool chainRejected = false;
                        for (int index = 0; index < syntheticNodes.Count; index++)
                        {
                            AccessSearchNode provisional = syntheticNodes[index];
                            var terminalNode = new AccessSearchNode(
                                provisional.Position, provisional.Height2,
                                provisional.Mode, handoff.Operation,
                                provisional.EntryDirection,
                                handoffSpanLength: span.Count);
                            float cellCost = 4f + GetCorrectedCellCost(
                                span[index + 1], handoff.Operation,
                                correctedRays[index + 1], out _);
                            chainCost += cellCost;
                            if (distance.TryGetValue(terminalNode, out float knownCost)
                                && knownCost <= chainCost + 0.0001f)
                            {
                                chainRejected = true;
                                break;
                            }
                            distance[terminalNode] = chainCost;
                            previous[terminalNode] = chainPredecessor;
                            generatedHistory[terminalNode] = finalHistory;
                            chainPredecessor = terminalNode;
                        }
                        if (chainRejected)
                            continue;

                        var ground = new AccessSearchNode(
                            handoff.Tile, groundHeight2,
                            AccessSearchMode.Ground, handoff.Operation,
                            handoffSpanLength: handoff.SpanLength);
                        float cleanupCost = GetCleanupEntryCost(
                            snapshot, chainPredecessor.Position, handoff.Tile);
                        Relax(snapshot, chainPredecessor, ground,
                            chainCost
                                + Manhattan(chainPredecessor.CostPosition,
                                    handoff.Tile)
                                + cleanupCost,
                            distance, previous, generatedHistory, queue,
                            useAStarHeuristic, goalIndex, diagnostics,
                            nextHistoryOverride: finalHistory);
                    }
                }

                bool TryBuildFinalHistory(
                    AccessGroundHandoff handoff,
                    out GeneratedPathHistory finalHistory,
                    out float terminalCost,
                    out List<IReadOnlyList<Tile2i>> correctedRays)
                {
                    correctedRays = new List<IReadOnlyList<Tile2i>>(span.Count);
                    terminalCost = currentCost;
                    finalHistory = currentHistory;
                    float currentCorrected = GetCorrectedCellCost(
                        span[0], handoff.Operation, null,
                        out IReadOnlyList<Tile2i> currentRays);
                    if (float.IsPositiveInfinity(currentCorrected))
                        return false;
                    float currentLeveling = GetCorrectedCellCost(
                        span[0], AccessHandoffOperation.Leveling, null,
                        out _);
                    if (float.IsPositiveInfinity(currentLeveling))
                        return false;
                    terminalCost += Math.Max(0f,
                        currentCorrected - currentLeveling);
                    correctedRays.Add(currentRays);

                    for (int index = 1; index < span.Count; index++)
                    {
                        float corrected = GetCorrectedCellCost(
                            span[index], handoff.Operation, null,
                            out IReadOnlyList<Tile2i> rays);
                        if (float.IsPositiveInfinity(corrected))
                            return false;
                        correctedRays.Add(rays);
                    }

                    if (!currentHistory.TryReplaceLatestGeneratedSpan(
                            new[] { span[0] },
                            new[] { correctedRays[0] },
                            handoff.EscapeTiles,
                            out finalHistory))
                    {
                        Reject(rejections, "ForwardHandoffHistoryMismatch");
                        return false;
                    }
                    for (int index = 1; index < span.Count; index++)
                        finalHistory = finalHistory.WithGenerated(
                            span[index].Origin, span[index].Profile,
                            correctedRays[index], handoff.EscapeTiles);
                    return true;
                }

                float GetCorrectedCellCost(
                    AccessHandoffSpanCell cell,
                    AccessHandoffOperation operation,
                    IReadOnlyList<Tile2i>? knownRays,
                    out IReadOnlyList<Tile2i> rays)
                {
                    float entryCost = CalculateGeneratedEntryCost(
                        snapshot, cell.Origin, cell.Profile,
                        cell.EntryDirection, operation,
                        out AccessLandscapingCost landscaping,
                        out _, out string entryRejection, diagnostics);
                    if (!string.IsNullOrEmpty(entryRejection))
                    {
                        Reject(rejections, "ForwardHandoff" + entryRejection);
                        rays = Array.Empty<Tile2i>();
                        return float.PositiveInfinity;
                    }
                    float propCost = CalculateGeneratedPropCleanupCost(
                        snapshot, cell.Origin, cell.Profile, operation,
                        out string propRejection, out _, out _);
                    if (!string.IsNullOrEmpty(propRejection))
                    {
                        Reject(rejections, "ForwardHandoff" + propRejection);
                        rays = Array.Empty<Tile2i>();
                        return float.PositiveInfinity;
                    }
                    rays = knownRays ?? landscaping.DisturbedRayTiles;
                    return entryCost + propCost;
                }
            }

            void AddHandoffs(Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile)
            {
                foreach (AccessGroundHandoff handoff in GetHandoffs(
                    snapshot, current.Position, currentProfile,
                    predecessorOrigin, predecessorProfile))
                {
                    if (emittedHandoffs.Add((
                        handoff.Tile, handoff.Operation, handoff.SpanLength)))
                        handoffs.Add(handoff);
                }
            }

            foreach (Tile2i direction in s_originDirections)
            {
                diagnostics.OriginNeighborChecks++;
                Tile2i nextOrigin = new Tile2i(current.Position.X + direction.X, current.Position.Y + direction.Y);
                if (previous.TryGetValue(current, out AccessSearchNode groundPredecessor)
                    && groundPredecessor.IsGround
                    && current.HandoffOperation != AccessHandoffOperation.None
                    && !IsGroundToGeneratedContinuation(
                        snapshot, current.Position, currentProfile,
                        groundPredecessor.Position, current.HandoffOperation, nextOrigin))
                {
                    Reject(rejections, "GToVHandoffDirection");
                    continue;
                }
                if (previous.TryGetValue(current, out AccessSearchNode parent)
                    && !parent.IsGround
                    && parent.Position == nextOrigin)
                {
                    Reject(rejections, "OriginRevisit");
                    continue;
                }
                AddOriginSuccessors(snapshot, current.Position, currentProfile, nextOrigin, direction,
                    current, true, currentCost, distance, previous, generatedHistory, queue, rejections, useAStarHeuristic,
                    goalIndex, diagnostics, !previous.ContainsKey(current));
            }
        }

        private static void AddOriginSuccessors(AccessSearchSnapshot snapshot,
            Tile2i currentOrigin, AccessHeightProfile currentProfile, Tile2i nextOrigin, Tile2i direction,
            AccessSearchNode current, bool hasCurrent, float baseCost,
            Dictionary<AccessSearchNode, float> distance,
            Dictionary<AccessSearchNode, AccessSearchNode> previous,
            Dictionary<AccessSearchNode, GeneratedPathHistory> generatedHistory,
            MinQueue queue, Dictionary<string, int> rejections,
            bool useAStarHeuristic, HeightAwareGoalIndex goalIndex,
            AccessSearchDiagnostics diagnostics,
            bool traceStartSuccessors)
        {
            void Trace(AccessSearchMode mode, AccessHeightProfile? profile, string outcome)
            {
                if (!traceStartSuccessors) return;
                diagnostics.RecordStartSuccessor(
                    $"from={currentOrigin} start={FormatProfile(currentProfile)} " +
                    $"direction={direction} next={nextOrigin} mode={mode} " +
                    $"profile={(profile.HasValue ? FormatProfile(profile.Value) : "none")} " +
                    $"outcome={outcome}");
            }

            if (!snapshot.IsOriginInside(nextOrigin))
            {
                Reject(rejections, "HorizontalBounds");
                Trace(AccessSearchMode.Flat, null, "HorizontalBounds");
                return;
            }

            if (snapshot.TryGetFixedProfile(nextOrigin, out AccessHeightProfile fixedProfile))
            {
                diagnostics.FixedProfileSuccessorChecks++;
                if (snapshot.IsProfileOceanBlocked(nextOrigin, fixedProfile))
                {
                    Reject(rejections, "OceanBelowMinimum");
                    Trace(AccessSearchMode.Existing, fixedProfile, "OceanBelowMinimum");
                    return;
                }
                if (!EdgesMatch(currentProfile, fixedProfile, direction))
                {
                    Reject(rejections, "FixedEdgeMismatch");
                    Trace(AccessSearchMode.Existing, fixedProfile, "FixedEdgeMismatch");
                    return;
                }
                var existing = new AccessSearchNode(nextOrigin, fixedProfile.Center2, AccessSearchMode.Existing);
                diagnostics.FixedProfileRelaxations++;
                Trace(AccessSearchMode.Existing, fixedProfile, "RelaxedFixed");
                Relax(snapshot, current, existing, baseCost + 4f, distance, previous, generatedHistory, queue, useAStarHeuristic,
                    goalIndex, diagnostics, hasCurrent);
                return;
            }

            foreach (AccessSearchMode mode in s_vModes)
            {
                diagnostics.GeneratedModeAttempts++;
                if (!TrySolveSuccessor(currentProfile, direction, mode, out AccessHeightProfile nextProfile))
                {
                    Reject(rejections, "EdgeProfile");
                    Trace(mode, null, "EdgeProfile");
                    continue;
                }
                if (!IsGeneratedCenterWithinUsefulHeightEnvelope(
                        snapshot, nextOrigin, nextProfile, diagnostics,
                        out string heightEnvelopeRejection))
                {
                    Reject(rejections, heightEnvelopeRejection);
                    Trace(mode, nextProfile, heightEnvelopeRejection);
                    continue;
                }
                diagnostics.GeneratedProfileFeasibleChecks++;
                long phaseStart = Stopwatch.GetTimestamp();
                bool profileFeasible = IsGeneratedProfileFeasible(
                    snapshot, nextOrigin, nextProfile, current, direction, out string reason);
                diagnostics.ProfileFeasibilityTicks += Stopwatch.GetTimestamp() - phaseStart;
                if (!profileFeasible)
                {
                    diagnostics.GeneratedProfileFeasibleFailures++;
                    Reject(rejections, reason);
                    Trace(mode, nextProfile, reason);
                    continue;
                }
                phaseStart = Stopwatch.GetTimestamp();
                string historyReason = string.Empty;
                bool historyCompatible = !hasCurrent
                    || IsCompatibleWithPathHistory(
                        nextOrigin, nextProfile, current, generatedHistory,
                        out historyReason);
                diagnostics.PathHistoryTicks += Stopwatch.GetTimestamp() - phaseStart;
                if (!historyCompatible)
                {
                    diagnostics.GeneratedPathHistoryFailures++;
                    Reject(rejections, string.IsNullOrEmpty(historyReason)
                        ? "PathSelfContact" : historyReason);
                    Trace(mode, nextProfile, string.IsNullOrEmpty(historyReason)
                        ? "PathSelfContact" : historyReason);
                    continue;
                }

                var next = new AccessSearchNode(
                    nextOrigin, nextProfile.Center2, mode,
                    entryDirection: direction);
                float lowerBoundCost = baseCost + 4f;
                if (IsKnownDistanceNoWorse(distance, next, lowerBoundCost))
                {
                    Trace(mode, nextProfile, "KnownDistanceNoWorse");
                    continue;
                }
                diagnostics.SideRayCostChecks++;
                phaseStart = Stopwatch.GetTimestamp();
                float generatedEntryCost = CalculateGeneratedEntryCost(
                    snapshot, nextOrigin, nextProfile,
                    direction, AccessHandoffOperation.Leveling,
                    out AccessLandscapingCost landscapingCost, out _, out string sideRayRejection,
                    diagnostics);
                diagnostics.SideRayCostTicks += Stopwatch.GetTimestamp() - phaseStart;
                diagnostics.SideRayCostSamples += landscapingCost.RaySampleCount;
                if (!string.IsNullOrEmpty(sideRayRejection))
                {
                    diagnostics.SideRayCostRejections++;
                    Reject(rejections, sideRayRejection);
                    Trace(mode, nextProfile, sideRayRejection);
                    continue;
                }
                phaseStart = Stopwatch.GetTimestamp();
                AccessSideRayResult turnOuterRay = ScoreTurnOuterCorner(
                    snapshot, current.Position, currentProfile,
                    current.EntryDirection, direction,
                    GetGeneratedWorkOperation(current.HandoffOperation), diagnostics,
                    out Tile2i turnCorner, out Tile2i turnDirection);
                diagnostics.SideRayCostTicks += Stopwatch.GetTimestamp() - phaseStart;
                diagnostics.SideRayCostSamples += turnOuterRay.SampleCount;
                if (turnOuterRay.IsFatal)
                {
                    diagnostics.SideRayCostRejections++;
                    Reject(rejections, turnOuterRay.FatalReason!);
                    Trace(mode, nextProfile, turnOuterRay.FatalReason!);
                    continue;
                }
                diagnostics.PropCleanupChecks++;
                phaseStart = Stopwatch.GetTimestamp();
                float propCleanupCost = CalculateGeneratedPropCleanupCost(
                    snapshot, nextOrigin, nextProfile, AccessHandoffOperation.Leveling,
                    out string propCleanupRejection, out _, out _);
                diagnostics.PropCleanupTicks += Stopwatch.GetTimestamp() - phaseStart;
                if (!string.IsNullOrEmpty(propCleanupRejection))
                {
                    diagnostics.PropCleanupRejections++;
                    Reject(rejections, propCleanupRejection);
                    Trace(mode, nextProfile, propCleanupRejection);
                    continue;
                }
                if (propCleanupCost > 0f) diagnostics.PropCleanupHits++;
                float turnOuterCost = snapshot.LandscapingCostDistanceScale
                    * SideRayWeight * turnOuterRay.TotalCost;
                float nextCost = baseCost + 4f + generatedEntryCost
                    + turnOuterCost + propCleanupCost;
                Trace(mode, nextProfile,
                    "RelaxedCost=" + nextCost.ToString(
                        "0.##", System.Globalization.CultureInfo.InvariantCulture));
                IReadOnlyList<AccessRayHeightConstraint> historyRayConstraints =
                    MergeRayHeightConstraints(
                        landscapingCost.RayHeightConstraints,
                        BuildRayHeightConstraints(
                            snapshot, turnCorner,
                            GetProfileCornerHeight(
                                current.Position, currentProfile, turnCorner),
                            turnDirection, turnOuterRay,
                            GetGeneratedWorkOperation(current.HandoffOperation),
                            snapshot.VehicleClearanceRadius));
                diagnostics.GeneratedRelaxations++;
                Relax(snapshot, current, next, nextCost, distance, previous, generatedHistory, queue, useAStarHeuristic,
                    goalIndex, diagnostics, hasCurrent,
                    MergeDisturbedRayTiles(
                        landscapingCost.DisturbedRayTiles,
                        turnCorner, turnDirection, turnOuterRay.DisturbedDistance,
                        snapshot.VehicleClearanceRadius),
                    historyRayConstraints);
            }
        }

        private static void ExpandGround(AccessSearchSnapshot snapshot, AccessSearchNode current, float currentCost,
            Dictionary<AccessSearchNode, float> distance,
            Dictionary<AccessSearchNode, AccessSearchNode> previous,
            Dictionary<AccessSearchNode, GeneratedPathHistory> generatedHistory,
            MinQueue queue, Dictionary<string, int> rejections,
            bool useAStarHeuristic, HeightAwareGoalIndex goalIndex,
            AccessSearchDiagnostics diagnostics)
        {
            diagnostics.GroundExpansions++;
            if (!generatedHistory.TryGetValue(
                    current, out GeneratedPathHistory currentHistory))
                currentHistory = GeneratedPathHistory.Empty;
            int incomingX = 0;
            int incomingY = 0;
            if (previous.TryGetValue(current, out AccessSearchNode groundParent)
                && groundParent.IsGround)
            {
                incomingX = Math.Sign(current.Position.X - groundParent.Position.X);
                incomingY = Math.Sign(current.Position.Y - groundParent.Position.Y);
            }
            bool currentHandoffClearsProp =
                (current.HandoffOperation == AccessHandoffOperation.Mining
                    || current.HandoffOperation == AccessHandoffOperation.Leveling)
                && snapshot.HasRemovableNonTreePropAtTile(current.Position);
            foreach (RelTile2i direction in s_tileDirections)
            {
                if (incomingX * direction.X + incomingY * direction.Y < 0)
                    continue;
                Tile2i nextTile = current.Position + direction;
                diagnostics.GroundSuccessorChecks++;
                bool nextIsGround = snapshot.IsGroundNode(nextTile);
                bool nextIsCleanup = !nextIsGround
                    && snapshot.CanTraverseToCleanupGround(
                        current.Position, nextTile, currentHandoffClearsProp);
                if (!nextIsGround && !nextIsCleanup)
                    continue;
                if (direction.X != 0 && direction.Y != 0)
                {
                    // A diagonal may only cross the corner after both cardinal
                    // corridors have independently proved clear.  Keep those
                    // intermediates ordinary ground: unlike V2's swept-center
                    // bookkeeping, V1 has no side-cleanup materialization edge.
                    Tile2i sideX = new Tile2i(nextTile.X, current.Position.Y);
                    Tile2i sideY = new Tile2i(current.Position.X, nextTile.Y);
                    if (!snapshot.IsGroundNode(sideX)
                        || !snapshot.IsGroundNode(sideY)
                        || currentHistory.IsGroundDisturbed(sideX)
                        || currentHistory.IsGroundDisturbed(sideY))
                        continue;
                }
                if (!nextIsGround) diagnostics.CleanupGroundSuccessorChecks++;
                if (!snapshot.TryGetGroundHeight2(nextTile, out int height2))
                    continue;
                if (currentHistory.IsGroundDisturbed(nextTile))
                {
                    Reject(rejections, "GroundOverlapsGeneratedWork");
                    continue;
                }
                var next = new AccessSearchNode(nextTile, height2, AccessSearchMode.Ground);
                float cleanupCost = GetCleanupEntryCost(snapshot, current.Position, nextTile);
                if (nextIsGround) diagnostics.GroundRelaxations++;
                else diagnostics.CleanupGroundRelaxations++;
                float stepCost = direction.X != 0 && direction.Y != 0
                    ? GroundDiagonalCost
                    : 1f;
                float nextCost = currentCost + stepCost + cleanupCost;
                Relax(snapshot, current, next, nextCost,
                    distance, previous, generatedHistory, queue,
                    useAStarHeuristic, goalIndex, diagnostics);
            }

            foreach (Tile2i origin in CandidateOriginsAtGroundTile(current.Position))
            {
                diagnostics.GroundToGeneratedOriginChecks++;
                if (!snapshot.IsOriginInside(origin))
                {
                    Reject(rejections, "HorizontalBounds");
                    continue;
                }
                foreach (AccessSearchMode mode in s_vModes)
                {
                    int center2 = snapshot.GetTerrainCenterHeight2(origin);
                    for (int delta = -3; delta <= 3; delta++)
                    {
                        if (!AccessHeightProfile.TryForMode(mode, center2 + delta, out AccessHeightProfile profile)) continue;
                        diagnostics.GroundToGeneratedProfileAttempts++;
                        if (!IsGeneratedCenterWithinUsefulHeightEnvelope(
                                snapshot, origin, profile, diagnostics,
                                out string heightEnvelopeRejection))
                        {
                            Reject(rejections, heightEnvelopeRejection);
                            continue;
                        }
                        diagnostics.GeneratedProfileFeasibleChecks++;
                        long phaseStart = Stopwatch.GetTimestamp();
                        bool profileFeasible = IsGeneratedProfileFeasible(
                            snapshot, origin, profile, current, default, out string reason);
                        diagnostics.ProfileFeasibilityTicks += Stopwatch.GetTimestamp() - phaseStart;
                        if (!profileFeasible)
                        { diagnostics.GeneratedProfileFeasibleFailures++; Reject(rejections, reason); continue; }
                        phaseStart = Stopwatch.GetTimestamp();
                        bool hasHandoff = TryGetGroundToGeneratedHandoff(
                            snapshot, origin, profile, current.Position,
                            out AccessHandoffOperation handoffOperation,
                            out Tile2i entryDirection);
                        diagnostics.HandoffValidationTicks += Stopwatch.GetTimestamp() - phaseStart;
                        if (!hasHandoff)
                        {
                            diagnostics.GroundToGeneratedHandoffFailures++;
                            continue;
                        }
                        phaseStart = Stopwatch.GetTimestamp();
                        bool historyCompatible = IsCompatibleWithPathHistory(
                            origin, profile, current, generatedHistory,
                            out string historyReason);
                        diagnostics.PathHistoryTicks += Stopwatch.GetTimestamp() - phaseStart;
                        if (!historyCompatible)
                        {
                            diagnostics.GeneratedPathHistoryFailures++;
                            Reject(rejections, historyReason);
                            continue;
                        }
                        var next = new AccessSearchNode(
                            origin, profile.Center2, mode,
                            handoffOperation, entryDirection);
                        float lowerBoundCost = currentCost + Manhattan(current.Position, next.CostPosition);
                        if (IsKnownDistanceNoWorse(distance, next, lowerBoundCost))
                            continue;
                        diagnostics.SideRayCostChecks++;
                        phaseStart = Stopwatch.GetTimestamp();
                        float generatedEntryCost = CalculateGeneratedEntryCost(
                            snapshot, origin, profile,
                            entryDirection, handoffOperation,
                            out AccessLandscapingCost landscapingCost, out _, out string sideRayRejection,
                            diagnostics);
                        diagnostics.SideRayCostTicks += Stopwatch.GetTimestamp() - phaseStart;
                        diagnostics.SideRayCostSamples += landscapingCost.RaySampleCount;
                        if (!string.IsNullOrEmpty(sideRayRejection))
                        { diagnostics.SideRayCostRejections++; Reject(rejections, sideRayRejection); continue; }
                        diagnostics.PropCleanupChecks++;
                        phaseStart = Stopwatch.GetTimestamp();
                        float propCleanupCost = CalculateGeneratedPropCleanupCost(
                            snapshot, origin, profile, GetGeneratedWorkOperation(handoffOperation),
                            out string propCleanupRejection, out _, out _);
                        diagnostics.PropCleanupTicks += Stopwatch.GetTimestamp() - phaseStart;
                        if (!string.IsNullOrEmpty(propCleanupRejection))
                        { diagnostics.PropCleanupRejections++; Reject(rejections, propCleanupRejection); continue; }
                        if (propCleanupCost > 0f) diagnostics.PropCleanupHits++;
                        float cost = currentCost + Manhattan(current.Position, next.CostPosition)
                            + generatedEntryCost + propCleanupCost;
                        diagnostics.GeneratedRelaxations++;
                        Relax(snapshot, current, next, cost,
                            distance, previous, generatedHistory, queue,
                            useAStarHeuristic, goalIndex, diagnostics,
                            generatedDisturbedRayTiles: landscapingCost.DisturbedRayTiles,
                            generatedRayHeightConstraints:
                                landscapingCost.RayHeightConstraints);
                    }
                }
            }
        }

        internal static bool IsGeneratedProfileFeasible(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile,
            AccessSearchNode predecessor,
            Tile2i direction,
            out string reason)
        {
            if (snapshot.IsProfileBlockedByProjectedDesignationHeight(origin, profile))
            {
                reason = "ProjectedDesignationHeight";
                return false;
            }
            bool useDirectionalDurability = predecessor.Mode != AccessSearchMode.Existing
                && predecessor.Mode != AccessSearchMode.Ground;
            return useDirectionalDurability
                ? snapshot.IsCandidateProfileFeasibleFromValidatedPredecessor(
                    origin, profile, predecessor.Position, direction, out reason)
                : snapshot.IsCandidateProfileFeasible(origin, profile, out reason);
        }

        private static bool IsCompatibleWithPathHistory(
            Tile2i nextOrigin,
            AccessHeightProfile nextProfile,
            AccessSearchNode current,
            Dictionary<AccessSearchNode, GeneratedPathHistory> generatedHistory,
            out string reason)
        {
            if (!generatedHistory.TryGetValue(current, out GeneratedPathHistory history))
                history = GeneratedPathHistory.Empty;
            Tile2i? allowedEdgeNeighbor = !current.IsGround
                && current.Mode != AccessSearchMode.Existing
                    ? current.Position
                    : (Tile2i?)null;
            return IsCompatibleWithPathHistory(
                nextOrigin, nextProfile, history, allowedEdgeNeighbor, out reason);
        }

        private static bool IsGeneratedCenterWithinUsefulHeightEnvelope(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile,
            AccessSearchDiagnostics diagnostics,
            out string rejection)
        {
            AccessUsefulHeightEnvelope? envelope = snapshot.UsefulHeightEnvelope;
            if (envelope == null)
            {
                rejection = string.Empty;
                return true;
            }

            diagnostics.HeightEnvelopeChecks++;
            Tile2i center = origin + new RelTile2i(2, 2);
            if (!envelope.TryGetBand(center, out int lowerHeight32, out int upperHeight32))
            {
                // A partial snapshot must never turn the experimental gate into
                // an unexpected hard rejection.
                diagnostics.HeightEnvelopeMissingSamples++;
                rejection = string.Empty;
                return true;
            }

            int centerHeight32 = checked(profile.Center2 * 16);
            if (centerHeight32 > upperHeight32)
            {
                diagnostics.HeightEnvelopeAboveRejections++;
                rejection = "HeightEnvelopeAbove";
                return false;
            }
            if (centerHeight32 < lowerHeight32)
            {
                diagnostics.HeightEnvelopeBelowRejections++;
                rejection = "HeightEnvelopeBelow";
                return false;
            }

            rejection = string.Empty;
            return true;
        }

        private static bool IsCompatibleWithPathHistory(
            Tile2i nextOrigin,
            AccessHeightProfile nextProfile,
            GeneratedPathHistory history,
            Tile2i? allowedEdgeNeighbor,
            out string reason)
        {
            if (history.ContainsOrigin(nextOrigin))
            {
                reason = "PathSelfContact";
                return false;
            }

            if (history.HasEdgeNeighborExcept(nextOrigin, allowedEdgeNeighbor))
            {
                reason = "PathAdjacentSelfContact";
                return false;
            }

            if (history.IsProfileBlockedByRayEnvelope(
                    nextOrigin, nextProfile,
                    out AccessSideRayOperation blockingOperation))
            {
                reason = blockingOperation == AccessSideRayOperation.Cut
                    ? "PathRayCutCeiling"
                    : "PathRayFillFloor";
                return false;
            }

            bool mismatch = false;
            nextProfile.AddWorldCorners(nextOrigin, (corner, height2) =>
            {
                if (history.TryGetCornerHeight(corner, out int existingHeight2)
                    && existingHeight2 != height2)
                    mismatch = true;
            });
            reason = mismatch ? "PathSelfContact" : string.Empty;
            return !mismatch;
        }

        private static AccessSearchMode GetRisingMode(Tile2i direction)
        {
            if (direction.X > 0) return AccessSearchMode.XPositive;
            if (direction.X < 0) return AccessSearchMode.XNegative;
            if (direction.Y > 0) return AccessSearchMode.YPositive;
            if (direction.Y < 0) return AccessSearchMode.YNegative;
            return AccessSearchMode.Flat;
        }

        private static IEnumerable<Tile2i> CandidateOriginsAtGroundTile(Tile2i tile)
        {
            var seen = new HashSet<Tile2i>();
            int baseX = tile.X & -4;
            int baseY = tile.Y & -4;
            Tile2i[] candidates =
            {
                new Tile2i(baseX, baseY), new Tile2i(baseX - 4, baseY),
                new Tile2i(baseX, baseY - 4), new Tile2i(baseX - 4, baseY - 4),
                new Tile2i(tile.X - 2, tile.Y - 2)
            };
            foreach (Tile2i candidate in candidates)
                if ((candidate.X & 3) == 0 && (candidate.Y & 3) == 0 && seen.Add(candidate))
                    yield return candidate;
        }

        private static List<AccessHandoffSpanCell> BuildRecentStraightGeneratedSpan(
            AccessSearchSnapshot snapshot,
            AccessSearchNode current,
            Dictionary<AccessSearchNode, AccessSearchNode> previous,
            int maxLength)
        {
            var reversed = new List<AccessHandoffSpanCell>(Math.Max(1, maxLength));
            if (current.IsGround || current.Mode == AccessSearchMode.Existing
                || !TryGetProfile(snapshot, current, out AccessHeightProfile currentProfile))
                return reversed;

            Tile2i direction = current.EntryDirection;
            if (!IsOriginStep(direction))
                return reversed;
            AccessSearchNode cursor = current;
            AccessHeightProfile cursorProfile = currentProfile;
            while (reversed.Count < maxLength)
            {
                reversed.Add(new AccessHandoffSpanCell(
                    cursor.Position, cursorProfile, cursor.EntryDirection));
                if (!previous.TryGetValue(cursor, out AccessSearchNode parent)
                    || parent.IsGround
                    || parent.Mode == AccessSearchMode.Existing
                    || parent.EntryDirection != direction
                    || cursor.Position != new Tile2i(
                        parent.Position.X + direction.X,
                        parent.Position.Y + direction.Y)
                    || !TryGetProfile(snapshot, parent, out cursorProfile))
                    break;
                cursor = parent;
            }
            reversed.Reverse();
            return reversed;
        }

        private static bool IsOriginStep(Tile2i direction)
            => (Math.Abs(direction.X) == 4 && direction.Y == 0)
                || (Math.Abs(direction.Y) == 4 && direction.X == 0);

        private static IEnumerable<AccessGroundHandoff> GetHandoffs(
            AccessSearchSnapshot snapshot, Tile2i origin, AccessHeightProfile profile,
            Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile)
        {
            var emitted = new HashSet<Tile2i>();
            if (snapshot.HasWorkableHandoffEvaluator)
            {
                foreach (AccessGroundHandoff handoff in snapshot.GetWorkableHandoffs(
                    origin, profile, predecessorOrigin, predecessorProfile))
                {
                    Tile2i corridorDirection = new Tile2i(
                        predecessorOrigin.X - origin.X,
                        predecessorOrigin.Y - origin.Y);
                    if (IsV1HandoffLaneEligible(origin, handoff.Tile, corridorDirection)
                        && emitted.Add(handoff.Tile))
                        yield return handoff;
                }
                yield break;
            }

            Tile2i center = origin + new RelTile2i(2, 2);
            Tile2i[] corners =
            {
                origin, origin + new RelTile2i(4, 0), origin + new RelTile2i(4, 4), origin + new RelTile2i(0, 4)
            };
            int[] heights = { profile.Nw2, profile.Ne2, profile.Se2, profile.Sw2 };
            bool centerMatches = MatchesGround(snapshot, center, profile.Center2)
                && snapshot.IsGroundOrCleanupNode(center);
            var matchingCorners = new bool[corners.Length];
            int matchingCornerCount = 0;
            for (int i = 0; i < corners.Length; i++)
            {
                matchingCorners[i] = MatchesGround(snapshot, corners[i], heights[i])
                    && snapshot.IsGroundOrCleanupNode(corners[i]);
                if (matchingCorners[i]) matchingCornerCount++;
            }
            if (!centerMatches && matchingCornerCount < 2) yield break;

            if (centerMatches)
            {
                if (emitted.Add(center))
                    yield return new AccessGroundHandoff(center, AccessHandoffOperation.None);
            }
            for (int i = 0; i < corners.Length; i++)
                if (matchingCorners[i])
                {
                    if (emitted.Add(corners[i]))
                        yield return new AccessGroundHandoff(corners[i], AccessHandoffOperation.None);
                }
        }

        internal static bool TryGetGroundToGeneratedHandoff(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i groundTile,
            out AccessHandoffOperation operation,
            out Tile2i entryDirection)
        {
            foreach (Tile2i direction in s_originDirections)
            {
                if (snapshot.HasWorkableHandoffEvaluator
                    && !IsV1HandoffLaneEligible(origin, groundTile, direction))
                    continue;
                Tile2i connectedPredecessor = new Tile2i(
                    origin.X + direction.X, origin.Y + direction.Y);
                foreach (AccessGroundHandoff candidate in GetHandoffs(
                    snapshot, origin, profile, connectedPredecessor, profile))
                {
                    if (candidate.Tile != groundTile) continue;
                    operation = candidate.Operation;
                    entryDirection = new Tile2i(-direction.X, -direction.Y);
                    return true;
                }
            }

            operation = AccessHandoffOperation.None;
            entryDirection = default;
            return false;
        }

        internal static bool IsV1HandoffLaneEligible(
            Tile2i origin,
            Tile2i groundTile,
            Tile2i corridorDirection)
        {
            if (corridorDirection.X != 0)
            {
                int lane = PositiveMod4(groundTile.Y - origin.Y);
                return lane == 1 || lane == 2;
            }
            if (corridorDirection.Y != 0)
            {
                int lane = PositiveMod4(groundTile.X - origin.X);
                return lane == 1 || lane == 2;
            }
            return false;
        }

        private static int PositiveMod4(int value)
        {
            int result = value % 4;
            return result < 0 ? result + 4 : result;
        }

        private static bool ContainsHandoffTile(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i groundTile)
            => TryGetGroundToGeneratedHandoff(
                snapshot, origin, profile, groundTile, out _, out _);

        internal static bool IsGroundToGeneratedContinuation(
            AccessSearchSnapshot snapshot,
            Tile2i handoffOrigin,
            AccessHeightProfile handoffProfile,
            Tile2i groundTile,
            AccessHandoffOperation operation,
            Tile2i nextGeneratedOrigin)
        {
            foreach (AccessGroundHandoff candidate in GetHandoffs(
                snapshot, handoffOrigin, handoffProfile,
                nextGeneratedOrigin, handoffProfile))
            {
                if (candidate.Tile == groundTile
                    && candidate.Operation == operation)
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool ContainsHandoff(AccessSearchSnapshot snapshot, Tile2i origin,
            AccessHeightProfile profile, Tile2i predecessorOrigin,
            AccessHeightProfile predecessorProfile, Tile2i tile,
            AccessHandoffOperation operation)
        {
            foreach (AccessGroundHandoff candidate in GetHandoffs(
                snapshot, origin, profile, predecessorOrigin, predecessorProfile))
                if (candidate.Tile == tile && candidate.Operation == operation) return true;
            return false;
        }

        private static bool MatchesGround(AccessSearchSnapshot snapshot, Tile2i tile, int targetHeight2)
        {
            if (!snapshot.TryGetGroundHeight2(tile, out int groundHeight2)) return false;
            return targetHeight2 == groundHeight2;
        }

        private static string FormatProfile(AccessHeightProfile profile)
            => $"[{profile.Nw2},{profile.Ne2},{profile.Se2},{profile.Sw2}]";

        internal static bool TrySolveSuccessor(AccessHeightProfile current, Tile2i direction,
            AccessSearchMode mode, out AccessHeightProfile successor)
        {
            current.GetEdge(direction, out int currentFirst2, out int currentSecond2);
            int templateCenter2 = mode == AccessSearchMode.Flat ? 0 : 1;
            if (!AccessHeightProfile.TryForMode(mode, templateCenter2, out AccessHeightProfile template))
            { successor = default; return false; }
            template.GetEdge(new Tile2i(-direction.X, -direction.Y), out int templateFirst2, out int templateSecond2);
            int firstOffset2 = templateFirst2 - templateCenter2;
            int secondOffset2 = templateSecond2 - templateCenter2;
            int center2 = currentFirst2 - firstOffset2;
            if (currentSecond2 - secondOffset2 != center2
                || !AccessHeightProfile.TryForMode(mode, center2, out successor))
            { successor = default; return false; }
            return true;
        }

        private static bool IsKnownDistanceNoWorse(
            Dictionary<AccessSearchNode, float> distance,
            AccessSearchNode node,
            float lowerBoundCost)
            => distance.TryGetValue(node, out float existing)
                && existing <= lowerBoundCost + 0.0001f;

        internal static bool EdgesMatch(AccessHeightProfile current, AccessHeightProfile next, Tile2i direction)
        {
            current.GetEdge(direction, out int a, out int b);
            next.GetEdge(new Tile2i(-direction.X, -direction.Y), out int c, out int d);
            return a == c && b == d;
        }

        internal static bool TryGetProfile(AccessSearchSnapshot snapshot, AccessSearchNode node, out AccessHeightProfile profile)
        {
            if (node.Mode == AccessSearchMode.Existing)
                return snapshot.TryGetFixedProfile(node.Position, out profile);
            return AccessHeightProfile.TryForMode(node.Mode, node.Height2, out profile);
        }

        private static void Relax(AccessSearchSnapshot snapshot, AccessSearchNode current, AccessSearchNode next,
            float nextCost, Dictionary<AccessSearchNode, float> distance,
            Dictionary<AccessSearchNode, AccessSearchNode> previous,
            Dictionary<AccessSearchNode, GeneratedPathHistory> generatedHistory,
            MinQueue queue,
            bool useAStarHeuristic, HeightAwareGoalIndex goalIndex,
            AccessSearchDiagnostics diagnostics,
            bool hasCurrent = true,
            IReadOnlyList<Tile2i>? generatedDisturbedRayTiles = null,
            IReadOnlyList<AccessRayHeightConstraint>? generatedRayHeightConstraints = null,
            GeneratedPathHistory? nextHistoryOverride = null)
        {
            if (distance.TryGetValue(next, out float existing) && existing <= nextCost + 0.0001f) return;
            diagnostics.QueueRelaxations++;
            distance[next] = nextCost;
            if (hasCurrent)
            {
                previous[next] = current;
                generatedHistory[next] = nextHistoryOverride
                    ?? BuildGeneratedHistory(
                        snapshot, current, next, generatedHistory,
                        generatedDisturbedRayTiles,
                        generatedRayHeightConstraints, diagnostics);
            }
            else
            {
                generatedHistory[next] = GeneratedPathHistory.Empty;
            }
            float heuristic = GetHeuristic(
                next, snapshot, useAStarHeuristic, goalIndex);
            queue.Push(new QueueEntry(
                next, nextCost, nextCost + heuristic, heuristic));
        }

        private static GeneratedPathHistory BuildGeneratedHistory(
            AccessSearchSnapshot snapshot,
            AccessSearchNode current,
            AccessSearchNode next,
            Dictionary<AccessSearchNode, GeneratedPathHistory> generatedHistory,
            IReadOnlyList<Tile2i>? generatedDisturbedRayTiles,
            IReadOnlyList<AccessRayHeightConstraint>? generatedRayHeightConstraints,
            AccessSearchDiagnostics diagnostics)
        {
            if (!generatedHistory.TryGetValue(current, out GeneratedPathHistory currentHistory))
                currentHistory = GeneratedPathHistory.Empty;
            if (next.IsGround || next.Mode == AccessSearchMode.Existing)
                return currentHistory;
            if (!TryGetProfile(snapshot, next, out AccessHeightProfile profile))
                return currentHistory;
            IReadOnlyList<Tile2i> disturbedRayTiles;
            IReadOnlyList<AccessRayHeightConstraint> rayHeightConstraints;
            if (generatedDisturbedRayTiles != null)
            {
                diagnostics.GeneratedHistoryCostReuses++;
                disturbedRayTiles = generatedDisturbedRayTiles;
                rayHeightConstraints = generatedRayHeightConstraints
                    ?? Array.Empty<AccessRayHeightConstraint>();
            }
            else
            {
                diagnostics.GeneratedHistoryCostRecalculations++;
                CalculateGeneratedEntryCost(
                    snapshot,
                    next.Position,
                    profile,
                    next.EntryDirection,
                    next.HandoffOperation,
                    out AccessLandscapingCost landscapingCost,
                    out _,
                    out _,
                    diagnostics);
                disturbedRayTiles = landscapingCost.DisturbedRayTiles;
                rayHeightConstraints = landscapingCost.RayHeightConstraints;
            }
            GeneratedPathHistory nextHistory = currentHistory.WithGenerated(
                next.Position,
                profile,
                disturbedRayTiles,
                rayHeightConstraints: rayHeightConstraints);
            diagnostics.GeneratedHistoryNodesCreated++;
            if (nextHistory.Depth > diagnostics.GeneratedHistoryMaxDepth)
                diagnostics.GeneratedHistoryMaxDepth = nextHistory.Depth;
            return nextHistory;
        }

        private static float GetHeuristic(
            AccessSearchNode node,
            AccessSearchSnapshot snapshot,
            bool useAStarHeuristic,
            HeightAwareGoalIndex goalIndex)
        {
            if (!snapshot.UseAStar || !useAStarHeuristic)
                return 0f;
            return goalIndex.GetLowerBound(
                node.CostPosition, node.Height2, node.IsGround);
        }

        internal sealed class HeightAwareGoalIndex
        {
            public static readonly HeightAwareGoalIndex Empty =
                new HeightAwareGoalIndex(default, 0, 0, Array.Empty<GoalHeightBand>());

            private readonly Tile2i m_boundsMin;
            private readonly int m_width;
            private readonly int m_height;
            private readonly GoalHeightBand[] m_bands;
            private readonly AccessV1GroundGoalDistance? m_groundGoalDistance;

            private HeightAwareGoalIndex(
                Tile2i boundsMin,
                int width,
                int height,
                GoalHeightBand[] bands,
                AccessV1GroundGoalDistance? groundGoalDistance = null)
            {
                m_boundsMin = boundsMin;
                m_width = width;
                m_height = height;
                m_bands = bands;
                m_groundGoalDistance = groundGoalDistance;
            }

            public static HeightAwareGoalIndex Build(
                AccessSearchSnapshot snapshot,
                IReadOnlyDictionary<int, List<Tile2i>> goalsByHeight2,
                bool includeGroundGoals = false)
            {
                var bands = new List<GoalHeightBand>(goalsByHeight2.Count);
                foreach (KeyValuePair<int, List<Tile2i>> pair in goalsByHeight2)
                {
                    if (pair.Value.Count == 0) continue;
                    bands.Add(new GoalHeightBand(
                        pair.Key,
                        AccessSearchSnapshot.BuildGoalDistance(
                            snapshot.GoalDistanceMin,
                            snapshot.GoalDistanceMax,
                            new HashSet<Tile2i>(pair.Value))));
                }
                return new HeightAwareGoalIndex(
                    snapshot.GoalDistanceMin,
                    snapshot.GoalDistanceWidth,
                    snapshot.GoalDistanceHeight,
                    bands.ToArray(),
                    includeGroundGoals ? snapshot.V1GroundGoalDistance : null);
            }

            public float GetLowerBound(
                Tile2i tile, int height2, bool isGround = false)
            {
                if (isGround && m_groundGoalDistance != null
                    && m_groundGoalDistance.TryGetDistance(tile, out float groundDistance))
                    return groundDistance;
                if (m_bands.Length == 0) return 0f;
                int x = tile.X - m_boundsMin.X;
                int y = tile.Y - m_boundsMin.Y;
                if (x < 0 || x >= m_width || y < 0 || y >= m_height)
                    return 0f;

                int index = y * m_width + x;
                float best = float.PositiveInfinity;
                for (int i = 0; i < m_bands.Length; i++)
                {
                    float horizontalDistance = m_bands[i].Distances[index];
                    if (horizontalDistance < 0) continue;
                    float lowerBound = Math.Max(
                        horizontalDistance,
                        Math.Abs(height2 - m_bands[i].Height2));
                    if (lowerBound < best) best = lowerBound;
                }
                return float.IsPositiveInfinity(best) ? 0f : best;
            }

            private readonly struct GoalHeightBand
            {
                public int Height2 { get; }
                public float[] Distances { get; }

                public GoalHeightBand(int height2, float[] distances)
                {
                    Height2 = height2;
                    Distances = distances;
                }
            }
        }

        private static float CalculateGeneratedEntryCost(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i entryDirection,
            AccessHandoffOperation handoffOperation,
            out AccessLandscapingCost landscapingCost,
            out float fixedCost,
            out string rejectionReason,
            AccessSearchDiagnostics? diagnostics = null)
        {
            AccessHandoffOperation workOperation =
                handoffOperation == AccessHandoffOperation.None
                    ? AccessHandoffOperation.Leveling
                    : handoffOperation;
            float directWorkCost = EstimateDirectWorkCost(
                snapshot, origin, profile, workOperation);
            fixedCost = GeneratedVFixedOverhead;
            rejectionReason = string.Empty;
            if (entryDirection == Tile2i.Zero)
            {
                landscapingCost = new AccessLandscapingCost(directWorkCost);
                return snapshot.LandscapingCostDistanceScale
                    * GetWeightedLandscapingCost(landscapingCost) + fixedCost;
            }
            if (!TryGetExitRayGeometry(
                    origin, profile, entryDirection,
                    out Tile2i leftCorner, out float leftHeight,
                    out Tile2i leftDirection,
                    out Tile2i rightCorner, out float rightHeight,
                    out Tile2i rightDirection))
            {
                landscapingCost = new AccessLandscapingCost(
                    directWorkCost, fatalReason: "SideRayInvalidEntryDirection");
                rejectionReason = landscapingCost.FatalReason!;
                return float.MaxValue;
            }

            Tile2i predecessorOrigin = new Tile2i(
                origin.X - entryDirection.X,
                origin.Y - entryDirection.Y);
            Tile2i? connectedFixedDesignationOrigin =
                snapshot.TryGetFixedProfile(predecessorOrigin, out _)
                    ? predecessorOrigin
                    : (Tile2i?)null;

            AccessSideRayResult left = ScoreExitCorner(
                snapshot, leftCorner, leftHeight, leftDirection, workOperation,
                diagnostics, connectedFixedDesignationOrigin);
            if (left.IsFatal)
            {
                landscapingCost = new AccessLandscapingCost(
                    directWorkCost,
                    raySampleCount: left.SampleCount,
                    fatalReason: left.FatalReason);
                rejectionReason = left.FatalReason!;
                return float.MaxValue;
            }
            AccessSideRayResult right = ScoreExitCorner(
                snapshot, rightCorner, rightHeight, rightDirection, workOperation,
                diagnostics, connectedFixedDesignationOrigin);
            if (right.IsFatal)
            {
                landscapingCost = new AccessLandscapingCost(
                    directWorkCost,
                    left.IntegratedCost,
                    unresolvedPenalty: left.UnresolvedPenalty,
                    raySampleCount: left.SampleCount + right.SampleCount,
                    fatalReason: right.FatalReason);
                rejectionReason = right.FatalReason!;
                return float.MaxValue;
            }

            landscapingCost = new AccessLandscapingCost(
                directWorkCost,
                left.IntegratedCost,
                right.IntegratedCost,
                left.UnresolvedPenalty + right.UnresolvedPenalty,
                left.SampleCount + right.SampleCount,
                disturbedRayTiles: BuildDisturbedRayTiles(
                    leftCorner, leftDirection, left.DisturbedDistance,
                    rightCorner, rightDirection, right.DisturbedDistance,
                    snapshot.VehicleClearanceRadius),
                rayHeightConstraints: MergeRayHeightConstraints(
                    BuildRayHeightConstraints(
                        snapshot, leftCorner, leftHeight, leftDirection,
                        left, workOperation, snapshot.VehicleClearanceRadius),
                    BuildRayHeightConstraints(
                        snapshot, rightCorner, rightHeight, rightDirection,
                        right, workOperation, snapshot.VehicleClearanceRadius)));
            return snapshot.LandscapingCostDistanceScale
                * GetWeightedLandscapingCost(landscapingCost)
                + fixedCost;
        }

        private static IReadOnlyList<Tile2i> BuildDisturbedRayTiles(
            Tile2i leftCorner,
            Tile2i leftDirection,
            int leftDistance,
            Tile2i rightCorner,
            Tile2i rightDirection,
            int rightDistance,
            int clearanceRadius)
        {
            var tiles = new HashSet<Tile2i>();
            Add(leftCorner, leftDirection, leftDistance);
            Add(rightCorner, rightDirection, rightDistance);
            return new List<Tile2i>(tiles).ToArray();

            void Add(Tile2i corner, Tile2i direction, int distance)
            {
                for (int step = 1; step <= distance; step++)
                {
                    Tile2i disturbed = new Tile2i(
                        corner.X + direction.X * step,
                        corner.Y + direction.Y * step);
                    for (int dx = -clearanceRadius; dx <= clearanceRadius; dx++)
                        for (int dy = -clearanceRadius; dy <= clearanceRadius; dy++)
                            tiles.Add(new Tile2i(disturbed.X + dx, disturbed.Y + dy));
                }
            }
        }

        private static IReadOnlyList<AccessRayHeightConstraint> BuildRayHeightConstraints(
            AccessSearchSnapshot snapshot,
            Tile2i corner,
            float plannedHeight,
            Tile2i direction,
            AccessSideRayResult ray,
            AccessHandoffOperation workOperation,
            int clearanceRadius)
        {
            AccessTerrainSampleKind cornerKind =
                snapshot.GetSideRayTerrainSample(corner, out float terrainHeight);
            if (ray.DisturbedDistance <= 0
                || cornerKind == AccessTerrainSampleKind.MissingSnapshot
                || cornerKind == AccessTerrainSampleKind.PhysicalMapEdge)
                return Array.Empty<AccessRayHeightConstraint>();

            const float epsilon = 0.0001f;
            AccessSideRayOperation operation = plannedHeight > terrainHeight + epsilon
                ? AccessSideRayOperation.Fill
                : plannedHeight < terrainHeight - epsilon
                    ? AccessSideRayOperation.Cut
                    : AccessSideRayOperation.None;
            if ((operation == AccessSideRayOperation.Fill
                    && workOperation == AccessHandoffOperation.Mining)
                || (operation == AccessSideRayOperation.Cut
                    && workOperation == AccessHandoffOperation.Dumping)
                || operation == AccessSideRayOperation.None)
                return Array.Empty<AccessRayHeightConstraint>();

            float materialSlope;
            if (operation == AccessSideRayOperation.Fill)
                materialSlope = snapshot.DumpingMaterialSlope;
            else if (!snapshot.TryGetMiningMaterialSlope(
                    corner, plannedHeight, out materialSlope, out _, out _))
                return Array.Empty<AccessRayHeightConstraint>();
            if (materialSlope <= 0f)
                return Array.Empty<AccessRayHeightConstraint>();

            var result = new List<AccessRayHeightConstraint>();
            for (int distance = 1; distance <= ray.DisturbedDistance; distance++)
            {
                Tile2i rayTile = new Tile2i(
                    corner.X + direction.X * distance,
                    corner.Y + direction.Y * distance);
                float projectedHeight = operation == AccessSideRayOperation.Fill
                    ? plannedHeight - distance * materialSlope
                    : plannedHeight + distance * materialSlope;
                for (int dx = -clearanceRadius; dx <= clearanceRadius; dx++)
                    for (int dy = -clearanceRadius; dy <= clearanceRadius; dy++)
                        result.Add(new AccessRayHeightConstraint(
                            new Tile2i(rayTile.X + dx, rayTile.Y + dy),
                            operation, projectedHeight));
            }
            return result;
        }

        private static IReadOnlyList<AccessRayHeightConstraint> MergeRayHeightConstraints(
            IReadOnlyList<AccessRayHeightConstraint> first,
            IReadOnlyList<AccessRayHeightConstraint> second)
        {
            if (first.Count == 0) return second;
            if (second.Count == 0) return first;
            var result = new AccessRayHeightConstraint[first.Count + second.Count];
            for (int index = 0; index < first.Count; index++)
                result[index] = first[index];
            for (int index = 0; index < second.Count; index++)
                result[first.Count + index] = second[index];
            return result;
        }

        private static float GetProfileCornerHeight(
            Tile2i origin, AccessHeightProfile profile, Tile2i corner)
        {
            if (corner == origin) return profile.Nw2 / 2f;
            if (corner == origin + new RelTile2i(4, 0)) return profile.Ne2 / 2f;
            if (corner == origin + new RelTile2i(4, 4)) return profile.Se2 / 2f;
            if (corner == origin + new RelTile2i(0, 4)) return profile.Sw2 / 2f;
            return profile.Center2 / 2f;
        }

        private static float GetWeightedLandscapingCost(
            AccessLandscapingCost landscapingCost)
            => DirectWorkWeight * landscapingCost.DirectWorkCost
                + SideRayWeight
                    * (landscapingCost.LeftSideRayCost
                        + landscapingCost.RightSideRayCost
                        + landscapingCost.UnresolvedPenalty);

        private static AccessSideRayResult ScoreExitCorner(
            AccessSearchSnapshot snapshot,
            Tile2i corner,
            float plannedHeight,
            Tile2i lateralDirection,
            AccessHandoffOperation workOperation,
            AccessSearchDiagnostics? diagnostics = null,
            Tile2i? exemptDesignationOrigin = null)
        {
            int plannedHeight2 = (int)Math.Round(plannedHeight * 2f);
            var cacheKey = new AccessSideRayCacheKey(
                corner, plannedHeight2, lateralDirection, workOperation,
                exemptDesignationOrigin);
            if (snapshot.TryGetCachedSideRay(cacheKey, out AccessSideRayResult cached))
            {
                if (diagnostics != null) diagnostics.SideRayCacheHits++;
                return cached;
            }
            if (diagnostics != null) diagnostics.SideRayCacheMisses++;

            AccessSideRayResult result;
            AccessTerrainSampleKind sampleKind =
                snapshot.GetSideRayTerrainSample(corner, out float terrainHeight);
            if (sampleKind == AccessTerrainSampleKind.MissingSnapshot)
                result = new AccessSideRayResult(
                    0f, 0f, 0, false, false, "SideRaySnapshotMissing");
            else if (sampleKind == AccessTerrainSampleKind.PhysicalMapEdge)
                result = new AccessSideRayResult(
                    0f, 0f, 0, false, false, "SideRayFootprintMapEdge");
            else
            {
                const float epsilon = 0.0001f;
                AccessSideRayOperation operation = plannedHeight > terrainHeight + epsilon
                    ? AccessSideRayOperation.Fill
                    : plannedHeight < terrainHeight - epsilon
                        ? AccessSideRayOperation.Cut
                        : AccessSideRayOperation.None;
                if ((operation == AccessSideRayOperation.Fill
                        && workOperation == AccessHandoffOperation.Mining)
                    || (operation == AccessSideRayOperation.Cut
                        && workOperation == AccessHandoffOperation.Dumping))
                    operation = AccessSideRayOperation.None;
                if (operation == AccessSideRayOperation.None)
                {
                    // A ray that meets terrain at its origin has zero work and
                    // zero cost, but its configured post-termination safety
                    // margin still has to protect the outward corridor. Access
                    // leveling is cut-safe here because another part of the
                    // same profile can be below terrain even when this exact
                    // corner already lies on terrain.
                    AccessSideRayOperation safetyOperation =
                        workOperation == AccessHandoffOperation.Dumping
                            ? AccessSideRayOperation.Fill
                            : AccessSideRayOperation.Cut;
                    result = AccessSideRayCost.ScoreZeroLengthBuffer(
                        snapshot,
                        corner,
                        lateralDirection,
                        plannedHeight,
                        safetyOperation,
                        AutoTerrainDesignationsMod.AccessRayEndBuffer,
                        exemptDesignationOrigin);
                }
                else
                {
                    float materialSlope;
                    if (operation == AccessSideRayOperation.Fill)
                    {
                        if (!snapshot.HasDumpingMaterial)
                        {
                            result = new AccessSideRayResult(
                                0f, 0f, 0, false, false,
                                "SideRayNoDumpingMaterial");
                            snapshot.CacheSideRay(cacheKey, result);
                            return result;
                        }
                        materialSlope = snapshot.DumpingMaterialSlope;
                    }
                    else if (!snapshot.TryGetMiningMaterialSlope(
                            corner, plannedHeight,
                            out materialSlope, out _, out _))
                    {
                        result = new AccessSideRayResult(
                            0f, 0f, 0, false, false, "SideRayMiningMaterialMissing");
                        snapshot.CacheSideRay(cacheKey, result);
                        return result;
                    }
                    result = AccessSideRayCost.Score(
                        snapshot,
                        corner,
                        lateralDirection,
                        plannedHeight,
                        operation,
                        materialSlope,
                        maxRayCost: AutoTerrainDesignationsMod.AccessRayMaxCost,
                        unresolvedPenalty: AutoTerrainDesignationsMod.AccessRayUnresolvedPenalty,
                        postTerminationSafetyMargin:
                            AutoTerrainDesignationsMod.AccessRayEndBuffer,
                        maxTraceDistance:
                            AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance,
                        exemptDesignationOrigin: exemptDesignationOrigin);
                }
            }
            snapshot.CacheSideRay(cacheKey, result);
            return result;
        }

        private static bool TryGetExitRayGeometry(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i entryDirection,
            out Tile2i leftCorner,
            out float leftHeight,
            out Tile2i leftDirection,
            out Tile2i rightCorner,
            out float rightHeight,
            out Tile2i rightDirection)
        {
            if (entryDirection.X > 0 && entryDirection.Y == 0)
            {
                leftCorner = origin + new RelTile2i(4, 0);
                leftHeight = profile.Ne2 / 2f;
                leftDirection = new Tile2i(0, -1);
                rightCorner = origin + new RelTile2i(4, 4);
                rightHeight = profile.Se2 / 2f;
                rightDirection = new Tile2i(0, 1);
                return true;
            }
            if (entryDirection.X < 0 && entryDirection.Y == 0)
            {
                leftCorner = origin;
                leftHeight = profile.Nw2 / 2f;
                leftDirection = new Tile2i(0, -1);
                rightCorner = origin + new RelTile2i(0, 4);
                rightHeight = profile.Sw2 / 2f;
                rightDirection = new Tile2i(0, 1);
                return true;
            }
            if (entryDirection.Y > 0 && entryDirection.X == 0)
            {
                leftCorner = origin + new RelTile2i(0, 4);
                leftHeight = profile.Sw2 / 2f;
                leftDirection = new Tile2i(-1, 0);
                rightCorner = origin + new RelTile2i(4, 4);
                rightHeight = profile.Se2 / 2f;
                rightDirection = new Tile2i(1, 0);
                return true;
            }
            if (entryDirection.Y < 0 && entryDirection.X == 0)
            {
                leftCorner = origin;
                leftHeight = profile.Nw2 / 2f;
                leftDirection = new Tile2i(-1, 0);
                rightCorner = origin + new RelTile2i(4, 0);
                rightHeight = profile.Ne2 / 2f;
                rightDirection = new Tile2i(1, 0);
                return true;
            }

            leftCorner = default;
            leftHeight = 0f;
            leftDirection = default;
            rightCorner = default;
            rightHeight = 0f;
            rightDirection = default;
            return false;
        }

        private static AccessSideRayResult ScoreTurnOuterCorner(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i oldDirection,
            Tile2i newDirection,
            AccessHandoffOperation workOperation,
            AccessSearchDiagnostics? diagnostics,
            out Tile2i corner,
            out Tile2i rayDirection)
        {
            corner = default;
            rayDirection = default;
            Tile2i oldUnit = UnitDirection(oldDirection);
            Tile2i newUnit = UnitDirection(newDirection);
            if (oldUnit == Tile2i.Zero || newUnit == Tile2i.Zero
                || oldUnit.X * newUnit.X + oldUnit.Y * newUnit.Y != 0
                || !TryGetExitRayGeometry(
                    origin, profile, oldDirection,
                    out Tile2i leftCorner, out float leftHeight, out Tile2i leftDirection,
                    out Tile2i rightCorner, out float rightHeight, out Tile2i rightDirection))
                return new AccessSideRayResult(0f, 0f, 0, false, false);

            Tile2i outsideTurnDirection = new Tile2i(-newUnit.X, -newUnit.Y);
            float plannedHeight;
            if (leftDirection == outsideTurnDirection)
            {
                corner = leftCorner;
                plannedHeight = leftHeight;
            }
            else if (rightDirection == outsideTurnDirection)
            {
                corner = rightCorner;
                plannedHeight = rightHeight;
            }
            else
                return new AccessSideRayResult(
                    0f, 0f, 0, false, false, "SideRayInvalidTurnGeometry");

            rayDirection = oldUnit;
            return ScoreExitCorner(
                snapshot, corner, plannedHeight, rayDirection,
                workOperation, diagnostics);
        }

        private static Tile2i UnitDirection(Tile2i direction)
            => new Tile2i(Math.Sign(direction.X), Math.Sign(direction.Y));

        private static IReadOnlyList<Tile2i> MergeDisturbedRayTiles(
            IReadOnlyList<Tile2i> existing,
            Tile2i corner,
            Tile2i direction,
            int distance,
            int snapshotClearanceRadius)
        {
            if (distance <= 0 || direction == Tile2i.Zero)
                return existing;
            var merged = new HashSet<Tile2i>(existing);
            for (int step = 1; step <= distance; step++)
            {
                Tile2i disturbed = new Tile2i(
                    corner.X + direction.X * step,
                    corner.Y + direction.Y * step);
                for (int dx = -snapshotClearanceRadius; dx <= snapshotClearanceRadius; dx++)
                    for (int dy = -snapshotClearanceRadius; dy <= snapshotClearanceRadius; dy++)
                        merged.Add(new Tile2i(disturbed.X + dx, disturbed.Y + dy));
            }
            return new List<Tile2i>(merged).ToArray();
        }

        private static float EstimateDirectWorkCost(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile,
            AccessHandoffOperation operation)
        {
            float cost = 0f;
            AddCorner(0, 0);
            AddCorner(4, 0);
            AddCorner(4, 4);
            AddCorner(0, 4);
            return cost;

            void AddCorner(int localX, int localY)
            {
                Tile2i tile = origin + new RelTile2i(localX, localY);
                AccessTerrainSampleKind sampleKind =
                    snapshot.GetSideRayTerrainSample(tile, out float terrainHeight);
                if (sampleKind == AccessTerrainSampleKind.MissingSnapshot
                    || sampleKind == AccessTerrainSampleKind.PhysicalMapEdge)
                    return;

                float plannedHeight = profile.GetHeight2NumeratorAt(localX, localY) / 32f;
                float delta = plannedHeight - terrainHeight;
                float operationGap = operation == AccessHandoffOperation.Mining
                    ? Math.Max(0f, -delta)
                    : operation == AccessHandoffOperation.Dumping
                        ? Math.Max(0f, delta)
                        : Math.Abs(delta);

                // Each corner represents one quarter of the 4x4 designation footprint.
                // Four uniform one-level gaps therefore preserve the previous cost of 16.
                cost += 4f * operationGap;
            }
        }

        internal static AccessV2TransitionEvaluation EvaluateV2Transition(
            AccessSearchSnapshot snapshot,
            AccessV2BandState? current,
            AccessV2Transition transition,
            AccessV2History history,
            Tile2i? connectedFixedOrigin)
        {
            float traversalCost = current.HasValue
                ? Manhattan(
                    GetV2CanonicalCenter(current.Value),
                    GetV2CanonicalCenter(transition.Next))
                : 0f;

            bool exactTerrainBand = !V2BandHasTerrainDelta(
                snapshot, transition.Next);
            // Exact straight/turn successors are real zero-work G seams. Keep
            // one terminal V state so the ordinary handoff evaluator can prove
            // the width-two boundary, but prohibit any further V expansion.
            // Ground-originated exact bands are similarly dominated by staying
            // in G. Synthetic fixed-source companions remain the sole exception.
            if (!connectedFixedOrigin.HasValue
                && exactTerrainBand
                && transition.Kind != AccessV2TransitionKind.Strafe)
                return new AccessV2TransitionEvaluation(
                    true, string.Empty,
                    traversalCost, 0f, 0f,
                    requiresGroundTransition: true);

            // Every newly owned V origin must survive materialization. This is
            // the swept 2x2 rule for strafes and the width-two brush rule for
            // straight/turn deltas. A mixed exact/work delta otherwise leaves
            // either an illegal one-cell passage or a redundant appendix.
            if (!connectedFixedOrigin.HasValue
                && transition.Delta.Any(item =>
                    !V2ProfileHasTerrainDelta(
                        snapshot, item.Origin, item.Profile)))
                return AccessV2TransitionEvaluation.Reject(
                    transition.Kind == AccessV2TransitionKind.Strafe
                        ? "StrafeRequiresCompleteMaterializedDelta"
                        : "TransitionRequiresCompleteMaterializedDelta");

            var rayConstraints = new List<AccessRayHeightConstraint>();
            var cleanupKeys = new HashSet<string>(StringComparer.Ordinal);
            float directCost = 0f;
            float fixedCost = 0f;
            float rayCost = 0f;
            float cleanupCost = 0f;

            for (int index = 0; index < transition.Delta.Count; index++)
            {
                AccessV2OriginProfile item = transition.Delta[index];
                if (snapshot.IsProfileBlockedByProjectedDesignationHeight(
                        item.Origin, item.Profile))
                    return AccessV2TransitionEvaluation.Reject(
                        "ProjectedDesignationHeight");
                if (history.IsProfileBlockedByRayEnvelope(
                        item.Origin, item.Profile,
                        out AccessSideRayOperation blockingOperation))
                    return AccessV2TransitionEvaluation.Reject(
                        blockingOperation == AccessSideRayOperation.Cut
                            ? "PathRayCutCeiling"
                            : "PathRayFillFloor");
                if (!snapshot.IsCandidateProfileFeasible(
                        item.Origin, item.Profile, out string profileReason))
                    return AccessV2TransitionEvaluation.Reject(profileReason);

                directCost += snapshot.LandscapingCostDistanceScale
                    * DirectWorkWeight
                    * EstimateDirectWorkCost(
                        snapshot, item.Origin, item.Profile,
                        AccessHandoffOperation.Leveling);
                fixedCost += GeneratedVFixedOverhead;

                CalculateGeneratedPropCleanupCost(
                    snapshot, item.Origin, item.Profile,
                    AccessHandoffOperation.Leveling,
                    out string cleanupReason, out _, out _);
                if (!string.IsNullOrEmpty(cleanupReason))
                    return AccessV2TransitionEvaluation.Reject(cleanupReason);
                if (snapshot.TryGetPropCleanupInfo(
                        item.Origin, out AccessPropCleanupInfo cleanupInfo)
                    && cleanupInfo.IsEligibleWithinGeneratedV
                    && cleanupInfo.HasDenseDebrisCleanup)
                {
                    if (cleanupInfo.Samples.Count == 0)
                    {
                        AddCleanupKey(
                            $"cleanup-origin:{cleanupInfo.Origin.X},{cleanupInfo.Origin.Y}",
                            isTree: false);
                    }
                    else
                    {
                        for (int sampleIndex = 0;
                            sampleIndex < cleanupInfo.Samples.Count;
                            sampleIndex++)
                        {
                            AccessPropSample sample = cleanupInfo.Samples[sampleIndex];
                            if (sample.IsDenseDebris)
                                AddCleanupKey(sample.CleanupObjectKey, isTree: false);
                        }
                    }
                }
            }

            if (!TryAddV2ExteriorRays(
                    snapshot, current, transition,
                    connectedFixedOrigin,
                    rayConstraints, ref rayCost,
                    out string rayReason))
                return AccessV2TransitionEvaluation.Reject(rayReason);

            return new AccessV2TransitionEvaluation(
                true, string.Empty,
                traversalCost,
                directCost + fixedCost + rayCost,
                cleanupCost,
                rayConstraints,
                cleanupKeys,
                directCost,
                fixedCost,
                rayCost);

            void AddCleanupKey(string key, bool isTree)
            {
                if (history.ContainsCleanupKey(key)
                    || !cleanupKeys.Add(key))
                    return;
                cleanupCost += snapshot.LandscapingCostDistanceScale
                    * AccessPropCleanupPolicy.GetCleanupLandscapingCost(isTree);
            }
        }

        private static bool V2BandHasTerrainDelta(
            AccessSearchSnapshot snapshot,
            AccessV2BandState state)
        {
            for (int lane = 0; lane < 2; lane++)
            {
                Tile2i origin = state.GetLaneOrigin(lane);
                AccessHeightProfile profile = state.GetLane(lane).Profile;
                if (V2ProfileHasTerrainDelta(snapshot, origin, profile))
                    return true;
            }
            return false;
        }

        private static bool V2ProfileHasTerrainDelta(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile)
        {
            for (int y = 0; y <= 4; y++)
                for (int x = 0; x <= 4; x++)
                {
                    Tile2i tile = origin + new RelTile2i(x, y);
                    if (!snapshot.TryGetGroundHeight2(
                            tile, out int terrainHeight2)
                        || profile.GetHeight2NumeratorAt(x, y)
                            != terrainHeight2 * 16)
                        return true;
                }
            return false;
        }

        internal static IReadOnlyList<AccessV2HandoffCandidate>
            EvaluateV2Handoffs(
                AccessSearchSnapshot snapshot,
                IReadOnlyList<AccessV2BandState> recentNewestFirst,
                AccessV2History history,
                Tile2i? requiredGroundEntry = null,
                AccessSearchDiagnostics? diagnostics = null)
        {
            if (snapshot.V2GroundGraph == null
                || !snapshot.HasV2WorkableHandoffEvaluator)
                return Array.Empty<AccessV2HandoffCandidate>();
            return AccessV2Handoffs.Evaluate(
                recentNewestFirst,
                history,
                snapshot.V2GroundGraph,
                snapshot.GetV2WorkableHandoffs,
                snapshot.GetV2WorkableHandoffSpans,
                snapshot.LandscapingCostDistanceScale,
                snapshot.IsProjectedV2CenterPathable,
                snapshot.DoesProjectedV2CenterOverlapWork,
                snapshot.IsV2HandoffCenterPathable,
                snapshot.IsV2HandoffGroundEntryPathable,
                snapshot.VehicleWidth,
                AccessV2CostModel.GetCenterSpokeCost(
                    GeneratedVFixedOverhead),
                requiredGroundEntry,
                diagnostics);
        }

        private static bool TryAddV2ExteriorRays(
            AccessSearchSnapshot snapshot,
            AccessV2BandState? current,
            AccessV2Transition transition,
            Tile2i? connectedFixedOrigin,
            ICollection<AccessRayHeightConstraint> constraints,
            ref float rayCost,
            out string reason)
        {
            AccessV2BandState next = transition.Next;
            if (transition.Kind == AccessV2TransitionKind.Strafe
                && current.HasValue)
                return TryAddV2StrafeExteriorRays(
                    snapshot, transition, connectedFixedOrigin,
                    constraints, ref rayCost, out reason);

            bool scoreLane0 = transition.Delta.Any(
                item => item.Origin == next.GetLaneOrigin(0));
            bool scoreLane1 = transition.Delta.Any(
                item => item.Origin == next.GetLaneOrigin(1));
            if (transition.Kind != AccessV2TransitionKind.Strafe)
            {
                scoreLane0 = true;
                scoreLane1 = true;
            }

            if (!TryGetExitRayGeometry(
                    next.GetLaneOrigin(0), next.Band.Lane0,
                    next.EntryDirection,
                    out Tile2i lane0OuterCorner,
                    out float lane0OuterHeight,
                    out Tile2i lane0OuterDirection,
                    out _, out _, out _)
                || !TryGetExitRayGeometry(
                    next.GetLaneOrigin(1), next.Band.Lane1,
                    next.EntryDirection,
                    out _, out _, out _,
                    out Tile2i lane1OuterCorner,
                    out float lane1OuterHeight,
                    out Tile2i lane1OuterDirection))
            {
                reason = "SideRayInvalidEntryDirection";
                return false;
            }

            if (scoreLane0 && !TryAddV2Ray(
                    snapshot, constraints, ref rayCost,
                    lane0OuterCorner, lane0OuterHeight,
                    lane0OuterDirection, connectedFixedOrigin,
                    out reason))
                return false;
            if (scoreLane1 && !TryAddV2Ray(
                    snapshot, constraints, ref rayCost,
                    lane1OuterCorner, lane1OuterHeight,
                    lane1OuterDirection, connectedFixedOrigin,
                    out reason))
                return false;

            if (transition.Kind == AccessV2TransitionKind.Turn
                && current.HasValue)
            {
                float landingHeight = current.Value.Band.Lane0.Center2 / 2f;
                for (int index = 0;
                    index < transition.OldDirectionTurnRays.Count;
                    index++)
                {
                    AccessV2TurnRay turnRay = transition.OldDirectionTurnRays[index];
                    if (!TryAddV2Ray(
                            snapshot, constraints, ref rayCost,
                            turnRay.Source, landingHeight,
                            UnitDirection(turnRay.Direction), null,
                            out reason))
                        return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool TryAddV2StrafeExteriorRays(
            AccessSearchSnapshot snapshot,
            AccessV2Transition transition,
            Tile2i? connectedFixedOrigin,
            ICollection<AccessRayHeightConstraint> constraints,
            ref float rayCost,
            out string reason)
        {
            AccessV2BandState next = transition.Next;
            bool ownsLane0 = transition.Delta.Any(
                item => item.Origin == next.GetLaneOrigin(0));
            bool ownsLane1 = transition.Delta.Any(
                item => item.Origin == next.GetLaneOrigin(1));
            int transverseSign = ownsLane0 && !ownsLane1 ? -1
                : ownsLane1 && !ownsLane0 ? 1
                : 0;
            if (transverseSign == 0)
            {
                reason = "StrafeRayInvalidShift";
                return false;
            }
            for (int index = 0; index < transition.Delta.Count; index++)
            {
                AccessV2OriginProfile item = transition.Delta[index];
                if (!TryGetExitRayGeometry(
                        item.Origin, item.Profile,
                        next.EntryDirection,
                        out Tile2i lowCorner, out float lowHeight,
                        out Tile2i lowDirection,
                        out Tile2i highCorner, out float highHeight,
                        out Tile2i highDirection))
                {
                    reason = "StrafeRayInvalidGeometry";
                    return false;
                }
                if (!TryAddV2Ray(
                        snapshot, constraints, ref rayCost,
                        transverseSign < 0 ? lowCorner : highCorner,
                        transverseSign < 0 ? lowHeight : highHeight,
                        transverseSign < 0 ? lowDirection : highDirection,
                        connectedFixedOrigin, out reason))
                    return false;
            }
            reason = string.Empty;
            return true;
        }

        private static bool TryAddV2Ray(
            AccessSearchSnapshot snapshot,
            ICollection<AccessRayHeightConstraint> constraints,
            ref float rayCost,
            Tile2i corner,
            float height,
            Tile2i direction,
            Tile2i? exemption,
            out string reason)
        {
            AccessSideRayResult ray = ScoreExitCorner(
                snapshot, corner, height, direction,
                AccessHandoffOperation.Leveling,
                exemptDesignationOrigin: exemption);
            if (ray.IsFatal)
            {
                reason = ray.FatalReason ?? "SideRayRejected";
                return false;
            }
            rayCost += snapshot.LandscapingCostDistanceScale
                * SideRayWeight * ray.TotalCost;
            IReadOnlyList<AccessRayHeightConstraint> added =
                BuildRayHeightConstraints(
                    snapshot, corner, height, direction,
                    ray, AccessHandoffOperation.Leveling,
                    snapshot.VehicleClearanceRadius);
            for (int index = 0; index < added.Count; index++)
                constraints.Add(added[index]);
            reason = string.Empty;
            return true;
        }

        internal static Tile2i GetV2CanonicalCenter(AccessV2BandState state)
            => AccessV2PotentialField.GetCanonicalCenter(state);

        private static float GetCleanupEntryCost(AccessSearchSnapshot snapshot, Tile2i fromTile, Tile2i toTile)
        {
            if (!snapshot.TryGetRequiredCleanupInfoForTile(toTile, out AccessPropCleanupInfo info)) return 0f;
            if (!info.HasDenseDebrisCleanup) return 0f;
            HashSet<string> fromKeys = new HashSet<string>(StringComparer.Ordinal);
            if (snapshot.TryGetRequiredCleanupInfoForTile(fromTile, out AccessPropCleanupInfo fromInfo))
            {
                foreach (string key in GetCleanupCostKeys(fromInfo))
                    fromKeys.Add(key);
            }

            float newCleanupCost = 0f;
            var toKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in GetCleanupCostKeys(info))
            {
                if (!toKeys.Add(key) || fromKeys.Contains(key))
                    continue;
                newCleanupCost += AccessPropCleanupPolicy.GetCleanupLandscapingCost(
                    key.StartsWith("tree:", StringComparison.Ordinal));
            }
            if (newCleanupCost == 0f && info.Samples.Count == 0
                && info.HasDenseDebrisCleanup && fromInfoMissingOrDifferentOrigin())
                newCleanupCost = AccessPropCleanupPolicy.GetCleanupLandscapingCost(isTree: false);
            return snapshot.LandscapingCostDistanceScale
                * newCleanupCost;

            bool fromInfoMissingOrDifferentOrigin()
                => !snapshot.TryGetRequiredCleanupInfoForTile(fromTile, out AccessPropCleanupInfo fallbackFromInfo)
                    || fallbackFromInfo.Origin != info.Origin;
        }

        private static float CalculateGeneratedPropCleanupCost(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile,
            AccessHandoffOperation operation,
            out string rejectionReason,
            out float treeCleanupCost,
            out float denseDebrisCleanupCost)
        {
            rejectionReason = string.Empty;
            treeCleanupCost = 0f;
            denseDebrisCleanupCost = 0f;
            if (!snapshot.TryGetPropCleanupInfo(origin, out AccessPropCleanupInfo info))
                return 0f;
            if (!info.IsEligibleWithinGeneratedV)
            {
                if (operation == AccessHandoffOperation.Dumping
                    && info.HasDenseDebrisCleanup)
                    rejectionReason = "DumpOnlyPropBlocker";
                return 0f;
            }
            if (!info.HasDenseDebrisCleanup)
                return 0f;

            float denseCleanupUnit = snapshot.LandscapingCostDistanceScale
                * AccessPropCleanupPolicy.GetCleanupLandscapingCost(isTree: false);
            var chargedDenseDebris = new HashSet<string>(StringComparer.Ordinal);

            if (info.Samples.Count == 0)
            {
                if (info.HasDenseDebrisCleanup)
                {
                    if (operation == AccessHandoffOperation.None)
                    {
                        rejectionReason = "ZeroWorkPropBlocker";
                        return 0f;
                    }
                    denseDebrisCleanupCost += denseCleanupUnit;
                }
                return treeCleanupCost + denseDebrisCleanupCost;
            }

            foreach (AccessPropSample sample in info.Samples)
            {
                // Trees remain in the snapshot for post-route harvesting but
                // deliberately contribute no feasibility rejection or cost.
                if (!sample.IsDenseDebris)
                    continue;
                if (operation == AccessHandoffOperation.None)
                {
                    rejectionReason = "ZeroWorkPropBlocker";
                    return 0f;
                }
                else if (chargedDenseDebris.Add(sample.CleanupObjectKey))
                    denseDebrisCleanupCost += denseCleanupUnit;
            }

            return treeCleanupCost + denseDebrisCleanupCost;
        }

        private static IEnumerable<string> GetCleanupCostKeys(AccessPropCleanupInfo info)
        {
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (AccessPropSample sample in info.Samples)
            {
                string? key = sample.IsDenseDebris
                        ? "dense:" + sample.CleanupObjectKey
                        : null;
                if (key != null && emitted.Add(key))
                    yield return key;
            }
            if (info.Samples.Count == 0)
                yield return $"cleanup-origin:{info.Origin.X},{info.Origin.Y}";
        }

        private static AccessSearchResult BuildResult(bool success, string failureReason, Tile2i startOrigin,
            AccessSearchNode startNode,
            IReadOnlyList<AccessSearchNode> path, float cost, int visited,
            IReadOnlyDictionary<string, int> rejections, AccessSearchSnapshot snapshot,
            AccessReachedGoalKind reachedGoalKind = AccessReachedGoalKind.None,
            AccessSearchDiagnostics? diagnostics = null)
        {
            float traversal = 0f, generated = 0f, fixedCost = 0f, tree = 0f, dense = 0f;
            float generatedDirect = 0f, leftRay = 0f, rightRay = 0f, unresolved = 0f;
            int raySamples = 0;
            var chargedCleanup = new HashSet<string>(StringComparer.Ordinal);
            var generatedOperationByPathIndex = new Dictionary<int, AccessHandoffOperation>();
            for (int index = 0; index < path.Count; index++)
            {
                AccessSearchNode pathNode = path[index];
                if (!pathNode.IsGround
                    && pathNode.Mode != AccessSearchMode.Existing
                    && pathNode.HandoffOperation != AccessHandoffOperation.None)
                    generatedOperationByPathIndex[index] = pathNode.HandoffOperation;
                if (!pathNode.IsGround
                    || pathNode.HandoffOperation == AccessHandoffOperation.None)
                    continue;
                int spanLength = Math.Max(1, pathNode.HandoffSpanLength);
                for (int spanIndex = Math.Max(0, index - spanLength);
                    spanIndex < index;
                    spanIndex++)
                    if (!path[spanIndex].IsGround
                        && path[spanIndex].Mode != AccessSearchMode.Existing)
                        generatedOperationByPathIndex[spanIndex] = pathNode.HandoffOperation;
            }
            AccessSearchNode predecessor = startNode;
            for (int pathIndex = 0; pathIndex < path.Count; pathIndex++)
            {
                AccessSearchNode node = path[pathIndex];
                traversal += predecessor.IsGround && node.IsGround
                    ? GroundStepCost(predecessor.Position, node.Position)
                    : Manhattan(predecessor.CostPosition, node.CostPosition);
                if (node.IsGround)
                {
                    if (snapshot.TryGetRequiredCleanupInfoForTile(
                        node.Position, out AccessPropCleanupInfo info))
                    {
                        foreach (string key in GetCleanupCostKeys(info))
                        {
                            if (!chargedCleanup.Add(key))
                                continue;
                            dense += snapshot.LandscapingCostDistanceScale
                                * AccessPropCleanupPolicy.GetCleanupLandscapingCost(isTree: false);
                        }
                    }
                }
                else if (node.Mode != AccessSearchMode.Existing)
                {
                    if (TryGetProfile(snapshot, node, out AccessHeightProfile profile))
                    {
                        AccessHandoffOperation generatedOperation =
                            generatedOperationByPathIndex.TryGetValue(
                                pathIndex, out AccessHandoffOperation mappedOperation)
                                ? mappedOperation
                                : node.HandoffOperation;
                        CalculateGeneratedEntryCost(
                            snapshot, node.Position, profile,
                            node.EntryDirection, generatedOperation,
                            out AccessLandscapingCost landscapingCost,
                            out float generatedFixedCost, out _);
                        generated += snapshot.LandscapingCostDistanceScale
                            * GetWeightedLandscapingCost(landscapingCost);
                        generatedDirect += snapshot.LandscapingCostDistanceScale
                            * landscapingCost.DirectWorkCost;
                        leftRay += snapshot.LandscapingCostDistanceScale
                            * landscapingCost.LeftSideRayCost;
                        rightRay += snapshot.LandscapingCostDistanceScale
                            * landscapingCost.RightSideRayCost;
                        unresolved += snapshot.LandscapingCostDistanceScale
                            * landscapingCost.UnresolvedPenalty;
                        raySamples += landscapingCost.RaySampleCount;
                        if (!predecessor.IsGround
                            && predecessor.Mode != AccessSearchMode.Existing
                            && TryGetProfile(snapshot, predecessor,
                                out AccessHeightProfile predecessorProfile))
                        {
                            AccessSideRayResult turnOuterRay = ScoreTurnOuterCorner(
                                snapshot, predecessor.Position, predecessorProfile,
                                predecessor.EntryDirection, node.EntryDirection,
                                GetGeneratedWorkOperation(predecessor.HandoffOperation),
                                diagnostics: null, out _, out _);
                            float turnScale = snapshot.LandscapingCostDistanceScale;
                            generated += turnScale * SideRayWeight * turnOuterRay.TotalCost;
                            // Keep the existing public two-ray breakdown stable:
                            // turn-owned frontal work is grouped with the right ray.
                            rightRay += turnScale * turnOuterRay.IntegratedCost;
                            unresolved += turnScale * turnOuterRay.UnresolvedPenalty;
                            raySamples += turnOuterRay.SampleCount;
                        }
                        fixedCost += generatedFixedCost;
                        float generatedPropCleanup = CalculateGeneratedPropCleanupCost(
                            snapshot, node.Position, profile,
                            GetGeneratedWorkOperation(generatedOperation),
                            out _, out float generatedTreeCleanup,
                            out float generatedDenseCleanup);
                        if (generatedPropCleanup > 0f)
                        {
                            tree += generatedTreeCleanup;
                            dense += generatedDenseCleanup;
                        }
                    }
                }
                predecessor = node;
            }
            return new AccessSearchResult(success, failureReason, startOrigin, path, cost, visited, rejections,
                traversal, generated, fixedCost, tree, dense, reachedGoalKind,
                generatedDirect, leftRay, rightRay, unresolved, raySamples, diagnostics);
        }

        private static List<AccessSearchNode> Reconstruct(AccessSearchNode end,
            AccessSearchNode start,
            Dictionary<AccessSearchNode, AccessSearchNode> previous)
        {
            var path = new List<AccessSearchNode>();
            var seen = new HashSet<AccessSearchNode>();
            while (!end.Equals(start) && seen.Add(end))
            {
                path.Add(end);
                if (!previous.TryGetValue(end, out AccessSearchNode parent)) break;
                end = parent;
            }
            path.Reverse();
            return path;
        }

        private static AccessHandoffOperation GetGeneratedWorkOperation(
            AccessHandoffOperation handoffOperation)
            => handoffOperation == AccessHandoffOperation.None
                ? AccessHandoffOperation.Leveling
                : handoffOperation;

        internal static IReadOnlyCollection<Tile2i> BuildFinalGeneratedDisturbedTiles(
            AccessSearchSnapshot snapshot,
            AccessSearchResult result)
        {
            if (result.V2Route != null)
            {
                var v2Disturbed = new HashSet<Tile2i>();
                if (!AccessV2Replay.TryReplay(
                        snapshot, result.V2Route,
                        out AccessV2History replayedHistory,
                        out IReadOnlyList<AccessV2OriginProfile> generated,
                        out _, out _))
                    return v2Disturbed;
                for (int index = 0; index < generated.Count; index++)
                    for (int x = 0; x <= 4; x++)
                        for (int y = 0; y <= 4; y++)
                            v2Disturbed.Add(generated[index].Origin
                                + new RelTile2i(x, y));
                v2Disturbed.UnionWith(replayedHistory.CollectRayTiles());
                return v2Disturbed;
            }
            var disturbed = new HashSet<Tile2i>();
            var operationByIndex = new Dictionary<int, AccessHandoffOperation>();
            for (int index = 0; index < result.Path.Count; index++)
            {
                AccessSearchNode node = result.Path[index];
                if (!node.IsGround && node.Mode != AccessSearchMode.Existing
                    && node.HandoffOperation != AccessHandoffOperation.None)
                    operationByIndex[index] = node.HandoffOperation;
                if (!node.IsGround || node.HandoffOperation == AccessHandoffOperation.None)
                    continue;
                int spanLength = Math.Max(1, node.HandoffSpanLength);
                for (int spanIndex = Math.Max(0, index - spanLength);
                    spanIndex < index; spanIndex++)
                    if (!result.Path[spanIndex].IsGround
                        && result.Path[spanIndex].Mode != AccessSearchMode.Existing)
                        operationByIndex[spanIndex] = node.HandoffOperation;
            }

            AccessSearchNode? predecessor = null;
            for (int index = 0; index < result.Path.Count; index++)
            {
                AccessSearchNode node = result.Path[index];
                if (!node.IsGround && node.Mode != AccessSearchMode.Existing
                    && TryGetProfile(snapshot, node, out AccessHeightProfile profile))
                {
                    AccessHandoffOperation operation = operationByIndex.TryGetValue(
                        index, out AccessHandoffOperation mapped)
                            ? mapped : node.HandoffOperation;
                    CalculateGeneratedEntryCost(
                        snapshot, node.Position, profile, node.EntryDirection, operation,
                        out AccessLandscapingCost landscaping, out _, out _);
                    foreach (Tile2i tile in landscaping.DisturbedRayTiles)
                        disturbed.Add(tile);
                    for (int x = 0; x <= 4; x++)
                        for (int y = 0; y <= 4; y++)
                            disturbed.Add(node.Position + new RelTile2i(x, y));

                    if (predecessor.HasValue
                        && !predecessor.Value.IsGround
                        && predecessor.Value.Mode != AccessSearchMode.Existing
                        && TryGetProfile(snapshot, predecessor.Value,
                            out AccessHeightProfile predecessorProfile))
                    {
                        AccessHandoffOperation predecessorOperation =
                            operationByIndex.TryGetValue(index - 1,
                                out AccessHandoffOperation mappedPredecessor)
                                    ? mappedPredecessor
                                    : predecessor.Value.HandoffOperation;
                        AccessSideRayResult turnRay = ScoreTurnOuterCorner(
                            snapshot, predecessor.Value.Position, predecessorProfile,
                            predecessor.Value.EntryDirection, node.EntryDirection,
                            GetGeneratedWorkOperation(predecessorOperation), null,
                            out Tile2i turnCorner, out Tile2i turnDirection);
                        foreach (Tile2i tile in MergeDisturbedRayTiles(
                            Array.Empty<Tile2i>(), turnCorner, turnDirection,
                            turnRay.DisturbedDistance, snapshot.VehicleClearanceRadius))
                            disturbed.Add(tile);
                    }
                }
                predecessor = node;
            }
            return disturbed;
        }

        private static bool ValidateGeneratedPath(IReadOnlyList<AccessSearchNode> path,
            AccessSearchSnapshot snapshot, out string reason)
        {
            var profilesByOrigin = new Dictionary<Tile2i, AccessHeightProfile>();
            var cornerHeights = new Dictionary<Tile2i, int>();
            AccessSearchNode? predecessor = null;
            foreach (AccessSearchNode node in path)
            {
                if (node.IsGround || node.Mode == AccessSearchMode.Existing)
                {
                    predecessor = node;
                    continue;
                }
                if (!TryGetProfile(snapshot, node, out AccessHeightProfile profile))
                {
                    predecessor = node;
                    continue;
                }
                if (profilesByOrigin.ContainsKey(node.Position))
                {
                    reason = $"FinalOriginRevisit@{node.Position}";
                    return false;
                }
                Tile2i? allowedEdgeNeighbor = predecessor.HasValue
                    && !predecessor.Value.IsGround
                    && predecessor.Value.Mode != AccessSearchMode.Existing
                        ? predecessor.Value.Position
                        : (Tile2i?)null;
                foreach (Tile2i direction in s_originDirections)
                {
                    Tile2i neighbor = node.Position + new RelTile2i(
                        direction.X, direction.Y);
                    if (profilesByOrigin.ContainsKey(neighbor)
                        && (!allowedEdgeNeighbor.HasValue
                            || neighbor != allowedEdgeNeighbor.Value))
                    {
                        reason = $"FinalAdjacentSelfContact@{node.Position}:neighbor={neighbor}";
                        return false;
                    }
                }
                profilesByOrigin[node.Position] = profile;
                bool mismatch = false;
                Tile2i mismatchCorner = default;
                int oldMismatchHeight2 = 0;
                int newMismatchHeight2 = 0;
                profile.AddWorldCorners(node.Position, (p, h) =>
                {
                    if (cornerHeights.TryGetValue(p, out int old) && old != h)
                    {
                        mismatch = true;
                        mismatchCorner = p;
                        oldMismatchHeight2 = old;
                        newMismatchHeight2 = h;
                    }
                    else cornerHeights[p] = h;
                });
                if (mismatch)
                {
                    reason = $"FinalSelfContactCorner@{mismatchCorner}:oldH2={oldMismatchHeight2},newH2={newMismatchHeight2},origin={node.Position}";
                    return false;
                }
                predecessor = node;
            }
            reason = string.Empty;
            return true;
        }

        private static int Manhattan(Tile2i a, Tile2i b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static float GroundStepCost(Tile2i from, Tile2i to)
            => from.X != to.X && from.Y != to.Y
                ? GroundDiagonalCost
                : Manhattan(from, to);

        private static float OctileDistance(Tile2i a, Tile2i b)
        {
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            int diagonal = Math.Min(dx, dy);
            return diagonal * GroundDiagonalCost + Math.Abs(dx - dy);
        }

        private static void Reject(Dictionary<string, int> rejections, string reason)
            => rejections[reason] = rejections.TryGetValue(reason, out int count) ? count + 1 : 1;

        private static AccessSearchResult Failed(string reason, Tile2i start, int visited,
            Dictionary<string, int> rejections,
            AccessSearchDiagnostics? diagnostics = null)
            => new AccessSearchResult(false, reason, start, Array.Empty<AccessSearchNode>(), 0f, visited,
                rejections, 0f, 0f, 0f, 0f, 0f,
                AccessReachedGoalKind.None, diagnostics: diagnostics);

        internal sealed class GeneratedPathHistory
        {
            public static readonly GeneratedPathHistory Empty = new GeneratedPathHistory();

            private readonly GeneratedPathHistory? m_parent;
            private readonly Tile2i m_origin;
            private readonly AccessHeightProfile m_profile;
            private readonly IReadOnlyList<Tile2i> m_rayDisturbedTiles;
            private readonly IReadOnlyList<Tile2i> m_handoffEscapeTiles;
            private readonly Dictionary<Tile2i, float> m_cutSupportCeilings;
            private readonly Dictionary<Tile2i, float> m_fillSurfaceFloors;
            private readonly bool m_hasGenerated;

            public int Depth { get; }

            private GeneratedPathHistory()
            {
                m_rayDisturbedTiles = Array.Empty<Tile2i>();
                m_handoffEscapeTiles = Array.Empty<Tile2i>();
                m_cutSupportCeilings = new Dictionary<Tile2i, float>();
                m_fillSurfaceFloors = new Dictionary<Tile2i, float>();
            }

            private GeneratedPathHistory(
                GeneratedPathHistory parent,
                Tile2i origin,
                AccessHeightProfile profile,
                IReadOnlyList<Tile2i> rayDisturbedTiles,
                IReadOnlyList<Tile2i>? handoffEscapeTiles = null,
                IReadOnlyList<AccessRayHeightConstraint>? rayHeightConstraints = null)
            {
                m_parent = parent;
                m_origin = origin;
                m_profile = profile;
                m_rayDisturbedTiles = rayDisturbedTiles;
                m_handoffEscapeTiles = handoffEscapeTiles ?? Array.Empty<Tile2i>();
                m_cutSupportCeilings = new Dictionary<Tile2i, float>();
                m_fillSurfaceFloors = new Dictionary<Tile2i, float>();
                if (rayHeightConstraints != null)
                {
                    for (int index = 0; index < rayHeightConstraints.Count; index++)
                    {
                        AccessRayHeightConstraint constraint = rayHeightConstraints[index];
                        if (constraint.Operation == AccessSideRayOperation.Cut)
                        {
                            if (!m_cutSupportCeilings.TryGetValue(
                                    constraint.Tile, out float existing)
                                || constraint.Height < existing)
                                m_cutSupportCeilings[constraint.Tile] = constraint.Height;
                        }
                        else if (constraint.Operation == AccessSideRayOperation.Fill)
                        {
                            if (!m_fillSurfaceFloors.TryGetValue(
                                    constraint.Tile, out float existing)
                                || constraint.Height > existing)
                                m_fillSurfaceFloors[constraint.Tile] = constraint.Height;
                        }
                    }
                }
                m_hasGenerated = true;
                Depth = parent.Depth + 1;
            }

            public bool ContainsOrigin(Tile2i origin)
            {
                for (GeneratedPathHistory? history = this;
                    history != null && history.m_hasGenerated;
                    history = history.m_parent)
                {
                    if (history.m_origin == origin)
                        return true;
                }
                return false;
            }

            public bool HasEdgeNeighborExcept(
                Tile2i origin,
                Tile2i? allowedNeighbor)
            {
                for (GeneratedPathHistory? history = this;
                    history != null && history.m_hasGenerated;
                    history = history.m_parent)
                {
                    if (allowedNeighbor.HasValue
                        && history.m_origin == allowedNeighbor.Value)
                        continue;
                    int dx = Math.Abs(history.m_origin.X - origin.X);
                    int dy = Math.Abs(history.m_origin.Y - origin.Y);
                    if ((dx == 4 && dy == 0) || (dx == 0 && dy == 4))
                        return true;
                }
                return false;
            }

            public bool TryGetCornerHeight(Tile2i corner, out int height2)
            {
                for (GeneratedPathHistory? history = this;
                    history != null && history.m_hasGenerated;
                    history = history.m_parent)
                {
                    Tile2i origin = history.m_origin;
                    if (corner == origin) { height2 = history.m_profile.Nw2; return true; }
                    if (corner == origin + new RelTile2i(4, 0)) { height2 = history.m_profile.Ne2; return true; }
                    if (corner == origin + new RelTile2i(4, 4)) { height2 = history.m_profile.Se2; return true; }
                    if (corner == origin + new RelTile2i(0, 4)) { height2 = history.m_profile.Sw2; return true; }
                }
                height2 = 0;
                return false;
            }

            public bool IsGroundDisturbed(
                Tile2i tile,
                Tile2i? exceptGeneratedOrigin = null)
            {
                for (GeneratedPathHistory? history = this;
                    history != null && history.m_hasGenerated;
                    history = history.m_parent)
                {
                    for (int i = 0; i < history.m_rayDisturbedTiles.Count; i++)
                        if (history.m_rayDisturbedTiles[i] == tile)
                            return true;
                    Tile2i origin = history.m_origin;
                    if (exceptGeneratedOrigin.HasValue
                        && origin == exceptGeneratedOrigin.Value)
                        continue;
                    if (tile.X >= origin.X && tile.X <= origin.X + 4
                        && tile.Y >= origin.Y && tile.Y <= origin.Y + 4)
                    {
                        bool isHandoffEscape = false;
                        for (int i = 0; i < history.m_handoffEscapeTiles.Count; i++)
                            if (history.m_handoffEscapeTiles[i] == tile)
                            {
                                isHandoffEscape = true;
                                break;
                            }
                        if (!isHandoffEscape)
                            return true;
                    }
                }
                return false;
            }

            public bool IsProfileBlockedByRayEnvelope(
                Tile2i origin,
                AccessHeightProfile profile,
                out AccessSideRayOperation blockingOperation)
            {
                const float epsilon = 0.0001f;
                for (GeneratedPathHistory? history = this;
                    history != null && history.m_hasGenerated;
                    history = history.m_parent)
                {
                    for (int y = 0; y <= 4; y++)
                    {
                        for (int x = 0; x <= 4; x++)
                        {
                            Tile2i tile = origin + new RelTile2i(x, y);
                            float height = profile.GetHeight2NumeratorAt(x, y) / 32f;
                            if (history.m_cutSupportCeilings.TryGetValue(
                                    tile, out float cutCeiling)
                                && height > cutCeiling + epsilon)
                            {
                                blockingOperation = AccessSideRayOperation.Cut;
                                return true;
                            }
                            if (history.m_fillSurfaceFloors.TryGetValue(
                                    tile, out float fillFloor)
                                && height < fillFloor - epsilon)
                            {
                                blockingOperation = AccessSideRayOperation.Fill;
                                return true;
                            }
                        }
                    }
                }
                blockingOperation = AccessSideRayOperation.None;
                return false;
            }

            public GeneratedPathHistory WithGenerated(
                Tile2i origin,
                AccessHeightProfile profile,
                IEnumerable<Tile2i> disturbedRayTiles,
                IReadOnlyList<Tile2i>? handoffEscapeTiles = null,
                IReadOnlyList<AccessRayHeightConstraint>? rayHeightConstraints = null)
            {
                IReadOnlyList<Tile2i> rayTiles = disturbedRayTiles as IReadOnlyList<Tile2i>
                    ?? new List<Tile2i>(disturbedRayTiles).ToArray();
                return new GeneratedPathHistory(
                    this, origin, profile, rayTiles, handoffEscapeTiles,
                    rayHeightConstraints);
            }

            public GeneratedPathHistory ReplaceLatestGeneratedRays(
                Tile2i origin,
                AccessHeightProfile profile,
                IEnumerable<Tile2i> disturbedRayTiles,
                IReadOnlyList<Tile2i>? handoffEscapeTiles = null)
            {
                if (!m_hasGenerated || m_origin != origin)
                    return this;
                GeneratedPathHistory parent = m_parent ?? Empty;
                IReadOnlyList<Tile2i> rayTiles = disturbedRayTiles as IReadOnlyList<Tile2i>
                    ?? new List<Tile2i>(disturbedRayTiles).ToArray();
                return new GeneratedPathHistory(
                    parent, origin, profile, rayTiles, handoffEscapeTiles);
            }

            public bool TryReplaceLatestGeneratedSpan(
                IReadOnlyList<AccessHandoffSpanCell> cells,
                IReadOnlyList<IReadOnlyList<Tile2i>> disturbedRayTiles,
                IReadOnlyList<Tile2i> handoffEscapeTiles,
                out GeneratedPathHistory replacement)
            {
                replacement = this;
                if (cells.Count == 0 || cells.Count != disturbedRayTiles.Count)
                    return false;

                GeneratedPathHistory? cursor = this;
                for (int index = cells.Count - 1; index >= 0; index--)
                {
                    if (cursor == null || !cursor.m_hasGenerated
                        || cursor.m_origin != cells[index].Origin)
                        return false;
                    cursor = cursor.m_parent;
                }

                GeneratedPathHistory rebuilt = cursor ?? Empty;
                for (int index = 0; index < cells.Count; index++)
                {
                    AccessHandoffSpanCell cell = cells[index];
                    rebuilt = new GeneratedPathHistory(
                        rebuilt,
                        cell.Origin,
                        cell.Profile,
                        disturbedRayTiles[index],
                        handoffEscapeTiles);
                }
                replacement = rebuilt;
                return true;
            }

        }

        internal readonly struct QueueEntry
        {
            public AccessSearchNode Node { get; }
            public float PathCost { get; }
            public float Priority { get; }
            public float Heuristic { get; }
            public QueueEntry(
                AccessSearchNode node,
                float pathCost,
                float priority,
                float heuristic)
            {
                Node = node;
                PathCost = pathCost;
                Priority = priority;
                Heuristic = heuristic;
            }
        }

        internal sealed class MinQueue
        {
            private readonly List<QueueEntry> m_items = new List<QueueEntry>();
            public int Count => m_items.Count;
            public void Push(QueueEntry entry)
            {
                m_items.Add(entry);
                int i = m_items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Less(m_items[i], m_items[parent])) break;
                    (m_items[i], m_items[parent]) = (m_items[parent], m_items[i]);
                    i = parent;
                }
            }
            public QueueEntry Pop()
            {
                QueueEntry result = m_items[0];
                int last = m_items.Count - 1;
                m_items[0] = m_items[last];
                m_items.RemoveAt(last);
                int i = 0;
                while (i < m_items.Count)
                {
                    int left = i * 2 + 1, right = left + 1, smallest = i;
                    if (left < m_items.Count && Less(m_items[left], m_items[smallest])) smallest = left;
                    if (right < m_items.Count && Less(m_items[right], m_items[smallest])) smallest = right;
                    if (smallest == i) break;
                    (m_items[i], m_items[smallest]) = (m_items[smallest], m_items[i]);
                    i = smallest;
                }
                return result;
            }
            private static bool Less(QueueEntry a, QueueEntry b)
            {
                int priority = a.Priority.CompareTo(b.Priority);
                if (priority != 0) return priority < 0;
                int heuristic = a.Heuristic.CompareTo(b.Heuristic);
                if (heuristic != 0) return heuristic < 0;
                int path = a.PathCost.CompareTo(b.PathCost);
                if (path != 0) return path < 0;
                int x = a.Node.Position.X.CompareTo(b.Node.Position.X);
                if (x != 0) return x < 0;
                int y = a.Node.Position.Y.CompareTo(b.Node.Position.Y);
                if (y != 0) return y < 0;
                int height = a.Node.Height2.CompareTo(b.Node.Height2);
                if (height != 0) return height < 0;
                return (int)a.Node.Mode < (int)b.Node.Mode;
            }
        }
    }
}

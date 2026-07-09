using System;
using System.Collections.Generic;
using Mafi;

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
            new RelTile2i(1, 0), new RelTile2i(-1, 0), new RelTile2i(0, 1), new RelTile2i(0, -1)
        };

        private static readonly AccessSearchMode[] s_vModes =
        {
            AccessSearchMode.Flat,
            AccessSearchMode.XPositive,
            AccessSearchMode.XNegative,
            AccessSearchMode.YPositive,
            AccessSearchMode.YNegative
        };

        private const int MAX_VISITED_NODES = 250000;
        private const float GENERATED_V_FIXED_OVERHEAD = 0.25f;

        public static bool ValidateCoreTransitions(out string failure)
        {
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
            if (!AutoDepthDesignation.TrySelectDirectionalHandoffOperation(
                    -1, 0, 0, 1, out AccessHandoffOperation directionalMining)
                || directionalMining != AccessHandoffOperation.Mining
                || !AutoDepthDesignation.TrySelectDirectionalHandoffOperation(
                    0, 1, -1, 0, out AccessHandoffOperation directionalDumping)
                || directionalDumping != AccessHandoffOperation.Dumping
                || !AutoDepthDesignation.TrySelectDirectionalHandoffOperation(
                    -1, 1, 0, 0, out AccessHandoffOperation levelHandoff)
                || levelHandoff != AccessHandoffOperation.Leveling
                || !AutoDepthDesignation.TrySelectDirectionalHandoffOperation(
                    -1, -1, 0, 0, out AccessHandoffOperation levelHandoffFromMining)
                || levelHandoffFromMining != AccessHandoffOperation.Leveling
                || AutoDepthDesignation.TrySelectDirectionalHandoffOperation(
                    -1, 1, 1, 1, out _)
                || AutoDepthDesignation.TrySelectDirectionalHandoffOperation(
                    -1, -1, -1, 0, out _)
                || AutoDepthDesignation.TrySelectDirectionalHandoffOperation(
                    0, 0, 1, 1, out _))
            { failure = "directional handoff must be level at the ground face or workable toward V and non-workable toward ground"; return false; }
            var mixedCleanup = AccessPropCleanupPolicy.BuildOriginInfo(
                new Tile2i(0, 0),
                new[]
                {
                    new AccessPropSample(new Tile2i(0, 0), true, false, true),
                    new AccessPropSample(new Tile2i(1, 0), false, true, true),
                });
            if (!mixedCleanup.IsEligible || !mixedCleanup.HasTreeCleanup || !mixedCleanup.HasDenseDebrisCleanup)
            { failure = "prop cleanup helper must preserve mixed tree and dense-debris classes"; return false; }
            var hardCleanup = AccessPropCleanupPolicy.BuildOriginInfo(
                new Tile2i(0, 0),
                new[] { new AccessPropSample(new Tile2i(0, 0), false, true, false) });
            if (hardCleanup.IsEligible || hardCleanup.BlockerKind != AccessPropBlockerKind.HardBlocker)
            { failure = "non-removable prop sample must classify as a hard blocker"; return false; }
            if (!AccessPropCleanupPolicy.DoesStubbedTerrainDeltaRemoveProp(AccessHandoffOperation.Mining, 4, 2)
                || AccessPropCleanupPolicy.DoesStubbedTerrainDeltaRemoveProp(AccessHandoffOperation.Mining, 4, 3)
                || !AccessPropCleanupPolicy.DoesStubbedTerrainDeltaRemoveProp(AccessHandoffOperation.Dumping, 2, 4)
                || AccessPropCleanupPolicy.DoesStubbedTerrainDeltaRemoveProp(AccessHandoffOperation.Dumping, 2, 3))
            { failure = "stubbed one-level prop-removal threshold helper failed"; return false; }
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
                Array.Empty<AccessDurabilityCorner>());
            if (oceanFixture.IsCandidateProfileFeasible(new Tile2i(12, 12), flat, out string oceanMismatch)
                || oceanMismatch != "OceanBelowMinimum")
            { failure = "V profiles below height 1 must not visit ocean"; return false; }
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
                out AccessLandscapingCost baselineLandscapingCost,
                out float baselineFixedCost);
            if (Math.Abs(baselineLandscapingCost.DirectWorkCost - 16f) > 0.0001f
                || baselineLandscapingCost.LeftSideRayCost != 0f
                || baselineLandscapingCost.RightSideRayCost != 0f
                || baselineLandscapingCost.UnresolvedPenalty != 0f
                || baselineLandscapingCost.IsFatal
                || Math.Abs(baselineFixedCost - GENERATED_V_FIXED_OVERHEAD) > 0.0001f
                || Math.Abs(baselineGeneratedEntryCost
                    - (16f * fixture.LandscapingCostDistanceScale + GENERATED_V_FIXED_OVERHEAD)) > 0.0001f)
            { failure = "stage-one generated entry cost must reproduce center-height baseline with zero side-ray cost"; return false; }
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
            AccessSideRayResult noOpRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 0f),
                Tile2i.Zero, new Tile2i(1, 0), 0f,
                AccessSideRayOperation.None, 1f);
            AccessSideRayResult resolvedFillRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 0f),
                Tile2i.Zero, new Tile2i(1, 0), 4f,
                AccessSideRayOperation.Fill, 1f);
            AccessSideRayResult resolvedCutRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Terrain, 4f),
                Tile2i.Zero, new Tile2i(1, 0), 0f,
                AccessSideRayOperation.Cut, 1f);
            if (noOpRay.TotalCost != 0f || noOpRay.SampleCount != 0
                || noOpRay.IsFatal || noOpRay.IsUnresolved
                || Math.Abs(resolvedFillRay.TotalCost - 6f) > 0.0001f
                || resolvedFillRay.SampleCount != 4
                || resolvedFillRay.IsFatal || resolvedFillRay.IsUnresolved
                || Math.Abs(resolvedCutRay.TotalCost - 6f) > 0.0001f
                || resolvedCutRay.SampleCount != 4
                || resolvedCutRay.IsFatal || resolvedCutRay.IsUnresolved)
            { failure = "side-ray integrator must preserve no-op and resolved fill/cut rectangle costs"; return false; }

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
                || unresolvedRay.SampleCount != 7
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
            AccessSideRayResult cutOceanRay = AccessSideRayCost.Score(
                _ => new AccessSideRayTerrainSample(AccessTerrainSampleKind.Ocean, -2f),
                Tile2i.Zero, new Tile2i(1, 0), 0f,
                AccessSideRayOperation.Cut, 1f);
            if (fillOceanRay.IsFatal
                || fillOceanRay.IsUnresolved
                || Math.Abs(fillOceanRay.TotalCost - 5f) > 0.0001f
                || cutOceanRay.FatalReason != "SideRayCutOcean")
            { failure = "side-ray integrator must continue fill through ocean and reject low-ocean cuts"; return false; }
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
                new[] { fixtureGoal },
                new[] { fixtureGoal },
                Array.Empty<Tile2i>(),
                Array.Empty<Tile2i>(),
                new[] { new AccessDurabilityCorner(new Tile2i(16, 16), 0) });
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
            var unsupportedWidthRequest = new AccessPathRequest(
                "fixture-width-two",
                fixture,
                rootedRequest.Start,
                rootedRequest.Goal,
                2,
                AccessPathIntent.ConstructAccessway);
            AccessSearchResult unsupportedWidthResult = FindPath(unsupportedWidthRequest);
            if (unsupportedWidthResult.Success
                || unsupportedWidthResult.FailureReason != "UnsupportedWidth")
            { failure = "V1 rooted request must reject widths other than one"; return false; }
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
                new[] { fixtureGoal, cleanupGoal },
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
            var cleanupGeneratedResult = new AccessSearchResult(true, string.Empty, cleanupStart,
                new[]
                {
                    new AccessSearchNode(cleanupStart, 0, AccessSearchMode.Flat),
                    new AccessSearchNode(cleanupGoal, 0, AccessSearchMode.Ground),
                }, 2f, 2, new Dictionary<string, int>());
            AccessDesignationPlan cleanupGeneratedPlan =
                AccessPathMaterializer.Materialize(cleanupFixture, cleanupGeneratedResult);
            if (cleanupGeneratedPlan.IsValid
                || cleanupGeneratedPlan.FailureReason != "PlanCleanupOriginGenerated")
            { failure = "cleanup origins must not materialize as generated V designations"; return false; }

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
            var generatedFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, false, 1f, 1f,
                groundHeights,
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
            var turnFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, false, 1f, 1f,
                groundHeights,
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
                && Math.Abs(left.DenseDebrisCleanupCost - right.DenseDebrisCleanupCost) <= 0.0001f;

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
            Tile2i start = request.Start.Nodes.Count > 0 ? request.Start.Nodes[0] : default;
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
                    ? HeightAwareGoalIndex.Build(snapshot, goalsByHeight2)
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
                useAStarHeuristic, goalIndex);

            if (queue.Count == 0)
                return AccessPathSearchSession.Completed(Failed("NoInitialSuccessor", startOrigin, 0, rejections));

            return new AccessPathSearchSession(snapshot, startOrigin, startNode,
                fixedGoalOrigins, includeGroundGoals, rejectGoal,
                useAStarHeuristic, goalIndex, maxCostLimit,
                distance, previous, generatedHistory, queue, rejections, lastRejectedGoalPath,
                lastGoalRejectionReason, lastRejectedGoalCost);
        }

        public sealed class AccessPathSearchSession
        {
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
            private List<AccessSearchNode>? m_lastRejectedGoalPath;
            private string m_lastGoalRejectionReason;
            private float m_lastRejectedGoalCost;
            private int m_visited;
            private readonly HeightAwareGoalIndex m_goalIndex;
            private readonly float m_maxCostLimit;

            public bool IsComplete { get; private set; }
            public AccessSearchResult Result { get; private set; }
            public int VisitedNodes => m_visited;
            public int PendingNodes => IsComplete || m_queue == null ? 0 : m_queue.Count;
            public Dictionary<string, int> Rejections => m_rejections;

            internal static AccessPathSearchSession Completed(AccessSearchResult result)
                => new AccessPathSearchSession(result);

            private AccessPathSearchSession(AccessSearchResult result)
            {
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
                List<AccessSearchNode>? lastRejectedGoalPath,
                string lastGoalRejectionReason,
                float lastRejectedGoalCost)
            {
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
                m_lastRejectedGoalPath = lastRejectedGoalPath;
                m_lastGoalRejectionReason = lastGoalRejectionReason;
                m_lastRejectedGoalCost = lastRejectedGoalCost;
                Result = Failed("SearchNotComplete", startOrigin, 0, rejections);
            }

            public int Step(int maxVisitedNodes)
            {
                if (IsComplete) return 0;
                if (maxVisitedNodes <= 0) maxVisitedNodes = 1;

                int visitedThisStep = 0;
                while (m_queue.Count > 0 && m_visited < MAX_VISITED_NODES && visitedThisStep < maxVisitedNodes)
                {
                    QueueEntry entry = m_queue.Pop();
                    if (entry.Priority > m_maxCostLimit)
                    {
                        CompleteFailed("CostLimitExceeded");
                        break;
                    }
                    if (!m_distance.TryGetValue(entry.Node, out float known) || entry.PathCost > known + 0.0001f)
                        continue;

                    AccessSearchNode current = entry.Node;
                    m_visited++;
                    visitedThisStep++;
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
                        List<AccessSearchNode> path = Reconstruct(current, m_startNode, m_previous);
                        var candidate = BuildResult(
                            true, string.Empty, m_startOrigin, m_startNode, path, known,
                            m_visited, m_rejections, m_snapshot, reachedGoalKind);
                        AccessDesignationPlan goalPlan = AccessPathMaterializer.Materialize(m_snapshot, candidate);
                        string goalFailure = goalPlan.IsValid
                            ? m_rejectGoal?.Invoke(candidate) ?? string.Empty
                            : string.IsNullOrEmpty(goalPlan.FailureReason)
                                ? "Materialization"
                                : goalPlan.FailureReason;
                        if (!string.IsNullOrEmpty(goalFailure))
                        {
                            Reject(m_rejections, "Goal" + goalFailure);
                            m_lastRejectedGoalPath = path;
                            m_lastGoalRejectionReason = goalFailure;
                            m_lastRejectedGoalCost = known;
                        }
                        else
                        {
                            Result = candidate;
                            IsComplete = true;
                            return visitedThisStep;
                        }
                    }

                    if (current.IsGround)
                        ExpandGround(m_snapshot, current, known, m_distance, m_previous, m_generatedHistory, m_queue, m_rejections,
                            m_useAStarHeuristic, m_goalIndex);
                    else if (TryGetProfile(m_snapshot, current, out AccessHeightProfile currentProfile))
                        ExpandOrigin(m_snapshot, current, currentProfile, known, m_distance, m_previous, m_generatedHistory, m_queue, m_rejections,
                            m_useAStarHeuristic, m_goalIndex);
                    else
                        Reject(m_rejections, "MissingProfile");
                }

                if (!IsComplete && (m_queue.Count == 0 || m_visited >= MAX_VISITED_NODES))
                    CompleteFailed();

                return visitedThisStep;
            }

            private void CompleteFailed(string? reason = null)
            {
                if (m_lastRejectedGoalPath != null)
                {
                    string finalReason = reason ?? (m_visited >= MAX_VISITED_NODES
                        ? "VisitedLimitAfterGoalRejection"
                        : m_lastGoalRejectionReason);
                    Result = new AccessSearchResult(false, finalReason, m_startOrigin, m_lastRejectedGoalPath,
                        m_lastRejectedGoalCost, m_visited, m_rejections);
                }
                else
                {
                    string finalReason = reason ?? (m_visited >= MAX_VISITED_NODES ? "VisitedLimit" : "NoPath");
                    Result = Failed(finalReason, m_startOrigin, m_visited, m_rejections);
                }
                IsComplete = true;
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
            bool useAStarHeuristic, HeightAwareGoalIndex goalIndex)
        {
            var handoffs = new List<AccessGroundHandoff>();
            var emittedHandoffs = new HashSet<(Tile2i Tile, AccessHandoffOperation Operation)>();
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

            foreach (AccessGroundHandoff handoff in handoffs)
            {
                if (!snapshot.TryGetGroundHeight2(handoff.Tile, out int groundHeight2)) continue;
                var ground = new AccessSearchNode(handoff.Tile, groundHeight2,
                    AccessSearchMode.Ground, handoff.Operation);
                Relax(snapshot, current, ground, currentCost + Manhattan(current.CostPosition, handoff.Tile),
                    distance, previous, generatedHistory, queue,
                    useAStarHeuristic, goalIndex);
            }

            void AddHandoffs(Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile)
            {
                foreach (AccessGroundHandoff handoff in GetHandoffs(
                    snapshot, current.Position, currentProfile,
                    predecessorOrigin, predecessorProfile))
                {
                    if (emittedHandoffs.Add((handoff.Tile, handoff.Operation)))
                        handoffs.Add(handoff);
                }
            }

            foreach (Tile2i direction in s_originDirections)
            {
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
                    goalIndex);
            }
        }

        private static void AddOriginSuccessors(AccessSearchSnapshot snapshot,
            Tile2i currentOrigin, AccessHeightProfile currentProfile, Tile2i nextOrigin, Tile2i direction,
            AccessSearchNode current, bool hasCurrent, float baseCost,
            Dictionary<AccessSearchNode, float> distance,
            Dictionary<AccessSearchNode, AccessSearchNode> previous,
            Dictionary<AccessSearchNode, GeneratedPathHistory> generatedHistory,
            MinQueue queue, Dictionary<string, int> rejections,
            bool useAStarHeuristic, HeightAwareGoalIndex goalIndex)
        {
            if (!snapshot.IsOriginInside(nextOrigin)) { Reject(rejections, "HorizontalBounds"); return; }

            if (snapshot.TryGetFixedProfile(nextOrigin, out AccessHeightProfile fixedProfile))
            {
                if (snapshot.IsProfileOceanBlocked(nextOrigin, fixedProfile))
                { Reject(rejections, "OceanBelowMinimum"); return; }
                if (!EdgesMatch(currentProfile, fixedProfile, direction)) { Reject(rejections, "FixedEdgeMismatch"); return; }
                var existing = new AccessSearchNode(nextOrigin, fixedProfile.Center2, AccessSearchMode.Existing);
                Relax(snapshot, current, existing, baseCost + 4f, distance, previous, generatedHistory, queue, useAStarHeuristic,
                    goalIndex, hasCurrent);
                return;
            }

            if (snapshot.IsCleanupOrigin(nextOrigin))
            { Reject(rejections, "CleanupOriginRequiresGround"); return; }

            foreach (AccessSearchMode mode in s_vModes)
            {
                if (!TrySolveSuccessor(currentProfile, direction, mode, out AccessHeightProfile nextProfile))
                { Reject(rejections, "EdgeProfile"); continue; }
                if (!IsGeneratedProfileFeasible(
                    snapshot, nextOrigin, nextProfile, current, direction, out string reason))
                { Reject(rejections, reason); continue; }
                if (hasCurrent && !IsCompatibleWithPathHistory(nextOrigin, nextProfile, current, generatedHistory))
                { Reject(rejections, "PathSelfContact"); continue; }

                var next = new AccessSearchNode(nextOrigin, nextProfile.Center2, mode);
                float generatedEntryCost = CalculateGeneratedEntryCost(
                    snapshot, nextOrigin, nextProfile, out _, out _);
                float nextCost = baseCost + 4f + generatedEntryCost;
                Relax(snapshot, current, next, nextCost, distance, previous, generatedHistory, queue, useAStarHeuristic,
                    goalIndex, hasCurrent);
            }
        }

        private static void ExpandGround(AccessSearchSnapshot snapshot, AccessSearchNode current, float currentCost,
            Dictionary<AccessSearchNode, float> distance,
            Dictionary<AccessSearchNode, AccessSearchNode> previous,
            Dictionary<AccessSearchNode, GeneratedPathHistory> generatedHistory,
            MinQueue queue, Dictionary<string, int> rejections,
            bool useAStarHeuristic, HeightAwareGoalIndex goalIndex)
        {
            foreach (RelTile2i direction in s_tileDirections)
            {
                Tile2i nextTile = current.Position + direction;
                if (!snapshot.IsGroundOrCleanupNode(nextTile) || !snapshot.TryGetGroundHeight2(nextTile, out int height2)) continue;
                var next = new AccessSearchNode(nextTile, height2, AccessSearchMode.Ground);
                float cleanupCost = GetCleanupEntryCost(snapshot, current.Position, nextTile);
                Relax(snapshot, current, next, currentCost + 1f + cleanupCost,
                    distance, previous, generatedHistory, queue,
                    useAStarHeuristic, goalIndex);
            }

            foreach (Tile2i origin in CandidateOriginsAtGroundTile(current.Position))
            {
                if (snapshot.IsCleanupOrigin(origin))
                {
                    Reject(rejections, "CleanupOriginRequiresGround");
                    continue;
                }
                foreach (AccessSearchMode mode in s_vModes)
                {
                    int center2 = snapshot.GetTerrainCenterHeight2(origin);
                    for (int delta = -3; delta <= 3; delta++)
                    {
                        if (!AccessHeightProfile.TryForMode(mode, center2 + delta, out AccessHeightProfile profile)) continue;
                        if (!IsGeneratedProfileFeasible(
                            snapshot, origin, profile, current, default, out string reason))
                        { Reject(rejections, reason); continue; }
                        if (!TryGetGroundToGeneratedHandoff(
                            snapshot, origin, profile, current.Position,
                            out AccessHandoffOperation handoffOperation))
                        {
                            continue;
                        }
                        if (!IsCompatibleWithPathHistory(origin, profile, current, generatedHistory))
                        { Reject(rejections, "PathSelfContact"); continue; }
                        var next = new AccessSearchNode(
                            origin, profile.Center2, mode, handoffOperation);
                        float generatedEntryCost = CalculateGeneratedEntryCost(
                            snapshot, origin, profile, out _, out _);
                        float cost = currentCost + Manhattan(current.Position, next.CostPosition)
                            + generatedEntryCost;
                        Relax(snapshot, current, next, cost,
                            distance, previous, generatedHistory, queue,
                            useAStarHeuristic, goalIndex);
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
            Dictionary<AccessSearchNode, GeneratedPathHistory> generatedHistory)
        {
            if (!generatedHistory.TryGetValue(current, out GeneratedPathHistory history))
                history = GeneratedPathHistory.Empty;
            if (history.ContainsOrigin(nextOrigin))
                return false;

            bool mismatch = false;
            nextProfile.AddWorldCorners(nextOrigin, (corner, height2) =>
            {
                if (history.TryGetCornerHeight(corner, out int existingHeight2)
                    && existingHeight2 != height2)
                    mismatch = true;
            });
            return !mismatch;
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
                    if (emitted.Add(handoff.Tile))
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
            out AccessHandoffOperation operation)
        {
            foreach (Tile2i direction in s_originDirections)
            {
                Tile2i connectedPredecessor = new Tile2i(
                    origin.X + direction.X, origin.Y + direction.Y);
                foreach (AccessGroundHandoff candidate in GetHandoffs(
                    snapshot, origin, profile, connectedPredecessor, profile))
                {
                    if (candidate.Tile != groundTile) continue;
                    operation = candidate.Operation;
                    return true;
                }
            }

            operation = AccessHandoffOperation.None;
            return false;
        }

        private static bool ContainsHandoffTile(
            AccessSearchSnapshot snapshot,
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i groundTile)
            => TryGetGroundToGeneratedHandoff(
                snapshot, origin, profile, groundTile, out _);

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

        private static bool TrySolveSuccessor(AccessHeightProfile current, Tile2i direction,
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
            bool hasCurrent = true)
        {
            if (distance.TryGetValue(next, out float existing) && existing <= nextCost + 0.0001f) return;
            distance[next] = nextCost;
            if (hasCurrent)
            {
                previous[next] = current;
                generatedHistory[next] = BuildGeneratedHistory(snapshot, current, next, generatedHistory);
            }
            else
            {
                generatedHistory[next] = GeneratedPathHistory.Empty;
            }
            float heuristic = GetHeuristic(
                next, snapshot, useAStarHeuristic, goalIndex);
            queue.Push(new QueueEntry(next, nextCost, nextCost + heuristic));
        }

        private static GeneratedPathHistory BuildGeneratedHistory(
            AccessSearchSnapshot snapshot,
            AccessSearchNode current,
            AccessSearchNode next,
            Dictionary<AccessSearchNode, GeneratedPathHistory> generatedHistory)
        {
            if (!generatedHistory.TryGetValue(current, out GeneratedPathHistory currentHistory))
                currentHistory = GeneratedPathHistory.Empty;
            if (next.IsGround || next.Mode == AccessSearchMode.Existing)
                return currentHistory;
            if (!TryGetProfile(snapshot, next, out AccessHeightProfile profile))
                return currentHistory;
            return currentHistory.WithGenerated(next.Position, profile);
        }

        private static float GetHeuristic(
            AccessSearchNode node,
            AccessSearchSnapshot snapshot,
            bool useAStarHeuristic,
            HeightAwareGoalIndex goalIndex)
        {
            if (!snapshot.UseAStar || !useAStarHeuristic)
                return 0f;
            return goalIndex.GetLowerBound(node.CostPosition, node.Height2);
        }

        internal sealed class HeightAwareGoalIndex
        {
            public static readonly HeightAwareGoalIndex Empty =
                new HeightAwareGoalIndex(default, 0, 0, Array.Empty<GoalHeightBand>());

            private readonly Tile2i m_boundsMin;
            private readonly int m_width;
            private readonly int m_height;
            private readonly GoalHeightBand[] m_bands;

            private HeightAwareGoalIndex(
                Tile2i boundsMin,
                int width,
                int height,
                GoalHeightBand[] bands)
            {
                m_boundsMin = boundsMin;
                m_width = width;
                m_height = height;
                m_bands = bands;
            }

            public static HeightAwareGoalIndex Build(
                AccessSearchSnapshot snapshot,
                IReadOnlyDictionary<int, List<Tile2i>> goalsByHeight2)
            {
                var bands = new List<GoalHeightBand>(goalsByHeight2.Count);
                foreach (KeyValuePair<int, List<Tile2i>> pair in goalsByHeight2)
                {
                    if (pair.Value.Count == 0) continue;
                    bands.Add(new GoalHeightBand(
                        pair.Key,
                        AccessSearchSnapshot.BuildGoalDistance(
                            snapshot.BoundsMin, snapshot.BoundsMax,
                            new HashSet<Tile2i>(pair.Value))));
                }
                return new HeightAwareGoalIndex(
                    snapshot.BoundsMin,
                    snapshot.GoalDistanceWidth,
                    snapshot.GoalDistanceHeight,
                    bands.ToArray());
            }

            public float GetLowerBound(Tile2i tile, int height2)
            {
                if (m_bands.Length == 0) return 0f;
                int x = tile.X - m_boundsMin.X;
                int y = tile.Y - m_boundsMin.Y;
                if (x < 0 || x >= m_width || y < 0 || y >= m_height)
                    return 0f;

                int index = y * m_width + x;
                int best = int.MaxValue;
                for (int i = 0; i < m_bands.Length; i++)
                {
                    int horizontalDistance = m_bands[i].Distances[index];
                    if (horizontalDistance < 0) continue;
                    int lowerBound = Math.Max(
                        horizontalDistance,
                        Math.Abs(height2 - m_bands[i].Height2));
                    if (lowerBound < best) best = lowerBound;
                }
                return best == int.MaxValue ? 0f : best;
            }

            private readonly struct GoalHeightBand
            {
                public int Height2 { get; }
                public int[] Distances { get; }

                public GoalHeightBand(int height2, int[] distances)
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
            out AccessLandscapingCost landscapingCost,
            out float fixedCost)
        {
            landscapingCost = new AccessLandscapingCost(
                EstimateDirectWorkCost(
                    profile.Center2,
                    snapshot.GetTerrainCenterHeight2(origin)));
            fixedCost = GENERATED_V_FIXED_OVERHEAD;
            return snapshot.LandscapingCostDistanceScale * landscapingCost.TotalCost
                + fixedCost;
        }

        private static float EstimateDirectWorkCost(int targetHeight2, int terrainHeight2)
        {
            float deltaHeight = Math.Abs(targetHeight2 - terrainHeight2) / 2f;
            // This uses the same 4x4-cell terrain-volume normalization as the Ore composition
            // panel's countedThick * 16 estimate. It is not a verified vanilla dump-truck
            // consumption rule; replace it if/when exact vanilla material consumption is sourced.
            return 16f * deltaHeight;
        }

        private static float GetCleanupEntryCost(AccessSearchSnapshot snapshot, Tile2i fromTile, Tile2i toTile)
        {
            if (!snapshot.TryGetCleanupInfoForTile(toTile, out AccessPropCleanupInfo info) || !info.IsEligible) return 0f;
            HashSet<string> fromKeys = new HashSet<string>(StringComparer.Ordinal);
            if (snapshot.TryGetCleanupInfoForTile(fromTile, out AccessPropCleanupInfo fromInfo)
                && fromInfo.IsEligible)
            {
                foreach (string key in GetCleanupCostKeys(fromInfo))
                    fromKeys.Add(key);
            }

            int newObjectCount = 0;
            var toKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in GetCleanupCostKeys(info))
            {
                if (!toKeys.Add(key) || fromKeys.Contains(key))
                    continue;
                newObjectCount++;
            }
            if (newObjectCount == 0 && info.Samples.Count == 0 && fromInfoMissingOrDifferentOrigin())
                newObjectCount = 1;
            return snapshot.LandscapingCostDistanceScale
                * AccessPropCleanupPolicy.GetCleanupLandscapingCost()
                * newObjectCount;

            bool fromInfoMissingOrDifferentOrigin()
                => !snapshot.TryGetCleanupInfoForTile(fromTile, out AccessPropCleanupInfo fallbackFromInfo)
                    || fallbackFromInfo.Origin != info.Origin;
        }

        private static IEnumerable<string> GetCleanupCostKeys(AccessPropCleanupInfo info)
        {
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (AccessPropSample sample in info.Samples)
            {
                string? key = sample.IsTree
                    ? "tree:" + sample.CleanupObjectKey
                    : sample.IsDenseDebris
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
            AccessReachedGoalKind reachedGoalKind = AccessReachedGoalKind.None)
        {
            float traversal = 0f, generated = 0f, fixedCost = 0f, tree = 0f, dense = 0f;
            var chargedCleanup = new HashSet<string>(StringComparer.Ordinal);
            AccessSearchNode predecessor = startNode;
            foreach (AccessSearchNode node in path)
            {
                traversal += Manhattan(predecessor.CostPosition, node.CostPosition);
                if (node.IsGround)
                {
                    if (snapshot.TryGetCleanupInfoForTile(node.Position, out AccessPropCleanupInfo info)
                        && info.IsEligible)
                    {
                        float cleanup = snapshot.LandscapingCostDistanceScale * AccessPropCleanupPolicy.GetCleanupLandscapingCost();
                        foreach (string key in GetCleanupCostKeys(info))
                        {
                            if (!chargedCleanup.Add(key))
                                continue;
                            if (key.StartsWith("tree:", StringComparison.Ordinal))
                                tree += cleanup;
                            else
                                dense += cleanup;
                        }
                    }
                }
                else if (node.Mode != AccessSearchMode.Existing)
                {
                    if (TryGetProfile(snapshot, node, out AccessHeightProfile profile))
                    {
                        CalculateGeneratedEntryCost(
                            snapshot, node.Position, profile,
                            out AccessLandscapingCost landscapingCost,
                            out float generatedFixedCost);
                        generated += snapshot.LandscapingCostDistanceScale
                            * landscapingCost.TotalCost;
                        fixedCost += generatedFixedCost;
                    }
                }
                predecessor = node;
            }
            return new AccessSearchResult(success, failureReason, startOrigin, path, cost, visited, rejections,
                traversal, generated, fixedCost, tree, dense, reachedGoalKind);
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

        private static bool ValidateGeneratedPath(IReadOnlyList<AccessSearchNode> path,
            AccessSearchSnapshot snapshot, out string reason)
        {
            var profilesByOrigin = new Dictionary<Tile2i, AccessHeightProfile>();
            var cornerHeights = new Dictionary<Tile2i, int>();
            foreach (AccessSearchNode node in path)
            {
                if (node.IsGround || node.Mode == AccessSearchMode.Existing) continue;
                if (!TryGetProfile(snapshot, node, out AccessHeightProfile profile)) continue;
                if (profilesByOrigin.ContainsKey(node.Position))
                {
                    reason = $"FinalOriginRevisit@{node.Position}";
                    return false;
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
            }
            reason = string.Empty;
            return true;
        }

        private static int Manhattan(Tile2i a, Tile2i b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static void Reject(Dictionary<string, int> rejections, string reason)
            => rejections[reason] = rejections.TryGetValue(reason, out int count) ? count + 1 : 1;

        private static AccessSearchResult Failed(string reason, Tile2i start, int visited,
            Dictionary<string, int> rejections)
            => new AccessSearchResult(false, reason, start, Array.Empty<AccessSearchNode>(), 0f, visited, rejections);

        internal sealed class GeneratedPathHistory
        {
            public static readonly GeneratedPathHistory Empty = new GeneratedPathHistory(
                new HashSet<Tile2i>(),
                new Dictionary<Tile2i, int>());

            private readonly HashSet<Tile2i> m_origins;
            private readonly Dictionary<Tile2i, int> m_cornerHeights;

            private GeneratedPathHistory(HashSet<Tile2i> origins, Dictionary<Tile2i, int> cornerHeights)
            {
                m_origins = origins;
                m_cornerHeights = cornerHeights;
            }

            public bool ContainsOrigin(Tile2i origin) => m_origins.Contains(origin);

            public bool TryGetCornerHeight(Tile2i corner, out int height2)
                => m_cornerHeights.TryGetValue(corner, out height2);

            public GeneratedPathHistory WithGenerated(Tile2i origin, AccessHeightProfile profile)
            {
                var origins = new HashSet<Tile2i>(m_origins) { origin };
                var cornerHeights = new Dictionary<Tile2i, int>(m_cornerHeights);
                profile.AddWorldCorners(origin, (corner, height2) => cornerHeights[corner] = height2);
                return new GeneratedPathHistory(origins, cornerHeights);
            }
        }

        internal readonly struct QueueEntry
        {
            public AccessSearchNode Node { get; }
            public float PathCost { get; }
            public float Priority { get; }
            public QueueEntry(AccessSearchNode node, float pathCost, float priority)
            { Node = node; PathCost = pathCost; Priority = priority; }
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

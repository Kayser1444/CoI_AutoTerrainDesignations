using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core.PathFinding;

namespace AutoTerrainDesignations.Access
{
    internal static class AccessCaptureFixtures
    {
        internal static bool ValidateAll(out string failure)
        {
            var budget = new AccessCaptureMemoryBudget(1024L * 1024L);
            if (!budget.TryAccept(512L * 1024L)
                || budget.EstimatedRetainedBytes != 512L * 1024L
                || budget.TryAccept(2L * 1024L * 1024L)
                || budget.EstimatedRetainedBytes != 2L * 1024L * 1024L)
            {
                failure =
                    "Snapshot memory budget did not fail closed at its configured ceiling.";
                return false;
            }

            IDisposable? captureLease = AccessCaptureBackpressure.TryAcquire();
            IDisposable? secondLease = AccessCaptureBackpressure.TryAcquire();
            if (captureLease == null || secondLease != null)
            {
                secondLease?.Dispose();
                captureLease?.Dispose();
                failure =
                    "Capture backpressure did not enforce a single in-flight snapshot.";
                return false;
            }
            captureLease.Dispose();
            IDisposable? reacquiredLease = AccessCaptureBackpressure.TryAcquire();
            if (reacquiredLease == null)
            {
                failure =
                    "Capture backpressure did not release its slot after completion.";
                return false;
            }
            // The fixture's final lease is intentionally released immediately;
            // production capture uses the iterator finally block for this.
            reacquiredLease.Dispose();

            var start = new AccessCaptureRevision(
                worldGeneration: 7,
                terrainDesignationRevision: 11,
                policyFingerprint: 13);
            var diagnostics = new AccessCaptureDiagnostics(
                start,
                4L * 1024L * 1024L);
            if (AccessCaptureRevisionPolicy.Classify(
                    start,
                    new AccessCaptureRevision(8, 11, 13))
                != AccessCaptureInvalidationKind.HardInvalidation
                || AccessCaptureRevisionPolicy.Classify(
                    start,
                    new AccessCaptureRevision(7, 12, 13))
                    != AccessCaptureInvalidationKind.EnvironmentalDirty
                || AccessCaptureRevisionPolicy.Classify(start, start)
                    != AccessCaptureInvalidationKind.None)
            {
                failure =
                    "Capture revision policy did not separate hard and environmental changes.";
                return false;
            }

            var towerSettings = new AccessRequestSettingsRevision(
                rampWidth: 2,
                clearanceMode: AutoTerrainDesignations.AccessVehicleClearanceMode.T3,
                planningSettingsFingerprint: 19);
            if (towerSettings != new AccessRequestSettingsRevision(
                    rampWidth: 2,
                    clearanceMode: AutoTerrainDesignations.AccessVehicleClearanceMode.T3,
                    planningSettingsFingerprint: 19)
                || towerSettings == new AccessRequestSettingsRevision(
                    rampWidth: 1,
                    clearanceMode: AutoTerrainDesignations.AccessVehicleClearanceMode.T1,
                    planningSettingsFingerprint: 19))
            {
                failure =
                    "Tower-local access settings revision did not detect a T3-to-T1 change.";
                return false;
            }

            var occupiedTiles = new HashSet<Tile2i>
            {
                new Tile2i(4, 8)
            };
            var fixedHeights = new Dictionary<Tile2i, HashSet<int>>
            {
                [new Tile2i(4, 8)] = new HashSet<int> { 6 }
            };
            var layoutOccupancies = new Dictionary<Tile2i,
                List<AccessCapturedLayoutOccupancy>>
            {
                [new Tile2i(4, 8)] =
                    new List<AccessCapturedLayoutOccupancy>
                    {
                        new AccessCapturedLayoutOccupancy(2, 3, 3f)
                    }
            };
            AccessCapturedBuildingFacts buildingFacts =
                AccessCapturedBuildingFacts.Capture(
                    occupiedTiles, fixedHeights, layoutOccupancies);
            occupiedTiles.Clear();
            fixedHeights[new Tile2i(4, 8)].Clear();
            layoutOccupancies[new Tile2i(4, 8)].Clear();
            if (buildingFacts.OccupiedTileCount != 1
                || !buildingFacts.ContainsOccupiedTile(new Tile2i(4, 8))
                || !buildingFacts.FixedHeights2ByTile.TryGetValue(
                    new Tile2i(4, 8), out HashSet<int>? capturedHeights)
                || !capturedHeights.Contains(6)
                || !buildingFacts.LayoutOccupanciesByTile.TryGetValue(
                    new Tile2i(4, 8),
                    out AccessCapturedLayoutOccupancy[]? capturedOccupancies)
                || capturedOccupancies.Length != 1
                || !buildingFacts.DoesOriginOverlap(new Tile2i(4, 8)))
            {
                failure =
                    "Building occupancy capture did not detach from live mutable collections.";
                return false;
            }

            Tile2i[] oddClearanceCenters =
                AccessPropCleanupFootprint.EnumerateBlockedCenters(
                    new Tile2i(10, 10),
                    clearance: 5,
                    boundsMin: new Tile2i(8, 8),
                    boundsMax: new Tile2i(11, 11))
                .ToArray();
            Tile2i[] evenClearanceCenters =
                AccessPropCleanupFootprint.EnumerateBlockedCenters(
                    new Tile2i(10, 10),
                    clearance: 4,
                    boundsMin: new Tile2i(0, 0),
                    boundsMax: new Tile2i(20, 20))
                .ToArray();
            if (oddClearanceCenters.Length != 16
                || oddClearanceCenters[0] != new Tile2i(8, 8)
                || oddClearanceCenters[15] != new Tile2i(11, 11)
                || evenClearanceCenters.Length != 16
                || evenClearanceCenters[0] != new Tile2i(9, 9)
                || evenClearanceCenters[15] != new Tile2i(12, 12))
            {
                failure =
                    "Cleanup footprint expansion did not preserve vehicle-center geometry and bounds.";
                return false;
            }
            Tile2i[] footprintFixtureTiles =
            {
                new Tile2i(-3, 4),
                new Tile2i(10, 10),
                new Tile2i(20, -7),
            };
            for (int clearance = 1; clearance <= 8; clearance++)
            {
                foreach (Tile2i occupiedTile in footprintFixtureTiles)
                {
                    Tile2i[] directCenters =
                        AccessPropCleanupFootprint.EnumerateBlockedCenters(
                            occupiedTile,
                            clearance,
                            new Tile2i(-5, -5),
                            new Tile2i(15, 15))
                        .ToArray();
                    Tile2i[] referenceCenters =
                        EnumerateReferenceBlockedCenters(
                            occupiedTile,
                            clearance,
                            new Tile2i(-5, -5),
                            new Tile2i(15, 15));
                    if (!directCenters.SequenceEqual(referenceCenters))
                    {
                        failure =
                            "Cleanup footprint expansion diverged from vehicle corner-space conversion.";
                        return false;
                    }
                }
            }

            Tile2i cleanupOrigin = new Tile2i(8, 8);
            var cleanupSamples = new[]
            {
                new AccessPropSample(
                    new Tile2i(9, 9), true, false, true,
                    cleanupObjectKey: "tree:fixture"),
                new AccessPropSample(
                    new Tile2i(10, 9), false, true, true,
                    cleanupObjectKey: "prop:fixture"),
            };
            AccessPropCleanupInfo policyCleanup =
                AccessPropCleanupPolicy.BuildOriginInfo(
                    cleanupOrigin,
                    cleanupSamples,
                    AccessPropBlockerKind.Durability);
            var cleanupAccumulator =
                new AccessCapturedPropCleanupAccumulator(cleanupOrigin);
            cleanupAccumulator.Add(
                cleanupSamples[0], AccessPropBlockerKind.None);
            cleanupAccumulator.Add(
                cleanupSamples[1], AccessPropBlockerKind.Durability);
            AccessPropCleanupInfo accumulatedCleanup =
                cleanupAccumulator.BuildInfo();
            if (accumulatedCleanup.Origin != policyCleanup.Origin
                || accumulatedCleanup.Classes != policyCleanup.Classes
                || accumulatedCleanup.BlockerKind != policyCleanup.BlockerKind
                || accumulatedCleanup.UsesTerrainRemovalPolicy
                    != policyCleanup.UsesTerrainRemovalPolicy
                || accumulatedCleanup.Samples.Count != policyCleanup.Samples.Count
                || accumulatedCleanup.Samples[0].CleanupObjectKey
                    != policyCleanup.Samples[0].CleanupObjectKey
                || accumulatedCleanup.Samples[1].CleanupObjectKey
                    != policyCleanup.Samples[1].CleanupObjectKey)
            {
                failure =
                    "Captured cleanup accumulation changed cleanup policy output.";
                return false;
            }

            Tile2i readinessTile = new Tile2i(12, 16);
            Tile2i clearTile = new Tile2i(13, 16);
            var readinessFacts = new AccessDesignationReadinessFacts(
                new Dictionary<Tile2i, float>
                {
                    [readinessTile] = 5f
                },
                new Dictionary<Tile2i, float>
                {
                    [readinessTile] = 4f
                },
                new Dictionary<Tile2i,
                    AccessCapturedLayoutOccupancy[]>
                {
                    [readinessTile] = new[]
                    {
                        new AccessCapturedLayoutOccupancy(2, 3, 3f)
                    }
                });
            if (readinessFacts.FactCount != 3
                || readinessFacts.IsMiningFulfilled(
                    readinessTile, 2f, 2f, upperEdge: false)
                || readinessFacts.IsMiningFulfilled(
                    readinessTile, 3f, 2f, upperEdge: true)
                || !readinessFacts.IsMiningFulfilled(
                    readinessTile, 2f, 2f, upperEdge: true)
                || !readinessFacts.IsMiningFulfilled(
                    clearTile, 2f, 2f, upperEdge: false)
                || !readinessFacts.IsDumpingFulfilled(
                    readinessTile, 2f, 3f)
                || !readinessFacts.IsDumpingFulfilled(
                    readinessTile, 2.9f, 3f)
                || readinessFacts.IsDumpingFulfilled(
                    readinessTile, 1f, 3f)
                || readinessFacts.IsDumpingFulfilled(
                    readinessTile, 2f, 4f))
            {
                failure =
                    "Captured designation-readiness facts did not match vanilla mining/dumping rules.";
                return false;
            }

            var lowerObstacleFacts = new AccessDesignationReadinessFacts(
                new Dictionary<Tile2i, float>
                {
                    [readinessTile] = 1f
                },
                new Dictionary<Tile2i, float>
                {
                    [readinessTile] = 1f
                });
            if (!lowerObstacleFacts.IsMiningFulfilled(
                    readinessTile, 2f, 2f, upperEdge: false))
            {
                failure =
                    "Mining readiness must ignore props and stumps below the target height.";
                return false;
            }

            diagnostics.SetEstimatedRetainedBytes(3L * 1024L * 1024L);
            diagnostics.MarkEnvironmentallyDirty(
                new AccessCaptureRevision(
                    worldGeneration: 7,
                    terrainDesignationRevision: 12,
                    policyFingerprint: 13),
                "FixtureDesignationChange");
            diagnostics.ObserveCompletion(
                new AccessCaptureRevision(
                    worldGeneration: 7,
                    terrainDesignationRevision: 12,
                    policyFingerprint: 13));
            if (!diagnostics.IsEnvironmentallyDirty
                || diagnostics.DirtyReason != "FixtureDesignationChange"
                || diagnostics.CompletionRevision.TerrainDesignationRevision != 12
                || diagnostics.EstimatedRetainedBytes != 3L * 1024L * 1024L)
            {
                failure =
                    "Environmental capture dirtiness did not retain source and completion revisions.";
                return false;
            }

            long smallEstimate =
                AccessSnapshotMemoryEstimator.EstimateRetainedBytes(
                    64, 16, 16, 4, 4, 32, 8, 4, 12, 2, 4, 8);
            long largeEstimate =
                AccessSnapshotMemoryEstimator.EstimateRetainedBytes(
                    128, 32, 32, 8, 8, 64, 16, 8, 24, 4, 8, 16);
            if (smallEstimate <= 0
                || largeEstimate <= smallEstimate
                || AccessSnapshotMemoryEstimator.EstimateRetainedBytes(
                    long.MaxValue, long.MaxValue, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
                    != long.MaxValue)
            {
                failure =
                    "Snapshot memory estimation was not positive, monotonic, and saturating.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static Tile2i[] EnumerateReferenceBlockedCenters(
            Tile2i occupiedTile,
            int clearance,
            Tile2i boundsMin,
            Tile2i boundsMax)
        {
            var centers = new List<Tile2i>();
            var requiredClearance = new RelTile1i(clearance);
            for (int y = occupiedTile.Y - clearance;
                y <= occupiedTile.Y + clearance;
                y++)
            {
                for (int x = occupiedTile.X - clearance;
                    x <= occupiedTile.X + clearance;
                    x++)
                {
                    var center = new Tile2i(x, y);
                    if (center.X < boundsMin.X || center.X > boundsMax.X
                        || center.Y < boundsMin.Y || center.Y > boundsMax.Y)
                        continue;
                    Tile2i corner =
                        VehiclePathFindingParams.ConvertToCornerTileSpace(
                            center,
                            requiredClearance);
                    if (occupiedTile.X >= corner.X
                        && occupiedTile.X < corner.X + clearance
                        && occupiedTile.Y >= corner.Y
                        && occupiedTile.Y < corner.Y + clearance)
                        centers.Add(center);
                }
            }
            return centers.ToArray();
        }
    }
}

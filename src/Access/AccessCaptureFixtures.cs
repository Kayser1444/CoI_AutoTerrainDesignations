using System;
using System.Collections.Generic;
using Mafi;

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
    }
}

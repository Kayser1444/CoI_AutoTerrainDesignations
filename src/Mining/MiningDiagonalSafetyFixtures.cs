using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerrainDesignations.Planning;
using Mafi;

namespace AutoTerrainDesignations.Mining
{
    internal static partial class MiningFixtures
    {
        private static bool ValidateDiagonalSafety(out string failure)
        {
            foreach (bool building in new[] { false, true })
            foreach (int dx in new[] { -1, 1 })
            foreach (int dy in new[] { -1, 1 })
            foreach (int buffer in new[] { 0, 2 })
            {
                var origin = new Tile2i(20, 20);
                var corner = new Tile2i(dx < 0 ? 20 : 24, dy < 0 ? 20 : 24);
                // At step 4 the ray is at height 0. Scaling diagonal rise by
                // sqrt(2) would incorrectly clear sea level before this contact.
                // With a buffer, put the hazard two steps past terrain contact.
                int distance = buffer == 0 ? 4 : 8;
                var hazard = corner + new RelTile2i(dx * distance, dy * distance);
                var policy = new MiningPolicy(0, 1, 0, null, 0, 0, 0, 0, 0,
                    false, 5, false, building, !building, buffer, 0, 1f, 1f);
                var truth = new Dictionary<Tile2i, CapturedTerrainColumn>();
                for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                {
                    var tile = new Tile2i(x, y);
                    bool ocean = !building && tile == hazard;
                    float surface = ocean ? -1f : 2f;
                    truth.Add(tile, new CapturedTerrainColumn(surface, ocean, new[] {
                        new CapturedTerrainLayer("Rock", "Rock", surface, surface - 1, 1, 0, 1, false),
                        new CapturedTerrainLayer("Ore", "Ore", surface - 1, -4, surface + 3, 1, 1, false),
                        new CapturedTerrainLayer("Bedrock", "Rock", -4, -40, 36, surface + 4, 1, true)
                    }));
                }
                var buildings = new HashSet<Tile2i>();
                if (building) buildings.Add(hazard);
                var request = new MiningRequest(new[] { origin }, new[] { "Ore" }, policy,
                    new Tile2i(48, 48), truth, buildings);
                string label = $"Diagonal {(building ? "building" : "ocean")} ({dx},{dy}) buffer={buffer}";
                MiningPlan direct = MiningPlanner.Execute(request, MiningStage.DirectSafety);
                if (direct.Depths.Count != 1)
                { failure = label + ": hazard must be outside direct protection."; return false; }
                MiningPlan complete = MiningPlanner.Execute(request);
                if (complete.Depths.Count != 0 || !complete.ReconcileEmpty)
                { failure = label + ": cardinal-only plan misses the corner hazard."; return false; }

                // Capture starts without the diagonal samples. The shared iterator
                // must request and collect them before Complete is allowed to pass.
                var captured = new Dictionary<Tile2i, CapturedTerrainColumn>();
                foreach (Tile2i tile in MiningPlanner.Cells(origin)) captured[tile] = truth[tile];
                var staged = new MiningRequest(request.Origins, request.ProductIds, policy,
                    request.TerrainSize, captured, buildings);
                foreach (Tile2i tile in MiningSafety.DirectFacts(staged, direct)) captured[tile] = truth[tile];
                int batches = 0;
                while (true)
                {
                    MiningPlan next = MiningPlanner.Execute(staged.Seal(), MiningStage.SafetyCoverage);
                    if (!next.NeedsSafetyCoverage)
                    {
                        if (!MiningReplayFacade.Canonical(next).SequenceEqual(MiningReplayFacade.Canonical(complete)))
                        { failure = label + ": staged capture changed geometry."; return false; }
                        break;
                    }
                    if (++batches > 4)
                    { failure = label + ": capture did not converge."; return false; }
                    bool failedClosed = false;
                    try { MiningPlanner.Execute(staged.Seal()); }
                    catch (InvalidOperationException) { failedClosed = true; }
                    if (!failedClosed)
                    { failure = label + ": missing diagonal facts were accepted by Complete."; return false; }
                    foreach (Tile2i tile in MiningSafety.TraceExterior(staged, next, new List<Tile2i>()))
                        captured[tile] = truth[tile];
                }
                if (!captured.ContainsKey(hazard))
                { failure = label + ": hazard was not included in capture coverage."; return false; }

                // Removing only the hazard must restore the otherwise safe work.
                buildings.Clear();
                truth[hazard] = truth[origin];
                if (MiningPlanner.Execute(request).Depths.Count != 1)
                { failure = label + ": unobstructed diagonals removed safe work."; return false; }
            }
            if (!ValidateDiagonalMapBounds(out failure)) return false;
            failure = string.Empty;
            return true;
        }

        private static bool ValidateDiagonalMapBounds(out string failure)
        {
            var policy = new MiningPolicy(0, 1, 0, null, 0, 0, 0, 0, 0,
                false, 5, false, true, true, 2, 0, 1f, 1f);
            var columns = new Dictionary<Tile2i, CapturedTerrainColumn>();
            var column = new CapturedTerrainColumn(20, false, new[] {
                new CapturedTerrainLayer("Ore", "Ore", 20, -40, 60, 0, 1, false),
                new CapturedTerrainLayer("Bedrock", "Rock", -40, -100, 60, 60, 1, true)
            });
            // Non-square map: each diagonal must stop at the nearer of X and Y.
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 48; x++) columns.Add(new Tile2i(x, y), column);
            foreach (int y in new[] { 0, 12, 24 })
            foreach (int x in new[] { 0, 20, 40 })
            {
                var request = new MiningRequest(new[] { new Tile2i(x, y) }, new[] { "Ore" },
                    policy, new Tile2i(48, 32), columns, new HashSet<Tile2i>());
                MiningPlan plan = MiningPlanner.Execute(request);
                var rejected = new List<Tile2i>();
                foreach (Tile2i tile in MiningSafety.TraceExterior(request, plan, rejected))
                    if (!request.IsValid(tile))
                    { failure = "Diagonal safety sampled beyond the map: " + tile; return false; }
                if (plan.Depths.Count != 1 || rejected.Count != 0)
                { failure = "Diagonal map-edge check removed unobstructed work."; return false; }
            }
            failure = string.Empty;
            return true;
        }
    }
}

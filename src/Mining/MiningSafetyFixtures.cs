using System;
using System.Collections.Generic;
using System.Linq;
using AutoTerrainDesignations.Planning;
using Mafi;

namespace AutoTerrainDesignations.Mining
{
    internal static partial class MiningFixtures
    {
        // Several connected columns hide unsafe west faces behind neighbors that
        // the first exterior pass removes. Both protections must inspect the new rim.
        private static bool ValidateExteriorConvergence(bool building, out string failure)
        {
            var policy = new MiningPolicy(0, 1, 0, null, 0, 0, 0, 0, 0,
                false, 5, false, building, !building, 0, 0, 1f, 1f);
            var truth = new Dictionary<Tile2i, CapturedTerrainColumn>();
            var buildings = new HashSet<Tile2i>();
            for (int y = 0; y < 96; y++)
            for (int x = 0; x < 96; x++)
            {
                bool ocean = !building && x <= 15;
                float surface = ocean ? -1f : 10f;
                truth.Add(new Tile2i(x, y), new CapturedTerrainColumn(surface, ocean, new[] {
                    new CapturedTerrainLayer("Rock", "Rock", surface, surface - 1, 1, 0, 1, false),
                    new CapturedTerrainLayer("Ore", "Ore", surface - 1, -20, surface + 19, 1, 1, false),
                    new CapturedTerrainLayer("Bedrock", "Rock", -20, -100, 80, surface + 20, 1, true)
                }));
                if (building && x == 15) buildings.Add(new Tile2i(x, y));
            }
            var origins = new List<Tile2i>();
            for (int y = 20; y <= 28; y += 4)
            for (int x = 20; x <= 48; x += 4) origins.Add(new Tile2i(x, y));
            var request = new MiningRequest(origins.ToArray(), new[] { "Ore" }, policy,
                new Tile2i(96, 96), truth, buildings);
            MiningPlan plan = MiningPlanner.Execute(request);
            var rejected = new List<Tile2i>();
            foreach (Tile2i tile in MiningSafety.TraceExterior(request, plan, rejected)) request.Column(tile);
            string kind = building ? "Building" : "Ocean";
            if (rejected.Count > 0)
            { failure = $"{kind}: final plan leaves {rejected.Count} unsafe designations after boundary removal."; return false; }
            int expected = building ? 3 : 12;
            if (plan.Depths.Count != expected)
            { failure = $"{kind}: expected {expected} safe survivors, got {plan.Depths.Count}."; return false; }
            var reversed = new MiningRequest(origins.AsEnumerable().Reverse().ToArray(), request.ProductIds,
                policy, request.TerrainSize, truth, buildings);
            if (!MiningReplayFacade.Canonical(plan).SequenceEqual(MiningReplayFacade.Canonical(MiningPlanner.Execute(reversed))))
            { failure = kind + ": protection depends on origin enumeration order."; return false; }

            // Exercise the live capture protocol with initially body-only coverage.
            // Newly exposed faces must request more facts, never silently pass.
            var captured = new Dictionary<Tile2i, CapturedTerrainColumn>();
            foreach (Tile2i origin in origins)
                foreach (Tile2i tile in MiningPlanner.Cells(origin)) captured[tile] = truth[tile];
            var staged = new MiningRequest(request.Origins, request.ProductIds, policy,
                request.TerrainSize, captured, buildings);
            MiningPlan body = MiningPlanner.Execute(staged, MiningStage.Body);
            foreach (Tile2i tile in MiningSafety.DirectFacts(staged, body)) captured[tile] = truth[tile];
            MiningPlan discovered;
            int batches = 0;
            while (true)
            {
                MiningRequest sealedInput = staged.Seal();
                discovered = MiningPlanner.Execute(sealedInput, MiningStage.SafetyCoverage);
                if (!discovered.NeedsSafetyCoverage) break;
                if (++batches > origins.Count + 1)
                { failure = kind + ": safety capture did not converge."; return false; }
                bool failedClosed = false;
                try { MiningPlanner.Execute(sealedInput); }
                catch (InvalidOperationException) { failedClosed = true; }
                if (!failedClosed)
                { failure = kind + ": incomplete later-pass facts were accepted by Complete."; return false; }
                int before = captured.Count;
                foreach (Tile2i tile in MiningSafety.TraceExterior(staged, discovered, new List<Tile2i>()))
                    captured[tile] = truth[tile];
                if (captured.Count == before || !MiningPlanner.Execute(sealedInput, MiningStage.SafetyCoverage).NeedsSafetyCoverage)
                { failure = kind + ": capture made no progress or mutated a sealed worker input."; return false; }
            }
            if (batches < 2 || !MiningReplayFacade.Canonical(plan).SequenceEqual(MiningReplayFacade.Canonical(discovered))
                || !MiningReplayFacade.Canonical(plan).SequenceEqual(MiningReplayFacade.Canonical(MiningPlanner.Execute(staged.Seal()))))
            { failure = kind + ": multi-pass staged capture differs from complete terrain."; return false; }

            var small = new MiningRequest(origins.Take(2).ToArray(), request.ProductIds,
                policy, request.TerrainSize, truth, buildings);
            MiningPlan empty = MiningPlanner.Execute(small);
            if (empty.Depths.Count != 0 || empty.Corners.Count != 0 || !empty.ReconcileEmpty || empty.Outcome != "SafetyRemovedAll")
            { failure = kind + ": all-unsafe multi-pass plan did not reconcile empty."; return false; }
            var disabled = new MiningPolicy(0, 1, 0, null, 0, 0, 0, 0, 0,
                false, 5, false, false, false, 0, 0, 1f, 1f);
            if (MiningPlanner.Execute(new MiningRequest(request.Origins, request.ProductIds,
                    disabled, request.TerrainSize, truth, buildings)).Depths.Count != origins.Count)
            { failure = kind + ": disabled protection removed designations."; return false; }
            failure = string.Empty;
            return true;
        }
    }
}

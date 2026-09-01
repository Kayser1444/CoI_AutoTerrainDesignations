using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using AutoTerrainDesignations.Access.Worker;
using AutoTerrainDesignations.Planning;
using Mafi;

namespace AutoTerrainDesignations.Mining
{
    internal static class MiningFixtures
    {
        internal static bool ValidateSafetyAndWorker(out string failure)
        {
            if (!ValidateSpikeFilter(out failure)) return false;
            var inspected = new HashSet<Type>();
            if (!Pure(typeof(MiningRequest)) || !Pure(typeof(MiningPlan)))
            { failure = "Mining publication retains a live game reference or delegate."; return false; }
            MiningRequest? workerInput = null;
            for (int scenario = 0; scenario < 3; scenario++)
            {
                var policy = new MiningPolicy(0, 1, 0, null, 0, 0, 0, 0, 0,
                    false, 5, false, true, true, 1, 0, 1f, 1f);
                var truth = new Dictionary<Tile2i, CapturedTerrainColumn>();
                for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    truth.Add(new Tile2i(x, y), new CapturedTerrainColumn(40,
                        scenario == 2 && x == 21 && y == 21, new[] {
                            new CapturedTerrainLayer("Rock", "Rock", 40, 39, 1, 0, 1, false),
                            new CapturedTerrainLayer("Ore", "Ore", 39, 34, 5, 1, 1, false),
                            new CapturedTerrainLayer("Rock", "Rock", 34, 0, 34, 6, 1, false),
                            new CapturedTerrainLayer("Bedrock", "Rock", 0, -10, 10, 40, 1, true)
                        }));
                var buildings = new HashSet<Tile2i>();
                if (scenario == 1) buildings.Add(new Tile2i(15, 20));
                var origins = new[] { new Tile2i(20, 20) };
                var captured = new Dictionary<Tile2i, CapturedTerrainColumn>();
                foreach (Tile2i cell in MiningPlanner.Cells(origins[0])) captured.Add(cell, truth[cell]);
                var input = new MiningRequest(origins, new[] { "Ore" }, policy,
                    new Tile2i(64, 64), captured, buildings);
                MiningPlan body = MiningPlanner.Execute(input, MiningStage.Body);
                foreach (Tile2i fact in MiningSafety.DirectFacts(input, body)) captured[fact] = truth[fact];
                MiningPlan direct = MiningPlanner.Execute(input, MiningStage.DirectSafety);
                foreach (Tile2i fact in MiningSafety.TraceExterior(input, direct, new List<Tile2i>()))
                    captured[fact] = truth[fact];
                MiningPlan actual = MiningPlanner.Execute(input);
                int expectedCount = scenario == 0 ? 1 : 0;
                if (actual.Depths.Count != expectedCount)
                { failure = "Mining safety scenario " + scenario; return false; }
                MiningPlan completeFacts = MiningPlanner.Execute(new MiningRequest(origins, new[] { "Ore" },
                    policy, new Tile2i(64, 64), truth, buildings));
                if (!MiningReplayFacade.Canonical(actual).SequenceEqual(MiningReplayFacade.Canonical(completeFacts)))
                { failure = "Staged safety capture changed geometry."; return false; }
                if (scenario == 0)
                {
                    workerInput = input.Seal();
                    Tile2i missing = new Tile2i(18, 20);
                    CapturedTerrainColumn saved = captured[missing];
                    captured.Remove(missing);
                    MiningPlanner.Execute(workerInput);
                    bool rejected = false;
                    try { MiningPlanner.Execute(input); }
                    catch (InvalidOperationException) { rejected = true; }
                    captured.Add(missing, saved);
                    if (!rejected) { failure = "Missing exterior terrain did not fail closed."; return false; }
                    CapturedTerrainColumn column = truth[origins[0]];
                    Access.AccessTerrainColumn access = column.ToAccessColumn();
                    for (float height = -11; height <= 41; height += 0.25f)
                    {
                        bool miningFound = column.TryGetSlope(height, out float miningSlope);
                        bool accessFound = access.TryGetNormalSlopeAt(height, out float accessSlope, out _);
                        if (miningFound != accessFound || miningSlope != accessSlope)
                        { failure = "Common capture projection changed access slope lookup."; return false; }
                    }
                }
            }
            AccessSearchWorker worker = AccessSearchWorker.Shared;
            const int world = 654321;
            worker.SetCurrentWorld(world);
            const long id = long.MaxValue - 1;
            if (!worker.TrySubmit(new AccessSearchWorkerJob(id, world, workerInput!, MiningStage.Complete), out failure))
                return false;
            Stopwatch wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 5000)
            {
                if (worker.TryConsumeTerminal(id, world, out AccessSearchWorkerTerminal? terminal))
                {
                    if (terminal!.MiningPlan == null || !MiningReplayFacade.Canonical(terminal.MiningPlan)
                        .SequenceEqual(MiningReplayFacade.Canonical(MiningPlanner.Execute(workerInput!))))
                    { failure = "Shared worker mining result differs: " + terminal.Fault; return false; }
                    var cancel = new CancelledControl();
                    try { MiningPlanner.Execute(workerInput!, MiningStage.Complete, cancel); }
                    catch (OperationCanceledException) { failure = string.Empty; return true; }
                    failure = "Cancelled mining produced a plan.";
                    return false;
                }
                Thread.Sleep(1);
            }
            worker.Abandon(id, "FixtureTimeout");
            failure = "Mining worker fixture timed out.";
            return false;

            bool Pure(Type type)
            {
                if (!inspected.Add(type)) return true;
                if (typeof(Delegate).IsAssignableFrom(type)
                    || (type.Namespace?.StartsWith("Mafi.Core", StringComparison.Ordinal) ?? false)
                    || (type.Namespace?.StartsWith("UnityEngine", StringComparison.Ordinal) ?? false)) return false;
                if (type.IsArray) return Pure(type.GetElementType()!);
                if (type.IsGenericType && type.GetGenericArguments().Any(argument => !Pure(argument))) return false;
                if (type.IsPrimitive || type.IsEnum || type == typeof(string)
                    || type.Assembly != typeof(MiningRequest).Assembly) return true;
                return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .All(field => Pure(field.FieldType));
            }
        }

        private static bool ValidateSpikeFilter(out string failure)
        {
            const string ore = "Ore";
            var origins = new[] { new Tile2i(8, 8) };
            var columns = new Dictionary<Tile2i, CapturedTerrainColumn>();
            for (int y = 7; y <= 12; y++)
            for (int x = 7; x <= 12; x++)
                columns.Add(new Tile2i(x, y), FilterFixtureColumn(34f));

            Tile2i spike = new Tile2i(9, 9);
            columns[spike] = FilterFixtureColumn(18f);
            var targets = new HashSet<string> { ore };
            MiningRequest enabled = FilterFixtureRequest(true, origins, columns);
            var restored = (MiningRequest)Access.AccessReplayGraphCodec.Deserialize(
                Access.AccessReplayGraphCodec.Serialize(enabled), typeof(MiningRequest));
            if (!restored.Policy.FilterOreSpikes)
            { failure = "Ore-spike policy did not survive replay serialization."; return false; }
            Dictionary<Tile2i, float> cutoffs = MiningSpikeFilter.BuildOreBottomCutoffs(
                enabled, targets, MiningSpikeFilterParameters.VanillaCorrection);
            if (cutoffs.Count != 1 || !cutoffs.TryGetValue(spike, out float cutoff)
                || Math.Abs(cutoff - 30f) > 0.0001f)
            { failure = "Ore-spike r4 fixture produced the wrong cutoff."; return false; }

            CapturedTerrainLayer spikeOre = columns[spike].LayerAt(1);
            float visible = MiningSpikeFilter.VisibleTargetThickness(
                spikeOre, true, true, cutoff);
            if (Math.Abs(visible - 9f) > 0.0001f)
            { failure = "Ore-spike cutoff trimmed the wrong target thickness."; return false; }

            MiningRequest disabled = FilterFixtureRequest(false, origins, columns);
            if (MiningSpikeFilter.BuildOreBottomCutoffs(disabled, targets,
                    MiningSpikeFilterParameters.VanillaCorrection).Count != 0)
            { failure = "Disabled ore-spike filter changed terrain interpretation."; return false; }

            columns[spike] = FilterFixtureColumn(34f);
            if (MiningSpikeFilter.BuildOreBottomCutoffs(
                    FilterFixtureRequest(true, origins, columns), targets,
                    MiningSpikeFilterParameters.VanillaCorrection).Count != 0)
            { failure = "Uniform bedrock was classified as an ore spike."; return false; }

            failure = string.Empty;
            return true;
        }

        private static MiningRequest FilterFixtureRequest(bool enabled,
            Tile2i[] origins, Dictionary<Tile2i, CapturedTerrainColumn> columns)
        {
            var policy = new MiningPolicy(0, 1, 0, null, 0, 0, 0, 0, 0,
                false, 5, enabled, false, false, 0, 0, 1f, 1f);
            return new MiningRequest(origins, new[] { "Ore" }, policy,
                new Tile2i(32, 32), columns, new HashSet<Tile2i>());
        }

        private static CapturedTerrainColumn FilterFixtureColumn(float bedrockTop)
        {
            const float surface = 40f;
            const float oreTop = 39f;
            return new CapturedTerrainColumn(surface, false, new[] {
                new CapturedTerrainLayer("Rock", "Rock", surface, oreTop,
                    1f, 0f, 1f, false),
                new CapturedTerrainLayer("Ore", "Ore", oreTop, bedrockTop,
                    oreTop - bedrockTop, 1f, 1f, false),
                new CapturedTerrainLayer("Bedrock", "Rock", bedrockTop,
                    bedrockTop - 10f, 10f, surface - bedrockTop, 1f, true)
            });
        }

        private sealed class CancelledControl : IAccessSearchExecutionControl
        {
            public bool CancellationRequested => true;
            public bool CaptureOverlay => false;
            public bool CaptureExpansionTrace => false;
            public void Publish(string phase, string subphase, int visited, int pending) { }
            public void RecordNode(Tile2i tile, int height2, bool isGround, int? priority) { }
            public void RecordExpansion(Access.V2.AccessV2ExpansionTrace expansion) { }
            public void RecordGroundExpansionOutcome(Access.V2.AccessV2GroundExpansionOutcomeTrace outcome) { }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using AutoTerrainDesignations.Mining;
using AutoTerrainDesignations.Planning;
using AutoTerrainDesignations.Access;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain.Resources;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        // Kept separate from the extracted planner: the unchanged legacy helpers
        // are the oracle during migration, not another invocation of the new core.
        internal static bool ValidateMiningExtractionFixtures(out string failure)
        {
            var oreProduct = (LooseProductProto)FormatterServices.GetUninitializedObject(typeof(LooseProductProto));
            typeof(Proto).GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(oreProduct, new Proto.ID("ATD_FixtureOre"));
            string oreId = oreProduct.Id.ToString();
            for (int seed = 0; seed < 12; seed++)
            for (int level = 0; level < 5; level++)
            for (int clearance = 0; clearance < 3; clearance++)
            {
                var random = new Random(seed);
                var origins = new List<Tile2i>();
                var columns = new Dictionary<Tile2i, CapturedTerrainColumn>();
                var resources = new Dictionary<Tile2i, List<ProductResource>>();
                var depths = new Dict<Tile2i, int>();
                var targetIds = new HashSet<string> { oreId };
                int? minElevation = seed % 3 == 0 ? (int?)12 : null;
                int maxLayers = seed % 2 == 0 ? 0 : 20;
                var policy = new MiningPolicy(level, 1 + seed % 3, maxLayers,
                    minElevation, clearance, s_minOreHeightByLevel[level],
                    s_minOrePurityByLevel[level], s_minBottomOreDensityByLevel[level],
                    s_minComponentSizeByLevel[level], seed % 2 == 0, 1 + seed % 10,
                    false, false, false, 0, 0, 1f, 1f);
                for (int y = 20; y < 40; y += 4)
                for (int x = 20; x < 40; x += 4)
                {
                    var origin = new Tile2i(x, y);
                    origins.Add(origin);
                    var oreResources = new List<ProductResource>();
                    float surface = float.MaxValue, total = 0, ore = 0;
                    foreach (Tile2i cell in EnumerateDesignatableTileCells(origin))
                    {
                        float height = 40 + random.Next(4);
                        surface = Math.Min(surface, height);
                        int amount = random.Next(0, 8);
                        // A narrow deep interval, mixed surfaces, disjoint deposits,
                        // and empty columns exercise the existing spike behavior.
                        float oreThickness = cell == new Tile2i(24, 24) ? 27.25f : amount * 0.25f;
                        float overburden = random.Next(1, 12) * 0.25f;
                        var layers = new List<CapturedTerrainLayer> {
                            new CapturedTerrainLayer("Rock", "Rock", height, height-overburden,
                                overburden, 0, 1f, false) };
                        float bottom = height - overburden;
                        if (oreThickness > 0)
                        {
                            layers.Add(new CapturedTerrainLayer("Ore", oreId, bottom,
                                bottom-oreThickness, oreThickness, overburden, 1f, false));
                            oreResources.Add(new ProductResource(oreProduct,
                                new ThicknessTilesF(Fix32.FromFloat(oreThickness)),
                                new ThicknessTilesF(Fix32.FromFloat(overburden))));
                            bottom -= oreThickness;
                        }
                        layers.Add(new CapturedTerrainLayer("Bedrock", "Rock", bottom,
                            bottom-10, 10, overburden+oreThickness, 1f, true));
                        columns.Add(cell, new CapturedTerrainColumn(height, false, layers));
                        total += overburden + oreThickness;
                        ore += oreThickness;
                    }
                    if (oreResources.Count == 0) continue;
                    resources.Add(origin, oreResources);
                    if (policy.MinPurity > 0 && ore / total < policy.MinPurity) continue;
                    if (policy.MinOreHeight > 0
                        && GetTargetProductAmount(oreResources, targetIds) < policy.MinOreHeight) continue;
                    bool found = policy.MinBottomDensity > 0
                        ? TryGetPurityAdjustedDepth(oreResources, targetIds, surface,
                            policy.MinBottomDensity, out int depth)
                        : TryGetDeepestResourceDepth(oreResources, targetIds, surface, out depth);
                    if (!found) continue;
                    if (maxLayers > 0) depth = Math.Max(depth, (int)surface-maxLayers);
                    if (minElevation.HasValue) depth = Math.Max(depth, minElevation.Value);
                    depths[origin] = depth;
                }
                FilterIsolatedDesignations(depths, targetIds, resources, level);
                FillRectilinearHull(depths, targetIds, resources, clearance);
                if (policy.FlattenBottom) FlattenDesignationBottom(depths, level, policy.FlatteningStrength);
                var corners = BuildAndSmoothCornerHeights(depths, policy.MaxHeightDiff, level == 0);
                var request = new MiningRequest(origins.ToArray(), new[] { oreId }, policy,
                    new Tile2i(100, 100), columns, new HashSet<Tile2i>());
                MiningPlan actual = MiningPlanner.Execute(request);
                if (!Same(depths, actual.Depths) || !Same(corners, actual.Corners))
                {
                    failure = $"Mining parity seed={seed} quality={level} clearance={clearance}";
                    return false;
                }
                if (seed == 0 && level == 0 && clearance == 0
                    && !AccessSearchReplayRecorder.ValidateMiningContainer(request, actual, out failure)) return false;
                var restored = (MiningRequest)AccessReplayGraphCodec.Deserialize(
                    AccessReplayGraphCodec.Serialize(request), typeof(MiningRequest));
                if (!MiningReplayFacade.Canonical(actual).SequenceEqual(
                    MiningReplayFacade.Canonical(MiningPlanner.Execute(restored))))
                {
                    failure = "Mining captured-input round trip changed geometry.";
                    return false;
                }
            }
            if (!MiningFixtures.ValidateSafetyAndWorker(out failure)) return false;
            failure = string.Empty;
            return true;

            bool Same(Dict<Tile2i, int> expected, Dict<Tile2i, int> actual)
                => expected.Count == actual.Count && expected.All(pair =>
                    actual.TryGetValue(pair.Key, out int value) && value == pair.Value);
        }
    }
}

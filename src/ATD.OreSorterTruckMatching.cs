// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Vehicles;

namespace AutoTerrainDesignations;

public static partial class AutoDepthDesignation
{
    private static class OreSorterTruckMatchingPatches
    {
        private const string BalanceBuffersForProductLocalFunction = "<balanceBuffers>g__balanceBuffersForProduct|";

        internal static void Apply(Harmony harmony)
        {
            Type registry = typeof(VehicleBuffersRegistry);
            Type truckData = AccessTools.Inner(registry, "TruckData")
                ?? throw new MissingMemberException(registry.FullName, "TruckData");
            // Install endpoint comparisons first. They behave identically for every
            // vanilla assignee and are ready before TruckData starts including sorters.
            var methodNames = new[]
            {
                "tryBalanceBuffers",
                "tryGetSurfacePlacementSpec",
                "tryGetSurfaceClearingSpec",
            };
            MethodInfo balanceForProduct = AccessTools.GetDeclaredMethods(registry)
                .Single(method => method.Name.StartsWith(BalanceBuffersForProductLocalFunction, StringComparison.Ordinal));
            var compare = new HarmonyMethod(typeof(OreSorterTruckMatchingPatches), nameof(CompareOreSorterMatchingEntity));
            harmony.Patch(balanceForProduct, transpiler: compare);
            foreach (string name in methodNames)
            {
                MethodInfo method = AccessTools.DeclaredMethod(registry, name)
                    ?? throw new MissingMethodException(registry.FullName, name);
                harmony.Patch(method, transpiler: compare);
            }

            ConstructorInfo constructor = AccessTools.GetDeclaredConstructors(truckData).Single();
            harmony.Patch(constructor, transpiler:
                new HarmonyMethod(typeof(OreSorterTruckMatchingPatches), nameof(IncludeOreSorterInTruckMatching)));
        }

        private static IEnumerable<CodeInstruction> IncludeOreSorterInTruckMatching(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            CodeInstruction staticEntityCheck = code.Single(instruction =>
                instruction.opcode == OpCodes.Isinst && Equals(instruction.operand, typeof(IStaticEntity)));
            staticEntityCheck.opcode = OpCodes.Call;
            staticEntityCheck.operand = AccessTools.Method(typeof(OreSorterTruckMatchingPatches), nameof(GetTruckMatchingStaticEntity));
            return code;
        }

        private static IStaticEntity? GetTruckMatchingStaticEntity(IEntityAssignedWithVehicles assignee)
            => assignee is OreSortingPlantVehiclesEnforcerAdapter adapter ? adapter.Sorter : assignee as IStaticEntity;

        private static IEnumerable<CodeInstruction> CompareOreSorterMatchingEntity(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var code = instructions.ToList();
            Type? truckData = AccessTools.Inner(typeof(VehicleBuffersRegistry), "TruckData");
            FieldInfo assignedBuilding = AccessTools.DeclaredField(truckData, "AssignedBuilding");
            FieldInfo value = AccessTools.DeclaredField(typeof(Option<IEntityAssignedWithVehicles>), "Value");
            MethodInfo unwrap = AccessTools.Method(typeof(OreSorterTruckMatchingPatches), nameof(GetTruckMatchingAssignee));
            int count = 0;
            for (int i = 1; i < code.Count; i++)
            {
                if (!code[i - 1].LoadsField(assignedBuilding) || !code[i].LoadsField(value))
                    continue;

                code[i].opcode = OpCodes.Call;
                code[i].operand = unwrap;
                count++;
            }

            int expected = original.Name switch
            {
                "tryBalanceBuffers" => 4,
                "tryGetSurfacePlacementSpec" => 1,
                "tryGetSurfaceClearingSpec" => 2,
                var name when name.StartsWith(BalanceBuffersForProductLocalFunction, StringComparison.Ordinal) => 2,
                _ => throw new InvalidOperationException($"Unexpected truck matcher {original.Name}."),
            };
            if (count != expected)
                throw new InvalidOperationException($"Expected {expected} assigned building reads in {original.Name}, found {count}.");
            return code;
        }

        // The registry compares endpoint objects by reference.
        private static IEntity GetTruckMatchingAssignee(Option<IEntityAssignedWithVehicles> building)
            => building.Value is OreSortingPlantVehiclesEnforcerAdapter adapter ? adapter.Sorter : building.Value;
    }
}

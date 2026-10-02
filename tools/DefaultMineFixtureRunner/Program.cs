using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AutoTerrainDesignations;
using HarmonyLib;

internal static class Program
{
    private static int Main(string[] args)
    {
        string managed = args.Length == 1 ? args[0]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "common", "Captain of Industry", "Captain of Industry_Data", "Managed");
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
        {
            string path = Path.Combine(managed, new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            ZoneIdentityFixtures();
            NativeIntegrationFixtures();
            Console.WriteLine("Default Mine fixtures passed: zone identities, native IL guards, and Harmony hook installation.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void ZoneIdentityFixtures()
    {
        KeyValuePair<int, ulong> Zone(int id, ulong mask) => new KeyValuePair<int, ulong>(id, mask);
        Equal(6UL, DefaultMineZoneSnapshot.Resolve(new[] { 10, 11 }, new[] { Zone(10, 2), Zone(11, 4) }, 1), "overlapping zones");
        Equal(2UL, DefaultMineZoneSnapshot.Resolve(new[] { 10, 11 }, new[] { Zone(10, 2), Zone(99, 4) }, 1), "deleted zone bit reused");
        Equal(1UL, DefaultMineZoneSnapshot.Resolve(new[] { 10 }, new[] { Zone(99, 2) }, 1), "all captured zones deleted");
        Equal(1UL, DefaultMineZoneSnapshot.Resolve(Array.Empty<int>(), new[] { Zone(10, 2) }, 1), "Default Zone stays captured across movement");
        Equal(8UL, DefaultMineZoneSnapshot.Resolve(new[] { 10 }, new[] { Zone(10, 8) }, 1), "identity survives mask reassignment");
    }

    private static void NativeIntegrationFixtures()
    {
        Assembly mod = Assembly.Load("AutoTerrainDesignations");
        Assembly core = Assembly.Load("Mafi.Core");
        Type hooks = mod.GetType("AutoTerrainDesignations.AutoDepthDesignation+DefaultMinePatches", true)!;
        Type Native(string name) => core.GetType(name, true)!;
        Type mining = Native("Mafi.Core.Vehicles.Jobs.MiningJob");
        Type excavator = Native("Mafi.Core.Vehicles.Excavators.Excavator");
        Type vehicle = Native("Mafi.Core.Entities.Dynamic.Vehicle");
        Type truck = Native("Mafi.Core.Vehicles.Trucks.Truck");
        Type provider = Native("Mafi.Core.Vehicles.Trucks.JobProviders.DefaultTruckJobProvider");
        Type factory = mining.GetNestedType("Factory")!;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
        MethodInfo assertion = hooks.GetMethod("AssignmentAssertionTranspiler", flags)!;
        MethodInfo cleanup = hooks.GetMethod("CleanupCargoTranspiler", flags)!;
        ConstructorInfo constructor = mining.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(ctor => ctor.GetParameters().Length == 7);
        Rewrite(assertion, constructor, "AssertMiningAssignment");
        Rewrite(assertion, AccessTools.Method(mining, "DoJobInternal"), "AssertMiningAssignment");
        Rewrite(cleanup, AccessTools.Method(mining, "cleanup"), "ClearMiningCargo");
        MustReject(assertion, new List<CodeInstruction> { new CodeInstruction(OpCodes.Ret) }, "missing native assertion");
        MustReject(cleanup, new List<CodeInstruction> { new CodeInstruction(OpCodes.Ret) }, "missing native cleanup");
        var original = PatchProcessor.GetOriginalInstructions(constructor).ToList();
        MustReject(assertion, original.Concat(PatchProcessor.GetOriginalInstructions(constructor)).ToList(), "duplicate native assertion");

        // Exercise production's exact Patch routine against installed assemblies.
        // No game world or mod feature state is instantiated by this check.
        var harmony = new Harmony("atd.default-mine.fixtures");
        MethodInfo patch = hooks.GetMethod("Patch", flags)!;
        void Install(Type type, string method, string? prefix = null, string? postfix = null, string? transpiler = null)
            => patch.Invoke(null, new object?[] { harmony, type, method, prefix, postfix, transpiler });
        try
        {
            Install(mining, ".ctor", transpiler: "AssignmentAssertionTranspiler");
            Install(mining, "DoJobInternal", "MiningStepPrefix", transpiler: "AssignmentAssertionTranspiler");
            Install(mining, "cleanup", transpiler: "CleanupCargoTranspiler");
            Install(mining, "isControlledByAssignedTower", "OwnershipPrefix");
            Install(factory, "TryCreateAndEnqueueJob", "AcquirePrefix");
            Install(mining, "handleMining", "ScoopPrefix");
            Install(excavator, "handleWaitingForTruck", "WaitingForTruckPrefix");
            Install(excavator, "ClearCargoImmediately", "ClearBucketPrefix");
            Install(mining, "RequestCancelReturnDeadline", "MiningCancelPrefix");
            Install(vehicle, "OnAssignTo", "VehicleInterruptPrefix");
            Install(vehicle, "GoTo", "VehicleInterruptPrefix");
            Install(vehicle, "OnEnabledChanged", "VehicleEnabledPrefix");
            Install(excavator, "SkipNoMovementMonitoring", postfix: "WaitingMonitoringPostfix");
            Install(vehicle, "get_ZoneMask", postfix: "ZoneMaskPostfix");
            Install(truck, "IsAvailableToBalanceCargo", postfix: "AvailabilityPostfix");
            Install(provider, "TryGetJobFor", "TruckJobPrefix");
            Install(Native("Mafi.Core.Vehicles.Jobs.VehicleJob"), "RequestCancel", "JobCancelPrefix");
            Install(Native("Mafi.Core.Entities.Dynamic.PathFindingEntity"), "CancelAllJobsAndResetState", "CancelAllPrefix");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static void Rewrite(MethodInfo rewrite, MethodBase target, string expectedHelper)
    {
        var original = PatchProcessor.GetOriginalInstructions(target).ToList();
        var rewritten = ((IEnumerable<CodeInstruction>)rewrite.Invoke(null, new object[] { original })!).ToList();
        Equal(1, rewritten.Count(code => code.operand is MethodInfo method && method.Name == expectedHelper), target.Name);
    }

    private static void MustReject(MethodInfo rewrite, List<CodeInstruction> code, string name)
    {
        try { rewrite.Invoke(null, new object[] { code }); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { return; }
        throw new Exception("Guard failed: " + name);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual)) throw new Exception($"{name}: expected {expected}, got {actual}.");
    }
}

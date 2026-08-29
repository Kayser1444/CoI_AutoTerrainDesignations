using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace AutoTerrainDesignations.Tools.AccessV2FixtureRunner
{
    internal static class Program
    {
        private static string s_modDirectory = string.Empty;
        private static string s_managedDirectory = string.Empty;

        private static int Main(string[] args)
        {
            bool replay = args.Length == 4
                && string.Equals(args[0], "replay", StringComparison.OrdinalIgnoreCase);
            bool benchmarkCodec = args.Length == 4
                && string.Equals(args[0], "codec-benchmark", StringComparison.OrdinalIgnoreCase);
            bool candidateReplay = args.Length == 4
                && string.Equals(args[0], "candidate-replay", StringComparison.OrdinalIgnoreCase);
            bool compatibleReplay = args.Length == 4
                && string.Equals(args[0], "compatible-replay", StringComparison.OrdinalIgnoreCase);
            bool traceCandidate = args.Length == 5
                && string.Equals(args[0], "trace-candidate", StringComparison.OrdinalIgnoreCase);
            bool benchmark = args.Length == 5
                && string.Equals(args[0], "benchmark", StringComparison.OrdinalIgnoreCase);
            bool caseMode = replay || benchmarkCodec || candidateReplay
                || compatibleReplay || traceCandidate || benchmark;
            if (!caseMode && args.Length != 2)
            {
                Console.Error.WriteLine(
                    "Usage:\n"
                    + "  AccessV2FixtureRunner <ATD assembly> <CoI Managed directory>\n"
                    + "  AccessV2FixtureRunner replay <ATD assembly> <CoI Managed directory> <case directory>\n"
                    + "  AccessV2FixtureRunner candidate-replay <ATD assembly> <CoI Managed directory> <case directory>\n"
                    + "  AccessV2FixtureRunner compatible-replay <ATD assembly> <CoI Managed directory> <case directory>\n"
                    + "  AccessV2FixtureRunner trace-candidate <ATD assembly> <CoI Managed directory> <case directory> <trace.csv>\n"
                    + "  AccessV2FixtureRunner benchmark <ATD assembly> <CoI Managed directory> <case directory> <repetitions>\n"
                    + "  AccessV2FixtureRunner codec-benchmark <ATD assembly> <CoI Managed directory> <case directory>");
                return 2;
            }

            string assemblyPath = Path.GetFullPath(args[caseMode ? 1 : 0]);
            s_modDirectory = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
            s_managedDirectory = Path.GetFullPath(args[caseMode ? 2 : 1]);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try
            {
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                if (replay)
                    return ReplayCase(assembly, Path.GetFullPath(args[3]));
                if (candidateReplay || compatibleReplay)
                    return ReplayCandidate(
                        assembly,
                        Path.GetFullPath(args[3]),
                        allowGameAssemblyMismatch: compatibleReplay);
                if (traceCandidate)
                    return TraceCandidate(
                        assembly,
                        Path.GetFullPath(args[3]),
                        Path.GetFullPath(args[4]));
                if (benchmarkCodec)
                    return BenchmarkCodec(
                        assembly, Path.GetFullPath(args[3]));
                if (benchmark)
                {
                    if (!int.TryParse(args[4], out int repetitions))
                    {
                        Console.Error.WriteLine(
                            "Benchmark repetitions must be an integer.");
                        return 2;
                    }
                    return BenchmarkCase(
                        assembly, Path.GetFullPath(args[3]), repetitions);
                }
                Type replayFixtures = assembly.GetType(
                    "AutoTerrainDesignations.Access.AccessSearchReplayFixtures", true);
                MethodInfo validateReplay = replayFixtures.GetMethod(
                    "ValidateAll",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        replayFixtures.FullName, "ValidateAll");
                object[] replayFixtureArgs = { string.Empty };
                bool replayFixtureSuccess = (bool)(
                    validateReplay.Invoke(null, replayFixtureArgs) ?? false);
                string replayFixtureFailure =
                    replayFixtureArgs[0] as string ?? string.Empty;
                Console.WriteLine(
                    "Access replay codec fixtures: "
                    + $"success={replayFixtureSuccess} "
                    + $"failure={replayFixtureFailure}");
                if (!replayFixtureSuccess) return 1;
                Type propRemovalPolicy = assembly.GetType(
                    "AutoTerrainDesignations.ATDPropRemovalLifecyclePolicy", true);
                MethodInfo validatePropRemoval = propRemovalPolicy.GetMethod(
                    "ValidateFixtures",
                    BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        propRemovalPolicy.FullName, "ValidateFixtures");
                object[] propRemovalArgs = { string.Empty };
                bool propRemovalSuccess = (bool)(
                    validatePropRemoval.Invoke(null, propRemovalArgs) ?? false);
                string propRemovalFailure =
                    propRemovalArgs[0] as string ?? string.Empty;
                Console.WriteLine(
                    "Prop-removal lifecycle fixtures: "
                    + $"success={propRemovalSuccess} "
                    + $"failure={propRemovalFailure}");
                if (!propRemovalSuccess) return 1;
                Type fixtures = assembly.GetType(
                    "AutoTerrainDesignations.Access.V2.AccessV2Fixtures", true);
                MethodInfo validate = fixtures.GetMethod(
                    "ValidateAll",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(fixtures.FullName, "ValidateAll");
                object[] invokeArgs = { string.Empty };
                bool success = (bool)(validate.Invoke(null, invokeArgs) ?? false);
                string failure = invokeArgs[0] as string ?? string.Empty;
                Console.WriteLine($"V2 geometry fixtures: success={success} failure={failure}");
                if (!success) return 1;

                Type coreSearch = assembly.GetType(
                    "AutoTerrainDesignations.Access.AccessPathSearch", true);
                MethodInfo validateCore = coreSearch.GetMethod(
                    "ValidateCoreTransitions",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        coreSearch.FullName, "ValidateCoreTransitions");
                object[] coreInvokeArgs = { string.Empty };
                bool coreSuccess = (bool)(
                    validateCore.Invoke(null, coreInvokeArgs) ?? false);
                string coreFailure = coreInvokeArgs[0] as string ?? string.Empty;
                Console.WriteLine(
                    $"V1 core fixtures: success={coreSuccess} failure={coreFailure}");
                if (!coreSuccess) return 1;

                Type state = assembly.GetType(
                    "AutoTerrainDesignations.AutoDepthDesignation", true);
                MethodInfo validateRemoval = state.GetMethod(
                    "ValidateGeneratedDesignationRemovalFixtures",
                    BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        state.FullName,
                        "ValidateGeneratedDesignationRemovalFixtures");
                object[] removalArgs = { string.Empty };
                bool removalSuccess = (bool)(
                    validateRemoval.Invoke(null, removalArgs) ?? false);
                string removalFailure =
                    removalArgs[0] as string ?? string.Empty;
                Console.WriteLine(
                    "Generated designation removal fixtures: "
                    + $"success={removalSuccess} "
                    + $"failure={removalFailure}");
                if (!removalSuccess) return 1;

                MethodInfo validateSettingsMigration = state.GetMethod(
                    "ValidateTurningRampsMigrationFixtures",
                    BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        state.FullName,
                        "ValidateTurningRampsMigrationFixtures");
                object[] settingsMigrationArgs = { string.Empty };
                bool settingsMigrationSuccess = (bool)(
                    validateSettingsMigration.Invoke(
                        null, settingsMigrationArgs) ?? false);
                string settingsMigrationFailure =
                    settingsMigrationArgs[0] as string ?? string.Empty;
                Console.WriteLine(
                    "Settings migration fixtures: "
                    + $"success={settingsMigrationSuccess} "
                    + $"failure={settingsMigrationFailure}");
                if (!settingsMigrationSuccess) return 1;

                Type retryFixtures = assembly.GetType(
                    "AutoTerrainDesignations.Access.AccessFailureRetryPolicyFixtures",
                    true);
                MethodInfo validateRetry = retryFixtures.GetMethod(
                    "ValidateAll",
                    BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        retryFixtures.FullName,
                        "ValidateAll");
                object[] retryArgs = { string.Empty };
                bool retrySuccess = (bool)(
                    validateRetry.Invoke(null, retryArgs) ?? false);
                string retryFailure = retryArgs[0] as string ?? string.Empty;
                Console.WriteLine(
                    "Access failure retry fixtures: "
                    + $"success={retrySuccess} "
                    + $"failure={retryFailure}");
                if (!retrySuccess) return 1;

                Type dryRunFixtures = assembly.GetType(
                    "AutoTerrainDesignations.Access.ExperimentalAccessDryRunResultFixtures",
                    true);
                MethodInfo validateDryRun = dryRunFixtures.GetMethod(
                    "ValidateAll",
                    BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        dryRunFixtures.FullName,
                        "ValidateAll");
                object[] dryRunArgs = { string.Empty };
                bool dryRunSuccess = (bool)(
                    validateDryRun.Invoke(null, dryRunArgs) ?? false);
                string dryRunFailure =
                    dryRunArgs[0] as string ?? string.Empty;
                Console.WriteLine(
                    "Request-scoped dry-run result fixtures: "
                    + $"success={dryRunSuccess} "
                    + $"failure={dryRunFailure}");
                if (!dryRunSuccess) return 1;

                MethodInfo validateFarmingOwnership = state.GetMethod(
                    "ValidateFarmingAccesswayOwnershipFixtures",
                    BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        state.FullName,
                        "ValidateFarmingAccesswayOwnershipFixtures");
                object[] farmingOwnershipArgs = { string.Empty };
                bool farmingOwnershipSuccess = (bool)(
                    validateFarmingOwnership.Invoke(
                        null, farmingOwnershipArgs) ?? false);
                string farmingOwnershipFailure =
                    farmingOwnershipArgs[0] as string ?? string.Empty;
                Console.WriteLine(
                    "Farming accessway ownership fixtures: "
                    + $"success={farmingOwnershipSuccess} "
                    + $"failure={farmingOwnershipFailure}");
                if (!farmingOwnershipSuccess) return 1;

                Type managerFixtures = assembly.GetType(
                    "AutoTerrainDesignations.Access.ATDAccesswayManagerFixtures",
                    true);
                MethodInfo validateManager = managerFixtures.GetMethod(
                    "ValidateAll",
                    BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        managerFixtures.FullName,
                        "ValidateAll");
                object[] managerArgs = { string.Empty };
                bool managerSuccess = (bool)(
                    validateManager.Invoke(null, managerArgs) ?? false);
                string managerFailure = managerArgs[0] as string ?? string.Empty;
                Console.WriteLine(
                    "Accessway manager fixtures: "
                    + $"success={managerSuccess} "
                    + $"failure={managerFailure}");
                if (!managerSuccess) return 1;

                Type workerFixtures = assembly.GetType(
                    "AutoTerrainDesignations.Access.Worker.AccessSearchWorkerFixtures",
                    true);
                MethodInfo validateWorker = workerFixtures.GetMethod(
                    "ValidateAll",
                    BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        workerFixtures.FullName, "ValidateAll");
                object[] workerArgs = { string.Empty };
                bool workerSuccess = (bool)(
                    validateWorker.Invoke(null, workerArgs) ?? false);
                string workerFailure =
                    workerArgs[0] as string ?? string.Empty;
                Console.WriteLine(
                    "Access search worker fixtures: "
                    + $"success={workerSuccess} failure={workerFailure}");
                if (!workerSuccess) return 1;

                Type acceptanceFixtures = assembly.GetType(
                    "AutoTerrainDesignations.Access.AccessPlacementAcceptancePolicyFixtures",
                    true);
                MethodInfo validateAcceptance = acceptanceFixtures.GetMethod(
                    "ValidateAll",
                    BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        acceptanceFixtures.FullName, "ValidateAll");
                object[] acceptanceArgs = { string.Empty };
                bool acceptanceSuccess = (bool)(
                    validateAcceptance.Invoke(null, acceptanceArgs) ?? false);
                string acceptanceFailure =
                    acceptanceArgs[0] as string ?? string.Empty;
                Console.WriteLine(
                    "Access placement acceptance fixtures: "
                    + $"success={acceptanceSuccess} "
                    + $"failure={acceptanceFailure}");
                return acceptanceSuccess ? 0 : 1;
            }
            catch (TargetInvocationException ex)
            {
                Console.Error.WriteLine(ex.InnerException ?? ex);
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
            }
        }

        private static int ReplayCase(Assembly assembly, string caseDirectory)
        {
            string assemblyPath = assembly.Location;
            string assemblyHash;
            using (SHA256 sha = SHA256.Create())
                assemblyHash = string.Concat(sha.ComputeHash(
                    File.ReadAllBytes(assemblyPath)).Select(
                        value => value.ToString("x2")));
            string buildTimestamp = assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(item => item.Key == "BuildTimestamp")?.Value
                ?? string.Empty;
            Console.WriteLine(
                $"ATD binary: path={assemblyPath} sha256={assemblyHash} "
                + $"buildTimestamp={buildTimestamp}");
            Type facade = assembly.GetType(
                "AutoTerrainDesignations.Access.AccessSearchReplayFacade", true);
            MethodInfo replay = facade.GetMethod(
                "TryReplayCase",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(facade.FullName, "TryReplayCase");
            object[] replayArgs = { caseDirectory, string.Empty };
            bool success = (bool)(replay.Invoke(null, replayArgs) ?? false);
            string report = replayArgs[1] as string ?? string.Empty;
            Console.WriteLine("Access search replay: " + report);
            return success ? 0 : 1;
        }

        private static int BenchmarkCodec(
            Assembly assembly, string caseDirectory)
        {
            Type facade = assembly.GetType(
                "AutoTerrainDesignations.Access.AccessSearchReplayFacade", true);
            MethodInfo benchmark = facade.GetMethod(
                "TryBenchmarkCaseCodec",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(
                    facade.FullName, "TryBenchmarkCaseCodec");
            object[] invokeArgs = { caseDirectory, string.Empty };
            bool success = (bool)(benchmark.Invoke(null, invokeArgs) ?? false);
            Console.WriteLine("Access replay codec benchmark: "
                + (invokeArgs[1] as string ?? string.Empty));
            return success ? 0 : 1;
        }

        private static int TraceCandidate(
            Assembly assembly,
            string caseDirectory,
            string tracePath)
        {
            Type facade = assembly.GetType(
                "AutoTerrainDesignations.Access.AccessSearchReplayFacade", true);
            MethodInfo trace = facade.GetMethod(
                "TryTraceCandidate",
                BindingFlags.Static | BindingFlags.Public
                    | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(
                    facade.FullName, "TryTraceCandidate");
            object[] traceArgs =
            {
                caseDirectory,
                false,
                tracePath,
                string.Empty,
            };
            bool success = (bool)(trace.Invoke(null, traceArgs) ?? false);
            Console.WriteLine(
                "Access search expansion trace: "
                + (traceArgs[3] as string ?? string.Empty));
            return success ? 0 : 1;
        }

        private static int ReplayCandidate(
            Assembly assembly,
            string caseDirectory,
            bool allowGameAssemblyMismatch)
        {
            PrintAssemblyIdentity(assembly);
            Type facade = assembly.GetType(
                "AutoTerrainDesignations.Access.AccessSearchReplayFacade", true);
            MethodInfo replay = facade.GetMethod(
                "TryReplayCandidate",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(
                    facade.FullName, "TryReplayCandidate");
            object[] invokeArgs =
            {
                caseDirectory,
                allowGameAssemblyMismatch,
                string.Empty,
            };
            bool success = (bool)(replay.Invoke(null, invokeArgs) ?? false);
            Console.WriteLine("Access search candidate replay: "
                + (invokeArgs[2] as string ?? string.Empty));
            return success ? 0 : 1;
        }

        private static int BenchmarkCase(
            Assembly assembly,
            string caseDirectory,
            int repetitions)
        {
            PrintAssemblyIdentity(assembly);
            Type facade = assembly.GetType(
                "AutoTerrainDesignations.Access.AccessSearchReplayFacade", true);
            MethodInfo benchmark = facade.GetMethod(
                "TryBenchmarkCase",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(
                    facade.FullName, "TryBenchmarkCase");
            object[] invokeArgs =
            {
                caseDirectory,
                repetitions,
                false,
                string.Empty,
            };
            bool success = (bool)(benchmark.Invoke(null, invokeArgs) ?? false);
            Console.WriteLine("Access search benchmark: "
                + (invokeArgs[3] as string ?? string.Empty));
            return success ? 0 : 1;
        }

        private static void PrintAssemblyIdentity(Assembly assembly)
        {
            string assemblyPath = assembly.Location;
            string assemblyHash;
            using (SHA256 sha = SHA256.Create())
                assemblyHash = string.Concat(sha.ComputeHash(
                    File.ReadAllBytes(assemblyPath)).Select(
                        value => value.ToString("x2")));
            string buildTimestamp = assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(item => item.Key == "BuildTimestamp")?.Value
                ?? string.Empty;
            Console.WriteLine(
                $"ATD binary: path={assemblyPath} sha256={assemblyHash} "
                + $"buildTimestamp={buildTimestamp}");
        }

        private static Assembly? ResolveAssembly(object sender, ResolveEventArgs args)
        {
            string fileName = new AssemblyName(args.Name).Name + ".dll";
            string modCandidate = Path.Combine(s_modDirectory, fileName);
            if (File.Exists(modCandidate)) return Assembly.LoadFrom(modCandidate);
            string managedCandidate = Path.Combine(s_managedDirectory, fileName);
            return File.Exists(managedCandidate)
                ? Assembly.LoadFrom(managedCandidate)
                : null;
        }
    }
}

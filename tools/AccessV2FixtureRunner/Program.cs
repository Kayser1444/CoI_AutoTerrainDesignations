using System;
using System.IO;
using System.Reflection;

namespace AutoTerrainDesignations.Tools.AccessV2FixtureRunner
{
    internal static class Program
    {
        private static string s_modDirectory = string.Empty;
        private static string s_managedDirectory = string.Empty;

        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine(
                    "Usage: AccessV2FixtureRunner <ATD assembly> <CoI Managed directory>");
                return 2;
            }

            string assemblyPath = Path.GetFullPath(args[0]);
            s_modDirectory = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
            s_managedDirectory = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try
            {
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
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
                    "ValidateTurningRampsExperimentalMigrationFixtures",
                    BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        state.FullName,
                        "ValidateTurningRampsExperimentalMigrationFixtures");
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
                return managerSuccess ? 0 : 1;
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

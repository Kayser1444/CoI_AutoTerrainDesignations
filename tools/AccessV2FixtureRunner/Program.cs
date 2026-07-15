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
                return coreSuccess ? 0 : 1;
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

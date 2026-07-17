// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
// Auto Terrain Designations - Runtime diagnostic level policy
using System;
using System.Diagnostics;
using CoI.AutoHelpers.Logging;

namespace AutoTerrainDesignations
{
    internal enum AtdDiagnosticLevel
    {
        Warning = 0,
        Info = 1,
        Debug = 2,
        Trace = 3,
    }

    internal static class AtdDiagnostics
    {
#if DEBUG
        internal const AtdDiagnosticLevel BuildDefaultLevel = AtdDiagnosticLevel.Debug;
#else
        internal const AtdDiagnosticLevel BuildDefaultLevel = AtdDiagnosticLevel.Info;
#endif

        private static AtdDiagnosticLevel s_level = BuildDefaultLevel;
        private static string s_configuredLevel = "Default";

        internal static AtdDiagnosticLevel Level => s_level;
        internal static string ConfiguredLevel => s_configuredLevel;
        internal static bool IsEnabled(AtdDiagnosticLevel level) => s_level >= level;
        internal static bool CollectTimings => IsEnabled(AtdDiagnosticLevel.Debug);

        internal static void ResetToBuildDefault()
        {
            s_configuredLevel = "Default";
            s_level = BuildDefaultLevel;
        }

        internal static bool TryApplyConfiguredLevel(string? value, out string error)
        {
            if (string.Equals(value?.Trim(), "default", StringComparison.OrdinalIgnoreCase))
            {
                ResetToBuildDefault();
                error = string.Empty;
                return true;
            }

            if (!TryParseLevel(value, out AtdDiagnosticLevel parsed))
            {
                error = "Use Default, Warning, Info, Debug, or Trace.";
                return false;
            }

            s_configuredLevel = parsed.ToString();
            s_level = parsed;
            error = string.Empty;
            return true;
        }

        internal static bool TrySetSessionLevel(string? value, out string error)
        {
            if (string.Equals(value?.Trim(), "default", StringComparison.OrdinalIgnoreCase))
            {
                s_level = GetConfiguredLevel();
                error = string.Empty;
                return true;
            }

            if (!TryParseLevel(value, out AtdDiagnosticLevel parsed))
            {
                error = "Use Default, Warning, Info, Debug, or Trace.";
                return false;
            }

            s_level = parsed;
            error = string.Empty;
            return true;
        }

        internal static string Describe()
            => $"active={s_level}, configured={s_configuredLevel}, buildDefault={BuildDefaultLevel}";

        internal static long Timestamp()
            => CollectTimings ? Stopwatch.GetTimestamp() : 0L;

        internal static long ElapsedSince(long start)
            => start == 0L ? 0L : Stopwatch.GetTimestamp() - start;

        internal static void Info(ModLogger logger, string message)
        {
            if (IsEnabled(AtdDiagnosticLevel.Info))
                logger.Info(message);
        }

        internal static void Debug(ModLogger logger, string message)
        {
            if (IsEnabled(AtdDiagnosticLevel.Debug))
                logger.Info(message);
        }

        private static AtdDiagnosticLevel GetConfiguredLevel()
            => string.Equals(s_configuredLevel, "Default", StringComparison.OrdinalIgnoreCase)
                ? BuildDefaultLevel
                : Enum.TryParse(s_configuredLevel, true, out AtdDiagnosticLevel parsed)
                    ? parsed
                    : BuildDefaultLevel;

        private static bool TryParseLevel(string? value, out AtdDiagnosticLevel level)
        {
            if (Enum.TryParse(value?.Trim(), true, out level)
                && Enum.IsDefined(typeof(AtdDiagnosticLevel), level))
                return true;

            level = BuildDefaultLevel;
            return false;
        }
    }
}

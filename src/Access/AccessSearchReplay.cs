using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AutoTerrainDesignations.Access.V2;
using AutoTerrainDesignations.Access.Worker;
using Mafi;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations.Access
{
    /// <summary>
    /// Observational timings attached to a replay case. They are deliberately
    /// excluded from the canonical result comparison.
    /// </summary>
    internal readonly struct AccessReplayPhaseTiming
    {
        public double PreparationMilliseconds { get; }
        public double SearchMilliseconds { get; }
        public double MaterializationMilliseconds { get; }

        public AccessReplayPhaseTiming(
            double preparationMilliseconds,
            double searchMilliseconds,
            double materializationMilliseconds)
        {
            PreparationMilliseconds = Math.Max(0d, preparationMilliseconds);
            SearchMilliseconds = Math.Max(0d, searchMilliseconds);
            MaterializationMilliseconds = Math.Max(0d, materializationMilliseconds);
        }
    }

    /// <summary>
    /// Optional memory evidence collected only for an explicitly armed
    /// laboratory capture. It is manifest metadata, never part of the replay
    /// request graph or canonical outcome.
    /// </summary>
    internal sealed class AccessReplayMemoryEvidence
    {
        internal const string MeasurementKind = "capture-boundary-v1";

        public string EstimatorVersion { get; }
        public long EstimatedRetainedBytes { get; }
        public long MemoryCeilingBytes { get; }
        public long ManagedHeapBeforeBytes { get; }
        public long ManagedHeapAfterBytes { get; }
        public long ManagedHeapDeltaBytes { get; }
        public long WorkingSetBeforeBytes { get; }
        public long WorkingSetAfterBytes { get; }
        public long WorkingSetDeltaBytes { get; }
        public long PrivateMemoryBeforeBytes { get; }
        public long PrivateMemoryAfterBytes { get; }
        public long PrivateMemoryDeltaBytes { get; }
        public int Gen0Collections { get; }
        public int Gen1Collections { get; }
        public int Gen2Collections { get; }
        public double ElapsedMilliseconds { get; }

        internal AccessReplayMemoryEvidence(
            string estimatorVersion,
            long estimatedRetainedBytes,
            long memoryCeilingBytes,
            long managedHeapBeforeBytes,
            long managedHeapAfterBytes,
            long workingSetBeforeBytes,
            long workingSetAfterBytes,
            long privateMemoryBeforeBytes,
            long privateMemoryAfterBytes,
            int gen0Collections,
            int gen1Collections,
            int gen2Collections,
            double elapsedMilliseconds)
        {
            EstimatorVersion = estimatorVersion ?? string.Empty;
            EstimatedRetainedBytes = Math.Max(0L, estimatedRetainedBytes);
            MemoryCeilingBytes = Math.Max(0L, memoryCeilingBytes);
            ManagedHeapBeforeBytes = managedHeapBeforeBytes;
            ManagedHeapAfterBytes = managedHeapAfterBytes;
            ManagedHeapDeltaBytes = Delta(
                managedHeapBeforeBytes, managedHeapAfterBytes);
            WorkingSetBeforeBytes = workingSetBeforeBytes;
            WorkingSetAfterBytes = workingSetAfterBytes;
            WorkingSetDeltaBytes = Delta(
                workingSetBeforeBytes, workingSetAfterBytes);
            PrivateMemoryBeforeBytes = privateMemoryBeforeBytes;
            PrivateMemoryAfterBytes = privateMemoryAfterBytes;
            PrivateMemoryDeltaBytes = Delta(
                privateMemoryBeforeBytes, privateMemoryAfterBytes);
            Gen0Collections = Math.Max(0, gen0Collections);
            Gen1Collections = Math.Max(0, gen1Collections);
            Gen2Collections = Math.Max(0, gen2Collections);
            ElapsedMilliseconds = Math.Max(0d, elapsedMilliseconds);
        }

        private static long Delta(long before, long after)
            => before < 0L || after < 0L ? -1L : after - before;
    }

    /// <summary>
    /// Process-local capture probe. It is created only while a replay capture
    /// is armed, so ordinary searches do not pay for process counters.
    /// Measurements are deliberately observational and must not control the
    /// live snapshot guard.
    /// </summary>
    internal sealed class AccessReplayMemoryProbe
    {
        private readonly Stopwatch m_timer;
        private readonly long m_managedHeapBeforeBytes;
        private readonly long m_workingSetBeforeBytes;
        private readonly long m_privateMemoryBeforeBytes;
        private readonly int m_gen0Before;
        private readonly int m_gen1Before;
        private readonly int m_gen2Before;

        private AccessReplayMemoryProbe()
        {
            m_timer = Stopwatch.StartNew();
            m_managedHeapBeforeBytes = ReadManagedHeap();
            ProcessMemorySample before = ReadProcessMemory();
            m_workingSetBeforeBytes = before.WorkingSetBytes;
            m_privateMemoryBeforeBytes = before.PrivateBytes;
            m_gen0Before = ReadCollectionCount(0);
            m_gen1Before = ReadCollectionCount(1);
            m_gen2Before = ReadCollectionCount(2);
        }

        internal static AccessReplayMemoryProbe Start()
            => new AccessReplayMemoryProbe();

        internal AccessReplayMemoryEvidence Complete(
            long estimatedRetainedBytes,
            long memoryCeilingBytes)
        {
            m_timer.Stop();
            long managedHeapAfterBytes = ReadManagedHeap();
            ProcessMemorySample after = ReadProcessMemory();
            return new AccessReplayMemoryEvidence(
                AccessSnapshotMemoryEstimator.Version,
                estimatedRetainedBytes,
                memoryCeilingBytes,
                m_managedHeapBeforeBytes,
                managedHeapAfterBytes,
                m_workingSetBeforeBytes,
                after.WorkingSetBytes,
                m_privateMemoryBeforeBytes,
                after.PrivateBytes,
                ReadCollectionCount(0) - m_gen0Before,
                ReadCollectionCount(1) - m_gen1Before,
                ReadCollectionCount(2) - m_gen2Before,
                m_timer.Elapsed.TotalMilliseconds);
        }

        private static long ReadManagedHeap()
        {
            try
            {
                return GC.GetTotalMemory(forceFullCollection: false);
            }
            catch
            {
                return -1L;
            }
        }

        private static ProcessMemorySample ReadProcessMemory()
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    process.Refresh();
                    return new ProcessMemorySample(
                        process.WorkingSet64,
                        process.PrivateMemorySize64);
                }
            }
            catch
            {
                return new ProcessMemorySample(-1L, -1L);
            }
        }

        private static int ReadCollectionCount(int generation)
        {
            try
            {
                return GC.CollectionCount(generation);
            }
            catch
            {
                return 0;
            }
        }

        private readonly struct ProcessMemorySample
        {
            internal long WorkingSetBytes { get; }
            internal long PrivateBytes { get; }

            internal ProcessMemorySample(
                long workingSetBytes, long privateBytes)
            {
                WorkingSetBytes = workingSetBytes;
                PrivateBytes = privateBytes;
            }
        }
    }

    /// <summary>
    /// Dormant developer capture switch. The hot path performs only the null
    /// check unless a developer explicitly arms the next accepted search.
    /// </summary>
    internal static partial class AccessSearchReplayRecorder
    {
        private const int SchemaVersion = 1;
        private const string CaseExtension = ".atd-access-case";
        private static readonly object s_gate = new object();
        private static ArmState? s_arm;
        private static string s_currentMapName = string.Empty;

        private sealed class ArmState
        {
            public string Name = string.Empty;
            public string ScenarioFamily = string.Empty;
            public DateTime ArmedUtc;
            public string AssemblyPath = string.Empty;
            public string AssemblyHash = string.Empty;
            public string Kind = "access";
            public string MapName = string.Empty;
        }

        internal static void SetCurrentMapName(string? mapName)
        {
            lock (s_gate)
                s_currentMapName = mapName ?? string.Empty;
        }

        internal static string Arm(string? name, string? scenarioFamily, string caseKind = "access")
        {
            if (caseKind != "access" && caseKind != "mining")
                return "Replay case kind must be access or mining.";
            string safeName = SanitizeName(name, "manual");
            string safeFamily = SanitizeName(scenarioFamily, "manual");
            string assemblyPath;
            string assemblyHash;
            try
            {
                assemblyPath = ArchiveCurrentAssembly(out assemblyHash);
            }
            catch (Exception ex)
            {
                return "Replay capture was not armed because the exact DLL "
                    + "could not be archived: " + ex.Message;
            }
            lock (s_gate)
            {
                if (s_arm != null)
                    return $"Replay capture already armed for '{s_arm.Name}'.";
                s_arm = new ArmState
                {
                    Name = safeName,
                    ScenarioFamily = safeFamily,
                    ArmedUtc = DateTime.UtcNow,
                    AssemblyPath = assemblyPath,
                    AssemblyHash = assemblyHash,
                    Kind = caseKind,
                };
            }
            return $"Armed the next accepted {caseKind} plan as '{safeName}' ({safeFamily}).";
        }

        internal static string Cancel()
        {
            lock (s_gate)
            {
                if (s_arm == null) return "Replay capture was not armed.";
                string name = s_arm.Name;
                s_arm = null;
                return $"Cancelled replay capture '{name}'.";
            }
        }

        internal static AccessReplayMemoryProbe? BeginMemoryProbe()
        {
            lock (s_gate)
            {
                return s_arm == null || s_arm.Kind != "access"
                    ? null
                    : AccessReplayMemoryProbe.Start();
            }
        }

        internal static AccessReplayCaptureOperation? BeginRecordAccepted(
            ExperimentalAccessCandidate candidate,
            string provenance)
        {
            ArmState? arm;
            lock (s_gate)
            {
                arm = s_arm;
                if (arm == null || arm.Kind != "access") return null;
                arm.MapName = s_currentMapName;
                s_arm = null;
            }

            return AccessReplayCaptureOperation.Start(operation =>
            {
                CancellationToken token = operation.CancellationToken;
                operation.SetProgress(0, "Validating replay capture");
                AccessPathRequest request = candidate.Request
                    ?? throw new InvalidOperationException(
                        "The accepted candidate did not retain its request.");
                if (request.Snapshot.IsEnvironmentallyDirty)
                    throw new InvalidOperationException(
                        "Capture refused because the snapshot was marked dirty: "
                        + request.Snapshot.CaptureDirtyReason);

                token.ThrowIfCancellationRequested();
                operation.SetProgress(0, "Sizing replay request");
                byte[] requestBytes = AccessReplayGraphCodec.Serialize(
                    request, token, (completed, total) =>
                        operation.SetProgress(
                            5 + (int)(completed * 70L / Math.Max(1L, total)),
                            "Encoding replay request"));
                operation.SetProgress(75, "Encoding canonical outcome");
                byte[] expectedBytes = AccessSearchReplayCanonical.Serialize(
                    candidate.SearchResult, candidate.Plan);
                token.ThrowIfCancellationRequested();
                operation.SetProgress(77, "Building replay payload");
                byte[] payload = BuildPayload(
                    requestBytes, expectedBytes, token);
                operation.SetProgress(79, "Hashing replay payload");
                string payloadHash = Sha256(payload, token);
                operation.SetProgress(80, "Preparing replay inbox");
                string root = GetInboxRoot();
                Directory.CreateDirectory(root);
                string directoryName = arm.Name + "-" + payloadHash.Substring(0, 16)
                    + CaseExtension;
                string completed = Path.Combine(root, directoryName);
                string? duplicate = Directory.GetDirectories(
                    root, "*-" + payloadHash.Substring(0, 16) + CaseExtension)
                    .FirstOrDefault();
                if (duplicate != null)
                    return "Replay case already exists: " + duplicate;

                string temporary = Path.Combine(root,
                    "." + directoryName + ".tmp-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary);
                try
                {
                    operation.SetProgress(80, "Compressing replay case");
                    string dataPath = Path.Combine(temporary, "case.bin.gz");
                    using (var file = new FileStream(
                        dataPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var gzip = new GZipStream(
                        file, CompressionLevel.Optimal, leaveOpen: false))
                    {
                        const int chunkBytes = 1024 * 1024;
                        for (int offset = 0; offset < payload.Length;
                            offset += chunkBytes)
                        {
                            token.ThrowIfCancellationRequested();
                            int count = Math.Min(chunkBytes,
                                payload.Length - offset);
                            gzip.Write(payload, offset, count);
                            operation.SetProgress(
                                80 + (int)((long)(offset + count) * 15L
                                    / Math.Max(1, payload.Length)),
                                "Compressing replay case");
                        }
                    }

                    operation.SetProgress(95, "Hashing replay identity");
                    string manifest = BuildManifest(
                        arm, candidate, provenance, payloadHash,
                        requestBytes.Length, expectedBytes.Length,
                        Sha256(requestBytes, token),
                        Sha256(expectedBytes, token), token);
                    File.WriteAllText(
                        Path.Combine(temporary, "manifest.json"), manifest,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    token.ThrowIfCancellationRequested();
                    operation.SetProgress(99, "Awaiting authoritative live acceptance");
                    operation.SetPendingPublication(
                        temporary, completed,
                        "Captured accepted search: " + completed);
                }
                catch
                {
                    if (Directory.Exists(temporary))
                        Directory.Delete(temporary, recursive: true);
                    throw;
                }

                return "Replay capture prepared for live acceptance.";
            });
        }

        private static byte[] BuildPayload(
            byte[] request,
            byte[] canonical,
            CancellationToken cancellationToken = default)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("ATD_ACCESS_REPLAY");
                writer.Write(SchemaVersion);
                writer.Write(request.Length);
                cancellationToken.ThrowIfCancellationRequested();
                writer.Write(request);
                writer.Write(canonical.Length);
                cancellationToken.ThrowIfCancellationRequested();
                writer.Write(canonical);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static string BuildManifest(
            ArmState arm,
            ExperimentalAccessCandidate candidate,
            string provenance,
            string payloadHash,
            int requestBytes,
            int canonicalBytes,
            string requestHash,
            string canonicalHash,
            CancellationToken cancellationToken = default)
        {
            Assembly assembly = typeof(AccessSearchReplayRecorder).Assembly;
            cancellationToken.ThrowIfCancellationRequested();
            AccessSearchSnapshot snapshot = candidate.Request!.Snapshot;
            AccessReplayMemoryEvidence? memoryEvidence =
                candidate.MemoryEvidence;
            string assemblyPath = arm.AssemblyPath;
            string assemblyHash = arm.AssemblyHash;
            if (!File.Exists(assemblyPath)
                || !string.Equals(
                    Sha256(File.ReadAllBytes(assemblyPath), cancellationToken),
                    assemblyHash, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Archived ATD replay binary failed its identity check.");
            string gameAssemblyFingerprint = GetGameAssemblyFingerprint(
                cancellationToken);
            string buildTimestamp = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(item => item.Key == "BuildTimestamp")?.Value
                ?? string.Empty;
            string configuration =
#if DEBUG
                "Debug";
#else
                "Release";
#endif
            AccessReplayPhaseTiming timing = candidate.Timing;
            return "{\n"
                + Json("schema", SchemaVersion.ToString(CultureInfo.InvariantCulture), false) + ",\n"
                + Json("caseName", arm.Name) + ",\n"
                + Json("scenarioFamily", arm.ScenarioFamily) + ",\n"
                + Json("mapName", arm.MapName) + ",\n"
                + Json("provenance", provenance) + ",\n"
                + Json("armedUtc", arm.ArmedUtc.ToString("O", CultureInfo.InvariantCulture)) + ",\n"
                + Json("capturedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)) + ",\n"
                + Json("requestId", candidate.Request?.RequestId ?? string.Empty) + ",\n"
                + Json("semanticPolicy", "access-search-v1") + ",\n"
                + Json("policyFingerprint",
                    candidate.Request!.Snapshot.Policy.SemanticFingerprint
                        .ToString(CultureInfo.InvariantCulture), false) + ",\n"
                + Json("payloadSha256", payloadHash) + ",\n"
                + Json("requestSha256", requestHash) + ",\n"
                + Json("canonicalSha256", canonicalHash) + ",\n"
                + Json("requestBytes", requestBytes.ToString(CultureInfo.InvariantCulture), false) + ",\n"
                + Json("canonicalBytes", canonicalBytes.ToString(CultureInfo.InvariantCulture), false) + ",\n"
                + Json("atdAssembly", assemblyPath) + ",\n"
                + Json("atdAssemblySha256", assemblyHash) + ",\n"
                + Json("buildConfiguration", configuration) + ",\n"
                + Json("buildTimestamp", buildTimestamp) + ",\n"
                + Json("gameAssemblyFingerprint", gameAssemblyFingerprint) + ",\n"
                + Json("memoryEstimatorVersion",
                    AccessSnapshotMemoryEstimator.Version) + ",\n"
                + Json("estimatedRetainedMemoryBytes",
                    snapshot.EstimatedRetainedMemoryBytes.ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("captureMemoryCeilingBytes",
                    snapshot.CaptureMemoryCeilingBytes.ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("captureMemoryMeasurement",
                    memoryEvidence == null
                        ? "unavailable"
                        : AccessReplayMemoryEvidence.MeasurementKind) + ",\n"
                + Json("captureManagedHeapBeforeBytes",
                    (memoryEvidence?.ManagedHeapBeforeBytes ?? -1L).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("captureManagedHeapAfterBytes",
                    (memoryEvidence?.ManagedHeapAfterBytes ?? -1L).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("captureManagedHeapDeltaBytes",
                    (memoryEvidence?.ManagedHeapDeltaBytes ?? -1L).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("captureWorkingSetBeforeBytes",
                    (memoryEvidence?.WorkingSetBeforeBytes ?? -1L).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("captureWorkingSetAfterBytes",
                    (memoryEvidence?.WorkingSetAfterBytes ?? -1L).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("captureWorkingSetDeltaBytes",
                    (memoryEvidence?.WorkingSetDeltaBytes ?? -1L).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("capturePrivateMemoryBeforeBytes",
                    (memoryEvidence?.PrivateMemoryBeforeBytes ?? -1L).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("capturePrivateMemoryAfterBytes",
                    (memoryEvidence?.PrivateMemoryAfterBytes ?? -1L).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("capturePrivateMemoryDeltaBytes",
                    (memoryEvidence?.PrivateMemoryDeltaBytes ?? -1L).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("captureGen0Collections",
                    (memoryEvidence?.Gen0Collections ?? -1).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("captureGen1Collections",
                    (memoryEvidence?.Gen1Collections ?? -1).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("captureGen2Collections",
                    (memoryEvidence?.Gen2Collections ?? -1).ToString(
                        CultureInfo.InvariantCulture), false) + ",\n"
                + Json("captureMeasurementMilliseconds",
                    (memoryEvidence?.ElapsedMilliseconds ?? -1d).ToString(
                        "R", CultureInfo.InvariantCulture), false) + ",\n"
                + Json("preparationMilliseconds", timing.PreparationMilliseconds.ToString("R", CultureInfo.InvariantCulture), false) + ",\n"
                + Json("searchMilliseconds", timing.SearchMilliseconds.ToString("R", CultureInfo.InvariantCulture), false) + ",\n"
                + Json("materializationMilliseconds", timing.MaterializationMilliseconds.ToString("R", CultureInfo.InvariantCulture), false) + "\n"
                + "}\n";
        }

        private static string Json(string key, string value, bool quote = true)
            => "  \"" + Escape(key) + "\": "
                + (quote ? "\"" + Escape(value) + "\"" : value);

        private static string Escape(string value)
            => (value ?? string.Empty).Replace("\\", "\\\\")
                .Replace("\"", "\\\"").Replace("\r", "\\r")
                .Replace("\n", "\\n");

        private static string SanitizeName(string? value, string fallback)
        {
            string source = string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
            var builder = new StringBuilder(Math.Min(source.Length, 64));
            foreach (char c in source)
            {
                if (builder.Length >= 64) break;
                builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_'
                    ? char.ToLowerInvariant(c)
                    : '-');
            }
            string result = builder.ToString().Trim('-');
            return result.Length == 0 ? fallback : result;
        }

        internal static string GetInboxRoot()
            => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry", "AccessSearchLaboratory",
                "AutoTerrainDesignations", "inbox");

        internal static string Sha256(byte[] bytes)
            => Sha256(bytes, CancellationToken.None);

        internal static string Sha256(
            byte[] bytes, CancellationToken cancellationToken)
        {
            using (SHA256 hash = SHA256.Create())
            {
                const int chunkBytes = 1024 * 1024;
                int offset = 0;
                while (offset < bytes.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(chunkBytes, bytes.Length - offset);
                    hash.TransformBlock(bytes, offset, count, bytes, offset);
                    offset += count;
                }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return string.Concat(hash.Hash.Select(value =>
                    value.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        internal static string ResolveAssemblyPath(Assembly assembly)
        {
            if (!string.IsNullOrEmpty(assembly.Location)
                && File.Exists(assembly.Location))
                return assembly.Location;
            if (!string.IsNullOrEmpty(assembly.CodeBase)
                && Uri.TryCreate(assembly.CodeBase, UriKind.Absolute, out Uri uri)
                && uri.IsFile && File.Exists(uri.LocalPath))
                return uri.LocalPath;
            string installed = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry", "Mods", "AutoTerrainDesignations",
                "AutoTerrainDesignations.dll");
            if (File.Exists(installed)) return installed;
            throw new FileNotFoundException(
                "Unable to resolve the loaded ATD assembly file.");
        }

        private static string ArchiveCurrentAssembly(out string assemblyHash)
        {
            string source = ResolveAssemblyPath(
                typeof(AccessSearchReplayRecorder).Assembly);
            byte[] bytes = File.ReadAllBytes(source);
            assemblyHash = Sha256(bytes);
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry", "AccessSearchLaboratory",
                "AutoTerrainDesignations", "binaries", assemblyHash);
            string archived = Path.Combine(
                directory, "AutoTerrainDesignations.dll");
            Directory.CreateDirectory(directory);
            if (!File.Exists(archived))
            {
                string temporary = archived + ".tmp-"
                    + Guid.NewGuid().ToString("N");
                File.WriteAllBytes(temporary, bytes);
                try
                {
                    File.Move(temporary, archived);
                }
                catch
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                    if (!File.Exists(archived)) throw;
                }
            }
            if (!string.Equals(
                    Sha256(File.ReadAllBytes(archived)), assemblyHash,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Archived ATD replay binary hash mismatch.");
            return archived;
        }

        internal static string GetGameAssemblyFingerprint(
            CancellationToken cancellationToken = default)
        {
            string[] names = { "Mafi", "Mafi.Base", "Mafi.Core", "Mafi.Unity" };
            return string.Join(";", names.Select(name =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(item => string.Equals(
                        item.GetName().Name, name, StringComparison.Ordinal))
                    ?? Assembly.Load(new AssemblyName(name));
                if (string.IsNullOrEmpty(assembly.Location)
                    || !File.Exists(assembly.Location))
                    throw new InvalidDataException(
                        "Game assembly location unavailable: " + name);
                return name + "=" + Sha256(
                    File.ReadAllBytes(assembly.Location), cancellationToken);
            }));
        }
    }

    internal sealed class AccessReplayCaptureOperation
    {
        private volatile string m_stage = "Starting replay capture";
        private volatile int m_percent;
        private volatile bool m_abortRequested;
        private Task<string> m_task = null!;
        private string? m_temporaryDirectory;
        private string? m_completedDirectory;
        private string? m_publicationMessage;
        private readonly CancellationTokenSource m_cancellation =
            new CancellationTokenSource();

        private AccessReplayCaptureOperation() { }

        internal string Stage => m_stage;
        internal int Percent => m_percent;
        internal bool IsComplete => m_task.IsCompleted;
        internal bool IsFaulted => m_task.IsFaulted;
        internal bool IsCanceled => m_task.IsCanceled;
        internal bool IsAbortRequested => m_abortRequested;
        internal CancellationToken CancellationToken
            => m_cancellation.Token;

        internal static AccessReplayCaptureOperation Start(
            Func<AccessReplayCaptureOperation, string> work)
        {
            var operation = new AccessReplayCaptureOperation();
            operation.m_task = Task.Run(
                () => work(operation), operation.m_cancellation.Token);
            return operation;
        }

        internal void SetProgress(int percent, string stage)
        {
            m_percent = Math.Max(0, Math.Min(100, percent));
            m_stage = "Recording access replay: " + stage;
        }

        internal void Cancel()
        {
            m_abortRequested = true;
            m_stage = "Cancelling access replay capture";
            m_cancellation.Cancel();
        }

        internal void CancelAndDiscardWhenComplete()
        {
            Cancel();
            m_task.ContinueWith(completed => TryFinalizePendingPublication(false, out string ignored));
        }

        internal void SetPendingPublication(
            string temporaryDirectory,
            string completedDirectory,
            string publicationMessage)
        {
            m_temporaryDirectory = temporaryDirectory;
            m_completedDirectory = completedDirectory;
            m_publicationMessage = publicationMessage;
        }

        internal void CompleteOnMainThread(
            bool authoritativeAcceptance = true,
            string rejectionReason = "")
        {
            if (!m_task.IsCompleted)
                throw new InvalidOperationException(
                    "Replay capture operation has not completed.");
            if (m_task.IsFaulted)
            {
                TryFinalizePendingPublication(
                    publish: false, out string cleanupFailure);
                Exception failure = m_task.Exception?.GetBaseException()
                    ?? new InvalidOperationException("Unknown replay capture failure.");
                AutoDepthDesignation.s_log.Warning(
                    "[ATD Access Replay] capture failed closed: " + failure
                    + (string.IsNullOrEmpty(cleanupFailure)
                        ? string.Empty
                        : "; staging cleanup failed: " + cleanupFailure));
                return;
            }
            if (m_task.IsCanceled)
            {
                TryFinalizePendingPublication(
                    publish: false, out string cleanupFailure);
                AutoDepthDesignation.s_log.Info(
                    "[ATD Access Replay] capture aborted by request; no case was completed."
                    + (string.IsNullOrEmpty(cleanupFailure)
                        ? string.Empty
                        : " Staging cleanup failed: " + cleanupFailure));
                return;
            }
            if (m_abortRequested || !authoritativeAcceptance)
            {
                TryFinalizePendingPublication(
                    publish: false, out string cleanupFailure);
                AutoDepthDesignation.s_log.Warning(
                    "[ATD Access Replay] capture refused before publication: "
                    + (m_abortRequested
                        ? "aborted by request"
                        : string.IsNullOrWhiteSpace(rejectionReason)
                            ? "authoritative live acceptance failed"
                            : rejectionReason)
                    + (string.IsNullOrEmpty(cleanupFailure)
                        ? string.Empty
                        : "; staging cleanup failed: " + cleanupFailure));
                return;
            }
            if (m_temporaryDirectory != null
                && m_completedDirectory != null)
            {
                if (!TryFinalizePendingPublication(
                        publish: true, out string publicationFailure))
                {
                    AutoDepthDesignation.s_log.Warning(
                        "[ATD Access Replay] capture failed closed during publication: "
                        + publicationFailure);
                    return;
                }
                m_percent = 100;
                m_stage = "Recording access replay: Replay capture complete";
                AutoDepthDesignation.s_log.Info(
                    "[ATD Access Replay] " + m_publicationMessage);
                return;
            }
            AutoDepthDesignation.s_log.Info(
                "[ATD Access Replay] " + m_task.Result);
        }

        internal bool TryFinalizePendingPublication(
            bool publish,
            out string failure)
        {
            string? temporary = m_temporaryDirectory;
            string? completed = m_completedDirectory;
            m_temporaryDirectory = null;
            m_completedDirectory = null;
            failure = string.Empty;
            if (temporary == null || !Directory.Exists(temporary))
            {
                if (publish)
                    failure = "staged capture directory is absent";
                return !publish;
            }
            try
            {
                if (publish)
                {
                    if (completed == null)
                        throw new InvalidOperationException(
                            "Completed capture directory was not assigned.");
                    Directory.Move(temporary, completed);
                }
                else
                {
                    Directory.Delete(temporary, recursive: true);
                }
                return true;
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": " + ex.Message;
                if (Directory.Exists(temporary))
                {
                    try
                    {
                        Directory.Delete(temporary, recursive: true);
                    }
                    catch (Exception cleanupEx)
                    {
                        failure += "; cleanup " + cleanupEx.GetType().Name
                            + ": " + cleanupEx.Message;
                    }
                }
                return false;
            }
        }
    }

    /// <summary>
    /// The only reflection seam used by the standalone laboratory runner.
    /// </summary>
    internal static class AccessSearchReplayFacade
    {
        private const int MaxCompressedBytes = 256 * 1024 * 1024;
        private const int MaxPayloadBytes = 1024 * 1024 * 1024;

        internal static bool TryReplayCase(string caseDirectory, out string report)
            => TryReplayCaseCore(
                caseDirectory,
                allowAssemblyMismatch: false,
                allowGameAssemblyMismatch: false,
                tracePath: null,
                out report);

        internal static bool TryReplayCandidate(
            string caseDirectory,
            bool allowGameAssemblyMismatch,
            out string report)
            => TryReplayCaseCore(
                caseDirectory,
                allowAssemblyMismatch: true,
                allowGameAssemblyMismatch,
                tracePath: null,
                out report);

        internal static bool TryTraceCandidate(
            string caseDirectory,
            bool allowGameAssemblyMismatch,
            string tracePath,
            out string report)
            => TryReplayCaseCore(
                caseDirectory,
                allowAssemblyMismatch: true,
                allowGameAssemblyMismatch,
                tracePath,
                out report);

        internal static bool TryAuditCase(
            string caseDirectory,
            out string report)
        {
            if (Mining.MiningReplayFacade.IsMiningCase(caseDirectory))
                return Mining.MiningReplayFacade.Replay(caseDirectory, true, false, 1, out report);
            try
            {
                LoadBenchmarkInput(
                    caseDirectory,
                    out AccessPathRequest request,
                    out byte[] expected);
                var canonical = (AccessSearchReplayCanonical.CanonicalRecord)
                    AccessReplayGraphCodec.Deserialize(
                        expected,
                        typeof(AccessSearchReplayCanonical.CanonicalRecord));
                AccessDesignationPlan plan = canonical.Plan;
                AccessSearchSnapshot snapshot = request.Snapshot;
                var capturedResult = new AccessSearchResult(
                    canonical.Success,
                    canonical.FailureReason,
                    canonical.StartOrigin,
                    canonical.Path,
                    BitConverter.ToSingle(
                        BitConverter.GetBytes(canonical.CostBits), 0),
                    0,
                    new Dictionary<string, int>(),
                    0f, 0f, 0f, 0f, 0f,
                    reachedGoalKind: canonical.ReachedGoalKind,
                    v2Route: canonical.V2Route);
                AccessDesignationPlan currentPlan =
                    AccessPathMaterializer.Materialize(
                        new AccessSearchWorkspace(snapshot),
                        capturedResult);
                var exactTerrain = new List<Tile2i>();
                var isolatedExactTerrain = new List<Tile2i>();
                var exactSourceTerrain = new HashSet<Tile2i>(
                    canonical.V2Route?.RouteSteps
                        .Where(step => step.Transition != null
                            && step.Transition.ScoreOnlyGeneratedExteriorRays)
                        .SelectMany(step => step.Transition!.Delta)
                        .Where(item => !AccessPathMaterializer
                            .ProfileHasTerrainDelta(
                                snapshot, item.Origin, item.Profile))
                        .Select(item => item.Origin)
                    ?? Enumerable.Empty<Tile2i>());
                foreach (AccessPlannedDesignation designation
                    in plan.Designations)
                {
                    if (AccessPathMaterializer.ProfileHasTerrainDelta(
                            snapshot, designation.Origin,
                            designation.Profile))
                        continue;
                    exactTerrain.Add(designation.Origin);
                    bool touchesAnother = plan.Designations.Any(other =>
                        other.Origin != designation.Origin
                        && Math.Abs(other.Origin.X - designation.Origin.X) <= 4
                        && Math.Abs(other.Origin.Y - designation.Origin.Y) <= 4);
                    if (!touchesAnother)
                        isolatedExactTerrain.Add(designation.Origin);
                }

                int denseProps = 0;
                int handledByPlannedTerrain = 0;
                int plannedExcavationCandidates = 0;
                var handledKeys = new HashSet<string>(StringComparer.Ordinal);
                var propDetails = new List<string>();
                foreach (AccessPropCleanupInfo cleanup in plan.CleanupOrigins)
                {
                    foreach (AccessPropSample sample in cleanup.Samples)
                    {
                        if (!sample.IsDenseDebris
                            || !handledKeys.Add(sample.CleanupObjectKey))
                            continue;
                        denseProps++;
                        bool handled = false;
                        bool excavationCandidate = false;
                        var covering = new List<string>();
                        foreach (AccessPlannedDesignation designation
                            in plan.Designations)
                        {
                            AccessHandoffOperation operation =
                                plan.HandoffOperationsByOrigin.TryGetValue(
                                    designation.Origin,
                                    out AccessHandoffOperation mappedOperation)
                                    ? mappedOperation
                                    : AccessHandoffOperation.Leveling;
                            var data = new DesignationData(
                                        designation.Origin,
                                        new HeightTilesI(
                                            designation.Profile.Nw2 / 2),
                                        new HeightTilesI(
                                            designation.Profile.Ne2 / 2),
                                        new HeightTilesI(
                                            designation.Profile.Se2 / 2),
                                        new HeightTilesI(
                                            designation.Profile.Sw2 / 2));
                            if (!AccessPropCleanupPolicy
                                    .TryGetDesignationTargetHeight(
                                        data, sample, out float targetHeight))
                                continue;
                            covering.Add($"{designation.Origin}:{operation}:"
                                + $"{targetHeight.ToString("0.###", CultureInfo.InvariantCulture)}");
                            excavationCandidate |=
                                (operation == AccessHandoffOperation.Mining
                                    || operation == AccessHandoffOperation.Leveling)
                                && targetHeight
                                    < sample.PlacedHeight - 0.0001f;
                            handled |= AccessPropCleanupPolicy
                                .PlannedOperationRemovesNonTreeProp(
                                    operation, data, sample);
                        }
                        if (handled)
                            handledByPlannedTerrain++;
                        if (excavationCandidate)
                            plannedExcavationCandidates++;
                        propDetails.Add($"{sample.CleanupObjectKey}@{sample.Tile}"
                            + $":placed={sample.PlacedHeight.ToString("0.###", CultureInfo.InvariantCulture)}"
                            + $":threshold={sample.DumpBurialThreshold.ToString("0.###", CultureInfo.InvariantCulture)}"
                            + $":covering={string.Join("|", covering)}"
                            + $":handled={handled}");
                    }
                }

                string auditManifest = File.ReadAllText(
                    Path.Combine(
                        Path.GetFullPath(caseDirectory), "manifest.json"),
                    Encoding.UTF8);
                report = $"case={ManifestString(auditManifest, "caseName")} "
                    + $"start={canonical.StartOrigin} "
                    + $"firstV2Lanes={(canonical.V2Route == null || canonical.V2Route.States.Count == 0 ? "none" : canonical.V2Route.States[0].GetLaneOrigin(0) + "|" + canonical.V2Route.States[0].GetLaneOrigin(1))} "
                    + $"designations={plan.Designations.Count} "
                    + $"currentPlanValid={currentPlan.IsValid} "
                    + $"currentDesignations={currentPlan.Designations.Count} "
                    + $"exactTerrain={exactTerrain.Count} "
                    + $"isolatedExactTerrain={isolatedExactTerrain.Count} "
                    + $"isolatedExactOrigins={string.Join(";", isolatedExactTerrain)} "
                    + $"exactSourceTerrain={exactSourceTerrain.Count} "
                    + $"denseProps={denseProps} "
                    + $"plannedExcavationCandidates={plannedExcavationCandidates} "
                    + $"handledByPlannedTerrain={handledByPlannedTerrain} "
                    + $"propDetails={string.Join(";", propDetails)}";
                return currentPlan.IsValid
                    && currentPlan.Designations.Count
                        == plan.Designations.Count - exactSourceTerrain.Count
                    && handledByPlannedTerrain
                        == plannedExcavationCandidates;
            }
            catch (Exception ex)
            {
                report = "Case audit failed closed: " + ex;
                return false;
            }
        }

        private static bool TryReplayCaseCore(
            string caseDirectory,
            bool allowAssemblyMismatch,
            bool allowGameAssemblyMismatch,
            string? tracePath,
            out string report)
        {
            if (Mining.MiningReplayFacade.IsMiningCase(caseDirectory))
            {
                if (tracePath != null)
                {
                    report = "Expansion tracing applies to access searches, not mining plans.";
                    return false;
                }
                return Mining.MiningReplayFacade.Replay(caseDirectory, allowAssemblyMismatch,
                    allowGameAssemblyMismatch, 1, out report);
            }
            try
            {
                string directory = Path.GetFullPath(caseDirectory);
                string dataPath = Path.Combine(directory, "case.bin.gz");
                var info = new FileInfo(dataPath);
                if (!info.Exists || info.Length <= 0 || info.Length > MaxCompressedBytes)
                    throw new InvalidDataException("Replay case has an invalid compressed size.");

                byte[] payload;
                using (var input = info.OpenRead())
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[64 * 1024];
                    int read;
                    while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > MaxPayloadBytes)
                            throw new InvalidDataException("Replay payload exceeds its size limit.");
                        output.Write(buffer, 0, read);
                    }
                    payload = output.ToArray();
                }

                string manifestPath = Path.Combine(directory, "manifest.json");
                if (!File.Exists(manifestPath))
                    throw new InvalidDataException("Replay manifest is missing.");
                string manifest = File.ReadAllText(manifestPath, Encoding.UTF8);
                RequireManifest(manifest, "semanticPolicy", "access-search-v1");
                RequireManifest(manifest, "payloadSha256",
                    AccessSearchReplayRecorder.Sha256(payload));
                string assemblyPath = AccessSearchReplayRecorder.ResolveAssemblyPath(
                    typeof(AccessSearchReplayFacade).Assembly);
                string candidateAssemblyHash =
                    AccessSearchReplayRecorder.Sha256(
                        File.ReadAllBytes(assemblyPath));
                string recordedAssemblyHash = ManifestString(
                    manifest, "atdAssemblySha256");
                if (!allowAssemblyMismatch
                    && !string.Equals(
                        recordedAssemblyHash, candidateAssemblyHash,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Replay manifest mismatch: atdAssemblySha256");
                if (!allowAssemblyMismatch)
                    RequireManifest(
                        manifest, "buildConfiguration", "Release");
                string currentGameFingerprint =
                    AccessSearchReplayRecorder.GetGameAssemblyFingerprint();
                string recordedGameFingerprint = ManifestString(
                    manifest, "gameAssemblyFingerprint");
                if (!allowGameAssemblyMismatch
                    && !string.Equals(
                        recordedGameFingerprint, currentGameFingerprint,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Replay manifest mismatch: gameAssemblyFingerprint");

                AccessPathRequest request;
                byte[] expected;
                byte[] requestBytes;
                using (var stream = new MemoryStream(payload, writable: false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    if (reader.ReadString() != "ATD_ACCESS_REPLAY")
                        throw new InvalidDataException("Replay magic mismatch.");
                    int schema = reader.ReadInt32();
                    if (schema != 1)
                        throw new InvalidDataException($"Unsupported replay schema {schema}.");
                    int requestLength = ReadLength(reader, 768 * 1024 * 1024);
                    requestBytes = reader.ReadBytes(requestLength);
                    if (requestBytes.Length != requestLength)
                        throw new EndOfStreamException("Truncated replay request.");
                    request = (AccessPathRequest)AccessReplayGraphCodec.Deserialize(
                        requestBytes, typeof(AccessPathRequest));
                    int expectedLength = ReadLength(reader, 256 * 1024 * 1024);
                    expected = reader.ReadBytes(expectedLength);
                    if (expected.Length != expectedLength || stream.Position != stream.Length)
                        throw new InvalidDataException("Truncated or trailing replay data.");
                }

                RequireManifest(manifest, "requestSha256",
                    AccessSearchReplayRecorder.Sha256(requestBytes));
                RequireManifest(manifest, "canonicalSha256",
                    AccessSearchReplayRecorder.Sha256(expected));
                if (!int.TryParse(
                        ManifestScalar(manifest, "policyFingerprint"),
                        NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int policyFingerprint)
                    || policyFingerprint
                        != request.Snapshot.Policy.SemanticFingerprint)
                    throw new InvalidDataException(
                        "Replay policy fingerprint mismatch.");

                if (request.Snapshot.IsEnvironmentallyDirty)
                    throw new InvalidDataException("Replay snapshot is marked dirty.");
                AccessSearchExpansionTraceCollector? trace =
                    string.IsNullOrWhiteSpace(tracePath)
                        ? null
                        : new AccessSearchExpansionTraceCollector();
                AccessSearchExecutionOutcome execution =
                    AccessSearchExecutionCore.Execute(request, trace);
                AccessSearchResult result = execution.SearchResult;
                AccessSearchDiagnostics replayDiagnostics =
                    result.Diagnostics;
                AccessDesignationPlan plan = execution.Plan;
                double preparationMs =
                    execution.Timing.PreparationMilliseconds;
                double searchMs = execution.Timing.SearchMilliseconds;
                double materializeMs =
                    execution.Timing.MaterializationMilliseconds;
                byte[] normalizedExpected =
                    AccessSearchReplayCanonical.Normalize(expected);
                byte[] actual = AccessSearchReplayCanonical.Serialize(result, plan);
                string recordedExpectedHash =
                    AccessSearchReplayRecorder.Sha256(expected);
                string expectedHash =
                    AccessSearchReplayRecorder.Sha256(normalizedExpected);
                string actualHash = AccessSearchReplayRecorder.Sha256(actual);
                bool exact = normalizedExpected.SequenceEqual(actual);
                if (trace != null)
                    trace.WriteCsv(Path.GetFullPath(tracePath!));
                string difference = exact
                    ? "none"
                    : AccessSearchReplayCanonical.DescribeDifference(
                        normalizedExpected, result, plan);
                report = $"case={ManifestString(manifest, "caseName")} "
                    + $"policy={ManifestString(manifest, "semanticPolicy")} "
                    + $"policyFingerprint={ManifestScalar(manifest, "policyFingerprint")} "
                    + $"exact={exact} expectedSha256={expectedHash} "
                    + $"recordedCanonicalSha256={recordedExpectedHash} "
                    + $"actualSha256={actualHash} diff={difference} "
                    + $"candidateAssemblySha256={candidateAssemblyHash} "
                    + $"recordedAssemblySha256={recordedAssemblyHash} "
                    + $"gameAssemblyMatch={string.Equals(recordedGameFingerprint, currentGameFingerprint, StringComparison.Ordinal)} "
                    + $"currentGameFingerprintSha256={AccessSearchReplayRecorder.Sha256(Encoding.UTF8.GetBytes(currentGameFingerprint))} "
                    + $"searchMs={searchMs.ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"preparationMs={preparationMs.ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"materializeMs={materializeMs.ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"v2BandExpansions={replayDiagnostics.V2BandExpansions} "
                    + $"v2GroundExpansions={replayDiagnostics.V2GroundExpansions} "
                    + $"g2vFirstEnqueueVisited={replayDiagnostics.V2GroundToVFirstEnqueueVisited} "
                    + $"groundReplacementChecks={replayDiagnostics.V2OrdinaryGroundReplacementChecks} "
                    + $"groundReplacementCandidates={replayDiagnostics.V2OrdinaryGroundReplacementCandidates} "
                    + $"groundReplacementPrunes={replayDiagnostics.V2OrdinaryGroundReplacementPrunes} "
                    + $"inGamePreparationMs={ManifestScalar(manifest, "preparationMilliseconds")} "
                    + $"inGameSearchMs={ManifestScalar(manifest, "searchMilliseconds")} "
                    + $"inGameMaterializeMs={ManifestScalar(manifest, "materializationMilliseconds")} "
                    + $"traceRows={(trace?.Count ?? 0)} "
                    + $"tracePath={(trace == null ? "none" : Path.GetFullPath(tracePath!))} "
                    + $"payloadSha256={AccessSearchReplayRecorder.Sha256(payload)}";
                return exact;
            }
            catch (Exception ex)
            {
                report = "Replay failed closed: " + ex;
                return false;
            }
        }

        private sealed class AccessSearchExpansionTraceCollector
            : IAccessSearchExecutionControl
        {
            private readonly Stopwatch m_timer = Stopwatch.StartNew();
            private readonly List<TraceRow> m_rows = new List<TraceRow>();
            private readonly Dictionary<int, AccessV2GroundExpansionOutcomeTrace>
                m_groundOutcomes =
                    new Dictionary<int, AccessV2GroundExpansionOutcomeTrace>();

            public bool CancellationRequested => false;
            public bool CaptureOverlay => false;
            public bool CaptureExpansionTrace => true;
            public int Count => m_rows.Count;

            public void Publish(
                string phase,
                string subphase,
                int visited,
                int pending)
            {
            }

            public void RecordNode(
                Tile2i tile,
                int height2,
                bool isGround,
                int? priority)
            {
            }

            public void RecordExpansion(AccessV2ExpansionTrace expansion)
                => m_rows.Add(new TraceRow(
                    m_timer.Elapsed.TotalMilliseconds, expansion));

            public void RecordGroundExpansionOutcome(
                AccessV2GroundExpansionOutcomeTrace outcome)
                => m_groundOutcomes[outcome.Ordinal] = outcome;

            public void WriteCsv(string path)
            {
                string? parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                using (var writer = new StreamWriter(
                    path, append: false, new UTF8Encoding(false)))
                {
                    writer.WriteLine(
                        "ordinal,elapsedMs,kind,x,y,height2,groundHeight2," +
                        "firstExpansion,queueAge,enqueuedAtVisited,cost," +
                        "groundRelaunchedV,axis,entryX,entryY," +
                        "hasHandoff,portalRootX,portalRootY," +
                        "groundToVAdapter,projectedGroundEntry," +
                        "historySignature,historyOrigins,historyRays," +
                        "historyCleanupKeys," +
                        "potentialOwnerGlobal,potentialOwnerId," +
                        "launchGroundX,launchGroundY,launchComponent," +
                        "mergeCenterX,mergeCenterY,groundComponent," +
                        "ordinaryGroundBestCost," +
                        "launchHistoryOrigins,launchHistoryRays," +
                        "launchHistoryCleanupKeys," +
                        "labelKeyHash," +
                        "goalAtPop,suffixAttempted,suffixSucceeded," +
                        "groundEnqueueAttempts,groundEnqueueAccepted," +
                        "vEnqueueAttempts,vEnqueueAccepted");
                    for (int index = 0; index < m_rows.Count; index++)
                    {
                        m_groundOutcomes.TryGetValue(
                            m_rows[index].Ordinal,
                            out AccessV2GroundExpansionOutcomeTrace outcome);
                        m_rows[index].Write(writer, outcome);
                    }
                }
            }

            private readonly struct TraceRow
            {
                private readonly double m_elapsedMilliseconds;
                private readonly AccessV2ExpansionTrace m_expansion;

                public TraceRow(
                    double elapsedMilliseconds,
                    AccessV2ExpansionTrace expansion)
                {
                    m_elapsedMilliseconds = elapsedMilliseconds;
                    m_expansion = expansion;
                }

                public int Ordinal => m_expansion.Ordinal;

                public void Write(
                    TextWriter writer,
                    AccessV2GroundExpansionOutcomeTrace outcome)
                {
                    AccessV2ExpansionTrace item = m_expansion;
                    writer.Write(item.Ordinal.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(m_elapsedMilliseconds.ToString(
                        "0.###", CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.IsGround ? "G" : "V");
                    writer.Write(',');
                    writer.Write(item.Center.X.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.Center.Y.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.Height2.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    if (item.GroundHeight2.HasValue)
                        writer.Write(item.GroundHeight2.Value.ToString(
                            CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.FirstExpansion ? "1" : "0");
                    writer.Write(',');
                    writer.Write(item.QueueAge.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.EnqueuedAtVisited.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.Cost.ToString(
                        "R", CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.IsGroundRelaunchedV ? "1" : "0");
                    writer.Write(',');
                    if (item.Axis.HasValue)
                        writer.Write(item.Axis.Value.ToString());
                    writer.Write(',');
                    if (item.EntryDirection.HasValue)
                        writer.Write(item.EntryDirection.Value.X.ToString(
                            CultureInfo.InvariantCulture));
                    writer.Write(',');
                    if (item.EntryDirection.HasValue)
                        writer.Write(item.EntryDirection.Value.Y.ToString(
                            CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.HasHandoff ? "1" : "0");
                    writer.Write(',');
                    if (item.FixedNavigationPortalRoot.HasValue)
                        writer.Write(item.FixedNavigationPortalRoot.Value.X
                            .ToString(CultureInfo.InvariantCulture));
                    writer.Write(',');
                    if (item.FixedNavigationPortalRoot.HasValue)
                        writer.Write(item.FixedNavigationPortalRoot.Value.Y
                            .ToString(CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.IsGroundToVAdapter ? "1" : "0");
                    writer.Write(',');
                    writer.Write(item.IsProjectedGroundEntry ? "1" : "0");
                    writer.Write(',');
                    writer.Write(item.HistorySignature.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.HistoryOrigins.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.HistoryRayConstraints.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.HistoryCleanupKeys.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.PotentialOwnerIsGlobal ? "1" : "0");
                    writer.Write(',');
                    writer.Write(item.PotentialOwnerId.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    if (item.GroundLaunchCenter.HasValue)
                        writer.Write(item.GroundLaunchCenter.Value.X.ToString(
                            CultureInfo.InvariantCulture));
                    writer.Write(',');
                    if (item.GroundLaunchCenter.HasValue)
                        writer.Write(item.GroundLaunchCenter.Value.Y.ToString(
                            CultureInfo.InvariantCulture));
                    writer.Write(',');
                    if (item.GroundLaunchComponent.HasValue)
                        writer.Write(item.GroundLaunchComponent.Value.ToString(
                            CultureInfo.InvariantCulture));
                    writer.Write(',');
                    if (item.PotentialMergeCenter.HasValue)
                        writer.Write(item.PotentialMergeCenter.Value.X.ToString(
                            CultureInfo.InvariantCulture));
                    writer.Write(',');
                    if (item.PotentialMergeCenter.HasValue)
                        writer.Write(item.PotentialMergeCenter.Value.Y.ToString(
                            CultureInfo.InvariantCulture));
                    writer.Write(',');
                    if (item.GroundComponent.HasValue)
                        writer.Write(item.GroundComponent.Value.ToString(
                            CultureInfo.InvariantCulture));
                    writer.Write(',');
                    if (item.OrdinaryGroundBestCost.HasValue)
                        writer.Write(item.OrdinaryGroundBestCost.Value.ToString(
                            "R", CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.LaunchHistoryOrigins.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.LaunchHistoryRayConstraints.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.LaunchHistoryCleanupKeys.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(item.LabelKeyHash.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(outcome.GoalAtPop ? "1" : "0");
                    writer.Write(',');
                    writer.Write(outcome.SuffixAttempted ? "1" : "0");
                    writer.Write(',');
                    writer.Write(outcome.SuffixSucceeded ? "1" : "0");
                    writer.Write(',');
                    writer.Write(outcome.GroundEnqueueAttempts.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(outcome.GroundEnqueueAccepted.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(outcome.VEnqueueAttempts.ToString(
                        CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.WriteLine(outcome.VEnqueueAccepted.ToString(
                        CultureInfo.InvariantCulture));
                }
            }
        }

        internal static bool TryBenchmarkCase(
            string caseDirectory,
            int repetitions,
            bool allowGameAssemblyMismatch,
            out string report)
        {
            if (Mining.MiningReplayFacade.IsMiningCase(caseDirectory))
                return Mining.MiningReplayFacade.Replay(caseDirectory, true, allowGameAssemblyMismatch, repetitions, out report);
            if (repetitions < 1 || repetitions > 50)
            {
                report = "Benchmark repetitions must be between 1 and 50.";
                return false;
            }
            if (!TryReplayCaseCore(
                caseDirectory,
                allowAssemblyMismatch: true,
                allowGameAssemblyMismatch,
                tracePath: null,
                out string validationReport))
            {
                report = "Benchmark semantic preflight failed: "
                    + validationReport;
                return false;
            }

            try
            {
                LoadBenchmarkInput(
                    caseDirectory,
                    out AccessPathRequest request,
                    out byte[] expected);
                string manifest = File.ReadAllText(
                    Path.Combine(
                        Path.GetFullPath(caseDirectory), "manifest.json"),
                    Encoding.UTF8);
                long estimatedRetainedMemoryBytes =
                    request.Snapshot.EstimatedRetainedMemoryBytes;
                long captureMemoryCeilingBytes =
                    request.Snapshot.CaptureMemoryCeilingBytes;
                string captureMemoryMeasurement =
                    TryManifestString(
                        manifest, "captureMemoryMeasurement")
                    ?? "unavailable";
                string memoryEstimatorVersion =
                    TryManifestString(
                        manifest, "memoryEstimatorVersion")
                    ?? "embedded-unknown";
                long captureManagedHeapDeltaBytes =
                    TryManifestScalar(
                        manifest, "captureManagedHeapDeltaBytes",
                        out long capturedHeapDelta)
                        ? capturedHeapDelta
                        : -1L;
                long captureWorkingSetDeltaBytes =
                    TryManifestScalar(
                        manifest, "captureWorkingSetDeltaBytes",
                        out long capturedWorkingSetDelta)
                        ? capturedWorkingSetDelta
                        : -1L;
                long capturePrivateMemoryDeltaBytes =
                    TryManifestScalar(
                        manifest, "capturePrivateMemoryDeltaBytes",
                        out long capturedPrivateDelta)
                        ? capturedPrivateDelta
                        : -1L;
                var preparation = new List<double>(repetitions);
                var search = new List<double>(repetitions);
                var materialization = new List<double>(repetitions);
                var total = new List<double>(repetitions);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long heapBefore = GC.GetTotalMemory(forceFullCollection: false);
                int gen0Before = GC.CollectionCount(0);
                int gen1Before = GC.CollectionCount(1);
                int gen2Before = GC.CollectionCount(2);
                Process process = Process.GetCurrentProcess();
                process.Refresh();
                TimeSpan cpuBefore = process.TotalProcessorTime;
                long peakWorkingSet = process.WorkingSet64;
                Stopwatch wall = Stopwatch.StartNew();

                for (int index = 0; index < repetitions; index++)
                {
                    AccessSearchExecutionOutcome execution =
                        AccessSearchExecutionCore.Execute(request);
                    AccessSearchResult result = execution.SearchResult;
                    AccessDesignationPlan plan = execution.Plan;
                    double preparationMs =
                        execution.Timing.PreparationMilliseconds;
                    double searchMs = execution.Timing.SearchMilliseconds;
                    double materializationMs =
                        execution.Timing.MaterializationMilliseconds;
                    byte[] actual = AccessSearchReplayCanonical.Serialize(
                        result, plan);
                    if (!expected.SequenceEqual(actual))
                    {
                        report = "Benchmark semantic regression at repetition "
                            + (index + 1).ToString(CultureInfo.InvariantCulture)
                            + ": "
                            + AccessSearchReplayCanonical.DescribeDifference(
                                expected, result, plan);
                        return false;
                    }
                    preparation.Add(preparationMs);
                    search.Add(searchMs);
                    materialization.Add(materializationMs);
                    total.Add(preparationMs + searchMs + materializationMs);
                    process.Refresh();
                    peakWorkingSet = Math.Max(
                        peakWorkingSet, process.WorkingSet64);
                }

                wall.Stop();
                process.Refresh();
                double cpuMs = (process.TotalProcessorTime - cpuBefore)
                    .TotalMilliseconds;
                long heapAfter = GC.GetTotalMemory(forceFullCollection: false);
                string candidateHash = AccessSearchReplayRecorder.Sha256(
                    File.ReadAllBytes(
                        AccessSearchReplayRecorder.ResolveAssemblyPath(
                            typeof(AccessSearchReplayFacade).Assembly)));
                report = $"benchmarkExact=True repetitions={repetitions} "
                    + $"candidateAssemblySha256={candidateHash} "
                    + $"preparationMedianMs={Median(preparation).ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"searchMedianMs={Median(search).ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"materializeMedianMs={Median(materialization).ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"totalMedianMs={Median(total).ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"totalMinMs={total.Min().ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"totalMaxMs={total.Max().ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"measuredWallMs={wall.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"cpuMs={cpuMs.ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"memoryEstimatorVersion={memoryEstimatorVersion} "
                    + $"estimatedRetainedMemoryBytes={estimatedRetainedMemoryBytes} "
                    + $"estimatedRetainedMemoryMiB="
                    + $"{(estimatedRetainedMemoryBytes / (1024d * 1024d)).ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"captureMemoryCeilingBytes={captureMemoryCeilingBytes} "
                    + $"captureMemoryMeasurement={captureMemoryMeasurement} "
                    + $"captureManagedHeapDeltaBytes={captureManagedHeapDeltaBytes} "
                    + $"captureWorkingSetDeltaBytes={captureWorkingSetDeltaBytes} "
                    + $"capturePrivateMemoryDeltaBytes={capturePrivateMemoryDeltaBytes} "
                    + $"captureManagedHeapDeltaToEstimateRatio="
                    + FormatRatio(captureManagedHeapDeltaBytes,
                        estimatedRetainedMemoryBytes) + " "
                    + $"peakWorkingSetBytes={peakWorkingSet} "
                    + $"managedHeapDeltaBytes={heapAfter - heapBefore} "
                    + $"replayManagedHeapDeltaToEstimateRatio="
                    + (repetitions == 1
                        ? FormatRatio(heapAfter - heapBefore,
                            estimatedRetainedMemoryBytes)
                        : "unavailable-repetitions") + " "
                    + $"gen0Collections={GC.CollectionCount(0) - gen0Before} "
                    + $"gen1Collections={GC.CollectionCount(1) - gen1Before} "
                    + $"gen2Collections={GC.CollectionCount(2) - gen2Before} "
                    + "allocationBytes=unavailable-net48";
                return true;
            }
            catch (Exception ex)
            {
                report = "Benchmark failed closed: " + ex;
                return false;
            }
        }

        private static void LoadBenchmarkInput(
            string caseDirectory,
            out AccessPathRequest request,
            out byte[] expected)
        {
            string directory = Path.GetFullPath(caseDirectory);
            string dataPath = Path.Combine(directory, "case.bin.gz");
            byte[] payload;
            using (var input = File.OpenRead(dataPath))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                if (output.Length > MaxPayloadBytes)
                    throw new InvalidDataException(
                        "Replay payload exceeds its size limit.");
                payload = output.ToArray();
            }
            string manifest = File.ReadAllText(
                Path.Combine(directory, "manifest.json"), Encoding.UTF8);
            RequireManifest(manifest, "payloadSha256",
                AccessSearchReplayRecorder.Sha256(payload));
            using (var stream = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadString() != "ATD_ACCESS_REPLAY"
                    || reader.ReadInt32() != 1)
                    throw new InvalidDataException("Replay header mismatch.");
                int requestLength = ReadLength(reader, 768 * 1024 * 1024);
                byte[] requestBytes = reader.ReadBytes(requestLength);
                if (requestBytes.Length != requestLength)
                    throw new EndOfStreamException("Truncated replay request.");
                request = (AccessPathRequest)AccessReplayGraphCodec.Deserialize(
                    requestBytes, typeof(AccessPathRequest));
                int expectedLength = ReadLength(reader, 256 * 1024 * 1024);
                expected = reader.ReadBytes(expectedLength);
                if (expected.Length != expectedLength
                    || stream.Position != stream.Length)
                    throw new InvalidDataException(
                        "Truncated or trailing replay data.");
                expected = AccessSearchReplayCanonical.Normalize(expected);
            }
        }

        private static double Median(List<double> values)
        {
            double[] ordered = values.OrderBy(value => value).ToArray();
            int middle = ordered.Length / 2;
            return (ordered.Length & 1) == 1
                ? ordered[middle]
                : (ordered[middle - 1] + ordered[middle]) / 2d;
        }

        internal static bool TryBenchmarkCaseCodec(
            string caseDirectory, out string report)
        {
            if (Mining.MiningReplayFacade.IsMiningCase(caseDirectory))
                return Mining.MiningReplayFacade.BenchmarkCodec(caseDirectory, out report);
            try
            {
                string dataPath = Path.Combine(
                    Path.GetFullPath(caseDirectory), "case.bin.gz");
                Stopwatch timer = Stopwatch.StartNew();
                byte[] payload;
                using (var input = File.OpenRead(dataPath))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    payload = output.ToArray();
                }
                double decompressMs = timer.Elapsed.TotalMilliseconds;
                byte[] requestBytes;
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    if (reader.ReadString() != "ATD_ACCESS_REPLAY"
                        || reader.ReadInt32() != 1)
                        throw new InvalidDataException("Replay header mismatch.");
                    int length = ReadLength(reader, 768 * 1024 * 1024);
                    requestBytes = reader.ReadBytes(length);
                    if (requestBytes.Length != length)
                        throw new EndOfStreamException("Truncated replay request.");
                }
                timer.Restart();
                object request = AccessReplayGraphCodec.Deserialize(
                    requestBytes, typeof(AccessPathRequest));
                double deserializeMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                long progressCompleted = 0L;
                long progressTotal = 0L;
                byte[] reencoded = AccessReplayGraphCodec.Serialize(
                    request, CancellationToken.None, (completed, total) =>
                    {
                        progressCompleted = completed;
                        progressTotal = total;
                    });
                double serializeMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                int cancelledAtPercent = 0;
                bool cancellationObserved = false;
                using (var cancellation = new CancellationTokenSource())
                {
                    try
                    {
                        AccessReplayGraphCodec.Serialize(
                            request, cancellation.Token, (completed, total) =>
                            {
                                cancelledAtPercent = (int)(completed * 100L
                                    / Math.Max(1L, total));
                                if (cancelledAtPercent >= 10)
                                    cancellation.Cancel();
                            });
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationObserved = true;
                    }
                }
                double cancelMs = timer.Elapsed.TotalMilliseconds;
                if (!cancellationObserved)
                    throw new InvalidOperationException(
                        "Progress-aware codec did not acknowledge cancellation.");
                timer.Restart();
                using (var gzip = new GZipStream(
                    Stream.Null, CompressionLevel.Optimal, leaveOpen: true))
                    gzip.Write(reencoded, 0, reencoded.Length);
                double compressMs = timer.Elapsed.TotalMilliseconds;
                report = $"requestBytes={requestBytes.Length} "
                    + $"reencodedBytes={reencoded.Length} "
                    + $"decompressMs={decompressMs.ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"deserializeMs={deserializeMs.ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"serializeMs={serializeMs.ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"compressMs={compressMs.ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"progress={progressCompleted}/{progressTotal} "
                    + $"cancelledAtPercent={cancelledAtPercent} "
                    + $"cancelMs={cancelMs.ToString("0.###", CultureInfo.InvariantCulture)}";
                return true;
            }
            catch (Exception ex)
            {
                report = "Codec benchmark failed: " + ex;
                return false;
            }
        }

        private static int ReadLength(BinaryReader reader, int maximum)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > maximum)
                throw new InvalidDataException("Replay section length is invalid.");
            return length;
        }

        internal static void RequireManifest(
            string manifest, string key, string expected)
        {
            Match match = Regex.Match(manifest,
                "\\\"" + Regex.Escape(key)
                + "\\\"\\s*:\\s*\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"");
            if (!match.Success)
                throw new InvalidDataException(
                    "Replay manifest field is missing: " + key);
            string actual = Regex.Unescape(match.Groups["value"].Value);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Replay manifest mismatch: " + key);
        }

        private static string ManifestString(string manifest, string key)
        {
            Match match = Regex.Match(manifest,
                "\\\"" + Regex.Escape(key)
                + "\\\"\\s*:\\s*\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"");
            if (!match.Success)
                throw new InvalidDataException(
                    "Replay manifest field is missing: " + key);
            return Regex.Unescape(match.Groups["value"].Value);
        }

        private static string ManifestScalar(string manifest, string key)
        {
            Match match = Regex.Match(manifest,
                "\\\"" + Regex.Escape(key)
                + "\\\"\\s*:\\s*(?<value>[-+0-9.eE]+)");
            if (!match.Success)
                throw new InvalidDataException(
                    "Replay manifest scalar is missing: " + key);
            return match.Groups["value"].Value;
        }

        private static string? TryManifestString(
            string manifest, string key)
        {
            Match match = Regex.Match(manifest,
                "\\\"" + Regex.Escape(key)
                + "\\\"\\s*:\\s*\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"");
            return match.Success
                ? Regex.Unescape(match.Groups["value"].Value)
                : null;
        }

        private static bool TryManifestScalar(
            string manifest, string key, out long value)
        {
            value = 0L;
            Match match = Regex.Match(manifest,
                "\\\"" + Regex.Escape(key)
                + "\\\"\\s*:\\s*(?<value>[-+0-9.eE]+)");
            return match.Success
                && long.TryParse(
                    match.Groups["value"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value);
        }

        private static string FormatRatio(long numerator, long denominator)
            => numerator < 0L || denominator <= 0L
                ? "unavailable"
                : (numerator / (double)denominator).ToString(
                    "0.###", CultureInfo.InvariantCulture);
    }

    internal static class AccessSearchReplayCanonical
    {
        internal sealed class CanonicalRecord
        {
            internal bool Success;
            internal string FailureReason = string.Empty;
            internal Tile2i StartOrigin;
            internal AccessReachedGoalKind ReachedGoalKind;
            internal int CostBits;
            internal AccessSearchNode[] Path = Array.Empty<AccessSearchNode>();
            internal AccessV2RouteData? V2Route;
            internal AccessDesignationPlan Plan = null!;
        }

        internal static byte[] Serialize(
            AccessSearchResult result, AccessDesignationPlan plan)
            => SerializeRecord(new CanonicalRecord
            {
                Success = result.Success,
                FailureReason = result.FailureReason ?? string.Empty,
                StartOrigin = result.StartOrigin,
                ReachedGoalKind = result.ReachedGoalKind,
                CostBits = BitConverter.ToInt32(BitConverter.GetBytes(result.Cost), 0),
                Path = result.Path.ToArray(),
                V2Route = NormalizeV2Route(result.V2Route),
                Plan = plan,
            });

        internal static byte[] Normalize(byte[] canonicalBytes)
        {
            var record = (CanonicalRecord)AccessReplayGraphCodec.Deserialize(
                canonicalBytes, typeof(CanonicalRecord));
            record.V2Route = NormalizeV2Route(record.V2Route);
            return SerializeRecord(record);
        }

        private static byte[] SerializeRecord(CanonicalRecord record)
            => AccessReplayGraphCodec.Serialize(record);

        private static AccessV2RouteData? NormalizeV2Route(
            AccessV2RouteData? route)
        {
            if (route == null)
                return null;
            AccessV2RouteStep[] detachedSteps = route.RouteSteps
                .Select(step => (AccessV2RouteStep)
                    AccessReplayGraphCodec.Deserialize(
                        AccessReplayGraphCodec.Serialize(step),
                        typeof(AccessV2RouteStep)))
                .ToArray();
            return new AccessV2RouteData(
                route.States,
                route.GeneratedProfiles,
                route.Handoff,
                route.GroundPath,
                detachedSteps,
                route.VehicleWidth,
                route.TerminalGoalCenters);
        }

        internal static string DescribeDifference(
            byte[] expectedBytes,
            AccessSearchResult actualResult,
            AccessDesignationPlan actualPlan)
        {
            var expected = (CanonicalRecord)AccessReplayGraphCodec.Deserialize(
                Normalize(expectedBytes), typeof(CanonicalRecord));
            var differences = new List<string>();
            if (expected.Success != actualResult.Success)
                differences.Add("terminal");
            if (!string.Equals(
                    expected.FailureReason,
                    actualResult.FailureReason ?? string.Empty,
                    StringComparison.Ordinal))
                differences.Add("reason");
            if (expected.StartOrigin != actualResult.StartOrigin)
                differences.Add("start");
            if (expected.ReachedGoalKind != actualResult.ReachedGoalKind)
                differences.Add("provider");
            int actualCostBits = BitConverter.ToInt32(
                BitConverter.GetBytes(actualResult.Cost), 0);
            if (expected.CostBits != actualCostBits)
                differences.Add("costBits");
            if (!AccessReplayGraphCodec.Serialize(expected.Path)
                    .SequenceEqual(AccessReplayGraphCodec.Serialize(
                        actualResult.Path.ToArray())))
                differences.Add("orderedPath");
            if (!AccessReplayGraphCodec.Serialize(expected.V2Route)
                    .SequenceEqual(AccessReplayGraphCodec.Serialize(
                        actualResult.V2Route)))
                differences.Add(DescribeV2Difference(
                    expected.V2Route, actualResult.V2Route));
            if (!AccessReplayGraphCodec.Serialize(expected.Plan)
                    .SequenceEqual(AccessReplayGraphCodec.Serialize(actualPlan)))
                differences.Add("materializedPlan");
            return differences.Count == 0
                ? "canonicalEncoding"
                : string.Join(",", differences);
        }

        private static string DescribeV2Difference(
            AccessV2RouteData? expected,
            AccessV2RouteData? actual)
        {
            if (expected == null || actual == null)
                return $"v2Route(null:{expected == null}/{actual == null})";

            var parts = new List<string>();
            AddComponentDifference(parts, "states",
                expected.States, actual.States);
            AddComponentDifference(parts, "profiles",
                expected.GeneratedProfiles, actual.GeneratedProfiles);
            AddComponentDifference(parts, "handoff",
                expected.Handoff, actual.Handoff);
            AddComponentDifference(parts, "ground",
                expected.GroundPath, actual.GroundPath);
            AddRouteStepDifference(
                parts, expected.RouteSteps, actual.RouteSteps);
            AddComponentDifference(parts, "goals",
                expected.TerminalGoalCenters, actual.TerminalGoalCenters);
            if (expected.VehicleWidth != actual.VehicleWidth)
                parts.Add($"width:{expected.VehicleWidth}/{actual.VehicleWidth}");
            return parts.Count == 0
                ? "v2Route(encoding)"
                : "v2Route(" + string.Join(";", parts) + ")";
        }

        private static void AddComponentDifference(
            ICollection<string> parts,
            string name,
            object? expected,
            object? actual)
        {
            byte[] expectedBytes = AccessReplayGraphCodec.Serialize(expected);
            byte[] actualBytes = AccessReplayGraphCodec.Serialize(actual);
            if (!expectedBytes.SequenceEqual(actualBytes))
            {
                parts.Add(name + ":"
                    + AccessSearchReplayRecorder.Sha256(expectedBytes)
                        .Substring(0, 12)
                    + "/"
                    + AccessSearchReplayRecorder.Sha256(actualBytes)
                        .Substring(0, 12));
            }
        }

        private static void AddRouteStepDifference(
            ICollection<string> parts,
            IReadOnlyList<AccessV2RouteStep> expected,
            IReadOnlyList<AccessV2RouteStep> actual)
        {
            byte[] expectedBytes = AccessReplayGraphCodec.Serialize(expected);
            byte[] actualBytes = AccessReplayGraphCodec.Serialize(actual);
            if (expectedBytes.SequenceEqual(actualBytes))
                return;

            int sharedCount = Math.Min(expected.Count, actual.Count);
            for (int index = 0; index < sharedCount; index++)
            {
                var fields = new List<string>();
                AddRouteStepFieldDifference(
                    fields, "state", expected[index].State, actual[index].State);
                AddRouteStepFieldDifference(
                    fields, "transition", expected[index].Transition,
                    actual[index].Transition);
                AddRouteStepFieldDifference(
                    fields, "handoff", expected[index].Handoff,
                    actual[index].Handoff);
                AddRouteStepFieldDifference(
                    fields, "ground", expected[index].GroundCenter,
                    actual[index].GroundCenter);
                if (expected[index].IsProjectedGroundEntry
                    != actual[index].IsProjectedGroundEntry)
                    fields.Add("projected");
                if (fields.Count == 0)
                    continue;
                parts.Add("steps:"
                    + AccessSearchReplayRecorder.Sha256(expectedBytes)
                        .Substring(0, 12)
                    + "/"
                    + AccessSearchReplayRecorder.Sha256(actualBytes)
                        .Substring(0, 12)
                    + $"[count:{expected.Count}/{actual.Count};first:{index};"
                    + string.Join("+", fields) + "]");
                return;
            }

            parts.Add("steps:"
                + AccessSearchReplayRecorder.Sha256(expectedBytes)
                    .Substring(0, 12)
                + "/"
                + AccessSearchReplayRecorder.Sha256(actualBytes)
                    .Substring(0, 12)
                + $"[count:{expected.Count}/{actual.Count};first:none]");
        }

        private static void AddRouteStepFieldDifference(
            ICollection<string> fields,
            string name,
            object? expected,
            object? actual)
        {
            if (!AccessReplayGraphCodec.Serialize(expected).SequenceEqual(
                    AccessReplayGraphCodec.Serialize(actual)))
                fields.Add(name);
        }
    }

    internal static class AccessSearchReplayFixtures
    {
        internal static bool ValidateAll(out string failure)
        {
            try
            {
                var heights = new Dictionary<Tile2i, int>();
                var ground = new List<Tile2i>();
                for (int y = 0; y <= 8; y++)
                    for (int x = 0; x <= 8; x++)
                    {
                        var tile = new Tile2i(x, y);
                        heights[tile] = 0;
                        ground.Add(tile);
                    }
                Tile2i start = new Tile2i(1, 1);
                Tile2i goal = new Tile2i(7, 7);
                var snapshot = new AccessSearchSnapshot(
                    new Tile2i(0, 0), new Tile2i(8, 8), new Tile2i(4, 4),
                    -2, 2, true, false, false, 1f, 1f,
                    heights,
                    new Dictionary<Tile2i, int>(),
                    new Dictionary<Tile2i, AccessHeightProfile>(),
                    Array.Empty<Tile2i>(), ground, new[] { goal },
                    Array.Empty<Tile2i>(), Array.Empty<Tile2i>(),
                    Array.Empty<AccessDurabilityCorner>());
                var request = new AccessPathRequest(
                    "replay-codec-fixture", snapshot,
                    new AccessPathEndpoint(
                        AccessPathEndpointKind.GroundTiles, new[] { start }),
                    new AccessPathEndpoint(
                        AccessPathEndpointKind.GroundTiles, new[] { goal }),
                    1, AccessPathIntent.InspectExistingRoute);
                byte[] encoded = AccessReplayGraphCodec.Serialize(request);
                long lastCompleted = 0L;
                long reportedTotal = 0L;
                byte[] progressEncoded = AccessReplayGraphCodec.Serialize(
                    request, CancellationToken.None, (completed, total) =>
                    {
                        if (completed < lastCompleted || completed > total)
                            throw new InvalidOperationException(
                                "Replay encoding progress was not monotonic.");
                        lastCompleted = completed;
                        reportedTotal = total;
                    });
                if (!encoded.SequenceEqual(progressEncoded)
                    || lastCompleted <= 0L
                    || lastCompleted != reportedTotal)
                {
                    failure = "Progress-aware replay encoding changed its output or did not complete at 100%.";
                    return false;
                }
                var restored = (AccessPathRequest)AccessReplayGraphCodec.Deserialize(
                    encoded, typeof(AccessPathRequest));
                AccessSearchResult expectedResult = AccessPathSearch.FindPath(
                    request, new AccessSearchWorkspace(snapshot));
                var restoredWorkspace = new AccessSearchWorkspace(restored.Snapshot);
                AccessSearchResult actualResult = AccessPathSearch.FindPath(
                    restored, restoredWorkspace);
                AccessDesignationPlan expectedPlan = expectedResult.Success
                    ? AccessPathMaterializer.Materialize(
                        new AccessSearchWorkspace(snapshot), expectedResult)
                    : AccessDesignationPlan.Invalid(
                        expectedResult.FailureReason, expectedResult.StartOrigin);
                AccessDesignationPlan actualPlan = actualResult.Success
                    ? AccessPathMaterializer.Materialize(
                        restoredWorkspace, actualResult)
                    : AccessDesignationPlan.Invalid(
                        actualResult.FailureReason, actualResult.StartOrigin);
                byte[] expected = AccessSearchReplayCanonical.Serialize(
                    expectedResult, expectedPlan);
                byte[] actual = AccessSearchReplayCanonical.Serialize(
                    actualResult, actualPlan);
                if (!expected.SequenceEqual(actual))
                {
                    failure = "Canonical outcome changed after request graph round trip.";
                    return false;
                }
                if (!ValidateRouteStepReferenceNormalization(out failure))
                    return false;
                using (var release = new ManualResetEventSlim(false))
                {
                    Stopwatch dispatchTimer = Stopwatch.StartNew();
                    AccessReplayCaptureOperation operation =
                        AccessReplayCaptureOperation.Start(_ =>
                        {
                            release.Wait();
                            return "fixture";
                        });
                    dispatchTimer.Stop();
                    if (dispatchTimer.ElapsedMilliseconds > 500
                        || operation.IsComplete)
                    {
                        failure = "Replay capture work did not dispatch asynchronously.";
                        release.Set();
                        return false;
                    }
                    release.Set();
                    if (!SpinWait.SpinUntil(
                            () => operation.IsComplete, 5000)
                        || operation.IsFaulted)
                    {
                        failure = "Background replay capture operation did not complete cleanly.";
                        return false;
                    }
                }
                using (var started = new ManualResetEventSlim(false))
                {
                    AccessReplayCaptureOperation cancellationOperation =
                        AccessReplayCaptureOperation.Start(operation =>
                        {
                            started.Set();
                            while (true)
                            {
                                operation.CancellationToken
                                    .ThrowIfCancellationRequested();
                                Thread.Yield();
                            }
                        });
                    if (!started.Wait(5000))
                    {
                        failure = "Cancellation fixture did not start.";
                        return false;
                    }
                    cancellationOperation.Cancel();
                    if (!SpinWait.SpinUntil(
                            () => cancellationOperation.IsComplete, 5000)
                        || !cancellationOperation.IsCanceled)
                    {
                        failure = "Background replay capture did not acknowledge cancellation.";
                        return false;
                    }
                }
                string publicationRoot = Path.Combine(
                    Path.GetTempPath(),
                    "atd-access-publication-fixture-"
                        + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(publicationRoot);
                try
                {
                    string rejectedStaging = Path.Combine(
                        publicationRoot, "rejected-staging");
                    string rejectedCompleted = Path.Combine(
                        publicationRoot, "rejected-completed");
                    AccessReplayCaptureOperation rejectedOperation =
                        AccessReplayCaptureOperation.Start(operation =>
                        {
                            Directory.CreateDirectory(rejectedStaging);
                            File.WriteAllText(
                                Path.Combine(rejectedStaging, "marker"), "fixture");
                            operation.SetPendingPublication(
                                rejectedStaging, rejectedCompleted, "fixture");
                            return "fixture";
                        });
                    string rejectionFailure = string.Empty;
                    if (!SpinWait.SpinUntil(
                            () => rejectedOperation.IsComplete, 5000)
                        || rejectedOperation.IsFaulted
                        || !rejectedOperation.TryFinalizePendingPublication(
                            publish: false, out rejectionFailure)
                        || Directory.Exists(rejectedStaging)
                        || Directory.Exists(rejectedCompleted))
                    {
                        failure = "Rejected replay publication was not removed: "
                            + rejectionFailure;
                        return false;
                    }

                    string acceptedStaging = Path.Combine(
                        publicationRoot, "accepted-staging");
                    string acceptedCompleted = Path.Combine(
                        publicationRoot, "accepted-completed");
                    AccessReplayCaptureOperation acceptedOperation =
                        AccessReplayCaptureOperation.Start(operation =>
                        {
                            Directory.CreateDirectory(acceptedStaging);
                            File.WriteAllText(
                                Path.Combine(acceptedStaging, "marker"), "fixture");
                            operation.SetPendingPublication(
                                acceptedStaging, acceptedCompleted, "fixture");
                            return "fixture";
                        });
                    string acceptanceFailure = string.Empty;
                    if (!SpinWait.SpinUntil(
                            () => acceptedOperation.IsComplete, 5000)
                        || acceptedOperation.IsFaulted
                        || !acceptedOperation.TryFinalizePendingPublication(
                            publish: true, out acceptanceFailure)
                        || Directory.Exists(acceptedStaging)
                        || !File.Exists(Path.Combine(
                            acceptedCompleted, "marker")))
                    {
                        failure = "Accepted replay publication was not completed: "
                            + acceptanceFailure;
                        return false;
                    }
                }
                finally
                {
                    if (Directory.Exists(publicationRoot))
                        Directory.Delete(publicationRoot, recursive: true);
                }
#if !DEBUG
                string temporary = Path.Combine(Path.GetTempPath(),
                    "atd-access-replay-fixture-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary);
                try
                {
                    byte[] payload;
                    using (var payloadStream = new MemoryStream())
                    using (var payloadWriter = new BinaryWriter(
                        payloadStream, Encoding.UTF8, true))
                    {
                        payloadWriter.Write("ATD_ACCESS_REPLAY");
                        payloadWriter.Write(1);
                        payloadWriter.Write(encoded.Length);
                        payloadWriter.Write(encoded);
                        payloadWriter.Write(expected.Length);
                        payloadWriter.Write(expected);
                        payloadWriter.Flush();
                        payload = payloadStream.ToArray();
                    }
                    using (var file = File.Create(Path.Combine(
                        temporary, "case.bin.gz")))
                    using (var gzip = new GZipStream(
                        file, CompressionLevel.Optimal))
                        gzip.Write(payload, 0, payload.Length);
                    Assembly assembly = typeof(AccessSearchReplayFixtures).Assembly;
                    string manifest = "{\n"
                        + "  \"caseName\": \"codec-fixture\",\n"
                        + "  \"semanticPolicy\": \"access-search-v1\",\n"
                        + "  \"policyFingerprint\": " + request.Snapshot.Policy.SemanticFingerprint.ToString(CultureInfo.InvariantCulture) + ",\n"
                        + "  \"payloadSha256\": \"" + AccessSearchReplayRecorder.Sha256(payload) + "\",\n"
                        + "  \"requestSha256\": \"" + AccessSearchReplayRecorder.Sha256(encoded) + "\",\n"
                        + "  \"canonicalSha256\": \"" + AccessSearchReplayRecorder.Sha256(expected) + "\",\n"
                        + "  \"atdAssemblySha256\": \"" + AccessSearchReplayRecorder.Sha256(File.ReadAllBytes(assembly.Location)) + "\",\n"
                        + "  \"buildConfiguration\": \"Release\",\n"
                        + "  \"gameAssemblyFingerprint\": \"" + AccessSearchReplayRecorder.GetGameAssemblyFingerprint() + "\",\n"
                        + "  \"preparationMilliseconds\": 0,\n"
                        + "  \"searchMilliseconds\": 0,\n"
                        + "  \"materializationMilliseconds\": 0\n"
                        + "}\n";
                    File.WriteAllText(Path.Combine(temporary, "manifest.json"),
                        manifest, new UTF8Encoding(false));
                    if (!AccessSearchReplayFacade.TryReplayCase(
                            temporary, out string replayFailure))
                    {
                        failure = "End-to-end replay fixture failed: "
                            + replayFailure;
                        return false;
                    }
                }
                finally
                {
                    if (Directory.Exists(temporary))
                        Directory.Delete(temporary, recursive: true);
                }
#endif
                failure = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
                return false;
            }
        }

        private static bool ValidateRouteStepReferenceNormalization(
            out string failure)
        {
            failure = string.Empty;
            if (!AccessHeightProfile.TryForMode(
                    AccessSearchMode.Flat, 0,
                    out AccessHeightProfile flat)
                || !AccessV2BandProfile.TryCreateEnabled(
                    AccessV2TravelAxis.X, flat, flat,
                    out AccessV2BandProfile band, out _))
            {
                failure = "Route-step normalization fixture could not create a flat V2 band.";
                return false;
            }

            var state = new AccessV2BandState(
                new Tile2i(0, 0), band, new Tile2i(4, 0));
            AccessV2Transition CreateTransition()
                => new AccessV2Transition(
                    AccessV2TransitionKind.Straight,
                    state,
                    Array.Empty<AccessV2OriginProfile>(),
                    Array.Empty<Tile2i>());
            AccessV2Transition sharedTransition = CreateTransition();
            AccessV2RouteData CreateRoute(bool shareTransition)
                => new AccessV2RouteData(
                    new[] { state },
                    new Dictionary<Tile2i, AccessHeightProfile>(),
                    null,
                    Array.Empty<Tile2i>(),
                    new[]
                    {
                        new AccessV2RouteStep(
                            state,
                            shareTransition
                                ? sharedTransition
                                : CreateTransition(),
                            null, null),
                        new AccessV2RouteStep(
                            state,
                            shareTransition
                                ? sharedTransition
                                : CreateTransition(),
                            null, null),
                    });
            AccessSearchReplayCanonical.CanonicalRecord CreateRecord(
                bool shareTransition)
                => new AccessSearchReplayCanonical.CanonicalRecord
                {
                    Success = true,
                    V2Route = CreateRoute(shareTransition),
                    Plan = AccessDesignationPlan.Invalid(
                        "fixture", new Tile2i(0, 0)),
                };

            byte[] shared = AccessReplayGraphCodec.Serialize(
                CreateRecord(shareTransition: true));
            byte[] detached = AccessReplayGraphCodec.Serialize(
                CreateRecord(shareTransition: false));
            if (shared.SequenceEqual(detached))
            {
                failure = "Route-step normalization fixture did not create distinct reference topologies.";
                return false;
            }
            byte[] normalizedShared =
                AccessSearchReplayCanonical.Normalize(shared);
            byte[] normalizedDetached =
                AccessSearchReplayCanonical.Normalize(detached);
            if (!normalizedShared.SequenceEqual(normalizedDetached)
                || !normalizedShared.SequenceEqual(
                    AccessSearchReplayCanonical.Normalize(normalizedShared)))
            {
                failure = "Canonical route-step encoding retained non-semantic reference topology.";
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Bounded, version-local graph codec for immutable owned search values.
    /// It intentionally refuses runtime types outside ATD, Mafi and core BCL.
    /// </summary>
    internal static class AccessReplayGraphCodec
    {
        private const int Version = 1;
        private const int MaxObjects = 4_000_000;
        private const int MaxCollection = 32_000_000;
        private const int MaxStringBytes = 16 * 1024 * 1024;
        private enum Tag : byte { Null, Reference, String, Primitive, Enum, Object, Array, List, Dictionary, Set }

        internal static byte[] Serialize(object? value)
            => Serialize(value, CancellationToken.None, progress: null);

        internal static byte[] Serialize(
            object? value,
            CancellationToken cancellationToken,
            Action<long, long>? progress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long totalWork = progress == null
                ? 0L
                : new GraphCounter(cancellationToken).Count(value);
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("ATD_GRAPH");
                writer.Write(Version);
                new GraphWriter(
                    writer, cancellationToken, totalWork, progress).Write(value);
                cancellationToken.ThrowIfCancellationRequested();
                writer.Flush();
                progress?.Invoke(totalWork, totalWork);
                return stream.ToArray();
            }
        }

        internal static object Deserialize(byte[] bytes, Type expectedType)
        {
            using (var stream = new MemoryStream(bytes, writable: false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadString() != "ATD_GRAPH" || reader.ReadInt32() != Version)
                    throw new InvalidDataException("Graph codec header mismatch.");
                object value = new GraphReader(reader).Read()
                    ?? throw new InvalidDataException("Graph root is null.");
                if (!expectedType.IsInstanceOfType(value) || stream.Position != stream.Length)
                    throw new InvalidDataException("Graph root type or length mismatch.");
                return value;
            }
        }

        private sealed class GraphWriter
        {
            private readonly BinaryWriter m_writer;
            private readonly CancellationToken m_cancellationToken;
            private readonly long m_totalWork;
            private readonly Action<long, long>? m_progress;
            private long m_completedWork;
            private readonly Dictionary<object, int> m_ids =
                new Dictionary<object, int>(ReferenceComparer.Instance);
            internal GraphWriter(
                BinaryWriter writer,
                CancellationToken cancellationToken,
                long totalWork,
                Action<long, long>? progress)
            {
                m_writer = writer;
                m_cancellationToken = cancellationToken;
                m_totalWork = totalWork;
                m_progress = progress;
            }

            internal void Write(object? value)
            {
                Tick();
                if (value == null) { m_writer.Write((byte)Tag.Null); return; }
                Type type = value.GetType();
                ValidateType(type);
                if (type == typeof(string))
                {
                    m_writer.Write((byte)Tag.String);
                    WriteString((string)value);
                    return;
                }
                if (IsPrimitive(type))
                {
                    m_writer.Write((byte)Tag.Primitive); WriteType(type);
                    WritePrimitive(value, type); return;
                }
                if (type.IsEnum)
                {
                    m_writer.Write((byte)Tag.Enum); WriteType(type);
                    m_writer.Write(Convert.ToInt64(value, CultureInfo.InvariantCulture)); return;
                }
                int id = 0;
                if (!type.IsValueType)
                {
                    if (m_ids.TryGetValue(value, out int existing))
                    { m_writer.Write((byte)Tag.Reference); m_writer.Write(existing); return; }
                    id = m_ids.Count + 1;
                    if (id > MaxObjects) throw new InvalidDataException("Graph object limit exceeded.");
                    m_ids.Add(value, id);
                }
                if (type.IsArray) { WriteArray((Array)value, type, id); return; }
                if (IsGeneric(type, typeof(List<>))) { WriteList((IList)value, type, id); return; }
                if (IsGeneric(type, typeof(Dictionary<,>))) { WriteDictionary((IDictionary)value, type, id); return; }
                if (IsGeneric(type, typeof(HashSet<>))) { WriteSet((IEnumerable)value, type, id); return; }
                m_writer.Write((byte)Tag.Object); WriteType(type); m_writer.Write(id);
                FieldInfo[] fields = GetFields(type);
                m_writer.Write(fields.Length);
                foreach (FieldInfo field in fields)
                {
                    WriteString(field.DeclaringType!.FullName + "|" + field.Name);
                    Write(field.GetValue(value));
                }
            }

            private void WriteArray(Array array, Type type, int id)
            {
                m_writer.Write((byte)Tag.Array); WriteType(type); m_writer.Write(id);
                m_writer.Write(array.Rank);
                for (int i = 0; i < array.Rank; i++) m_writer.Write(array.GetLength(i));
                foreach (object? item in array) Write(item);
            }

            private void WriteList(IList list, Type type, int id)
            {
                CheckCount(list.Count); m_writer.Write((byte)Tag.List); WriteType(type);
                m_writer.Write(id); m_writer.Write(list.Count);
                foreach (object? item in list) Write(item);
            }

            private void WriteDictionary(IDictionary dictionary, Type type, int id)
            {
                CheckCount(dictionary.Count); m_writer.Write((byte)Tag.Dictionary); WriteType(type);
                m_writer.Write(id); m_writer.Write(dictionary.Count);
                var keys = new List<object?>();
                foreach (object? key in dictionary.Keys) keys.Add(key);
                m_cancellationToken.ThrowIfCancellationRequested();
                keys.Sort((left, right) => string.CompareOrdinal(
                    StableSortKey(left), StableSortKey(right)));
                m_cancellationToken.ThrowIfCancellationRequested();
                foreach (object? key in keys) { Write(key); Write(dictionary[key!]); }
            }

            private void WriteSet(IEnumerable set, Type type, int id)
            {
                var values = new List<object?>(); foreach (object? value in set) values.Add(value);
                m_cancellationToken.ThrowIfCancellationRequested();
                values.Sort((left, right) => string.CompareOrdinal(
                    StableSortKey(left), StableSortKey(right)));
                m_cancellationToken.ThrowIfCancellationRequested();
                CheckCount(values.Count); m_writer.Write((byte)Tag.Set); WriteType(type);
                m_writer.Write(id); m_writer.Write(values.Count); foreach (object? value in values) Write(value);
            }

            private void WriteType(Type type) => WriteString(type.AssemblyQualifiedName
                ?? throw new InvalidDataException("Type name unavailable."));
            private void WriteString(string value)
            {
                if (Encoding.UTF8.GetByteCount(value) > MaxStringBytes)
                    throw new InvalidDataException("String limit exceeded.");
                m_writer.Write(value);
            }
            private static void CheckCount(int count)
            { if (count < 0 || count > MaxCollection) throw new InvalidDataException("Collection limit exceeded."); }
            private void WritePrimitive(object value, Type type)
            {
                if (type == typeof(bool)) m_writer.Write((bool)value);
                else if (type == typeof(byte)) m_writer.Write((byte)value);
                else if (type == typeof(sbyte)) m_writer.Write((sbyte)value);
                else if (type == typeof(short)) m_writer.Write((short)value);
                else if (type == typeof(ushort)) m_writer.Write((ushort)value);
                else if (type == typeof(int)) m_writer.Write((int)value);
                else if (type == typeof(uint)) m_writer.Write((uint)value);
                else if (type == typeof(long)) m_writer.Write((long)value);
                else if (type == typeof(ulong)) m_writer.Write((ulong)value);
                else if (type == typeof(float)) m_writer.Write(BitConverter.ToInt32(BitConverter.GetBytes((float)value), 0));
                else if (type == typeof(double)) m_writer.Write(BitConverter.DoubleToInt64Bits((double)value));
                else if (type == typeof(char)) m_writer.Write((char)value);
                else throw new InvalidDataException("Unsupported primitive " + type.FullName);
            }

            private void Tick()
            {
                m_completedWork++;
                if ((m_completedWork & 4095L) != 0L)
                    return;
                m_cancellationToken.ThrowIfCancellationRequested();
                m_progress?.Invoke(m_completedWork, m_totalWork);
            }
        }

        private sealed class GraphCounter
        {
            private readonly CancellationToken m_cancellationToken;
            private readonly HashSet<object> m_seen =
                new HashSet<object>(ReferenceComparer.Instance);
            private long m_work;

            internal GraphCounter(CancellationToken cancellationToken)
                => m_cancellationToken = cancellationToken;

            internal long Count(object? value)
            {
                Visit(value);
                return Math.Max(1L, m_work);
            }

            private void Visit(object? value)
            {
                m_work++;
                if ((m_work & 4095L) == 0L)
                    m_cancellationToken.ThrowIfCancellationRequested();
                if (value == null) return;
                Type type = value.GetType();
                ValidateType(type);
                if (type == typeof(string) || IsPrimitive(type) || type.IsEnum)
                    return;
                if (!type.IsValueType && !m_seen.Add(value))
                    return;
                if (type.IsArray)
                {
                    foreach (object? item in (Array)value) Visit(item);
                    return;
                }
                if (IsGeneric(type, typeof(List<>)))
                {
                    foreach (object? item in (IList)value) Visit(item);
                    return;
                }
                if (IsGeneric(type, typeof(Dictionary<,>)))
                {
                    IDictionary dictionary = (IDictionary)value;
                    foreach (object? key in dictionary.Keys)
                    {
                        Visit(key);
                        Visit(dictionary[key!]);
                    }
                    return;
                }
                if (IsGeneric(type, typeof(HashSet<>)))
                {
                    foreach (object? item in (IEnumerable)value) Visit(item);
                    return;
                }
                foreach (FieldInfo field in GetFields(type))
                    Visit(field.GetValue(value));
            }
        }

        private sealed class GraphReader
        {
            private readonly BinaryReader m_reader;
            private readonly Dictionary<int, object> m_objects = new Dictionary<int, object>();
            internal GraphReader(BinaryReader reader) { m_reader = reader; }
            internal object? Read()
            {
                Tag tag = (Tag)m_reader.ReadByte();
                switch (tag)
                {
                    case Tag.Null: return null;
                    case Tag.Reference:
                        int reference = m_reader.ReadInt32();
                        if (!m_objects.TryGetValue(reference, out object found))
                            throw new InvalidDataException("Invalid graph reference.");
                        return found;
                    case Tag.String: return ReadString();
                    case Tag.Primitive: { Type t = ReadType(); return ReadPrimitive(t); }
                    case Tag.Enum: { Type t = ReadType(); return Enum.ToObject(t, m_reader.ReadInt64()); }
                    case Tag.Array: return ReadArray();
                    case Tag.List: return ReadList();
                    case Tag.Dictionary: return ReadDictionary();
                    case Tag.Set: return ReadSet();
                    case Tag.Object: return ReadObject();
                    default: throw new InvalidDataException("Unknown graph tag.");
                }
            }

            private object ReadArray()
            {
                Type type = ReadType(); int id = m_reader.ReadInt32(); int rank = ReadCount(32);
                var lengths = new int[rank]; for (int i = 0; i < rank; i++) lengths[i] = ReadCount(MaxCollection);
                Array array = Array.CreateInstance(type.GetElementType()!, lengths); Add(id, array);
                var indices = new int[rank]; FillArray(array, indices, 0); return array;
            }
            private void FillArray(Array array, int[] indices, int dimension)
            {
                for (int i = 0; i < array.GetLength(dimension); i++)
                {
                    indices[dimension] = i;
                    if (dimension + 1 == array.Rank) array.SetValue(Read(), indices);
                    else FillArray(array, indices, dimension + 1);
                }
            }
            private object ReadList()
            {
                Type type = ReadType(); int id = m_reader.ReadInt32(); int count = ReadCount(MaxCollection);
                var list = (IList)Activator.CreateInstance(type)!; Add(id, list);
                for (int i = 0; i < count; i++) list.Add(Read()); return list;
            }
            private object ReadDictionary()
            {
                Type type = ReadType(); int id = m_reader.ReadInt32(); int count = ReadCount(MaxCollection);
                var dictionary = (IDictionary)Activator.CreateInstance(type)!; Add(id, dictionary);
                for (int i = 0; i < count; i++) dictionary.Add(Read(), Read()); return dictionary;
            }
            private object ReadSet()
            {
                Type type = ReadType(); int id = m_reader.ReadInt32(); int count = ReadCount(MaxCollection);
                object set = Activator.CreateInstance(type)!; Add(id, set);
                MethodInfo add = type.GetMethod("Add")!;
                for (int i = 0; i < count; i++) add.Invoke(set, new[] { Read() }); return set;
            }
            private object ReadObject()
            {
                Type type = ReadType(); int id = m_reader.ReadInt32();
                object value = type.IsValueType
                    ? Activator.CreateInstance(type)!
                    : FormatterServices.GetUninitializedObject(type);
                if (!type.IsValueType) Add(id, value);
                FieldInfo[] fields = GetFields(type); int count = ReadCount(10000);
                bool legacyMiningPolicy = type.FullName ==
                        "AutoTerrainDesignations.Mining.MiningPolicy"
                    && count == fields.Length - 1;
                if (count != fields.Length && !legacyMiningPolicy)
                    throw new InvalidDataException("Field layout mismatch for " + type.FullName);
                int fieldIndex = 0;
                for (int i = 0; i < count; i++)
                {
                    if (legacyMiningPolicy && fieldIndex < fields.Length
                        && fields[fieldIndex].Name == "FilterOreSpikes")
                        fieldIndex++;
                    if (fieldIndex >= fields.Length)
                        throw new InvalidDataException("Field layout mismatch for " + type.FullName);
                    FieldInfo field = fields[fieldIndex++];
                    string key = ReadString();
                    string expected = field.DeclaringType!.FullName + "|" + field.Name;
                    if (key != expected) throw new InvalidDataException("Field layout mismatch for " + type.FullName);
                    field.SetValue(value, Read());
                }
                if (legacyMiningPolicy && fieldIndex < fields.Length
                    && fields[fieldIndex].Name == "FilterOreSpikes")
                    fieldIndex++;
                if (fieldIndex != fields.Length)
                    throw new InvalidDataException("Field layout mismatch for " + type.FullName);
                return value;
            }
            private void Add(int id, object value)
            {
                if (id <= 0 || id > MaxObjects || m_objects.ContainsKey(id))
                    throw new InvalidDataException("Invalid graph object id.");
                m_objects.Add(id, value);
            }
            private Type ReadType()
            {
                string name = ReadString();
                Type? type = Type.GetType(name, throwOnError: false);
                if (type == null)
                {
                    string simple = name.Split(',')[0];
                    type = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(assembly => assembly.GetType(simple, false))
                        .FirstOrDefault(candidate => candidate != null);
                }
                if (type == null) throw new InvalidDataException("Unknown graph type " + name);
                ValidateType(type); return type;
            }
            private string ReadString()
            {
                string value = m_reader.ReadString();
                if (Encoding.UTF8.GetByteCount(value) > MaxStringBytes)
                    throw new InvalidDataException("String limit exceeded.");
                return value;
            }
            private int ReadCount(int maximum)
            {
                int count = m_reader.ReadInt32();
                if (count < 0 || count > maximum) throw new InvalidDataException("Count limit exceeded.");
                return count;
            }
            private object ReadPrimitive(Type type)
            {
                if (type == typeof(bool)) return m_reader.ReadBoolean();
                if (type == typeof(byte)) return m_reader.ReadByte();
                if (type == typeof(sbyte)) return m_reader.ReadSByte();
                if (type == typeof(short)) return m_reader.ReadInt16();
                if (type == typeof(ushort)) return m_reader.ReadUInt16();
                if (type == typeof(int)) return m_reader.ReadInt32();
                if (type == typeof(uint)) return m_reader.ReadUInt32();
                if (type == typeof(long)) return m_reader.ReadInt64();
                if (type == typeof(ulong)) return m_reader.ReadUInt64();
                if (type == typeof(float)) return BitConverter.ToSingle(BitConverter.GetBytes(m_reader.ReadInt32()), 0);
                if (type == typeof(double)) return BitConverter.Int64BitsToDouble(m_reader.ReadInt64());
                if (type == typeof(char)) return m_reader.ReadChar();
                throw new InvalidDataException("Unsupported primitive " + type.FullName);
            }
        }

        private static bool IsPrimitive(Type type)
            => type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)
                || type == typeof(short) || type == typeof(ushort) || type == typeof(int)
                || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
                || type == typeof(float) || type == typeof(double) || type == typeof(char);
        private static bool IsGeneric(Type type, Type definition)
            => type.IsGenericType && type.GetGenericTypeDefinition() == definition;
        private static FieldInfo[] GetFields(Type type)
            => EnumerateTypes(type).SelectMany(item => item.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly))
                .Where(field => !field.IsNotSerialized)
                .OrderBy(field => field.DeclaringType!.FullName, StringComparer.Ordinal)
                .ThenBy(field => field.Name, StringComparer.Ordinal).ToArray();
        private static IEnumerable<Type> EnumerateTypes(Type type)
        {
            for (Type? current = type; current != null; current = current.BaseType)
                yield return current;
        }
        private static void ValidateType(Type type)
        {
            if (type.IsPointer || typeof(Delegate).IsAssignableFrom(type)
                || type == typeof(IntPtr) || type == typeof(UIntPtr))
                throw new InvalidDataException("Runtime type refused: " + type.FullName);
            string assembly = type.Assembly.GetName().Name ?? string.Empty;
            if (assembly != "AutoTerrainDesignations" && assembly != "Mafi"
                && assembly != "Mafi.Base" && assembly != "Mafi.Core"
                && assembly != "mscorlib" && assembly != "System"
                && !assembly.StartsWith("System.", StringComparison.Ordinal))
                throw new InvalidDataException("Assembly refused: " + assembly);
        }

        private static string StableSortKey(object? value)
        {
            if (value == null) return "0";
            Type type = value.GetType();
            if (value is string text) return "s:" + text;
            if (type.IsEnum) return "e:" + type.FullName + ":"
                + Convert.ToInt64(value, CultureInfo.InvariantCulture)
                    .ToString("D20", CultureInfo.InvariantCulture);
            if (IsPrimitive(type)) return "p:" + type.FullName + ":"
                + Convert.ToString(value, CultureInfo.InvariantCulture);
            if (type.IsValueType)
                return "v:" + type.FullName + ":" + string.Join("|",
                    GetFields(type).Select(field =>
                        field.Name + "=" + StableSortKey(field.GetValue(value))));
            return "r:" + type.FullName + ":" + value;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}

using System;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using AutoTerrainDesignations.Mining;

namespace AutoTerrainDesignations.Access
{
    internal static partial class AccessSearchReplayRecorder
    {
        internal static AccessReplayCaptureOperation? BeginRecordMining(
            MiningRequest request, MiningPlan plan)
        {
            ArmState? arm;
            lock (s_gate)
            {
                arm = s_arm;
                if (arm == null || arm.Kind != "mining") return null;
                arm.MapName = s_currentMapName;
                s_arm = null;
            }
            return AccessReplayCaptureOperation.Start(operation =>
            {
                var token = operation.CancellationToken;
                operation.SetProgress(0, "Encoding mining request");
                byte[] input = AccessReplayGraphCodec.Serialize(request, token,
                    (done, total) => operation.SetProgress(
                        5 + (int)(done * 70 / Math.Max(1, total)),
                        "Encoding mining request"),
                    done => operation.SetSizingProgress(done));
                byte[] expected = MiningReplayFacade.Canonical(plan);
                byte[] payload = BuildPayload(input, expected, token);
                string hash = Sha256(payload, token);
                string root = GetInboxRoot();
                Directory.CreateDirectory(root);
                string destination = Path.Combine(root, arm.Name + "-" + hash.Substring(0, 16) + CaseExtension);
                if (Directory.Exists(destination)) return "Mining replay case already exists: " + destination;
                string temporary = Path.Combine(root, "." + arm.Name + ".tmp-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary);
                operation.SetPendingPublication(temporary, destination, "Captured mining plan: " + destination);
                try
                {
                    WriteMiningCaseFiles(temporary, arm, plan, input, expected, payload, hash, token);
                    token.ThrowIfCancellationRequested();
                    operation.SetProgress(99, "Mining replay ready");
                    return "Mining replay prepared.";
                }
                catch
                {
                    operation.TryFinalizePendingPublication(false, out _);
                    throw;
                }
            });
        }
        private static void WriteMiningCaseFiles(string temporary, ArmState arm,
            MiningPlan plan, byte[] input, byte[] expected, byte[] payload, string hash,
            System.Threading.CancellationToken token)
        {
            using (var file = File.Create(Path.Combine(temporary, "case.bin.gz")))
            using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            {
                for (int offset = 0; offset < payload.Length; offset += 1024 * 1024)
                {
                    token.ThrowIfCancellationRequested();
                    gzip.Write(payload, offset, Math.Min(1024 * 1024, payload.Length-offset));
                }
            }
            if (!string.Equals(Sha256(File.ReadAllBytes(arm.AssemblyPath), token), arm.AssemblyHash,
                StringComparison.Ordinal)) throw new InvalidDataException("Archived mining DLL identity changed.");
            string config =
#if DEBUG
                "Debug";
#else
                "Release";
#endif
            string manifest = "{\n" + string.Join(",\n", new[] {
                Json("schema", "1", false), Json("caseKind", "mining"),
                Json("caseName", arm.Name), Json("scenarioFamily", arm.ScenarioFamily),
                Json("mapName", arm.MapName),
                Json("semanticPolicy", "mining-planning-v1"),
                Json("provenance", plan.Depths.Count == 0
                    ? "accepted-empty-mining-plan" : "native-mining-batch-verified"),
                Json("armedUtc", arm.ArmedUtc.ToString("O", CultureInfo.InvariantCulture)),
                Json("capturedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                Json("payloadSha256", hash), Json("requestSha256", Sha256(input, token)),
                Json("canonicalSha256", Sha256(expected, token)),
                Json("requestBytes", input.Length.ToString(CultureInfo.InvariantCulture), false),
                Json("canonicalBytes", expected.Length.ToString(CultureInfo.InvariantCulture), false),
                Json("atdAssembly", arm.AssemblyPath), Json("atdAssemblySha256", arm.AssemblyHash),
                Json("buildConfiguration", config),
                Json("buildTimestamp", typeof(MiningPlanner).Assembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == "BuildTimestamp")?.Value ?? ""),
                Json("gameAssemblyFingerprint", GetGameAssemblyFingerprint(token))
            }) + "\n}\n";
            File.WriteAllText(Path.Combine(temporary, "manifest.json"), manifest, new UTF8Encoding(false));
        }

        internal static bool ValidateMiningContainer(MiningRequest request, MiningPlan plan, out string failure)
        {
            string directory = Path.Combine(Path.GetTempPath(), "ATD-mining-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string assembly = ResolveAssemblyPath(typeof(MiningPlanner).Assembly);
                var arm = new ArmState { Name = "synthetic", ScenarioFamily = "fixture", Kind = "mining",
                    AssemblyPath = assembly, AssemblyHash = Sha256(File.ReadAllBytes(assembly)), ArmedUtc = DateTime.UtcNow };
                byte[] input = AccessReplayGraphCodec.Serialize(request);
                byte[] canonical = MiningReplayFacade.Canonical(plan);
                Write(canonical);
#if DEBUG
                const bool candidate = true;
#else
                const bool candidate = false;
#endif
                if (!MiningReplayFacade.Replay(directory, candidate, false, 1, out failure)) return false;
                if (!MiningReplayFacade.BenchmarkCodec(directory, out failure)) return false;
                string manifestPath = Path.Combine(directory, "manifest.json");
                string manifest = File.ReadAllText(manifestPath);
                AccessSearchReplayFacade.RequireManifest(
                    manifest, "mapName", string.Empty);
                File.WriteAllText(manifestPath, manifest.Replace(arm.AssemblyHash, "invalid-assembly"));
                if (MiningReplayFacade.Replay(directory, false, false, 1, out _))
                { failure = "Mining replay accepted a wrong DLL identity."; return false; }
                File.WriteAllText(manifestPath, manifest.Replace(Sha256(input), "invalid-input"));
                if (MiningReplayFacade.Replay(directory, true, false, 1, out _))
                { failure = "Mining replay accepted corrupt input identity."; return false; }
                byte[] changed = (byte[])canonical.Clone();
                changed[changed.Length - 1] ^= 1;
                Write(changed);
                if (MiningReplayFacade.Replay(directory, true, false, 1, out _))
                { failure = "Mining replay ignored the independent expected geometry."; return false; }
                failure = string.Empty;
                return true;

                void Write(byte[] expected)
                {
                    var token = System.Threading.CancellationToken.None;
                    byte[] payload = BuildPayload(input, expected, token);
                    WriteMiningCaseFiles(directory, arm, plan, input, expected, payload, Sha256(payload), token);
                }
            }
            catch (Exception ex) { failure = "Mining container fixture: " + ex; return false; }
            finally { Directory.Delete(directory, true); }
        }

    }
}

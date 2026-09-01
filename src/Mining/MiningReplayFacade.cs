using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AutoTerrainDesignations.Access;
using Mafi;

namespace AutoTerrainDesignations.Mining
{
    internal static class MiningReplayFacade
    {
        private const int MaxPayloadBytes = 1024 * 1024 * 1024;

        internal static bool BenchmarkCodec(string directory, out string report)
        {
            try
            {
                Stopwatch timer = Stopwatch.StartNew();
                MiningRequest request = Load(directory, true, false, out _);
                double loadMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                byte[] input = AccessReplayGraphCodec.Serialize(request);
                double encodeMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var restored = (MiningRequest)AccessReplayGraphCodec.Deserialize(input, typeof(MiningRequest));
                double decodeMs = timer.Elapsed.TotalMilliseconds;
                if (!Canonical(MiningPlanner.Execute(request)).SequenceEqual(Canonical(MiningPlanner.Execute(restored))))
                    throw new InvalidDataException("Mining codec round trip changed geometry.");
                report = FormattableString.Invariant($"kind=mining requestBytes={input.Length} loadMs={loadMs:0.###} encodeMs={encodeMs:0.###} decodeMs={decodeMs:0.###}");
                return true;
            }
            catch (Exception ex) { report = "Mining codec benchmark failed: " + ex; return false; }
        }

        internal static bool IsMiningCase(string directory)
        {
            string path = Path.Combine(directory, "manifest.json");
            return File.Exists(path) && File.ReadAllText(path).Contains("mining-planning-v1");
        }

        internal static byte[] Canonical(MiningPlan plan)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("ATD_MINING_PLAN_V1");
                writer.Write(plan.Outcome);
                writer.Write(plan.ReconcileEmpty);
                writer.Write(plan.Depths.Count);
                foreach (var pair in plan.Depths.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X))
                { writer.Write(pair.Key.X); writer.Write(pair.Key.Y); writer.Write(pair.Value); }
                writer.Write(plan.Corners.Count);
                foreach (var pair in plan.Corners.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X))
                { writer.Write(pair.Key.X); writer.Write(pair.Key.Y); writer.Write(pair.Value); }
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal static bool Replay(string directory, bool allowAssemblyMismatch,
            bool allowGameMismatch, int repetitions, out string report)
        {
            try
            {
                if (repetitions < 1 || repetitions > 50)
                    throw new InvalidDataException("Repetitions must be between 1 and 50.");
                MiningRequest request = Load(directory, allowAssemblyMismatch, allowGameMismatch, out byte[] expected);
                double[] timings = new double[repetitions];
                MiningPlan? plan = null;
                for (int i = 0; i < repetitions; i++)
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    plan = MiningPlanner.Execute(request);
                    timings[i] = timer.Elapsed.TotalMilliseconds;
                    byte[] actual = Canonical(plan);
                    if (!expected.SequenceEqual(actual))
                    {
                        report = "Mining geometry differs: expected=" + AccessSearchReplayRecorder.Sha256(expected)
                            + " actual=" + AccessSearchReplayRecorder.Sha256(actual)
                            + " designations=" + plan.Depths.Count;
                        return false;
                    }
                }
                report = $"kind=mining exact=True designations={plan!.Depths.Count} "
                    + $"repetitions={repetitions} milliseconds=["
                    + string.Join(",", timings.Select(t => t.ToString("0.###", CultureInfo.InvariantCulture))) + "]";
                return true;
            }
            catch (Exception ex)
            {
                report = "Mining replay failed closed: " + ex;
                return false;
            }
        }

        private static MiningRequest Load(string directory, bool allowAssemblyMismatch,
            bool allowGameMismatch, out byte[] expected)
        {
            string manifest = File.ReadAllText(Path.Combine(directory, "manifest.json"));
            AccessSearchReplayFacade.RequireManifest(manifest, "semanticPolicy", "mining-planning-v1");
            AccessSearchReplayFacade.RequireManifest(manifest, "caseKind", "mining");
            if (!allowAssemblyMismatch)
            {
                string assembly = AccessSearchReplayRecorder.ResolveAssemblyPath(typeof(MiningPlanner).Assembly);
                AccessSearchReplayFacade.RequireManifest(manifest, "atdAssemblySha256",
                    AccessSearchReplayRecorder.Sha256(File.ReadAllBytes(assembly)));
                AccessSearchReplayFacade.RequireManifest(manifest, "buildConfiguration", "Release");
            }
            if (!allowGameMismatch)
                AccessSearchReplayFacade.RequireManifest(manifest, "gameAssemblyFingerprint",
                    AccessSearchReplayRecorder.GetGameAssemblyFingerprint());
            var file = new FileInfo(Path.Combine(directory, "case.bin.gz"));
            if (!file.Exists || file.Length <= 0 || file.Length > 256 * 1024 * 1024)
                throw new InvalidDataException("Invalid mining replay compressed size.");
            byte[] payload;
            using (var input = file.OpenRead())
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaxPayloadBytes)
                        throw new InvalidDataException("Mining replay payload exceeds size limit.");
                    output.Write(buffer, 0, read);
                }
                payload = output.ToArray();
            }
            AccessSearchReplayFacade.RequireManifest(manifest, "payloadSha256", AccessSearchReplayRecorder.Sha256(payload));
            using (var stream = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadString() != "ATD_ACCESS_REPLAY" || reader.ReadInt32() != 1)
                    throw new InvalidDataException("Unsupported mining replay container.");
                // Mining capture accepts a payload up to 1 GiB. Keep the request
                // section at the same ceiling so a valid, highly compressible full
                // terrain snapshot cannot be recorded and then rejected by replay.
                byte[] input = ReadPart(reader, MaxPayloadBytes);
                expected = ReadPart(reader, 256 * 1024 * 1024);
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing mining replay bytes.");
                AccessSearchReplayFacade.RequireManifest(manifest, "requestSha256", AccessSearchReplayRecorder.Sha256(input));
                AccessSearchReplayFacade.RequireManifest(manifest, "canonicalSha256", AccessSearchReplayRecorder.Sha256(expected));
                return (MiningRequest)AccessReplayGraphCodec.Deserialize(input, typeof(MiningRequest));
            }
        }

        private static byte[] ReadPart(BinaryReader reader, int limit)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > limit || length > reader.BaseStream.Length-reader.BaseStream.Position)
                throw new InvalidDataException("Invalid mining replay section length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }
    }
}

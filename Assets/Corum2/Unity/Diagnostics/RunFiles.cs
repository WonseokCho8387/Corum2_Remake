using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    /// <summary>Writes structured log lines (L) as NDJSON, one file per run.</summary>
    public sealed class NdjsonFileSink : IDiagSink, IDisposable
    {
        readonly StreamWriter _writer;
        public string Path { get; }
        public long Lines { get; private set; }

        public NdjsonFileSink(string path)
        {
            Path = path;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            _writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = false };
        }

        public void Write(DiagEvent e)
        {
            _writer.WriteLine(DiagJson.Serialize(e));
            Lines++;
            if (Lines % 200 == 0) _writer.Flush();
        }

        public void Flush() => _writer.Flush();

        public void Dispose()
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }

    public static class RunFiles
    {
        /// <summary>Editor: &lt;project&gt;/Diagnostics/Runs (git-ignored). Player: persistentDataPath/Diagnostics/Runs.</summary>
        public static string RunDirectory()
        {
            var root = Application.isEditor
                ? System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Diagnostics", "Runs"))
                : System.IO.Path.Combine(Application.persistentDataPath, "Diagnostics", "Runs");
            Directory.CreateDirectory(root);
            return root;
        }

        public static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(new UTF8Encoding(false).GetBytes(text));
                var sb = new StringBuilder(64);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string FileSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(path))
            {
                var bytes = sha.ComputeHash(s);
                var sb = new StringBuilder(64);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Data snapshot (D): the exact Inspector values used by this run.</summary>
        public static string WriteDataSnapshot(string dir, string runId, WorldDefinition world, MovementConfig move, InputProfileConfig input, AirUnlockFixture unlocks)
        {
            var sb = new StringBuilder();
            sb.Append("{\n\"runId\":\"").Append(runId).Append("\",\n");
            sb.Append("\"movement\":").Append(JsonUtility.ToJson(move, true)).Append(",\n");
            sb.Append("\"input\":").Append(JsonUtility.ToJson(input, true)).Append(",\n");
            sb.Append("\"unlocks\":").Append(JsonUtility.ToJson(unlocks, true)).Append(",\n");
            sb.Append("\"world\":{\"startMapId\":\"").Append(world.startMapId).Append("\",\"startSpawnId\":\"").Append(world.startSpawnId).Append("\",\"maps\":[\n");
            for (var i = 0; i < world.maps.Count; i++)
            {
                if (i > 0) sb.Append(",\n");
                sb.Append(JsonUtility.ToJson(world.maps[i], true));
            }
            sb.Append("]}\n}\n");
            var path = System.IO.Path.Combine(dir, runId + ".data.json");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }
    }
}

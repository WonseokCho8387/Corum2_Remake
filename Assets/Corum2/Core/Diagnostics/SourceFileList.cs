using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Corum2.Core
{
    public sealed class SourceFileEntry
    {
        public string Path;
        public long Size;
        public string Sha256;
        /// <summary>Git blob id of the same content after git's own normalisation; "" when unknown.</summary>
        public string GitBlob = "";
    }

    /// <summary>
    /// Per-file source list for master spec 13.3 source_file_hash_manifest (WP02-M02): relative path, byte size and
    /// SHA-256 of every file in the source scope. Written as TSV with "# key: value" header lines.
    /// </summary>
    public static class SourceFileList
    {
        public const string Columns = "path\tsize\tsha256\tgit_blob";

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(bytes);
                var sb = new StringBuilder(64);
                foreach (var b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>Hashes the files that exist under root; paths use '/' and are sorted ordinally.
        /// Missing paths (e.g. deleted but still tracked) are returned in <paramref name="missing"/>.</summary>
        public static List<SourceFileEntry> Hash(string root, IEnumerable<string> relativePaths, List<string> missing = null)
        {
            var list = new List<SourceFileEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in relativePaths)
            {
                var rel = raw.Replace('\\', '/');
                if (rel.Length == 0 || !seen.Add(rel)) continue;
                CheckPath(rel);
                var full = System.IO.Path.Combine(root, rel);
                if (!File.Exists(full)) { missing?.Add(rel); continue; }
                var bytes = File.ReadAllBytes(full);
                list.Add(new SourceFileEntry { Path = rel, Size = bytes.LongLength, Sha256 = Sha256Hex(bytes) });
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return list;
        }

        static void CheckPath(string rel)
        {
            foreach (var c in rel)
                if (c == '\t' || c == '\n' || c == '\r') throw new ArgumentException("path contains a tab or line break: " + rel);
            if (rel.StartsWith("/", StringComparison.Ordinal) || rel.Contains("../") || rel.StartsWith("..", StringComparison.Ordinal))
                throw new ArgumentException("path must be relative inside the root: " + rel);
        }

        /// <summary>All files under the given top-level folders (fallback when git is not available).</summary>
        public static List<string> Walk(string root, IEnumerable<string> folders)
        {
            var result = new List<string>();
            foreach (var f in folders)
            {
                var dir = System.IO.Path.Combine(root, f);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    result.Add(file.Substring(root.TrimEnd('/', '\\').Length + 1).Replace('\\', '/'));
            }
            return result;
        }

        public static long TotalBytes(IEnumerable<SourceFileEntry> entries)
        {
            long n = 0;
            foreach (var e in entries) n += e.Size;
            return n;
        }

        /// <summary>TSV text: header lines (# key: value), the column line, one row per file.</summary>
        public static string ToTsv(IList<SourceFileEntry> entries, IEnumerable<KeyValuePair<string, string>> header)
        {
            var sb = new StringBuilder();
            foreach (var kv in header) sb.Append("# ").Append(kv.Key).Append(": ").Append(kv.Value.Replace('\n', ' ')).Append('\n');
            sb.Append("# entries: ").Append(entries.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("# total_bytes: ").Append(TotalBytes(entries).ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(Columns).Append('\n');
            foreach (var e in entries)
                sb.Append(e.Path).Append('\t').Append(e.Size.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Sha256).Append('\t').Append(e.GitBlob ?? "").Append('\n');
            return sb.ToString();
        }

        /// <summary>Reads rows back (header and comment lines skipped).</summary>
        public static List<SourceFileEntry> Parse(string tsv)
        {
            var list = new List<SourceFileEntry>();
            var columnsSeen = false;
            foreach (var line in tsv.Split('\n'))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                if (!columnsSeen)
                {
                    if (line != Columns) throw new FormatException("expected column line, got: " + line);
                    columnsSeen = true;
                    continue;
                }
                var p = line.Split('\t');
                if (p.Length != 4) throw new FormatException("row needs 4 columns: " + line);
                list.Add(new SourceFileEntry
                {
                    Path = p[0], Size = long.Parse(p[1], CultureInfo.InvariantCulture), Sha256 = p[2], GitBlob = p[3],
                });
            }
            if (!columnsSeen) throw new FormatException("no column line");
            return list;
        }
    }
}

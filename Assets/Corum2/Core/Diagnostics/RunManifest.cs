using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Corum2.Core
{
    /// <summary>Ordered YAML mapping for nested manifest values.</summary>
    public sealed class YamlMap : List<KeyValuePair<string, object>>
    {
        public YamlMap Add(string key, object value)
        {
            Add(new KeyValuePair<string, object>(key, value));
            return this;
        }
    }

    /// <summary>
    /// Minimal YAML 1.2 emitter for run manifests (WP02-M01-R1-A). Every string is a double-quoted scalar with escapes,
    /// so values containing ": ", "#", brackets, quotes or line breaks stay one string. Booleans, numbers and null are
    /// written as YAML types. Keys must be plain identifiers.
    /// </summary>
    public static class Yaml
    {
        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == 0x7f || (c >= 0x80 && c <= 0x9f) || c == 0x2028 || c == 0x2029 || c == 0xfeff)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Scalar(object v)
        {
            switch (v)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case string s: return Quote(s);
                case int i: return i.ToString(CultureInfo.InvariantCulture);
                case long l: return l.ToString(CultureInfo.InvariantCulture);
                case float f: return Number(f);
                case double d: return Number(d);
                default: return Quote(Convert.ToString(v, CultureInfo.InvariantCulture));
            }
        }

        static string Number(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            var s = d.ToString("R", CultureInfo.InvariantCulture);
            return s.IndexOfAny(new[] { '.', 'E', 'e' }) >= 0 ? s : s + ".0";
        }

        static void CheckKey(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("empty YAML key");
            foreach (var c in key)
                if (!(c == '_' || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
                    throw new ArgumentException("YAML key must be [a-z0-9_]: " + key);
        }

        public static void WriteEntry(StringBuilder sb, string key, object value, int indent)
        {
            CheckKey(key);
            var pad = new string(' ', indent);
            if (value is YamlMap map)
            {
                if (map.Count == 0) { sb.Append(pad).Append(key).Append(": {}\n"); return; }
                sb.Append(pad).Append(key).Append(":\n");
                foreach (var kv in map) WriteEntry(sb, kv.Key, kv.Value, indent + 2);
                return;
            }
            if (value is IEnumerable list && !(value is string))
            {
                var items = new List<object>();
                foreach (var x in list) items.Add(x);
                if (items.Count == 0) { sb.Append(pad).Append(key).Append(": []\n"); return; }
                sb.Append(pad).Append(key).Append(":\n");
                foreach (var x in items)
                {
                    if (x is YamlMap m && m.Count > 0)
                    {
                        // "- " then the first entry inline, the rest aligned under it.
                        var first = true;
                        foreach (var kv in m)
                        {
                            var sub = new StringBuilder();
                            WriteEntry(sub, kv.Key, kv.Value, indent + 4);
                            var text = sub.ToString();
                            if (first) { sb.Append(pad).Append("  - ").Append(text.Substring(indent + 4)); first = false; }
                            else sb.Append(text);
                        }
                    }
                    else sb.Append(pad).Append("  - ").Append(Scalar(x is YamlMap ? null : x)).Append('\n');
                }
                return;
            }
            sb.Append(pad).Append(key).Append(": ").Append(Scalar(value)).Append('\n');
        }
    }

    /// <summary>
    /// Run manifest (spec 13.3) with fixed run identity (WP02-M01-R1-B). run_id and started_at are set once at run
    /// creation and cannot be replaced; every render records updated_at and an increasing manifest_revision.
    /// </summary>
    public sealed class RunManifest
    {
        public const string TimeFormat = "yyyy-MM-ddTHH:mm:sszzz";
        static readonly string[] Reserved = { "run_id", "started_at", "updated_at", "manifest_revision" };

        public string RunId { get; }
        public DateTimeOffset StartedAt { get; }
        public int Revision { get; private set; }
        readonly List<KeyValuePair<string, object>> _fields = new List<KeyValuePair<string, object>>();
        readonly List<string> _comments = new List<string>();

        public RunManifest(string runId, DateTimeOffset startedAt)
        {
            RunId = runId;
            StartedAt = startedAt;
        }

        public void Comment(string line) => _comments.Add(line);

        /// <summary>Adds a field, or replaces its value keeping the original position.</summary>
        public void Set(string key, object value)
        {
            if (Array.IndexOf(Reserved, key) >= 0) throw new ArgumentException(key + " is fixed at run creation");
            for (var i = 0; i < _fields.Count; i++)
                if (_fields[i].Key == key) { _fields[i] = new KeyValuePair<string, object>(key, value); return; }
            _fields.Add(new KeyValuePair<string, object>(key, value));
        }

        public object Get(string key)
        {
            foreach (var kv in _fields) if (kv.Key == key) return kv.Value;
            return null;
        }

        public static string FormatTime(DateTimeOffset t) => t.ToString(TimeFormat, CultureInfo.InvariantCulture);

        /// <summary>Renders the next revision of the manifest.</summary>
        public string Render(DateTimeOffset now)
        {
            Revision++;
            var sb = new StringBuilder();
            foreach (var c in _comments) sb.Append("# ").Append(c.Replace("\n", " ")).Append('\n');
            Yaml.WriteEntry(sb, "run_id", RunId, 0);
            foreach (var kv in _fields) Yaml.WriteEntry(sb, kv.Key, kv.Value, 0);
            Yaml.WriteEntry(sb, "started_at", FormatTime(StartedAt), 0);
            Yaml.WriteEntry(sb, "updated_at", FormatTime(now), 0);
            Yaml.WriteEntry(sb, "manifest_revision", Revision, 0);
            return sb.ToString();
        }
    }
}

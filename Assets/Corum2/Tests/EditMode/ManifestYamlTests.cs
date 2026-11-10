using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Corum2.Core;
using NUnit.Framework;

namespace Corum2.Tests
{
    /// <summary>WP02-M01-R1-A (typed, parseable YAML) and -B (run_id / started_at kept across rewrites).
    /// Full YAML parsing of the written files is checked by Tools/validate_manifest.py (PyYAML safe_load).</summary>
    public class ManifestYamlTests
    {
        static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(9));

        /// <summary>Decoder for the double-quoted scalars the emitter writes (YAML 1.2 escapes it uses).</summary>
        static string Unquote(string q)
        {
            Assert.That(q.Length >= 2 && q[0] == '"' && q[q.Length - 1] == '"', "not double-quoted: " + q);
            var sb = new StringBuilder();
            for (var i = 1; i < q.Length - 1; i++)
            {
                var c = q[i];
                Assert.That(c != '"', "unescaped quote inside scalar: " + q);
                if (c != '\\') { sb.Append(c); continue; }
                var e = q[++i];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)int.Parse(q.Substring(i + 1, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: Assert.Fail("unknown escape \\" + e); break;
                }
            }
            return sb.ToString();
        }

        static readonly string[] Tricky =
        {
            "Claude Code (automated: GameHost.RunInputScript)",
            "[Keyboard, Mouse]; scripted input",
            "a # not a comment",
            "true", "null", "1.2.0", "- dash", "* star", "& anchor", "! tag", "% dir", "@at", "`tick", "{x: 1}",
            "quote \" and back\\slash", "line1\nline2\r\n\ttab", "한글 값: 콜론", "\u0001ctrl\u007f", "  lead/trail  ", "",
            "D:\\ProjectR\\Corum2", "key: value: again",
        };

        [Test]
        public void Quote_RoundTrips_TrickyStrings()
        {
            foreach (var s in Tricky)
            {
                var q = Yaml.Quote(s);
                Assert.That(q.IndexOf('\n'), Is.EqualTo(-1), "one line: " + s);
                Assert.That(Unquote(q), Is.EqualTo(s));
            }
        }

        [Test]
        public void Scalars_AreTyped()
        {
            Assert.That(Yaml.Scalar(true), Is.EqualTo("true"));
            Assert.That(Yaml.Scalar(false), Is.EqualTo("false"));
            Assert.That(Yaml.Scalar(null), Is.EqualTo("null"));
            Assert.That(Yaml.Scalar(50), Is.EqualTo("50"));
            Assert.That(Yaml.Scalar(0.02), Is.EqualTo("0.02"));
            Assert.That(Yaml.Scalar(2.0), Is.EqualTo("2.0"));
            Assert.That(Yaml.Scalar("false"), Is.EqualTo("\"false\""));
        }

        [Test]
        public void Lists_Maps_AndEmptyCollections()
        {
            var sb = new StringBuilder();
            Yaml.WriteEntry(sb, "devices", new List<YamlMap>
            {
                new YamlMap().Add("name", "Keyboard").Add("virtual", false),
                new YamlMap().Add("name", "Corum2VirtualKeyboard").Add("virtual", true),
            }, 0);
            Yaml.WriteEntry(sb, "policies", new[] { "EXP-A: x", "EXP-B" }, 0);
            Yaml.WriteEntry(sb, "empty", new string[0], 0);
            Yaml.WriteEntry(sb, "nested", new YamlMap().Add("tree", "abc").Add("inner", new YamlMap().Add("n", 1)), 0);
            Assert.That(sb.ToString(), Is.EqualTo(
                "devices:\n" +
                "  - name: \"Keyboard\"\n" +
                "    virtual: false\n" +
                "  - name: \"Corum2VirtualKeyboard\"\n" +
                "    virtual: true\n" +
                "policies:\n" +
                "  - \"EXP-A: x\"\n" +
                "  - \"EXP-B\"\n" +
                "empty: []\n" +
                "nested:\n" +
                "  tree: \"abc\"\n" +
                "  inner:\n" +
                "    n: 1\n"));
        }

        [Test]
        public void Keys_MustBePlain()
        {
            Assert.Throws<ArgumentException>(() => Yaml.WriteEntry(new StringBuilder(), "bad key", 1, 0));
            Assert.Throws<ArgumentException>(() => Yaml.WriteEntry(new StringBuilder(), "a:b", 1, 0));
        }

        static RunManifest Sample()
        {
            var m = new RunManifest("RUN-20261110-090000", T0);
            m.Comment("test manifest");
            m.Set("spec_sha256", new string('a', 64));
            m.Set("working_tree_dirty", false);
            m.Set("dirty_diff_artifact", null);
            m.Set("tester", "Claude Code (automated: GameHost.RunInputScript)");
            return m;
        }

        static Dictionary<string, string> TopLevel(string yaml)
        {
            var d = new Dictionary<string, string>();
            foreach (var line in yaml.Split('\n'))
            {
                if (line.Length == 0 || line[0] == '#' || line[0] == ' ') continue;
                var i = line.IndexOf(": ", StringComparison.Ordinal);
                if (i < 0) { Assert.That(line.EndsWith(":"), "top-level line: " + line); continue; }
                var key = line.Substring(0, i);
                Assert.That(Regex.IsMatch(key, "^[a-z0-9_]+$"), "key: " + key);
                Assert.That(d.ContainsKey(key), Is.False, "duplicate key " + key);
                d[key] = line.Substring(i + 2);
            }
            return d;
        }

        [Test]
        public void Rewrites_KeepRunIdAndStartedAt()
        {
            var m = Sample();
            var first = TopLevel(m.Render(T0.AddSeconds(1)));
            m.Set("tester", "changed later");
            m.Set("capture", new YamlMap().Add("path", "D:/x.mp4").Add("fps", 50));
            var second = TopLevel(m.Render(T0.AddSeconds(12)));
            var third = TopLevel(m.Render(T0.AddMinutes(3)));

            foreach (var r in new[] { first, second, third })
            {
                Assert.That(Unquote(r["run_id"]), Is.EqualTo("RUN-20261110-090000"));
                Assert.That(Unquote(r["started_at"]), Is.EqualTo("2026-11-10T09:00:00+09:00"));
            }
            Assert.That(Unquote(first["updated_at"]), Is.EqualTo("2026-11-10T09:00:01+09:00"));
            Assert.That(Unquote(third["updated_at"]), Is.EqualTo("2026-11-10T09:03:00+09:00"));
            Assert.That(first["manifest_revision"], Is.EqualTo("1"));
            Assert.That(third["manifest_revision"], Is.EqualTo("3"));
            Assert.That(Unquote(second["tester"]), Is.EqualTo("changed later"));
            Assert.That(m.StartedAt, Is.EqualTo(T0));
        }

        [Test]
        public void ReservedKeys_CannotBeReplaced()
        {
            var m = Sample();
            foreach (var k in new[] { "run_id", "started_at", "updated_at", "manifest_revision" })
                Assert.Throws<ArgumentException>(() => m.Set(k, "x"), k);
        }

        [Test]
        public void Set_ReplacesInPlace_FieldOrderStable()
        {
            var m = Sample();
            var a = m.Render(T0);
            m.Set("spec_sha256", new string('b', 64));
            var b = m.Render(T0);
            Func<string, int> idx = s => s.IndexOf("spec_sha256:", StringComparison.Ordinal);
            Assert.That(idx(b), Is.EqualTo(idx(a)));
            Assert.That(b, Does.Contain("spec_sha256: \"" + new string('b', 64) + "\""));
        }

        [Test]
        public void Rendered_Values_AreTyped()
        {
            var r = TopLevel(Sample().Render(T0));
            Assert.That(r["working_tree_dirty"], Is.EqualTo("false"));
            Assert.That(r["dirty_diff_artifact"], Is.EqualTo("null"));
            Assert.That(Unquote(r["spec_sha256"]), Does.Match("^[0-9a-f]{64}$"));
            Assert.That(Unquote(r["tester"]), Is.EqualTo("Claude Code (automated: GameHost.RunInputScript)"));
        }
    }
}

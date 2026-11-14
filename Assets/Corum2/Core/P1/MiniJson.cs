using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Corum2.Core.P1
{
    /// <summary>
    /// Minimal JSON for the P1 save snapshot (Core has no engine references, so no JsonUtility). Values are
    /// Dictionary&lt;string, object&gt; (insertion ordered via list of keys), List&lt;object&gt;, string, double, bool, null.
    /// </summary>
    public static class MiniJson
    {
        public sealed class Obj
        {
            public readonly List<string> Keys = new List<string>();
            public readonly Dictionary<string, object> Map = new Dictionary<string, object>();

            public Obj Set(string k, object v)
            {
                if (!Map.ContainsKey(k)) Keys.Add(k);
                Map[k] = v;
                return this;
            }

            public bool Has(string k) => Map.ContainsKey(k);
            public object Get(string k) => Map.TryGetValue(k, out var v) ? v : null;
            public string Str(string k, string d = "") => Get(k) as string ?? d;
            public double Num(string k, double d = 0) => Get(k) is double x ? x : d;
            public int Int(string k, int d = 0) => Get(k) is double x ? (int)Math.Round(x) : d;
            public long Long(string k, long d = 0) => Get(k) is double x ? (long)Math.Round(x) : d;
            public float Flt(string k, float d = 0) => Get(k) is double x ? (float)x : d;
            public bool Bool(string k, bool d = false) => Get(k) is bool b ? b : d;
            public Obj O(string k) => Get(k) as Obj;
            public List<object> L(string k) => Get(k) as List<object> ?? new List<object>();
        }

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            W(sb, v);
            return sb.ToString();
        }

        static void W(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: Str(sb, s); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case ulong u: Str(sb, u.ToString(CultureInfo.InvariantCulture)); break;
                case Obj o:
                    sb.Append('{');
                    for (var i = 0; i < o.Keys.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Str(sb, o.Keys[i]);
                        sb.Append(':');
                        W(sb, o.Map[o.Keys[i]]);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable e:
                    sb.Append('[');
                    var first = true;
                    foreach (var x in e)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        W(sb, x);
                    }
                    sb.Append(']');
                    break;
                default: Str(sb, v.ToString()); break;
            }
        }

        static void Str(StringBuilder sb, string s)
        {
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static object Parse(string s)
        {
            var i = 0;
            var v = P(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("MiniJson: trailing data at " + i);
            return v;
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object P(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("MiniJson: unexpected end");
            var c = s[i];
            if (c == '{')
            {
                i++;
                var o = new Obj();
                Ws(s, ref i);
                if (s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    var k = (string)P(s, ref i);
                    Ws(s, ref i);
                    if (s[i++] != ':') throw new FormatException("MiniJson: ':' expected");
                    o.Set(k, P(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return o; }
                    throw new FormatException("MiniJson: ',' or '}' expected");
                }
            }
            if (c == '[')
            {
                i++;
                var l = new List<object>();
                Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(P(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("MiniJson: ',' or ']' expected");
                }
            }
            if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (s[i] != '"')
                {
                    if (s[i] == '\\')
                    {
                        i++;
                        var e = s[i++];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                            default: sb.Append(e); break;
                        }
                    }
                    else sb.Append(s[i++]);
                }
                i++;
                return sb.ToString();
            }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            var st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }
    }
}

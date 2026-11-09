using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Corum2.Core
{
    /// <summary>
    /// One structured log record. Common fields follow spec 13.4; event-specific fields go in Fields.
    /// </summary>
    public sealed class DiagEvent
    {
        public string Type = "";
        public string RunId = "";
        public string TestCaseId = "";
        public long EventId;
        public double RealTime;
        public double GameTime;
        public long SimulationTick;
        public string ActorId = "";
        public string TargetId = "";
        public string StateBefore = "";
        public string StateAfter = "";
        public string Reason = "";
        public string DataVersion = "";
        public readonly List<KeyValuePair<string, object>> Fields = new List<KeyValuePair<string, object>>();

        public object Get(string key)
        {
            foreach (var kv in Fields)
                if (kv.Key == key) return kv.Value;
            return null;
        }
    }

    public interface IDiagSink
    {
        void Write(DiagEvent e);
    }

    public sealed class MemorySink : IDiagSink
    {
        public readonly List<DiagEvent> Events = new List<DiagEvent>();
        public void Write(DiagEvent e) => Events.Add(e);

        public List<DiagEvent> OfType(string type)
        {
            var r = new List<DiagEvent>();
            foreach (var e in Events)
                if (e.Type == type) r.Add(e);
            return r;
        }
    }

    public sealed class NullSink : IDiagSink
    {
        public static readonly NullSink Instance = new NullSink();
        public void Write(DiagEvent e) { }
    }

    public sealed class DiagLogger
    {
        readonly IDiagSink _sink;
        long _nextEventId = 1;

        public string RunId = "";
        public string TestCaseId = "";
        public string DataVersion = "";
        public double RealTime;
        public double GameTime;
        public long Tick;
        /// <summary>Per-tick position samples (needed for trajectory logs, e.g. TC-03-02). Off by default.</summary>
        public bool TickSamples;

        public DiagLogger(IDiagSink sink)
        {
            _sink = sink ?? NullSink.Instance;
        }

        public void Emit(string type, string actorId, string targetId, string before, string after, string reason, params (string key, object value)[] fields)
        {
            var e = new DiagEvent
            {
                Type = type,
                RunId = RunId,
                TestCaseId = TestCaseId,
                EventId = _nextEventId++,
                RealTime = RealTime,
                GameTime = GameTime,
                SimulationTick = Tick,
                ActorId = actorId ?? "",
                TargetId = targetId ?? "",
                StateBefore = before ?? "",
                StateAfter = after ?? "",
                Reason = reason ?? "",
                DataVersion = DataVersion,
            };
            if (fields != null)
                foreach (var f in fields)
                    e.Fields.Add(new KeyValuePair<string, object>(f.key, f.value));
            _sink.Write(e);
        }
    }

    /// <summary>NDJSON serialisation without engine dependencies.</summary>
    public static class DiagJson
    {
        public static string Serialize(DiagEvent e)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            Prop(sb, "type", e.Type, true);
            Prop(sb, "runId", e.RunId);
            Prop(sb, "testCaseId", e.TestCaseId);
            Prop(sb, "eventId", e.EventId);
            Prop(sb, "realTime", e.RealTime);
            Prop(sb, "gameTime", e.GameTime);
            Prop(sb, "simulationTick", e.SimulationTick);
            Prop(sb, "actorId", e.ActorId);
            Prop(sb, "targetId", e.TargetId);
            Prop(sb, "stateBefore", e.StateBefore);
            Prop(sb, "stateAfter", e.StateAfter);
            Prop(sb, "reason", e.Reason);
            Prop(sb, "dataVersion", e.DataVersion);
            foreach (var kv in e.Fields) Prop(sb, kv.Key, kv.Value);
            sb.Append('}');
            return sb.ToString();
        }

        static void Prop(StringBuilder sb, string key, object value, bool first = false)
        {
            if (!first) sb.Append(',');
            WriteString(sb, key);
            sb.Append(':');
            WriteValue(sb, value);
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case float f:
                    sb.Append(float.IsNaN(f) || float.IsInfinity(f) ? "null" : f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case Enum en:
                    WriteString(sb, en.ToString());
                    break;
                case Vec2 p:
                    sb.Append("[");
                    WriteValue(sb, p.X);
                    sb.Append(',');
                    WriteValue(sb, p.Y);
                    sb.Append(']');
                    break;
                default:
                    WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
                    break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
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
    }
}

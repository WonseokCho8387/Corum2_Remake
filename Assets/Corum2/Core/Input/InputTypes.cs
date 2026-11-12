using System;
using System.Collections.Generic;

namespace Corum2.Core
{
    /// <summary>
    /// Logical game actions (spec 4.1). Device buttons are mapped to these by the Unity adapter.
    /// Move* are edges derived from the stick/keys direction so short taps keep their raw timestamps.
    /// Pad names in comments are the master-spec labels; SpecialAttack on Y is EXP-INPUT-01 (not final).
    /// </summary>
    public enum LogicalAction
    {
        MoveLeft,
        MoveRight,
        MoveUp,
        MoveDown,
        Jump,           // A
        Attack,         // X (default proposal in the spec)
        SpecialAttack,  // Y candidate (EXP-INPUT-01)
        Guard,          // RB
        RingToggle,     // LB
        QuickUp,        // D-pad up (quickslot)
        QuickDown,
        QuickLeft,
        QuickRight,
        ButtonB,        // B: discard / cancel inside the ring (R-02-05). Outside the ring B is undecided (spec 4.1).
        PagePrev,       // LT: previous ring page (R-02-05). Unassigned outside the ring.
        PageNext,       // RT: next ring page
    }

    public enum InputEdge
    {
        Press,
        Release,
    }

    /// <summary>8-way direction sector of the movement input.</summary>
    public enum Dir8
    {
        None,
        Up,
        UpRight,
        Right,
        DownRight,
        Down,
        DownLeft,
        Left,
        UpLeft,
    }

    public static class Dir8Util
    {
        public static int Horizontal(Dir8 d)
        {
            switch (d)
            {
                case Dir8.Right:
                case Dir8.UpRight:
                case Dir8.DownRight:
                    return 1;
                case Dir8.Left:
                case Dir8.UpLeft:
                case Dir8.DownLeft:
                    return -1;
                default:
                    return 0;
            }
        }

        public static int Vertical(Dir8 d)
        {
            switch (d)
            {
                case Dir8.Up:
                case Dir8.UpRight:
                case Dir8.UpLeft:
                    return 1;
                case Dir8.Down:
                case Dir8.DownRight:
                case Dir8.DownLeft:
                    return -1;
                default:
                    return 0;
            }
        }

        public static Dir8 FromComponents(int h, int v)
        {
            if (h > 0) return v > 0 ? Dir8.UpRight : (v < 0 ? Dir8.DownRight : Dir8.Right);
            if (h < 0) return v > 0 ? Dir8.UpLeft : (v < 0 ? Dir8.DownLeft : Dir8.Left);
            return v > 0 ? Dir8.Up : (v < 0 ? Dir8.Down : Dir8.None);
        }

        /// <summary>
        /// Stick vector to 8 sectors of 45 degrees centred on the axes, with a radial deadzone.
        /// Sector boundaries and deadzone are tuning values (U-04), recorded as EXP-INPUT-03.
        /// </summary>
        public static Dir8 FromStick(float x, float y, float deadzone)
        {
            var mag = Math.Sqrt(x * x + y * y);
            if (mag < deadzone || mag <= 0.0) return Dir8.None;
            var angle = Math.Atan2(y, x) * 180.0 / Math.PI; // -180..180, 0 = right
            if (angle < 0) angle += 360.0;
            var sector = (int)Math.Floor((angle + 22.5) / 45.0) % 8;
            switch (sector)
            {
                case 0: return Dir8.Right;
                case 1: return Dir8.UpRight;
                case 2: return Dir8.Up;
                case 3: return Dir8.UpLeft;
                case 4: return Dir8.Left;
                case 5: return Dir8.DownLeft;
                case 6: return Dir8.Down;
                default: return Dir8.DownRight;
            }
        }
    }

    /// <summary>One press or release with a unique id and the raw device timestamp (real time, seconds).</summary>
    public readonly struct InputEvent
    {
        public readonly long EventId;
        public readonly LogicalAction Action;
        public readonly InputEdge Edge;
        public readonly double RawTime;
        public readonly string Device;
        public readonly string Control;
        /// <summary>
        /// Seq of the stick sample this direction edge came from (WP05-F01 R2). All edges of one sample share it; 0 means
        /// the event is its own sample (buttons, keyboard keys, scripted single edges).
        /// </summary>
        public readonly long SampleGroup;

        public InputEvent(long eventId, LogicalAction action, InputEdge edge, double rawTime, string device, string control, long sampleGroup = 0)
        {
            SampleGroup = sampleGroup;
            EventId = eventId;
            Action = action;
            Edge = edge;
            RawTime = rawTime;
            Device = device;
            Control = control;
        }
    }

    /// <summary>
    /// One analog stick reading with its device time and an arrival sequence number taken from the same counter as
    /// InputEvent ids, so stick changes and button presses can be replayed in the order they happened (WP03-F02).
    /// </summary>
    public readonly struct StickSample
    {
        public readonly double Time;
        public readonly float X;
        public readonly float Y;
        public readonly long Seq;

        public StickSample(double time, float x, float y, long seq)
        {
            Time = time;
            X = x;
            Y = y;
            Seq = seq;
        }

        /// <summary>True if this sample happened before the event (time, then arrival order).</summary>
        public bool Before(in InputEvent e) => Time < e.RawTime || (Time == e.RawTime && Seq < e.EventId);
    }

    /// <summary>Everything the simulation receives for one fixed tick.</summary>
    public sealed class TickInput
    {
        public double RealTime;
        public float StickX;
        public float StickY;
        public Dir8 Dir;
        public string Device = "none";
        public readonly List<InputEvent> Events = new List<InputEvent>();

        public void Clear()
        {
            Events.Clear();
        }
    }

    /// <summary>
    /// Turns successive direction samples into MoveLeft/Right/Up/Down press/release edges.
    /// Used by the Unity collector (per input callback) and by tests.
    /// </summary>
    public sealed class DirectionEdgeTracker
    {
        int _h;
        int _v;

        public Dir8 Current => Dir8Util.FromComponents(_h, _v);

        public void Update(Dir8 dir, double time, string device, string control, Func<long> nextId, List<InputEvent> output, long sampleGroup = 0)
        {
            var h = Dir8Util.Horizontal(dir);
            var v = Dir8Util.Vertical(dir);
            if (h != _h)
            {
                if (_h != 0) output.Add(new InputEvent(nextId(), _h > 0 ? LogicalAction.MoveRight : LogicalAction.MoveLeft, InputEdge.Release, time, device, control, sampleGroup));
                if (h != 0) output.Add(new InputEvent(nextId(), h > 0 ? LogicalAction.MoveRight : LogicalAction.MoveLeft, InputEdge.Press, time, device, control, sampleGroup));
                _h = h;
            }
            if (v != _v)
            {
                if (_v != 0) output.Add(new InputEvent(nextId(), _v > 0 ? LogicalAction.MoveUp : LogicalAction.MoveDown, InputEdge.Release, time, device, control, sampleGroup));
                if (v != 0) output.Add(new InputEvent(nextId(), v > 0 ? LogicalAction.MoveUp : LogicalAction.MoveDown, InputEdge.Press, time, device, control, sampleGroup));
                _v = v;
            }
        }
    }

    /// <summary>
    /// Input transport buffer (appendix 3.2 item 2). Collects events between ticks and hands each event
    /// to exactly one tick. This is NOT a gameplay jump/combo buffer: it never re-delivers or delays an event
    /// beyond the next tick.
    /// </summary>
    public sealed class InputTransport
    {
        readonly List<InputEvent> _pending = new List<InputEvent>();
        readonly HashSet<long> _delivered = new HashSet<long>();
        long _nextId = 1;

        public int PendingCount => _pending.Count;
        public long DuplicatesDropped { get; private set; }

        public long NextEventId() => _nextId++;

        public void Enqueue(InputEvent e) => _pending.Add(e);

        /// <summary>Moves all pending events into the tick, ordered by raw time then id.</summary>
        public void Drain(TickInput tick)
        {
            tick.Events.Clear();
            if (_pending.Count == 0) return;
            _pending.Sort((a, b) =>
            {
                var c = a.RawTime.CompareTo(b.RawTime);
                return c != 0 ? c : a.EventId.CompareTo(b.EventId);
            });
            foreach (var e in _pending)
            {
                if (!_delivered.Add(e.EventId))
                {
                    DuplicatesDropped++;
                    continue;
                }
                tick.Events.Add(e);
            }
            _pending.Clear();
        }
    }
}

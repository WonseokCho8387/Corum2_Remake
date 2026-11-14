using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    /// <summary>
    /// P1 spell command recognition (MAG-39, 92~99):
    ///  - history = changes of the held 8-way direction (neutral is not an entry), recorded only in the combat input
    ///    context; entering any non-combat context clears it and nothing is replayed afterwards (MAG-99)
    ///  - the ten new spells use QCF/QCB/DP/RDP/HCF/HCB relative to facing (left facing mirrors, MAG-93); the magic
    ///    missile keeps the original absolute →↑← (MAG-39 exception)
    ///  - timing [시험]: motion within MotionWindow (first to last direction), button within ButtonWindow after the last
    ///    direction; the missile uses MissileWindow from its first direction to the button
    ///  - one button: the longest completed motion wins (MAG-94)
    /// </summary>
    public sealed class P1CommandRecognizer
    {
        public readonly struct Entry
        {
            public readonly Dir8 Dir;
            public readonly double T;
            /// <summary>Character facing when this direction was entered: a motion is read relative to the facing at its
            /// first direction (a QCB ends with back, which turns the walking character; MAG-93).</summary>
            public readonly int Facing;
            public Entry(Dir8 d, double t, int f) { Dir = d; T = t; Facing = f; }
        }

        readonly P1Profile _p;
        readonly List<Entry> _h = new List<Entry>();
        Dir8 _last = Dir8.None;
        public IReadOnlyList<Entry> History => _h;

        public P1CommandRecognizer(P1Profile p) => _p = p;

        public P1CommandRecognizer Clone()
        {
            var c = new P1CommandRecognizer(_p) { _last = _last };
            c._h.AddRange(_h);
            return c;
        }

        public void Observe(Dir8 held, double t, int facing = 1)
        {
            if (held == _last) return;
            _last = held;
            if (held == Dir8.None) return;
            _h.Add(new Entry(held, t, facing));
            var cut = t - Math.Max(_p.MissileWindow, _p.MotionWindow + _p.ButtonWindow) - 0.05;
            var n = 0;
            while (n < _h.Count && _h[n].T < cut) n++;
            if (n > 0) _h.RemoveRange(0, n);
            if (_h.Count > 32) _h.RemoveRange(0, _h.Count - 32);
        }

        /// <summary>MAG-99: drop every partial motion. The held direction is not re-recorded until it changes.</summary>
        public void Clear(Dir8 held)
        {
            _h.Clear();
            _last = held;
        }

        public static Dir8[] Motion(MotionKind m)
        {
            switch (m)
            {
                case MotionKind.QCF: return new[] { Dir8.Down, Dir8.DownRight, Dir8.Right };
                case MotionKind.QCB: return new[] { Dir8.Down, Dir8.DownLeft, Dir8.Left };
                case MotionKind.DP: return new[] { Dir8.Right, Dir8.Down, Dir8.DownRight };
                case MotionKind.RDP: return new[] { Dir8.Left, Dir8.Down, Dir8.DownLeft };
                case MotionKind.HCF: return new[] { Dir8.Left, Dir8.DownLeft, Dir8.Down, Dir8.DownRight, Dir8.Right };
                case MotionKind.HCB: return new[] { Dir8.Right, Dir8.DownRight, Dir8.Down, Dir8.DownLeft, Dir8.Left };
                default: return new[] { Dir8.Right, Dir8.Up, Dir8.Left };
            }
        }

        public static Dir8 Mirror(Dir8 d)
        {
            switch (d)
            {
                case Dir8.Right: return Dir8.Left;
                case Dir8.Left: return Dir8.Right;
                case Dir8.UpRight: return Dir8.UpLeft;
                case Dir8.UpLeft: return Dir8.UpRight;
                case Dir8.DownRight: return Dir8.DownLeft;
                case Dir8.DownLeft: return Dir8.DownRight;
                default: return d;
            }
        }

        /// <summary>Direction sequence for this facing (missile: absolute, never mirrored).</summary>
        public static Dir8[] Sequence(SpellDef s, int facing)
        {
            var m = Motion(s.Motion);
            if (s.Motion == MotionKind.MissileOriginal || facing >= 0) return m;
            var r = new Dir8[m.Length];
            for (var i = 0; i < m.Length; i++) r[i] = Mirror(m[i]);
            return r;
        }

        bool Completes(SpellDef s, double now, out int facing)
        {
            facing = 1;
            var k = Motion(s.Motion).Length;
            if (k > _h.Count) return false;
            facing = _h[_h.Count - k].Facing;
            var seq = Sequence(s, facing);
            for (var i = 0; i < k; i++)
                if (_h[_h.Count - k + i].Dir != seq[i]) return false;
            var first = _h[_h.Count - k].T;
            var last = _h[_h.Count - 1].T;
            if (s.Motion == MotionKind.MissileOriginal) return now - first <= _p.MissileWindow + 1e-6;
            return last - first <= _p.MotionWindow + 1e-6 && now - last <= _p.ButtonWindow + 1e-6;
        }

        /// <summary>Spell completed for a button press now (longest motion wins), or null.</summary>
        public SpellDef Match(SpellButton button, double now) => Match(button, now, out _);

        /// <summary>Spell completed for a button press now (longest motion wins), with the facing the motion was read in.</summary>
        public SpellDef Match(SpellButton button, double now, out int motionFacing)
        {
            SpellDef best = null;
            var bestLen = 0;
            motionFacing = 1;
            foreach (var s in _p.Spells)
            {
                if (s.Button != button || !Completes(s, now, out var f)) continue;
                var len = Motion(s.Motion).Length;
                if (best == null || len > bestLen) { best = s; bestLen = len; motionFacing = f; }
            }
            return best;
        }

        public static bool TryButton(LogicalAction a, out SpellButton b)
        {
            switch (a)
            {
                case LogicalAction.Attack: b = SpellButton.X; return true;
                case LogicalAction.Jump: b = SpellButton.A; return true;
                case LogicalAction.ButtonB: b = SpellButton.B; return true;
                default: b = SpellButton.X; return false;
            }
        }

        public string Describe(double now)
        {
            var parts = new List<string>();
            foreach (var e in _h) parts.Add(e.Dir + "@" + (now - e.T).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            return string.Join(" ", parts);
        }
    }
}

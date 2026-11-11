using System;
using System.Collections.Generic;

namespace Corum2.Core
{
    /// <summary>
    /// R-02-02 magic command recognition: a direction sequence followed by an action button. The structure is confirmed;
    /// the command list, timing and every conflict with other inputs are not (U-02, CF-01). The temporary choices are
    /// explicit and logged, never hidden:
    ///   EXP-CMD-01 history = changes of the held 8-way direction (neutral is not an entry and does not clear the history);
    ///              a command matches when its directions are the newest history entries in order, the first of them
    ///              no older than Window before the button press; commands need at least MinCommandDirections directions
    ///   EXP-CMD-02 when a command matches, RingProfile.CommandTakesButton decides: true = the cast takes the press and the
    ///              button's normal action does not run; false = the normal action runs and the match is only logged.
    ///              Every match logs the competing normal action (button + held direction + player state) for CF-01
    ///   EXP-CMD-03 if the cast is refused (state lock), the press goes to the normal action and the refusal is logged
    ///   EXP-CMD-04 several matches: the longest sequence wins, then list order
    ///   EXP-CMD-05 a successful match clears the history; nothing else does except age (entries older than the longest
    ///              window are dropped)
    ///   EXP-CMD-06 history is only recorded while the game owns the input (not while the ring is open or the player is
    ///              dead); map up/down points keep working on their own (R-01-04), CF-01 decides their interaction
    /// </summary>
    public sealed class MagicCommandRecognizer
    {
        public readonly struct Entry
        {
            public readonly Dir8 Dir;
            public readonly double GameTime;
            public readonly long Tick;

            public Entry(Dir8 d, double t, long tick)
            {
                Dir = d;
                GameTime = t;
                Tick = tick;
            }
        }

        readonly RingProfile _p;
        readonly List<Entry> _history = new List<Entry>();
        Dir8 _last = Dir8.None;
        readonly float _maxWindow;

        public IReadOnlyList<Entry> History => _history;

        public MagicCommandRecognizer(RingProfile profile)
        {
            _p = profile;
            foreach (var c in profile.Commands) _maxWindow = Math.Max(_maxWindow, c.Window);
        }

        public bool HasCommandsFor(LogicalAction button)
        {
            foreach (var c in _p.Commands)
                if (c.Button == button) return true;
            return false;
        }

        /// <summary>Record the held direction after an input event or a tick resync.</summary>
        public void Observe(Dir8 held, double gameTime, long tick)
        {
            if (held == _last) return;
            _last = held;
            if (held == Dir8.None) return;
            _history.Add(new Entry(held, gameTime, tick));
            Prune(gameTime);
        }

        void Prune(double now)
        {
            var cut = now - _maxWindow - 1e-6;
            var n = 0;
            while (n < _history.Count && _history[n].GameTime < cut) n++;
            if (n > 0) _history.RemoveRange(0, n);
            if (_history.Count > 32) _history.RemoveRange(0, _history.Count - 32);
        }

        /// <summary>Best matching command for a button press now, or null.</summary>
        public MagicCommand Match(LogicalAction button, double now)
        {
            Prune(now);
            MagicCommand best = null;
            foreach (var c in _p.Commands)
            {
                if (c.Button != button) continue;
                var k = c.Directions.Count;
                if (k < _p.MinCommandDirections || k > _history.Count) continue;
                var ok = true;
                for (var i = 0; i < k && ok; i++)
                    ok = _history[_history.Count - k + i].Dir == c.Directions[i];
                if (!ok) continue;
                if (now - _history[_history.Count - k].GameTime > c.Window + 1e-6) continue;
                if (best == null || k > best.Directions.Count) best = c;
            }
            return best;
        }

        public void Clear()
        {
            _history.Clear();
        }

        public string Describe(double now)
        {
            var parts = new List<string>();
            foreach (var e in _history) parts.Add(e.Dir + "@" + (now - e.GameTime).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            return string.Join(" ", parts);
        }
    }
}

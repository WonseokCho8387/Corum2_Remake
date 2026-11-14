using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    public sealed class BuffInstance
    {
        public string BuffId = "";
        public Rarity Rarity;
        public float Strength;
        public float Remaining;
        public long Seq;
    }

    /// <summary>
    /// Temporary buffs (§5, BUF-01~11): independent stacks with their own remaining time, a per-buff stack cap, family
    /// sums capped per family, time passes only while the character is controllable (ring, menu, growth screen, loading
    /// pause it). Same effect and rarity from level-up or merchant = same strength and duration.
    /// </summary>
    public sealed class BuffSystem
    {
        readonly P1Profile _p;
        public readonly List<BuffInstance> Active = new List<BuffInstance>();
        public DiagLogger Log;
        long _seq;

        public BuffSystem(P1Profile p, DiagLogger log = null)
        {
            _p = p;
            Log = log;
        }

        public int Stacks(string id)
        {
            var n = 0;
            foreach (var b in Active) if (b.BuffId == id) n++;
            return n;
        }

        public bool AtCap(string id)
        {
            var d = _p.Buff(id);
            return d == null || Stacks(id) >= d.MaxStacks;
        }

        /// <summary>Adds a stack. False (nothing changes) at the stack cap (BUF-09).</summary>
        public bool Add(string id, Rarity r, string source = "")
        {
            var d = _p.Buff(id);
            if (d == null || AtCap(id))
            {
                Log?.Emit("p1.buff.refused", "player", "", "", "", "StackCap(BUF-09)", ("buffId", id), ("stacks", Stacks(id)));
                return false;
            }
            var b = new BuffInstance { BuffId = id, Rarity = r, Strength = _p.BuffRules.Strength(d, r), Remaining = _p.BuffRules.Duration(r), Seq = ++_seq };
            Active.Add(b);
            Log?.Emit("p1.buff.add", "player", "", "", "", source, ("buffId", id), ("rarity", r), ("strength", b.Strength), ("duration", b.Remaining),
                ("stacks", Stacks(id)));
            return true;
        }

        /// <summary>Advances timers only while controllable (BUF-01/02). Expired stacks are removed and the sums recomputed.</summary>
        public void Tick(float dt, bool controllable)
        {
            if (!controllable || dt <= 0f) return;
            for (var i = Active.Count - 1; i >= 0; i--)
            {
                Active[i].Remaining -= dt;
                if (Active[i].Remaining <= 1e-6f)
                {
                    Log?.Emit("p1.buff.expire", "player", "", "", "", "Expired", ("buffId", Active[i].BuffId), ("seq", Active[i].Seq));
                    Active.RemoveAt(i);
                }
            }
        }

        /// <summary>Additive sum of one family, clamped to the family cap (BUF-06/08).</summary>
        public float Family(string family)
        {
            float sum = 0, cap = 0;
            foreach (var d in _p.Buffs)
                if (d.Family == family)
                {
                    cap = d.FamilyCap;
                    foreach (var b in Active) if (b.BuffId == d.Id) sum += b.Strength;
                }
            return Math.Min(cap, sum);
        }

        public float PhysAtk => Family("PhysAtk");
        public float MagAtk => Family("MagAtk");
        public float AirAtk => Family("AirAtk");
        public float DamageReduction => Family("DamageReduction");
        public float MpCost => Family("MpCost");
        public float Move => Family("Move");
        public float AttackSpeed => Family("AttackSpeed");
        public float ItemHeal => Family("ItemHeal");

        public List<object> ToJson()
        {
            var l = new List<object>();
            foreach (var b in Active)
                l.Add(new MiniJson.Obj().Set("id", b.BuffId).Set("rarity", b.Rarity.ToString()).Set("strength", (double)b.Strength)
                    .Set("remaining", (double)b.Remaining).Set("seq", (double)b.Seq));
            return l;
        }

        public void FromJson(List<object> l)
        {
            Active.Clear();
            foreach (var x in l)
            {
                var o = (MiniJson.Obj)x;
                var b = new BuffInstance
                {
                    BuffId = o.Str("id"), Rarity = (Rarity)Enum.Parse(typeof(Rarity), o.Str("rarity")), Strength = o.Flt("strength"),
                    Remaining = o.Flt("remaining"), Seq = o.Long("seq"),
                };
                Active.Add(b);
                _seq = Math.Max(_seq, b.Seq);
            }
        }
    }
}

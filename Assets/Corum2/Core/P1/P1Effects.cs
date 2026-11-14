using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    /// <summary>Active sword enchant (MAG-50/71/72/77/78): one at a time; values are fixed when the cast commits.</summary>
    public sealed class SwordState
    {
        public string SpellId = "";
        public float Remaining;
        public float Bonus;      // light: physical sword attack bonus
        public float LastHit;    // light epic: extra light damage on the 4th normal hit
        public float Wave;       // light legendary: sword wave damage
        public float Rate;       // dark: lifesteal rate
        public float KillHeal;   // dark epic
        public float ComboRate;  // dark legendary: + per consecutive effective hit
        public float ComboCap = 0.25f;
        public bool Light => SpellId == "spell.light_sword";
        public bool Dark => SpellId == "spell.dark_sword";
    }

    /// <summary>
    /// Live P1 effects on the player that are not buffs: magic shield, archangel shield, sword enchant, status flags.
    /// Timers run on controllable time only (MAG-56). Statuses are flags only until the status list is decided
    /// (EXP-P1-STATUS-01: poison / confusion as test mapping, MAG-69/70).
    /// </summary>
    public sealed class P1Effects
    {
        public ShieldPool MagicShield;
        public bool ShieldStatusBlock;
        public ShieldPool ArchangelShield;
        public SwordState Sword;
        public readonly HashSet<string> Statuses = new HashSet<string>();
        public DiagLogger Log;

        public List<ShieldPool> ShieldOrder()
        {
            var l = new List<ShieldPool>();
            if (MagicShield != null && MagicShield.Remaining > 0) l.Add(MagicShield);
            if (ArchangelShield != null && ArchangelShield.Remaining > 0) l.Add(ArchangelShield);
            return l;
        }

        /// <summary>Statuses newly applied are blocked while an epic magic shield is up (MAG-70).</summary>
        public bool TryApplyStatus(string status)
        {
            if (MagicShield != null && MagicShield.Remaining > 0 && ShieldStatusBlock)
            {
                Log?.Emit("p1.status.blocked", "player", "", "", "", "ShieldEpic(MAG-70)", ("status", status));
                return false;
            }
            Statuses.Add(status);
            return true;
        }

        public void Tick(float dt, bool controllable)
        {
            if (!controllable || dt <= 0f) return;
            if (MagicShield != null)
            {
                MagicShield.Time -= dt;
                if (MagicShield.Time <= 0f || MagicShield.Remaining <= 0)
                {
                    Log?.Emit("p1.shield.end", "player", "", "", "", MagicShield.Remaining <= 0 ? "Depleted" : "Expired", ("shield", "MagicShield"));
                    MagicShield = null;
                }
            }
            if (ArchangelShield != null)
            {
                ArchangelShield.Time -= dt;
                if (ArchangelShield.Time <= 0f || ArchangelShield.Remaining <= 0) ArchangelShield = null;
            }
            if (Sword != null)
            {
                Sword.Remaining -= dt;
                if (Sword.Remaining <= 0f)
                {
                    Log?.Emit("p1.sword.end", "player", "", "", "", "Expired", ("spellId", Sword.SpellId));
                    Sword = null;
                }
            }
        }

        static MiniJson.Obj Pool(ShieldPool s) => s == null ? null : new MiniJson.Obj().Set("id", s.Id).Set("remaining", s.Remaining).Set("time", (double)s.Time);

        static ShieldPool Pool(MiniJson.Obj o) => o == null ? null : new ShieldPool { Id = o.Str("id"), Remaining = o.Int("remaining"), Time = o.Flt("time") };

        public MiniJson.Obj ToJson()
        {
            var o = new MiniJson.Obj().Set("magicShield", Pool(MagicShield)).Set("shieldStatusBlock", ShieldStatusBlock)
                .Set("archangelShield", Pool(ArchangelShield));
            if (Sword != null)
                o.Set("sword", new MiniJson.Obj().Set("spellId", Sword.SpellId).Set("remaining", (double)Sword.Remaining).Set("bonus", (double)Sword.Bonus)
                    .Set("lastHit", (double)Sword.LastHit).Set("wave", (double)Sword.Wave).Set("rate", (double)Sword.Rate)
                    .Set("killHeal", (double)Sword.KillHeal).Set("comboRate", (double)Sword.ComboRate).Set("comboCap", (double)Sword.ComboCap));
            var st = new List<string>(Statuses);
            st.Sort(StringComparer.Ordinal);
            o.Set("statuses", st);
            return o;
        }

        public void FromJson(MiniJson.Obj o)
        {
            MagicShield = Pool(o.O("magicShield"));
            ShieldStatusBlock = o.Bool("shieldStatusBlock");
            ArchangelShield = Pool(o.O("archangelShield"));
            var s = o.O("sword");
            Sword = s == null ? null : new SwordState
            {
                SpellId = s.Str("spellId"), Remaining = s.Flt("remaining"), Bonus = s.Flt("bonus"), LastHit = s.Flt("lastHit"), Wave = s.Flt("wave"),
                Rate = s.Flt("rate"), KillHeal = s.Flt("killHeal"), ComboRate = s.Flt("comboRate"), ComboCap = s.Flt("comboCap", 0.25f),
            };
            Statuses.Clear();
            foreach (var x in o.L("statuses")) Statuses.Add((string)x);
        }
    }
}

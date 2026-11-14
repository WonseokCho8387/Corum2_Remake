using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    public sealed class ShieldPool
    {
        public string Id = "";
        public int Remaining;
        public float Time;
    }

    /// <summary>
    /// P1 damage order (BAL-01~09, v0.1.36), engine neutral:
    ///   pre-buff = base + capped permanent technique/spell bonus + permanent attack stat + equipment/enchant
    ///   raw      = pre-buff x (1 + capped temporary attack buffs, families summed: BUF-08)
    ///   P0 event first: 0-damage events end here; guard multiplier 0.10 when a front guard succeeded
    ///   real     = raw x guard x K/(K+DEF) x (1 - clamp(temp reduction))   (decimals kept to the end, BAL-06)
    ///   int      = guarded ? floor(real) : max(1, floor(real))              (BAL-05)
    ///   shields  = magic shield, then archangel shield (BAL-09); the rest is HP damage
    /// </summary>
    public static class DamageCalc
    {
        const double Eps = 1e-6;

        public static double DefenseMultiplier(DamageProfile p, float defense) => p.DefenseK / (p.DefenseK + Math.Max(0f, defense));

        public static double Raw(float preBuff, float attackBuffRatio) => Math.Max(0.0, preBuff * (1.0 + attackBuffRatio));

        public static double PreShieldReal(DamageProfile p, double raw, bool guarded, float defense, float tempReduction)
        {
            var temp = 1.0 - Math.Max(0f, Math.Min(tempReduction, p.TempDamageReductionCap));
            return raw * (guarded ? p.GuardMultiplier : 1.0) * DefenseMultiplier(p, defense) * temp;
        }

        /// <summary>Integer damage before shields. zeroEvent = parry-zero / invulnerable / clash / wake-up invulnerable / no hit.</summary>
        public static int PreShield(DamageProfile p, double raw, bool guarded, float defense, float tempReduction, bool zeroEvent = false)
        {
            if (zeroEvent || raw <= 0) return 0;
            var real = PreShieldReal(p, raw, guarded, defense, tempReduction);
            var f = (int)Math.Floor(real + Eps);
            return guarded ? Math.Max(0, f) : Math.Max(p.MinDamage, f);
        }

        /// <summary>Absorbs damage with the shields in order; returns the HP damage left. Empty pools are removed.</summary>
        public static int Absorb(int damage, List<ShieldPool> ordered, List<(string id, int absorbed)> log = null)
        {
            var left = damage;
            for (var i = 0; i < ordered.Count && left > 0; i++)
            {
                var s = ordered[i];
                var take = Math.Min(left, s.Remaining);
                if (take <= 0) continue;
                s.Remaining -= take;
                left -= take;
                log?.Add((s.Id, take));
            }
            return left;
        }

        /// <summary>BAL-08: one floor over the summed real HP losses of one attack event, clamped to the missing HP.</summary>
        public static int Lifesteal(float summedActualLoss, float rate, float curHp, float maxHp)
        {
            if (summedActualLoss <= 0 || rate <= 0) return 0;
            var heal = (int)Math.Floor(summedActualLoss * rate + Eps);
            return (int)Math.Max(0, Math.Min(heal, Math.Floor(maxHp - curHp + Eps)));
        }

        /// <summary>BUF-10: max(per-spell minimum, floor((base + permanent change) x (1 - clamp(discount)))).</summary>
        public static int CommandMp(int permanentCost, int minMp, float discount, float discountCap)
        {
            var d = Math.Max(0f, Math.Min(discount, discountCap));
            return Math.Max(minMp, (int)Math.Floor(permanentCost * (1.0 - d) + Eps));
        }

        /// <summary>BUF-11: one item use, each resource summed then multiplied then floored once.</summary>
        public static int ItemHeal(int totalOfResource, float itemBuff) => (int)Math.Floor(totalOfResource * (1.0 + itemBuff) + Eps);
    }
}

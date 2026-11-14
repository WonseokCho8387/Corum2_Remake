using System.Collections.Generic;

namespace Corum2.Core.P1
{
    /// <summary>
    /// One consistent snapshot (GRO-48, MER-29/30, BUF-05, MAG-91): growth state incl. pending choices and rng,
    /// EXP wallets, buffs with remaining times, shields / sword, summon, merchant regions + rng, bag, HP/MP, map and
    /// position. An open shop choice is never part of it (MER-26/30). Projectiles and walls are transient (MAG-32).
    /// </summary>
    public static class P1SaveSnapshot
    {
        public const int Version = 1;

        public static string Save(P1Runtime rt)
        {
            var sim = rt.Sim;
            rt.G.CurHp = sim.Combat?.PlayerHp ?? rt.G.CurHp;
            var o = new MiniJson.Obj().Set("version", Version).Set("profile", rt.P.Version)
                .Set("map", sim.CurrentMap.Id).Set("x", (double)sim.Player.Position.X).Set("y", (double)sim.Player.Position.Y).Set("facing", sim.Player.Facing)
                .Set("hp", (double)(sim.Combat?.PlayerHp ?? rt.G.CurHp))
                .Set("progress", rt.G.ToJson()).Set("buffs", rt.Buffs.ToJson()).Set("effects", rt.Fx.ToJson())
                .Set("summon", rt.Magic.Summon?.ToJson()).Set("merchant", rt.Merchant.ToJson());
            var inv = sim.Items?.Inventory;
            if (inv != null)
            {
                var stacks = new List<object>();
                foreach (var s in inv.Stacks) stacks.Add(new MiniJson.Obj().Set("id", s.ItemId).Set("n", s.Count));
                o.Set("bag", stacks).Set("quickslots", new List<string>(inv.Quickslots));
            }
            return MiniJson.Write(o);
        }

        public static void Load(P1Runtime rt, string json, string reason)
        {
            var o = (MiniJson.Obj)MiniJson.Parse(json);
            var sim = rt.Sim;
            rt.Merchant.Interrupt("Load(MER-26)");
            rt.Bind(P1Progress.FromJson(o.O("progress")));
            rt.Buffs.FromJson(o.L("buffs"));
            rt.Fx.FromJson(o.O("effects"));
            rt.Magic.Summon = P1Summon.FromJson(o.O("summon"));
            rt.Merchant.FromJson(o.O("merchant"));
            var inv = sim.Items?.Inventory;
            if (inv != null && o.Has("bag"))
            {
                var ids = new List<string>();
                foreach (var s in inv.Stacks) ids.Add(s.ItemId);
                foreach (var id in ids) inv.TestSetCount(id, 0);
                foreach (var x in o.L("bag"))
                {
                    var s = (MiniJson.Obj)x;
                    inv.TestSetCount(s.Str("id"), s.Int("n"));
                }
                var q = o.L("quickslots");
                for (var i = 0; i < 4 && i < q.Count; i++) inv.Quickslots[i] = (string)q[i] ?? "";
            }
            sim.P1Place(o.Str("map"), new Vec2(o.Flt("x"), o.Flt("y")), o.Int("facing", 1), reason);
            if (sim.Player.State == MotorState.Dead) sim.Player.ReviveFromDead(reason);
            sim.Combat?.SetPlayerHp(o.Flt("hp"), reason);
            rt.OnLoaded();
            sim.Log.Emit("p1.load", PlayerMotor.Actor, "", "", "", reason, ("level", rt.G.Level), ("pending", rt.G.PendingLevels), ("levelExp", rt.G.LevelExp),
                ("lv50Currency", rt.G.Lv50Currency), ("buffs", rt.Buffs.Active.Count), ("summon", rt.Magic.Summon?.SpellId ?? ""), ("map", sim.CurrentMap.Id));
        }
    }
}

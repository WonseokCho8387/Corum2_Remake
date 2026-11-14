using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    /// <summary>Bag view of the P0 ring inventory with the P1 capacity model (EXP-P1-BAG-01).</summary>
    public sealed class InventoryBag : IP1Bag
    {
        readonly Func<Inventory> _inv;
        readonly P1Profile _p;

        public InventoryBag(Func<Inventory> inv, P1Profile p)
        {
            _inv = inv;
            _p = p;
        }

        public bool TryAdd(string itemId, int count)
        {
            var inv = _inv();
            if (inv == null) return false;
            var have = inv.Count(itemId);
            if (have == 0 && inv.StackCount >= _p.BagStackLimit) return false;
            if (have + count > _p.BagCountLimit) return false;
            inv.TestSetCount(itemId, have + count);
            return true;
        }
    }

    /// <summary>
    /// Wires the P1 systems into one Simulation (created only when a P1Profile is given; P0 scenes never have it).
    /// Owns: progression (EXP / growth queue), buffs, effects, P1 magic, the combat and item extensions and the growth
    /// screen pause. Growth screen (GRO-44/47): when a level-up is earned the first safe boundary is the end of the
    /// current tick; then the ring and pending item uses are closed / cancelled without cost, the command buffer is
    /// dropped (MAG-99) and the game stops until every pending choice is made (GRO-46/50).
    /// </summary>
    public sealed class P1Runtime : IItemExtension
    {
        public readonly Simulation Sim;
        public readonly P1Profile P;
        public P1Progress G { get; private set; }
        public ProgressionSystem Prog { get; private set; }
        public readonly BuffSystem Buffs;
        public readonly P1Effects Fx = new P1Effects();
        public readonly P1MagicSystem Magic;
        public P1CombatBridge Bridge { get; private set; }
        public MerchantSystem Merchant;
        public int GrowthCursor;
        public bool UiPause;
        public static readonly string[] ExperimentalPolicies =
        {
            "EXP-P1-AIM-01 projectile aim = held 8-way direction at the effect commit, none = facing (MAG-27/95 capture order undecided)",
            "EXP-P1-CAST-01 P1 spells start from the P0 EXP-MAGIC-01 states (air cast list undecided, MAG-105)",
            "EXP-P1-CAST-02 a command is read relative to the facing at its first direction and the cast faces that front",
            "EXP-P1-SWORD-01 recasting the same sword refreshes its time and values (MAG-50/77 open)",
            "EXP-P1-SUMMON-01 summons hover behind Volker and hit the nearest front enemy in range (AI detail open)",
            "EXP-P1-STATUS-01 poison / confusion are flags only (status list open)",
            "EXP-P1-ITEM-01 fallback rewards and merchant goods use p1test.* catalog ids (remake catalog undecided)",
            "EXP-P1-BAG-01 bag = 16 stacks x 99 (capacity undecided)",
            "EXP-P1-STORY-01 story stage is a data field (chapter boundaries undecided)",
            "EXP-P1-FB-01 a fire ball that reaches the end of its life explodes there",
            "EXP-P1-WAVE-01 light sword legendary wave starts on the 4th normal hit",
            "EXP-P1-B-01 B outside the ring without a completed motion keeps the P0 behaviour (selected-item use undecided)",
            "EXP-P1-REGION-01 merchant regions and safe spawns are sandbox test content",
            "EXP-P1-GROWTH-UI-01 growth screen input: left/right select, A confirm, Y reroll",
            "EXP-P1-SHOP-01 Up near the merchant opens the shop; up/down select, A buy/pick, B close (asks); the game is paused while the shop is open",
            "EXP-P1-EXIT-01 cancelling the region-exit question steps the player 0.6 u back from the edge",
        };

        public P1Runtime(Simulation sim, P1Profile p, P1Progress g = null)
        {
            Sim = sim;
            P = p;
            Buffs = new BuffSystem(p, sim.Log);
            Fx.Log = sim.Log;
            Magic = new P1MagicSystem(this);
            Merchant = new MerchantSystem(this);
            Merchant.Regions.AddRange(p.Regions);
            Bind(g ?? P1Progress.New(p));
            if (sim.Death != null) sim.Death.ExternalReturn = opt => opt == "CheckpointRestart" && RestoreCheckpoint("CheckpointRestart(R-06-19, MER-30)");
        }

        // ================================================================ checkpoint

        public string Checkpoint { get; private set; }

        public void SaveCheckpoint(string reason)
        {
            Checkpoint = P1SaveSnapshot.Save(this);
            Sim.Log.Emit("p1.checkpoint", PlayerMotor.Actor, "", "", "", reason, ("level", G.Level), ("levelExp", G.LevelExp), ("lv50Currency", G.Lv50Currency),
                ("buffs", Buffs.Active.Count), ("summon", Magic.Summon?.SpellId ?? ""), ("bytes", Checkpoint.Length));
        }

        public bool RestoreCheckpoint(string reason)
        {
            if (Checkpoint == null) return false;
            P1SaveSnapshot.Load(this, Checkpoint, reason);
            return true;
        }

        public void OnLoaded()
        {
            _growthEntered = false;
            PendingExit = null;
            UiPause = false;
            Magic.ClearProjectiles();
            Magic.ClearBuffer("Load(MAG-99)");
        }

        // ================================================================ region exit (MER-25)

        public sealed class ExitRequest
        {
            public string MapId = "", SpawnId = "", ConnectionId = "", Reason = "";
        }

        public ExitRequest PendingExit { get; private set; }

        /// <summary>Simulation transition gate: leaving a region that still has the merchant asks first.</summary>
        public bool BeforeTransition(string mapId, string spawnId, string connectionId, string reason)
        {
            var from = Merchant.RegionOfMap(Sim.CurrentMap.Id);
            var to = Merchant.RegionOfMap(mapId);
            if (from == null || to == from || !Merchant.PresentHere) return true;
            PendingExit = new ExitRequest { MapId = mapId, SpawnId = spawnId, ConnectionId = connectionId, Reason = reason };
            UiPause = true;
            Sim.Log.Emit("p1.merchant.exitConfirm", PlayerMotor.Actor, "", "", "", "LeaveRegionAsk(MER-25)", ("from", from.Id), ("to", to?.Id ?? ""));
            return false;
        }

        public void AnswerExit(bool leave)
        {
            var x = PendingExit;
            if (x == null) return;
            PendingExit = null;
            UiPause = false;
            if (!leave)
            {
                // stay: step back from the edge so the edge does not ask again at once (EXP-P1-EXIT-01)
                var back = Sim.Player.Position.X < (Sim.CurrentMap.MinX + Sim.CurrentMap.MaxX) * 0.5f ? 0.6f : -0.6f;
                Sim.Player.Position = new Vec2(Sim.Player.Position.X + back, Sim.Player.Position.Y);
                Sim.Log.Emit("p1.merchant.exitCancel", PlayerMotor.Actor, "", "", "", "Stay(MER-25)", ("stock", MerchantSystem.Describe(Merchant.Here)));
                return;
            }
            Merchant.Depart("LeftRegionConfirmed(MER-25)");
            Sim.P1Transition(x.MapId, x.SpawnId, x.ConnectionId, x.Reason);
        }

        /// <summary>(Re)binds a progress state (new game or loaded snapshot).</summary>
        public void Bind(P1Progress g)
        {
            G = g;
            Prog = new ProgressionSystem(P, g, Sim.Log) { Bag = new InventoryBag(() => Sim.Items?.Inventory, P) };
            Prog.Rules.BuffStacks = Buffs.Stacks;
            Prog.AddBuff = (id, r) => Buffs.Add(id, r, "GrowthCard");
            Prog.FullHeal = (hp, mp) =>
            {
                G.CurMp = mp;
                Sim.Combat?.SetPlayerHp(hp, "LevelUpFullHeal(GRO-04)");
            };
            if (Sim.Combat != null)
            {
                Bridge = new P1CombatBridge(P, Prog, Buffs, Fx, Sim.Combat, Sim.Log);
                Sim.Combat.Ext = Bridge;
                Sim.Combat.ExternalSpellEffect = Magic.OnEffectTime;
                Bridge.OnFourthHit = Magic.OnFourthHit;
            }
            if (Sim.Items != null) Sim.Items.Ext = this;
            SyncUnlocks();
        }

        public void OnStart()
        {
            Merchant.OnMapEntered(Sim.CurrentMap.Id);
            Sim.Combat?.SetPlayerHp(Math.Min(G.CurHp, Prog.Rules.MaxHp(G)), "P1Start");
            Sim.Log.Emit("p1.start", PlayerMotor.Actor, "", "", "", "P1Runtime", ("profile", P.Version), ("level", G.Level),
                ("experimentalPolicies", string.Join(" | ", ExperimentalPolicies)));
        }

        public bool GrowthOpen => G.Offer != null && G.PendingLevels > 0;

        /// <summary>The game is stopped (growth screen, or a UI that owns the game such as the merchant).</summary>
        public bool Paused => GrowthOpen || UiPause;

        public bool MerchantOpen => Merchant != null && Merchant.ShopOpen;

        /// <summary>MAG-99 combat input context: no ring, menu, popup, death prompt, growth screen, shop.</summary>
        public bool CombatContext =>
            !Paused && !MerchantOpen && !(Sim.Ring != null && Sim.Ring.IsOpen) && !(Sim.Death != null && Sim.Death.Active) &&
            Sim.Player.State != MotorState.Dead;

        public bool Controllable => CombatContext;

        void SyncUnlocks()
        {
            Sim.Player.GroundSpeedScale = 1f + Buffs.Move; // BF-MOVE
            Sim.Unlocks.ExtraJump = G.Has("DoubleJump");
            Sim.Unlocks.AirDash = G.Has("AirDash");
            Sim.Unlocks.Dive = G.Has("AirDive");
            if (Sim.Combat != null)
            {
                Sim.Combat.Unlocks.DashPierce = G.EquipTraits.Contains("AirDashPierce");
                Sim.Combat.Unlocks.DiveArea = G.EquipTraits.Contains("AirDiveAreaAttack");
            }
        }

        // ================================================================ simulation hooks

        public void BeginTick(List<InputEvent> events)
        {
            if (!CombatContext) Magic.ClearBuffer("NonCombatContext(MAG-99)");
            Magic.PreScan(events, Sim.GameTime);
        }

        public string TryCommand(in InputEvent e)
        {
            if (e.Edge == InputEdge.Press && e.Action == LogicalAction.MoveUp && CombatContext && Merchant.NearMerchant())
            {
                var r = Merchant.OpenShop();
                return r == null ? "p1shop:open" : "rejected:p1shop:" + r;
            }
            return Magic.TryCommand(e);
        }

        public void Observe(Dir8 held) => Magic.Observe(held, Sim.GameTime);

        public void Step(float dt)
        {
            SyncUnlocks();
            var c = Controllable;
            Magic.Step(dt, c);
            Buffs.Tick(dt, c);
            Fx.Tick(dt, c);
            if (G.Unclaimed.Count > 0) Prog.ClaimUnclaimed();
            if (G.PendingLevels > 0) Prog.EnsureOffer();
            if (GrowthOpen) EnterGrowth();
            G.CurHp = Sim.Combat?.PlayerHp ?? G.CurHp;
        }

        bool _growthEntered;

        void EnterGrowth()
        {
            if (_growthEntered) return;
            _growthEntered = true;
            GrowthCursor = 0;
            var real = Sim.Log.RealTime;
            if (Sim.Ring != null && Sim.Ring.IsOpen) Sim.Ring.Close("GrowthPreempt(GRO-47)", real);
            Sim.Items?.CancelPending("GrowthPreempt(GRO-47, no consume)");
            Merchant?.Interrupt("GrowthPreempt(GRO-47/50)");
            Magic.ClearBuffer("GrowthScreen(MAG-99)");
            Sim.Log.Emit("p1.growth.open", PlayerMotor.Actor, "", "", "", "FirstSafeBoundary(GRO-44/47)", ("targetLevel", G.Offer.TargetLevel),
                ("cards", string.Join(",", G.Offer.Cards)), ("hp", Sim.Combat?.PlayerHp ?? -1));
        }

        /// <summary>Input while paused by the growth screen (EXP-P1-GROWTH-UI-01).</summary>
        public void HandlePausedInput(List<InputEvent> events)
        {
            if (GrowthOpen) EnterGrowth(); // a level-up granted outside the step still pre-empts the ring / pending use
            foreach (var e in events)
            {
                if (e.Edge != InputEdge.Press) continue;
                if (GrowthOpen)
                {
                    var n = G.Offer.Cards.Count;
                    switch (e.Action)
                    {
                        case LogicalAction.MoveLeft: GrowthCursor = (GrowthCursor + n - 1) % n; break;
                        case LogicalAction.MoveRight: GrowthCursor = (GrowthCursor + 1) % n; break;
                        case LogicalAction.SpecialAttack: Prog.Reroll(); break;
                        case LogicalAction.Jump: ChooseGrowth(GrowthCursor); break;
                    }
                }
                else if (PendingExit != null)
                {
                    if (e.Action == LogicalAction.Jump) AnswerExit(true);
                    else if (e.Action == LogicalAction.ButtonB) AnswerExit(false);
                }
                else Merchant?.HandleInput(e);
            }
        }

        public string ChooseGrowth(int index)
        {
            var r = Prog.Choose(index);
            GrowthCursor = 0;
            if (r == null && !GrowthOpen)
            {
                _growthEntered = false;
                Sim.Log.Emit("p1.growth.close", PlayerMotor.Actor, "", "", "", "AllChoicesDone(GRO-46)", ("level", G.Level), ("hp", Sim.Combat?.PlayerHp ?? -1));
            }
            else if (r == null) _growthEntered = true;
            return r;
        }

        public void OnMapChanged()
        {
            Magic.OnMapChanged();
            Merchant.OnMapEntered(Sim.CurrentMap.Id);
        }

        // ================================================================ items

        public string ItemHeal(ItemDefinition item)
        {
            var t = P.Item(item.Id);
            var hpTotal = t != null ? t.HpTotal : (int)item.Amount;
            var mpTotal = t != null ? t.MpTotal : 0;
            var hp = DamageCalc.ItemHeal(hpTotal, Buffs.ItemHeal);
            var mp = DamageCalc.ItemHeal(mpTotal, Buffs.ItemHeal);
            var hpBefore = Sim.Combat?.PlayerHp ?? 0;
            var mpBefore = G.CurMp;
            if (hp > 0) Sim.Combat?.SetPlayerHp(hpBefore + hp, "ItemHeal(BUF-11)");
            if (mp > 0) G.CurMp = Math.Min(Prog.Rules.MaxMp(G), G.CurMp + mp);
            Sim.Log.Emit("p1.item.heal", PlayerMotor.Actor, item.Id, "", "", "OneFloorPerResource(BUF-11)", ("hpParts", hpTotal), ("mpParts", mpTotal),
                ("itemBuff", Buffs.ItemHeal), ("hp", hp), ("mp", mp), ("hpBefore", hpBefore), ("hpAfter", Sim.Combat?.PlayerHp ?? 0), ("mpBefore", mpBefore), ("mpAfter", G.CurMp));
            return "p1heal hp+" + hp + " mp+" + mp;
        }

        public string CastScroll(ItemDefinition item, long requestId) => Magic.CastScroll(item.MagicId, item.Id, requestId);

        /// <summary>Adds P0 ring item definitions for the P1 test catalog (potions, scrolls) to a ring profile.</summary>
        public static void AddRingItems(P1Profile p, RingProfile ring)
        {
            foreach (var t in p.Items)
            {
                if (ring.FindItem(t.Id) != null) continue;
                var d = new ItemDefinition { Id = t.Id, Name = t.Name, Description = t.Name, Cooldown = 0.5f };
                if (t.ScrollSpellId != "") { d.Kind = ItemKind.MagicScroll; d.Effect = ItemEffectType.Magic; d.MagicId = t.ScrollSpellId; }
                else if (t.Revive) { d.Kind = ItemKind.Consumable; d.Effect = ItemEffectType.Revive; }
                else { d.Kind = ItemKind.Consumable; d.Effect = ItemEffectType.Heal; d.Amount = t.HpTotal; }
                ring.Items.Add(d);
            }
            foreach (var s in p.Spells)
                if (ring.FindMagic(s.Id) == null)
                    ring.Magics.Add(new MagicDefinition { Id = s.Id, Effect = MagicEffect.External, CastTime = s.CastTime, Recovery = s.Recovery, Knockback = 1f });
        }
    }
}

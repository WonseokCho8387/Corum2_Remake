using System;
using System.Collections.Generic;

namespace Corum2.Core
{
    public enum UseSource
    {
        Ring,
        Quickslot,
    }

    public enum UseDecision
    {
        /// <summary>Accepted; runs at the start of the next simulation tick (EXP-ITEM-01).</summary>
        Accepted,
        /// <summary>Accepted and waiting until the player can act (R-05-05 hit reaction; CF-07 motion, EXP-ITEM-03).</summary>
        Reserved,
        Rejected,
    }

    public struct UseResult
    {
        public UseDecision Decision;
        public string Reason;
        public long RequestId;
    }

    /// <summary>A use request waiting for its tick (spec 10.3 PendingUse).</summary>
    public sealed class PendingUse
    {
        public long RequestId;
        public string ItemId = "";
        public UseSource Source;
        public double RequestRealTime;
        public double RequestGameTime;
        public long RequestTick;
        public string State = "";
        public string WaitReason = "";
    }

    /// <summary>
    /// Runs item uses inside the simulation. Ring and quickslot requests arrive between ticks; effects happen only in
    /// Step (BeginTick), never at the request. Policies for the undecided parts are experimental:
    ///   EXP-ITEM-01 an accepted use runs at the start of the next tick
    ///   EXP-ITEM-02 one shared item cooldown after an item use, length from the used item (U-03)
    ///   EXP-ITEM-03 a request made during an attack, dash or dive waits until that motion ends (CF-07)
    ///   EXP-ITEM-04 the item is consumed when the effect runs; if it is gone by then the use is cancelled (CF-08)
    ///   EXP-ITEM-05 one pending use at a time; further requests are rejected and consume nothing (CF-08)
    ///   EXP-ITEM-06 death cancels a pending use without consuming it (CF-08)
    ///   EXP-QS-01 a quickslot press during hit reaction is rejected, not reserved (CF-08)
    /// Confirmed: R-05-05 a ring use during hit reaction is reserved and runs after the reaction ends.
    /// </summary>
    public sealed class ItemUseSystem
    {
        readonly Simulation _sim;
        readonly RingProfile _p;
        public readonly Inventory Inventory;
        long _nextRequestId = 1;

        public PendingUse Pending { get; private set; }
        /// <summary>Game time when the shared item cooldown ends.</summary>
        public double CooldownUntil { get; private set; }
        public string LastCooldownItem { get; private set; } = "";
        public int ExecutedCount { get; private set; }

        public ItemUseSystem(Simulation sim, RingProfile profile)
        {
            _sim = sim;
            _p = profile;
            Inventory = new Inventory(profile);
        }

        DiagLogger Log => _sim.Log;
        PlayerMotor P => _sim.Player;

        public double CooldownLeft => Math.Max(0.0, CooldownUntil - _sim.GameTime);

        static bool InHitReaction(MotorState s) => s == MotorState.HitStun || s == MotorState.DodgeReact;

        static bool InMotion(MotorState s) =>
            s == MotorState.GroundAttack || s == MotorState.AirAttack || s == MotorState.AirDash || s == MotorState.Dive;

        /// <summary>EXP-ITEM-07: a magic scroll also waits through a slide or roll (states where a cast cannot start).</summary>
        static bool WaitsFor(ItemDefinition item, MotorState s) =>
            InMotion(s) || (item != null && item.Effect == ItemEffectType.Magic && (s == MotorState.Slide || s == MotorState.Roll));

        /// <summary>Request from the ring (A on an item) or a quickslot press. Called between ticks at real time.</summary>
        public UseResult Request(string itemId, UseSource source, double realTime, string via)
        {
            var id = _nextRequestId++;
            var item = _p.FindItem(itemId);
            var s = P.State;
            string reject = null;
            if (item == null || Inventory.Count(itemId) <= 0) reject = "NotOwned";
            else if (!item.Usable)
                reject = item.Kind == ItemKind.Quest ? "NotUsable:Quest" : item.Effect == ItemEffectType.Revive ? "NotUsable:ReviveOnlyOnDeath(EXP-DEATH-05)" : "NotUsable:NoEffect";
            else if (s == MotorState.Dead) reject = "PlayerDead";
            else if (Pending != null) reject = "PendingExists(EXP-ITEM-05, CF-08)";
            else if (CooldownLeft > 0) reject = "Cooldown(EXP-ITEM-02)";
            else if (source == UseSource.Quickslot && InHitReaction(s)) reject = "QuickslotDuringHitReaction(EXP-QS-01, CF-08)";

            Log.RealTime = realTime;
            if (reject != null)
            {
                Log.Emit("item.request", PlayerMotor.Actor, itemId, s.ToString(), s.ToString(), "Rejected:" + reject,
                    ("requestId", id), ("source", source), ("via", via), ("count", Inventory.Count(itemId)),
                    ("cooldownLeft", CooldownLeft), ("pendingRequestId", Pending?.RequestId ?? 0));
                return new UseResult { Decision = UseDecision.Rejected, Reason = reject, RequestId = id };
            }

            var wait = InHitReaction(s) ? "HitReaction(R-05-05)" : WaitsFor(item, s) ? "Motion:" + s + (InMotion(s) ? "(EXP-ITEM-03, CF-07)" : "(EXP-ITEM-07)") : "";
            Pending = new PendingUse
            {
                RequestId = id, ItemId = itemId, Source = source, RequestRealTime = realTime, RequestGameTime = _sim.GameTime,
                RequestTick = _sim.Tick, State = wait.Length > 0 ? "Reserved" : "Accepted", WaitReason = wait,
            };
            var decision = wait.Length > 0 ? UseDecision.Reserved : UseDecision.Accepted;
            Log.Emit("item.request", PlayerMotor.Actor, itemId, s.ToString(), s.ToString(), decision + (wait.Length > 0 ? ":" + wait : ":NextTick(EXP-ITEM-01)"),
                ("requestId", id), ("source", source), ("via", via), ("count", Inventory.Count(itemId)), ("cooldownLeft", CooldownLeft));
            return new UseResult { Decision = decision, Reason = wait, RequestId = id };
        }

        /// <summary>Called at the start of every Step: runs or keeps the pending use.</summary>
        public void BeginTick()
        {
            var u = Pending;
            if (u == null) return;
            var s = P.State;
            if (s == MotorState.Dead)
            {
                Pending = null;
                Log.Emit("item.cancel", PlayerMotor.Actor, u.ItemId, u.State, "Cancelled", "PlayerDead(EXP-ITEM-06, CF-08)",
                    ("requestId", u.RequestId), ("consumed", false));
                return;
            }
            var waitItem = _p.FindItem(u.ItemId);
            if (InHitReaction(s) || WaitsFor(waitItem, s))
            {
                var why = InHitReaction(s) ? "HitReaction(R-05-05)" : "Motion:" + s + (InMotion(s) ? "(EXP-ITEM-03, CF-07)" : "(EXP-ITEM-07)");
                if (u.WaitReason != why)
                {
                    Log.Emit("item.wait", PlayerMotor.Actor, u.ItemId, u.WaitReason, why, "StillLocked", ("requestId", u.RequestId));
                    u.WaitReason = why;
                    u.State = "Reserved";
                }
                return;
            }
            Pending = null;
            Execute(u);
        }

        void Execute(PendingUse u)
        {
            var item = _p.FindItem(u.ItemId);
            var cleared = new List<string>();
            if (item != null && item.Effect == ItemEffectType.Magic && Inventory.Count(u.ItemId) > 0)
            {
                // WP05-F03 / EXP-ITEM-07: start the cast first; consume, clear slots and start the cooldown only if it started.
                var magic0 = _p.FindMagic(item.MagicId);
                Log.Emit("magic.request", PlayerMotor.Actor, "", "", "", "Scroll(R-02-10)",
                    ("magicId", item.MagicId), ("power", magic0?.Power ?? -1f), ("castTime", magic0?.CastTime ?? -1f), ("source", "scroll:" + item.Id),
                    ("requestId", u.RequestId));
                var refused = _sim.Combat != null ? _sim.Combat.CastMagic(magic0, "scroll:" + item.Id, u.RequestId) : "NoCombat";
                if (refused != null)
                {
                    Log.Emit("item.cancel", PlayerMotor.Actor, u.ItemId, u.State, "Cancelled", "CastRefused:" + refused + "(EXP-ITEM-07)",
                        ("requestId", u.RequestId), ("consumed", false), ("cooldownStarted", false), ("count", Inventory.Count(u.ItemId)));
                    return;
                }
            }
            if (item == null || !Inventory.Remove(u.ItemId, 1, cleared))
            {
                Log.Emit("item.cancel", PlayerMotor.Actor, u.ItemId, u.State, "Cancelled", "NotOwnedAtExecution(EXP-ITEM-04, CF-08)",
                    ("requestId", u.RequestId), ("consumed", false));
                return;
            }
            ExecutedCount++;
            var hpBefore = _sim.Combat?.PlayerHp ?? -1f;
            var effect = "none";
            switch (item.Effect)
            {
                case ItemEffectType.Heal:
                    if (_sim.Combat != null)
                    {
                        _sim.Combat.PlayerHp = Math.Min(_sim.Combat.PlayerMaxHp, _sim.Combat.PlayerHp + item.Amount);
                        effect = "heal+" + item.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else effect = "heal(no combat in this scene)";
                    break;
                case ItemEffectType.Magic:
                    effect = "magic:" + item.MagicId + ":castStarted";
                    break;
            }
            CooldownUntil = _sim.GameTime + item.Cooldown;
            LastCooldownItem = item.Id;
            Log.Emit("item.use", PlayerMotor.Actor, item.Id, u.State, "Executed", effect,
                ("requestId", u.RequestId), ("source", u.Source), ("requestTick", u.RequestTick), ("execTick", _sim.Tick),
                ("requestGameTime", u.RequestGameTime), ("execGameTime", _sim.GameTime), ("waitedFor", u.WaitReason),
                ("hpBefore", hpBefore), ("hpAfter", _sim.Combat?.PlayerHp ?? -1f), ("countAfter", Inventory.Count(item.Id)),
                ("cooldown", item.Cooldown), ("cooldownUntil", CooldownUntil), ("quickslotsCleared", string.Join(",", cleared)),
                ("inventory", Inventory.Describe()));
        }
    }
}

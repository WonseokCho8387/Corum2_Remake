using System;
using System.Collections.Generic;

namespace Corum2.Core
{
    public enum DeathStage
    {
        None,
        /// <summary>Death motion (R-06-19 "사망 모션 후").</summary>
        Motion,
        /// <summary>A revive item is owned: ask whether to use it.</summary>
        RevivePrompt,
        /// <summary>Game over with the three options.</summary>
        GameOver,
    }

    /// <summary>
    /// R-06-19 player death flow, TC-06-19a part (UI branch). Confirmed: after the death motion, a revive question when a
    /// revive item is owned, otherwise game over; game over offers checkpoint restart, save select / load and lobby.
    /// TC-06-19b (the actual return to a checkpoint / save / lobby) needs the save/checkpoint contract (U-08) and is
    /// BLOCKED_SPEC: choosing an option is logged and shown, nothing is restored.
    /// Temporary choices (U-08):
    ///   EXP-DEATH-01 death motion length DeathMotionTime (game time); input is ignored during it
    ///   EXP-DEATH-02 declining the revive goes to game over
    ///   EXP-DEATH-03 revive: HP = ReviveHpRatio x max, full invulnerability for ReviveInvulnTime, same position, one
    ///                revive item consumed
    ///   EXP-DEATH-04 prompt / menu input: up/down or left/right move the cursor, A (Jump) confirms, B declines the revive
    ///                (B does nothing on the game-over menu); the world keeps simulating (enemies start no new attack while
    ///                the player is dead, R-08-07)
    ///   EXP-DEATH-05 revive items are only offered here, never used from the ring or a quickslot
    /// </summary>
    public sealed class DeathFlow
    {
        public static readonly string[] GameOverOptions = { "CheckpointRestart", "LoadSave", "Lobby" };
        public static readonly string[] PromptOptions = { "UseRevive", "Decline" };

        readonly Simulation _sim;
        public DeathStage Stage { get; private set; }
        public int Cursor { get; private set; }
        public float Timer { get; private set; }
        public string Message { get; private set; } = "";
        public string LastChoice { get; private set; } = "";
        public int Deaths { get; private set; }

        public DeathFlow(Simulation sim) => _sim = sim;

        DiagLogger Log => _sim.Log;
        public bool Active => Stage != DeathStage.None;

        string ReviveItemId()
        {
            var inv = _sim.Items?.Inventory;
            var prof = _sim.RingProfile;
            if (inv == null || prof == null) return null;
            foreach (var it in prof.Items)
                if (it.Effect == ItemEffectType.Revive && inv.Count(it.Id) > 0) return it.Id;
            return null;
        }

        public int ReviveItemCount()
        {
            var inv = _sim.Items?.Inventory;
            var prof = _sim.RingProfile;
            if (inv == null || prof == null) return 0;
            var n = 0;
            foreach (var it in prof.Items)
                if (it.Effect == ItemEffectType.Revive) n += inv.Count(it.Id);
            return n;
        }

        void To(DeathStage s, string reason, params (string, object)[] extra)
        {
            var before = Stage;
            Stage = s;
            Cursor = 0;
            var f = new List<(string, object)> { ("reviveItems", ReviveItemCount()), ("deaths", Deaths) };
            f.AddRange(extra);
            Log.Emit("death.flow", PlayerMotor.Actor, "", before.ToString(), s.ToString(), reason, f.ToArray());
        }

        /// <summary>Called every tick after combat.</summary>
        public void Step(float dt)
        {
            var c = _sim.Combat;
            if (c == null) return;
            if (Stage == DeathStage.None)
            {
                if (_sim.Player.State != MotorState.Dead) return;
                Deaths++;
                Timer = c.Profile.DeathMotionTime;
                Message = "";
                To(DeathStage.Motion, "DeathMotion(R-06-19, EXP-DEATH-01)", ("motionTime", Timer));
                return;
            }
            if (Stage == DeathStage.Motion)
            {
                Timer -= dt;
                if (Timer > 1e-4f) return;
                if (ReviveItemId() != null) To(DeathStage.RevivePrompt, "MotionDone:ReviveOwned(R-06-19)", ("options", string.Join(",", PromptOptions)));
                else To(DeathStage.GameOver, "MotionDone:NoReviveItem(R-06-19)", ("options", string.Join(",", GameOverOptions)));
            }
        }

        /// <summary>Input while the flow is active. Returns the outcome logged with the input.</summary>
        public string HandleEvent(in InputEvent e)
        {
            if (e.Edge != InputEdge.Press) return "death:" + Stage + ":release";
            switch (Stage)
            {
                case DeathStage.Motion:
                    return "death:Motion:Ignored(EXP-DEATH-01)";
                case DeathStage.RevivePrompt:
                    if (IsCursorMove(e.Action)) return MoveCursor(e.Action, PromptOptions.Length);
                    if (e.Action == LogicalAction.ButtonB) return Decline("ButtonB(EXP-DEATH-04)", e);
                    if (e.Action == LogicalAction.Jump)
                        return Cursor == 0 ? UseRevive(e) : Decline("Confirm:Decline", e);
                    return "death:RevivePrompt:Ignored";
                case DeathStage.GameOver:
                    if (IsCursorMove(e.Action)) return MoveCursor(e.Action, GameOverOptions.Length);
                    if (e.Action == LogicalAction.Jump) return Choose(e);
                    return "death:GameOver:Ignored";
            }
            return "death:None";
        }

        static bool IsCursorMove(LogicalAction a) =>
            a == LogicalAction.MoveUp || a == LogicalAction.MoveDown || a == LogicalAction.MoveLeft || a == LogicalAction.MoveRight;

        string MoveCursor(LogicalAction a, int n)
        {
            var step = a == LogicalAction.MoveUp || a == LogicalAction.MoveLeft ? -1 : 1;
            var before = Cursor;
            Cursor = Math.Max(0, Math.Min(n - 1, Cursor + step)); // no wrap (EXP-DEATH-04)
            var names = Stage == DeathStage.GameOver ? GameOverOptions : PromptOptions;
            Log.Emit("death.cursor", PlayerMotor.Actor, "", names[before], names[Cursor], "CursorMove(EXP-DEATH-04)", ("stage", Stage));
            return "death:" + Stage + ":cursor:" + names[Cursor];
        }

        string UseRevive(in InputEvent e)
        {
            var id = ReviveItemId();
            if (id == null)
            {
                To(DeathStage.GameOver, "ReviveItemGone(EXP-DEATH-03)");
                return "death:ReviveItemGone";
            }
            var before = _sim.Items.Inventory.Count(id);
            _sim.Items.Inventory.Remove(id, 1, new List<string>());
            LastChoice = "UseRevive";
            Log.Emit("death.revive", PlayerMotor.Actor, id, "RevivePrompt", "Revived", "UseRevive(R-06-19, EXP-DEATH-03)",
                ("inputEventId", e.EventId), ("itemCountBefore", before), ("itemCountAfter", _sim.Items.Inventory.Count(id)));
            Stage = DeathStage.None;
            Cursor = 0;
            _sim.Combat.Revive("Revive(R-06-19)");
            Log.Emit("death.flow", PlayerMotor.Actor, "", "RevivePrompt", "None", "Revived", ("reviveItems", ReviveItemCount()), ("deaths", Deaths));
            return "death:Revived";
        }

        string Decline(string why, in InputEvent e)
        {
            LastChoice = "Decline";
            To(DeathStage.GameOver, "ReviveDeclined:" + why + "(EXP-DEATH-02)", ("inputEventId", e.EventId), ("options", string.Join(",", GameOverOptions)));
            return "death:Declined";
        }

        string Choose(in InputEvent e)
        {
            var opt = GameOverOptions[Cursor];
            LastChoice = opt;
            Message = opt + ": return not implemented - BLOCKED_SPEC (U-08, TC-06-19b)";
            Log.Emit("death.menuSelect", PlayerMotor.Actor, "", "GameOver", "GameOver", "Selected:" + opt + "(R-06-19)",
                ("inputEventId", e.EventId), ("option", opt), ("returnPerformed", false), ("tc19b", "BLOCKED_SPEC(U-08)"));
            return "death:GameOver:select:" + opt;
        }
    }
}

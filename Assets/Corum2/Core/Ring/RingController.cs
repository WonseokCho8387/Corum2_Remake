using System;
using System.Collections.Generic;

namespace Corum2.Core
{
    public enum RingMode
    {
        Closed,
        Browse,
        /// <summary>Y fixed a target; the next D-pad press assigns it (R-02-07).</summary>
        Registering,
        /// <summary>X shows the selected item's description; the ring stays open (spec 4.2 proposal, U-03).</summary>
        Detail,
        /// <summary>B asked to discard; A confirms, B cancels (R-02-08).</summary>
        ConfirmDiscard,
    }

    /// <summary>
    /// Ring inventory (P0-02). Runs at real time between simulation ticks so the UI keeps normal speed while the world
    /// is slowed (spec 4.2 proposal). Item effects never run here: uses go to ItemUseSystem, which runs them in Step.
    ///
    /// Confirmed rules implemented: R-02-03 open/close with LB, slow world while open; R-02-04 cursor follows the stick,
    /// neutral = centre and no selection; R-02-05 A use, Y register, X detail, B discard, LT/RT page, stick not used
    /// for movement while open; R-02-07 Y fixes the target, D-pad assigns, D-pad in registration never uses an item;
    /// R-02-08 discard asks again, quest items blocked, ring kept on cancel/block; R-02-09 auto-close after use,
    /// registration and confirmed discard only; R-02-11 open on ground, in the air and during attacks and hit reaction.
    ///
    /// Experimental choices (U-03 unless noted):
    ///   EXP-RING-02 8 slots per page, one per 8-way sector, slot 0 = up, clockwise
    ///   EXP-RING-03 opening the ring releases held movement for the game; closing re-presses the still-held direction
    ///   EXP-RING-04 LT/RT wrap around the pages
    ///   EXP-RING-05 B in registration cancels registration (ring stays); X or B closes the description
    ///   EXP-RING-06 being hit keeps the ring open; death closes it
    ///   EXP-RING-07 discard removes one unit
    ///   EXP-RING-08 registering onto a used quickslot replaces it (spec 4.2 proposal); the same item may sit in several slots
    ///   (EXP-RING-09 withdrawn in WP-03-R1: R-02-07 requires the stick release before the D-pad, WP03-F01)
    ///   EXP-RING-10 D-pad outside registration is ignored while the ring is open (U-03 / TC-UI-12 open branch)
    ///   EXP-RING-11 a rejected use (cooldown, quest item, pending use) keeps the ring open with feedback
    /// </summary>
    public sealed class RingController
    {
        readonly RingProfile _p;
        readonly ItemUseSystem _items;
        readonly DiagLogger _log;
        readonly Func<long> _tick;

        public RingMode Mode { get; private set; } = RingMode.Closed;
        public bool IsOpen => Mode != RingMode.Closed;
        public int Page { get; private set; }
        public int SelectedSlot { get; private set; } = -1;
        /// <summary>Item that was selected when the ring last closed (P1 MAG-98: plain B outside the ring uses it).</summary>
        public string LastSelectedItem { get; private set; } = "";
        public float CursorX { get; private set; }
        public float CursorY { get; private set; }
        /// <summary>Item fixed by Y (registration), shown in Detail, or asked about in ConfirmDiscard.</summary>
        public string TargetItem { get; private set; } = "";
        public string Feedback { get; private set; } = "";
        public int OpenCount { get; private set; }
        public string LastCloseReason { get; private set; } = "";
        /// <summary>Registration only: the stick has been at neutral since Y fixed the target (R-02-07 step 3).</summary>
        public bool ReleasedSinceRegister { get; private set; }

        Inventory Inv => _items.Inventory;

        public RingController(RingProfile profile, ItemUseSystem items, DiagLogger log, Func<long> tick)
        {
            _p = profile;
            _items = items;
            _log = log;
            _tick = tick;
        }

        public string SelectedItem => SelectedSlot < 0 ? null : Inv.At(Page, SelectedSlot);

        /// <summary>Ring slot for a stick sector: slot 0 = Up, then clockwise (EXP-RING-02).</summary>
        public static int SlotOf(Dir8 d)
        {
            switch (d)
            {
                case Dir8.Up: return 0;
                case Dir8.UpRight: return 1;
                case Dir8.Right: return 2;
                case Dir8.DownRight: return 3;
                case Dir8.Down: return 4;
                case Dir8.DownLeft: return 5;
                case Dir8.Left: return 6;
                case Dir8.UpLeft: return 7;
                default: return -1;
            }
        }

        void Emit(string type, string before, string after, string reason, double realTime, params (string, object)[] fields)
        {
            _log.RealTime = realTime;
            var all = new List<(string, object)>(fields)
            {
                ("mode", Mode), ("page", Page), ("pageCount", Inv.PageCount), ("selectedSlot", SelectedSlot),
                ("selectedItem", SelectedItem ?? ""), ("target", TargetItem), ("lastSimTick", _tick()),
            };
            _log.Emit(type, PlayerMotor.Actor, "", before, after, reason, all.ToArray());
        }

        void SetMode(RingMode m, string reason, double realTime)
        {
            if (m == Mode) return;
            var before = Mode;
            Mode = m;
            Emit("ring.mode", before.ToString(), m.ToString(), reason, realTime);
        }

        public void Open(string reason, double realTime)
        {
            if (IsOpen) return;
            OpenCount++;
            Page = Math.Min(Page, Inv.PageCount - 1);
            TargetItem = "";
            Feedback = "";
            SelectedSlot = -1;
            Mode = RingMode.Browse;
            Emit("ring.open", "Closed", "Browse", reason, realTime, ("openCount", OpenCount), ("slowScale", _p.SlowScale),
                ("inventory", Inv.Describe()), ("quickslots", Inv.DescribeQuickslots()));
        }

        public void Close(string reason, double realTime)
        {
            if (!IsOpen) return;
            var before = Mode;
            if (SelectedItem != null) LastSelectedItem = SelectedItem;
            Mode = RingMode.Closed;
            LastCloseReason = reason;
            TargetItem = "";
            SelectedSlot = -1;
            CursorX = CursorY = 0f;
            Emit("ring.close", before.ToString(), "Closed", reason, realTime);
        }

        /// <summary>Stick sample while open (R-02-04): the cursor is the stick vector; neutral clears the selection.</summary>
        public void Cursor(float x, float y, double realTime)
        {
            if (!IsOpen) return;
            CursorX = x;
            CursorY = y;
            var slot = SlotOf(Dir8Util.FromStick(x, y, _p.CursorDeadzone));
            if (Mode == RingMode.Registering && slot < 0 && !ReleasedSinceRegister)
            {
                ReleasedSinceRegister = true;
                Emit("ring.release", "Held", "Released", "StickReleased(R-02-07)", realTime, ("stick", new Vec2(x, y)));
            }
            if (slot == SelectedSlot) return;
            var before = SelectedSlot;
            SelectedSlot = slot;
            Emit("ring.cursor", before.ToString(), slot.ToString(), slot < 0 ? "Neutral(R-02-04)" : "Sector", realTime,
                ("stick", new Vec2(x, y)));
        }

        /// <summary>A button press while open. Returns the outcome text for the input route log.</summary>
        public string Press(LogicalAction a, double realTime)
        {
            if (!IsOpen) return "ringClosed";
            if (QuickSlotUtil.TryFromAction(a, out var q)) return Dpad(q, realTime);
            switch (Mode)
            {
                case RingMode.ConfirmDiscard:
                    if (a == LogicalAction.Jump) return ConfirmDiscard(realTime);
                    if (a == LogicalAction.ButtonB)
                    {
                        Feedback = "Discard cancelled";
                        Emit("ring.discard", "Confirm", "Cancelled", "Cancel(R-02-08)", realTime, ("item", TargetItem));
                        TargetItem = "";
                        SetMode(RingMode.Browse, "DiscardCancelled", realTime);
                        return "discardCancelled";
                    }
                    return Ignore(a, "WaitingForDiscardConfirm", realTime);
                case RingMode.Detail:
                    if (a == LogicalAction.Attack || a == LogicalAction.ButtonB)
                    {
                        TargetItem = "";
                        SetMode(RingMode.Browse, "DetailClosed(EXP-RING-05)", realTime);
                        return "detailClosed";
                    }
                    break;
                case RingMode.Registering:
                    if (a == LogicalAction.ButtonB)
                    {
                        Feedback = "Registration cancelled";
                        TargetItem = "";
                        SetMode(RingMode.Browse, "RegistrationCancelled(EXP-RING-05)", realTime);
                        return "registrationCancelled";
                    }
                    if (a != LogicalAction.PagePrev && a != LogicalAction.PageNext)
                        return Ignore(a, "Registering:WaitingForDpad(R-02-07)", realTime);
                    break;
            }
            switch (a)
            {
                case LogicalAction.PagePrev:
                case LogicalAction.PageNext:
                    return TurnPage(a == LogicalAction.PageNext ? 1 : -1, realTime);
                case LogicalAction.Jump:
                    return Use(realTime);
                case LogicalAction.SpecialAttack:
                    return StartRegistration(realTime);
                case LogicalAction.Attack:
                    return ShowDetail(realTime);
                case LogicalAction.ButtonB:
                    return AskDiscard(realTime);
                default:
                    return Ignore(a, "NotARingAction", realTime);
            }
        }

        string Ignore(LogicalAction a, string why, double realTime)
        {
            Emit("ring.ignore", Mode.ToString(), Mode.ToString(), why, realTime, ("action", a));
            return "ignored:" + why;
        }

        string TurnPage(int d, double realTime)
        {
            var n = Inv.PageCount;
            var before = Page;
            Page = ((Page + d) % n + n) % n; // EXP-RING-04
            Emit("ring.page", before.ToString(), Page.ToString(), d > 0 ? "Next(RT)" : "Prev(LT)", realTime);
            return "page" + Page;
        }

        string Use(double realTime)
        {
            if (Mode == RingMode.Registering) return Ignore(LogicalAction.Jump, "Registering", realTime);
            var item = SelectedItem;
            if (item == null) return Ignore(LogicalAction.Jump, SelectedSlot < 0 ? "NoSelection(R-02-04)" : "EmptySlot", realTime);
            var r = _items.Request(item, UseSource.Ring, realTime, "ring.A");
            if (r.Decision == UseDecision.Rejected)
            {
                Feedback = "Cannot use: " + r.Reason;
                Emit("ring.use", Mode.ToString(), Mode.ToString(), "Rejected:" + r.Reason + "(EXP-RING-11)", realTime, ("item", item), ("requestId", r.RequestId));
                return "useRejected";
            }
            Emit("ring.use", Mode.ToString(), "Closed", r.Decision.ToString(), realTime, ("item", item), ("requestId", r.RequestId));
            Close(r.Decision == UseDecision.Reserved ? "UseReserved(R-05-05/R-02-09)" : "Used(R-02-09)", realTime);
            return "use" + r.Decision;
        }

        string StartRegistration(double realTime)
        {
            var item = SelectedItem;
            if (item == null) return Ignore(LogicalAction.SpecialAttack, "NoSelection", realTime);
            TargetItem = item;
            ReleasedSinceRegister = false;
            Feedback = "Release the stick, then press a D-pad direction";
            SetMode(RingMode.Registering, "TargetFixed(R-02-07)", realTime);
            return "registering";
        }

        string Dpad(QuickSlot q, double realTime)
        {
            if (Mode != RingMode.Registering)
            {
                // TC-UI-12: outside registration the in-ring D-pad behaviour is undecided (U-03).
                return Ignore(QuickSlotUtil.ToAction(q), "DpadOutsideRegistration(EXP-RING-10, U-03)", realTime);
            }
            if (!ReleasedSinceRegister)
            {
                // R-02-07: select -> Y -> release the stick -> D-pad. Before the release nothing is assigned or used.
                Feedback = "Release the stick first";
                return Ignore(QuickSlotUtil.ToAction(q), "Registering:StickNotReleased(R-02-07)", realTime);
            }
            var i = (int)q;
            var replaced = Inv.Quickslots[i];
            Inv.Quickslots[i] = TargetItem;
            Emit("quickslot.assign", replaced, TargetItem, "Registered(R-02-07)" + (replaced.Length > 0 ? ";Replaced(EXP-RING-08)" : ""), realTime,
                ("slot", q), ("stickNeutral", SelectedSlot < 0), ("stickReleasedSinceY", ReleasedSinceRegister),
                ("quickslots", Inv.DescribeQuickslots()), ("itemConsumed", false));
            Close("Registered(R-02-09)", realTime);
            return "registered:" + q;
        }

        string ShowDetail(double realTime)
        {
            var item = SelectedItem;
            if (item == null) return Ignore(LogicalAction.Attack, "NoSelection", realTime);
            TargetItem = item;
            SetMode(RingMode.Detail, "Detail(X)", realTime);
            return "detail";
        }

        string AskDiscard(double realTime)
        {
            var item = SelectedItem;
            if (item == null) return Ignore(LogicalAction.ButtonB, "NoSelection", realTime);
            var def = _p.FindItem(item);
            if (def != null && !def.Discardable)
            {
                Feedback = "Quest items cannot be discarded";
                Emit("ring.discard", Mode.ToString(), Mode.ToString(), "Blocked:Quest(R-02-08)", realTime, ("item", item), ("count", Inv.Count(item)));
                return "discardBlocked";
            }
            TargetItem = item;
            Feedback = "Discard? A = yes, B = no";
            SetMode(RingMode.ConfirmDiscard, "DiscardAsked(R-02-08)", realTime);
            return "discardAsked";
        }

        string ConfirmDiscard(double realTime)
        {
            var item = TargetItem;
            var before = Inv.Count(item);
            var cleared = new List<string>();
            var ok = Inv.Remove(item, 1, cleared); // EXP-RING-07
            Emit("ring.discard", "Confirm", ok ? "Discarded" : "Missing", ok ? "Confirmed(R-02-08)" : "NotOwned", realTime,
                ("item", item), ("countBefore", before), ("countAfter", Inv.Count(item)), ("quickslotsCleared", string.Join(",", cleared)),
                ("inventory", Inv.Describe()));
            Close("Discarded(R-02-09)", realTime);
            return "discarded";
        }
    }

    /// <summary>
    /// Decides which consumer gets each input event (appendix 3.2 item 3: one owner per event). Called in real time
    /// order between ticks. LB toggles the ring; while it is open every press goes to the ring and the stick is the
    /// cursor; quickslot presses with the ring closed go to the item system; everything else goes to the game.
    /// </summary>
    public sealed class RingInputRouter
    {
        readonly RingController _ring;
        readonly ItemUseSystem _items;
        readonly DiagLogger _log;
        readonly Func<MotorState> _playerState;
        readonly Func<long> _nextEventId;
        int _rawH;
        int _rawV;

        public RingInputRouter(RingController ring, ItemUseSystem items, DiagLogger log, Func<MotorState> playerState, Func<long> nextEventId)
        {
            _ring = ring;
            _items = items;
            _log = log;
            _playerState = playerState;
            _nextEventId = nextEventId;
        }

        /// <summary>Direction the game should see: neutral while the ring is open (R-02-05).</summary>
        public Dir8 GameDir => _ring.IsOpen ? Dir8.None : Dir8Util.FromComponents(_rawH, _rawV);

        void Route(in InputEvent e, string consumer, string outcome)
        {
            _log.RealTime = e.RawTime;
            _log.Emit("input.route", PlayerMotor.Actor, "", "", "", outcome, ("inputEventId", e.EventId), ("action", e.Action),
                ("edge", e.Edge), ("device", e.Device), ("consumer", consumer), ("ringMode", _ring.Mode), ("requestTime", e.RawTime),
                ("playerState", _playerState()));
        }

        void TrackDir(in InputEvent e)
        {
            var on = e.Edge == InputEdge.Press;
            switch (e.Action)
            {
                case LogicalAction.MoveLeft: if (on) _rawH = -1; else if (_rawH < 0) _rawH = 0; break;
                case LogicalAction.MoveRight: if (on) _rawH = 1; else if (_rawH > 0) _rawH = 0; break;
                case LogicalAction.MoveUp: if (on) _rawV = 1; else if (_rawV > 0) _rawV = 0; break;
                case LogicalAction.MoveDown: if (on) _rawV = -1; else if (_rawV < 0) _rawV = 0; break;
            }
        }

        static bool IsMove(LogicalAction a) =>
            a == LogicalAction.MoveLeft || a == LogicalAction.MoveRight || a == LogicalAction.MoveUp || a == LogicalAction.MoveDown;

        void Synth(LogicalAction a, InputEdge edge, double t, List<InputEvent> toGame, string why)
        {
            var e = new InputEvent(_nextEventId(), a, edge, t, "router", "router/" + why);
            toGame.Add(e);
            Route(e, "game", "Synthetic:" + why + "(EXP-RING-03)");
        }

        /// <summary>Routes one event; game-bound events (and synthetic movement edges) are appended to toGame.</summary>
        public void Handle(in InputEvent e, List<InputEvent> toGame)
        {
            if (IsMove(e.Action)) TrackDir(e);

            // R-06-19: while the player is dead the death prompt / game-over menu owns the input (EXP-DEATH-04).
            if (_playerState() == MotorState.Dead && !_ring.IsOpen && e.Action != LogicalAction.RingToggle)
            {
                toGame.Add(e);
                Route(e, "game", "Forward:DeathFlow(R-06-19)");
                return;
            }

            if (e.Action == LogicalAction.RingToggle)
            {
                if (e.Edge == InputEdge.Release) { Route(e, "ring", "ToggleRelease"); return; }
                if (_ring.IsOpen)
                {
                    _ring.Close("Manual(LB)", e.RawTime);
                    Route(e, "ring", "Close");
                    // Movement still held resumes for the game.
                    if (_rawH != 0) Synth(_rawH > 0 ? LogicalAction.MoveRight : LogicalAction.MoveLeft, InputEdge.Press, e.RawTime, toGame, "ResumeHeld");
                    if (_rawV != 0) Synth(_rawV > 0 ? LogicalAction.MoveUp : LogicalAction.MoveDown, InputEdge.Press, e.RawTime, toGame, "ResumeHeld");
                    return;
                }
                if (_playerState() == MotorState.Dead) { Route(e, "ring", "Refused:PlayerDead"); return; }
                _ring.Open("LB(R-02-03)", e.RawTime);
                Route(e, "ring", "Open");
                if (_rawH != 0) Synth(_rawH > 0 ? LogicalAction.MoveRight : LogicalAction.MoveLeft, InputEdge.Release, e.RawTime, toGame, "ReleaseHeldOnOpen");
                if (_rawV != 0) Synth(_rawV > 0 ? LogicalAction.MoveUp : LogicalAction.MoveDown, InputEdge.Release, e.RawTime, toGame, "ReleaseHeldOnOpen");
                return;
            }

            if (_ring.IsOpen)
            {
                if (IsMove(e.Action)) { Route(e, "ring", "Cursor(R-02-05)"); return; }
                if (e.Edge == InputEdge.Release)
                {
                    // Releases of game buttons pressed before the ring opened still reach the game so nothing stays held.
                    toGame.Add(e);
                    Route(e, "game", "ReleaseWhileRingOpen(EXP-RING-03)");
                    return;
                }
                var outcome = _ring.Press(e.Action, e.RawTime);
                Route(e, "ring", outcome);
                if (!_ring.IsOpen)
                {
                    if (_rawH != 0) Synth(_rawH > 0 ? LogicalAction.MoveRight : LogicalAction.MoveLeft, InputEdge.Press, e.RawTime, toGame, "ResumeHeld");
                    if (_rawV != 0) Synth(_rawV > 0 ? LogicalAction.MoveUp : LogicalAction.MoveDown, InputEdge.Press, e.RawTime, toGame, "ResumeHeld");
                }
                return;
            }

            if (QuickSlotUtil.TryFromAction(e.Action, out var q))
            {
                if (e.Edge == InputEdge.Release) { Route(e, "quickslot", "Release"); return; }
                var item = _items.Inventory.Quickslots[(int)q];
                if (string.IsNullOrEmpty(item)) { Route(e, "quickslot", "EmptySlot:" + q); return; }
                var r = _items.Request(item, UseSource.Quickslot, e.RawTime, "quickslot." + q);
                Route(e, "quickslot", r.Decision + (r.Reason != null && r.Reason.Length > 0 ? ":" + r.Reason : "") + ":" + q);
                return;
            }

            toGame.Add(e);
            Route(e, "game", "Forward");
        }

        /// <summary>Called when the simulation reports death (EXP-RING-06).</summary>
        public void OnPlayerDead(double realTime, List<InputEvent> toGame)
        {
            if (_ring.IsOpen) _ring.Close("PlayerDead(EXP-RING-06)", realTime);
        }
    }
}

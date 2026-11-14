using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    /// <summary>
    /// P1 magic (MAG-01~107, BAL-10~12). Flow: recognise (combat context only) -> longest motion -> several spells in the
    /// same tick = all refused (MAG-106) -> learned / state / summon kind / MP checks (fail = no fallback to X/A/B,
    /// MAG-103/104; un-castable state = discarded, MAG-105) -> cast motion without spending -> at the effect time the
    /// resource is re-checked and spent once, then the effect is created (MAG-107, BAL-10); a legal zero-change effect
    /// still spends (BAL-11). Scrolls: never need the spell learned, MP 0, one scroll, same effect (MAG-02/03/34/42).
    /// Temporary rules: EXP-P1-AIM-01 aim = held 8-way direction at the commit, none = facing; EXP-P1-CAST-01 castable
    /// states = P0 EXP-MAGIC-01; EXP-P1-SWORD-01 same sword recast refreshes; EXP-P1-SUMMON-01 summon AI.
    /// </summary>
    public sealed class P1MagicSystem
    {
        readonly P1Runtime _rt;
        P1Profile P => _rt.P;
        P1Progress G => _rt.G;
        GrowthRules R => _rt.Prog.Rules;
        CombatSystem C => _rt.Sim.Combat;
        PlayerMotor Pl => _rt.Sim.Player;
        DiagLogger Log => _rt.Sim.Log;

        public readonly P1CommandRecognizer Recognizer;
        public readonly List<P1Projectile> Projectiles = new List<P1Projectile>();
        public readonly List<P1Explosion> Explosions = new List<P1Explosion>();
        public readonly List<P1Wall> Walls = new List<P1Wall>();
        public readonly List<P1Dot> Dots = new List<P1Dot>();
        public P1Summon Summon;
        float _hotLeft, _hotTimer, _hotPerSec;
        readonly List<(float delay, Action fire)> _queued = new List<(float, Action)>();
        long _nextProj = 1, _nextCast = 1;
        readonly Dictionary<string, AttackDefinition> _defs = new Dictionary<string, AttackDefinition>();
        readonly HashSet<long> _multiRefused = new HashSet<long>();
        readonly HashSet<long> _waveSpawned = new HashSet<long>();

        sealed class Cast
        {
            public long Id;
            public SpellDef Spell;
            public string Source = "";
            public string ScrollItem = "";
            public long AttackInstanceId;
            public long InputEventId;
        }

        Cast _cast;
        public string LastResult = "";
        public int Commits, Refusals;

        public P1MagicSystem(P1Runtime rt)
        {
            _rt = rt;
            Recognizer = new P1CommandRecognizer(rt.P);
        }

        void Emit(string type, string reason, params (string, object)[] f) => Log.Emit(type, PlayerMotor.Actor, "", "", "", reason, f);

        public MagicDefinition CastDef(SpellDef s) => new MagicDefinition
        {
            Id = s.Id, Effect = MagicEffect.External, CastTime = s.CastTime, Recovery = s.Recovery, Power = 0f, Knockback = 1f,
        };

        public int CommandCost(SpellDef s) => DamageCalc.CommandMp(R.SpellCost(G, s.Id), s.MinMp, _rt.Buffs.MpCost, MpCap());

        float MpCap()
        {
            foreach (var b in P.Buffs) if (b.Family == "MpCost") return b.FamilyCap;
            return 0.3f;
        }

        public bool Learned(SpellDef s) => s.Kind == SpellKind.Missile ? G.Has("MagicMissile") : G.Has(s.Id);

        // ================================================================ input

        /// <summary>MAG-106: before the tick's events run, find presses whose spells would complete; two or more different
        /// spells in one tick refuse them all.</summary>
        public void PreScan(List<InputEvent> events, double now)
        {
            _multiRefused.Clear();
            if (!_rt.CombatContext) return;
            var rec = Recognizer.Clone();
            var held = Pl.HeldDir;
            int h = Pl.HeldH, v = Pl.HeldV;
            var hits = new List<(long id, string spell)>();
            foreach (var e in events)
            {
                if (e.Action == LogicalAction.MoveLeft || e.Action == LogicalAction.MoveRight || e.Action == LogicalAction.MoveUp || e.Action == LogicalAction.MoveDown)
                {
                    var on = e.Edge == InputEdge.Press;
                    if (e.Action == LogicalAction.MoveLeft) h = on ? -1 : (h == -1 ? 0 : h);
                    if (e.Action == LogicalAction.MoveRight) h = on ? 1 : (h == 1 ? 0 : h);
                    if (e.Action == LogicalAction.MoveUp) v = on ? 1 : (v == 1 ? 0 : v);
                    if (e.Action == LogicalAction.MoveDown) v = on ? -1 : (v == -1 ? 0 : v);
                    rec.Observe(Dir8Util.FromComponents(h, v), now, Pl.Facing);
                    continue;
                }
                if (e.Edge != InputEdge.Press || !P1CommandRecognizer.TryButton(e.Action, out var b)) continue;
                var s = rec.Match(b, now);
                if (s != null) hits.Add((e.EventId, s.Id));
            }
            var distinct = new HashSet<string>();
            foreach (var x in hits) distinct.Add(x.spell);
            if (distinct.Count >= 2)
            {
                foreach (var x in hits) _multiRefused.Add(x.id);
                Emit("p1.magic.refuse", "MultipleSpellsSameTick(MAG-106)", ("spells", string.Join(",", distinct)), ("events", hits.Count));
            }
        }

        public void Observe(Dir8 held, double now)
        {
            if (_rt.CombatContext) Recognizer.Observe(held, now, Pl.Facing);
        }

        public void ClearBuffer(string reason)
        {
            if (Recognizer.History.Count > 0) Emit("p1.magic.bufferCleared", reason, ("history", Recognizer.Describe(_rt.Sim.GameTime)));
            Recognizer.Clear(Pl.HeldDir);
        }

        /// <summary>Called for each game input event before P0 handling. Null = not a spell command (normal action runs).</summary>
        public string TryCommand(in InputEvent e)
        {
            if (e.Edge != InputEdge.Press || !P1CommandRecognizer.TryButton(e.Action, out var button)) return null;
            if (!_rt.CombatContext) return null;
            var now = _rt.Sim.GameTime;
            if (_multiRefused.Contains(e.EventId))
            {
                // MAG-106: every press of a same-tick multi-spell completion is refused, none falls back to X/A/B
                Recognizer.Clear(Pl.HeldDir);
                Refusals++;
                LastResult = "refused:MultipleSpellsSameTick";
                Emit("p1.magic.refuse", "MultipleSpellsSameTick(MAG-106)", ("inputEventId", e.EventId), ("consumed", false));
                return "rejected:p1magic:MultipleSpellsSameTick(MAG-106)";
            }
            var s = Recognizer.Match(button, now, out var motionFacing);
            if (s == null) return null;
            Emit("p1.magic.command", "Matched(MAG-92~94)", ("spellId", s.Id), ("button", button), ("inputEventId", e.EventId), ("facing", Pl.Facing),
                ("history", Recognizer.Describe(now)));
            Recognizer.Clear(Pl.HeldDir);
            if (!Learned(s)) return Refuse(s, "NotLearned(MAG-06/41, no fallback MAG-103/104)", e.EventId);
            var summonRefuse = SummonConflict(s);
            if (summonRefuse != null) return Refuse(s, summonRefuse, e.EventId);
            var cost = CommandCost(s);
            if (G.CurMp < cost) return Refuse(s, "MpShort:" + G.CurMp + "<" + cost + "(MAG-54)", e.EventId);
            if (_cast != null && C.Attack != null && C.Attack.InstanceId == _cast.AttackInstanceId) return Refuse(s, "AlreadyCasting(MAG-105)", e.EventId);
            var faceBefore = Pl.Facing;
            Pl.Facing = motionFacing; // EXP-P1-CAST-02: the cast faces the front the motion was read in
            var rej = C.CastMagic(CastDef(s), "p1command:" + s.Id, e.EventId);
            if (rej != null)
            {
                Pl.Facing = faceBefore;
                return Refuse(s, "StateCannotCast:" + rej + "(MAG-105 discarded)", e.EventId);
            }
            _cast = new Cast { Id = _nextCast++, Spell = s, Source = "command", AttackInstanceId = C.Attack.InstanceId, InputEventId = e.EventId };
            Emit("p1.magic.castStart", "NoSpendAtStart(MAG-107)", ("spellId", s.Id), ("castId", _cast.Id), ("costNow", cost), ("mp", G.CurMp));
            LastResult = "cast:" + s.Id;
            return "p1magic:" + s.Id;
        }

        string Refuse(SpellDef s, string why, long eventId)
        {
            Refusals++;
            LastResult = "refused:" + why;
            Emit("p1.magic.refuse", why, ("spellId", s.Id), ("inputEventId", eventId), ("mp", G.CurMp), ("consumed", false));
            return "rejected:p1magic:" + why;
        }

        string SummonConflict(SpellDef s)
        {
            if (s.Kind != SpellKind.Archangel && s.Kind != SpellKind.Demon) return null;
            if (Summon != null && Summon.Kind != s.Kind) return "OtherSummonActive:" + Summon.SpellId + "(MAG-83/84)";
            return null;
        }

        /// <summary>Scroll use from the ring / quickslot (IItemExtension.CastScroll path).</summary>
        public string CastScroll(string spellId, string itemId, long requestId)
        {
            var s = P.Spell(spellId);
            if (s == null) return "UnknownSpell";
            var sc = SummonConflict(s);
            if (sc != null) { Refuse(s, sc, requestId); return sc; }
            var rej = C.CastMagic(CastDef(s), "p1scroll:" + itemId, requestId);
            if (rej != null) return rej;
            _cast = new Cast { Id = _nextCast++, Spell = s, Source = "scroll", ScrollItem = itemId, AttackInstanceId = C.Attack.InstanceId, InputEventId = requestId };
            Emit("p1.magic.castStart", "ScrollNoSpendAtStart(MAG-107)", ("spellId", s.Id), ("castId", _cast.Id), ("scroll", itemId),
                ("count", _rt.Sim.Items?.Inventory.Count(itemId) ?? -1));
            return null;
        }

        // ================================================================ commit

        /// <summary>CombatSystem.ExternalSpellEffect: the cast reached its effect time.</summary>
        public void OnEffectTime(PlayerAttack a)
        {
            var c = _cast;
            if (c == null || a.InstanceId != c.AttackInstanceId) return;
            _cast = null;
            var s = c.Spell;
            var sc = SummonConflict(s);
            if (sc != null) { CommitFail(c, sc); return; }
            int cost = 0;
            if (c.Source == "command")
            {
                cost = CommandCost(s);
                if (G.CurMp < cost) { CommitFail(c, "CommitMpRecheck:" + G.CurMp + "<" + cost + "(BAL-10)"); return; }
                G.CurMp -= cost;
            }
            else
            {
                var inv = _rt.Sim.Items?.Inventory;
                if (inv == null || !inv.Remove(c.ScrollItem, 1)) { CommitFail(c, "CommitScrollRecheck:none(BAL-10)"); return; }
            }
            Commits++;
            var aim = AimAngle();
            Emit("p1.magic.commit", "SpendOnce(MAG-107)", ("spellId", s.Id), ("castId", c.Id), ("source", c.Source), ("mpCost", cost), ("mpAfter", G.CurMp),
                ("scroll", c.ScrollItem), ("scrollLeft", c.ScrollItem != "" ? _rt.Sim.Items.Inventory.Count(c.ScrollItem) : -1), ("aimDeg", aim));
            LastResult = "commit:" + s.Id;
            Effect(s, c.Id, aim);
        }

        void CommitFail(Cast c, string why)
        {
            Refusals++;
            LastResult = "commitFail:" + why;
            Emit("p1.magic.commitFail", why, ("spellId", c.Spell.Id), ("castId", c.Id), ("source", c.Source), ("consumed", false));
        }

        /// <summary>Called each tick: a cast whose motion ended without reaching its effect (guard / hit) spends nothing.</summary>
        void CheckCancelled()
        {
            if (_cast == null) return;
            if (C.Attack == null || C.Attack.InstanceId != _cast.AttackInstanceId)
            {
                Emit("p1.magic.cancelled", "MotionEndedBeforeEffect(MAG-107: no spend)", ("spellId", _cast.Spell.Id), ("castId", _cast.Id), ("source", _cast.Source));
                _cast = null;
            }
        }

        float AimAngle()
        {
            var h = Pl.HeldH;
            var v = Pl.HeldV;
            if (h == 0 && v == 0) return Pl.Facing >= 0 ? 0f : 180f;
            return (float)(Math.Atan2(v, h) * 180.0 / Math.PI);
        }

        Vec2 Muzzle() => new Vec2(Pl.Position.X + Pl.Facing * 0.5f, Pl.Position.Y + 1.0f);

        float Ax(SpellDef s, string axis) => G.Axis(s.Id + "." + axis);

        // ================================================================ effects

        void Effect(SpellDef s, long castId, float aim)
        {
            switch (s.Kind)
            {
                case SpellKind.Missile: FireMissiles(s, castId, aim); break;
                case SpellKind.LightOrb:
                {
                    FireOrb(s, castId, aim);
                    var extra = (int)Ax(s, "extraShots");
                    for (var i = 1; i <= extra; i++) _queued.Add((s.P("extraShotInterval", 0.12f) * i, () => FireOrb(s, castId, aim)));
                    break;
                }
                case SpellKind.DarkOrb: FireOrb(s, castId, aim); break;
                case SpellKind.FireBall: FireOrb(s, castId, aim); break;
                case SpellKind.FireWall: PlaceWalls(s, castId); break;
                case SpellKind.Heal:
                {
                    var before = C.PlayerHp;
                    C.SetPlayerHp(C.PlayerHp + s.P("heal", 5) + Ax(s, "heal"), "P1Heal(MAG-49/73)");
                    if (Ax(s, "cure") > 0) { _rt.Fx.Statuses.Remove("Poison"); _rt.Fx.Statuses.Remove("Confusion"); }
                    var hot = Ax(s, "hot");
                    if (hot > 0) { _hotLeft = hot; _hotTimer = 1f; _hotPerSec = s.P("hotPerSecond", 1); }
                    Emit("p1.magic.heal", "FixedHeal(MAG-49)", ("hpBefore", before), ("hpAfter", C.PlayerHp), ("hot", hot));
                    break;
                }
                case SpellKind.Shield:
                    _rt.Fx.MagicShield = new ShieldPool { Id = "MagicShield", Remaining = (int)(s.P("absorb", 10) + Ax(s, "absorb")), Time = s.P("duration", 12) + Ax(s, "duration") };
                    _rt.Fx.ShieldStatusBlock = Ax(s, "statusBlock") > 0;
                    Emit("p1.magic.shield", "ReplaceShield(MAG-76)", ("absorb", _rt.Fx.MagicShield.Remaining), ("time", _rt.Fx.MagicShield.Time));
                    break;
                case SpellKind.LightSword:
                case SpellKind.DarkSword:
                {
                    var prev = _rt.Fx.Sword?.SpellId ?? "";
                    var light = s.Kind == SpellKind.LightSword;
                    _rt.Fx.Sword = new SwordState
                    {
                        SpellId = s.Id, Remaining = s.P("duration", 12) + Ax(s, "duration"),
                        Bonus = light ? s.P("bonus", 3) + Ax(s, "bonus") : 0, LastHit = light ? Ax(s, "lastHit") : 0, Wave = light ? Ax(s, "wave") : 0,
                        Rate = light ? 0 : s.P("lifesteal", 0.25f) + Ax(s, "rate"), KillHeal = light ? 0 : Ax(s, "killHeal"), ComboRate = light ? 0 : Ax(s, "comboRate"),
                    };
                    Emit("p1.magic.sword", prev == "" ? "Enchant" : prev == s.Id ? "RefreshSame(EXP-P1-SWORD-01)" : "ReplaceOther(MAG-77)", ("spellId", s.Id), ("previous", prev),
                        ("time", _rt.Fx.Sword.Remaining));
                    break;
                }
                case SpellKind.Archangel:
                case SpellKind.Demon:
                    SummonOrExtend(s);
                    break;
            }
        }

        AttackDefinition Def(string spellId, bool physical = false)
        {
            var key = spellId + (physical ? ":phys" : "");
            if (_defs.TryGetValue(key, out var d)) return d;
            var s = P.Spell(spellId);
            d = new AttackDefinition
            {
                Id = "p1." + spellId, Kind = physical ? AttackKind.GroundNormal : AttackKind.Spell, DamageType = physical ? DamageType.Physical : DamageType.Magic,
                Knockback = s?.P("knockback", 3) ?? 3, Hitstop = 0.03f, NoHitbox = true, Sword = false,
            };
            _defs[key] = d;
            return d;
        }

        /// <summary>One independent hit event (MAG-31): own damage and reaction (unless a damage-only tick, MAG-65).</summary>
        void HitEnemy(EnemyActor en, string spellId, float pre, bool damageOnly, string how, bool physical = false)
        {
            if (en == null || !en.Targetable) return;
            var a = C.NewExternalAttack(Def(spellId, physical), how);
            a.P1PreBuff = pre;
            a.DamageOnly = damageOnly;
            C.ApplyExternalHit(a, en, how);
        }

        void FireMissiles(SpellDef s, long castId, float aim)
        {
            var n = R.MissileProjectiles(G);
            var dmg = R.MissilePermanentDamage(G);
            var homingCards = (int)G.Axis("mm.homing");
            R.Homing(homingCards, out var half, out var range, out var turn);
            var spread = s.P("spreadDeg", 6);
            var targets = new List<string>();
            for (var i = 0; i < n; i++)
            {
                var ang = aim + (i - (n - 1) * 0.5f) * spread;
                var p = NewProjectile(s, castId, ang, s.P("speed", 8), s.P("lifetime", 2), s.P("radius", 0.2f), dmg);
                if (homingCards > 0)
                {
                    p.HomeTurn = turn;
                    p.TargetId = SelectTarget(p.Pos, ang, half, range);
                    if (p.TargetId == "") p.TargetLost = true; // MAG-24: straight, never re-search
                    targets.Add(p.TargetId);
                }
            }
            Emit("p1.magic.missile", "Fire(MAG-12/28/29)", ("count", n), ("damagePerProjectile", dmg), ("homingCards", homingCards), ("halfAngle", half),
                ("range", range), ("turn", turn), ("targets", string.Join(",", targets)), ("aimDeg", aim));
        }

        P1Projectile NewProjectile(SpellDef s, long castId, float angle, float speed, float life, float radius, float dmg)
        {
            var p = new P1Projectile
            {
                Id = _nextProj++, CastId = castId, SpellId = s.Id, Kind = s.Kind, Pos = Muzzle(), Angle = angle, Speed = speed, Life = life,
                Radius = radius, Damage = dmg,
            };
            Projectiles.Add(p);
            return p;
        }

        void FireOrb(SpellDef s, long castId, float aim)
        {
            var magAtk = R.Stat(G, "MagAtk");
            switch (s.Kind)
            {
                case SpellKind.LightOrb:
                {
                    var p = NewProjectile(s, castId, aim, s.P("speed", 11) + Ax(s, "speed"), s.P("lifetime", 2), s.P("radius", 0.25f), s.P("damage", 5) + Ax(s, "dmg") + magAtk);
                    p.ChainLeft = (int)Ax(s, "chain");
                    p.ChainRadius = s.P("chainRadius", 3);
                    break;
                }
                case SpellKind.DarkOrb:
                {
                    var p = NewProjectile(s, castId, aim, s.P("speed", 6), s.P("lifetime", 2.5f), s.P("radius", 0.28f) + Ax(s, "radius"), s.P("damage", 10) + Ax(s, "dmg") + magAtk);
                    p.PierceLeft = (int)Ax(s, "pierce");
                    p.Shock = Ax(s, "shock");
                    p.ShockRadius = s.P("shockRadius", 1);
                    break;
                }
                case SpellKind.FireBall:
                {
                    var p = NewProjectile(s, castId, aim, s.P("speed", 9), s.P("lifetime", 1.5f), s.P("projRadius", 0.3f), 0);
                    p.ExplodeDamage = s.P("damage", 15) + Ax(s, "dmg") + magAtk;
                    p.ExplodeRadius = s.P("radius", 1.25f) + Ax(s, "radius");
                    p.Burn = Ax(s, "burn");
                    p.BurnTick = s.P("burnTick", 1);
                    p.BurnInterval = s.P("burnInterval", 1);
                    p.Delayed = (int)Ax(s, "delayed");
                    p.Delay = s.P("delay", 0.4f);
                    p.DelayedRatio = s.P("delayedRatio", 0.5f);
                    break;
                }
            }
        }

        /// <summary>MAG-19/22/26/29: nearest enemy inside this projectile's front cone and range at launch; obstacles ignored.</summary>
        public string SelectTarget(Vec2 from, float angle, float half, float range)
        {
            var best = "";
            var bestD = float.MaxValue;
            foreach (var en in C.Enemies)
            {
                if (!en.Targetable) continue;
                var c = new Vec2(en.Body.CenterX, en.Body.CenterY);
                var dx = c.X - from.X;
                var dy = c.Y - from.Y;
                var d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d > range) continue;
                var a = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
                if (Math.Abs(DeltaAngle(angle, a)) > half) continue;
                if (d < bestD) { bestD = d; best = en.Id; }
            }
            return best;
        }

        static float DeltaAngle(float from, float to)
        {
            var d = (to - from) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        EnemyActor Enemy(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var en in C.Enemies) if (en.Id == id) return en;
            return null;
        }

        void PlaceWalls(SpellDef s, long castId)
        {
            if (Walls.Count > 0) Emit("p1.magic.wallReplace", "OneActiveCast(MAG-66)", ("removed", Walls.Count));
            Walls.Clear();
            var count = 1 + (int)Ax(s, "extraWalls");
            var ground = Collision.SnapToGround(_rt.Sim.CurrentMap, Pl.Position, 0.2f);
            for (var i = 0; i < count; i++)
            {
                var x = Pl.Position.X + Pl.Facing * (s.P("distance", 2) + i * s.P("spacing", 1.2f));
                Walls.Add(new P1Wall
                {
                    CastId = castId, X = x, Y = ground.Y, Width = s.P("width", 1.5f) + Ax(s, "width"), Height = s.P("height", 2.2f),
                    Tick = s.P("tick", 2) + Ax(s, "tick") + R.Stat(G, "MagAtk"), Interval = s.P("interval", 0.75f), Remaining = s.P("duration", 4) + Ax(s, "duration"),
                });
            }
            Emit("p1.magic.wall", "GroundFixedFront(MAG-67)", ("count", count), ("x", Walls[0].X), ("y", Walls[0].Y), ("time", Walls[0].Remaining));
        }

        void SummonOrExtend(SpellDef s)
        {
            if (Summon != null && Summon.Kind == s.Kind)
            {
                var before = Summon.Remaining;
                Summon.Remaining = Math.Min(s.P("durationCap", 36), Summon.Remaining + s.P("extend", 18));
                Emit("p1.summon.extend", "SameKindExtend(MAG-85, BAL-11)", ("spellId", s.Id), ("before", before), ("after", Summon.Remaining));
                return;
            }
            Summon = new P1Summon
            {
                SpellId = s.Id, Kind = s.Kind, Remaining = s.P("duration", 18), Pos = new Vec2(Pl.Position.X - Pl.Facing * 0.8f, Pl.Position.Y + 1.2f),
                AttackTimer = 0.5f, HealTimer = 0f,
            };
            Emit("p1.summon.start", "Summon(MAG-52/83)", ("spellId", s.Id), ("time", Summon.Remaining));
        }

        // ================================================================ step

        /// <summary>World step while the game runs (controllable time; MAG-56: maintained effects only run here).</summary>
        public void Step(float dt, bool controllable)
        {
            CheckCancelled();
            if (!controllable) return;
            for (var i = _queued.Count - 1; i >= 0; i--)
            {
                var q = _queued[i];
                q.delay -= dt;
                if (q.delay <= 1e-6f) { _queued.RemoveAt(i); q.fire(); }
                else _queued[i] = q;
            }
            StepProjectiles(dt);
            StepExplosions(dt);
            StepWalls(dt);
            StepDots(dt);
            StepSummon(dt);
            if (_hotLeft > 0)
            {
                _hotTimer -= dt;
                if (_hotTimer <= 1e-6f)
                {
                    _hotTimer += 1f;
                    _hotLeft -= 1f;
                    C.SetPlayerHp(C.PlayerHp + _hotPerSec, "P1HealOverTime(MAG-69 legendary)");
                }
            }
        }

        void StepProjectiles(float dt)
        {
            var map = _rt.Sim.CurrentMap;
            foreach (var p in Projectiles)
            {
                if (p.Dead) continue;
                if (p.HomeTurn > 0 && !p.TargetLost)
                {
                    var t = Enemy(p.TargetId);
                    if (t == null || !t.Targetable)
                    {
                        p.TargetLost = true; // MAG-23/25: dead / removed / left the map -> straight, no re-search
                        Emit("p1.proj.targetLost", "TargetInvalid(MAG-25)", ("projectileId", p.Id), ("targetId", p.TargetId), ("angle", p.Angle));
                    }
                    else
                    {
                        var want = (float)(Math.Atan2(t.Body.CenterY - p.Pos.Y, t.Body.CenterX - p.Pos.X) * 180.0 / Math.PI);
                        var d = DeltaAngle(p.Angle, want);
                        var max = p.HomeTurn * dt;
                        p.Angle += Math.Max(-max, Math.Min(max, d));
                    }
                }
                var rad = p.Angle * Math.PI / 180.0;
                p.Pos = new Vec2(p.Pos.X + (float)Math.Cos(rad) * p.Speed * dt, p.Pos.Y + (float)Math.Sin(rad) * p.Speed * dt);
                p.Life -= dt;
                var box = Aabb.FromCenterSize(p.Pos.X, p.Pos.Y, p.Radius * 2, p.Radius * 2);
                var wall = false;
                foreach (var so in map.Solids) if (so.Overlaps(box)) { wall = true; break; }
                if (wall)
                {
                    Emit("p1.proj.wall", "SolidHit(MAG-30)", ("projectileId", p.Id), ("spellId", p.SpellId));
                    Finish(p);
                    continue;
                }
                EnemyActor hit = null;
                var bestD = float.MaxValue;
                foreach (var en in C.Enemies)
                {
                    if (!C.ExternalHittable(en) || p.Hit.Contains(en.Id) || !en.Body.Overlaps(box)) continue;
                    var dd = Math.Abs(en.Body.CenterX - p.Pos.X);
                    if (dd < bestD) { bestD = dd; hit = en; }
                }
                if (hit != null)
                {
                    p.Hit.Add(hit.Id); // MAG-64: one projectile never hits the same enemy twice
                    if (p.Kind == SpellKind.FireBall) { Explode(p.Pos, p.ExplodeRadius, p.ExplodeDamage, p, "FireBall:Explosion"); FireBallAfter(p); p.Dead = true; continue; }
                    HitEnemy(hit, p.SpellId, p.Damage, false, (p.Physical ? "SwordWave:" : "Projectile:") + p.Id, p.Physical);
                    if (p.ChainLeft > 0)
                    {
                        var next = NearestUnhit(p, p.ChainRadius);
                        if (next != null)
                        {
                            p.ChainLeft--;
                            p.Angle = (float)(Math.Atan2(next.Body.CenterY - p.Pos.Y, next.Body.CenterX - p.Pos.X) * 180.0 / Math.PI);
                            Emit("p1.proj.chain", "NewTarget(MAG-59 epic)", ("projectileId", p.Id), ("next", next.Id), ("left", p.ChainLeft));
                            continue;
                        }
                    }
                    if (p.PierceLeft > 0) { p.PierceLeft--; continue; }
                    Finish(p);
                    continue;
                }
                if (p.Life <= 0f)
                {
                    if (p.Kind == SpellKind.FireBall) { Explode(p.Pos, p.ExplodeRadius, p.ExplodeDamage, p, "FireBall:Expire(EXP-P1-FB-01)"); FireBallAfter(p); }
                    p.Dead = true;
                }
            }
            Projectiles.RemoveAll(x => x.Dead);
        }

        EnemyActor NearestUnhit(P1Projectile p, float radius)
        {
            EnemyActor best = null;
            var bd = float.MaxValue;
            foreach (var en in C.Enemies)
            {
                if (!en.Targetable || p.Hit.Contains(en.Id)) continue;
                var dx = en.Body.CenterX - p.Pos.X;
                var dy = en.Body.CenterY - p.Pos.Y;
                var d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d <= radius && d < bd) { bd = d; best = en; }
            }
            return best;
        }

        /// <summary>End of a projectile: dark orb final shockwave (MAG-60 legendary), fire ball explosion.</summary>
        void Finish(P1Projectile p)
        {
            if (p.Kind == SpellKind.DarkOrb && p.Shock > 0) Explode(p.Pos, p.ShockRadius, p.Shock, p, "DarkOrb:Shockwave");
            if (p.Kind == SpellKind.FireBall) { Explode(p.Pos, p.ExplodeRadius, p.ExplodeDamage, p, "FireBall:Explosion"); FireBallAfter(p); }
            p.Dead = true;
        }

        void FireBallAfter(P1Projectile p)
        {
            for (var i = 1; i <= p.Delayed; i++)
                Explosions.Add(new P1Explosion
                {
                    CastId = p.CastId, SpellId = p.SpellId, Pos = p.Pos, Radius = p.ExplodeRadius, Damage = (float)Math.Floor(p.ExplodeDamage * p.DelayedRatio),
                    Timer = p.Delay * i, Label = "FireBall:Delayed" + i + "(BAL-12)",
                });
        }

        void Explode(Vec2 at, float radius, float damage, P1Projectile src, string label)
        {
            var area = Aabb.FromCenterSize(at.X, at.Y, radius * 2, radius * 2);
            var n = 0;
            foreach (var en in C.Enemies)
            {
                if (!en.Targetable || !en.Body.Overlaps(area)) continue;
                n++;
                HitEnemy(en, src.SpellId, damage, false, label);
                if (src.Burn > 0 && en.Targetable)
                {
                    Dots.RemoveAll(d => d.EnemyId == en.Id && d.SpellId == src.SpellId);
                    Dots.Add(new P1Dot { EnemyId = en.Id, SpellId = src.SpellId, Tick = src.BurnTick, Interval = src.BurnInterval, Remaining = src.Burn, Timer = src.BurnInterval });
                }
            }
            Emit("p1.magic.explode", label, ("x", at.X), ("y", at.Y), ("radius", radius), ("damage", damage), ("targets", n));
        }

        void StepExplosions(float dt)
        {
            for (var i = Explosions.Count - 1; i >= 0; i--)
            {
                var x = Explosions[i];
                x.Timer -= dt;
                if (x.Timer > 1e-6f) continue;
                Explosions.RemoveAt(i);
                Explode(x.Pos, x.Radius, x.Damage, new P1Projectile { SpellId = x.SpellId }, x.Label);
            }
        }

        void StepWalls(float dt)
        {
            for (var i = Walls.Count - 1; i >= 0; i--)
            {
                var w = Walls[i];
                w.Remaining -= dt;
                if (w.Remaining <= 0f) { Walls.RemoveAt(i); continue; }
                var box = Aabb.FromFeet(new Vec2(w.X, w.Y), w.Width, w.Height);
                foreach (var en in C.Enemies)
                {
                    if (!en.Targetable || !en.Body.Overlaps(box)) continue;
                    if (!w.NextTick.TryGetValue(en.Id, out var t))
                    {
                        w.NextTick[en.Id] = w.Interval;
                        HitEnemy(en, "spell.fire_wall", w.Tick, false, "FireWall:First(MAG-65)");
                    }
                    else
                    {
                        t -= dt;
                        if (t <= 1e-6f)
                        {
                            t += w.Interval;
                            HitEnemy(en, "spell.fire_wall", w.Tick, true, "FireWall:Tick(MAG-65 damage only)");
                        }
                        w.NextTick[en.Id] = t;
                    }
                }
            }
        }

        void StepDots(float dt)
        {
            for (var i = Dots.Count - 1; i >= 0; i--)
            {
                var d = Dots[i];
                var en = Enemy(d.EnemyId);
                if (en == null || !en.Targetable) { Dots.RemoveAt(i); continue; }
                d.Timer -= dt;
                d.Remaining -= dt;
                if (d.Timer <= 1e-6f)
                {
                    d.Timer += d.Interval;
                    HitEnemy(en, d.SpellId, d.Tick, true, "Dot:" + d.SpellId + "(MAG-65 damage only)");
                }
                if (d.Remaining <= 1e-6f) Dots.RemoveAt(i);
            }
        }

        EnemyActor FrontNearest(float range)
        {
            EnemyActor best = null;
            var bd = float.MaxValue;
            foreach (var en in C.Enemies)
            {
                if (!en.Targetable) continue;
                var dx = en.Position.X - Pl.Position.X;
                if (dx * Pl.Facing < 0f || Math.Abs(dx) > range) continue; // MAG-88 Volker's front only
                if (Math.Abs(dx) < bd) { bd = Math.Abs(dx); best = en; }
            }
            return best;
        }

        void StepSummon(float dt)
        {
            var sm = Summon;
            if (sm == null) return;
            var s = P.Spell(sm.SpellId);
            sm.Remaining -= dt;
            if (sm.Remaining <= 0f)
            {
                Emit("p1.summon.end", "Expired", ("spellId", sm.SpellId));
                Summon = null;
                return;
            }
            // EXP-P1-SUMMON-01: hover behind Volker; attacks the nearest enemy in front within range
            sm.Pos = new Vec2(Pl.Position.X - Pl.Facing * 0.8f, Pl.Position.Y + 1.2f);
            sm.AttackTimer -= dt;
            if (sm.Kind == SpellKind.Archangel)
            {
                var interval = Math.Max(0.1f, s.P("healInterval", 4) + Ax(s, "healInterval"));
                sm.HealTimer -= dt;
                if (sm.HealTimer <= 0f && C.PlayerHp <= C.PlayerMaxHp * s.P("healThreshold", 0.5f))
                {
                    sm.HealTimer = interval;
                    var before = C.PlayerHp;
                    C.SetPlayerHp(C.PlayerHp + s.P("heal", 5) + Ax(s, "heal"), "ArchangelHeal(MAG-89)");
                    if (Ax(s, "cure") > 0) { _rt.Fx.Statuses.Remove("Poison"); _rt.Fx.Statuses.Remove("Confusion"); }
                    var sh = Ax(s, "shield");
                    if (sh > 0) _rt.Fx.ArchangelShield = new ShieldPool { Id = "ArchangelShield", Remaining = (int)sh, Time = s.P("shieldDuration", 12) };
                    Emit("p1.summon.heal", "BelowThreshold(MAG-89)", ("hpBefore", before), ("hpAfter", C.PlayerHp), ("shield", sh));
                }
                if (sm.AttackTimer <= 0f)
                {
                    var t = FrontNearest(s.P("range", 6));
                    if (t != null)
                    {
                        sm.AttackTimer = s.P("attackInterval", 3);
                        HitEnemy(t, sm.SpellId, s.P("attack", 2) + R.Stat(G, "MagAtk"), false, "Archangel:Light(MAG-80/87)");
                    }
                }
            }
            else
            {
                if (sm.AttackTimer <= 0f)
                {
                    var t = FrontNearest(s.P("range", 6));
                    if (t != null)
                    {
                        sm.AttackTimer = Math.Max(0.1f, s.P("attackInterval", 2) + Ax(s, "attackInterval"));
                        sm.TargetId = t.Id;
                        sm.Pos = new Vec2(t.Position.X - Pl.Facing * s.P("meleeReach", 1.2f), t.Position.Y + 0.6f);
                        HitEnemy(t, sm.SpellId, s.P("attack", 6) + Ax(s, "attack") + R.Stat(G, "MagAtk"), false, "Demon:Melee(MAG-80/87)");
                        var aoe = Ax(s, "aoe");
                        if (aoe > 0)
                        {
                            var r = s.P("aoeRadius", 1);
                            foreach (var en in C.Enemies)
                                if (en != t && en.Targetable && Math.Abs(en.Position.X - t.Position.X) <= r)
                                    HitEnemy(en, sm.SpellId, aoe, false, "Demon:Area(MAG-82 epic)");
                        }
                        var flame = Ax(s, "flame");
                        if (flame > 0 && t.Targetable)
                        {
                            Dots.RemoveAll(d => d.EnemyId == t.Id && d.SpellId == sm.SpellId);
                            Dots.Add(new P1Dot { EnemyId = t.Id, SpellId = sm.SpellId, Tick = s.P("flameTick", 1), Interval = s.P("flameInterval", 1), Remaining = flame, Timer = s.P("flameInterval", 1) });
                        }
                    }
                }
            }
        }

        /// <summary>Light sword legendary wave (MAG-71): one wave per 4th-hit attack (EXP-P1-WAVE-01 spawn at that hit).</summary>
        public void OnFourthHit(PlayerAttack a)
        {
            var sw = _rt.Fx.Sword;
            if (sw == null || !sw.Light || sw.Wave <= 0 || !_waveSpawned.Add(a.InstanceId)) return;
            var s = P.Spell("spell.light_sword");
            var p = NewProjectile(s, 0, Pl.Facing >= 0 ? 0 : 180, s.P("waveSpeed", 10), s.P("waveLifetime", 0.6f), 0.4f, sw.Wave);
            p.Physical = true;
            p.Hit.UnionWith(a.HitByEvent.Count > 0 ? a.HitByEvent[0] : new HashSet<string>());
            Emit("p1.magic.swordWave", "LegendaryWave(MAG-71)", ("damage", sw.Wave));
        }

        public void ClearProjectiles()
        {
            Projectiles.Clear();
            Walls.Clear();
            Explosions.Clear();
            Dots.Clear();
            _queued.Clear();
            _cast = null;
        }

        /// <summary>MAG-32: projectiles, walls, explosions and DOTs end with the map; the summon follows (MAG-90).</summary>
        public void OnMapChanged()
        {
            var n = Projectiles.Count + Walls.Count + Explosions.Count + Dots.Count;
            Projectiles.Clear();
            Walls.Clear();
            Explosions.Clear();
            Dots.Clear();
            _queued.Clear();
            if (Summon != null) Summon.Pos = new Vec2(Pl.Position.X - Pl.Facing * 0.8f, Pl.Position.Y + 1.2f);
            Emit("p1.magic.mapClear", "MapChange(MAG-32/90)", ("removed", n), ("summonKept", Summon != null), ("summonLeft", Summon?.Remaining ?? 0));
        }
    }
}

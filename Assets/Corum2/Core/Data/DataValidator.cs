using System.Collections.Generic;

namespace Corum2.Core
{
    public sealed class ValidationIssue
    {
        public readonly string Source;
        public readonly string Message;

        public ValidationIssue(string source, string message)
        {
            Source = source;
            Message = message;
        }

        public override string ToString() => Source + ": " + Message;
    }

    /// <summary>
    /// Load-time checks (spec 11.2, appendix section 4). Failures are reported, never patched with defaults.
    /// </summary>
    public static class DataValidator
    {
        public static List<ValidationIssue> Validate(WorldData world, MovementProfile move, InputProfile input, AirUnlocks unlocks)
        {
            var issues = new List<ValidationIssue>();
            CheckMeta(issues, "MovementProfile", move?.Meta);
            CheckMeta(issues, "InputProfile", input?.Meta);
            CheckMeta(issues, "AirUnlocks", unlocks?.Meta);

            if (move != null) ValidateMovement(issues, move);
            if (input != null)
            {
                // WP-01-R1 F-04: range checks written as "must be inside" so NaN fails them too.
                if (!(input.StickDeadzone >= 0f && input.StickDeadzone < 1f)) issues.Add(new ValidationIssue("InputProfile", "StickDeadzone must be a finite value in [0,1) (got " + input.StickDeadzone + ")."));
                if (!(input.DoubleTapWindow > 0.0 && input.DoubleTapWindow < 10.0)) issues.Add(new ValidationIssue("InputProfile", "DoubleTapWindow must be a finite value in (0,10) s (got " + input.DoubleTapWindow + ")."));
            }

            if (world == null)
            {
                issues.Add(new ValidationIssue("World", "World data missing."));
                return issues;
            }

            var ids = new HashSet<string>();
            foreach (var m in world.Maps)
            {
                if (string.IsNullOrEmpty(m.Id)) issues.Add(new ValidationIssue("Map", "Map without id."));
                else if (!ids.Add(m.Id)) issues.Add(new ValidationIssue("Map " + m.Id, "Duplicate map id."));
            }

            if (world.FindMap(world.StartMapId) == null) issues.Add(new ValidationIssue("World", "Start map '" + world.StartMapId + "' not found."));
            else if (world.FindMap(world.StartMapId).FindSpawn(world.StartSpawnId) == null)
                issues.Add(new ValidationIssue("World", "Start spawn '" + world.StartSpawnId + "' not found in start map."));

            foreach (var m in world.Maps) ValidateMap(issues, world, m, move);
            return issues;
        }

        /// <summary>WP-02 combat data checks (spec 11.2): finite values, timings, sizes, mandatory magic knockback.</summary>
        public static List<ValidationIssue> ValidateCombat(CombatProfile p, CombatUnlocks u, IList<EnemyDefinition> enemies, WorldData world)
        {
            var issues = new List<ValidationIssue>();
            CheckMeta(issues, "CombatProfile", p?.Meta);
            CheckMeta(issues, "CombatUnlocks", u?.Meta);
            if (p == null) return issues;
            if (p.GroundNormals.Length != 4) issues.Add(new ValidationIssue("CombatProfile", "GroundNormals must have 4 entries (R-04-01)."));
            if (p.AirNormals.Length != 4) issues.Add(new ValidationIssue("CombatProfile", "AirNormals must have 4 entries (R-04-01)."));
            var all = new List<AttackDefinition>(p.GroundNormals);
            all.AddRange(p.AirNormals);
            all.Add(p.Launcher);
            all.Add(p.AirDash);
            all.Add(p.Dive);
            all.Add(p.DownAttack);
            foreach (var a in all) ValidateAttack(issues, "CombatProfile attack " + a?.Id, a);
            if (p.Launcher != null && !(p.Launcher.LaunchVelocity > 0f)) issues.Add(new ValidationIssue("CombatProfile", "Launcher.LaunchVelocity must be > 0."));
            if (p.DownAttack != null && !p.DownAttack.HitsDowned) issues.Add(new ValidationIssue("CombatProfile", "DownAttack must hit downed targets (R-06-09)."));
            if (p.Dive != null && !p.Dive.HitsDowned) issues.Add(new ValidationIssue("CombatProfile", "Dive must hit downed targets (R-06-09)."));
            foreach (var (name, v, min) in new[]
                     {
                         ("PlayerMaxHp", p.PlayerMaxHp, 0f), ("ComboWindow", p.ComboWindow, 0f), ("AirAttackFallSpeed", p.AirAttackFallSpeed, 0f),
                         ("ParryWindow", p.ParryWindow, 0f), ("ParryStunTime", p.ParryStunTime, 0f), ("ParryKnockback", p.ParryKnockback, 0f),
                         ("DodgeReactionTime", p.DodgeReactionTime, 0f), ("DodgeKnockback", p.DodgeKnockback, 0f), ("PlayerHitstun", p.PlayerHitstun, 0f),
                         ("KnockbackDecel", p.KnockbackDecel, 0f), ("DiveAreaRadius", p.DiveAreaRadius, 0f), ("DownAttackRange", p.DownAttackRange, 0f),
                         ("DeathMotionTime", p.DeathMotionTime, 0f), ("ReviveHpRatio", p.ReviveHpRatio, 0f),
                     })
            {
                if (Finite(issues, "CombatProfile", name, v) && !(v > min)) issues.Add(new ValidationIssue("CombatProfile", name + " must be > 0."));
            }
            if (Finite(issues, "CombatProfile", "ReviveHpRatio", p.ReviveHpRatio) && p.ReviveHpRatio > 1f)
                issues.Add(new ValidationIssue("CombatProfile", "ReviveHpRatio must be <= 1 (EXP-DEATH-03)."));
            if (Finite(issues, "CombatProfile", "ReviveInvulnTime", p.ReviveInvulnTime) && p.ReviveInvulnTime < 0f)
                issues.Add(new ValidationIssue("CombatProfile", "ReviveInvulnTime must be >= 0 (EXP-DEATH-03)."));
            if (Finite(issues, "CombatProfile", "RollInvulnStart", p.RollInvulnStart) & Finite(issues, "CombatProfile", "RollInvulnEnd", p.RollInvulnEnd) &&
                !(p.RollInvulnStart >= 0f && p.RollInvulnEnd > p.RollInvulnStart))
                issues.Add(new ValidationIssue("CombatProfile", "Roll invulnerability must be a part of the roll: 0 <= start < end (R-06-13)."));

            var ids = new HashSet<string>();
            foreach (var e in enemies ?? new EnemyDefinition[0])
            {
                var src = "Enemy " + e.Id;
                CheckMeta(issues, src, e.Meta);
                if (string.IsNullOrEmpty(e.Id) || !ids.Add(e.Id)) issues.Add(new ValidationIssue(src, "Enemy id empty or duplicated."));
                foreach (var (name, v) in new[] { ("MaxHp", e.MaxHp), ("BodyWidth", e.BodyWidth), ("BodyHeight", e.BodyHeight), ("Gravity", e.Gravity), ("StaggerTime", e.StaggerTime),
                             ("JuggleFallSpeed", e.JuggleFallSpeed), ("JuggleSlowTime", e.JuggleSlowTime), ("DownTime", e.DownTime), ("WakeupTime", e.WakeupTime),
                             ("DeathTime", e.DeathTime), ("VanishTime", e.VanishTime) })
                    if (Finite(issues, src, name, v) && !(v > 0f)) issues.Add(new ValidationIssue(src, name + " must be > 0."));
                if (Finite(issues, src, "StaggerResistance", e.StaggerResistance) && !(e.StaggerResistance >= 0f && e.StaggerResistance <= 1f))
                    issues.Add(new ValidationIssue(src, "StaggerResistance must be in [0,1]."));
                if (Finite(issues, src, "PhysicalKnockbackScale", e.PhysicalKnockbackScale) && e.PhysicalKnockbackScale < 0f)
                    issues.Add(new ValidationIssue(src, "PhysicalKnockbackScale must be >= 0 (0 allowed, R-06-05)."));
                if (Finite(issues, src, "MagicKnockbackScale", e.MagicKnockbackScale) && !(e.MagicKnockbackScale > 0f))
                    issues.Add(new ValidationIssue(src, "MagicKnockbackScale must be > 0: magic knockback 0 is not allowed (R-06-06)."));
                if (Finite(issues, src, "LaunchResistScale", e.LaunchResistScale) && !(e.LaunchResistScale > 0f && e.LaunchResistScale < 1f))
                    issues.Add(new ValidationIssue(src, "LaunchResistScale must be in (0,1)."));
                if (e.Attack != null)
                {
                    ValidateAttack(issues, src + " attack", e.Attack);
                    foreach (var (name, v) in new[] { ("AttackRange", e.AttackRange), ("AttackInterval", e.AttackInterval) })
                        if (Finite(issues, src, name, v) && !(v > 0f)) issues.Add(new ValidationIssue(src, name + " must be > 0."));
                    if (Finite(issues, src, "FirstAttackDelay", e.FirstAttackDelay) && e.FirstAttackDelay < 0f)
                        issues.Add(new ValidationIssue(src, "FirstAttackDelay must be >= 0."));
                }
                AiValidator.Validate(issues, e, ValidateAttack);
            }
            if (world != null)
                foreach (var m in world.Maps)
                foreach (var s in m.Enemies)
                {
                    var src = "Map " + m.Id + " enemy " + s.InstanceId;
                    if (!ids.Contains(s.EnemyId)) issues.Add(new ValidationIssue(src, "Enemy definition '" + s.EnemyId + "' not found."));
                    Finite(issues, src, "X", s.Position.X);
                    Finite(issues, src, "Y", s.Position.Y);
                    if (s.Facing != 1 && s.Facing != -1) issues.Add(new ValidationIssue(src, "Facing must be 1 or -1."));
                }
            return issues;
        }

        static void ValidateAttack(List<ValidationIssue> issues, string src, AttackDefinition a)
        {
            if (a == null)
            {
                issues.Add(new ValidationIssue(src, "Attack missing."));
                return;
            }
            foreach (var (name, v) in new[] { ("Startup", a.Startup), ("Recovery", a.Recovery), ("Hitstop", a.Hitstop), ("TestDamage", a.TestDamage), ("Knockback", a.Knockback), ("LaunchVelocity", a.LaunchVelocity) })
                if (Finite(issues, src, name, v) && v < 0f) issues.Add(new ValidationIssue(src, name + " must be >= 0."));
            foreach (var (name, v) in new[] { ("Active", a.Active), ("HitboxWidth", a.HitboxWidth), ("HitboxHeight", a.HitboxHeight) })
                if (Finite(issues, src, name, v) && !(v > 0f)) issues.Add(new ValidationIssue(src, name + " must be > 0."));
            Finite(issues, src, "HitboxOffsetX", a.HitboxOffsetX);
            Finite(issues, src, "HitboxOffsetY", a.HitboxOffsetY);
            if (a.HitEvents < 1) issues.Add(new ValidationIssue(src, "HitEvents must be >= 1."));
            if (a.DamageType == DamageType.Magic && !(a.Knockback > 0f))
                issues.Add(new ValidationIssue(src, "Magic attack knockback must be > 0 (R-06-06)."));
        }

        static void CheckMeta(List<ValidationIssue> issues, string source, DataMeta meta)
        {
            if (meta == null)
            {
                issues.Add(new ValidationIssue(source, "Data missing."));
                return;
            }
            if (string.IsNullOrEmpty(meta.DomainId)) issues.Add(new ValidationIssue(source, "DomainId missing."));
            if (string.IsNullOrEmpty(meta.DataVersion)) issues.Add(new ValidationIssue(source, "DataVersion missing."));
            if (meta.Status != "experimental" && meta.Status != "approved") issues.Add(new ValidationIssue(source, "Status must be 'experimental' or 'approved'."));
        }

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Rejects NaN/Infinity with source and field name (WP-01-R1 F-04).</summary>
        static bool Finite(List<ValidationIssue> issues, string source, string field, float v)
        {
            if (IsFinite(v)) return true;
            issues.Add(new ValidationIssue(source, field + " must be finite (got " + v + ")."));
            return false;
        }

        static void FiniteBox(List<ValidationIssue> issues, string source, string field, Aabb b)
        {
            Finite(issues, source, field + ".MinX", b.MinX);
            Finite(issues, source, field + ".MinY", b.MinY);
            Finite(issues, source, field + ".MaxX", b.MaxX);
            Finite(issues, source, field + ".MaxY", b.MaxY);
        }

        static void ValidateMovement(List<ValidationIssue> issues, MovementProfile m)
        {
            void Positive(string name, float v)
            {
                if (!Finite(issues, "MovementProfile", name, v)) return;
                if (!(v > 0f)) issues.Add(new ValidationIssue("MovementProfile", name + " must be > 0."));
            }

            Positive("WalkSpeed", m.WalkSpeed);
            Positive("RunSpeed", m.RunSpeed);
            Positive("Gravity", m.Gravity);
            Positive("JumpHeight", m.JumpHeight);
            Positive("ExtraJumpHeight", m.ExtraJumpHeight);
            Positive("AirControlSpeed", m.AirControlSpeed);
            Positive("AirAcceleration", m.AirAcceleration);
            Positive("MaxFallSpeed", m.MaxFallSpeed);
            Positive("SlideSpeed", m.SlideSpeed);
            Positive("SlideDuration", m.SlideDuration);
            Positive("RollSpeed", m.RollSpeed);
            Positive("RollDuration", m.RollDuration);
            Positive("DashSpeed", m.DashSpeed);
            Positive("DashDuration", m.DashDuration);
            Positive("DiveSpeed", m.DiveSpeed);
            Positive("BodyWidth", m.BodyWidth);
            Positive("StandHeight", m.StandHeight);
            Positive("CrouchHeight", m.CrouchHeight);
            if (Finite(issues, "MovementProfile", "DashEndSpeed", m.DashEndSpeed) && m.DashEndSpeed < 0f) issues.Add(new ValidationIssue("MovementProfile", "DashEndSpeed must be >= 0."));
            Finite(issues, "MovementProfile", "DiveAngleDeg", m.DiveAngleDeg);
            // R-03-02 / spec 11.2: walk jump distance < run jump distance.
            if (!(m.WalkSpeed < m.RunSpeed)) issues.Add(new ValidationIssue("MovementProfile", "WalkSpeed must be < RunSpeed (walk jump shorter than run jump)."));
            if (!(m.CrouchHeight < m.StandHeight)) issues.Add(new ValidationIssue("MovementProfile", "CrouchHeight must be < StandHeight."));
            if (!(m.DiveAngleDeg > 0f && m.DiveAngleDeg < 90f)) issues.Add(new ValidationIssue("MovementProfile", "DiveAngleDeg must be in (0,90) for a diagonal dive."));
        }

        static void ValidateMap(List<ValidationIssue> issues, WorldData world, MapData m, MovementProfile move)
        {
            var src = "Map " + m.Id;
            CheckMeta(issues, src, m.Meta);
            Finite(issues, src, "MinX", m.MinX);
            Finite(issues, src, "MaxX", m.MaxX);
            Finite(issues, src, "KillY", m.KillY);
            Finite(issues, src, "CombatPlaneZ", m.CombatPlaneZ);
            Finite(issues, src, "Camera.MinX", m.Camera.MinX);
            Finite(issues, src, "Camera.MaxX", m.Camera.MaxX);
            Finite(issues, src, "Camera.Y", m.Camera.Y);
            Finite(issues, src, "Camera.FollowSharpness", m.Camera.FollowSharpness);
            for (var i = 0; i < m.Solids.Count; i++) FiniteBox(issues, src, "Solids[" + i + "]", m.Solids[i]);
            foreach (var s in m.Spawns)
            {
                Finite(issues, src, "Spawn " + s.Id + ".X", s.Position.X);
                Finite(issues, src, "Spawn " + s.Id + ".Y", s.Position.Y);
            }
            foreach (var p in m.Points) FiniteBox(issues, src, "Point " + p.Id + ".Area", p.Area);            if (!(m.MinX < m.MaxX)) issues.Add(new ValidationIssue(src, "MinX must be < MaxX."));
            if (m.Camera.MinX > m.Camera.MaxX) issues.Add(new ValidationIssue(src, "Camera MinX must be <= MaxX."));
            if (m.Camera.FollowSharpness < 0f) issues.Add(new ValidationIssue(src, "Camera FollowSharpness must be >= 0."));
            if (m.Spawns.Count == 0) issues.Add(new ValidationIssue(src, "Map needs at least one spawn."));

            var spawnIds = new HashSet<string>();
            foreach (var s in m.Spawns)
            {
                if (string.IsNullOrEmpty(s.Id) || !spawnIds.Add(s.Id)) issues.Add(new ValidationIssue(src, "Spawn id empty or duplicated: '" + s.Id + "'."));
                if (s.Position.X < m.MinX || s.Position.X > m.MaxX) issues.Add(new ValidationIssue(src, "Spawn '" + s.Id + "' outside map range."));
                if (s.Facing != 1 && s.Facing != -1) issues.Add(new ValidationIssue(src, "Spawn '" + s.Id + "' facing must be 1 or -1."));
                if (move != null)
                {
                    var body = Aabb.FromFeet(s.Position, move.BodyWidth, move.StandHeight);
                    foreach (var solid in m.Solids)
                        if (body.Overlaps(solid)) issues.Add(new ValidationIssue(src, "Spawn '" + s.Id + "' overlaps a solid " + solid + "."));
                }
            }

            foreach (var solid in m.Solids)
                if (!(solid.MinX < solid.MaxX) || !(solid.MinY < solid.MaxY)) issues.Add(new ValidationIssue(src, "Solid with non-positive size " + solid + "."));

            ValidateEdge(issues, world, src + " left edge", m.Left);
            ValidateEdge(issues, world, src + " right edge", m.Right);

            var pointIds = new HashSet<string>();
            foreach (var p in m.Points)
            {
                if (string.IsNullOrEmpty(p.Id) || !pointIds.Add(p.Id)) issues.Add(new ValidationIssue(src, "Point id empty or duplicated: '" + p.Id + "'."));
                if (!(p.Area.MinX < p.Area.MaxX) || !(p.Area.MinY < p.Area.MaxY)) issues.Add(new ValidationIssue(src, "Point '" + p.Id + "' has empty area."));
                CheckTarget(issues, world, src + " point " + p.Id, p.TargetMapId, p.TargetSpawnId);
            }
        }

        static void ValidateEdge(List<ValidationIssue> issues, WorldData world, string src, EdgeRule e)
        {
            if (e == null)
            {
                issues.Add(new ValidationIssue(src, "Edge policy missing (must be Blocked or Connected explicitly)."));
                return;
            }
            if (e.Policy != EdgePolicy.Connected) return;
            if (string.IsNullOrEmpty(e.ConnectionId)) issues.Add(new ValidationIssue(src, "Connected edge needs a ConnectionId."));
            CheckTarget(issues, world, src, e.TargetMapId, e.TargetSpawnId);
        }

        static void CheckTarget(List<ValidationIssue> issues, WorldData world, string src, string mapId, string spawnId)
        {
            var target = world.FindMap(mapId);
            if (target == null)
            {
                issues.Add(new ValidationIssue(src, "Target map '" + mapId + "' not found."));
                return;
            }
            if (target.FindSpawn(spawnId) == null) issues.Add(new ValidationIssue(src, "Target spawn '" + spawnId + "' not found in map '" + mapId + "'."));
        }
    }
}

using System.Collections.Generic;

namespace Corum2.Core
{
    /// <summary>Load-time checks for ring data (spec 11.2). Failures stop the run; nothing is patched with defaults.</summary>
    public static class RingValidator
    {
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        public static List<ValidationIssue> Validate(RingProfile p)
        {
            var issues = new List<ValidationIssue>();
            if (p == null) return issues;
            if (string.IsNullOrEmpty(p.Meta?.DomainId) || string.IsNullOrEmpty(p.Meta?.DataVersion))
                issues.Add(new ValidationIssue("RingProfile", "meta domainId/dataVersion missing"));
            if (!Finite(p.SlowScale) || !(p.SlowScale > 0f && p.SlowScale <= 1f))
                issues.Add(new ValidationIssue("RingProfile", "SlowScale must be in (0, 1]; 0 would pause the game (R-02-03: no full pause)"));
            if (p.SlotsPerPage != 8)
                issues.Add(new ValidationIssue("RingProfile", "SlotsPerPage must be 8 while slots map to 8 stick sectors (EXP-RING-02)"));
            if (!Finite(p.CursorDeadzone) || p.CursorDeadzone <= 0f || p.CursorDeadzone >= 1f)
                issues.Add(new ValidationIssue("RingProfile", "CursorDeadzone must be in (0, 1)"));
            var ids = new HashSet<string>();
            foreach (var i in p.Items)
            {
                if (string.IsNullOrEmpty(i.Id) || !ids.Add(i.Id)) issues.Add(new ValidationIssue("RingProfile", "item id empty or duplicated: '" + i.Id + "'"));
                if (!Finite(i.Cooldown) || i.Cooldown < 0f)
                    issues.Add(new ValidationIssue("Item " + i.Id, "Cooldown must be a finite number >= 0 (got " + i.Cooldown + ")"));
                if (!Finite(i.Amount) || i.Amount < 0f)
                    issues.Add(new ValidationIssue("Item " + i.Id, "Amount must be a finite number >= 0 (got " + i.Amount + ")"));
                if (i.Effect == ItemEffectType.Heal && !(i.Amount > 0f))
                    issues.Add(new ValidationIssue("Item " + i.Id, "Heal items need Amount > 0"));
                if (i.Effect == ItemEffectType.Magic && p.FindMagic(i.MagicId) == null)
                    issues.Add(new ValidationIssue("Item " + i.Id, "magic '" + i.MagicId + "' not defined"));
                if (i.Effect == ItemEffectType.Revive && i.Kind != ItemKind.Consumable)
                    issues.Add(new ValidationIssue("Item " + i.Id, "revive items must be consumables"));
                if (i.Kind == ItemKind.Quest && i.Effect != ItemEffectType.None)
                    issues.Add(new ValidationIssue("Item " + i.Id, "quest items have no use effect in WP-03"));
            }
            var magicIds = new HashSet<string>();
            foreach (var m in p.Magics)
            {
                if (string.IsNullOrEmpty(m.Id) || !magicIds.Add(m.Id)) issues.Add(new ValidationIssue("RingProfile", "magic id empty or duplicated: '" + m.Id + "'"));
                if (!Finite(m.Power) || m.Power < 0f) issues.Add(new ValidationIssue("Magic " + m.Id, "Power must be a finite number >= 0 (got " + m.Power + ")"));
                if (!Finite(m.CastTime) || m.CastTime < 0f) issues.Add(new ValidationIssue("Magic " + m.Id, "CastTime must be a finite number >= 0 (got " + m.CastTime + ")"));
                if (!Finite(m.Recovery) || m.Recovery < 0f) issues.Add(new ValidationIssue("Magic " + m.Id, "Recovery must be a finite number >= 0"));
                switch (m.Effect)
                {
                    case MagicEffect.Attack:
                        if (!(m.Power > 0f)) issues.Add(new ValidationIssue("Magic " + m.Id, "attack magic needs Power > 0"));
                        if (!Finite(m.Active) || m.Active <= 0f) issues.Add(new ValidationIssue("Magic " + m.Id, "Active must be > 0"));
                        if (!Finite(m.HitboxWidth) || m.HitboxWidth <= 0f || !Finite(m.HitboxHeight) || m.HitboxHeight <= 0f)
                            issues.Add(new ValidationIssue("Magic " + m.Id, "Hitbox size must be > 0"));
                        if (!Finite(m.HitboxOffsetX) || !Finite(m.HitboxOffsetY)) issues.Add(new ValidationIssue("Magic " + m.Id, "Hitbox offset must be finite"));
                        if (!Finite(m.Knockback) || m.Knockback <= 0f) issues.Add(new ValidationIssue("Magic " + m.Id, "Knockback must be > 0 for magic (R-06-06)"));
                        if (!Finite(m.Hitstop) || m.Hitstop < 0f) issues.Add(new ValidationIssue("Magic " + m.Id, "Hitstop must be >= 0"));
                        break;
                    case MagicEffect.Heal:
                        if (!(m.Power > 0f)) issues.Add(new ValidationIssue("Magic " + m.Id, "heal magic needs Power > 0"));
                        break;
                    case MagicEffect.Buff:
                        if (!(m.Power > 0f)) issues.Add(new ValidationIssue("Magic " + m.Id, "buff magic needs Power (multiplier) > 0"));
                        if (!Finite(m.BuffDuration) || m.BuffDuration <= 0f) issues.Add(new ValidationIssue("Magic " + m.Id, "BuffDuration must be > 0"));
                        break;
                }
            }
            if (p.MinCommandDirections < 2) issues.Add(new ValidationIssue("RingProfile", "MinCommandDirections must be >= 2 (EXP-CMD-01)"));
            var cmdIds = new HashSet<string>();
            var shapes = new HashSet<string>();
            foreach (var c in p.Commands)
            {
                var cs = "Command " + c.Id;
                if (string.IsNullOrEmpty(c.Id) || !cmdIds.Add(c.Id)) issues.Add(new ValidationIssue("RingProfile", "command id empty or duplicated: '" + c.Id + "'"));
                if (p.FindMagic(c.MagicId) == null) issues.Add(new ValidationIssue(cs, "magic '" + c.MagicId + "' not defined"));
                if (c.Directions.Count < p.MinCommandDirections)
                    issues.Add(new ValidationIssue(cs, "needs at least " + p.MinCommandDirections + " directions (EXP-CMD-01)"));
                for (var i = 0; i < c.Directions.Count; i++)
                {
                    if (c.Directions[i] == Dir8.None) issues.Add(new ValidationIssue(cs, "direction None is not allowed"));
                    if (i > 0 && c.Directions[i] == c.Directions[i - 1]) issues.Add(new ValidationIssue(cs, "the same direction twice in a row can never be recorded (EXP-CMD-01)"));
                }
                if (c.Button != LogicalAction.Attack && c.Button != LogicalAction.SpecialAttack && c.Button != LogicalAction.Guard && c.Button != LogicalAction.Jump)
                    issues.Add(new ValidationIssue(cs, "button must be an action button (Attack, SpecialAttack, Guard, Jump), got " + c.Button));
                if (!Finite(c.Window) || c.Window <= 0f) issues.Add(new ValidationIssue(cs, "Window must be a finite number > 0"));
                var shape = string.Join(">", c.Directions) + "+" + c.Button;
                if (!shapes.Add(shape)) issues.Add(new ValidationIssue(cs, "same directions and button as another command: " + shape));
            }
            var seen = new HashSet<string>();
            foreach (var s in p.StartInventory)
            {
                if (p.FindItem(s.ItemId) == null) issues.Add(new ValidationIssue("StartInventory", "unknown item '" + s.ItemId + "'"));
                if (s.Count <= 0) issues.Add(new ValidationIssue("StartInventory", "count must be > 0 for '" + s.ItemId + "'"));
                if (!seen.Add(s.ItemId)) issues.Add(new ValidationIssue("StartInventory", "item listed twice: '" + s.ItemId + "'"));
            }
            if (p.StartQuickslots == null || p.StartQuickslots.Length != 4)
                issues.Add(new ValidationIssue("RingProfile", "StartQuickslots must have 4 entries (Up, Right, Down, Left)"));
            else
                foreach (var q in p.StartQuickslots)
                    if (!string.IsNullOrEmpty(q) && !seen.Contains(q))
                        issues.Add(new ValidationIssue("StartQuickslots", "'" + q + "' is not in the start inventory"));
            return issues;
        }
    }
}

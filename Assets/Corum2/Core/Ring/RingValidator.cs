using System.Collections.Generic;

namespace Corum2.Core
{
    /// <summary>Load-time checks for ring data (spec 11.2). Failures stop the run; nothing is patched with defaults.</summary>
    public static class RingValidator
    {
        public static List<ValidationIssue> Validate(RingProfile p)
        {
            var issues = new List<ValidationIssue>();
            if (p == null) return issues;
            if (string.IsNullOrEmpty(p.Meta?.DomainId) || string.IsNullOrEmpty(p.Meta?.DataVersion))
                issues.Add(new ValidationIssue("RingProfile", "meta domainId/dataVersion missing"));
            if (!(p.SlowScale > 0f && p.SlowScale <= 1f))
                issues.Add(new ValidationIssue("RingProfile", "SlowScale must be in (0, 1]; 0 would pause the game (R-02-03: no full pause)"));
            if (p.SlotsPerPage != 8)
                issues.Add(new ValidationIssue("RingProfile", "SlotsPerPage must be 8 while slots map to 8 stick sectors (EXP-RING-02)"));
            if (p.CursorDeadzone <= 0f || p.CursorDeadzone >= 1f)
                issues.Add(new ValidationIssue("RingProfile", "CursorDeadzone must be in (0, 1)"));
            var ids = new HashSet<string>();
            foreach (var i in p.Items)
            {
                if (string.IsNullOrEmpty(i.Id) || !ids.Add(i.Id)) issues.Add(new ValidationIssue("RingProfile", "item id empty or duplicated: '" + i.Id + "'"));
                if (i.Cooldown < 0f) issues.Add(new ValidationIssue("Item " + i.Id, "negative cooldown"));
                if (i.Effect == ItemEffectType.Magic && p.FindMagic(i.MagicId) == null)
                    issues.Add(new ValidationIssue("Item " + i.Id, "magic '" + i.MagicId + "' not defined"));
                if (i.Kind == ItemKind.Quest && i.Effect != ItemEffectType.None)
                    issues.Add(new ValidationIssue("Item " + i.Id, "quest items have no use effect in WP-03"));
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

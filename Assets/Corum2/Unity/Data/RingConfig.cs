using System;
using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    [Serializable]
    public sealed class ItemEntry
    {
        public string id = "";
        public string displayName = "";
        [TextArea] public string description = "";
        public ItemKind kind;
        public ItemEffectType effect;
        [Tooltip("Heal amount (TEST-ITEM-01).")] public float amount;
        public string magicId = "";
        [Tooltip("Item cooldown after use, game seconds (EXP-ITEM-02, U-03).")] public float cooldown = 1f;
    }

    [Serializable]
    public sealed class MagicEntry
    {
        public string id = "";
        public MagicEffect effect;
        [Tooltip("Attack: test damage D; Heal: HP; Buff: damage multiplier (TEST-MAGIC-01).")] public float power;
        [Tooltip("Game seconds from request to effect.")] public float castTime;
        public float recovery = 0.2f;
        [Header("Attack effect")] public float active = 0.1f;
        public float hitboxOffsetX = 1.4f;
        public float hitboxOffsetY = 0.9f;
        public float hitboxWidth = 2.2f;
        public float hitboxHeight = 1.6f;
        [Tooltip("> 0 (R-06-06).")] public float knockback = 4f;
        public float hitstop = 0.04f;
        [Header("Buff effect")] public float buffDuration = 5f;

        public MagicDefinition ToCore() => new MagicDefinition
        {
            Id = id, Effect = effect, Power = power, CastTime = castTime, Recovery = recovery, Active = active,
            HitboxOffsetX = hitboxOffsetX, HitboxOffsetY = hitboxOffsetY, HitboxWidth = hitboxWidth, HitboxHeight = hitboxHeight,
            Knockback = knockback, Hitstop = hitstop, BuffDuration = buffDuration,
        };
    }

    /// <summary>Inspector form of MagicCommand (R-02-02; list and timing are test data, U-02).</summary>
    [Serializable]
    public sealed class CommandEntry
    {
        public string id = "";
        public string magicId = "";
        public List<Dir8> directions = new List<Dir8>();
        public LogicalAction button = LogicalAction.Attack;
        public float window = 0.6f;

        public MagicCommand ToCore()
        {
            var c = new MagicCommand { Id = id, MagicId = magicId, Button = button, Window = window };
            c.Directions.AddRange(directions);
            return c;
        }
    }

    [Serializable]
    public sealed class StackEntry
    {
        public string itemId = "";
        public int count = 1;
    }

    /// <summary>Ring/quickslot data (appendix 6: RingConfig / ItemDefinition). All values are experimental test data.</summary>
    [CreateAssetMenu(menuName = "Corum2/Ring Config", fileName = "RingConfig")]
    public sealed class RingConfig : ScriptableObject
    {
        public MetaFields meta = new MetaFields
        {
            domainId = "ring.config", dataVersion = "wp03-1",
            policyIds = new[] { "EXP-RING-01", "EXP-RING-02", "EXP-ITEM-02", "TEST-ITEM-01" },
            relatedRIds = new[] { "R-02-03", "R-02-04", "R-02-05", "R-02-06", "R-02-07", "R-02-08", "R-02-09", "R-02-10", "R-02-11", "R-05-05" },
        };
        [Tooltip("World time scale while the ring is open. 0.2 is the spec's proposal (EXP-RING-01).")]
        [Range(0.01f, 1f)] public float slowScale = 0.2f;
        public int slotsPerPage = 8;
        [Range(0.05f, 0.95f)] public float cursorDeadzone = 0.35f;
        public List<MagicEntry> magics = new List<MagicEntry>();
        [Header("Magic commands (R-02-02; EXP-CMD-01..06, CF-01 undecided)")] public List<CommandEntry> commands = new List<CommandEntry>();
        [Tooltip("EXP-CMD-02: a matched command takes the button press (normal action skipped).")] public bool commandTakesButton = true;
        public int minCommandDirections = 3;
        public List<ItemEntry> items = new List<ItemEntry>();
        public List<StackEntry> startInventory = new List<StackEntry>();
        [Tooltip("Up, Right, Down, Left. Empty = no item.")]
        public string[] startQuickslots = { "", "", "", "" };

        public RingProfile ToCore()
        {
            var p = new RingProfile { Meta = meta.ToCore(), SlowScale = slowScale, SlotsPerPage = slotsPerPage, CursorDeadzone = cursorDeadzone };
            foreach (var m in magics) p.Magics.Add(m.ToCore());
            foreach (var c in commands) p.Commands.Add(c.ToCore());
            p.CommandTakesButton = commandTakesButton;
            p.MinCommandDirections = minCommandDirections;
            foreach (var i in items)
                p.Items.Add(new ItemDefinition
                {
                    Id = i.id, Name = i.displayName, Description = i.description, Kind = i.kind, Effect = i.effect,
                    Amount = i.amount, MagicId = i.magicId, Cooldown = i.cooldown,
                });
            foreach (var s in startInventory) p.StartInventory.Add(new ItemStack { ItemId = s.itemId, Count = s.count });
            p.StartQuickslots = (string[])(startQuickslots ?? new string[0]).Clone();
            return p;
        }
    }
}

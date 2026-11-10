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
        public float power;
        [Tooltip("Game seconds from request to effect.")] public float castTime;
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
        public List<ItemEntry> items = new List<ItemEntry>();
        public List<StackEntry> startInventory = new List<StackEntry>();
        [Tooltip("Up, Right, Down, Left. Empty = no item.")]
        public string[] startQuickslots = { "", "", "", "" };

        public RingProfile ToCore()
        {
            var p = new RingProfile { Meta = meta.ToCore(), SlowScale = slowScale, SlotsPerPage = slotsPerPage, CursorDeadzone = cursorDeadzone };
            foreach (var m in magics) p.Magics.Add(new MagicDefinition { Id = m.id, Power = m.power, CastTime = m.castTime });
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

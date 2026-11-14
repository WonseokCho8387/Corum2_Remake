using System;
using System.Collections.Generic;

namespace Corum2.Core
{
    public enum ItemKind
    {
        Consumable,
        /// <summary>Consumable magic scroll (R-02-06, R-02-10). Uses a MagicDefinition shared with the magic command.</summary>
        MagicScroll,
        /// <summary>Cannot be discarded (R-02-08) and has no use effect in WP-03.</summary>
        Quest,
    }

    public enum ItemEffectType
    {
        None,
        Heal,
        /// <summary>Casts the referenced MagicDefinition through the same path as the magic command (R-02-10, WP-05).</summary>
        Magic,
        /// <summary>Revive item (R-06-19): only offered by the death prompt, never used from the ring or a quickslot
        /// (EXP-DEATH-05).</summary>
        Revive,
    }

    /// <summary>What a magic does when its cast time has passed (WP-05 temporary effects, TEST-MAGIC-01; real magic is P1).</summary>
    public enum MagicEffect
    {
        /// <summary>A magic-type hit in front of the player (R-06-06/12 rules apply: magic knockback, armor break).</summary>
        Attack,
        /// <summary>Restores Power HP.</summary>
        Heal,
        /// <summary>Player attack damage x Power for BuffDuration seconds.</summary>
        Buff,
        /// <summary>P1: the cast motion only; the effect is created by the P1 magic system at its commit (MAG-107).</summary>
        External,
    }

    /// <summary>
    /// One magic command (R-02-02): a direction sequence followed by an action button. Lists, buttons and windows are
    /// temporary test data (U-02); conflicts with other actions are CF-01 and handled by the explicit EXP-CMD policies.
    /// </summary>
    public sealed class MagicCommand
    {
        public string Id = "";
        public string MagicId = "";
        /// <summary>Directions in order (8-way sectors; None is not allowed).</summary>
        public readonly List<Dir8> Directions = new List<Dir8>();
        public LogicalAction Button = LogicalAction.Attack;
        /// <summary>Game seconds from the first direction of the sequence to the button press.</summary>
        public float Window = 0.6f;
    }

    /// <summary>One magic's base numbers. A command and a scroll of the same magic must read the same definition (R-02-10).</summary>
    public sealed class MagicDefinition
    {
        public string Id = "";
        public MagicEffect Effect;
        /// <summary>Attack: test damage D; Heal: HP; Buff: damage multiplier (TEST-MAGIC-01).</summary>
        public float Power;
        /// <summary>Game seconds from the request to the effect.</summary>
        public float CastTime;
        /// <summary>Motion lock after the effect (R-05-01 applies to the cast motion, EXP-MAGIC-01).</summary>
        public float Recovery = 0.2f;
        // Attack effect hit area (relative to the caster's feet, +X = facing)
        public float Active = 0.1f;
        public float HitboxOffsetX = 1.4f;
        public float HitboxOffsetY = 0.9f;
        public float HitboxWidth = 2.2f;
        public float HitboxHeight = 1.6f;
        /// <summary>Must be &gt; 0 for an attack magic (R-06-06).</summary>
        public float Knockback = 4f;
        public float Hitstop = 0.04f;
        public float BuffDuration = 5f;
    }

    public sealed class ItemDefinition
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public ItemKind Kind;
        public ItemEffectType Effect;
        public float Amount;
        public string MagicId = "";
        /// <summary>Item cooldown after this item is used, game seconds (EXP-ITEM-02, value U-03).</summary>
        public float Cooldown = 1f;

        public bool Usable => Kind != ItemKind.Quest && Effect != ItemEffectType.None && Effect != ItemEffectType.Revive;
        public bool Discardable => Kind != ItemKind.Quest;
    }

    public struct ItemStack
    {
        public string ItemId;
        public int Count;
    }

    /// <summary>Ring/quickslot numbers. All values are proposals or tuning values (spec 10.4: slow 20% is a proposal).</summary>
    public sealed class RingProfile
    {
        public DataMeta Meta = new DataMeta();
        /// <summary>World time scale while the ring is open (R-02-03; 0.2 is the spec's proposal, EXP-RING-01).</summary>
        public float SlowScale = 0.2f;
        /// <summary>Slots per page, one per 8-way sector (EXP-RING-02, U-03).</summary>
        public int SlotsPerPage = 8;
        /// <summary>Stick magnitude below which the cursor is neutral and nothing is selected (R-02-04).</summary>
        public float CursorDeadzone = 0.35f;
        public readonly List<ItemDefinition> Items = new List<ItemDefinition>();
        public readonly List<MagicDefinition> Magics = new List<MagicDefinition>();
        public readonly List<MagicCommand> Commands = new List<MagicCommand>();
        /// <summary>EXP-CMD-02 (CF-01 undecided): true = a recognised command takes the button press and the button's
        /// normal action does not run; false = the normal action runs and the command is only logged.</summary>
        public bool CommandTakesButton = true;
        /// <summary>EXP-CMD-01: shortest allowed direction sequence (keeps temporary commands away from 1-2 direction moves).</summary>
        public int MinCommandDirections = 3;
        public readonly List<ItemStack> StartInventory = new List<ItemStack>();
        /// <summary>Quickslot item ids at start, index = QuickSlot (Up, Right, Down, Left); "" = empty.</summary>
        public string[] StartQuickslots = { "", "", "", "" };

        public ItemDefinition FindItem(string id)
        {
            foreach (var i in Items) if (i.Id == id) return i;
            return null;
        }

        public MagicDefinition FindMagic(string id)
        {
            foreach (var m in Magics) if (m.Id == id) return m;
            return null;
        }
    }

    public enum QuickSlot
    {
        Up = 0,
        Right = 1,
        Down = 2,
        Left = 3,
    }

    public static class QuickSlotUtil
    {
        public static bool TryFromAction(LogicalAction a, out QuickSlot slot)
        {
            switch (a)
            {
                case LogicalAction.QuickUp: slot = QuickSlot.Up; return true;
                case LogicalAction.QuickRight: slot = QuickSlot.Right; return true;
                case LogicalAction.QuickDown: slot = QuickSlot.Down; return true;
                case LogicalAction.QuickLeft: slot = QuickSlot.Left; return true;
                default: slot = QuickSlot.Up; return false;
            }
        }

        public static LogicalAction ToAction(QuickSlot q)
        {
            switch (q)
            {
                case QuickSlot.Up: return LogicalAction.QuickUp;
                case QuickSlot.Right: return LogicalAction.QuickRight;
                case QuickSlot.Down: return LogicalAction.QuickDown;
                default: return LogicalAction.QuickLeft;
            }
        }
    }

    /// <summary>Owned items in a fixed order plus the four quickslots. Runtime state (not data).</summary>
    public sealed class Inventory
    {
        readonly RingProfile _p;
        readonly List<ItemStack> _stacks = new List<ItemStack>();
        public readonly string[] Quickslots = { "", "", "", "" };

        public Inventory(RingProfile p)
        {
            _p = p;
            foreach (var s in p.StartInventory) if (s.Count > 0) _stacks.Add(s);
            for (var i = 0; i < 4 && i < p.StartQuickslots.Length; i++) Quickslots[i] = p.StartQuickslots[i] ?? "";
        }

        public int StackCount => _stacks.Count;
        public IReadOnlyList<ItemStack> Stacks => _stacks;

        public int PageCount => Math.Max(1, (_stacks.Count + _p.SlotsPerPage - 1) / _p.SlotsPerPage);

        public int Count(string itemId)
        {
            foreach (var s in _stacks) if (s.ItemId == itemId) return s.Count;
            return 0;
        }

        /// <summary>Item in a ring slot of a page, or null for an empty slot.</summary>
        public string At(int page, int slot)
        {
            var i = page * _p.SlotsPerPage + slot;
            return slot >= 0 && slot < _p.SlotsPerPage && i >= 0 && i < _stacks.Count ? _stacks[i].ItemId : null;
        }

        /// <summary>Removes n units; an emptied stack leaves the ring (R-02-10) and the quickslots pointing at it
        /// are cleared (EXP-QS-02, U-03). Returns false if fewer than n are owned.</summary>
        public bool Remove(string itemId, int n, List<string> clearedSlots = null)
        {
            for (var i = 0; i < _stacks.Count; i++)
            {
                if (_stacks[i].ItemId != itemId) continue;
                if (_stacks[i].Count < n) return false;
                var left = _stacks[i].Count - n;
                if (left > 0) _stacks[i] = new ItemStack { ItemId = itemId, Count = left };
                else
                {
                    _stacks.RemoveAt(i);
                    for (var q = 0; q < 4; q++)
                        if (Quickslots[q] == itemId)
                        {
                            Quickslots[q] = "";
                            clearedSlots?.Add(((QuickSlot)q).ToString());
                        }
                }
                return true;
            }
            return false;
        }

        /// <summary>Test setup only (evidence scripts, logged by the caller): sets an owned count, adding the stack at the end
        /// or removing it.</summary>
        public void TestSetCount(string itemId, int n)
        {
            for (var i = 0; i < _stacks.Count; i++)
            {
                if (_stacks[i].ItemId != itemId) continue;
                if (n > 0) _stacks[i] = new ItemStack { ItemId = itemId, Count = n };
                else Remove(itemId, _stacks[i].Count);
                return;
            }
            if (n > 0) _stacks.Add(new ItemStack { ItemId = itemId, Count = n });
        }

        public string Describe()
        {
            var parts = new List<string>();
            foreach (var s in _stacks) parts.Add(s.ItemId + "x" + s.Count);
            return string.Join(",", parts);
        }

        public string DescribeQuickslots() =>
            "U=" + Quickslots[0] + ",R=" + Quickslots[1] + ",D=" + Quickslots[2] + ",L=" + Quickslots[3];
    }
}

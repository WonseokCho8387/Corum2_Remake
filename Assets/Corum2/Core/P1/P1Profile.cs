using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    public enum Rarity { Normal, Rare, Epic, Legendary }

    /// <summary>Story progress band that opens rarities (§3.3). Boundary events are content (EXP-P1-STORY-01).</summary>
    public enum StoryStage { Early, Mid, Late }

    public enum CardKind
    {
        /// <summary>Five base stats, fixed +n (GRO-11/12, BAL-DRAFT-01).</summary>
        Stat,
        /// <summary>Physical technique cards (GRO-15~25).</summary>
        Skill,
        /// <summary>Magic missile upgrades (MAG-07~39).</summary>
        Missile,
        /// <summary>One-time normal unlock of one of the other 10 spells (MAG-41/44).</summary>
        SpellUnlock,
        /// <summary>Per-spell, per-rarity function cards (MAG-46, 59~91).</summary>
        SpellUpgrade,
        /// <summary>Temporary buff (§5).</summary>
        Buff,
        /// <summary>Mandatory unlock M1~M4 (§3.4).</summary>
        Mandatory,
        /// <summary>HP/MP item fallback reward (GRO-31/36~40). Not a natural candidate.</summary>
        Fallback,
    }

    public enum SpellButton { X, A, B }

    /// <summary>Motion of a spell command, written for a right-facing character (§2.7).</summary>
    public enum MotionKind { MissileOriginal, QCF, QCB, DP, RDP, HCF, HCB }

    public enum SpellKind { Missile, LightOrb, DarkOrb, FireBall, FireWall, Heal, Shield, LightSword, DarkSword, Archangel, Demon }

    [Serializable]
    public sealed class NamedValue
    {
        public string Key = "";
        public float Value;

        public NamedValue() { }
        public NamedValue(string k, float v) { Key = k; Value = v; }
    }

    /// <summary>Per-character base values and permanent absolute caps (§1.1 A+C, §4.2). Numbers are [시험].</summary>
    [Serializable]
    public sealed class CharacterDef
    {
        public string Id = "volker";
        public int BaseMaxHp = 10;
        public int BaseMaxMp = 5;
        public int CapMaxHp = 100;
        public int CapMaxMp = 100;
        /// <summary>Caps for the three new stats are 미정 (§4.2); test values (EXP-P1-CAP-01).</summary>
        public int CapPhysAtk = 30;
        public int CapMagAtk = 30;
        public int CapDefense = 40;
        public int StartKarz = 100;
        /// <summary>Weapon base hit damage (origin sword 1), kept apart from permanent bonuses (§4.2).</summary>
        public float WeaponBaseDamage = 1f;
    }

    [Serializable]
    public sealed class RarityRow
    {
        public StoryStage Stage;
        public float Normal, Rare, Epic, Legendary;

        public float Get(Rarity r) => r == Rarity.Normal ? Normal : r == Rarity.Rare ? Rare : r == Rarity.Epic ? Epic : Legendary;
    }

    [Serializable]
    public sealed class MandatoryDef
    {
        /// <summary>Learn flag set when chosen (MagicMissile / DoubleJump / AirDash / AirDive).</summary>
        public string Id = "";
        public string CardId = "";
        /// <summary>First level-up (target level) at which it may be offered (GRO-41). Editor field, never hard-coded.</summary>
        public int FirstLevel = 2;
        /// <summary>M1..M4 fixed tie order (§3.5 step 3).</summary>
        public int Order;
        public bool StoryGateEnabled;
        public List<string> PrerequisiteMandatoryIds = new List<string>();
    }

    /// <summary>One growth axis changed by a card. Value is the accumulated permanent bonus of that axis.</summary>
    [Serializable]
    public sealed class CardAxis
    {
        public string Axis = "";
        public float Delta;
        /// <summary>Absolute limit of the accumulated bonus: max for Delta &gt; 0, min for Delta &lt; 0. Ignored for axes
        /// whose limit comes from the character or spell data (stat.*, mm.*).</summary>
        public float Limit;
        /// <summary>Amount granted by the first card of this axis when it differs from later cards (0 = use Delta).</summary>
        public float First;

        public CardAxis() { }
        public CardAxis(string a, float d, float l, float first = 0f) { Axis = a; Delta = d; Limit = l; First = first; }
    }

    [Serializable]
    public sealed class CardDef
    {
        public string Id = "";
        public CardKind Kind;
        public Rarity Rarity;
        /// <summary>false = temporary (buff) slot type for the 75/25 split (GRO-07).</summary>
        public bool Permanent = true;
        /// <summary>Learn flag the character must own (technique / spell).</summary>
        public string RequiresLearned = "";
        /// <summary>Learn flag this card grants.</summary>
        public string Unlocks = "";
        public bool OneTime;
        public int MinLevel;
        /// <summary>Spell whose command MP cost the card changes (MAG-33/55/63/101).</summary>
        public string SpellId = "";
        public int MpDelta;
        public string BuffId = "";
        public float Weight = 1f;
        public bool ExcludedFromMerchantPack;
        public List<CardAxis> Axes = new List<CardAxis>();
        public string Label = "";
    }

    [Serializable]
    public sealed class FallbackDef
    {
        public string CardId = "FallbackHPItem";
        /// <summary>Remake catalog item id. The real catalog is not decided; test ids p1test.* (EXP-P1-ITEM-01).</summary>
        public string ItemId = "";
        public int Count = 1;
        public bool Enabled = true;
    }

    [Serializable]
    public sealed class BuffDef
    {
        public string Id = "";
        /// <summary>Stat family whose temporary bonuses are summed and capped together (BUF-06).</summary>
        public string Family = "";
        public float NormalStrength;
        public int MaxStacks = 3;
        public float FamilyCap;
        public string Label = "";
    }

    [Serializable]
    public sealed class BuffRules
    {
        public float NormalDuration = 180f;
        public float LongDuration = 240f;
        public float StrongMultiplier = 1.5f;
        /// <summary>BUF-04: offer weight multiplier per active stack of the same buff (never 0 below the stack cap).</summary>
        public float ActiveStackWeightFactor = 0.5f;

        public float Duration(Rarity r) => r == Rarity.Rare || r == Rarity.Legendary ? LongDuration : NormalDuration;
        public float Strength(BuffDef b, Rarity r) => b.NormalStrength * (r == Rarity.Epic || r == Rarity.Legendary ? StrongMultiplier : 1f);
    }

    [Serializable]
    public sealed class SpellDef
    {
        public string Id = "";
        public string Name = "";
        public SpellKind Kind;
        public MotionKind Motion;
        public SpellButton Button;
        public int BaseMp = 1;
        /// <summary>Per-spell minimum command cost, &gt;= 1 (MAG-09/102).</summary>
        public int MinMp = 1;
        /// <summary>Unlock card minimum level (MAG-41). The missile uses the mandatory path instead.</summary>
        public int MinLevel = 1;
        public float CastTime = 0.25f;
        public float Recovery = 0.2f;
        /// <summary>Original EXE reference power (comparison seed only, never copied into new effects).</summary>
        public float OriginPower;
        /// <summary>Spell-specific tuning values (damage, speed, radius, duration ...). Editor list.</summary>
        public List<NamedValue> Params = new List<NamedValue>();

        public float P(string key, float dflt = 0f)
        {
            foreach (var p in Params) if (p.Key == key) return p.Value;
            return dflt;
        }

        public SpellDef Set(string key, float v)
        {
            foreach (var p in Params) if (p.Key == key) { p.Value = v; return this; }
            Params.Add(new NamedValue(key, v));
            return this;
        }
    }

    [Serializable]
    public sealed class TestItemDef
    {
        public string Id = "";
        public string Name = "";
        public int HpRestore;
        public int MpRestore;
        /// <summary>Extra effect parts of one item (BUF-11: parts of one resource are summed before the one floor).</summary>
        public List<int> HpParts = new List<int>();
        public List<int> MpParts = new List<int>();
        public bool Revive;

        public int HpTotal { get { var t = HpRestore; foreach (var p in HpParts) t += p; return t; } }
        public int MpTotal { get { var t = MpRestore; foreach (var p in MpParts) t += p; return t; } }
        /// <summary>Spell id for a scroll item ("" = not a scroll).</summary>
        public string ScrollSpellId = "";
    }

    [Serializable]
    public sealed class MerchantProduct
    {
        public string Kind = "PermPack"; // PermPack / TempPack / Buff / RerollToken
        public float PriceRatio;
        public int MinPrice;
        public float Weight = 1f;
        public float FirstVisitWeight = 1f;
    }

    [Serializable]
    public sealed class MerchantProfile
    {
        public float BaseChance = 0.15f;
        public float ChanceStep = 0.10f;
        public float MaxChance = 1f;
        public int MinStock = 3;
        public int MaxStock = 5;
        public int PermPackUseLimit = 5;
        public int Lv50PriceReference = 500;
        public List<MerchantProduct> Products = new List<MerchantProduct>();
    }

    [Serializable]
    public sealed class DamageProfile
    {
        public float DefenseK = 20f;
        public float TempDamageReductionCap = 0.25f;
        public int MinDamage = 1;
        public float GuardMultiplier = 0.10f;
    }

    /// <summary>
    /// All P1 data (single source of numbers; nothing hard-coded in the systems). [확정] rules live in code, [시험] numbers
    /// here, original EXE values in Origin*/OriginPower fields. Built by P1Defaults; exposed to the editor by P1ProfileAsset.
    /// </summary>
    [Serializable]
    public sealed class P1Profile
    {
        public string Version = "p1-test-1";
        public List<CharacterDef> Characters = new List<CharacterDef>();
        public int MaxLevel = 50;
        /// <summary>EXP needed at level L to reach L+1, index L (0 unused). Origin seed T(L)-T(L-1).</summary>
        public List<int> ExpToNext = new List<int>();
        public float Lv50CurrencyRate = 1f;
        public float PermanentSlotWeight = 0.75f;
        public float TemporarySlotWeight = 0.25f;
        public List<RarityRow> StoryRarity = new List<RarityRow>();
        public List<MandatoryDef> Mandatory = new List<MandatoryDef>();
        public List<CardDef> Cards = new List<CardDef>();
        public List<FallbackDef> Fallbacks = new List<FallbackDef>();
        public float EpicMissileVariantWeight = 0.5f;
        public int RerollsPerLevel = 1;
        // magic missile permanent model (MAG-08/12/37, BAL-DRAFT-02/03)
        public int MissileBaseProjectiles = 1;
        public int MissileMaxProjectiles = 5;
        public float MissilePermanentDamageCap = 20f;
        public float HomingAngleFirst = 12f, HomingAngleStep = 12f, HomingAngleCap = 48f;
        public float HomingRangeFirst = 5f, HomingRangeStep = 1.5f, HomingRangeCap = 11f;
        public float HomingTurnFirst = 40f, HomingTurnStep = 25f, HomingTurnCap = 140f;
        public List<SpellDef> Spells = new List<SpellDef>();
        public List<BuffDef> Buffs = new List<BuffDef>();
        public BuffRules BuffRules = new BuffRules();
        public MerchantProfile Merchant = new MerchantProfile();
        public DamageProfile Damage = new DamageProfile();
        public List<TestItemDef> Items = new List<TestItemDef>();
        /// <summary>Merchant regions and registered safe spawns (sandbox content, EXP-P1-REGION-01).</summary>
        public List<RegionDef> Regions = new List<RegionDef>();
        /// <summary>Distinct item stacks the bag holds (inventory size is 미정, EXP-P1-BAG-01).</summary>
        public int BagStackLimit = 16;
        public int BagCountLimit = 99;
        // command input timing seeds (§2.7: 0.45 s motion, 0.15 s button) [시험]
        public float MotionWindow = 0.45f;
        public float ButtonWindow = 0.15f;
        public float MissileWindow = 0.6f;

        public CharacterDef Character(string id)
        {
            foreach (var c in Characters) if (c.Id == id) return c;
            return Characters.Count > 0 ? Characters[0] : null;
        }

        public CardDef Card(string id)
        {
            foreach (var c in Cards) if (c.Id == id) return c;
            return null;
        }

        public SpellDef Spell(string id)
        {
            foreach (var s in Spells) if (s.Id == id) return s;
            return null;
        }

        public BuffDef Buff(string id)
        {
            foreach (var b in Buffs) if (b.Id == id) return b;
            return null;
        }

        public FallbackDef Fallback(string cardId)
        {
            foreach (var f in Fallbacks) if (f.CardId == cardId) return f;
            return null;
        }

        public TestItemDef Item(string id)
        {
            foreach (var i in Items) if (i.Id == id) return i;
            return null;
        }

        public RarityRow Rarities(StoryStage s)
        {
            foreach (var r in StoryRarity) if (r.Stage == s) return r;
            return StoryRarity.Count > 0 ? StoryRarity[0] : new RarityRow { Normal = 1 };
        }

        public int ExpNeeded(int level) => level >= 1 && level < ExpToNext.Count ? ExpToNext[level] : int.MaxValue;

        public MerchantProduct Product(string kind)
        {
            foreach (var p in Merchant.Products) if (p.Kind == kind) return p;
            return null;
        }

        /// <summary>Content validation problems (GRO-36 fallback refs, unique ids, positive counts). Empty = valid.</summary>
        public List<string> Validate()
        {
            var errs = new List<string>();
            var ids = new HashSet<string>();
            foreach (var c in Cards)
                if (!ids.Add(c.Id)) errs.Add("DuplicateCardId:" + c.Id);
            foreach (var f in Fallbacks)
            {
                if (!ids.Add(f.CardId)) errs.Add("DuplicateCardId:" + f.CardId);
                if (f.Count <= 0) errs.Add("FallbackCountNotPositive:" + f.CardId);
                if (!f.Enabled) errs.Add("FallbackDisabled:" + f.CardId);
                if (Item(f.ItemId) == null) errs.Add("FallbackItemMissing:" + f.CardId + "->" + f.ItemId);
            }
            if (Fallbacks.Count < 2) errs.Add("FallbackNeedsTwoKinds");
            foreach (var m in Mandatory)
                if (Card(m.CardId) == null) errs.Add("MandatoryCardMissing:" + m.Id);
            if (ExpToNext.Count < MaxLevel) errs.Add("ExpCurveShort");
            foreach (var s in Spells)
                if (s.MinMp < 1) errs.Add("SpellMinMpBelowOne:" + s.Id);
            return errs;
        }
    }
}

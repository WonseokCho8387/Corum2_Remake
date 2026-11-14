using System.Collections.Generic;

namespace Corum2.Core.P1
{
    /// <summary>
    /// Builds the P1 test profile from the master's [시험]/[시험 초안] tables (v0.1.38: §3.3, §4.2, §4.5, §4.7~4.9, §5, §6,
    /// BAL-DRAFT-01~08). Every number is editable data; rules do not read these constants directly.
    /// </summary>
    public static class P1Defaults
    {
        public const string MissileId = "spell.missile";
        static readonly Rarity[] All = { Rarity.Normal, Rarity.Rare, Rarity.Epic, Rarity.Legendary };

        public static string R(Rarity r) => r == Rarity.Normal ? "N" : r == Rarity.Rare ? "R" : r == Rarity.Epic ? "E" : "L";

        public static P1Profile Create()
        {
            var p = new P1Profile();
            p.Characters.Add(new CharacterDef());
            // EXP: origin T(L)=100+50L(L-1) cumulative; per level need = T(L)-T(L-1), L=1 -> 100 (BL-03)
            p.ExpToNext.Add(0);
            for (var l = 1; l <= 50; l++) p.ExpToNext.Add(l == 1 ? 100 : 100 * (l - 1));
            p.StoryRarity.Add(new RarityRow { Stage = StoryStage.Early, Normal = 80, Rare = 20, Epic = 0, Legendary = 0 });
            p.StoryRarity.Add(new RarityRow { Stage = StoryStage.Mid, Normal = 70, Rare = 23, Epic = 7, Legendary = 0 });
            p.StoryRarity.Add(new RarityRow { Stage = StoryStage.Late, Normal = 70, Rare = 22, Epic = 7, Legendary = 1 });

            Spells(p);
            Mandatory(p);
            Stats(p);
            Skills(p);
            Missile(p);
            SpellCards(p);
            Buffs(p);
            Items(p);
            Merchant(p);
            return p;
        }

        static void Mandatory(P1Profile p)
        {
            void M(string id, int lv, int order, string label)
            {
                var cardId = "card.mandatory." + id;
                p.Mandatory.Add(new MandatoryDef { Id = id, CardId = cardId, FirstLevel = lv, Order = order });
                p.Cards.Add(new CardDef { Id = cardId, Kind = CardKind.Mandatory, Rarity = Rarity.Normal, Unlocks = id, OneTime = true, ExcludedFromMerchantPack = true, Label = label, SpellId = id == "MagicMissile" ? MissileId : "" });
            }
            M("MagicMissile", 2, 1, "매직 미사일 습득");
            M("DoubleJump", 4, 2, "2단 점프 습득");
            M("AirDash", 6, 3, "공중 직선 돌진 습득");
            M("AirDive", 8, 4, "공중 낙하 강공격 습득");
        }

        static void Stats(P1Profile p)
        {
            void S(string stat, string label, params int[] d)
            {
                for (var i = 0; i < 4; i++)
                    p.Cards.Add(new CardDef
                    {
                        Id = "card.stat." + stat + "." + R(All[i]), Kind = CardKind.Stat, Rarity = All[i], Label = label + " +" + d[i],
                        Axes = { new CardAxis("stat." + stat, d[i], 0) },
                    });
            }
            S("MaxHp", "최대 HP", 2, 3, 5, 8);
            S("MaxMp", "최대 MP", 1, 2, 3, 5);
            S("PhysAtk", "물리 공격", 1, 2, 3, 5);
            S("MagAtk", "마법 공격", 1, 2, 3, 5);
            S("Defense", "방어", 1, 2, 3, 5);
        }

        static void Skills(P1Profile p)
        {
            void K(string skill, string req, string label, float cap, params float[] d)
            {
                for (var i = 0; i < 4; i++)
                    p.Cards.Add(new CardDef
                    {
                        Id = "card.skill." + skill + "." + R(All[i]), Kind = CardKind.Skill, Rarity = All[i], RequiresLearned = req,
                        Label = label + " +" + d[i], Axes = { new CardAxis("skill." + skill + ".dmg", d[i], cap) },
                    });
            }
            K("groundBasic", "", "지상 4연타 전타 피해", 10, 1, 2, 3, 5);
            K("airBasic", "", "공중 4연타 전타 피해", 10, 1, 2, 3, 5);
            K("airDash", "AirDash", "공중 돌진 피해", 12, 1, 2, 3, 5);
            K("airDiveDowned", "AirDive", "낙하 강공격 다운 추가 피해", 10, 1, 2, 3, 5);
            K("downAttack", "", "다운 공격 피해", 10, 1, 2, 3, 5);
            float[] h = { 0.15f, 0.25f, 0.40f, 0.60f }, t = { 0.05f, 0.09f, 0.14f, 0.20f };
            for (var i = 0; i < 4; i++)
                p.Cards.Add(new CardDef
                {
                    Id = "card.skill.launcher." + R(All[i]), Kind = CardKind.Skill, Rarity = All[i], Label = "런처 띄우기 높이/체공",
                    Axes = { new CardAxis("skill.launcher.height", h[i], 1.5f), new CardAxis("skill.launcher.time", t[i], 0.5f) },
                });
        }

        static void Missile(P1Profile p)
        {
            CardDef C(string id, Rarity r, string label, int mp, params CardAxis[] ax)
            {
                var c = new CardDef { Id = "card.missile." + id, Kind = CardKind.Missile, Rarity = r, RequiresLearned = "MagicMissile", SpellId = MissileId, MpDelta = mp, Label = label };
                c.Axes.AddRange(ax);
                p.Cards.Add(c);
                return c;
            }
            C("N", Rarity.Normal, "매직 미사일 피해+2 / MP+1", 1, new CardAxis("mm.dmg", 2, 0));
            C("R", Rarity.Rare, "매직 미사일 피해+3", 0, new CardAxis("mm.dmg", 3, 0));
            C("E.proj", Rarity.Epic, "매직 미사일 투사체+1", 0, new CardAxis("mm.proj", 1, 0));
            C("E.homing", Rarity.Epic, "매직 미사일 유도 강화", 0, new CardAxis("mm.homing", 1, 0));
            C("L", Rarity.Legendary, "매직 미사일 피해+5 / MP-1", -1, new CardAxis("mm.dmg", 5, 0));
        }

        static SpellDef Sp(P1Profile p, string id, string name, SpellKind k, MotionKind m, SpellButton b, int mp, int lv, float origin)
        {
            var s = new SpellDef { Id = id, Name = name, Kind = k, Motion = m, Button = b, BaseMp = mp, MinMp = 1, MinLevel = lv, OriginPower = origin };
            p.Spells.Add(s);
            return s;
        }

        static void Spells(P1Profile p)
        {
            Sp(p, MissileId, "매직 미사일", SpellKind.Missile, MotionKind.MissileOriginal, SpellButton.X, 1, 2, 3)
                .Set("damage", 3).Set("speed", 8).Set("lifetime", 2).Set("radius", 0.2f).Set("spreadDeg", 6);
            Sp(p, "spell.light_orb", "빛의 구", SpellKind.LightOrb, MotionKind.QCF, SpellButton.X, 2, 4, 5)
                .Set("damage", 5).Set("speed", 11).Set("lifetime", 2).Set("radius", 0.25f).Set("chainRadius", 3).Set("extraShotInterval", 0.12f);
            Sp(p, "spell.dark_orb", "어둠의 구", SpellKind.DarkOrb, MotionKind.QCB, SpellButton.X, 3, 6, 10)
                .Set("damage", 10).Set("speed", 6).Set("lifetime", 2.5f).Set("radius", 0.28f).Set("shockRadius", 1.0f);
            Sp(p, "spell.heal", "회복", SpellKind.Heal, MotionKind.QCF, SpellButton.B, 3, 8, 0)
                .Set("heal", 5).Set("hotPerSecond", 1);
            Sp(p, "spell.fire_ball", "화이어 볼", SpellKind.FireBall, MotionKind.DP, SpellButton.X, 5, 14, 15)
                .Set("damage", 15).Set("radius", 1.25f).Set("speed", 9).Set("lifetime", 1.5f).Set("projRadius", 0.3f)
                .Set("burnTick", 1).Set("burnInterval", 1).Set("delay", 0.4f).Set("delayedRatio", 0.5f);
            Sp(p, "spell.light_sword", "빛의 검", SpellKind.LightSword, MotionKind.QCF, SpellButton.A, 6, 16, 0)
                .Set("bonus", 3).Set("duration", 12).Set("waveSpeed", 10).Set("waveLifetime", 0.6f);
            Sp(p, "spell.dark_sword", "어둠의 검", SpellKind.DarkSword, MotionKind.QCB, SpellButton.A, 7, 18, 0)
                .Set("lifesteal", 0.25f).Set("duration", 12);
            Sp(p, "spell.shield", "매직 실드", SpellKind.Shield, MotionKind.DP, SpellButton.A, 10, 28, 0)
                .Set("absorb", 10).Set("duration", 12);
            Sp(p, "spell.archangel", "대천사 소환", SpellKind.Archangel, MotionKind.HCF, SpellButton.X, 25, 34, 30)
                .Set("duration", 18).Set("extend", 18).Set("durationCap", 36).Set("attack", 2).Set("attackInterval", 3)
                .Set("heal", 5).Set("healInterval", 4).Set("healThreshold", 0.5f).Set("range", 6);
            Sp(p, "spell.demon", "악마 소환", SpellKind.Demon, MotionKind.HCB, SpellButton.X, 30, 36, 40)
                .Set("duration", 18).Set("extend", 18).Set("durationCap", 36).Set("attack", 6).Set("attackInterval", 2)
                .Set("aoeRadius", 1.0f).Set("flameTick", 1).Set("flameInterval", 1).Set("range", 6).Set("meleeReach", 1.2f);
            Sp(p, "spell.fire_wall", "화이어 월", SpellKind.FireWall, MotionKind.RDP, SpellButton.X, 15, 38, 10)
                .Set("tick", 2).Set("interval", 0.75f).Set("duration", 4).Set("width", 1.5f).Set("height", 2.2f)
                .Set("distance", 2.0f).Set("spacing", 1.2f);
        }

        static void SpellCards(P1Profile p)
        {
            foreach (var s in p.Spells)
            {
                if (s.Id == MissileId) continue;
                p.Cards.Add(new CardDef
                {
                    Id = "card.spell." + Short(s.Id) + ".unlock", Kind = CardKind.SpellUnlock, Rarity = Rarity.Normal, OneTime = true,
                    Unlocks = s.Id, MinLevel = s.MinLevel, SpellId = s.Id, Label = s.Name + " 해금",
                });
            }
            void U(string spell, Rarity r, string label, int mp, params CardAxis[] ax)
            {
                var c = new CardDef
                {
                    Id = "card.spell." + Short(spell) + "." + R(r), Kind = CardKind.SpellUpgrade, Rarity = r, RequiresLearned = spell,
                    SpellId = spell, MpDelta = mp, Label = label,
                };
                foreach (var a in ax) { a.Axis = spell + "." + a.Axis; c.Axes.Add(a); }
                p.Cards.Add(c);
            }
            const string lo = "spell.light_orb", dk = "spell.dark_orb", fb = "spell.fire_ball", fw = "spell.fire_wall";
            const string he = "spell.heal", sh = "spell.shield", ls = "spell.light_sword", ds = "spell.dark_sword", aa = "spell.archangel", de = "spell.demon";
            U(lo, Rarity.Normal, "빛의 구 피해+1", 0, new CardAxis("dmg", 1, 10));
            U(lo, Rarity.Rare, "빛의 구 속도+1.5", 0, new CardAxis("speed", 1.5f, 5));
            U(lo, Rarity.Epic, "빛의 구 연쇄+1", 0, new CardAxis("chain", 1, 3));
            U(lo, Rarity.Legendary, "빛의 구 연속 발사+1", 1, new CardAxis("extraShots", 1, 2));
            U(dk, Rarity.Normal, "어둠의 구 피해+2", 0, new CardAxis("dmg", 2, 14));
            U(dk, Rarity.Rare, "어둠의 구 크기+0.08", 0, new CardAxis("radius", 0.08f, 0.32f));
            U(dk, Rarity.Epic, "어둠의 구 관통+1", 1, new CardAxis("pierce", 1, 2));
            U(dk, Rarity.Legendary, "어둠의 구 충격파", 1, new CardAxis("shock", 2, 8));
            U(fb, Rarity.Normal, "화이어 볼 폭발 피해+2", 0, new CardAxis("dmg", 2, 12));
            U(fb, Rarity.Rare, "화이어 볼 반경+0.25", 0, new CardAxis("radius", 0.25f, 1.25f));
            U(fb, Rarity.Epic, "화이어 볼 화상", 1, new CardAxis("burn", 1, 5, 3));
            U(fb, Rarity.Legendary, "화이어 볼 지연 폭발+1", 1, new CardAxis("delayed", 1, 2));
            U(fw, Rarity.Normal, "화이어 월 틱 피해+1", 0, new CardAxis("tick", 1, 3));
            U(fw, Rarity.Rare, "화이어 월 너비+0.4", 0, new CardAxis("width", 0.4f, 1.5f));
            U(fw, Rarity.Epic, "화이어 월 지속+1초", 0, new CardAxis("duration", 1, 4));
            U(fw, Rarity.Legendary, "화이어 월 추가 벽+1", 2, new CardAxis("extraWalls", 1, 1));
            U(he, Rarity.Normal, "회복량+2", 0, new CardAxis("heal", 2, 20));
            U(he, Rarity.Rare, "회복 MP-1", -1);
            U(he, Rarity.Epic, "회복 시 중독·혼란 해제", 0, new CardAxis("cure", 1, 1));
            U(he, Rarity.Legendary, "지속 회복", 0, new CardAxis("hot", 1, 6, 3));
            U(sh, Rarity.Normal, "실드 흡수+3", 0, new CardAxis("absorb", 3, 20));
            U(sh, Rarity.Rare, "실드 지속+3초", 0, new CardAxis("duration", 3, 12));
            U(sh, Rarity.Epic, "실드 중 상태이상 차단", 0, new CardAxis("statusBlock", 1, 1));
            U(sh, Rarity.Legendary, "실드 흡수+6", 0, new CardAxis("absorb", 6, 20));
            U(ls, Rarity.Normal, "빛의 검 공격+1", 0, new CardAxis("bonus", 1, 7));
            U(ls, Rarity.Rare, "빛의 검 지속+3초", 0, new CardAxis("duration", 3, 12));
            U(ls, Rarity.Epic, "빛의 검 4타 빛 피해+2", 0, new CardAxis("lastHit", 2, 10));
            U(ls, Rarity.Legendary, "빛의 검 4타 검기", 0, new CardAxis("wave", 2, 9, 3));
            U(ds, Rarity.Normal, "어둠의 검 흡혈+5%p", 0, new CardAxis("rate", 0.05f, 0.25f));
            U(ds, Rarity.Rare, "어둠의 검 지속+3초", 0, new CardAxis("duration", 3, 12));
            U(ds, Rarity.Epic, "어둠의 검 처치 회복", 0, new CardAxis("killHeal", 1, 6, 2));
            U(ds, Rarity.Legendary, "어둠의 검 연속 적중 흡혈", 0, new CardAxis("comboRate", 0.05f, 0.25f));
            U(aa, Rarity.Normal, "대천사 회복량+2", 0, new CardAxis("heal", 2, 7));
            U(aa, Rarity.Rare, "대천사 회복 간격-0.5초", 0, new CardAxis("healInterval", -0.5f, -2f));
            U(aa, Rarity.Epic, "대천사 상태이상 해제", 0, new CardAxis("cure", 1, 1));
            U(aa, Rarity.Legendary, "대천사 보호막", 0, new CardAxis("shield", 2, 12, 4));
            U(de, Rarity.Normal, "악마 공격+2", 0, new CardAxis("attack", 2, 8));
            U(de, Rarity.Rare, "악마 공격 간격-0.25초", 0, new CardAxis("attackInterval", -0.25f, -1f));
            U(de, Rarity.Epic, "악마 범위 공격", 0, new CardAxis("aoe", 1, 6, 2));
            U(de, Rarity.Legendary, "악마 흑염", 0, new CardAxis("flame", 1, 6, 3));
        }

        public static string Short(string spellId) => spellId.StartsWith("spell.") ? spellId.Substring(6) : spellId;

        static void Buffs(P1Profile p)
        {
            void B(string id, string fam, float s, float cap, string label)
            {
                p.Buffs.Add(new BuffDef { Id = id, Family = fam, NormalStrength = s, FamilyCap = cap, MaxStacks = 3, Label = label });
                foreach (var r in All)
                    p.Cards.Add(new CardDef { Id = "card.buff." + id + "." + R(r), Kind = CardKind.Buff, Rarity = r, Permanent = false, BuffId = id, Label = label });
            }
            B("BF-PATK", "PhysAtk", 0.08f, 0.30f, "투지");
            B("BF-DEF", "DamageReduction", 0.08f, 0.25f, "강철 피부");
            B("BF-MATK", "MagAtk", 0.10f, 0.30f, "마력 증폭");
            B("BF-MPCOST", "MpCost", 0.10f, 0.30f, "마력 절약");
            B("BF-AIR", "AirAtk", 0.10f, 0.30f, "공중 지배");
            B("BF-MOVE", "Move", 0.05f, 0.15f, "질풍");
            B("BF-ASPD", "AttackSpeed", 0.05f, 0.20f, "신속");
            B("BF-ITEM", "ItemHeal", 0.10f, 0.30f, "연금술의 축복");
        }

        static void Items(P1Profile p)
        {
            p.Items.Add(new TestItemDef { Id = "p1test.potion.hp", Name = "치료제(시험)", HpRestore = 5 });
            p.Items.Add(new TestItemDef { Id = "p1test.potion.mp", Name = "소마석(시험)", MpRestore = 8 });
            p.Items.Add(new TestItemDef { Id = "p1test.potion.mix", Name = "혼합 회복약(시험)", HpParts = { 2, 1 }, MpParts = { 3, 2 } });
            p.Items.Add(new TestItemDef { Id = "p1test.revive", Name = "부활석(시험)", Revive = true });
            foreach (var s in p.Spells)
                p.Items.Add(new TestItemDef { Id = "p1test.scroll." + Short(s.Id), Name = s.Name + " 스크롤(시험)", ScrollSpellId = s.Id });
            p.Fallbacks.Add(new FallbackDef { CardId = "FallbackHPItem", ItemId = "p1test.potion.hp", Count = 1 });
            p.Fallbacks.Add(new FallbackDef { CardId = "FallbackMPItem", ItemId = "p1test.potion.mp", Count = 1 });
        }

        static void Merchant(P1Profile p)
        {
            p.Merchant.Products.Add(new MerchantProduct { Kind = "PermPack", PriceRatio = 0.80f, MinPrice = 120, Weight = 1, FirstVisitWeight = 4 });
            p.Merchant.Products.Add(new MerchantProduct { Kind = "TempPack", PriceRatio = 0.40f, MinPrice = 70, Weight = 1, FirstVisitWeight = 1 });
            p.Merchant.Products.Add(new MerchantProduct { Kind = "Buff", PriceRatio = 0.35f, MinPrice = 60, Weight = 1, FirstVisitWeight = 1 });
            p.Merchant.Products.Add(new MerchantProduct { Kind = "RerollToken", PriceRatio = 0.45f, MinPrice = 80, Weight = 1, FirstVisitWeight = 1 });
        }
    }
}

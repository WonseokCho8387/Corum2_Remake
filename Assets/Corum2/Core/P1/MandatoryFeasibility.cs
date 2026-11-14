using System.Collections.Generic;
using System.Text;

namespace Corum2.Core.P1
{
    /// <summary>
    /// GRO-35/43 content pre-check: with the current mandatory first levels, does every mandatory reach its forced
    /// 1-card screen (two shown declines, then forced) before Lv50 in the worst case? The worst-case player declines
    /// every shown mandatory whenever another card could be chosen, and takes the forced one on a 1-card screen. Uses the
    /// generator's own budget / waiting order, with natural cards removed (they never change mandatory pressure).
    /// </summary>
    public static class MandatoryFeasibility
    {
        public sealed class Result
        {
            public bool Ok;
            public readonly Dictionary<string, int> LearnedAtLevel = new Dictionary<string, int>();
            public string Detail = "";
        }

        public static Result Check(P1Profile p)
        {
            var only = new P1Profile
            {
                Characters = p.Characters, Mandatory = p.Mandatory, Spells = p.Spells, StoryRarity = p.StoryRarity,
                ExpToNext = p.ExpToNext, MaxLevel = p.MaxLevel,
            };
            foreach (var m in p.Mandatory)
            {
                var c = p.Card(m.CardId);
                if (c != null) only.Cards.Add(c);
            }
            var gen = new GrowthOfferGenerator(new GrowthRules(only));
            var g = P1Progress.New(only);
            var res = new Result { Ok = true };
            var log = new StringBuilder();
            for (var target = 2; target <= p.MaxLevel; target++)
            {
                var offer = gen.Generate(g, target);
                var pick = offer.Budget == 1 && offer.MandatoryShown.Count > 0 ? offer.MandatoryShown[0] : null;
                foreach (var mid in offer.MandatoryShown)
                {
                    var m = gen.MandatoryByCard(mid);
                    if (mid == pick)
                    {
                        g.Learned.Add(m.Id);
                        res.LearnedAtLevel[m.Id] = target;
                    }
                    else g.M(m.Id).DeclineCount++;
                }
                if (offer.MandatoryShown.Count > 0)
                    log.Append("Lv").Append(target).Append('[').Append(offer.Budget).Append("]:").Append(string.Join("+", offer.MandatoryShown))
                        .Append(pick != null ? "->" + pick : "").Append("; ");
                g.Level = target;
            }
            foreach (var m in p.Mandatory)
                if (!res.LearnedAtLevel.ContainsKey(m.Id))
                {
                    res.Ok = false;
                    log.Append("NOT_GUARANTEED:").Append(m.Id).Append("; ");
                }
            res.Detail = log.ToString().Trim();
            return res;
        }
    }
}

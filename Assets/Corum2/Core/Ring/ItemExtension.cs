namespace Corum2.Core
{
    /// <summary>Optional P1 hook into item use (null in P0 scenes).</summary>
    public interface IItemExtension
    {
        /// <summary>Applies a heal item (HP/MP, BF-ITEM, BUF-11). Returns a log description.</summary>
        string ItemHeal(ItemDefinition item);

        /// <summary>Starts a P1 scroll cast without consuming the scroll. Null = started, else the reject code.</summary>
        string CastScroll(ItemDefinition item, long requestId);
    }
}

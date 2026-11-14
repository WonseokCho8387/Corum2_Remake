namespace Corum2.Core.P1
{
    /// <summary>Wandering magic merchant (MER-01~30). Filled in by P1-D.</summary>
    public sealed partial class MerchantSystem
    {
        public bool ShopOpen { get; private set; }

        public void Interrupt(string reason)
        {
            ShopOpen = false;
        }

        public void HandleInput(in InputEvent e) { }
    }
}

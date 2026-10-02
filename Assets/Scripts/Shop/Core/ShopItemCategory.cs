namespace Woodsmen.Shop
{
    /// <summary>
    /// Categories of items available in shops.
    /// </summary>
    public enum ShopItemCategory
    {
        /// <summary>Combat gear and specialized weaponry.</summary>
        Weapons = 0,

        /// <summary>Elemental or status attributes (e.g. fire damage on hit, frost slow).</summary>
        Attribute = 1,

        /// <summary>Consumables such as healing potions, food, and elixirs.</summary>
        Consumables = 2,

        /// <summary>Permanent or passive physical enhancements (e.g. attack speed, chopping speed).</summary>
        PhysicalUpgrade = 3
    }
}

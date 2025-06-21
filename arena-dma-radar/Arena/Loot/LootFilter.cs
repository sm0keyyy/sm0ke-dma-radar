using arena_dma_radar.UI.Misc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace arena_dma_radar.Arena.Loot
{
    internal static class LootFilter
    {
        private static Config Config => Program.Config;
        public static string SearchString;

        /// <summary>
        /// Creates a loot filter based on current Loot Filter settings.
        /// </summary>
        /// <returns>Loot Filter Predicate.</returns>
        public static Predicate<LootItem> Create()
        {
            var search = SearchString?.Trim();
            var usePrices = string.IsNullOrEmpty(search);
            if (usePrices)
            {
                Predicate<LootItem> p = (x) => // Default Predicate
                {
                    return 
                            (Config.ShowWeapons && x.IsWeapon) ||
                            (Config.ShowThrowables && x.IsThrowableWeapon) ||
                            (Config.ShowMeds && x.IsMeds) ||
                            (Config.ShowBackpacks && x.IsBackpack);
                };
                return (item) =>
                {
                    if (p(item))
                    {
                        if (item is LootContainer container)
                            container.SetFilter(p);

                        return true;
                    }
                    return false;
                };
            }
            else // Loot Search
            {
                var names = search!.Split(',').Select(a => a.Trim()).ToArray();
                Predicate<LootItem> p = (x) => // Search Predicate
                {
                    return names.Any(a => x.Name.Contains(a, StringComparison.OrdinalIgnoreCase));
                };
                return (item) =>
                {
                    if (item.ContainsSearchPredicate(p))
                    {
                        if (item is LootContainer container)
                        {
                            container.SetFilter(p);
                        }
                        return true;
                    }
                    return false;
                };
            }
        }
    }
}

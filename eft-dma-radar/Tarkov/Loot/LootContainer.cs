using eft_dma_shared.Common.Misc.Data;

namespace eft_dma_radar.Tarkov.Loot
{
    public class LootContainer : LootItem
    {
        private static readonly TarkovMarketItem _defaultItem = new();
        private static readonly Predicate<LootItem> _pTrue = (x) => { return true; };
        private Predicate<LootItem> _filter = _pTrue;

        // Performance optimization: Cache important loot LINQ query results
        private List<LootItem> _cachedImportantLoot = null;
        private int _lastLootCount = -1;

        public override string Name
        {
            get
            {
                var items = this.FilteredLoot;
                if (items is not null && items.Count() == 1)
                    return items.First().Name ?? "Loot";
                return "Loot";
            }
        }
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name">Name of container (example: AIRDROP).</param>
        public LootContainer(IReadOnlyList<LootItem> loot) : base(_defaultItem)
        {
            ArgumentNullException.ThrowIfNull(loot, nameof(loot));
            this.Loot = loot;
        }

        /// <summary>
        /// Update the filter for this container.
        /// </summary>
        /// <param name="filter">New filter to be set.</param>
        public void SetFilter(Predicate<LootItem> filter)
        {
            ArgumentNullException.ThrowIfNull(filter, nameof(filter));
            _filter = filter;
        }

        /// <summary>
        /// All items inside this Container (unfiltered/unordered).
        /// </summary>
        public IReadOnlyList<LootItem> Loot { get; }

        /// <summary>
        /// All Items inside this container that pass the current Loot Filter.
        /// Ordered by Important/Price Value.
        /// </summary>
        public IEnumerable<LootItem> FilteredLoot => Loot
            .Where(x => _filter(x))
            .OrderLoot();

        /// <summary>
        /// Performance-optimized method to get important loot items with caching.
        /// Caches the filtered/ordered list and only recomputes when loot count changes.
        /// This eliminates expensive LINQ queries (Where + 4x OrderBy) from running every frame.
        /// </summary>
        public List<LootItem> GetImportantLoot()
        {
            // Check if loot count changed (items picked up/dropped)
            int currentCount = Loot?.Count ?? 0;

            if (_cachedImportantLoot == null || currentCount != _lastLootCount)
            {
                // Recompute important loot only when necessary
                if (this is LootCorpse && Loot != null)
                {
                    _cachedImportantLoot = Loot
                        .Where(item => item.IsImportant ||
                                      item is QuestItem ||
                                      (Program.Config.QuestHelper.Enabled && item.IsQuestCondition) ||
                                      item.IsWishlisted ||
                                      (UI.Pages.LootFilterControl.ShowBackpacks && item.IsBackpack) ||
                                      (UI.Pages.LootFilterControl.ShowMeds && item.IsMeds) ||
                                      (UI.Pages.LootFilterControl.ShowFood && item.IsFood) ||
                                      (UI.Pages.LootFilterControl.ShowWeapons && item.IsWeapon) ||
                                      item.IsValuableLoot ||
                                      (!item.IsGroupedBlacklisted && item.MatchedFilter?.Color != null && !string.IsNullOrEmpty(item.MatchedFilter.Color)))
                        .OrderLoot()
                        .Take(5)
                        .ToList();
                }
                else
                {
                    _cachedImportantLoot = new List<LootItem>();
                }

                _lastLootCount = currentCount;
            }

            return _cachedImportantLoot;
        }
    }
}
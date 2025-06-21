using arena_dma_radar.Arena.Loot;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Unity.Collections;
using System.Collections.Frozen;

namespace arena_dma_radar.Arena.ArenaPlayer.Plugins
{
    public sealed class GearManager
    {
        private static readonly FrozenSet<string> THERMAL_IDS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "5c110624d174af029e69734c", // T7 Thermal Goggles
            "6478641c19d732620e045e17", // ECHO1 1-2x30mm 30Hz thermal reflex
            "609bab8b455afd752b2e6138", // T12W 30Hz thermal reflex sight
            "63fc44e2429a8a166c7f61e6", // Armasight Zeus-Pro 640 2-8x50 30Hz
            "5d1b5e94d7ad1a2b865a96b0", // FLIR RS-32 2.25-9x 35mm 60Hz
            "606f2696f2cb2e02a42aceb1", // MP-155 Ultima thermal camera
            "5a1eaa87fcdbcb001865f75e"  // Trijicon REAP-IR thermal
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> NVG_IDS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "5c066e3a0db834001b7353f0", // N-15 Night Vision
            "5c0696830db834001d23f5da", // PNV-10T Night Vision
            "5c0558060db834001b735271", // GPNVG-18 Night Vision
            "57235b6f24597759bf5a30f1", // AN/PVS-14 Night Vision Monocular
            "5b3b6e495acfc4330140bd88", // Vulcan MG night scope 3.5x
            "5a7c74b3e899ef0014332c29"  // NSPU-M night Scope
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> UBGL_IDS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "62e7e7bbe6da9612f743f1e0", // GP-25 Kostyor 40mm
            "6357c98711fb55120211f7e1", // M203 40mm
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> _skipSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SecuredContainer", "Dogtag", "Compass", "Eyewear", "ArmBand", "Scabbard"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly string BLASTGANG_BOMB = "6669a73bef0f6220df0ed178";

        /// <summary>
        /// List of equipped items in Player Inventory Slots.
        /// </summary>
        public IReadOnlyDictionary<string, string> Equipment { get; private set; } = new Dictionary<string, string>();

        /// <summary>
        /// True if NVG items are equipped.
        /// </summary>
        public bool HasNVG { get; private set; }

        /// <summary>
        /// True if Thermal items are equipped.
        /// </summary>
        public bool HasThermal { get; private set; }

        /// <summary>
        /// True if Underbarrel Grenade Launcher is equipped.
        /// </summary>
        public bool HasUBGL { get; private set; }

        /// <summary>
        /// True if Blastgang Backpack is on player
        /// </summary>
        public bool HasBomb { get; private set; }

        private readonly IReadOnlyDictionary<string, ulong> _slots;

        public GearManager(ArenaObservedPlayer player)
        {
            var inventorycontroller = Memory.ReadPtr(player.InventoryControllerAddr);
            var inventory = Memory.ReadPtr(inventorycontroller + Offsets.InventoryController.Inventory);
            var equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
            var slotsPtr = Memory.ReadPtr(equipment + Offsets.Equipment.Slots);

            _slots = LoadSlots(slotsPtr);
            Refresh();
        }

        private static IReadOnlyDictionary<string, ulong> LoadSlots(ulong slotsPtr)
        {
            using var slotsArray = MemArray<ulong>.Get(slotsPtr);
            var slotDict = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

            foreach (var slotPtr in slotsArray)
            {
                try
                {
                    var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.ID);
                    var name = Memory.ReadUnityString(namePtr);

                    if (!_skipSlots.Contains(name))
                        slotDict.TryAdd(name, slotPtr);
                }
                catch { }
            }

            return slotDict;
        }

        public void Refresh()
        {
            var gearDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var equippedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var slot in _slots)
            {
                try
                {
                    var containedItem = Memory.ReadPtr(slot.Value + Offsets.Slot.ContainedItem);
                    if (containedItem == 0)
                        continue;

                    var inventorytemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                    var idPtr = Memory.ReadValue<Types.MongoID>(inventorytemplate + Offsets.ItemTemplate._id);
                    var id = Memory.ReadUnityString(idPtr.StringID);

                    if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                    {
                        gearDict.TryAdd(slot.Key, entry.Name);
                        equippedIds.Add(id);
                    }
                    else if (LootManager.BLASTGANG_ITEMS.TryGetValue(id, out var manualName))
                    {
                        gearDict.TryAdd(slot.Key, manualName);
                        equippedIds.Add(id);
                    }
                }
                catch { }
            }

            Equipment = gearDict;
            HasBomb = equippedIds.Contains(BLASTGANG_BOMB);
            HasNVG = equippedIds.Any(id => NVG_IDS.Contains(id));
            HasThermal = equippedIds.Any(id => THERMAL_IDS.Contains(id));
            HasUBGL = equippedIds.Any(id => UBGL_IDS.Contains(id));
        }
    }
}
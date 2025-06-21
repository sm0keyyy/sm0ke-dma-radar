using arena_dma_radar.Arena.GameWorld.Interactive;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;

namespace arena_dma_radar.Arena.GameWorld
{
    public sealed class InteractiveManager
    {
        private readonly ulong _localGameWorld;
        private readonly HashSet<ArenaPresetRefillContainer> _refillContainers;
        private bool _isInitializationFailed;

        public IReadOnlyCollection<ArenaPresetRefillContainer> RefillContainers => _refillContainers;

        public InteractiveManager(ulong localGameWorld)
        {
            _localGameWorld = localGameWorld;
            _refillContainers = new HashSet<ArenaPresetRefillContainer>();
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                LoadRefillContainers();
                _isInitializationFailed = false;
            }
            catch
            {
                _isInitializationFailed = true;
            }
        }

        private void LoadRefillContainers()
        {
            var interactableArrayPtr = Memory.ReadPtrChain(_localGameWorld, new uint[] { 0x268, 0x30 }, false);

            using var array = MemArray<ulong>.Get(interactableArrayPtr, false);
            var validItems = array.Where(IsValidItem).ToHashSet();

            foreach (var item in validItems)
            {
                if (IsRefillContainer(item))
                    _refillContainers.Add(new ArenaPresetRefillContainer(item));
            }
        }

        private static bool IsValidItem(ulong item) => item != 0x0;

        private static bool IsRefillContainer(ulong item)
        {
            var itemName = ObjectClass.ReadName(item);
            return itemName == "ArenaPresetRefillContainer";
        }

        public void Refresh()
        {
            if (_isInitializationFailed)
                return;

            if (!_refillContainers.Any())
            {
                Initialize();
                return;
            }
        }
    }
}
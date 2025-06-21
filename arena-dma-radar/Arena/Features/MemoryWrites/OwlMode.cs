using arena_dma_radar.Arena.Features;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class OwlMode : MemWriteFeature<OwlMode>
    {
        private bool _lastEnabledState;
        private ulong _cachedEFTHardSettingsInstance;

        private static readonly Vector2 ORIGINAL_MOUSE_LOOK_HORIZONTAL_LIMIT = new(-40f, 40f);
        private static readonly Vector2 ORIGINAL_MOUSE_LOOK_VERTICAL_LIMIT = new(-50f, 20f);
        private static readonly Vector2 NEW_MOUSE_LOOK_HORIZONTAL_LIMIT = new(-float.MaxValue, float.MaxValue);
        private static readonly Vector2 NEW_MOUSE_LOOK_VERTICAL_LIMIT = new(-float.MaxValue, float.MaxValue);

        public override bool Enabled
        {
            get => MemWrites.Config.OwlMode;
            set => MemWrites.Config.OwlMode = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                var hardSettingsInstance = GetEFTHardSettingsInstance();
                if (!hardSettingsInstance.IsValidVirtualAddress())
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var (horizontalLimit, verticalLimit) = Enabled
                        ? (NEW_MOUSE_LOOK_HORIZONTAL_LIMIT, NEW_MOUSE_LOOK_VERTICAL_LIMIT)
                        : (ORIGINAL_MOUSE_LOOK_HORIZONTAL_LIMIT, ORIGINAL_MOUSE_LOOK_VERTICAL_LIMIT);

                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFTHardSettings.MOUSE_LOOK_HORIZONTAL_LIMIT, horizontalLimit);
                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFTHardSettings.MOUSE_LOOK_VERTICAL_LIMIT, verticalLimit);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[OwlMode] {(Enabled ? "Enabled" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[OwlMode]: {ex}");
                _cachedEFTHardSettingsInstance = default;
            }
        }

        private ulong GetEFTHardSettingsInstance()
        {
            if (_cachedEFTHardSettingsInstance.IsValidVirtualAddress())
                return _cachedEFTHardSettingsInstance;

            try
            {
                var hardSettingsClass = MonoLib.MonoClass.Find("Assembly-CSharp", "EFTHardSettings", out var hardSettingsClassAddress);
                if (!hardSettingsClassAddress.IsValidVirtualAddress())
                    return 0x0;

                var instance = Memory.ReadPtr(hardSettingsClass.GetStaticFieldDataPtr());
                if (!instance.IsValidVirtualAddress())
                    return 0x0;

                _cachedEFTHardSettingsInstance = instance;
                return instance;
            }
            catch
            {
                return 0x0;
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedEFTHardSettingsInstance = default;
        }
    }
}
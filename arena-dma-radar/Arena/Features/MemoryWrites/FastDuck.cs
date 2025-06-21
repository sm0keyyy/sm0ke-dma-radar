using arena_dma_radar.Arena.Features;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;
using arena_dma_radar.Arena.ArenaPlayer;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class FastDuck : MemWriteFeature<FastDuck>
    {
        private bool _lastEnabledState;
        private ulong _cachedEFTHardSettingsInstance;

        private const float ORIGINAL_SPEED = 3f;
        private const float FAST_SPEED = 9999f;

        public override bool Enabled
        {
            get => MemWrites.Config.FastDuck;
            set => MemWrites.Config.FastDuck = value;
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
                    var targetSpeed = Enabled ? FAST_SPEED : ORIGINAL_SPEED;
                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFTHardSettings.POSE_CHANGING_SPEED, targetSpeed);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[FastDuck] {(Enabled ? "Enabled" : "Disabled")} (Speed: {targetSpeed:F0})");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[FastDuck]: {ex}");
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
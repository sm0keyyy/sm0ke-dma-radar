using arena_dma_radar.Arena.Features;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class LongJump : MemWriteFeature<LongJump>
    {
        private bool _lastEnabledState;
        private float _lastMultiplier;
        private ulong _cachedEFTHardSettingsInstance;

        private const float ORIGINAL_AIR_CONTROL_SAME_DIR = 1.2f;
        private const float ORIGINAL_AIR_CONTROL_NONE_OR_ORT_DIR = 0.9f;

        public override bool Enabled
        {
            get => MemWrites.Config.LongJump.Enabled;
            set => MemWrites.Config.LongJump.Enabled = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                var hardSettingsInstance = GetEFTHardSettingsInstance();
                if (!hardSettingsInstance.IsValidVirtualAddress())
                    return;

                var currentMultiplier = MemWrites.Config.LongJump.Multiplier;
                var stateChanged = Enabled != _lastEnabledState;
                var multiplierChanged = Math.Abs(currentMultiplier - _lastMultiplier) > 0.001f;

                if ((Enabled && (stateChanged || multiplierChanged)) || (!Enabled && stateChanged))
                {
                    var (sameDirValue, noneOrOrtDirValue) = Enabled
                        ? (ORIGINAL_AIR_CONTROL_SAME_DIR * currentMultiplier, ORIGINAL_AIR_CONTROL_NONE_OR_ORT_DIR * currentMultiplier)
                        : (ORIGINAL_AIR_CONTROL_SAME_DIR, ORIGINAL_AIR_CONTROL_NONE_OR_ORT_DIR);

                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFTHardSettings.AIR_CONTROL_SAME_DIR, sameDirValue);
                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFTHardSettings.AIR_CONTROL_NONE_OR_ORT_DIR, noneOrOrtDirValue);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        _lastMultiplier = currentMultiplier;

                        if (Enabled)
                            LoneLogging.WriteLine($"[LongJump] Enabled (Multiplier: {currentMultiplier:F2})");
                        else
                            LoneLogging.WriteLine("[LongJump] Disabled");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[LongJump]: {ex}");
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
            _lastMultiplier = default;
            _cachedEFTHardSettingsInstance = default;
        }
    }
}
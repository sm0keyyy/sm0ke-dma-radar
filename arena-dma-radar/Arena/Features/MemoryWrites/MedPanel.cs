using arena_dma_radar.Arena.Features;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;
using arena_dma_radar.Arena.ArenaPlayer;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class MedPanel : MemWriteFeature<MedPanel>
    {
        private bool _lastEnabledState;
        private ulong _cachedEFTHardSettingsInstance;

        public override bool Enabled
        {
            get => MemWrites.Config.MedPanel;
            set => MemWrites.Config.MedPanel = value;
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
                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFTHardSettings.MED_EFFECT_USING_PANEL, Enabled);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[MedPanel] {(Enabled ? "Enabled" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[MedPanel]: {ex}");
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
using arena_dma_radar.Arena.Features;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class DisableWeaponCollision : MemWriteFeature<DisableWeaponCollision>
    {
        private bool _lastEnabledState;
        private ulong _cachedEFTHardSettingsInstance;

        private const uint ORIGINAL_WEAPON_OCCLUSION_LAYERS = 1082136832;
        private const uint DISABLED_WEAPON_OCCLUSION_LAYERS = 0;

        public override bool Enabled
        {
            get => MemWrites.Config.DisableWeaponCollision;
            set => MemWrites.Config.DisableWeaponCollision = value;
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
                    var targetLayers = Enabled ? DISABLED_WEAPON_OCCLUSION_LAYERS : ORIGINAL_WEAPON_OCCLUSION_LAYERS;
                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFTHardSettings.WEAPON_OCCLUSION_LAYERS, targetLayers);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[DisableWeaponCollision] {(Enabled ? "Enabled" : "Disabled")} (Layers: {targetLayers})");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[DisableWeaponCollision]: {ex}");
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
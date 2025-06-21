using eft_dma_shared.Common.Misc;
using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Unity;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class NoVisor : MemWriteFeature<NoVisor>
    {
        private bool _lastEnabledState;
        private ulong _cachedVisorEffect;

        private const float VISOR_DISABLED = 0f;
        private const float VISOR_ENABLED = 1f;

        public override bool Enabled
        {
            get => MemWrites.Config.NoVisor;
            set => MemWrites.Config.NoVisor = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var visorEffect = GetVisorEffect(game);
                    if (!visorEffect.IsValidVirtualAddress())
                        return;

                    var targetIntensity = Enabled ? VISOR_DISABLED : VISOR_ENABLED;
                    writes.AddValueEntry(visorEffect + Offsets.VisorEffect.Intensity, targetIntensity);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[NoVisor] {(Enabled ? "Enabled" : "Disabled")} (Intensity: {targetIntensity})");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[NoVisor]: {ex}");
                _cachedVisorEffect = default;
            }
        }

        private ulong GetVisorEffect(LocalGameWorld game)
        {
            if (_cachedVisorEffect.IsValidVirtualAddress())
                return _cachedVisorEffect;

            var fps = game.CameraManager?.FPSCamera ?? 0x0;
            if (!fps.IsValidVirtualAddress())
                return 0x0;

            var visorEffect = MonoBehaviour.GetComponent(fps, "VisorEffect");
            if (!visorEffect.IsValidVirtualAddress())
                return 0x0;

            _cachedVisorEffect = visorEffect;
            return visorEffect;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedVisorEffect = default;
        }
    }
}
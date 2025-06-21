using eft_dma_shared.Common.Misc;
using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Unity;
using arena_dma_radar.Arena.ArenaPlayer;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class ThermalVision : MemWriteFeature<ThermalVision>
    {
        private bool _currentState;
        private ulong _cachedThermalVisionComponent;

        public override bool Enabled
        {
            get => MemWrites.Config.ThermalVision;
            set => MemWrites.Config.ThermalVision = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game || Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                var targetState = Enabled && !localPlayer.CheckIfADS();
                if (targetState == _currentState)
                    return;

                var thermalComponent = GetThermalVisionComponent(game);
                if (!thermalComponent.IsValidVirtualAddress())
                    return;

                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.On, targetState);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.IsNoisy, !targetState);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.IsFpsStuck, !targetState);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.IsMotionBlurred, !targetState);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.IsGlitch, !targetState);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.IsPixelated, !targetState);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.ChromaticAberrationThermalShift, targetState ? 0f : 0.013f);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.UnsharpRadiusBlur, targetState ? 0.0001f : 5f);

                writes.Callbacks += () =>
                {
                    _currentState = targetState;
                    LoneLogging.WriteLine($"[ThermalVision] {(targetState ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[ThermalVision]: {ex}");
                _cachedThermalVisionComponent = default;
            }
        }

        private ulong GetThermalVisionComponent(LocalGameWorld game)
        {
            if (_cachedThermalVisionComponent.IsValidVirtualAddress())
                return _cachedThermalVisionComponent;

            var fps = game.CameraManager?.FPSCamera ?? 0x0;
            if (!fps.IsValidVirtualAddress())
                return 0x0;

            var thermalComponent = MonoBehaviour.GetComponent(fps, "ThermalVision");
            if (!thermalComponent.IsValidVirtualAddress())
                return 0x0;

            _cachedThermalVisionComponent = thermalComponent;
            return thermalComponent;
        }

        public override void OnRaidStart()
        {
            _currentState = default;
            _cachedThermalVisionComponent = default;
        }
    }
}
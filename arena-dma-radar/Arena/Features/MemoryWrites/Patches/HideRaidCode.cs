using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Unity;
using arena_dma_radar;
using arena_dma_radar.Arena.Features;
using eft_dma_shared.Common.Misc;

namespace arena_dma_radar.Arena.Features.MemoryWrites.Patches
{
    public sealed class HideRaidCode : MemPatchFeature<HideRaidCode>
    {
        private bool _set;

        public override bool Enabled
        {
            get => MemWrites.Config.HideRaidCode;
            set => MemWrites.Config.HideRaidCode = value;
        }
        public override bool CanRun => Enabled && NativeHook.Initialized;
        public override bool TryApply()
        {
            try
            {
                ulong alphaLabel = NativeMethods.FindGameObjectS("Preloader UI/Preloader UI/BottomPanel/Content/UpperPart/AlphaLabel");
                if (alphaLabel == 0x0)
                {
                    LoneLogging.WriteLine("HideRaidCode: Could not find AlphaLabel GameObject");
                    return false;
                }

                if (Enabled)
                {
                    if (!_set)
                    {
                        NativeMethods.GameObjectSetActive(alphaLabel, false);
                        LoneLogging.WriteLine("HideRaidCode [ON]");
                        _set = true;
                    }
                }
                else if (_set)
                {
                    NativeMethods.GameObjectSetActive(alphaLabel, true);
                    LoneLogging.WriteLine("HideRaidCode [OFF]");
                    _set = false;
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR configuring HideRaidCode: {ex}");
                return false;
            }
            return true;
        }

        public override void OnRaidStart()
        {
            _set = default;
        }

        public override void OnGameStop()
        {
            _set = false;
        }
    }
}

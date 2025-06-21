using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using arena_dma_radar.Arena.GameWorld;
using arena_dma_radar.Arena.Features;
using System;
using static eft_dma_shared.Common.Unity.MonoLib;
using arena_dma_radar.Arena.ArenaPlayer;

namespace arena_dma_radar.Arena.Features.MemoryWrites.Patches
{
    public sealed class DisableScreenEffects : MemPatchFeature<DisableScreenEffects>
    {
        private bool _patched;

        public override bool Enabled
        {
            get => MemWrites.Config.DisableScreenEffects;
            set => MemWrites.Config.DisableScreenEffects = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override bool TryApply()
        {
            if (Memory.Game is not LocalGameWorld game || Memory.LocalPlayer is not LocalPlayer localPlayer)
                return false;

            if (Enabled)
            {
                if (!_patched)
                {
                    var sigInfo = new SignatureInfo(null, ShellKeeper.PatchReturn);
                    PatchMethodE(ClassNames.GrenadeFlashScreenEffect.ClassName, ClassNames.GrenadeFlashScreenEffect.MethodName, sigInfo, compileClass: true);
                    _patched = true;
                    LoneLogging.WriteLine("DisableScreenEffects: Patched GrenadeFlashScreenEffect.");
                }

                TrySetState("EyeBurn", false);
                TrySetState("CC_Wiggle", false);
                TrySetState("CC_DoubleVision", false);
                TrySetState("BloodOnScreen", false);
                TrySetState("CC_FastVignette", false);
                TrySetState("CC_RadialBlur", false);
                TrySetState("DistortCameraFX", false);
                return true;
            }
            return true;
        }

        private void TrySetState(string componentName, bool state)
        {
            try
            {
                if (Memory.Game is LocalGameWorld game)
                {
                    var cam = game.CameraManager?.FPSCamera ?? 0x0;
                    var component = MonoBehaviour.GetComponent(cam, componentName);
                    var behaviourPtr = Memory.ReadPtr(component + 0x10);
                    var behaviour = new Behaviour(behaviourPtr);

                    if (behaviour.GetState() != state)
                    {
                        if (!behaviour.SetState(state))
                            LoneLogging.WriteLine($"[DisableScreenEffects] Failed to set {componentName} to {state}.");
                        else
                            LoneLogging.WriteLine($"[DisableScreenEffects] {componentName} set to {state}.");
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[DisableScreenEffects] Error with {componentName}: {ex.Message}");
            }
        }
    }
}
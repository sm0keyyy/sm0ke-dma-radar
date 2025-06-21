using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using static arena_dma_radar.Arena.API.EFTProfileService;
using static eft_dma_shared.Common.Unity.MonoLib;

namespace arena_dma_radar.Arena.Features.MemoryWrites.Patches
{
    public sealed class RemoveableAttachments : MemPatchFeature<RemoveableAttachments>
    {
        private bool _lastEnabledState;

        public override bool Enabled
        {
            get => MemWrites.Config.RemoveableAttachments;
            set => MemWrites.Config.RemoveableAttachments = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override bool CanRun
        {
            get
            {
                if (!Memory.Ready || !DelayElapsed)
                    return false;
                return Memory.Game is LocalGameWorld game && game.InRaid && NativeHook.Initialized;
            }
        }

        public override bool TryApply()
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game || Memory.LocalPlayer is not { } localPlayer)
                    return false;

                var stateChanged = Enabled != _lastEnabledState;

                if (!Enabled)
                {
                    if (stateChanged)
                    {
                        _lastEnabledState = false;
                        LoneLogging.WriteLine("[RemoveableAttachments] Disabled");
                    }
                    return true;
                }

                if (!IsApplied && NativeHook.Initialized)
                {
                    if (!ApplyAttachmentPatches())
                        return false;

                    IsApplied = true;
                }

                if (stateChanged)
                {
                    _lastEnabledState = true;
                    LoneLogging.WriteLine("[RemoveableAttachments] Enabled - All attachments can now be removed");
                }

                return true;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[RemoveableAttachments]: {ex}");
                return false;
            }
        }

        private bool ApplyAttachmentPatches()
        {
            try
            {
                var sigInfoFalse = new SignatureInfo(null, ShellKeeper.PatchFalse);
                PatchMethodE(ClassNames.VitalParts.ClassName, ClassNames.VitalParts.MethodName, sigInfoFalse, 0, compileClass: true);

                var sigInfoTrue = new SignatureInfo(null, ShellKeeper.PatchTrue);
                PatchMethodE(ClassNames.InventoryLogic_Mod.ClassName, ClassNames.InventoryLogic_Mod.MethodName, sigInfoTrue, compileClass: true);

                LoneLogging.WriteLine("[RemoveableAttachments] Method patches applied successfully");
                return true;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[RemoveableAttachments] Failed to apply method patches: {ex}");
                return false;
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
        }

        public override void OnGameStop()
        {
            IsApplied = false;
            _lastEnabledState = default;
        }
    }
}
using eft_dma_shared.Common.Misc;
using arena_dma_radar.Arena.Features;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using arena_dma_radar.Arena.GameWorld;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using arena_dma_radar.Arena.ArenaPlayer;

namespace arena_dma_radar.Arena.Features.MemoryWrites.Patches
{
    public sealed class FOVChanger : MemPatchFeature<FOVChanger>
    {
        private bool _lastEnabledState;
        private float _lastFOV;
        private ulong _cachedFPSCamera;

        public override bool Enabled
        {
            get => MemWrites.Config.FOV.Enabled;
            set => MemWrites.Config.FOV.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(50);

        protected override Func<ulong> GetPFunc => Lookup_SetFov;

        public override bool TryApply()
        {
            try
            {
                if (!Enabled)
                {
                    if (_lastEnabledState)
                    {
                        _lastEnabledState = false;
                        LoneLogging.WriteLine("[FOVChanger] Disabled");
                    }
                    return true;
                }

                if (Memory.Game is not LocalGameWorld game)
                    return false;

                if (!IsApplied && NativeHook.Initialized)
                {
                    if (!ApplyFOVPatch())
                        return false;

                    IsApplied = true;
                }

                var currentFOV = CalculateCurrentFOV(game);
                var stateChanged = Enabled != _lastEnabledState;
                var fovChanged = Math.Abs(currentFOV - _lastFOV) > 0.1f;

                if (stateChanged || fovChanged)
                {
                    WriteFOV(currentFOV, game);
                    _lastEnabledState = Enabled;
                    _lastFOV = currentFOV;

                    if (stateChanged)
                        LoneLogging.WriteLine($"[FOVChanger] Enabled (FOV: {currentFOV:F0})");
                }

                return true;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[FOVChanger]: {ex}");
                _cachedFPSCamera = default;
                return false;
            }
        }

        private bool ApplyFOVPatch()
        {
            try
            {
                var sigInfo = new SignatureInfo(null, ShellKeeper.PatchReturn);
                PatchMethodE(ClassNames.FovChanger.ClassName, ClassNames.FovChanger.MethodName, sigInfo, compileClass: true);

                LoneLogging.WriteLine("[FOVChanger] Method patch applied successfully");
                return true;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[FOVChanger] Failed to apply method patch: {ex}");
                return false;
            }
        }

        private float CalculateCurrentFOV(LocalGameWorld game)
        {
            var config = MemWrites.Config.FOV;
            if (config.InstantZoomActive)
                return config.InstantZoom;

            var isAiming = Memory.LocalPlayer?.CheckIfADS() ?? false;
            if (isAiming)
                return config.ADS;

            return MemWrites.Config.ThirdPerson ? config.ThirdPerson : config.Base;
        }

        private void WriteFOV(float fov, LocalGameWorld game)
        {
            var fpsCamera = GetFPSCamera(game);
            if (!fpsCamera.IsValidVirtualAddress())
                return;

            Memory.WriteValue(fpsCamera + UnityOffsets.Camera.FOV, fov);
        }

        private ulong GetFPSCamera(LocalGameWorld game)
        {
            if (_cachedFPSCamera.IsValidVirtualAddress())
                return _cachedFPSCamera;

            var fpsCamera = game.CameraManager?.FPSCamera ?? 0x0;
            if (fpsCamera.IsValidVirtualAddress())
                _cachedFPSCamera = fpsCamera;

            return fpsCamera;
        }

        private static ulong Lookup_SetFov()
        {
            if (!NativeHook.Initialized)
                throw new Exception("NativeHook not initialized!");

            var @class = MonoLib.MonoClass.Find("Assembly-CSharp", ClassNames.FovChanger.ClassName, out ulong classAddr);
            classAddr.ThrowIfInvalidVirtualAddress();

            if (NativeMethods.CompileClass(classAddr) == 0)
                throw new Exception("Failed to compile class");

            if (@class.TryFindJittedMethod(ClassNames.FovChanger.MethodName, out ulong method))
            {
                return method;
            }
            else
            {
                var jittedMethod = NativeMethods.CompileMethod(method);
                if (jittedMethod == 0)
                    throw new Exception("Failed to compile method");
                return jittedMethod;
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _lastFOV = default;
            _cachedFPSCamera = default;
        }

        public override void OnGameStop()
        {
            IsApplied = false;
            _lastEnabledState = default;
            _lastFOV = default;
            _cachedFPSCamera = default;
        }
    }
}
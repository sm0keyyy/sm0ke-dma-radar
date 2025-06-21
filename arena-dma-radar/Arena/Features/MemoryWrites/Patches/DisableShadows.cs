using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel;
using arena_dma_radar;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using static eft_dma_shared.Common.Unity.MonoLib;
using System;

namespace arena_dma_radar.Arena.Features.MemoryWrites.Patches
{
    public sealed class DisableShadows : MemPatchFeature<DisableShadows>
    {
        private bool _set;
        private ulong _compiledMethod;

        public override bool Enabled
        {
            get => MemWrites.Config.DisableShadows;
            set => MemWrites.Config.DisableShadows = value;
        }

        private enum ShadowQuality
        {
            Disable,
            HardOnly,
            All
        }

        public override bool TryApply()
        {
            try
            {
                if (_compiledMethod == 0)
                {
                    var mClass = MonoClass.Find("UnityEngine.CoreModule", "UnityEngine.QualitySettings", out ulong classAddress);
                    if (classAddress == 0x0)
                    {
                        LoneLogging.WriteLine("DisableShadows: QualitySettings class not found.");
                        return false;
                    }

                    ulong compiled = NativeMethods.CompileClass(classAddress);
                    if (compiled == 0x0)
                    {
                        LoneLogging.WriteLine("DisableShadows: Failed to compile QualitySettings class.");
                        return false;
                    }

                    var methodPtr = mClass.FindMethod("set_shadows");
                    if (methodPtr == 0x0)
                    {
                        LoneLogging.WriteLine("DisableShadows: set_shadows method not found.");
                        return false;
                    }

                    _compiledMethod = NativeMethods.CompileMethod(methodPtr);
                    if (_compiledMethod == 0x0)
                    {
                        LoneLogging.WriteLine("DisableShadows: Failed to compile set_shadows method.");
                        return false;
                    }

                    LoneLogging.WriteLine($"DisableShadows: Successfully cached compiled method: 0x{_compiledMethod:X}");
                }

                if (Enabled && !_set)
                {
                    NativeHook.Call(_compiledMethod, (ulong)ShadowQuality.Disable);
                    _set = true;
                    LoneLogging.WriteLine("DisableShadows [ON]");
                    return true;
                }
                else if (!Enabled && _set)
                {
                    NativeHook.Call(_compiledMethod, (ulong)ShadowQuality.All);
                    _set = false;
                    LoneLogging.WriteLine("DisableShadows [OFF]");
                    return true;
                }

                return true;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR configuring DisableShadows: {ex}");
                return false;
            }
        }

        public override void OnRaidStart()
        {
            base.OnRaidStart();

            if (_compiledMethod == 0)
            {
                try
                {
                    var mClass = MonoClass.Find("UnityEngine.CoreModule", "UnityEngine.QualitySettings", out ulong classAddress);
                    if (classAddress != 0x0)
                    {
                        ulong compiled = NativeMethods.CompileClass(classAddress);
                        if (compiled != 0x0)
                        {
                            var methodPtr = mClass.FindMethod("set_shadows");
                            if (methodPtr != 0x0)
                            {
                                _compiledMethod = NativeMethods.CompileMethod(methodPtr);

                                if (_compiledMethod != 0x0)
                                    LoneLogging.WriteLine($"DisableShadows: Cached compiled method: 0x{_compiledMethod:X}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"ERROR initializing DisableShadows: {ex}");
                }
            }

            _set = default;
        }

        public override void OnGameStop()
        {
            base.OnGameStop();
            _compiledMethod = 0;
            _set = false;
        }
    }
}
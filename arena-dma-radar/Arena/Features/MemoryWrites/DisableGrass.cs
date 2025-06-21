using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.ArenaPlayer;
using arena_dma_radar.Arena.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class DisableGrass : MemWriteFeature<DisableGrass>
    {
        private readonly struct Bounds(Vector3 p, Vector3 e)
        {
            public readonly Vector3 P = p;
            public readonly Vector3 E = e;
        }

        private static readonly Bounds HIDDEN_BOUNDS = new(new(0f, 0f, 0f), new(0f, 0f, 0f));
        private static readonly Bounds SHOWN_BOUNDS = new(new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f));

        private static readonly HashSet<string> ExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "factory4_day",
            "factory4_night",
            "laboratory"
        };

        private bool _lastEnabledState;
        private ulong _cachedGPUManagerListPtr;

        public override bool Enabled
        {
            get => MemWrites.Config.DisableGrass;
            set => MemWrites.Config.DisableGrass = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;

                if (ExcludedMaps.Contains(game.MapID))
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var gpuManagerListPtr = GetGPUManagerListPtr();
                    if (!gpuManagerListPtr.IsValidVirtualAddress())
                        return;

                    ApplyGrassState(writes, gpuManagerListPtr, Enabled);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[DisableGrass] {(Enabled ? "Enabled" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[DisableGrass]: {ex}");
                _cachedGPUManagerListPtr = default;
            }
        }

        private ulong GetGPUManagerListPtr()
        {
            if (_cachedGPUManagerListPtr.IsValidVirtualAddress())
                return _cachedGPUManagerListPtr;

            try
            {
                var gpuInstancerManager = MonoLib.MonoClass.Find("Assembly-CSharp", "GPUInstancer.GPUInstancerManager", out var gpuInstancerManagerClass);
                if (!gpuInstancerManagerClass.IsValidVirtualAddress())
                    return 0x0;

                var gpuManagerListPtr = Memory.ReadPtr(gpuInstancerManager.GetStaticFieldDataPtr());
                if (!gpuManagerListPtr.IsValidVirtualAddress())
                    return 0x0;

                _cachedGPUManagerListPtr = gpuManagerListPtr;
                return gpuManagerListPtr;
            }
            catch
            {
                return 0x0;
            }
        }

        private static void ApplyGrassState(ScatterWriteHandle writes, ulong gpuManagerListPtr, bool hideGrass)
        {
            try
            {
                using var managers = MemList<ulong>.Get(gpuManagerListPtr, false);
                foreach (var manager in managers)
                {
                    if (!manager.IsValidVirtualAddress())
                        continue;

                    var runtimeDataPtr = Memory.ReadPtr(manager + Offsets.GPUInstancerManager.runtimeDataList);
                    if (!runtimeDataPtr.IsValidVirtualAddress())
                        continue;

                    using var runtimeList = MemList<ulong>.Get(runtimeDataPtr, false);
                    foreach (var runtime in runtimeList)
                    {
                        if (!runtime.IsValidVirtualAddress())
                            continue;

                        var bounds = hideGrass ? HIDDEN_BOUNDS : SHOWN_BOUNDS;
                        writes.AddValueEntry(runtime + Offsets.RuntimeDataList.instanceBounds, bounds);
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[DisableGrass] ApplyGrassState error: {ex}");
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedGPUManagerListPtr = default;
        }
    }
}
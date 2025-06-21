using eft_dma_shared.Common.Misc;
using arena_dma_radar.Arena.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Unity;
using arena_dma_radar.Arena.Features;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class TimeOfDay : MemWriteFeature<TimeOfDay>
    {
        private bool _set;
        private int _lastHour;

        private ulong _cachedTodTime;
        private ulong _cachedTodCycle;

        public override bool Enabled
        {
            get => MemWrites.Config.TimeOfDay.Enabled;
            set => MemWrites.Config.TimeOfDay.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

        private static readonly HashSet<string> ExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "factory4_day",
            "factory4_night",
            "laboratory"
        };

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;

                if (ExcludedMaps.Contains(game.MapID))
                    return;

                var currentHour = MemWrites.Config.TimeOfDay.Hour;

                if (Enabled && (!_set || _lastHour != currentHour))
                {
                    var (todTime, todCycle) = GetPointers(game);

                    writes.AddValueEntry(todTime + Offsets.TOD_Time.LockCurrentTime, true);
                    writes.AddValueEntry(todCycle + Offsets.TOD_CycleParameters.Hour, (float)currentHour);

                    writes.Callbacks += () =>
                    {
                        if (!_set)
                        {
                            _set = true;
                            LoneLogging.WriteLine("Time of Day enabled!");
                        }
                        _lastHour = currentHour;
                    };
                }
                else if (!Enabled && _set)
                {
                    var (todTime, _) = GetPointers(game);

                    writes.AddValueEntry(todTime + Offsets.TOD_Time.LockCurrentTime, false);
                    writes.Callbacks += () =>
                    {
                        _set = false;
                        LoneLogging.WriteLine("Time of Day disabled!");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[TimeOfDay]: {ex}");
            }
        }

        private (ulong todTime, ulong todCycle) GetPointers(LocalGameWorld game)
        {
            if (_cachedTodTime.IsValidVirtualAddress() && _cachedTodCycle.IsValidVirtualAddress())
                return (_cachedTodTime, _cachedTodCycle);

            var fps = game.CameraManager?.FPSCamera ?? 0x0;
            fps.ThrowIfInvalidVirtualAddress();

            var todScatt = MonoBehaviour.GetComponent(fps, "TOD_Scattering");
            var todSky = Memory.ReadPtr(todScatt + Offsets.TOD_Scattering.sky);
            var todCycle = Memory.ReadPtr(todSky + Offsets.TOD_Sky.Cycle);

            if (ObjectClass.ReadName(todCycle) != "TOD_CycleParameters")
                throw new ArgumentOutOfRangeException(nameof(todCycle), "Invalid TOD_CycleParameters object");

            var todComp = Memory.ReadPtr(todSky + Offsets.TOD_Sky.TOD_Components);
            var todTime = Memory.ReadPtr(todComp + Offsets.TOD_Components.TOD_Time);

            if (ObjectClass.ReadName(todTime) != "TOD_Time")
                throw new ArgumentOutOfRangeException(nameof(todTime), "Invalid TOD_Time object");

            _cachedTodTime = todTime;
            _cachedTodCycle = todCycle;

            return (todTime, todCycle);
        }

        public override void OnRaidStart()
        {
            _set = default;
            _lastHour = default;

            _cachedTodTime = default;
            _cachedTodCycle = default;
        }
    }
}
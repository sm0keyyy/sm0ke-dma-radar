using eft_dma_shared.Common.Misc;
using arena_dma_radar.Arena.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Unity;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class ClearWeather : MemWriteFeature<ClearWeather>
    {
        private bool _lastEnabledState;
        private ulong _cachedWeatherDebug;

        private static readonly HashSet<string> ExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "factory4_day",
            "factory4_night",
            "laboratory"
        };

        public override bool Enabled
        {
            get => MemWrites.Config.ClearWeather;
            set => MemWrites.Config.ClearWeather = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

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
                    var weatherDebug = GetWeatherDebug();
                    if (!weatherDebug.IsValidVirtualAddress())
                        return;

                    if (Enabled)
                        ApplyClearWeatherSettings(writes, weatherDebug);
                    else
                        writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.isEnabled, false);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[ClearWeather] {(Enabled ? "Enabled" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[ClearWeather]: {ex}");
                _cachedWeatherDebug = default;
            }
        }

        private ulong GetWeatherDebug()
        {
            if (_cachedWeatherDebug.IsValidVirtualAddress())
                return _cachedWeatherDebug;

            try
            {
                var weatherControllerStatic = MonoLib.MonoClass.Find("Assembly-CSharp", "EFT.Weather.WeatherController", out _).GetStaticFieldData();
                if (!weatherControllerStatic.IsValidVirtualAddress())
                    return 0x0;

                var weatherController = Memory.ReadPtr(weatherControllerStatic + 0x0);
                if (!weatherController.IsValidVirtualAddress())
                    return 0x0;

                var weatherDebug = Memory.ReadPtr(weatherController + Offsets.WeatherController.WeatherDebug);
                if (!weatherDebug.IsValidVirtualAddress())
                    return 0x0;

                if (ObjectClass.ReadName(weatherDebug) != "WeatherDebug")
                    throw new InvalidOperationException($"Invalid WeatherDebug object detected");

                _cachedWeatherDebug = weatherDebug;
                return weatherDebug;
            }
            catch
            {
                return 0x0;
            }
        }

        private static void ApplyClearWeatherSettings(ScatterWriteHandle writes, ulong weatherDebug)
        {
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.isEnabled, true);
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.WindMagnitude, 0f);
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.CloudDensity, 0f);
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.Fog, 0.001f);
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.Rain, 0f);
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.LightningThunderProbability, 0f);
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedWeatherDebug = default;
        }
    }
}
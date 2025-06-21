using eft_dma_shared.Common.Misc;
using arena_dma_radar.Arena.Features;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Unity;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class FullBright : MemWriteFeature<FullBright>
    {
        private bool _lastEnabledState;
        private float _lastBrightness;
        private ulong _cachedLevelSettings;

        public override bool Enabled
        {
            get => MemWrites.Config.FullBright.Enabled;
            set => MemWrites.Config.FullBright.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(200);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                var configBrightness = MemWrites.Config.FullBright.Intensity;
                var stateChanged = Enabled != _lastEnabledState;
                var brightnessChanged = Math.Abs(configBrightness - _lastBrightness) > 0.001f;

                if ((Enabled && (stateChanged || brightnessChanged)) || (!Enabled && stateChanged))
                {
                    var levelSettings = GetLevelSettings();
                    if (!levelSettings.IsValidVirtualAddress())
                        return;

                    ApplyFullBrightSettings(writes, levelSettings, Enabled, configBrightness);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        _lastBrightness = configBrightness;

                        if (Enabled)
                            LoneLogging.WriteLine($"[FullBright] Enabled (Intensity: {configBrightness:F2})");
                        else
                            LoneLogging.WriteLine("[FullBright] Disabled");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[FullBright]: {ex}");
                _cachedLevelSettings = default;
            }
        }

        private ulong GetLevelSettings()
        {
            if (_cachedLevelSettings.IsValidVirtualAddress())
                return _cachedLevelSettings;

            var levelSettings = Memory.ReadPtr(MonoLib.LevelSettingsField);
            if (!levelSettings.IsValidVirtualAddress())
                return 0x0;

            _cachedLevelSettings = levelSettings;
            return levelSettings;
        }

        private static void ApplyFullBrightSettings(ScatterWriteHandle writes, ulong levelSettings, bool enabled, float brightness)
        {
            if (enabled)
            {
                writes.AddValueEntry(levelSettings + Offsets.LevelSettings.AmbientMode, (int)AmbientMode.Trilight);

                var equatorColor = new UnityColor(brightness, brightness, brightness);
                var groundColor = new UnityColor(0f, 0f, 0f);

                writes.AddValueEntry(levelSettings + Offsets.LevelSettings.EquatorColor, ref equatorColor);
                writes.AddValueEntry(levelSettings + Offsets.LevelSettings.GroundColor, ref groundColor);
            }
            else
            {
                writes.AddValueEntry(levelSettings + Offsets.LevelSettings.AmbientMode, (int)AmbientMode.Flat);
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _lastBrightness = default;
            _cachedLevelSettings = default;
        }

        private enum AmbientMode : int
        {
            Skybox,
            Trilight,
            Flat = 3,
            Custom
        }
    }
}
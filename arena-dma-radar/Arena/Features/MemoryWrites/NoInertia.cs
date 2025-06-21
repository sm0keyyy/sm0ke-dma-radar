using arena_dma_radar.Arena.ArenaPlayer;
using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;
using static SDK.Offsets;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class NoInertia : MemWriteFeature<NoInertia>
    {
        private bool _lastEnabledState;
        private ulong _cachedHardSettings;
        private ulong _cachedInertiaSettings;

        public override bool Enabled
        {
            get => MemWrites.Config.NoInertia;
            set => MemWrites.Config.NoInertia = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game || Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var (hardSettings, inertiaSettings) = GetSettingsPointers();
                    if (!ValidatePointers(hardSettings, inertiaSettings))
                        return;

                    var movementContext = localPlayer.MovementContext;
                    if (!movementContext.IsValidVirtualAddress())
                        return;

                    ApplyInertiaSettings(writes, movementContext, hardSettings, inertiaSettings, Enabled);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[NoInertia] {(Enabled ? "Enabled" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[NoInertia]: {ex}");
                ClearCache();
            }
        }

        private (ulong hardSettings, ulong inertiaSettings) GetSettingsPointers()
        {
            if (_cachedHardSettings.IsValidVirtualAddress() && _cachedInertiaSettings.IsValidVirtualAddress())
                return (_cachedHardSettings, _cachedInertiaSettings);

            var hardSettingsClass = MonoLib.MonoClass.Find("Assembly-CSharp", "EFTHardSettings", out var hardSettingsClassAddress);
            if (!hardSettingsClassAddress.IsValidVirtualAddress())
                return (0x0, 0x0);

            var hardSettings = Memory.ReadPtr(hardSettingsClass.GetStaticFieldDataPtr());
            if (!hardSettings.IsValidVirtualAddress())
                return (0x0, 0x0);

            var inertiaSettingsSingleton = Memory.ReadPtr(MonoLib.Singleton.FindOne(ClassNames.InertiaSettings.ClassName));
            if (!inertiaSettingsSingleton.IsValidVirtualAddress())
                return (0x0, 0x0);

            var inertiaSettings = Memory.ReadPtr(inertiaSettingsSingleton + Offsets.GlobalConfigs.Inertia);
            if (!inertiaSettings.IsValidVirtualAddress())
                return (0x0, 0x0);

            _cachedHardSettings = hardSettings;
            _cachedInertiaSettings = inertiaSettings;

            return (hardSettings, inertiaSettings);
        }

        private static bool ValidatePointers(ulong hardSettings, ulong inertiaSettings)
        {
            return hardSettings.IsValidVirtualAddress() && inertiaSettings.IsValidVirtualAddress();
        }

        private static void ApplyInertiaSettings(ScatterWriteHandle writes, ulong movementContext, ulong hardSettings, ulong inertiaSettings, bool enabled)
        {
            writes.AddValueEntry(movementContext + Offsets.MovementContext.WalkInertia, enabled ? 0 : 1);
            writes.AddValueEntry(movementContext + Offsets.MovementContext.SprintBrakeInertia, enabled ? 0f : 1f);
            writes.AddValueEntry(inertiaSettings + Offsets.InertiaSettings.FallThreshold, enabled ? 99999f : 1.5f);
            writes.AddValueEntry(inertiaSettings + Offsets.InertiaSettings.BaseJumpPenalty, enabled ? 0f : 0.25f);
            writes.AddValueEntry(inertiaSettings + Offsets.InertiaSettings.BaseJumpPenaltyDuration, enabled ? 0f : 0.3f);
            writes.AddValueEntry(inertiaSettings + Offsets.InertiaSettings.MoveTimeRange, enabled ? new Vector2(0f, 0f) : new Vector2(0.6f, 0.85f));
            writes.AddValueEntry(hardSettings + Offsets.EFTHardSettings.DecelerationSpeed, enabled ? 100f : 1f);
        }

        private void ClearCache()
        {
            _cachedHardSettings = default;
            _cachedInertiaSettings = default;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            ClearCache();
        }
    }
}
using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Config;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel;
using eft_dma_shared.Common.Unity.LowLevel.Chams;
using static eft_dma_shared.Common.Misc.Config.ChamsConfig;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class Chams : MemWriteFeature<Chams>
    {
        private bool _lastEnabledState;
        private readonly Dictionary<ChamsEntityType, EntityChamsSettings> _lastEntityStates = new();

        public static ChamsConfig Config => MemWrites.Config.Chams;

        public override bool Enabled
        {
            get => Config.Enabled;
            set => Config.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (!Memory.InRaid || Memory.Game is not LocalGameWorld game)
                    return;

                var stateChanged = HasStateChanged();

                if (!Enabled && _lastEnabledState)
                {
                    DisableAllChams(writes, game);
                    return;
                }

                if (!Enabled)
                    return;

                PlayerChamsManager.ProcessPlayerChams(writes, game);
                GrenadeChamsManager.ProcessGrenadeChams(writes, game);

                UpdateStateTracking();
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Chams]: {ex}");
            }
        }

        private bool HasStateChanged()
        {
            if (Enabled != _lastEnabledState)
                return true;

            return CheckEntityStatesChanged();
        }

        private bool CheckEntityStatesChanged()
        {
            foreach (var kvp in Config.EntityChams)
            {
                var entityType = kvp.Key;
                var currentSettings = kvp.Value;

                if (!_lastEntityStates.TryGetValue(entityType, out var lastSettings))
                    return true;

                if (HasEntitySettingsChanged(currentSettings, lastSettings))
                    return true;
            }

            return false;
        }

        private bool HasEntitySettingsChanged(EntityChamsSettings current, EntityChamsSettings last)
        {
            return current.Enabled != last.Enabled ||
                   current.Mode != last.Mode ||
                   current.ClothingChamsMode != last.ClothingChamsMode ||
                   current.GearChamsMode != last.GearChamsMode ||
                   current.VisibleColor != last.VisibleColor ||
                   current.InvisibleColor != last.InvisibleColor ||
                   current.RevertOnDeath != last.RevertOnDeath;
        }

        private void UpdateStateTracking()
        {
            _lastEnabledState = Enabled;
            _lastEntityStates.Clear();

            foreach (var kvp in Config.EntityChams)
            {
                _lastEntityStates[kvp.Key] = new EntityChamsSettings
                {
                    Enabled = kvp.Value.Enabled,
                    Mode = kvp.Value.Mode,
                    ClothingChamsMode = kvp.Value.ClothingChamsMode,
                    GearChamsMode = kvp.Value.GearChamsMode,
                    VisibleColor = kvp.Value.VisibleColor,
                    InvisibleColor = kvp.Value.InvisibleColor,
                    RevertOnDeath = kvp.Value.RevertOnDeath
                };
            }
        }

        private void DisableAllChams(ScatterWriteHandle writes, LocalGameWorld game)
        {
            PlayerChamsManager.RevertAllPlayerChams(writes, game);

            writes.Callbacks += () => _lastEnabledState = false;
        }

        public override void OnRaidStart()
        {
            ResetState();
            InitializeManagers();
        }

        public override void OnGameStop()
        {
            ResetState();
            PlayerChamsManager.Reset();
            GrenadeChamsManager.Reset();
        }

        private void ResetState()
        {
            _lastEnabledState = default;
            _lastEntityStates.Clear();
        }

        private void InitializeManagers()
        {
            Config.InitializeDefaults();

            PlayerChamsManager.Initialize();
            GrenadeChamsManager.Initialize();
        }
    }
}
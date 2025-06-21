using arena_dma_radar.Arena.GameWorld;
using arena_dma_radar.UI.Misc;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Config;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;
using eft_dma_shared.Common.Unity.LowLevel;
using eft_dma_shared.Common.Unity.LowLevel.Chams;
using eft_dma_shared.Common.Unity.LowLevel.Chams.Arena;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using eft_dma_shared.Common.Unity.LowLevel.Types;
using System.Collections.Concurrent;

namespace arena_dma_radar.Arena.Features
{
    /// <summary>
    /// Manages grenade chams application
    /// </summary>
    public static class GrenadeChamsManager
    {
        private static readonly ConcurrentDictionary<ulong, GrenadeChamsState> _grenadeStates = new();

        private static Config Config => Program.Config;
        private static ChamsConfig ChamsConfig => Config.ChamsConfig;

        #region Public API

        public static void ProcessGrenadeChams(ScatterWriteHandle writes, LocalGameWorld game)
        {
            try
            {
                if (!ChamsConfig.Enabled || game.Grenades == null)
                    return;

                var activeGrenades = game.Grenades.Where(g => g.IsActive).ToList();
                if (!activeGrenades.Any())
                    return;

                var entityType = ChamsEntityType.Grenade;
                var entitySettings = ChamsConfig.GetEntitySettings(entityType);

                if (!entitySettings.Enabled || !AreMaterialsReady(entitySettings, entityType))
                    return;

                foreach (var grenade in activeGrenades)
                {
                    if (ShouldProcessGrenade(grenade))
                        ApplyChamsToGrenade(writes, game, grenade, entitySettings);
                }

                CleanupInactiveGrenades(activeGrenades);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Grenade Chams] Error processing: {ex.Message}");
            }
        }

        public static void Reset()
        {
            _grenadeStates.Clear();
        }

        public static void Initialize()
        {
            ApplyConfiguredColors();
            LoneLogging.WriteLine("[Grenade Chams] Manager initialized");
        }

        #endregion

        #region Private Implementation

        private static bool ShouldProcessGrenade(Grenade grenade)
        {
            if (!grenade.IsActive)
                return false;

            var state = GetState(grenade);
            return state?.IsActive != true;
        }

        private static bool AreMaterialsReady(ChamsConfig.EntityChamsSettings entitySettings, ChamsEntityType entityType)
        {
            return IsBasicMode(entitySettings.Mode) || ChamsManager.AreMaterialsReadyForEntityType(entityType);
        }

        private static bool IsBasicMode(ChamsMode mode) => mode == ChamsMode.Basic || mode == ChamsMode.Visible;

        private static void ApplyChamsToGrenade(ScatterWriteHandle writes, LocalGameWorld game, Grenade grenade, ChamsConfig.EntityChamsSettings entitySettings)
        {
            try
            {
                var state = GetOrCreateState(grenade);

                var materialId = GetMaterialId(entitySettings.Mode, game.CameraManager, ChamsEntityType.Grenade, entitySettings);

                if (materialId == -1 || !StateNeedsUpdate(state, entitySettings, materialId))
                    return;

                ApplyChamsInternal(writes, grenade, materialId);
                writes.Execute(() => ValidateWrite(grenade, game));

                UpdateState(state, entitySettings, materialId);

                LoneLogging.WriteLine($"[Grenade Chams] Applied chams to grenade {grenade.Name} - Mode: {entitySettings.Mode}");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Grenade Chams] Failed to apply chams to grenade {grenade.Name}: {ex.Message}");
            }
        }

        private static bool StateNeedsUpdate(GrenadeChamsState state, ChamsConfig.EntityChamsSettings entitySettings, int materialId)
        {
            return state.Mode != entitySettings.Mode || state.MaterialId != materialId;
        }

        private static void ApplyChamsInternal(ScatterWriteHandle writes, Grenade grenade, int materialId)
        {
            try
            {
                var pRenderersArray = Memory.ReadValue<ulong>(grenade + Offsets.Grenade.Renderers);
                if (!Utils.IsValidVirtualAddress(pRenderersArray))
                    return;

                using var renderersArray = MemArray<ulong>.Get(pRenderersArray);

                foreach (var renderer in renderersArray)
                {
                    if (!Utils.IsValidVirtualAddress(renderer))
                        continue;

                    var rendererNative = Memory.ReadValue<ulong>(renderer + 0x10);
                    if (Utils.IsValidVirtualAddress(rendererNative))
                        WriteMaterialToRenderer(writes, rendererNative, materialId);
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Grenade Chams] Failed to apply chams internally: {ex.Message}");
            }
        }

        private static void WriteMaterialToRenderer(ScatterWriteHandle writes, ulong renderer, int materialId)
        {
            try
            {
                int materialsCount = Memory.ReadValueEnsure<int>(renderer + UnityOffsets.Renderer.Count);
                if (materialsCount <= 0 || materialsCount > 30)
                    return;

                var materialsArrayPtr = Memory.ReadValueEnsure<ulong>(renderer + UnityOffsets.Renderer.Materials);
                materialsArrayPtr.ThrowIfInvalidVirtualAddress();

                var materials = Enumerable.Repeat(materialId, materialsCount).ToArray();
                writes.AddBufferEntry(materialsArrayPtr, materials.AsSpan());
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Grenade Chams] Failed to write material to renderer: {ex.Message}");
            }
        }

        private static bool ValidateWrite(Grenade grenade, LocalGameWorld game)
        {
            return grenade.IsActive && game.IsSafeToWriteMem;
        }

        private static void CleanupInactiveGrenades(IEnumerable<Grenade> activeGrenades)
        {
            var activeGrenadeBases = activeGrenades.Select(g => (ulong)g).ToHashSet();
            var inactiveGrenadeBases = _grenadeStates.Keys.Where(grenadeBase => !activeGrenadeBases.Contains(grenadeBase)).ToList();

            foreach (var grenadeBase in inactiveGrenadeBases)
                _grenadeStates.TryRemove(grenadeBase, out _);

            if (inactiveGrenadeBases.Any())
                LoneLogging.WriteLine($"[Grenade Chams] Cleaned up {inactiveGrenadeBases.Count} inactive grenades");
        }

        #endregion

        #region Material Management

        private static int GetMaterialId(ChamsMode mode, CameraManager cameraManager, ChamsEntityType entityType, ChamsConfig.EntityChamsSettings settings)
        {
            return mode switch
            {
                ChamsMode.Basic => GetBasicMaterialId(cameraManager),
                ChamsMode.Visible => GetVisibleMaterialId(cameraManager, settings),
                _ => ChamsManager.GetMaterialIDForLoot(mode, entityType)
            };
        }

        private static int GetBasicMaterialId(CameraManager cameraManager)
        {
            try
            {
                var ssaa = MonoBehaviour.GetComponent(cameraManager.FPSCamera, "SSAA");
                if (ssaa == 0) return -1;

                var opticMaskMaterial = Memory.ReadPtr(ssaa + UnityOffsets.SSAA.OpticMaskMaterial);
                if (opticMaskMaterial == 0) return -1;

                var opticMonoBehaviour = Memory.ReadPtr(opticMaskMaterial + ObjectClass.MonoBehaviourOffset);
                if (opticMonoBehaviour == 0) return -1;

                return Memory.ReadValue<MonoBehaviour>(opticMonoBehaviour).InstanceID;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Grenade Chams] Failed to get basic material ID: {ex.Message}");
                return -1;
            }
        }

        private static int GetVisibleMaterialId(CameraManager cameraManager, ChamsConfig.EntityChamsSettings settings)
        {
            try
            {
                var nvgComponent = MonoBehaviour.GetComponent(cameraManager.FPSCamera, "NightVision");
                if (nvgComponent == 0) return -1;

                var opticMaskMaterial = Memory.ReadPtr(nvgComponent + 0x98);
                if (opticMaskMaterial == 0) return -1;

                var opticMonoBehaviour = Memory.ReadPtr(opticMaskMaterial + ObjectClass.MonoBehaviourOffset);
                if (opticMonoBehaviour == 0) return -1;

                var materialId = Memory.ReadValue<MonoBehaviour>(opticMonoBehaviour).InstanceID;

                if (materialId != -1)
                {
                    var colorAddr = nvgComponent + 0xE0;
                    var unityColor = new UnityColor(settings.VisibleColor);
                    Memory.WriteValue(colorAddr, unityColor);
                }

                return materialId;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Grenade Chams] Failed to get visible material ID: {ex.Message}");
                return -1;
            }
        }

        #endregion

        #region Color Management

        public static void ApplyConfiguredColors()
        {
            try
            {
                if (!ChamsConfig.Enabled)
                    return;

                LoneLogging.WriteLine("[Grenade Chams] Applying configured colors to materials...");

                using var chamsColorMem = new RemoteBytes(SizeChecker<UnityColor>.Size);
                var colorsApplied = 0;

                var entityType = ChamsEntityType.Grenade;
                var entitySettings = ChamsConfig.GetEntitySettings(entityType);

                foreach (var materialKvp in ChamsManager.Materials)
                {
                    var (mode, matEntityType) = materialKvp.Key;
                    var material = materialKvp.Value;

                    if (matEntityType != entityType || material.InstanceID == 0)
                        continue;

                    try
                    {
                        SKColor visibleColor, invisibleColor;
                        var materialColorSettings = entitySettings.MaterialColors?.ContainsKey(mode) == true
                            ? entitySettings.MaterialColors[mode]
                            : null;

                        if (materialColorSettings != null)
                        {
                            if (!SKColor.TryParse(materialColorSettings.VisibleColor, out visibleColor))
                                visibleColor = SKColor.Parse("#FFFF00");

                            if (!SKColor.TryParse(materialColorSettings.InvisibleColor, out invisibleColor))
                                invisibleColor = SKColor.Parse("#FF0000");
                        }
                        else
                        {
                            if (!SKColor.TryParse(entitySettings.VisibleColor, out visibleColor))
                                visibleColor = SKColor.Parse("#FFFF00");

                            if (!SKColor.TryParse(entitySettings.InvisibleColor, out invisibleColor))
                                invisibleColor = SKColor.Parse("#FF0000");
                        }

                        var visibleUnityColor = new UnityColor(visibleColor.Red, visibleColor.Green, visibleColor.Blue, visibleColor.Alpha);
                        var invisibleUnityColor = new UnityColor(invisibleColor.Red, invisibleColor.Green, invisibleColor.Blue, invisibleColor.Alpha);

                        NativeMethods.SetMaterialColor(chamsColorMem, material.Address, material.ColorVisible, visibleUnityColor);
                        NativeMethods.SetMaterialColor(chamsColorMem, material.Address, material.ColorInvisible, invisibleUnityColor);
                        colorsApplied++;
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[Grenade Chams] Failed to set color for {mode}/{entityType}: {ex.Message}");
                    }
                }

                LoneLogging.WriteLine($"[Grenade Chams] Applied colors to {colorsApplied} grenade materials");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Grenade Chams] Failed to apply configured colors: {ex.Message}");
            }
        }

        #endregion

        #region State Management

        private static GrenadeChamsState GetOrCreateState(Grenade grenade)
        {
            return _grenadeStates.GetOrAdd(grenade, _ => new GrenadeChamsState());
        }

        private static GrenadeChamsState GetState(Grenade grenade)
        {
            return _grenadeStates.TryGetValue(grenade, out var state) ? state : null;
        }

        private static void UpdateState(GrenadeChamsState state, ChamsConfig.EntityChamsSettings entitySettings, int materialId)
        {
            state.Mode = entitySettings.Mode;
            state.MaterialId = materialId;
            state.LastAppliedTime = DateTime.UtcNow;
            state.IsActive = true;
        }

        #endregion

        #region Utilities

        public static string GetDiagnosticInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Grenade Chams Diagnostic Info]");
            sb.AppendLine($"Active Grenade States: {_grenadeStates.Count(s => s.Value.IsActive)}");
            sb.AppendLine($"Grenade States Tracked: {_grenadeStates.Count}");

            sb.AppendLine("\nMaterial Availability:");
            sb.AppendLine($"  {ChamsManager.GetEntityTypeStatus(ChamsEntityType.Grenade)}");

            return sb.ToString();
        }

        #endregion

        #region State Class

        private class GrenadeChamsState
        {
            public ChamsMode Mode { get; set; } = ChamsMode.Basic;
            public int MaterialId { get; set; } = -1;
            public DateTime LastAppliedTime { get; set; }
            public bool IsActive { get; set; }
        }

        #endregion
    }
}
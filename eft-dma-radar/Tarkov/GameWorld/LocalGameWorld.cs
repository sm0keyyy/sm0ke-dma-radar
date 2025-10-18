﻿using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_shared.Common.DMA;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Unity;

namespace eft_dma_radar.Tarkov.GameWorld
{
    /// <summary>
    /// Class containing Game (Raid) instance.
    /// IDisposable.
    /// </summary>
    public sealed class LocalGameWorld : IDisposable
    {
        #region Fields / Properties / Constructors

        public static implicit operator ulong(LocalGameWorld x) => x.Base;

        /// <summary>
        /// LocalGameWorld Address.
        /// </summary>
        private ulong Base { get; }

        private static Config Config => Program.Config;

        private static readonly WaitTimer _refreshWait = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly RegisteredPlayers _rgtPlayers;
        private readonly LootManager _lootManager;
        private readonly ExitManager _exfilManager;
        private readonly ExplosivesManager _grenadeManager;
        private readonly WorldInteractablesManager _worldInteractablesManager;
        public WorldInteractablesManager Interactables => _worldInteractablesManager;
        private readonly Thread _t1;
        private readonly Thread _t2;
        private readonly Thread _t3;
        private readonly Thread _t4;
        private readonly Thread _t5;

        /// <summary>
        /// Map ID of Current Map.
        /// </summary>
        public string MapID { get; }
        public static bool IsOffline { get; private set; }
        public bool InRaid => !_disposed;
        public IReadOnlyCollection<Player> Players => _rgtPlayers;
        public IReadOnlyCollection<IExplosiveItem> Explosives => _grenadeManager;
        public IReadOnlyCollection<IExitPoint> Exits => _exfilManager;
        public LocalPlayer LocalPlayer => _rgtPlayers?.LocalPlayer;
        public LootManager Loot => _lootManager;

        public QuestManager QuestManager { get; private set; }

        public CameraManager CameraManager { get; private set; }

        /// <summary>
        /// True if raid instance is still active, and safe to Write Memory.
        /// </summary>
        public bool IsSafeToWriteMem
        {
            get
            {
                try
                {
                    if (!InRaid)
                        return false;
                    return IsRaidActive();
                }
                catch
                {
                    return false;
                }
            }
        }

        static LocalGameWorld()
        {
            MemDMABase.GameStopped += Memory_GameStopped;
        }

        private static void Memory_GameStopped(object sender, EventArgs e)
        {
            _screenManagerStaticClass = null;
        }

        /// <summary>
        /// Game Constructor.
        /// Only called internally.
        /// </summary>
        private LocalGameWorld(ulong localGameWorld, string mapID)
        {
            var ct = _cts.Token;
            Base = localGameWorld;
            MapID = mapID;
            _t1 = new Thread(() => { RealtimeWorker(ct); })
            {
                IsBackground = true
            };
            _t2 = new Thread(() => { MiscWorker(ct); })
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _t3 = new Thread(() => { GrenadesWorker(ct); })
            {
                IsBackground = true
            };
            _t4 = new Thread(() => { FastWorker(ct); })
            {
                IsBackground = true
            };
            _t5 = new Thread(() => { InteractablesWorker(ct); })
            {
                IsBackground = true
            };
            // Reset static assets for a new raid/game.
            Player.Reset();
            var rgtPlayersAddr = Memory.ReadPtr(localGameWorld + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
            _rgtPlayers = new RegisteredPlayers(rgtPlayersAddr, this);
            if (_rgtPlayers.GetPlayerCount() < 1)
                throw new ArgumentOutOfRangeException(nameof(_rgtPlayers));
            _lootManager = new(localGameWorld, ct);
            _exfilManager = new(localGameWorld, _rgtPlayers.LocalPlayer.IsPmc);
            _grenadeManager = new(localGameWorld);
            LoneLogging.WriteLine($"[WorldInteractablesManager] Calling from LocalGameWorld: 0x{localGameWorld:X}");
            _worldInteractablesManager = new(localGameWorld);
        }

        /// <summary>
        /// Start all Game Threads.
        /// </summary>
        public void Start()
        {
            _t1.Start();
            _t2.Start();
            _t3.Start();
            _t4.Start();
            _t5.Start();
        }

        /// <summary>
        /// Blocks until a LocalGameWorld Singleton Instance can be instantiated.
        /// </summary>
        public static LocalGameWorld CreateGameInstance()
        {
            while (true)
            {
                ResourceJanitor.Run();
                Memory.ThrowIfNotInGame();
                try
                {
                    var instance = GetLocalGameWorld();
                    LoneLogging.WriteLine("Raid has started!");
                    return instance;
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"ERROR Instantiating Game Instance: {ex}");
                }
                finally
                {
                    Thread.Sleep(1000);
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Checks if a Raid has started.
        /// Loads Local Game World resources.
        /// </summary>
        /// <returns>True if Raid has started, otherwise False.</returns>
        private static LocalGameWorld GetLocalGameWorld()
        {
            try
            {
                /// Get LocalGameWorld
                var localGameWorld = Memory.ReadPtr(MonoLib.GameWorldField, false); // Game world >> Local Game World
                /// Get Selected Map
                // Check if this is an offline raid
                ulong classNamePtr = Memory.ReadPtrChain(localGameWorld, UnityOffsets.Component.To_NativeClassName, false);
                string className = Memory.ReadString(classNamePtr, 64, false);
                if (className == "ClientLocalGameWorld")
                    IsOffline = true;
                else
                    IsOffline = false;

                var mapPtr = Memory.ReadValue<ulong>(localGameWorld + Offsets.GameWorld.Location, false);
                if (mapPtr == 0x0) // Offline Mode
                {
                    var localPlayer = Memory.ReadPtr(localGameWorld + Offsets.ClientLocalGameWorld.MainPlayer, false);
                    LoneLogging.WriteLine($"[DEBUG] localPlayer: 0x{localPlayer:X}");
                    mapPtr = Memory.ReadPtr(localPlayer + Offsets.Player.Location, false);
                    LoneLogging.WriteLine($"[DEBUG] mapPtr (offline): 0x{mapPtr:X}");
                }

                var map = Memory.ReadUnityString(mapPtr, 64, false);
                LoneLogging.WriteLine("Detected Map " + map);
                if (!GameData.MapNames.ContainsKey(map)) // Also makes sure we're not in the hideout
                    throw new Exception("Invalid Map ID!");
                return new LocalGameWorld(localGameWorld, map);
            }
            catch (Exception ex)
            {
                throw new Exception("ERROR Getting LocalGameWorld", ex);
            }
        }

        /// <summary>
        /// Main Game Loop executed by Memory Worker Thread. Refreshes/Updates Player List and performs Player Allocations.
        /// </summary>
        public void Refresh()
        {
            try
            {
                ThrowIfRaidEnded();
                if (MapID.Equals("tarkovstreets", StringComparison.OrdinalIgnoreCase) ||
                    MapID.Equals("woods", StringComparison.OrdinalIgnoreCase))
                    TryAllocateBTR();
                _rgtPlayers.Refresh(); // Check for new players, add to list, etc.
            }
            catch (RaidEnded)
            {
                NotificationsShared.Info("Raid has ended!");
                LoneLogging.WriteLine("Raid has ended!");
                LootFilterControl.RemoveNonStaticGroups();
                LootItem.ClearNotificationHistory();
                Dispose();
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ERROR - Raid ended due to unhandled exception: {ex}");
                LootFilterControl.RemoveNonStaticGroups();
                LootItem.ClearNotificationHistory();
                throw;
            }
        }

        /// <summary>
        /// Throws an exception if the current raid instance has ended.
        /// </summary>
        /// <exception cref="RaidEnded"></exception>
        private void ThrowIfRaidEnded()
        {
            for (int i = 0; i < 5; i++) // Re-attempt if read fails -- 5 times
            {
                try
                {
                    if (!IsRaidActive())
                    {
                        GuardManager.ClearCache();
                        LootFilterControl.RemoveNonStaticGroups();
                        LootItem.ClearNotificationHistory();
                        throw new Exception("Not in raid!");
                    }
                    return;
                }
                catch { Thread.Sleep(10); } // short delay between read attempts
            }
            throw new RaidEnded(); // Still not valid? Raid must have ended.
        }

        /// <summary>
        /// Checks if the Current Raid is Active, and LocalPlayer is alive/active.
        /// </summary>
        /// <returns>True if raid is active, otherwise False.</returns>
        private bool IsRaidActive()
        {
            try
            {
                var localGameWorld = Memory.ReadPtr(MonoLib.GameWorldField, false);
                ArgumentOutOfRangeException.ThrowIfNotEqual(localGameWorld, this, nameof(localGameWorld));
                var mainPlayer = Memory.ReadPtr(localGameWorld + Offsets.ClientLocalGameWorld.MainPlayer, false);
                ArgumentOutOfRangeException.ThrowIfNotEqual(mainPlayer, _rgtPlayers.LocalPlayer, nameof(mainPlayer));
                return _rgtPlayers.GetPlayerCount() > 0;
            }
            catch
            {
                LootFilterControl.RemoveNonStaticGroups();
                LootItem.ClearNotificationHistory();
                return false;
            }
        }

        #endregion

        #region Raid Started Status

        private readonly Lock _rhsLock = new();
        private bool _raidHasStarted;
        private static ulong? _screenManagerStaticClass;
        /// <summary>
        /// Checks if the Raid has started (players can move about).
        /// If False will only lookup one thread at a time (Thread Safe). This means this call may block for a short time.
        /// </summary>
        /// <returns>True if raid has started, otherwise False.</returns>
        public bool RaidHasStarted
        {
            get
            {
                if (_raidHasStarted) // Only lookup once
                    return true;
                lock (_rhsLock)
                {
                    if (_raidHasStarted) // Only lookup once
                        return true;
                    try
                    {
                        if (this.CameraManager is CameraManager cm)
                        {
                            cm.FPSCamera.ThrowIfInvalidVirtualAddress();
                            // If we can get the camera but the screen name is MatchmakerFinalCountdown the raid hasn't fully begun yet
                            _screenManagerStaticClass ??= MonoLib.MonoClass.Find("Assembly-CSharp", ClassNames.ScreenManager.ClassName, out _).GetStaticFieldData();
                            if (_screenManagerStaticClass is ulong screenManagerStaticClass)
                            {
                                try
                                {
                                    var screenManager = Memory.ReadPtr(screenManagerStaticClass + Offsets.ScreenManager.Instance);
                                    var current = Memory.ReadPtr(screenManager + Offsets.ScreenManager.CurrentScreenController);
                                    var generic = Memory.ReadPtr(current + Offsets.CurrentScreenController.Generic);
                                    string name = ObjectClass.ReadName(generic, 128, false);
                                    if (name == "EftBattleUIScreen")
                                    {
                                        LoneLogging.WriteLine("Raid has started!");
                                        return _raidHasStarted = true;
                                    }
                                }
                                catch
                                {
                                    _screenManagerStaticClass = null;
                                }
                            }

                        }
                    }
                    catch
                    {
                    }
                }
                return false;
            }
        }

        #endregion

        #region Realtime Thread T1

        /// <summary>
        /// Managed Thread that does realtime (player position/info) updates.
        /// </summary>
        private void RealtimeWorker(CancellationToken ct) // t1
        {
            if (_disposed) return;
            try
            {
                LoneLogging.WriteLine("Realtime thread starting...");
                while (InRaid)
                {
                    if (Config.RatelimitRealtimeReads || !CameraManagerBase.EspRunning || (MemWriteFeature<Aimbot>.Instance.Enabled && Aimbot.Engaged))
                        _refreshWait.AutoWait(TimeSpan.FromMilliseconds(1), 1000);
                    ct.ThrowIfCancellationRequested();
                    RealtimeLoop(); // Realtime update loop (player positions, etc.)
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ERROR on Realtime Thread: {ex}"); // Log CRITICAL error
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                LoneLogging.WriteLine("Realtime thread stopping...");
            }
        }

        /// <summary>
        /// Updates all Realtime Values (View Matrix, player positions, etc.)
        /// </summary>
        private void RealtimeLoop()
        {
            using var _ = PerformanceProfiler.Instance.BeginSection("T1 Realtime Loop");
            try
            {
                var players = _rgtPlayers.Where(x => x.IsActive && x.IsAlive);
                var localPlayer = LocalPlayer;
                if (!players.Any()) // No players - Throttle
                {
                    Thread.Sleep(1);
                    return;
                }

                using var scatterMap = ScatterReadMap.Get();
                var round1 = scatterMap.AddRound(false);
                if (CameraManager is CameraManager cm)
                {
                    cm.OnRealtimeLoop(round1[-1], localPlayer);
                }
                int i = 0;
                foreach (var player in players)
                {
                    player.OnRealtimeLoop(round1[i++]);
                }

                scatterMap.Execute(); // Execute scatter read
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ERROR - UpdatePlayers Loop FAILED: {ex}");
            }
        }

        #endregion

        #region Misc Thread T2

        /// <summary>
        /// Managed Thread that does Misc. Local Game World Updates.
        /// </summary>
        private void MiscWorker(CancellationToken ct) // t2
        {
            if (_disposed) return;
            try
            {
                LoneLogging.WriteLine("Misc thread starting...");
                while (InRaid)
                {
                    ct.ThrowIfCancellationRequested();
                    UpdateMisc();
                    Thread.Sleep(50);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ERROR on Misc Thread: {ex}"); // Log CRITICAL error
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                LoneLogging.WriteLine("Misc thread stopping...");
            }
        }

        /// <summary>
        /// Validates Player Transforms -> Checks Exfils -> Checks Loot -> Checks Quests
        /// </summary>
        private void UpdateMisc()
        {
            using var _ = PerformanceProfiler.Instance.BeginSection("T2 Misc Loop");

            using (PerformanceProfiler.Instance.BeginSection("  T2 ValidateTransforms"))
            {
                ValidatePlayerTransforms(); // Check for transform anomalies
            }

            using (PerformanceProfiler.Instance.BeginSection("  T2 Exfils"))
            {
                _exfilManager.Refresh();
            }

            using (PerformanceProfiler.Instance.BeginSection("  T2 Loot"))
            {
                _lootManager.Refresh();
            }

            if (Config.LootWishlist)
            {
                try
                {
                    using (PerformanceProfiler.Instance.BeginSection("  T2 Wishlist"))
                    {
                        Memory.LocalPlayer?.RefreshWishlist();
                    }
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"[Wishlist] ERROR Refreshing: {ex}");
                }
            }

            using (PerformanceProfiler.Instance.BeginSection("  T2 Gear"))
            {
                RefreshGear(); // Update gear periodically
            }

            if (Config.QuestHelper.Enabled)
                try
                {
                    using (PerformanceProfiler.Instance.BeginSection("  T2 Quests"))
                    {
                        if (QuestManager is null)
                        {
                            var localPlayer = LocalPlayer;
                            if (localPlayer is not null)
                                QuestManager = new QuestManager(localPlayer.Profile);
                        }
                        else
                        {
                            QuestManager.Refresh();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"[QuestManager] CRITICAL ERROR: {ex}");
                }
        }

        /// <summary>
        /// Refresh Gear Manager
        /// </summary>
        private void RefreshGear()
        {
            try
            {
                var players = _rgtPlayers
                    .Where(x => x.IsHostileActive);
                if (players is not null && players.Any())
                    foreach (var player in players)
                        player.RefreshGear();
            }
            catch
            {
            }
        }

        public void ValidatePlayerTransforms()
        {
            try
            {
                var players = _rgtPlayers
                    .Where(x => x.IsActive && x.IsAlive && x is not BtrOperator);
                if (players.Any()) // at least 1 player
                {
                    using var scatterMap = ScatterReadMap.Get();
                    var round1 = scatterMap.AddRound();
                    var round2 = scatterMap.AddRound();
                    int i = 0;
                    foreach (var player in players)
                    {
                        player.OnValidateTransforms(round1[i], round2[i]);
                        i++;
                    }
                    scatterMap.Execute(); // execute scatter read
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ERROR - ValidatePlayerTransforms Loop FAILED: {ex}");
            }
        }

        #endregion

        #region Grenades Thread T3

        /// <summary>
        /// Managed Thread that does Grenade/Throwable updates.
        /// </summary>
        private void GrenadesWorker(CancellationToken ct) // t3
        {
            if (_disposed) return;
            try
            {
                LoneLogging.WriteLine("Grenades thread starting...");
                while (InRaid)
                {
                    ct.ThrowIfCancellationRequested();
                    _grenadeManager.Refresh();
                    Thread.Sleep(10);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ERROR on Grenades Thread: {ex}"); // Log CRITICAL error
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                LoneLogging.WriteLine("Grenades thread stopping...");
            }
        }

        #endregion

        #region Fast Thread T4

        /// <summary>
        /// Managed Thread that does Hands Manager / DMA Toolkit updates.
        /// No long operations on this thread.
        /// </summary>
        private void FastWorker(CancellationToken ct) // t4
        {
            if (_disposed) return;
            try
            {
                LoneLogging.WriteLine("FastWorker thread starting...");
                while (InRaid)
                {
                    ct.ThrowIfCancellationRequested();
                    RefreshCameraManager();
                    RefreshFast();
                    Thread.Sleep(100);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ERROR on FastWorker Thread: {ex}"); // Log CRITICAL error
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                LoneLogging.WriteLine("FastWorker thread stopping...");
            }
        }

        private void InteractablesWorker(CancellationToken ct)
        {
            if (_disposed) return;
            try
            {
                LoneLogging.WriteLine("Interactables thread starting...");
                while (InRaid)
                {
                    ct.ThrowIfCancellationRequested();
                    RefreshWorldInteractables();
                    Thread.Sleep(750);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ERROR on Interactables Thread: {ex}");
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                LoneLogging.WriteLine("Interactables thread stopping...");
            }
        }

        private void RefreshCameraManager()
        {
            try
            {
                CameraManager ??= new();
            }
            catch
            {
                //LoneLogging.WriteLine($"ERROR Refreshing Cameras! {ex}");
            }
        }
        private void RefreshWorldInteractables()
        {
            _worldInteractablesManager.Refresh();
        }
        /// <summary>
        /// Refresh various player items via Fast Worker Thread.
        /// Uses batched scatter reads for optimal performance (5-10x faster).
        /// </summary>
        private void RefreshFast()
        {
            using var _ = PerformanceProfiler.Instance.BeginSection("T4 Fast Loop");
            try
            {
                var players = _rgtPlayers
                    .Where(x => x.IsActive && x.IsAlive && x.Hands is not null);

                if (players is not null && players.Any())
                {
                    using (PerformanceProfiler.Instance.BeginSection("  T4 Hands (Batched)"))
                    {
                        // Use batched scatter read for all players (5-10x faster than individual reads)
                        using var scatterMap = ScatterReadMap.Get();
                        var round1 = scatterMap.AddRound();
                        var round2 = scatterMap.AddRound();
                        var round3 = scatterMap.AddRound();
                        var round4 = scatterMap.AddRound();

                        int i = 0;
                        foreach (var player in players)
                        {
                            player.OnFastLoop(round1[i], round2[i], round3[i], round4[i]);
                            i++;
                        }

                        scatterMap.Execute(); // Execute all reads in batch
                    }

                    using (PerformanceProfiler.Instance.BeginSection("  T4 Firearm"))
                    {
                        // Update local player firearm (non-DMA logic)
                        var localPlayer = players.FirstOrDefault(x => x is LocalPlayer) as LocalPlayer;
                        if (localPlayer is not null)
                            localPlayer.Firearm.Update();
                    }
                }
            }
            catch
            {
            }
        }

        #endregion

        #region BTR Vehicle

        /// <summary>
        /// Checks if there is a Bot attached to the BTR Turret and re-allocates the player instance.
        /// </summary>
        public void TryAllocateBTR()
        {
            try
            {
                var btrController = Memory.ReadPtr(this + Offsets.ClientLocalGameWorld.BtrController);
                var btrView = Memory.ReadPtr(btrController + Offsets.BtrController.BtrView);
                var btrTurretView = Memory.ReadPtr(btrView + Offsets.BTRView.turret);
                var btrOperator = Memory.ReadPtr(btrTurretView + Offsets.BTRTurretView.AttachedBot);
                _rgtPlayers.TryAllocateBTR(btrView, btrOperator);
            }
            catch
            {
                //LoneLogging.WriteLine($"ERROR Allocating BTR: {ex}");
            }
        }

        #endregion

        #region IDisposable

        private bool _disposed = false;

        public void Dispose()
        {
            bool disposed = Interlocked.Exchange(ref _disposed, true);
            if (!disposed)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
        }

        #endregion

        #region Types

        public sealed class RaidEnded : Exception
        {
            public RaidEnded()
            {
            }

            public RaidEnded(string message)
                : base(message)
            {
            }

            public RaidEnded(string message, Exception inner)
                : base(message, inner)
            {
            }
        }

        #endregion
    }
}
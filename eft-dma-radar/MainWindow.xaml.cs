﻿using System.Buffers;
using eft_dma_radar.Tarkov;
using eft_dma_radar.Tarkov.API;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.UI.SKWidgetControl;
using eft_dma_shared.Common.ESP;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel;
using HandyControl.Controls;
using HandyControl.Themes;
using HandyControl.Tools;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Linq;
using System.Security.Authentication.ExtendedProtection;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Switch = eft_dma_radar.Tarkov.GameWorld.Exits.Switch;
using Timer = System.Timers.Timer;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar
{
    public partial class MainWindow
    {
        #region Fields / Properties
        private DispatcherTimer _sizeChangeTimer;
        private readonly Stopwatch _fpsSw = new();
        private readonly PrecisionTimer _renderTimer;

        private IMouseoverEntity _mouseOverItem;
        private bool _mouseDown;
        private Point _lastMousePosition;
        private Vector2 _mapPanPosition;

        private Dictionary<string, PanelInfo> _panels;

        private int _zoomStep => Config.ZoomStep;
        private float _zoomToMouseStrength => Config.ZoomToMouse;
        private int _fps;
        private int _zoom = 100;
        public int _rotationDegrees = 0;
        private bool _freeMode = false;
        private bool _isDraggingToolbar = false;
        private Point _toolbarDragStartPoint;

        private const int MIN_LOOT_PANEL_WIDTH = 200;
        private const int MIN_LOOT_PANEL_HEIGHT = 200;
        private const int MIN_LOOT_FILTER_PANEL_WIDTH = 200;
        private const int MIN_LOOT_FILTER_PANEL_HEIGHT = 200;
        private const int MIN_WATCHLIST_PANEL_WIDTH = 200;
        private const int MIN_WATCHLIST_PANEL_HEIGHT = 200;
        private const int MIN_PLAYERHISTORY_PANEL_WIDTH = 350;
        private const int MIN_PLAYERHISTORY_PANEL_HEIGHT = 130;
        private const int MIN_ESP_PANEL_WIDTH = 200;
        private const int MIN_ESP_PANEL_HEIGHT = 200;
        private const int MIN_MEMORY_WRITING_PANEL_WIDTH = 200;
        private const int MIN_MEMORY_WRITING_PANEL_HEIGHT = 200;
        private const int MIN_SETTINGS_PANEL_WIDTH = 200;
        private const int MIN_SETTINGS_PANEL_HEIGHT = 200;
        private const int MIN_SEARCH_SETTINGS_PANEL_WIDTH = 200;
        private const int MIN_SEARCH_SETTINGS_PANEL_HEIGHT = 200;

        private readonly object _renderLock = new object();
        private volatile bool _isRendering = false;
        private volatile bool _uiInteractionActive = false;
        private DispatcherTimer _uiActivityTimer;

        private readonly Stopwatch _statusSw = Stopwatch.StartNew();
        private int _statusOrder = 1;

        // Performance optimization: Cache for MouseOverItems
        private IEnumerable<IMouseoverEntity> _mouseOverItemsCache;
        private int _mouseOverItemsCacheFrame;
        private int _currentFrame;

        // Performance optimization: Cache for map parameters
        private LoneMapParams _cachedMapParams;
        private int _cachedZoom = -1;
        private Vector2 _cachedPosition;
        private bool _cachedFreeMode;

        // Performance optimization: Reusable paint for pings to avoid allocations
        private readonly SKPaint _pingPaint = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4,
            IsAntialias = true
        };

        // Performance optimization: Cache for ordered players to eliminate repeated LINQ allocations
        private List<Player> _orderedPlayersCache;
        private int _orderedPlayersCacheFrame = -1;

        // Performance optimization: Cache for doors list
        private int _lastDoorCount = -1;

        // Performance optimization: Cache for filtered loot
        private bool _lootCorpseSettingsEnabled = false;
        private int _lootFilterCacheFrame = -1;

        private AimviewWidget _aimview;
        public AimviewWidget AimView { get => _aimview; private set => _aimview = value; }

        private PlayerInfoWidget _playerInfo;
        public PlayerInfoWidget PlayerInfo { get => _playerInfo; private set => _playerInfo = value; }

        private DebugInfoWidget _debugInfo;
        public DebugInfoWidget DebugInfo { get => _debugInfo; private set => _debugInfo = value; }

        private LootInfoWidget _lootInfo;
        public LootInfoWidget LootInfo { get => _lootInfo; private set => _lootInfo = value; }

        private QuestInfoWidget _questInfo;
        public QuestInfoWidget QuestInfo { get => _questInfo; private set => _questInfo = value; }

        /// <summary>
        /// Determines if MainWindow is ready or not
        /// </summary>
        public static bool Initialized = false;

        private static List<PingEffect> _activePings = new();

        /// <summary>
        /// Main UI/Application Config.
        /// </summary>
        public static Config Config => Program.Config;

        private static EntityTypeSettings MineEntitySettings = Config?.EntityTypeSettings?.GetSettings("Mine");

        /// <summary>
        /// Singleton Instance of MainWindow.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal static MainWindow Window { get; private set; }

        /// <summary>
        /// Current UI Scale Value for Primary Application Window.
        /// </summary>
        public static float UIScale => Config.UIScale;

        /// <summary>
        /// Currently 'Moused Over' Group.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public static int? MouseoverGroup { get; private set; }

        /// <summary>
        /// Map Identifier of Current Map.
        /// </summary>
        private static string MapID
        {
            get
            {
                var id = Memory?.MapID ?? "null";
                return id;
            }
        }

        /// <summary>
        /// Item Search Filter has been set/applied.
        /// </summary>
        private bool FilterIsSet =>
            !string.IsNullOrEmpty(LootSettings.txtLootToSearch.Text);

        /// <summary>
        /// True if corpses are visible as loot.
        /// </summary>
        private bool LootCorpsesVisible =>
            Config.ProcessLoot &&
            LootItem.CorpseSettings.Enabled &&
            !FilterIsSet;

        /// <summary>
        /// Game has started and Radar is starting up...
        /// </summary>
        private static bool Starting => Memory?.Starting ?? false;

        /// <summary>
        /// Radar has found Escape From Tarkov process and is ready.
        /// </summary>
        private static bool Ready => Memory?.Ready ?? false;

        /// <summary>
        /// Radar has found Local Game World, and a Raid Instance is active.
        /// </summary>
        private static bool InRaid => Memory?.InRaid ?? false;

        /// <summary>
        /// LocalPlayer (who is running Radar) 'Player' object.
        /// Returns the player the Current Window belongs to.
        /// </summary>
        private static LocalPlayer LocalPlayer => Memory?.LocalPlayer ?? null;

        /// <summary>
        /// All Filtered Loot on the map.
        /// </summary>
        private static IEnumerable<LootItem> Loot => Memory.Loot?.FilteredLoot;

        /// <summary>
        /// All Unfiltered Loot on the map.
        /// </summary>
        private static IEnumerable<LootItem> UnfilteredLoot => Memory.Loot?.UnfilteredLoot;

        /// <summary>
        /// All Static Containers on the map.
        /// </summary>
        private static IEnumerable<StaticLootContainer> Containers => Memory.Loot?.StaticLootContainers;

        /// <summary>
        /// All Players in Local Game World (including dead/exfil'd) 'Player' collection.
        /// </summary>
        private static IReadOnlyCollection<Player> AllPlayers => Memory.Players;

        /// <summary>
        /// Contains all 'Hot' grenades in Local Game World, and their position(s).
        /// </summary>
        private static IReadOnlyCollection<IExplosiveItem> Explosives => Memory.Explosives;

        /// <summary>
        /// Contains all 'Exfils' in Local Game World, and their status/position(s).
        /// </summary>
        private static IReadOnlyCollection<IExitPoint> Exits => Memory.Exits;

        private static LootSettingsControl LootSettings = new LootSettingsControl();

        /// <summary>
        /// Contains all 'mouse-overable' items.
        /// Performance optimized: caches result per frame to avoid repeated LINQ operations.
        /// </summary>
        private IEnumerable<IMouseoverEntity> MouseOverItems
        {
            get
            {
                // Return cached result if we're still in the same frame
                if (_mouseOverItemsCacheFrame == _currentFrame && _mouseOverItemsCache != null)
                    return _mouseOverItemsCache;

                var players = AllPlayers
                                  .Where(x => x is not Tarkov.EFTPlayer.LocalPlayer
                                              && !x.HasExfild && (LootCorpsesVisible ? x.IsAlive : true))
                              ?? Enumerable.Empty<Player>();

                var loot = Loot ?? Enumerable.Empty<IMouseoverEntity>();
                var containers = Containers ?? Enumerable.Empty<IMouseoverEntity>();
                var exits = Exits ?? Enumerable.Empty<IMouseoverEntity>();
                var questZones = Memory.QuestManager?.LocationConditions ?? Enumerable.Empty<IMouseoverEntity>();
                var switches = Switches ?? Enumerable.Empty<IMouseoverEntity>();
                var doors = Doors ?? Enumerable.Empty<Door>();

                if (FilterIsSet && !LootItem.CorpseSettings.Enabled)
                    players = players.Where(x =>
                        x.LootObject is null || !loot.Contains(x.LootObject));

                // Materialize the concatenated result to avoid repeated enumeration
                var result = loot.Concat(containers).Concat(players).Concat(exits).Concat(questZones).Concat(switches).Concat(doors).ToList();
                _mouseOverItemsCache = result.Count > 0 ? result : null;
                _mouseOverItemsCacheFrame = _currentFrame;

                return _mouseOverItemsCache;
            }
        }
        public void UpdateWindowTitle(string configName)
        {
            if (string.IsNullOrWhiteSpace(configName))
                TitleTextBlock.Text = "sm0keyyy's DMA Radar";
            else
                TitleTextBlock.Text = $"sm0keyyy's DMA Radar - {configName}";
        }
        private List<Tarkov.GameWorld.Exits.Switch> Switches = new List<Tarkov.GameWorld.Exits.Switch>();
        public static List<Tarkov.GameWorld.Interactables.Door> Doors = new List<Tarkov.GameWorld.Interactables.Door>();
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            Window = this;

            // Enable WPF rendering optimizations for smoother frame pacing
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default; // Use hardware acceleration
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display); // Optimized text rendering

            this.SizeChanged += MainWindow_SizeChanged;

            if (Config.WindowMaximized)
                this.WindowState = WindowState.Maximized;

            if (Config.WindowSize.Width > 0 && Config.WindowSize.Height > 0)
            {
                this.Width = Config.WindowSize.Width;
                this.Height = Config.WindowSize.Height;
            }

            EspColorOptions.LoadColors(Config);
            CameraManagerBase.UpdateViewportRes();

            var interval = TimeSpan.FromMilliseconds(1000d / Config.RadarTargetFPS);
            _renderTimer = new(interval);

            this.MouseDoubleClick += MainWindow_MouseDoubleClick;
            this.Closing += MainWindow_Closing;
            this.Loaded += (s, e) =>
            {
                Growl.Register("MainGrowl", GrowlPanel);

                RadarColorOptions.LoadColors(Config);
                EspColorOptions.LoadColors(Config);
                InterfaceColorOptions.LoadColors(Config);
                this.PreviewKeyDown += MainWindow_PreviewKeyDown;

                InitializeCanvas();
            };

            Initialized = true;
            InitializePanels();
            InitializeUIActivityMonitoring();
            InitilizeTelemetry();
        }

        private void btnDebug_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // debug code
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        #region Rendering
        /// <summary>
        /// Main Render Event.
        /// </summary>
        private void SkCanvas_PaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
        {
            var isStarting = Starting;
            var isReady = Ready; // cache bool
            var inRaid = InRaid; // cache bool
            var localPlayer = LocalPlayer; // cache ref to current player
            var canvas = e.Surface.Canvas; // get Canvas reference to draw on

            try
            {
                SkiaResourceTracker.TrackMainWindowFrame();

                // Increment frame counter for caching optimizations
                _currentFrame++;

                SetFPS(inRaid, canvas);
                // Check for map switch
                var mapID = MapID;
                //LoneLogging.WriteLine($"[DEBUG] MapID = {mapID}");

                if (string.IsNullOrWhiteSpace(mapID))
                    return;

                if (!mapID.Equals(LoneMapManager.Map?.ID, StringComparison.OrdinalIgnoreCase))
                {
                    LoneMapManager.LoadMap(mapID);
                    UpdateSwitches();
                }

                canvas.Clear(InterfaceColorOptions.RadarBackgroundColor); // Clear canvas

                if (inRaid && localPlayer is not null) // LocalPlayer is in a raid -> Begin Drawing...
                {
                    //LoneLogging.WriteLine($"[DEBUG] InRaid = {inRaid}, LocalPlayer = {(localPlayer != null)}");
                    var map = LoneMapManager.Map; // Cache ref
                    ArgumentNullException.ThrowIfNull(map, nameof(map));
                    var closestToMouse = _mouseOverItem; // cache ref
                    var mouseOverGrp = MouseoverGroup; // cache value for entire render
                                                       // Get LocalPlayer location
                    var localPlayerPos = localPlayer.Position;
                    var localPlayerMapPos = localPlayerPos.ToMapPos(map.Config);

                    // Prepare to draw Game Map - use smooth zoom/pan values
                    // Performance optimization: cache map parameters when zoom/position hasn't changed
                    LoneMapParams mapParams;
                    bool needsRecalc = _cachedZoom != _zoom || _cachedFreeMode != _freeMode;

                    if (_freeMode)
                    {
                        needsRecalc |= _cachedPosition.X != _mapPanPosition.X || _cachedPosition.Y != _mapPanPosition.Y;
                        if (needsRecalc)
                        {
                            mapParams = map.GetParameters(skCanvas, _zoom, ref _mapPanPosition);
                            _cachedMapParams = mapParams;
                            _cachedZoom = _zoom;
                            _cachedPosition = _mapPanPosition;
                            _cachedFreeMode = _freeMode;
                        }
                        else
                        {
                            mapParams = _cachedMapParams;
                        }
                    }
                    else
                    {
                        needsRecalc |= _cachedPosition.X != localPlayerMapPos.X || _cachedPosition.Y != localPlayerMapPos.Y;
                        if (needsRecalc)
                        {
                            mapParams = map.GetParameters(skCanvas, _zoom, ref localPlayerMapPos);
                            _cachedMapParams = mapParams;
                            _cachedZoom = _zoom;
                            _cachedPosition = localPlayerMapPos;
                            _cachedFreeMode = _freeMode;
                        }
                        else
                        {
                            mapParams = _cachedMapParams;
                        }
                    }

                    if (GeneralSettingsControl.chkMapSetup.IsChecked == true)
                        MapSetupControl.UpdatePlayerPosition(localPlayer);

                    var mapCanvasBounds = new SKRect() // Drawing Destination
                    {
                        Left = 0,
                        Right = (float)skCanvas.ActualWidth,
                        Top = 0,
                        Bottom = (float)skCanvas.ActualHeight
                    };

                    // Get the center of the canvas
                    var centerX = (mapCanvasBounds.Left + mapCanvasBounds.Right) / 2;
                    var centerY = (mapCanvasBounds.Top + mapCanvasBounds.Bottom) / 2;

                    // Apply a rotation transformation to the canvas
                    canvas.RotateDegrees(_rotationDegrees, centerX, centerY);

                    // Draw Map
                    map.Draw(canvas, localPlayer.Position.Y, mapParams.Bounds, mapCanvasBounds);

                    // Cache canvas dimensions for viewport culling (used multiple times)
                    var canvasWidth = (float)skCanvas.ActualWidth;
                    var canvasHeight = (float)skCanvas.ActualHeight;

                    // Update 'important' / quest item asterisk
                    SKPaints.UpdatePulsingAsteriskColor();

                    // Draw other players
                    var allPlayers = AllPlayers?
                        .Where(x => !x.HasExfild);

                    var battleMode = Config.BattleMode;

                    // Performance optimized: use helper method
                    if (!Config.PlayersOnTop && Config.ConnectGroups)
                    {
                        DrawGroupConnections(canvas, allPlayers, map, mapParams);
                    }

                    if (!Config.PlayersOnTop)
                    {
                        // Performance optimized: use cached ordered players list
                        var ordered = GetOrderedPlayers(allPlayers, localPlayer);
                        if (ordered is not null)
                        {
                            foreach (var player in ordered)
                            {
                                // Viewport culling: skip players that are off-screen
                                if (!IsWorldPosInViewport(player.Position, mapParams, canvasWidth, canvasHeight))
                                    continue;

                                player.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }

                    if (!battleMode && Config.Containers.Show && StaticLootContainer.Settings.Enabled)
                    {
                        var containers = Containers;
                        if (containers is not null)
                        {
                            foreach (var container in containers)
                            {
                                if (LootSettingsControl.ContainerIsTracked(container.ID ?? "NULL"))
                                {
                                    if (Config.Containers.HideSearched && container.Searched)
                                        continue;

                                    // Viewport culling: skip containers that are off-screen
                                    if (!IsWorldPosInViewport(container.Position, mapParams, canvasWidth, canvasHeight))
                                        continue;

                                    container.Draw(canvas, mapParams, localPlayer);
                                }
                            }
                        }
                    }

                    if (!battleMode && (Config.ProcessLoot &&
                        (LootItem.CorpseSettings.Enabled ||
                        LootItem.LootSettings.Enabled ||
                        LootItem.ImportantLootSettings.Enabled ||
                        LootItem.QuestItemSettings.Enabled)))
                    {
                        // Performance optimized: cache corpse settings check outside the loop
                        var corpseEnabled = LootItem.CorpseSettings.Enabled;
                        var loot = Loot;

                        if (loot is not null)
                        {
                            // Performance optimized: iterate backwards by index to avoid Reverse() allocation
                            // IReadOnlyList allows direct indexed access without creating intermediate collections
                            if (loot is IReadOnlyList<LootItem> lootList)
                            {
                                for (int i = lootList.Count - 1; i >= 0; i--)
                                {
                                    var item = lootList[i];

                                    // Skip quest items (handled separately below)
                                    if (item is QuestItem)
                                        continue;

                                    // Early-out: skip corpses if corpse rendering is disabled
                                    if (!corpseEnabled && item is LootCorpse)
                                        continue;

                                    // Viewport culling: skip items that are off-screen
                                    if (!IsWorldPosInViewport(item.Position, mapParams, canvasWidth, canvasHeight))
                                        continue;

                                    item.CheckNotify();
                                    item.Draw(canvas, mapParams, localPlayer);
                                }
                            }
                            else
                            {
                                // Fallback for non-list implementations
                                foreach (var item in loot.Reverse())
                                {
                                    // Skip quest items (handled separately below)
                                    if (item is QuestItem)
                                        continue;

                                    // Early-out: skip corpses if corpse rendering is disabled
                                    if (!corpseEnabled && item is LootCorpse)
                                        continue;

                                    // Viewport culling: skip items that are off-screen
                                    if (!IsWorldPosInViewport(item.Position, mapParams, canvasWidth, canvasHeight))
                                        continue;

                                    item.CheckNotify();
                                    item.Draw(canvas, mapParams, localPlayer);
                                }
                            }
                        }
                    }

                    if (!battleMode && Config.QuestHelper.Enabled)
                    {
                        if (LootItem.QuestItemSettings.Enabled && !localPlayer.IsScav)
                        {
                            var questItems = Loot?.Where(x => x is QuestItem);
                            if (questItems is not null)
                                foreach (var item in questItems)
                                    item.Draw(canvas, mapParams, localPlayer);
                        }

                        if (QuestManager.Settings.Enabled && !localPlayer.IsScav)
                        {
                            var questLocations = Memory.QuestManager?.LocationConditions;
                            if (questLocations is not null)
                                foreach (var loc in questLocations)
                                {
                                    if (loc.Outline is not null && !Config.QuestHelper.KillZones)
                                        continue;

                                    loc.Draw(canvas, mapParams, localPlayer);
                                }
                        }
                    }

                    if (MineEntitySettings.Enabled && GameData.Mines.TryGetValue(mapID, out var mines))
                    {
                        foreach (ref var mine in mines.Span)
                        {
                            var dist = Vector3.Distance(localPlayer.Position, mine);
                            if (dist > MineEntitySettings.RenderDistance)
                                continue;

                            var mineZoomedPos = mine.ToMapPos(map.Config).ToZoomedPos(mapParams);

                            var length = 3.5f * MainWindow.UIScale;

                            canvas.DrawLine(new SKPoint(mineZoomedPos.X - length, mineZoomedPos.Y + length),
                                           new SKPoint(mineZoomedPos.X + length, mineZoomedPos.Y - length),
                                           SKPaints.PaintExplosives);
                            canvas.DrawLine(new SKPoint(mineZoomedPos.X - length, mineZoomedPos.Y - length),
                                           new SKPoint(mineZoomedPos.X + length, mineZoomedPos.Y + length),
                                           SKPaints.PaintExplosives);
                        }
                    }

                    if (Tripwire.Settings.Enabled ||
                        Grenade.Settings.Enabled ||
                        MortarProjectile.Settings.Enabled)
                    {
                        var explosives = Explosives;
                        if (explosives is not null)
                        {
                            foreach (var explosive in explosives)
                            {
                                explosive.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }

                    if (!battleMode && (Exfil.Settings.Enabled ||
                        TransitPoint.Settings.Enabled))
                    {
                        var exits = Exits;
                        if (exits is not null)
                        {
                            foreach (var exit in exits)
                            {
                                if (exit is Exfil exfil && !localPlayer.IsPmc && exfil.Status is Exfil.EStatus.Closed)
                                    continue; // Only draw available SCAV Exfils

                                exit.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }

                    if (!battleMode && Switch.Settings.Enabled)
                        foreach (var swtch in Switches)
                            swtch.Draw(canvas, mapParams, localPlayer);

                    if (!battleMode && Door.Settings.Enabled)
                    {
                        var doorsSet = Memory.Game?.Interactables._Doors;
                        if (doorsSet is not null && doorsSet.Count > 0)
                        {
                            // Performance optimized: only regenerate doors list if count changed
                            if (doorsSet.Count != _lastDoorCount)
                            {
                                Doors = doorsSet.ToList();
                                _lastDoorCount = doorsSet.Count;
                            }

                            foreach (var door in Doors)
                                door.Draw(canvas, mapParams, localPlayer);
                        }
                        else
                        {
                            Doors = null;
                            _lastDoorCount = -1;
                        }
                    }

                    // Performance optimized: use helper method
                    if (Config.PlayersOnTop && Config.ConnectGroups)
                    {
                        DrawGroupConnections(canvas, allPlayers, map, mapParams);
                    }

                    if (Config.PlayersOnTop)
                    {
                        // Performance optimized: use cached ordered players list
                        var ordered = GetOrderedPlayers(allPlayers, localPlayer);
                        if (ordered is not null)
                        {
                            foreach (var player in ordered)
                            {
                                // Viewport culling: skip players that are off-screen
                                if (!IsWorldPosInViewport(player.Position, mapParams, canvasWidth, canvasHeight))
                                    continue;

                                player.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }

                    // Draw LocalPlayer
                    localPlayer.Draw(canvas, mapParams, localPlayer);

                    closestToMouse?.DrawMouseover(canvas, mapParams, localPlayer); // draw tooltip for object the mouse is closest to

                    // Performance optimized: iterate backwards to avoid ToList() allocation
                    if (_activePings.Count > 0)
                    {
                        var now = DateTime.UtcNow;

                        for (int i = _activePings.Count - 1; i >= 0; i--)
                        {
                            var ping = _activePings[i];
                            var elapsed = (float)(now - ping.StartTime).TotalSeconds;
                            if (elapsed > ping.DurationSeconds)
                            {
                                _activePings.RemoveAt(i);
                                continue;
                            }

                            float progress = elapsed / ping.DurationSeconds;
                            float radius = 10 + 50 * progress;
                            float alpha = 1f - progress;

                            var center = ping.Position.ToMapPos(map.Config).ToZoomedPos(mapParams);

                            // Performance optimized: reuse paint object, only update color
                            _pingPaint.Color = new SKColor(0, 255, 255, (byte)(alpha * 255));
                            canvas.DrawCircle(center.X, center.Y, radius, _pingPaint);
                        }
                    }

                    if (allPlayers is not null && Config.ShowInfoTab) // Players Overlay
                        _playerInfo?.Draw(canvas, localPlayer, allPlayers);

                    if (Config.AimviewWidgetEnabled)
                        _aimview?.Draw(canvas);

                    if (Config.ShowDebugWidget)
                        _debugInfo?.Draw(canvas);

                    if (Config.ShowLootInfoWidget)
                        _lootInfo?.Draw(canvas, UnfilteredLoot);

                    if (Config.ShowQuestInfoWidget)
                        _questInfo?.Draw(canvas);
                }
                else // LocalPlayer is *not* in a Raid -> Display Reason
                {
                    if (!isStarting)
                        GameNotRunningStatus(canvas);
                    else if (isStarting && !isReady)
                        StartingUpStatus(canvas);
                    else if (!inRaid)
                        WaitingForRaidStatus(canvas);
                }

                SetStatusText(canvas);
                canvas.Flush(); // commit frame to GPU
            }
            catch (Exception ex) // Log rendering errors
            {
                LoneLogging.WriteLine($"CRITICAL RENDER ERROR: {ex}");
            }
        }
        private static int DrawPriority(PlayerType t) => t switch
        {
            PlayerType.SpecialPlayer => 7,
            PlayerType.Streamer => 6,
            PlayerType.USEC or PlayerType.BEAR => 5,
            PlayerType.PScav => 4,
            PlayerType.AIBoss => 3,
            PlayerType.AIRaider => 2,
            _ => 1

        };

        /// <summary>
        /// Checks if a position is within the visible viewport with a small margin.
        /// Performance optimization to skip drawing off-screen entities.
        /// </summary>
        private static bool IsInViewport(SKPoint position, float canvasWidth, float canvasHeight, float margin = 100f)
        {
            return position.X >= -margin && position.X <= canvasWidth + margin &&
                   position.Y >= -margin && position.Y <= canvasHeight + margin;
        }

        /// <summary>
        /// Checks if a world position would be visible in the viewport when converted to screen coordinates.
        /// Performance optimization to skip drawing off-screen entities.
        /// </summary>
        private static bool IsWorldPosInViewport(System.Numerics.Vector3 worldPos, LoneMapParams mapParams, float canvasWidth, float canvasHeight, float margin = 100f)
        {
            var mapPos = worldPos.ToMapPos(mapParams.Map);
            var screenPos = mapPos.ToZoomedPos(mapParams);
            return IsInViewport(screenPos, canvasWidth, canvasHeight, margin);
        }

        /// <summary>
        /// Determines the level of detail to render based on zoom level.
        /// Performance optimization to reduce text rendering at extreme zoom levels.
        /// </summary>
        /// <param name="zoom">Current zoom percentage (100 = 100%)</param>
        /// <returns>0 = Full detail, 1 = Medium detail (text only), 2 = Low detail (dots only)</returns>
        private static int GetLODLevel(int zoom)
        {
            if (zoom >= 150) return 2;      // Far out - dots only
            if (zoom >= 110) return 1;      // Medium - text only, no icons
            return 0;                       // Normal - full detail
        }

        /// <summary>
        /// Performance-optimized method to draw group connection lines.
        /// Uses ArrayPool to reduce allocations and avoid repeated LINQ materializations.
        /// </summary>
        private void DrawGroupConnections(SKCanvas canvas, IEnumerable<Player> allPlayers, ILoneMap map, LoneMapParams mapParams)
        {
            if (allPlayers is null) return;

            // Performance optimized: Use List<Player> to avoid multiple enumerations
            var playersList = allPlayers as List<Player> ?? allPlayers.ToList();

            // Count grouped players first to avoid unnecessary allocation
            int groupedCount = 0;
            foreach (var p in playersList)
            {
                if (p.IsHumanHostileActive && p.GroupID != -1)
                    groupedCount++;
            }

            if (groupedCount == 0) return;

            // Collect unique group IDs
            var groups = new HashSet<int>();
            foreach (var p in playersList)
            {
                if (p.IsHumanHostileActive && p.GroupID != -1)
                    groups.Add(p.GroupID);
            }

            // Process each group
            foreach (var grp in groups)
            {
                // Count members in this group first
                int memberCount = 0;
                foreach (var p in playersList)
                {
                    if (p.IsHumanHostileActive && p.GroupID == grp)
                        memberCount++;
                }

                if (memberCount <= 1) continue;

                // Use ArrayPool to reduce allocations
                var positions = ArrayPool<SKPoint>.Shared.Rent(memberCount);
                try
                {
                    // Fill positions array
                    int idx = 0;
                    foreach (var p in playersList)
                    {
                        if (p.IsHumanHostileActive && p.GroupID == grp)
                        {
                            positions[idx++] = p.Position.ToMapPos(map.Config).ToZoomedPos(mapParams);
                        }
                    }

                    // Draw connection lines
                    for (int i = 0; i < memberCount - 1; i++)
                    {
                        canvas.DrawLine(
                            positions[i].X, positions[i].Y,
                            positions[i + 1].X, positions[i + 1].Y,
                            SKPaints.PaintConnectorGroup);
                    }
                }
                finally
                {
                    ArrayPool<SKPoint>.Shared.Return(positions);
                }
            }
        }

        /// <summary>
        /// Performance-optimized method to get ordered players with caching.
        /// Caches the ordered list for the current frame to avoid repeated LINQ allocations.
        /// </summary>
        private List<Player> GetOrderedPlayers(IEnumerable<Player> allPlayers, Player localPlayer)
        {
            if (allPlayers is null) return null;

            // Check if we can use the cached result
            if (_orderedPlayersCache != null && _orderedPlayersCacheFrame == _currentFrame)
            {
                return _orderedPlayersCache;
            }

            // Build new ordered list - only allocate once per frame
            _orderedPlayersCache = allPlayers
                .Where(p => p != localPlayer)
                .OrderBy(p => DrawPriority(p.Type))
                .ToList();

            _orderedPlayersCacheFrame = _currentFrame;
            return _orderedPlayersCache;
        }

        public static void PingItem(string itemName)
        {
            var matchingLootItems = Loot?.Where(x => x?.Name?.IndexOf(itemName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (matchingLootItems != null && matchingLootItems.Any())
            {
                foreach (var lootItem in matchingLootItems)
                {
                    _activePings.Add(new PingEffect
                    {
                        Position = lootItem.Position,
                        StartTime = DateTime.UtcNow
                    });
                    LoneLogging.WriteLine($"[Ping] Pinged item: {lootItem.Name} at {lootItem.Position}");
                }
            }
            else
            {
                LoneLogging.WriteLine($"[Ping] Item '{itemName}' not found.");
            }
        }

        private void SkCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            NotifyUIActivity();

            if (!InRaid)
                return;

            _mouseDown = true;
            _lastMousePosition = e.GetPosition(skCanvas);

            var shouldCheckMouseover = e.RightButton != MouseButtonState.Pressed;

            if (shouldCheckMouseover)
                CheckMouseoverItems(e.GetPosition(skCanvas));

            if (e.RightButton == MouseButtonState.Pressed &&
                _mouseOverItem is Player player &&
                player.IsHostileActive)
            {
                player.IsFocused = !player.IsFocused;
            }
        }

        private void SkCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                NotifyUIActivity();

            var currentPos = e.GetPosition(skCanvas);

            if (_mouseDown && _freeMode && e.LeftButton == MouseButtonState.Pressed)
            {
                var deltaX = (float)(currentPos.X - _lastMousePosition.X);
                var deltaY = (float)(currentPos.Y - _lastMousePosition.Y);

                _mapPanPosition.X -= deltaX;
                _mapPanPosition.Y -= deltaY;

                _lastMousePosition = currentPos;
                skCanvas.InvalidateVisual();
                return;
            }

            if (!InRaid)
            {
                ClearRefs();
                return;
            }

            var items = MouseOverItems;
            if (items?.Any() != true)
            {
                ClearRefs();
                return;
            }

            var mouse = new Vector2((float)currentPos.X, (float)currentPos.Y);
            var closest = items.Aggregate(
                (x1, x2) => Vector2.Distance(x1.MouseoverPosition, mouse)
                            < Vector2.Distance(x2.MouseoverPosition, mouse)
                        ? x1
                        : x2); // Get object 'closest' to mouse position

            if (Vector2.Distance(closest.MouseoverPosition, mouse) >= 12)
            {
                ClearRefs();
                return;
            }

            switch (closest)
            {
                case Player player:
                    _mouseOverItem = player;
                    if (player.IsHumanHostile
                        && player.GroupID != -1)
                        MouseoverGroup = player.GroupID; // Set group ID for closest player(s)
                    else
                        MouseoverGroup = null; // Clear Group ID
                    break;
                case LootCorpse corpseObj:
                    _mouseOverItem = corpseObj;
                    var corpse = corpseObj.PlayerObject;
                    if (corpse is not null)
                    {
                        if (corpse.IsHumanHostile && corpse.GroupID != -1)
                            MouseoverGroup = corpse.GroupID; // Set group ID for closest player(s)
                    }
                    else
                    {
                        MouseoverGroup = null;
                    }
                    break;
                case LootContainer ctr:
                    _mouseOverItem = ctr;
                    break;
                case LootItem ctr:
                    _mouseOverItem = ctr;
                    break;
                case IExitPoint exit:
                    _mouseOverItem = exit;
                    MouseoverGroup = null;
                    break;
                case Tarkov.GameWorld.Exits.Switch swtch:
                    _mouseOverItem = swtch;
                    MouseoverGroup = null;
                    break;
                case QuestLocation quest:
                    _mouseOverItem = quest;
                    MouseoverGroup = null;
                    break;
                case Door door:
                    _mouseOverItem = door;
                    MouseoverGroup = null;
                    break;
                default:
                    ClearRefs();
                    break;
            }
        }

        private void SkCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _mouseDown = false;

            if (_freeMode)
                skCanvas.InvalidateVisual();
        }

        private void SkCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!InRaid)
                return;

            var mousePosition = e.GetPosition(skCanvas);

            var zoomChange = e.Delta > 0 ? -_zoomStep : _zoomStep;
            var newZoom = Math.Max(1, Math.Min(200, _zoom + zoomChange));

            if (newZoom == _zoom)
                return;

            if (_freeMode && zoomChange < 0)
            {
                var zoomFactor = (float)newZoom / _zoom;
                var canvasCenter = new Vector2((float)skCanvas.ActualWidth / 2, (float)skCanvas.ActualHeight / 2);
                var mouseOffset = new Vector2((float)mousePosition.X - canvasCenter.X, (float)mousePosition.Y - canvasCenter.Y);

                var panAdjustment = mouseOffset * (1 - zoomFactor) * _zoomToMouseStrength;
                _mapPanPosition.X += panAdjustment.X;
                _mapPanPosition.Y += panAdjustment.Y;
            }

            _zoom = newZoom;
            skCanvas.InvalidateVisual();
        }
        private void ClearRefs()
        {
            _mouseOverItem = null;
            MouseoverGroup = null;
        }

        private void CheckMouseoverItems(Point mousePosition)
        {
            var mousePos = new Vector2((float)mousePosition.X, (float)mousePosition.Y);
            IMouseoverEntity closest = null;
            var closestDist = float.MaxValue;
            int? mouseoverGroup = null;

            var items = MouseOverItems;
            if (items != null)
            {
                foreach (var item in items)
                {
                    float dist = Vector2.Distance(mousePos, item.MouseoverPosition);
                    if (dist < closestDist && dist < 10f * UIScale)
                    {
                        closestDist = dist;
                        closest = item;

                        if (item is Player player)
                            mouseoverGroup = player.GroupID;
                    }
                }
            }

            _mouseOverItem = closest;
            MouseoverGroup = mouseoverGroup;
            skCanvas.InvalidateVisual();
        }

        private void IncrementStatus()
        {
            if (_statusSw.Elapsed.TotalSeconds >= 1d)
            {
                if (_statusOrder == 3)
                    _statusOrder = 1;
                else
                    _statusOrder++;
                _statusSw.Restart();
            }
        }

        private void GameNotRunningStatus(SKCanvas canvas)
        {
            const string notRunning = "Game Process Not Running!";
            float textWidth = SKPaints.TextRadarStatus.MeasureText(notRunning);
            canvas.DrawText(notRunning, ((float)skCanvas.ActualWidth / 2) - textWidth / 2f, (float)skCanvas.ActualHeight / 2,
                SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void StartingUpStatus(SKCanvas canvas)
        {
            const string startingUp1 = "Starting Up.";
            const string startingUp2 = "Starting Up..";
            const string startingUp3 = "Starting Up...";
            string status = _statusOrder == 1 ?
                startingUp1 : _statusOrder == 2 ?
                startingUp2 : startingUp3;
            float textWidth = SKPaints.TextRadarStatus.MeasureText(startingUp1);
            canvas.DrawText(status, ((float)skCanvas.ActualWidth / 2) - textWidth / 2f, (float)skCanvas.ActualHeight / 2,
                SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void WaitingForRaidStatus(SKCanvas canvas)
        {
            const string waitingFor1 = "Waiting for Raid Start.";
            const string waitingFor2 = "Waiting for Raid Start..";
            const string waitingFor3 = "Waiting for Raid Start...";
            string status = _statusOrder == 1 ?
                waitingFor1 : _statusOrder == 2 ?
                waitingFor2 : waitingFor3;
            float textWidth = SKPaints.TextRadarStatus.MeasureText(waitingFor1);
            canvas.DrawText(status, ((float)skCanvas.ActualWidth / 2) - textWidth / 2f, (float)skCanvas.ActualHeight / 2,
                SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void SetFPS(bool inRaid, SKCanvas canvas)
        {
            if (_fpsSw.ElapsedMilliseconds >= 1000)
            {
                if (Config.ShowDebugWidget)
                    _debugInfo?.UpdateFps(_fps);

                var fps = Interlocked.Exchange(ref _fps, 0); // Get FPS -> Reset FPS counter
                _fpsSw.Restart();
            }
            else
            {
                _fps++; // Increment FPS counter
            }
        }

        /// <summary>
        /// Set the status text in the top middle of the radar window.
        /// </summary>
        /// <param name="canvas"></param>
        private void SetStatusText(SKCanvas canvas)
        {
            try
            {
                var memWritesEnabled = MemWrites.Enabled;
                var aimEnabled = Aimbot.Config.Enabled;
                var mode = Aimbot.Config.TargetingMode;
                string label = null;

                if (memWritesEnabled && Config.MemWrites.RageMode)
                    label = MemWriteFeature<Aimbot>.Instance.Enabled ? $"{mode.GetDescription()}: RAGE MODE" : "RAGE MODE";

                else if (memWritesEnabled && aimEnabled)
                {
                    if (Aimbot.Config.RandomBone.Enabled)
                        label = $"{mode.GetDescription()}: Random Bone";
                    else if (Aimbot.Config.SilentAim.AutoBone)
                        label = $"{mode.GetDescription()}: Auto Bone";
                    else
                    {
                        var defaultBone = MemoryWritingControl.cboTargetBone.Text;
                        label = $"{mode.GetDescription()}: {defaultBone}";
                    }
                }

                if (memWritesEnabled)
                {
                    if (MemWriteFeature<WideLean>.Instance.Enabled)
                    {
                        if (label is null)
                            label = "Lean";
                        else
                            label += " (Lean)";
                    }

                    if (MemWriteFeature<LootThroughWalls>.Instance.Enabled && LootThroughWalls.ZoomEngaged)
                    {
                        if (label is null)
                            label = "LTW";
                        else
                            label += " (LTW)";
                    }
                    else if (MemWriteFeature<MoveSpeed>.Instance.Enabled)
                    {
                        if (label is null)
                            label = "MOVE";
                        else
                            label += " (MOVE)";
                    }
                }

                if (label is null)
                    return;

                var width = (float)skCanvas.CanvasSize.Width;
                var height = (float)skCanvas.CanvasSize.Height;
                var labelWidth = SKPaints.TextStatusSmall.MeasureText(label);
                var spacing = 1f * UIScale;
                var top = spacing; // Start from top of the canvas
                var labelHeight = SKPaints.TextStatusSmall.FontSpacing;
                var bgRect = new SKRect(
                    width / 2 - labelWidth / 2,
                    top,
                    width / 2 + labelWidth / 2,
                    top + labelHeight + spacing);
                canvas.DrawRect(bgRect, SKPaints.PaintTransparentBacker);
                var textLoc = new SKPoint(width / 2, top + labelHeight);
                canvas.DrawText(label, textLoc, SKPaints.TextStatusSmall);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR Setting Aim UI Text: {ex}");
            }
        }

        public void PurgeSKResources()
        {
            Dispatcher.Invoke(() =>
            {
                skCanvas?.GRContext?.PurgeResources();
            });
        }

        private void RenderTimer_Elapsed(object sender, EventArgs e)
        {
            // Simple check without lock - volatile bool is sufficient here
            if (_isRendering) return;

            try
            {
                // Use Invoke (synchronous) instead of BeginInvoke to let timer control frame pacing
                // This prevents frames from piling up in the dispatcher queue
                // Always use Render priority for consistent frame timing - background priority causes stuttering
                Dispatcher.Invoke(new Action(() =>
                {
                    if (_isRendering) return;
                    _isRendering = true;

                    try
                    {
                        skCanvas.InvalidateVisual();
                    }
                    finally
                    {
                        _isRendering = false;
                    }
                }), DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"Render timer error: {ex.Message}");
                _isRendering = false;
            }
        }

        private async void InitializeCanvas()
        {
            _renderTimer.Start();
            _fpsSw.Start();

            while (skCanvas.GRContext is null)
                await Task.Delay(25);

            // Enable WPF rendering optimizations for smoother frame pacing
            RenderOptions.SetBitmapScalingMode(skCanvas, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(skCanvas, EdgeMode.Aliased);
            RenderOptions.SetCachingHint(skCanvas, CachingHint.Cache);

            skCanvas.GRContext.SetResourceCacheLimit(536870912); // 512 MB

            SetupWidgets();

            // Setup the canvas and event handlers
            skCanvas.PaintSurface += SkCanvas_PaintSurface;
            skCanvas.MouseDown += SkCanvas_MouseDown;
            skCanvas.MouseMove += SkCanvas_MouseMove;
            skCanvas.MouseUp += SkCanvas_MouseUp;
            skCanvas.MouseWheel += SkCanvas_MouseWheel;

            _renderTimer.Elapsed += RenderTimer_Elapsed;

            MineEntitySettings = MainWindow.Config.EntityTypeSettings.GetSettings("Mine");
        }

        /// <summary>
        /// Setup Widgets after SKElement is fully loaded and window sized properly.
        /// </summary>
        private void SetupWidgets()
        {
            var left = 2;
            var top = 0;
            var right = (float)skCanvas.ActualWidth;
            var bottom = (float)skCanvas.ActualHeight;

            if (Config.Widgets.AimviewLocation == default)
            {
                Config.Widgets.AimviewLocation = new SKRect(left, bottom - 200, left + 200, bottom);
            }
            if (Config.Widgets.PlayerInfoLocation == default)
            {
                Config.Widgets.PlayerInfoLocation = new SKRect(right - 1, top + 45, right, top + 1);
            }
            if (Config.Widgets.DebugInfoLocation == default)
            {
                Config.Widgets.DebugInfoLocation = new SKRect(left, top, left, top);
            }
            if (Config.Widgets.LootInfoLocation == default)
            {
                Config.Widgets.LootInfoLocation = new SKRect(left, top + 45, left, top);
            }
            if (Config.Widgets.QuestInfoLocation == default)
            {
                Config.Widgets.QuestInfoLocation = new SKRect(left, top + 50, left + 500, top);
            }

            _aimview = new AimviewWidget(skCanvas, Config.Widgets.AimviewLocation, Config.Widgets.AimviewMinimized, UIScale);
            _playerInfo = new PlayerInfoWidget(skCanvas, Config.Widgets.PlayerInfoLocation, Config.Widgets.PlayerInfoMinimized, UIScale);
            _debugInfo = new DebugInfoWidget(skCanvas, Config.Widgets.DebugInfoLocation, Config.Widgets.DebugInfoMinimized, UIScale);
            _lootInfo = new LootInfoWidget(skCanvas, Config.Widgets.LootInfoLocation, Config.Widgets.LootInfoMinimized, UIScale);
            _questInfo = new QuestInfoWidget(skCanvas, Config.Widgets.QuestInfoLocation, Config.Widgets.QuestInfoMinimized, UIScale);
        }

        public void UpdateRenderTimerInterval(int targetFPS)
        {
            var interval = TimeSpan.FromMilliseconds(1000d / targetFPS);
            _renderTimer.Interval = interval;
        }
        #endregion

        #region Panel Events
        #region General Settings
        /// <summary>
        /// Handles opening general settings panel
        /// </summary>
        private void btnGeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("GeneralSettings");
        }

        /// <summary>
        /// Handle close request from settings panel
        /// </summary>
        private void GeneralSettingsControl_CloseRequested(object sender, EventArgs e)
        {
            GeneralSettingsPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from settings panel
        /// </summary>
        private void GeneralSettingsControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(GeneralSettingsPanel) + e.OffsetX;
            var top = Canvas.GetTop(GeneralSettingsPanel) + e.OffsetY;

            Canvas.SetLeft(GeneralSettingsPanel, left);
            Canvas.SetTop(GeneralSettingsPanel, top);

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from settings panel
        /// </summary>
        private void GeneralSettingsControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = GeneralSettingsPanel.Width + e.DeltaWidth;
            var height = GeneralSettingsPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_SETTINGS_PANEL_WIDTH);
            height = Math.Max(height, MIN_SETTINGS_PANEL_HEIGHT);

            GeneralSettingsPanel.Width = width;
            GeneralSettingsPanel.Height = height;

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Loot Settings
        /// <summary>
        /// Handles setting loot settings panel visibility
        /// </summary>
        private void btnLootSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("LootSettings");

        }

        /// <summary>
        /// Handle close request from loot settings control
        /// </summary>
        private void LootSettingsControl_CloseRequested(object sender, EventArgs e)
        {
            LootSettingsPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from loot settings control
        /// </summary>
        private void LootSettingsControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(LootSettingsPanel) + e.OffsetX;
            var top = Canvas.GetTop(LootSettingsPanel) + e.OffsetY;

            Canvas.SetLeft(LootSettingsPanel, left);
            Canvas.SetTop(LootSettingsPanel, top);

            EnsurePanelInBounds(LootSettingsPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from loot settings control
        /// </summary>
        private void LootSettingsControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = LootSettingsPanel.Width + e.DeltaWidth;
            var height = LootSettingsPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_LOOT_PANEL_WIDTH);
            height = Math.Max(height, MIN_LOOT_PANEL_HEIGHT);

            LootSettingsPanel.Width = width;
            LootSettingsPanel.Height = height;

            EnsurePanelInBounds(LootSettingsPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Memory Writing Settings
        /// <summary>
        /// Handles setting memory writing panel visibility
        /// </summary>
        private void btnMemoryWritingSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("MemoryWriting");
        }

        /// <summary>
        /// Handle close request from memory writing control
        /// </summary>
        private void MemoryWritingControl_CloseRequested(object sender, EventArgs e)
        {
            MemoryWritingPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from memory writing control
        /// </summary>
        private void MemoryWritingControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(MemoryWritingPanel) + e.OffsetX;
            var top = Canvas.GetTop(MemoryWritingPanel) + e.OffsetY;

            Canvas.SetLeft(MemoryWritingPanel, left);
            Canvas.SetTop(MemoryWritingPanel, top);

            EnsurePanelInBounds(MemoryWritingPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from memory writing control
        /// </summary>
        private void MemoryWritingControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = MemoryWritingPanel.Width + e.DeltaWidth;
            var height = MemoryWritingPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_MEMORY_WRITING_PANEL_WIDTH);
            height = Math.Max(height, MIN_MEMORY_WRITING_PANEL_HEIGHT);

            MemoryWritingPanel.Width = width;
            MemoryWritingPanel.Height = height;

            EnsurePanelInBounds(MemoryWritingPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region ESP Settings
        /// <summary>
        /// Handles setting ESP panel visibility
        /// </summary>
        private void btnESPSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("ESP");
        }

        /// <summary>
        /// Handle close request from ESP settings control
        /// </summary>
        private void ESPControl_CloseRequested(object sender, EventArgs e)
        {
            ESPPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from ESP settings control
        /// </summary>
        private void ESPControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(ESPPanel) + e.OffsetX;
            var top = Canvas.GetTop(ESPPanel) + e.OffsetY;

            Canvas.SetLeft(ESPPanel, left);
            Canvas.SetTop(ESPPanel, top);

            EnsurePanelInBounds(ESPPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from ESP settings control
        /// </summary>
        private void ESPControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = ESPPanel.Width + e.DeltaWidth;
            var height = ESPPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_ESP_PANEL_WIDTH);
            height = Math.Max(height, MIN_ESP_PANEL_HEIGHT);

            ESPPanel.Width = width;
            ESPPanel.Height = height;

            EnsurePanelInBounds(ESPPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Watchlist
        /// <summary>
        /// Handles setting Watchlist panel visibility
        /// </summary>
        private void btnWatchlist_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("Watchlist");
        }

        /// <summary>
        /// Handle close request from Watchlist control
        /// </summary>
        private void WatchlistControl_CloseRequested(object sender, EventArgs e)
        {
            WatchlistPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from Watchlist control
        /// </summary>
        private void WatchlistControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(WatchlistPanel) + e.OffsetX;
            var top = Canvas.GetTop(WatchlistPanel) + e.OffsetY;

            Canvas.SetLeft(WatchlistPanel, left);
            Canvas.SetTop(WatchlistPanel, top);

            EnsurePanelInBounds(WatchlistPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from Watchlist control
        /// </summary>
        private void WatchlistControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = WatchlistPanel.Width + e.DeltaWidth;
            var height = WatchlistPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_WATCHLIST_PANEL_WIDTH);
            height = Math.Max(height, MIN_WATCHLIST_PANEL_HEIGHT);

            WatchlistPanel.Width = width;
            WatchlistPanel.Height = height;

            EnsurePanelInBounds(WatchlistPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Player History
        /// <summary>
        /// Handles setting Player History panel visibility
        /// </summary>
        private void btnPlayerHistory_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("PlayerHistory");
        }

        /// <summary>
        /// Handle close request from Player History control
        /// </summary>
        private void PlayerHistoryControl_CloseRequested(object sender, EventArgs e)
        {
            PlayerHistoryPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from Player History control
        /// </summary>
        private void PlayerHistoryControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(PlayerHistoryPanel) + e.OffsetX;
            var top = Canvas.GetTop(PlayerHistoryPanel) + e.OffsetY;

            Canvas.SetLeft(PlayerHistoryPanel, left);
            Canvas.SetTop(PlayerHistoryPanel, top);

            EnsurePanelInBounds(PlayerHistoryPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from Player History control
        /// </summary>
        private void PlayerHistoryControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = PlayerHistoryPanel.Width + e.DeltaWidth;
            var height = PlayerHistoryPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_PLAYERHISTORY_PANEL_WIDTH);
            height = Math.Max(height, MIN_PLAYERHISTORY_PANEL_HEIGHT);

            PlayerHistoryPanel.Width = width;
            PlayerHistoryPanel.Height = height;

            EnsurePanelInBounds(PlayerHistoryPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Loot Filter Settings
        /// <summary>
        /// Handles setting loot filter panel visibility
        /// </summary>
        private void btnLootFilter_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("LootFilter");

            if (!LootFilterControl.firstRemove)
                LootFilterControl.RemoveNonStaticGroups();
        }

        /// <summary>
        /// Handle close request from loot filter control
        /// </summary>
        private void LootFilterControl_CloseRequested(object sender, EventArgs e)
        {
            LootFilterPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from loot filter control
        /// </summary>
        private void LootFilterControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(LootFilterPanel) + e.OffsetX;
            var top = Canvas.GetTop(LootFilterPanel) + e.OffsetY;

            Canvas.SetLeft(LootFilterPanel, left);
            Canvas.SetTop(LootFilterPanel, top);

            EnsurePanelInBounds(LootFilterPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from loot filter control
        /// </summary>
        private void LootFilterControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = LootFilterPanel.Width + e.DeltaWidth;
            var height = LootFilterPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_LOOT_FILTER_PANEL_WIDTH);
            height = Math.Max(height, MIN_LOOT_FILTER_PANEL_HEIGHT);

            LootFilterPanel.Width = width;
            LootFilterPanel.Height = height;

            EnsurePanelInBounds(LootFilterPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Map Setup Panel
        /// <summary>
        /// Handles setting map setup panel visibility
        /// </summary>
        private void btnMapSetup_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("MapSetup");

            if (LoneMapManager.Map?.Config != null)
            {
                var config = LoneMapManager.Map.Config;
                MapSetupControl.UpdateMapConfiguration(config.X, config.Y, config.Scale);
            }
            else
            {
                MapSetupControl.UpdateMapConfiguration(0, 0, 1);
            }
        }

        /// <summary>
        /// Handle close request from map setup control
        /// </summary>
        private void MapSetupControl_CloseRequested(object sender, EventArgs e)
        {
            GeneralSettingsControl.chkMapSetup.IsChecked = false;
        }

        /// <summary>
        /// Handle drag request from map setup control
        /// </summary>
        private void MapSetupControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(MapSetupPanel) + e.OffsetX;
            var top = Canvas.GetTop(MapSetupPanel) + e.OffsetY;

            Canvas.SetLeft(MapSetupPanel, left);
            Canvas.SetTop(MapSetupPanel, top);

            EnsurePanelInBounds(MapSetupPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from map setup control
        /// </summary>
        private void MapSetupControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = MapSetupPanel.Width + e.DeltaWidth;
            var height = MapSetupPanel.Height + e.DeltaHeight;

            width = Math.Max(width, 300);
            height = Math.Max(height, 300);

            MapSetupPanel.Width = width;
            MapSetupPanel.Height = height;

            EnsurePanelInBounds(MapSetupPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Player Preview Panel
        private void btnPlayerPreview_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("PlayerPreview");
        }

        private void PlayerPreviewControl_CloseRequested(object sender, EventArgs e)
        {
            PlayerPreviewPanel.Visibility = Visibility.Collapsed;
        }

        private void PlayerPreviewControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(PlayerPreviewPanel) + e.OffsetX;
            var top = Canvas.GetTop(PlayerPreviewPanel) + e.OffsetY;

            Canvas.SetLeft(PlayerPreviewPanel, left);
            Canvas.SetTop(PlayerPreviewPanel, top);

            EnsurePanelInBounds(PlayerPreviewPanel, mainContentGrid, adjustSize: false);
        }

        private void PlayerPreviewControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = PlayerPreviewPanel.Width + e.DeltaWidth;
            var height = PlayerPreviewPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_SETTINGS_PANEL_WIDTH);
            height = Math.Max(height, MIN_SETTINGS_PANEL_HEIGHT);

            PlayerPreviewPanel.Width = width;
            PlayerPreviewPanel.Height = height;

            EnsurePanelInBounds(PlayerPreviewPanel, mainContentGrid, adjustSize: false);
        }

        #endregion

        #region Search Panel Settings

        /// <summary>
        /// Handles visibility for search settings panel
        /// </summary>
        private void btnSettingsSearch_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("SettingsSearch");
        }

        /// <summary>
        /// Handle close request from loot filter control
        /// </summary>
        private void SettingsSearchControl_CloseRequested(object sender, EventArgs e)
        {
            SettingsSearchPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle close request from search control
        /// </summary>
        private void SettingsSearchControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(SettingsSearchPanel) + e.OffsetX;
            var top = Canvas.GetTop(SettingsSearchPanel) + e.OffsetY;

            Canvas.SetLeft(SettingsSearchPanel, left);
            Canvas.SetTop(SettingsSearchPanel, top);

            EnsurePanelInBounds(SettingsSearchPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from loot filter control
        /// </summary>
        private void SettingsSearchControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = SettingsSearchPanel.Width + e.DeltaWidth;
            var height = SettingsSearchPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_SEARCH_SETTINGS_PANEL_WIDTH);
            height = Math.Max(height, MIN_SEARCH_SETTINGS_PANEL_HEIGHT);

            SettingsSearchPanel.Width = width;
            SettingsSearchPanel.Height = height;

            EnsurePanelInBounds(SettingsSearchPanel, mainContentGrid, adjustSize: false);
        }

        public void EnsurePanelVisibleForElement(FrameworkElement fe)
        {
            // find the owning UserControl (e.g., LootSettingsControl)
            var uc = FindAncestor<UserControl>(fe);
            if (uc == null) return;

            // panelKey is the control's name without "Control", e.g., "LootSettings"
            var panelKey = uc.Name?.EndsWith("Control") == true
                ? uc.Name.Substring(0, uc.Name.Length - "Control".Length)
                : uc.Name;

            if (string.IsNullOrWhiteSpace(panelKey)) return;

            // make panel visible & bring to front via your existing map
            if (_panels != null && _panels.TryGetValue(panelKey, out var info))
            {
                info.Panel.Visibility = Visibility.Visible;
                BringPanelToFront(info.Canvas);
                EnsurePanelInBounds(info.Panel, mainContentGrid, adjustSize: false);
            }
        }

        // generic ancestor finder you already have in a few spots
        private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            for (DependencyObject? cur = start; cur != null; cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur))
                if (cur is T a) return a;
            return null;
        }
        #endregion

        #endregion

        #region Toolbar Events
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }

        private void CustomToolbar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                _isDraggingToolbar = true;
                _toolbarDragStartPoint = e.GetPosition(customToolbar);
                customToolbar.CaptureMouse();
                e.Handled = true;
            }
        }

        private void CustomToolbar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingToolbar && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(ToolbarCanvas);
                var offsetX = currentPosition.X - _toolbarDragStartPoint.X;
                var offsetY = currentPosition.Y - _toolbarDragStartPoint.Y;

                Canvas.SetLeft(customToolbar, offsetX);
                Canvas.SetTop(customToolbar, offsetY);

                EnsurePanelInBounds(customToolbar, mainContentGrid, adjustSize: false);

                e.Handled = true;
            }
        }

        private void CustomToolbar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingToolbar)
            {
                _isDraggingToolbar = false;
                customToolbar.ReleaseMouseCapture();

                e.Handled = true;
            }
        }

        private void btnRestart_Click(object sender, RoutedEventArgs e)
        {
            Memory.RestartRadar = true;

            LootFilterControl.RemoveNonStaticGroups();
            LootItem.ClearNotificationHistory();
        }

        private void btnFreeMode_Click(object sender, RoutedEventArgs e)
        {
            _freeMode = !_freeMode;
            if (_freeMode)
            {
                var localPlayer = LocalPlayer;
                if (localPlayer is not null && LoneMapManager.Map?.Config is not null)
                {
                    var localPlayerMapPos = localPlayer.Position.ToMapPos(LoneMapManager.Map.Config);
                    _mapPanPosition = new Vector2
                    {
                        X = localPlayerMapPos.X,
                        Y = localPlayerMapPos.Y
                    };
                }

                if (Application.Current.Resources["RegionBrush"] is SolidColorBrush regionBrush)
                {
                    var regionColor = regionBrush.Color;
                    var newR = (byte)Math.Max(0, regionColor.R > 50 ? regionColor.R - 30 : regionColor.R - 15);
                    var newG = (byte)Math.Max(0, regionColor.G > 50 ? regionColor.G - 30 : regionColor.G - 15);
                    var newB = (byte)Math.Max(0, regionColor.B > 50 ? regionColor.B - 30 : regionColor.B - 15);
                    var darkerColor = Color.FromArgb(regionColor.A, newR, newG, newB);

                    btnFreeMode.Background = new SolidColorBrush(darkerColor);
                }
                else
                {
                    btnFreeMode.Background = new SolidColorBrush(Colors.DarkRed);
                }

                btnFreeMode.ToolTip = "Free Mode (ON) - Click and drag to pan";
            }
            else
            {
                btnFreeMode.Background = new SolidColorBrush(Colors.Transparent);
                btnFreeMode.ToolTip = "Free Mode (OFF) - Map follows player";
            }

            skCanvas.InvalidateVisual();
        }
        #endregion

        #region Window Events
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                Growl.ClearGlobal();

                SaveToolbarPosition();
                SavePanelPositions();

                Config.WindowMaximized = (WindowState == WindowState.Maximized);

                if (!Config.WindowMaximized)
                    Config.WindowSize = new Size(ActualWidth, ActualHeight);

                Config.Widgets.AimviewLocation = _aimview.ClientRect;
                Config.Widgets.AimviewMinimized = _aimview.Minimized;
                Config.Widgets.PlayerInfoLocation = _playerInfo.ClientRect;
                Config.Widgets.PlayerInfoMinimized = _playerInfo.Minimized;
                Config.Widgets.DebugInfoLocation = _debugInfo.ClientRect;
                Config.Widgets.DebugInfoMinimized = _debugInfo.Minimized;
                Config.Widgets.LootInfoLocation = _lootInfo.ClientRect;
                Config.Widgets.LootInfoMinimized = _lootInfo.Minimized;
                Config.Widgets.QuestInfoLocation = _questInfo.ClientRect;
                Config.Widgets.QuestInfoMinimized = _questInfo.Minimized;
                Config.Zoom = _zoom;

                if (ESPForm.Window != null)
                {
                    if (ESPForm.Window.InvokeRequired)
                    {
                        ESPForm.Window.Invoke(new Action(() =>
                        {
                            ESPForm.Window.Close();
                        }));
                    }
                    else
                    {
                        ESPForm.Window.Close();
                    }
                }

                _renderTimer.Dispose();
                _pingPaint?.Dispose(); // Dispose reusable paint object

                Window = null;

                Memory.CloseFPGA(); // Close FPGA
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"Error during application shutdown: {ex}");
            }
        }

        private void MainWindow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (InRaid && _mouseOverItem is Player player && player.IsStreaming)
                try
                {
                    Process.Start(new ProcessStartInfo(player.StreamingURL) { UseShellExecute = true });
                }
                catch
                {
                    NotificationsShared.Error("Unable to open this player's Twitch. Do you have a default browser set?");
                }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsLoaded && _panels != null)
            {
                if (_sizeChangeTimer == null)
                {
                    _sizeChangeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    _sizeChangeTimer.Tick += (s, args) =>
                    {
                        _sizeChangeTimer.Stop();
                        EnsureAllPanelsInBounds();
                    };
                }

                _sizeChangeTimer.Stop();
                _sizeChangeTimer.Start();
            }
        }
        #endregion

        #region Helper Functions
        private void InitializeUIActivityMonitoring()
        {
            _uiActivityTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };

            _uiActivityTimer.Tick += (s, e) =>
            {
                _uiInteractionActive = false;
                _uiActivityTimer.Stop();
            };
        }
        private void InitilizeTelemetry()
        {
            bool sendUsage = Config?.SendAnonymousUsage ?? true;
            if (!sendUsage)
                return;

            Telemetry.Start(appVersion: Program.Version, true);
            Telemetry.BeatNow(Program.Version);
        }

        private void NotifyUIActivity()
        {
            _uiInteractionActive = true;
            _uiActivityTimer.Stop();
            _uiActivityTimer.Start();
        }

        /// <summary>
        /// Zooms the bitmap 'in'.
        /// </summary>
        /// <param name="amt">Amount to zoom in</param>
        /// <param name="mousePosition">Optional mouse position to zoom towards. If null, zooms to center.</param>
        public void ZoomIn(int amt, Point? mousePosition = null)
        {
            var newZoom = Math.Max(1, _zoom - amt);

            if (mousePosition.HasValue && _freeMode)
            {
                var zoomFactor = (float)newZoom / _zoom;
                var canvasCenter = new Vector2((float)skCanvas.ActualWidth / 2, (float)skCanvas.ActualHeight / 2);
                var mouseOffset = new Vector2((float)mousePosition.Value.X - canvasCenter.X, (float)mousePosition.Value.Y - canvasCenter.Y);

                var panAdjustment = mouseOffset * (1 - zoomFactor) * _zoomToMouseStrength;
                _mapPanPosition.X += panAdjustment.X;
                _mapPanPosition.Y += panAdjustment.Y;
            }

            _zoom = newZoom;
            skCanvas.InvalidateVisual();
        }

        /// <summary>
        /// Zooms the bitmap 'out'.
        /// </summary>
        /// <param name="amt">Amount to zoom in</param>
        public void ZoomOut(int amt)
        {
            // Zoom out never adjusts pan - always zooms from center
            _zoom = Math.Min(200, _zoom + amt);
            skCanvas.InvalidateVisual();
        }
        private void InitializeToolbar()
        {
            RestoreToolbarPosition();

            customToolbar.MouseLeftButtonDown += CustomToolbar_MouseLeftButtonDown;
            customToolbar.MouseMove += CustomToolbar_MouseMove;
            customToolbar.MouseLeftButtonUp += CustomToolbar_MouseLeftButtonUp;
        }

        private void InitializePanels()
        {
            var coordinator = PanelCoordinator.Instance;
            coordinator.RegisterRequiredPanel("GeneralSettings");
            coordinator.RegisterRequiredPanel("MemoryWriting");
            coordinator.RegisterRequiredPanel("ESP");
            coordinator.RegisterRequiredPanel("LootFilter");
            coordinator.RegisterRequiredPanel("LootSettings");
            coordinator.RegisterRequiredPanel("Watchlist");
            coordinator.RegisterRequiredPanel("PlayerHistory");
            coordinator.RegisterRequiredPanel("PlayerPreview");
            coordinator.RegisterRequiredPanel("SettingsSearch");
            coordinator.AllPanelsReady += OnAllPanelsReady;
        }

        private void OnAllPanelsReady(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                InitializeToolbar();
                InitializePanelsCollection();

                ESPControl.BringToFrontRequested += (s, args) => BringPanelToFront(ESPCanvas);
                GeneralSettingsControl.BringToFrontRequested += (s, args) => BringPanelToFront(GeneralSettingsCanvas);
                LootSettingsControl.BringToFrontRequested += (s, args) => BringPanelToFront(LootSettingsCanvas);
                MemoryWritingControl.BringToFrontRequested += (s, args) => BringPanelToFront(MemoryWritingCanvas);
                WatchlistControl.BringToFrontRequested += (s, args) => BringPanelToFront(WatchlistCanvas);
                PlayerHistoryControl.BringToFrontRequested += (s, args) => BringPanelToFront(PlayerHistoryCanvas);
                LootFilterControl.BringToFrontRequested += (s, args) => BringPanelToFront(LootFilterCanvas);
                PlayerPreviewControl.BringToFrontRequested += (s, args) => BringPanelToFront(PlayerPreviewCanvas);
                MapSetupControl.BringToFrontRequested += (s, args) => BringPanelToFront(MapSetupCanvas);
                SettingsSearchControl.BringToFrontRequested += (s, e) => BringPanelToFront(SettingsSearchCanvas);

                AttachPanelClickHandlers();
                RestorePanelPositions();
                AttachPanelEvents();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ValidateAndFixImportedToolbarPosition();
                    ValidateAndFixImportedPanelPositions();
                    EnsureAllPanelsInBounds();
                }), DispatcherPriority.Loaded);
            });

            LoneLogging.WriteLine("[PANELS] All panels are ready!");
        }

        public void EnsureAllPanelsInBounds()
        {
            try
            {
                if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
                    return;

                foreach (var panel in _panels.Values)
                {
                    EnsurePanelInBounds(panel.Panel, mainContentGrid);
                }

                if (customToolbar != null)
                    EnsurePanelInBounds(customToolbar, mainContentGrid);

                LoneLogging.WriteLine("[PANELS] Ensured all panels are within window bounds");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error ensuring panels in bounds: {ex.Message}");
            }
        }

        public void ValidateAndFixImportedPanelPositions()
        {
            try
            {
                if (Config.PanelPositions == null)
                {
                    LoneLogging.WriteLine("[PANELS] No panel positions in imported config");
                    return;
                }

                var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
                var windowHeight = ActualHeight > 0 ? ActualHeight : Height;

                if (windowWidth <= 0) windowWidth = 1200;
                if (windowHeight <= 0) windowHeight = 800;

                bool needsSave = false;

                foreach (var panelKey in _panels.Keys)
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panelKey);
                    if (propInfo?.GetValue(Config.PanelPositions) is PanelPositionConfig posConfig)
                    {
                        var originalLeft = posConfig.Left;
                        var originalTop = posConfig.Top;
                        var originalWidth = posConfig.Width;
                        var originalHeight = posConfig.Height;

                        var minWidth = GetMinimumPanelWidth(_panels[panelKey].Panel);
                        var minHeight = GetMinimumPanelHeight(_panels[panelKey].Panel);

                        if (posConfig.Width < minWidth)
                        {
                            posConfig.Width = minWidth;
                            needsSave = true;
                        }

                        if (posConfig.Height < minHeight)
                        {
                            posConfig.Height = minHeight;
                            needsSave = true;
                        }

                        var maxLeft = windowWidth - posConfig.Width - 10;
                        var maxTop = windowHeight - posConfig.Height - 10;

                        if (posConfig.Left < 0 || posConfig.Left > maxLeft)
                        {
                            posConfig.Left = Math.Max(10, Math.Min(posConfig.Left, maxLeft));
                            needsSave = true;
                        }

                        if (posConfig.Top < 0 || posConfig.Top > maxTop)
                        {
                            posConfig.Top = Math.Max(10, Math.Min(posConfig.Top, maxTop));
                            needsSave = true;
                        }

                        if (needsSave)
                        {
                            LoneLogging.WriteLine($"[PANELS] Fixed imported position for {panelKey}: " +
                                $"({originalLeft},{originalTop},{originalWidth},{originalHeight}) -> " +
                                $"({posConfig.Left},{posConfig.Top},{posConfig.Width},{posConfig.Height})");
                        }
                    }
                }

                if (needsSave)
                {
                    Config.Save();
                    LoneLogging.WriteLine("[PANELS] Saved corrected panel positions");
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error validating imported panel positions: {ex.Message}");
            }
        }

        public void ValidateAndFixImportedToolbarPosition()
        {
            try
            {
                if (Config.ToolbarPosition == null)
                {
                    LoneLogging.WriteLine("[TOOLBAR] No toolbar position in imported config");
                    return;
                }

                var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
                var windowHeight = ActualHeight > 0 ? ActualHeight : Height;

                if (windowWidth <= 0) windowWidth = 1200;
                if (windowHeight <= 0) windowHeight = 800;

                var toolbarConfig = Config.ToolbarPosition;
                var originalLeft = toolbarConfig.Left;
                var originalTop = toolbarConfig.Top;

                var toolbarWidth = customToolbar?.ActualWidth > 0 ? customToolbar.ActualWidth : 200;
                var toolbarHeight = customToolbar?.ActualHeight > 0 ? customToolbar.ActualHeight : 40;

                bool needsSave = false;
                const double minGap = 0;

                var maxLeft = windowWidth - toolbarWidth - minGap;
                var maxTop = windowHeight - toolbarHeight - minGap;

                if (toolbarConfig.Left < 0 || toolbarConfig.Left > maxLeft)
                {
                    toolbarConfig.Left = Math.Max(0, Math.Min(toolbarConfig.Left, maxLeft));
                    needsSave = true;
                }

                if (toolbarConfig.Top < 0 || toolbarConfig.Top > maxTop)
                {
                    toolbarConfig.Top = Math.Max(0, Math.Min(toolbarConfig.Top, maxTop));
                    needsSave = true;
                }

                if (needsSave)
                {
                    Config.Save();
                    LoneLogging.WriteLine($"[TOOLBAR] Fixed imported toolbar position: ({originalLeft},{originalTop}) -> ({toolbarConfig.Left},{toolbarConfig.Top})");
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[TOOLBAR] Error validating imported toolbar position: {ex.Message}");
            }
        }

        public void EnsurePanelInBounds(FrameworkElement panel, FrameworkElement container, bool adjustSize = true)
        {
            if (panel == null || container == null)
                return;

            try
            {
                var left = Canvas.GetLeft(panel);
                var top = Canvas.GetTop(panel);

                if (double.IsNaN(left)) left = 5;
                if (double.IsNaN(top)) top = 5;

                // Performance optimization: cache property accesses
                var containerWidth = container.ActualWidth;
                var containerHeight = container.ActualHeight;

                if (containerWidth <= 0) containerWidth = 1200;
                if (containerHeight <= 0) containerHeight = 800;

                var panelActualWidth = panel.ActualWidth;
                var panelActualHeight = panel.ActualHeight;
                var panelWidth = panelActualWidth > 0 ? panelActualWidth : panel.Width;
                var panelHeight = panelActualHeight > 0 ? panelActualHeight : panel.Height;

                if (adjustSize)
                {
                    if (panelWidth <= 0 || double.IsNaN(panelWidth))
                        panelWidth = GetMinimumPanelWidth(panel);
                    if (panelHeight <= 0 || double.IsNaN(panelHeight))
                        panelHeight = GetMinimumPanelHeight(panel);

                    panelWidth = Math.Min(panelWidth, containerWidth * 0.9);
                    panelHeight = Math.Min(panelHeight, containerHeight * 0.9);
                }

                const double padding = 0;
                var maxLeft = containerWidth - panelWidth - padding;
                var maxTop = containerHeight - panelHeight - padding;

                left = Math.Max(padding, Math.Min(left, maxLeft));
                top = Math.Max(padding, Math.Min(top, maxTop));

                Canvas.SetLeft(panel, left);
                Canvas.SetTop(panel, top);

                if (adjustSize)
                {
                    if (panel.Width != panelWidth) panel.Width = panelWidth;
                    if (panel.Height != panelHeight) panel.Height = panelHeight;
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error in EnsurePanelInBounds for {panel?.Name}: {ex.Message}");

                Canvas.SetLeft(panel, 0);
                Canvas.SetTop(panel, 0);
            }
        }

        private double GetMinimumPanelWidth(FrameworkElement panel)
        {
            return panel?.Name switch
            {
                "GeneralSettingsPanel" => MIN_SETTINGS_PANEL_WIDTH,
                "LootSettingsPanel" => MIN_LOOT_PANEL_WIDTH,
                "MemoryWritingPanel" => MIN_MEMORY_WRITING_PANEL_WIDTH,
                "ESPPanel" => MIN_ESP_PANEL_WIDTH,
                "WatchlistPanel" => MIN_WATCHLIST_PANEL_WIDTH,
                "PlayerHistoryPanel" => MIN_PLAYERHISTORY_PANEL_WIDTH,
                "LootFilterPanel" => MIN_LOOT_FILTER_PANEL_WIDTH,
                "PlayerPreviewPanel" => MIN_SETTINGS_PANEL_WIDTH,
                "SettingsSearchPanel" => MIN_SEARCH_SETTINGS_PANEL_WIDTH,
                "MapSetupPanel" => 300,
                _ => 200
            };
        }

        private double GetMinimumPanelHeight(FrameworkElement panel)
        {
            return panel?.Name switch
            {
                "GeneralSettingsPanel" => MIN_SETTINGS_PANEL_HEIGHT,
                "LootSettingsPanel" => MIN_LOOT_PANEL_HEIGHT,
                "MemoryWritingPanel" => MIN_MEMORY_WRITING_PANEL_HEIGHT,
                "ESPPanel" => MIN_ESP_PANEL_HEIGHT,
                "WatchlistPanel" => MIN_WATCHLIST_PANEL_HEIGHT,
                "PlayerHistoryPanel" => MIN_PLAYERHISTORY_PANEL_HEIGHT,
                "LootFilterPanel" => MIN_LOOT_FILTER_PANEL_HEIGHT,
                "PlayerPreviewPanel" => MIN_SETTINGS_PANEL_HEIGHT,
                "SettingsSearchPanel" => MIN_SEARCH_SETTINGS_PANEL_HEIGHT,
                "MapSetupPanel" => 300,
                _ => 200
            };
        }

        private void UpdateSwitches()
        {
            Switches.Clear();

            if (GameData.Switches.TryGetValue(MapID, out var switchesDict))
                foreach (var kvp in switchesDict)
                {
                    Switches.Add(new Tarkov.GameWorld.Exits.Switch(kvp.Value, kvp.Key));
                }
        }

        private void BringPanelToFront(Canvas panelCanvas)
        {
            var canvases = new List<Canvas>
            {
                GeneralSettingsCanvas,
                LootSettingsCanvas,
                MemoryWritingCanvas,
                ESPCanvas,
                WatchlistCanvas,
                PlayerHistoryCanvas,
                LootFilterCanvas,
                PlayerPreviewCanvas,
                MapSetupCanvas
            };

            foreach (var canvas in canvases)
            {
                Canvas.SetZIndex(canvas, 1000);
            }

            Canvas.SetZIndex(panelCanvas, 1001);
        }

        private void AttachPreviewMouseDown(FrameworkElement panel, Canvas canvas)
        {
            panel.PreviewMouseDown += (s, e) =>
            {
                BringPanelToFront(canvas);
            };
        }

        private void AttachPanelClickHandlers()
        {
            AttachPreviewMouseDown(GeneralSettingsPanel, GeneralSettingsCanvas);
            AttachPreviewMouseDown(LootSettingsPanel, LootSettingsCanvas);
            AttachPreviewMouseDown(MemoryWritingPanel, MemoryWritingCanvas);
            AttachPreviewMouseDown(ESPPanel, ESPCanvas);
            AttachPreviewMouseDown(WatchlistPanel, WatchlistCanvas);
            AttachPreviewMouseDown(PlayerHistoryPanel, PlayerHistoryCanvas);
            AttachPreviewMouseDown(LootFilterPanel, LootFilterCanvas);
            AttachPreviewMouseDown(PlayerPreviewPanel, PlayerPreviewCanvas);
            AttachPreviewMouseDown(MapSetupPanel, MapSetupCanvas);
            AttachPreviewMouseDown(SettingsSearchPanel, SettingsSearchCanvas);

            ESPCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(ESPCanvas);
            GeneralSettingsCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(GeneralSettingsCanvas);
            LootSettingsCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(LootSettingsCanvas);
            MemoryWritingCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(MemoryWritingCanvas);
            WatchlistCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(WatchlistCanvas);
            PlayerHistoryCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(PlayerHistoryCanvas);
            LootFilterCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(LootFilterCanvas);
            PlayerPreviewCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(PlayerPreviewCanvas);
            MapSetupCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(MapSetupCanvas);
            SettingsSearchCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(SettingsSearchCanvas);
        }

        private void TogglePanelVisibility(string panelKey)
        {
            if (_panels.TryGetValue(panelKey, out var panelInfo))
            {
                if (panelInfo.Panel.Visibility == Visibility.Visible)
                {
                    panelInfo.Panel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panelKey);

                    if (propInfo != null)
                    {
                        var posConfig = propInfo.GetValue(Config.PanelPositions) as PanelPositionConfig;

                        if (posConfig != null)
                        {
                            posConfig.ApplyToPanel(panelInfo.Panel, panelInfo.Canvas);
                        }
                        else
                        {
                            Canvas.SetLeft(panelInfo.Panel, mainContentGrid.ActualWidth - panelInfo.Panel.Width - 20);
                            Canvas.SetTop(panelInfo.Panel, 20);
                        }
                    }

                    panelInfo.Panel.Visibility = Visibility.Visible;
                    BringPanelToFront(panelInfo.Canvas);
                }

                SaveSinglePanelPosition(panelKey);
            }
        }

        private void AttachPanelEvents()
        {
            EventHandler<PanelDragEventArgs> sharedDragHandler = (s, e) =>
            {
                NotifyUIActivity();
                var controlName = (s as UserControl)?.Name;
                if (controlName != null && controlName.EndsWith("Control") && controlName.Length > "Control".Length)
                {
                    string panelKey = controlName.Substring(0, controlName.Length - "Control".Length);
                    if (_panels.TryGetValue(panelKey, out var panelInfo))
                    {
                        var left = Canvas.GetLeft(panelInfo.Panel) + e.OffsetX;
                        var top = Canvas.GetTop(panelInfo.Panel) + e.OffsetY;

                        Canvas.SetLeft(panelInfo.Panel, left);
                        Canvas.SetTop(panelInfo.Panel, top);

                        EnsurePanelInBounds(panelInfo.Panel, mainContentGrid, adjustSize: false);
                        SaveSinglePanelPosition(panelKey);
                    }
                }
            };

            EventHandler<PanelResizeEventArgs> sharedResizeHandler = (s, e) =>
            {
                NotifyUIActivity();
                var controlName = (s as UserControl)?.Name;
                if (controlName != null && controlName.EndsWith("Control") && controlName.Length > "Control".Length)
                {
                    string panelKey = controlName.Substring(0, controlName.Length - "Control".Length);
                    if (_panels.TryGetValue(panelKey, out var panelInfo))
                    {
                        var width = panelInfo.Panel.Width + e.DeltaWidth;
                        var height = panelInfo.Panel.Height + e.DeltaHeight;

                        width = Math.Max(width, panelInfo.MinWidth);
                        height = Math.Max(height, panelInfo.MinHeight);

                        var currentLeft = Canvas.GetLeft(panelInfo.Panel);
                        var currentTop = Canvas.GetTop(panelInfo.Panel);

                        var maxWidth = mainContentGrid.ActualWidth - currentLeft;
                        var maxHeight = mainContentGrid.ActualHeight - currentTop;

                        width = Math.Min(width, Math.Max(panelInfo.MinWidth, maxWidth));
                        height = Math.Min(height, Math.Max(panelInfo.MinHeight, maxHeight));

                        panelInfo.Panel.Width = width;
                        panelInfo.Panel.Height = height;

                        EnsurePanelInBounds(panelInfo.Panel, mainContentGrid, adjustSize: false);

                        SaveSinglePanelPosition(panelKey);
                    }
                }
            };

            EventHandler sharedCloseHandler = (s, e) =>
            {
                NotifyUIActivity();
                var controlName = (s as UserControl)?.Name;
                if (controlName != null && controlName.EndsWith("Control") && controlName.Length > "Control".Length)
                {
                    string panelKey = controlName.Substring(0, controlName.Length - "Control".Length);
                    if (_panels.TryGetValue(panelKey, out var panelInfo))
                    {
                        panelInfo.Panel.Visibility = Visibility.Collapsed;
                        SaveSinglePanelPosition(panelKey);
                    }
                }
            };

            GeneralSettingsControl.DragRequested += sharedDragHandler;
            GeneralSettingsControl.ResizeRequested += sharedResizeHandler;
            GeneralSettingsControl.CloseRequested += sharedCloseHandler;

            LootSettingsControl.DragRequested += sharedDragHandler;
            LootSettingsControl.ResizeRequested += sharedResizeHandler;
            LootSettingsControl.CloseRequested += sharedCloseHandler;

            MemoryWritingControl.DragRequested += sharedDragHandler;
            MemoryWritingControl.ResizeRequested += sharedResizeHandler;
            MemoryWritingControl.CloseRequested += sharedCloseHandler;

            ESPControl.DragRequested += sharedDragHandler;
            ESPControl.ResizeRequested += sharedResizeHandler;
            ESPControl.CloseRequested += sharedCloseHandler;

            WatchlistControl.DragRequested += sharedDragHandler;
            WatchlistControl.ResizeRequested += sharedResizeHandler;
            WatchlistControl.CloseRequested += sharedCloseHandler;

            PlayerHistoryControl.DragRequested += sharedDragHandler;
            PlayerHistoryControl.ResizeRequested += sharedResizeHandler;
            PlayerHistoryControl.CloseRequested += sharedCloseHandler;

            LootFilterControl.DragRequested += sharedDragHandler;
            LootFilterControl.ResizeRequested += sharedResizeHandler;
            LootFilterControl.CloseRequested += sharedCloseHandler;

            PlayerPreviewControl.DragRequested += sharedDragHandler;
            PlayerPreviewControl.ResizeRequested += sharedResizeHandler;
            PlayerPreviewControl.CloseRequested += sharedCloseHandler;

            MapSetupControl.DragRequested += sharedDragHandler;
            MapSetupControl.CloseRequested += sharedCloseHandler;

            SettingsSearchControl.DragRequested += sharedDragHandler;
            SettingsSearchControl.ResizeRequested += sharedResizeHandler;
            SettingsSearchControl.CloseRequested += sharedCloseHandler;
        }

        private void InitializePanelsCollection()
        {
            _panels = new Dictionary<string, PanelInfo>
            {
                ["GeneralSettings"] = new PanelInfo(GeneralSettingsPanel, GeneralSettingsCanvas, "GeneralSettings", MIN_SETTINGS_PANEL_WIDTH, MIN_SETTINGS_PANEL_HEIGHT),
                ["LootSettings"] = new PanelInfo(LootSettingsPanel, LootSettingsCanvas, "LootSettings", MIN_LOOT_PANEL_WIDTH, MIN_LOOT_PANEL_HEIGHT),
                ["MemoryWriting"] = new PanelInfo(MemoryWritingPanel, MemoryWritingCanvas, "MemoryWriting", MIN_MEMORY_WRITING_PANEL_WIDTH, MIN_MEMORY_WRITING_PANEL_HEIGHT),
                ["ESP"] = new PanelInfo(ESPPanel, ESPCanvas, "ESP", MIN_ESP_PANEL_WIDTH, MIN_ESP_PANEL_HEIGHT),
                ["Watchlist"] = new PanelInfo(WatchlistPanel, WatchlistCanvas, "Watchlist", MIN_WATCHLIST_PANEL_WIDTH, MIN_WATCHLIST_PANEL_HEIGHT),
                ["PlayerHistory"] = new PanelInfo(PlayerHistoryPanel, PlayerHistoryCanvas, "PlayerHistory", MIN_PLAYERHISTORY_PANEL_WIDTH, MIN_PLAYERHISTORY_PANEL_HEIGHT),
                ["LootFilter"] = new PanelInfo(LootFilterPanel, LootFilterCanvas, "LootFilter", MIN_LOOT_FILTER_PANEL_WIDTH, MIN_LOOT_FILTER_PANEL_HEIGHT),
                ["PlayerPreview"] = new PanelInfo(PlayerPreviewPanel, PlayerPreviewCanvas, "PlayerPreview", MIN_SETTINGS_PANEL_WIDTH, MIN_SETTINGS_PANEL_HEIGHT),
                ["MapSetup"] = new PanelInfo(MapSetupPanel, MapSetupCanvas, "MapSetup", 300, 300),
                ["SettingsSearch"] = new PanelInfo(SettingsSearchPanel, SettingsSearchCanvas, "SettingsSearch", MIN_SEARCH_SETTINGS_PANEL_WIDTH, MIN_SEARCH_SETTINGS_PANEL_HEIGHT)
            };
        }

        private void SavePanelPositions()
        {
            try
            {
                foreach (var panel in _panels)
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panel.Key);
                    if (propInfo != null)
                    {
                        var posConfig = PanelPositionConfig.FromPanel(panel.Value.Panel, panel.Value.Canvas);
                        propInfo.SetValue(Config.PanelPositions, posConfig);
                    }
                }

                Config.Save();
                LoneLogging.WriteLine("[PANELS] Saved panel positions to config");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error saving panel positions: {ex.Message}");
            }
        }

        private void SaveSinglePanelPosition(string panelKey)
        {
            try
            {
                if (_panels.TryGetValue(panelKey, out var panelInfo))
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panelKey);
                    if (propInfo != null)
                    {
                        var posConfig = PanelPositionConfig.FromPanel(panelInfo.Panel, panelInfo.Canvas);
                        propInfo.SetValue(Config.PanelPositions, posConfig);
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error saving panel position for {panelKey}: {ex.Message}");
            }
        }

        public void RestorePanelPositions()
        {
            try
            {
                foreach (var panel in _panels)
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panel.Key);

                    if (propInfo != null)
                    {
                        var posConfig = propInfo.GetValue(Config.PanelPositions) as PanelPositionConfig;

                        if (posConfig != null)
                        {
                            posConfig.ApplyToPanel(panel.Value.Panel, panel.Value.Canvas);
                            EnsurePanelInBounds(panel.Value.Panel, mainContentGrid, adjustSize: false);
                        }
                        else
                        {
                            Canvas.SetLeft(panel.Value.Panel, 20);
                            Canvas.SetTop(panel.Value.Panel, 20);
                        }
                    }
                }

                LoneLogging.WriteLine("[PANELS] Restored panel positions from config with bounds checking");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error restoring panel positions: {ex.Message}");
            }
        }

        private void SaveToolbarPosition()
        {
            try
            {
                Config.ToolbarPosition = ToolbarPositionConfig.FromToolbar(customToolbar);
                LoneLogging.WriteLine("[TOOLBAR] Saved toolbar position to config");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[TOOLBAR] Error saving toolbar position: {ex.Message}");
            }
        }

        public void RestoreToolbarPosition()
        {
            try
            {
                if (Config.ToolbarPosition != null)
                {
                    Config.ToolbarPosition.ApplyToToolbar(customToolbar);
                    LoneLogging.WriteLine("[TOOLBAR] Restored toolbar position from config");
                }
                else
                {
                    Canvas.SetLeft(customToolbar, 900);
                    Canvas.SetTop(customToolbar, 5);
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[TOOLBAR] Error restoring toolbar position: {ex.Message}");
                Canvas.SetLeft(customToolbar, 900);
                Canvas.SetTop(customToolbar, 5);
            }
        }

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public IAsyncResult BeginInvoke(Action method)
        {
            return (IAsyncResult)Dispatcher.BeginInvoke(method);
        }
        #endregion
        #region UI KeyBinds
        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                btnSettingsSearch_Click(sender, e);
                e.Handled = true;
            }
            if (e.Key == Key.Delete)
            {
                LootFilterControl.HandleDeleteKey();
                e.Handled = true;
            }
        }
        #endregion
        private class PanelInfo
        {
            public Border Panel { get; set; }
            public Canvas Canvas { get; set; }
            public string ConfigName { get; set; }
            public int MinWidth { get; set; }
            public int MinHeight { get; set; }

            public PanelInfo(Border panel, Canvas canvas, string configName, int minWidth, int minHeight)
            {
                Panel = panel;
                Canvas = canvas;
                ConfigName = configName;
                MinWidth = minWidth;
                MinHeight = minHeight;
            }
        }

        private class PingEffect
        {
            public Vector3 Position;
            public DateTime StartTime;
            public float DurationSeconds = 2f;
        }
    }
}
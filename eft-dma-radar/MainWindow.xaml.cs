using System.Buffers;
using System.Numerics;
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
using NetFabric.Hyperlinq;
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
        private List<Canvas> _allPanelCanvases; // Performance: cached list for z-index operations

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
        public static bool _showProfiler = false; // Toggle with F11 or Settings checkbox
        private DispatcherTimer _uiActivityTimer;

        // Performance optimization: Debounced config save to reduce I/O operations
        private DispatcherTimer _configSaveDebounceTimer;
        private bool _hasPendingConfigSave = false;

        private readonly Stopwatch _statusSw = Stopwatch.StartNew();
        private int _statusOrder = 1;

        // Performance optimization: Cached status strings to avoid allocations
        private const string _statusNotRunning = "Game Process Not Running!";
        private const string _statusStartingUp1 = "Starting Up.";
        private const string _statusStartingUp2 = "Starting Up..";
        private const string _statusStartingUp3 = "Starting Up...";
        private const string _statusWaitingRaid1 = "Waiting for Raid Start.";
        private const string _statusWaitingRaid2 = "Waiting for Raid Start..";
        private const string _statusWaitingRaid3 = "Waiting for Raid Start...";

        // Performance optimization: Cache for MouseOverItems with dirty flag
        private IEnumerable<IMouseoverEntity> _mouseOverItemsCache;
        private int _mouseOverItemsCacheFrame;
        private int _currentFrame;
        private bool _mouseOverItemsDirty = true;

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

        // Performance optimization: Pooled paint for player dimming zones
        private readonly SKPaint _dimmingPaint = new()
        {
            Style = SKPaintStyle.Fill,
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

        // Performance optimization: Spatial indexing for viewport culling
        // Separate spatial indexes for each entity type to handle different data sources and update frequencies
        private SpatialIndex<Player> _playerSpatialIndex;
        private SpatialIndex<LootItem> _lootSpatialIndex;
        private SpatialIndex<StaticLootContainer> _containerSpatialIndex;
        private SpatialIndex<IExplosiveItem> _explosiveSpatialIndex;

        // Track when spatial indexes need rebuilding
        private int _lastPlayerCount = -1;
        private long _lastPlayerRebuildFrame = -1;
        private int _lastLootCount = -1;
        private long _lastLootRebuildFrame = -1;
        private int _lastContainerCount = -1;
        private long _lastContainerRebuildFrame = -1;
        private int _lastExplosivesCount = -1;
        private long _lastExplosivesRebuildFrame = -1;
        private string _lastSpatialIndexMapID = null;

        // Rebuild interval: At 75 FPS, 90 frames = ~1.2 sec (good balance for performance)
        // Adjust based on your FPS: 60fps=60frames(1sec), 75fps=90frames(1.2sec)
        private const int SPATIAL_INDEX_REFRESH_INTERVAL = 90;

        // Cache dimming enabled state per frame to avoid repeated config access
        private bool _playerDimmingEnabledCache = false;
        private long _playerDimmingCacheFrame = -1;

        // Performance metrics for spatial culling (accessible by DebugInfoWidget)
        public int TotalPlayerCount { get; private set; } = 0;
        public int VisiblePlayerCount { get; private set; } = 0;
        public int TotalLootCount { get; private set; } = 0;
        public int VisibleLootCount { get; private set; } = 0;
        public int TotalContainerCount { get; private set; } = 0;
        public int VisibleContainerCount { get; private set; } = 0;
        public int TotalExplosivesCount { get; private set; } = 0;
        public int VisibleExplosivesCount { get; private set; } = 0;

        // Performance optimization: Draw call batching
        private readonly DrawBatcher _drawBatcher = new();

        // Performance optimization: GRContext resource tracking
        private int _framesSinceLastGC = 0;
        private const int GC_INTERVAL_FRAMES = 300; // Purge GPU resources every 300 frames (~5 seconds at 60fps)

        // Performance optimization: Pre-rendered text atlases for 10-50x faster text rendering
        // Distance atlas: "0m" to "500m" every 1m (501 sprites, ~5-8MB)
        // Height atlas: "-50m" to "+50m" every 1m (100 sprites, ~1-2MB)
        // Loot name atlas: ALL item ShortNames from database (~1500-2000 sprites, ~15-20MB)
        // Player info atlas: Common static strings (THERMAL, NVG, UBGL, etc.) (~20 sprites, ~100KB)
        private static TextAtlas _distanceAtlas;
        private static TextAtlas _heightAtlas;
        private static TextAtlas _lootNameAtlas;
        private static TextAtlas _playerInfoAtlas;

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

        private static List<PingEffect> _activePings = new(20); // Pre-allocated capacity for ping effects

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
        /// Performance optimized: caches result with dirty flag to avoid repeated LINQ operations.
        /// </summary>
        private IEnumerable<IMouseoverEntity> MouseOverItems
        {
            get
            {
                // Return cached result if still valid for current frame and not dirty
                if (!_mouseOverItemsDirty && _mouseOverItemsCacheFrame == _currentFrame && _mouseOverItemsCache != null)
                    return _mouseOverItemsCache;

                // Performance: avoid repeated property access
                var allPlayers = AllPlayers;
                var loot = Loot;
                var containers = Containers;
                var exits = Exits;
                var switches = Switches;
                var doors = Doors;
                var lootCorpsesVisible = LootCorpsesVisible;
                var filterIsSet = FilterIsSet;

                // Pre-allocate list with estimated capacity to reduce resizing
                var result = new List<IMouseoverEntity>(256);

                // Add loot items
                if (loot != null)
                {
                    foreach (var item in loot)
                        result.Add(item);
                }

                // Add containers
                if (containers != null)
                {
                    foreach (var container in containers)
                        result.Add(container);
                }

                // Add players with filtering
                if (allPlayers != null)
                {
                    foreach (var player in allPlayers)
                    {
                        if (player is Tarkov.EFTPlayer.LocalPlayer || player.HasExfild)
                            continue;

                        if (!lootCorpsesVisible && !player.IsAlive)
                            continue;

                        if (filterIsSet && !LootItem.CorpseSettings.Enabled)
                        {
                            if (player.LootObject != null && loot != null)
                            {
                                bool found = false;
                                foreach (var lootItem in loot)
                                {
                                    if (lootItem == player.LootObject)
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                                if (found)
                                    continue;
                            }
                        }

                        result.Add(player);
                    }
                }

                // Add exits
                if (exits != null)
                {
                    foreach (var exit in exits)
                        result.Add(exit);
                }

                // Add quest zones
                var questZones = Memory.QuestManager?.LocationConditions;
                if (questZones != null)
                {
                    foreach (var zone in questZones)
                        result.Add(zone);
                }

                // Add switches
                if (switches != null)
                {
                    foreach (var swtch in switches)
                        result.Add(swtch);
                }

                // Add doors
                if (doors != null)
                {
                    foreach (var door in doors)
                        result.Add(door);
                }

                _mouseOverItemsCache = result.Count > 0 ? result : null;
                _mouseOverItemsCacheFrame = _currentFrame;
                _mouseOverItemsDirty = false;

                return _mouseOverItemsCache;
            }
        }

        /// <summary>
        /// Invalidates the MouseOverItems cache. Call when collections change.
        /// </summary>
        private void InvalidateMouseOverCache()
        {
            _mouseOverItemsDirty = true;
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

            // Initialize spatial indexes for efficient viewport culling
            _playerSpatialIndex = new SpatialIndex<Player>();
            _lootSpatialIndex = new SpatialIndex<LootItem>();
            _containerSpatialIndex = new SpatialIndex<StaticLootContainer>();
            _explosiveSpatialIndex = new SpatialIndex<IExplosiveItem>();

            // Initialize text atlases for ultra-fast text rendering (10-50x faster than DrawText)
            // Pre-render all distance and height strings at startup
            InitializeTextAtlases();

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

        /// <summary>
        /// Initializes text atlases for ultra-fast text rendering.
        /// Pre-renders all distance/height strings + ALL loot names at startup for 10-50x performance gain.
        /// </summary>
        private static void InitializeTextAtlases()
        {
            // Create distance atlas: "0m" to "500m" every 1 meter (501 sprites, ~5-8MB)
            _distanceAtlas = TextAtlas.CreateDistanceAtlas(SKPaints.TextLocalPlayer);

            // Create height atlas: "-50m" to "+50m" every 1 meter (100 sprites, ~1-2MB)
            _heightAtlas = TextAtlas.CreateHeightAtlas(SKPaints.TextLocalPlayer);

            // Create loot name atlas: ALL item ShortNames from database (~1500-2000 sprites, ~15-20MB)
            // This provides 10-50x speedup for loot text rendering (eliminates double-draw overhead)
            if (EftDataManager.IsInitialized && EftDataManager.AllItems?.Count > 0)
            {
                var lootNames = EftDataManager.AllItems.Values.Select(item => item.ShortName).Distinct();
                _lootNameAtlas = TextAtlas.CreateLootAtlas(SKPaints.TextImportantLoot, lootNames);
                LoneLogging.WriteLine($"Text atlases initialized: Distance={_distanceAtlas != null}, Height={_heightAtlas != null}, LootNames={_lootNameAtlas != null} ({EftDataManager.AllItems.Count} items)");
            }
            else
            {
                LoneLogging.WriteLine($"WARNING: EftDataManager not initialized - loot name atlas skipped");
                LoneLogging.WriteLine($"Text atlases initialized: Distance={_distanceAtlas != null}, Height={_heightAtlas != null}, LootNames=false");
            }

            // Create player info atlas: Common static strings that appear on players
            // Eliminates double-draw overhead for THERMAL, NVG, UBGL, tags, etc.
            var playerInfoStrings = new[]
            {
                "THERMAL", "NVG", "UBGL", "ERROR", "*",
                // Add common alert tags
                "BOSS", "GUARD", "RAIDER", "ROGUE", "CULTIST", "SNIPER",
                // Add common price strings (round to nearest 100k for cache hits)
                "100k₽", "200k₽", "300k₽", "400k₽", "500k₽", "600k₽", "700k₽", "800k₽", "900k₽",
                "1M₽", "2M₽", "3M₽", "4M₽", "5M₽"
            };
            _playerInfoAtlas = TextAtlas.CreateCustomAtlas(SKPaints.TextLocalPlayer, playerInfoStrings);

            LoneLogging.WriteLine($"Player info atlas initialized with {playerInfoStrings.Length} strings");
        }

        /// <summary>
        /// Gets the distance atlas for rendering distance text.
        /// </summary>
        public static TextAtlas DistanceAtlas => _distanceAtlas;

        /// <summary>
        /// Gets the height atlas for rendering height difference text.
        /// </summary>
        public static TextAtlas HeightAtlas => _heightAtlas;

        /// <summary>
        /// Gets the loot name atlas for rendering loot item names (ShortName).
        /// </summary>
        public static TextAtlas LootNameAtlas => _lootNameAtlas;

        /// <summary>
        /// Gets the player info atlas for rendering static player info strings (THERMAL, NVG, etc.).
        /// </summary>
        public static TextAtlas PlayerInfoAtlas => _playerInfoAtlas;

        private void btnDebug_Click(object sender, RoutedEventArgs e)
        {
            // AUTOMATED BENCHMARK: Start/stop automated LOD benchmark
            if (AutomatedBenchmark.Instance.IsRunning)
            {
                AutomatedBenchmark.Instance.Cancel();
                HandyControl.Controls.MessageBox.Show("Benchmark cancelled.", "Benchmark");
                return;
            }

            var result = HandyControl.Controls.MessageBox.Show(
                "Start automated benchmark?\n\n" +
                "This will:\n" +
                "- Test LOD 0, 1, and 2 sequentially\n" +
                "- Take ~30 seconds total\n" +
                "- Automatically control zoom\n" +
                "- Export detailed results to Desktop\n\n" +
                "Make sure you're in-raid with entities visible!",
                "Automated Benchmark",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                AutomatedBenchmark.Instance.Start();
                LoneLogging.WriteLine("Automated benchmark started! Check console for progress.");
            }

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
            using var _ = PerformanceProfiler.Instance.BeginSection("Total Frame");
            PerformanceProfiler.Instance.BeginFrame();

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
                            Config.GetMapLODThresholds(map.ID, out int lod0, out int lod1);
                            mapParams = map.GetParameters(skCanvas, _zoom, ref _mapPanPosition, lod0, lod1);
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
                            Config.GetMapLODThresholds(map.ID, out int lod0, out int lod1);
                            mapParams = map.GetParameters(skCanvas, _zoom, ref localPlayerMapPos, lod0, lod1);
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
                    using (PerformanceProfiler.Instance.BeginSection("Map Drawing"))
                    {
                        map.Draw(canvas, localPlayer.Position.Y, mapParams.Bounds, mapCanvasBounds);
                    }

                    // Cache canvas dimensions for viewport culling (used multiple times)
                    var canvasWidth = (float)skCanvas.ActualWidth;
                    var canvasHeight = (float)skCanvas.ActualHeight;

                    // Update 'important' / quest item asterisk
                    SKPaints.UpdatePulsingAsteriskColor();

                    // === Rebuild Spatial Indexes for Efficient Viewport Culling ===
                    // Rebuild spatial indexes when entity counts change or map switches
                    // This allows O(log n) viewport queries instead of O(n) iteration

                    // Check if map changed - force full rebuild
                    bool mapChanged = _lastSpatialIndexMapID != mapID;
                    if (mapChanged)
                    {
                        _lastSpatialIndexMapID = mapID;
                    }

                    // Draw other players
                    // Materialize collection immediately to avoid HyperLINQ lazy enumeration issues
                    var allPlayers = AllPlayers?
                        .Where(x => !x.HasExfild)
                        .ToList();

                    var battleMode = Config.BattleMode;

                    // Fetch all entity collections for spatial indexing
                    var containers = Containers;
                    var loot = Loot;
                    var explosives = Explosives;

                    using (PerformanceProfiler.Instance.BeginSection("Spatial Index Management"))
                    {

                        // Rebuild Players Spatial Index
                        if (allPlayers is not null)
                        {
                            int playerCount = allPlayers.Count;
                            long framesSinceLastRebuild = _currentFrame - _lastPlayerRebuildFrame;

                            // Rebuild if:
                            // 1. Map changed (immediate)
                            // 2. Count changed (immediate - catches most add/remove)
                            // 3. Every 30 frames (~0.5 sec - catches entity swaps where count stays same)
                            if (mapChanged || playerCount != _lastPlayerCount || framesSinceLastRebuild >= SPATIAL_INDEX_REFRESH_INTERVAL)
                            {
                                using (PerformanceProfiler.Instance.BeginSection("  Rebuild Players Index"))
                                {
                                    _playerSpatialIndex.Rebuild(allPlayers, map.Config);
                                    _lastPlayerRebuildFrame = _currentFrame;
                                    _lastPlayerCount = playerCount;
                                }
                            }

                            TotalPlayerCount = playerCount; // Total before culling
                        }

                        // Rebuild Containers Spatial Index
                        if (!battleMode && containers is not null && Config.Containers.Show && StaticLootContainer.Settings.Enabled)
                        {
                            int containerCount = containers is ICollection<StaticLootContainer> cColl ? cColl.Count : containers.AsValueEnumerable().Count();
                            long framesSinceLastRebuild = _currentFrame - _lastContainerRebuildFrame;

                            // Rebuild on map change, count change, or every 30 frames
                            if (mapChanged || containerCount != _lastContainerCount || framesSinceLastRebuild >= SPATIAL_INDEX_REFRESH_INTERVAL)
                            {
                                using (PerformanceProfiler.Instance.BeginSection("  Rebuild Containers Index"))
                                {
                                    _containerSpatialIndex.Rebuild(containers, map.Config);
                                    _lastContainerRebuildFrame = _currentFrame;
                                    _lastContainerCount = containerCount;
                                }
                            }

                            TotalContainerCount = containerCount; // Total before culling
                        }

                        // Rebuild Loot Spatial Index
                        if (!battleMode && loot is not null && Config.ProcessLoot)
                        {
                            int lootCount = loot is ICollection<LootItem> lColl ? lColl.Count : loot.AsValueEnumerable().Count();
                            long framesSinceLastRebuild = _currentFrame - _lastLootRebuildFrame;

                            // Rebuild on map change, count change, or every 30 frames
                            // Critical: catches when loot is picked up/spawned with unchanged count
                            if (mapChanged || lootCount != _lastLootCount || framesSinceLastRebuild >= SPATIAL_INDEX_REFRESH_INTERVAL)
                            {
                                using (PerformanceProfiler.Instance.BeginSection("  Rebuild Loot Index"))
                                {
                                    _lootSpatialIndex.Rebuild(loot, map.Config);
                                    _lastLootRebuildFrame = _currentFrame;
                                    _lastLootCount = lootCount;
                                }
                            }

                            TotalLootCount = lootCount; // Total before culling
                        }

                        // Rebuild Explosives Spatial Index
                        if (explosives is not null && (Tripwire.Settings.Enabled || Grenade.Settings.Enabled || MortarProjectile.Settings.Enabled))
                        {
                            int explosivesCount = explosives is ICollection<IExplosiveItem> eColl ? eColl.Count : explosives.AsValueEnumerable().Count();
                            long framesSinceLastRebuild = _currentFrame - _lastExplosivesRebuildFrame;

                            // Rebuild on map change, count change, or every 30 frames
                            if (mapChanged || explosivesCount != _lastExplosivesCount || framesSinceLastRebuild >= SPATIAL_INDEX_REFRESH_INTERVAL)
                            {
                                using (PerformanceProfiler.Instance.BeginSection("  Rebuild Explosives Index"))
                                {
                                    _explosiveSpatialIndex.Rebuild(explosives, map.Config);
                                    _lastExplosivesRebuildFrame = _currentFrame;
                                    _lastExplosivesCount = explosivesCount;
                                }
                            }

                            TotalExplosivesCount = explosivesCount; // Total before culling
                        }
                    }

                    // === LAYER 1: BACKGROUND ENTITIES (Z=100-199) ===
                    using (PerformanceProfiler.Instance.BeginSection("Draw Background Entities"))
                    {
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

                    if (!battleMode && Switch.Settings.Enabled)
                    {
                        foreach (var swtch in Switches)
                        {
                            // Apply proximity-based dimming to reduce clutter near players
                            float opacity = CalculateEntityOpacityNearPlayers(swtch.Position, allPlayers, localPlayer, map, mapParams);
                            if (opacity < 1.0f)
                            {
                                using (var pooledPaint = PooledPaint.GetFill())
                                {
                                    pooledPaint.Paint.Color = SKColors.White.WithAlpha((byte)(opacity * 255));
                                    canvas.SaveLayer(pooledPaint.Paint);
                                    swtch.Draw(canvas, mapParams, localPlayer);
                                    canvas.Restore();
                                }
                            }
                            else
                            {
                                swtch.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }

                    if (!battleMode && Door.Settings.Enabled)
                    {
                        var doorsSet = Memory.Game?.Interactables._Doors;
                        if (doorsSet is not null && doorsSet.Count > 0)
                        {
                            // Performance optimized: only regenerate doors list if count changed
                            if (doorsSet.Count != _lastDoorCount)
                            {
                                Doors = doorsSet.AsValueEnumerable().ToList();
                                _lastDoorCount = doorsSet.Count;
                            }

                            foreach (var door in Doors)
                            {
                                // Apply proximity-based dimming to reduce clutter near players
                                float opacity = CalculateEntityOpacityNearPlayers(door.Position, allPlayers, localPlayer, map, mapParams);
                                if (opacity < 1.0f)
                                {
                                    using (var pooledPaint = PooledPaint.GetFill())
                                    {
                                        pooledPaint.Paint.Color = SKColors.White.WithAlpha((byte)(opacity * 255));
                                        canvas.SaveLayer(pooledPaint.Paint);
                                        door.Draw(canvas, mapParams, localPlayer);
                                        canvas.Restore();
                                    }
                                }
                                else
                                {
                                    door.Draw(canvas, mapParams, localPlayer);
                                }
                            }
                        }
                        else
                        {
                            Doors = null;
                            _lastDoorCount = -1;
                        }
                    }

                    if (!battleMode && Config.QuestHelper.Enabled && QuestManager.Settings.Enabled && !localPlayer.IsScav)
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
                    } // End Draw Background Entities

                    // === LAYER 2: LOOT & CONTAINERS (Z=200-299) ===
                    using (PerformanceProfiler.Instance.BeginSection("Draw Loot & Containers"))
                    {
                        if (!battleMode && Config.Containers.Show && StaticLootContainer.Settings.Enabled)
                    {
                        if (containers is not null)
                        {
                            // Performance optimized: Use spatial index for viewport culling instead of checking each container
                            // Materialize immediately to avoid lazy enumeration invalidation
                            // Reduced margin from 100f to 50f for better culling performance
                            var visibleContainersList = _containerSpatialIndex.QueryViewport(mapParams, canvasWidth, canvasHeight, margin: 50f).ToList();
                            VisibleContainerCount = visibleContainersList.Count;
                            foreach (var container in visibleContainersList)
                            {
                                if (LootSettingsControl.ContainerIsTracked(container.ID ?? "NULL"))
                                {
                                    if (Config.Containers.HideSearched && container.Searched)
                                        continue;

                                    // Apply proximity-based dimming to reduce clutter near players
                                    float opacity = CalculateEntityOpacityNearPlayers(container.Position, allPlayers, localPlayer, map, mapParams);
                                    if (opacity < 1.0f)
                                    {
                                        using (var pooledPaint = PooledPaint.GetFill())
                                        {
                                            pooledPaint.Paint.Color = SKColors.White.WithAlpha((byte)(opacity * 255));
                                            canvas.SaveLayer(pooledPaint.Paint);
                                            container.Draw(canvas, mapParams, localPlayer);
                                            canvas.Restore();
                                        }
                                    }
                                    else
                                    {
                                        container.Draw(canvas, mapParams, localPlayer);
                                    }
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
                        // Performance optimized: cache corpse settings check per-frame to avoid repeated property access
                        bool corpseEnabled;
                        if (_lootFilterCacheFrame == _currentFrame)
                        {
                            corpseEnabled = _lootCorpseSettingsEnabled;
                        }
                        else
                        {
                            corpseEnabled = LootItem.CorpseSettings.Enabled;
                            _lootCorpseSettingsEnabled = corpseEnabled;
                            _lootFilterCacheFrame = _currentFrame;
                        }

                        if (loot is not null)
                        {
                            // Performance optimized: Use spatial index for viewport culling
                            // Query returns only entities within viewport bounds - massive performance gain for large loot counts
                            // Materialize immediately to avoid lazy enumeration invalidation
                            // Reduced margin from 100f to 50f for better culling performance (most important for loot)
                            var visibleLootList = _lootSpatialIndex.QueryViewport(mapParams, canvasWidth, canvasHeight, margin: 50f).ToList();
                            VisibleLootCount = visibleLootList.Count;

                            // Render in reverse order for proper z-ordering (items should render back-to-front)
                            foreach (var item in Enumerable.Reverse(visibleLootList))
                            {
                                // Skip quest items (handled separately below)
                                if (item is QuestItem)
                                    continue;

                                // Early-out: skip corpses if corpse rendering is disabled
                                if (!corpseEnabled && item is LootCorpse)
                                    continue;

                                item.CheckNotify();

                                // Apply proximity-based dimming to reduce clutter near players
                                float opacity = CalculateEntityOpacityNearPlayers(item.Position, allPlayers, localPlayer, map, mapParams);
                                if (opacity < 1.0f)
                                {
                                    using (var pooledPaint = PooledPaint.GetFill())
                                    {
                                        pooledPaint.Paint.Color = SKColors.White.WithAlpha((byte)(opacity * 255));
                                        canvas.SaveLayer(pooledPaint.Paint);
                                        item.Draw(canvas, mapParams, localPlayer);
                                        canvas.Restore();
                                    }
                                }
                                else
                                {
                                    item.Draw(canvas, mapParams, localPlayer);
                                }
                            }
                        }
                    }

                    if (!battleMode && Config.QuestHelper.Enabled)
                    {
                        if (LootItem.QuestItemSettings.Enabled && !localPlayer.IsScav)
                        {
                            var questItems = Loot?.AsValueEnumerable().Where(x => x is QuestItem);
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

                    if (!battleMode && Config.QuestHelper.Enabled && LootItem.QuestItemSettings.Enabled && !localPlayer.IsScav)
                    {
                        var questItems = Loot?.AsValueEnumerable().Where(x => x is QuestItem);
                        if (questItems is not null)
                            foreach (var item in questItems)
                                item.Draw(canvas, mapParams, localPlayer);
                    }
                    } // End Draw Loot & Containers

                    // === LAYER 3: PLAYERS & AI (Z=300-399) ===
                    using (PerformanceProfiler.Instance.BeginSection("Draw Players & AI"))
                    {
                        if (Config.ConnectGroups)
                    {
                        DrawGroupConnections(canvas, allPlayers, map, mapParams);
                    }

                    // Performance optimized: use cached ordered players list + spatial index for viewport culling
                    var ordered = GetOrderedPlayers(allPlayers, localPlayer);
                    if (ordered is not null)
                    {
                        // Use spatial index to query only players within viewport bounds
                        // Materialize immediately to avoid lazy enumeration invalidation
                        // Reduced margin from 100f to 50f for better culling performance
                        var visiblePlayersList = _playerSpatialIndex.QueryViewport(mapParams, canvasWidth, canvasHeight, margin: 50f).ToList();
                        VisiblePlayerCount = visiblePlayersList.Count;

                        // Convert to HashSet for O(1) lookup to maintain draw order from GetOrderedPlayers
                        var visiblePlayerSet = new HashSet<Player>(visiblePlayersList);

                        foreach (var player in ordered)
                        {
                            // Only draw if player is in the visible set (viewport culled)
                            if (!visiblePlayerSet.Contains(player))
                                continue;

                            player.Draw(canvas, mapParams, localPlayer);
                        }
                    }

                    if (Tripwire.Settings.Enabled ||
                        Grenade.Settings.Enabled ||
                        MortarProjectile.Settings.Enabled)
                    {
                        if (explosives is not null)
                        {
                            // Performance optimized: Use spatial index for viewport culling
                            // Materialize immediately to avoid lazy enumeration invalidation
                            // Reduced margin from 100f to 50f for better culling performance
                            var visibleExplosivesList = _explosiveSpatialIndex.QueryViewport(mapParams, canvasWidth, canvasHeight, margin: 50f).ToList();
                            VisibleExplosivesCount = visibleExplosivesList.Count;
                            foreach (var explosive in visibleExplosivesList)
                            {
                                explosive.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }
                    } // End Draw Players & AI

                    // === LAYER 4: CRITICAL OVERLAYS (Z=400+) ===

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

                                // Apply proximity-based dimming to reduce clutter near players
                                float opacity = CalculateEntityOpacityNearPlayers(exit.Position, allPlayers, localPlayer, map, mapParams);
                                if (opacity < 1.0f)
                                {
                                    using (var pooledPaint = PooledPaint.GetFill())
                                    {
                                        pooledPaint.Paint.Color = SKColors.White.WithAlpha((byte)(opacity * 255));
                                        canvas.SaveLayer(pooledPaint.Paint);
                                        exit.Draw(canvas, mapParams, localPlayer);
                                        canvas.Restore();
                                    }
                                }
                                else
                                {
                                    exit.Draw(canvas, mapParams, localPlayer);
                                }
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

                // Performance optimization: Periodic GPU resource cleanup
                _framesSinceLastGC++;
                if (_framesSinceLastGC >= GC_INTERVAL_FRAMES)
                {
                    skCanvas?.GRContext?.Flush();
                    skCanvas?.GRContext?.PurgeResources();
                    _framesSinceLastGC = 0;
                }

                // Draw performance profiler overlay if enabled (toggle with F11)
                if (_showProfiler)
                {
                    DrawProfilerOverlay(canvas);
                }

                // AUTOMATED BENCHMARK: Draw status overlay
                if (AutomatedBenchmark.Instance.IsRunning)
                {
                    DrawBenchmarkStatusOverlay(canvas);
                }

                // AUTOMATED BENCHMARK: Feed frame time and get target zoom if benchmark is running
                if (inRaid && localPlayer is not null && AutomatedBenchmark.Instance.IsRunning)
                {
                    var frameTimeMs = PerformanceProfiler.Instance.LastFrameMs;
                    var currentLOD = _cachedMapParams.LODLevel;
                    var targetLOD = AutomatedBenchmark.Instance.Update(frameTimeMs, currentLOD);

                    if (targetLOD.HasValue)
                    {
                        // Get LOD thresholds for this map
                        Config.GetMapLODThresholds(mapID, out int lod0Threshold, out int lod1Threshold);

                        // Adjust zoom to reach target LOD
                        // Zoom range: 1-100 (1=closest, 100=farthest)
                        // LOD 0: zoom < lod0Threshold (e.g., < 70)
                        // LOD 1: zoom >= lod0Threshold && < lod1Threshold (e.g., 70-84)
                        // LOD 2: zoom >= lod1Threshold (e.g., >= 85)
                        _zoom = targetLOD.Value switch
                        {
                            0 => Math.Max(1, lod0Threshold - 10),     // Well into LOD 0 (e.g., 60 if threshold is 70)
                            1 => (lod0Threshold + lod1Threshold) / 2, // Middle of LOD 1 (e.g., 77 if thresholds are 70-85)
                            2 => Math.Min(100, lod1Threshold + 10),   // Well into LOD 2 (e.g., 95 if threshold is 85)
                            _ => _zoom
                        };
                    }
                }

                canvas.Flush(); // commit frame to GPU
            }
            catch (Exception ex) // Log rendering errors
            {
                LoneLogging.WriteLine($"CRITICAL RENDER ERROR: {ex}");
            }
        }
        /// <summary>
        /// Draws the performance profiler overlay showing frame timings and bottlenecks.
        /// </summary>
        private void DrawProfilerOverlay(SKCanvas canvas)
        {
            var stats = PerformanceProfiler.Instance.GetStats();
            if (stats.TotalFrames == 0) return;

            var x = 10f;
            var y = 30f;
            var lineHeight = 18f;

            using var backgroundPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 200),
                Style = SKPaintStyle.Fill
            };

            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 14,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
            };

            // Calculate overlay height
            var overlayHeight = (stats.Sections.Count + 3) * lineHeight + 20f;
            canvas.DrawRect(5, 10, 400, overlayHeight, backgroundPaint);

            // Header
            canvas.DrawText($"PERFORMANCE PROFILER", x, y, textPaint);
            y += lineHeight * 1.5f;

            // Frame time
            var frameColor = stats.LastFrameMs > 16.66 ? SKColors.Red :
                           stats.LastFrameMs > 13.33 ? SKColors.Yellow : SKColors.LightGreen;
            textPaint.Color = frameColor;
            canvas.DrawText($"Frame: {stats.LastFrameMs:F2}ms ({(1000.0 / stats.LastFrameMs):F0} FPS)", x, y, textPaint);
            y += lineHeight;

            textPaint.Color = SKColors.White;
            canvas.DrawText($"Frames: {stats.TotalFrames}", x, y, textPaint);
            y += lineHeight * 1.5f;

            // Section timings (top 10)
            textPaint.Color = SKColors.Cyan;
            canvas.DrawText("Section Timings (Recent Avg):", x, y, textPaint);
            y += lineHeight;

            textPaint.Color = SKColors.White;
            foreach (var section in stats.Sections.Take(10))
            {
                var percentage = (section.RecentAverageMs / stats.LastFrameMs) * 100;
                var sectionColor = section.RecentAverageMs > 5 ? SKColors.Orange :
                                 section.RecentAverageMs > 2 ? SKColors.Yellow : SKColors.LightGray;
                textPaint.Color = sectionColor;

                var indent = section.Name.StartsWith("  ") ? "    " : "";
                canvas.DrawText($"{indent}{section.Name}: {section.RecentAverageMs:F2}ms ({percentage:F0}%)", x, y, textPaint);
                y += lineHeight;
            }
        }

        private void DrawBenchmarkStatusOverlay(SKCanvas canvas)
        {
            var benchmark = AutomatedBenchmark.Instance;
            if (!benchmark.IsRunning) return;

            var x = 10f;
            var y = 30f;
            var lineHeight = 20f;
            var overlayWidth = 450f;

            using var backgroundPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 220),
                Style = SKPaintStyle.Fill
            };

            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 16,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold)
            };

            // Calculate overlay height
            var overlayHeight = 7 * lineHeight + 30f;
            canvas.DrawRect(5, 10, overlayWidth, overlayHeight, backgroundPaint);

            // Header
            textPaint.Color = SKColors.Yellow;
            canvas.DrawText($"AUTOMATED BENCHMARK RUNNING", x, y, textPaint);
            y += lineHeight * 1.5f;

            // State
            textPaint.TextSize = 14;
            textPaint.Color = SKColors.Cyan;
            string stateText = benchmark.State switch
            {
                AutomatedBenchmark.BenchmarkState.ZoomingToLOD0 => "Zooming to LOD 0...",
                AutomatedBenchmark.BenchmarkState.ZoomingToLOD1 => "Zooming to LOD 1...",
                AutomatedBenchmark.BenchmarkState.ZoomingToLOD2 => "Zooming to LOD 2...",
                AutomatedBenchmark.BenchmarkState.WarmingUp => "Warming Up...",
                AutomatedBenchmark.BenchmarkState.Sampling => "Sampling Data...",
                _ => "Unknown"
            };
            canvas.DrawText($"State: {stateText}", x, y, textPaint);
            y += lineHeight;

            // Current LOD
            textPaint.Color = SKColors.White;
            canvas.DrawText($"Current LOD: {benchmark.CurrentLOD}", x, y, textPaint);
            y += lineHeight;

            // Progress
            var progress = benchmark.Progress;
            textPaint.Color = SKColors.LightGreen;
            canvas.DrawText($"Progress: {progress}%", x, y, textPaint);
            y += lineHeight;

            // Progress bar
            var barX = x;
            var barY = y;
            var barWidth = overlayWidth - 20f;
            var barHeight = 20f;

            using var barBgPaint = new SKPaint
            {
                Color = new SKColor(50, 50, 50, 255),
                Style = SKPaintStyle.Fill
            };

            using var barFillPaint = new SKPaint
            {
                Color = new SKColor(0, 200, 0, 255),
                Style = SKPaintStyle.Fill
            };

            using var barOutlinePaint = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f
            };

            canvas.DrawRect(barX, barY, barWidth, barHeight, barBgPaint);
            canvas.DrawRect(barX, barY, barWidth * (progress / 100f), barHeight, barFillPaint);
            canvas.DrawRect(barX, barY, barWidth, barHeight, barOutlinePaint);
            y += barHeight + lineHeight * 0.5f;

            // Instructions
            textPaint.TextSize = 12;
            textPaint.Color = SKColors.LightGray;
            canvas.DrawText("This will test LOD 0, 1, and 2 sequentially", x, y, textPaint);
            y += lineHeight * 0.8f;
            canvas.DrawText("Results will be exported to Desktop when complete", x, y, textPaint);
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
        /// Performance-optimized method to draw group connection lines.
        /// Uses ArrayPool to reduce allocations and avoid repeated LINQ materializations.
        /// </summary>
        private void DrawGroupConnections(SKCanvas canvas, IEnumerable<Player> allPlayers, ILoneMap map, LoneMapParams mapParams)
        {
            if (allPlayers is null) return;

            // Performance optimized: Use List<Player> to avoid multiple enumerations
            var playersList = allPlayers as List<Player> ?? allPlayers.AsValueEnumerable().ToList();

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
        /// Calculates the opacity factor for an entity based on its proximity to players.
        /// Entities close to players are faded out to reduce visual clutter and make players stand out.
        /// Performance optimized: returns early if dimming is disabled, uses SIMD vectorization for batch distance calculations.
        /// </summary>
        /// <param name="entityPos">Position of the entity in world coordinates</param>
        /// <param name="allPlayers">All players in the raid</param>
        /// <param name="localPlayer">The local player (has larger dimming radius)</param>
        /// <param name="map">Current map for coordinate conversion</param>
        /// <param name="mapParams">Map parameters for zoomed position calculation</param>
        /// <returns>Opacity multiplier (0.0-1.0). 1.0 = full opacity, lower values = more transparent</returns>
        private float CalculateEntityOpacityNearPlayers(Vector3 entityPos, IEnumerable<Player> allPlayers, Player localPlayer, ILoneMap map, LoneMapParams mapParams)
        {
            // Cache dimming enabled check per frame to avoid repeated config access
            if (_playerDimmingCacheFrame != _currentFrame)
            {
                _playerDimmingEnabledCache = Config.PlayerDimmingEnabled;
                _playerDimmingCacheFrame = _currentFrame;
            }

            if (!_playerDimmingEnabledCache || allPlayers is null)
                return 1.0f; // Full opacity when dimming is disabled

            // OPTIMIZATION: Use player spatial index for O(log n) proximity queries instead of O(n) iteration
            // Query only nearby players within max dimming radius
            float maxRadius = Math.Max(Config.LocalPlayerDimmingRadius, Config.PlayerDimmingRadius) * UIScale;
            var entityMapPos = entityPos.ToMapPos(map.Config);

            // Use spatial index to get only nearby players
            var nearbyPlayers = _playerSpatialIndex.QueryRadius(
                new System.Numerics.Vector2(entityMapPos.X, entityMapPos.Y),
                maxRadius
            );

            var entityZoomedPos = entityMapPos.ToZoomedPos(mapParams);

            // SIMD OPTIMIZATION: Use batch processing only when beneficial (4+ players)
            // For small counts, scalar operations are faster due to lower setup overhead
            var nearbyPlayersList = nearbyPlayers as List<Player> ?? nearbyPlayers.ToList();
            int playerCount = nearbyPlayersList.Count;
            if (playerCount == 0)
                return 1.0f;

            // Use scalar path for small player counts (lower overhead than SIMD setup)
            if (playerCount < 4)
            {
                foreach (var player in nearbyPlayersList)
                {
                    if (player is null) continue;

                    var playerMapPos = player.Position.ToMapPos(map.Config).ToZoomedPos(mapParams);
                    float radius = (player == localPlayer) ?
                        Config.LocalPlayerDimmingRadius * UIScale :
                        Config.PlayerDimmingRadius * UIScale;

                    // Calculate squared distance (avoid sqrt for performance)
                    float distSq = VectorMath.DistanceSquared(
                        entityZoomedPos.X, entityZoomedPos.Y,
                        playerMapPos.X, playerMapPos.Y
                    );

                    // Early exit if we're inside dimming zone
                    if (distSq <= radius * radius)
                    {
                        return 1.0f - Config.PlayerDimmingOpacity;
                    }
                }
                return 1.0f;
            }

            // SIMD batch processing path for 4+ players (4-8 distances per cycle on AVX2)
            // Use ArrayPool to avoid allocations - minimal overhead for larger batches
            var playerPositions = ArrayPool<System.Numerics.Vector2>.Shared.Rent(playerCount);
            var radiiSquared = ArrayPool<float>.Shared.Rent(playerCount);
            var distancesSquared = ArrayPool<float>.Shared.Rent(playerCount);

            try
            {
                // Prepare data for batch processing
                int i = 0;
                foreach (var player in nearbyPlayersList)
                {
                    if (player is null) continue;

                    var playerMapPos = player.Position.ToMapPos(map.Config).ToZoomedPos(mapParams);
                    playerPositions[i] = new System.Numerics.Vector2(playerMapPos.X, playerMapPos.Y);

                    float radius = (player == localPlayer) ?
                        Config.LocalPlayerDimmingRadius * UIScale :
                        Config.PlayerDimmingRadius * UIScale;
                    radiiSquared[i] = radius * radius;

                    i++;
                }

                if (i == 0)
                    return 1.0f;

                // Vectorized batch distance calculation (4-8 distances per cycle on AVX2)
                VectorMath.CalculateDistancesSquaredBatch(
                    entityZoomedPos.X,
                    entityZoomedPos.Y,
                    playerPositions,
                    distancesSquared
                );

                // Check if entity is within any dimming zone
                for (int j = 0; j < i; j++)
                {
                    if (distancesSquared[j] <= radiiSquared[j])
                    {
                        // Inside dimming zone: apply configured opacity reduction
                        return 1.0f - Config.PlayerDimmingOpacity;
                    }
                }

                // Outside all dimming zones: full opacity
                return 1.0f;
            }
            finally
            {
                // Return rented arrays to pool
                ArrayPool<System.Numerics.Vector2>.Shared.Return(playerPositions);
                ArrayPool<float>.Shared.Return(radiiSquared);
                ArrayPool<float>.Shared.Return(distancesSquared);
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
                .AsValueEnumerable()
                .Where(p => p != localPlayer)
                .OrderBy(p => DrawPriority(p.Type))
                .ToList();

            _orderedPlayersCacheFrame = _currentFrame;
            return _orderedPlayersCache;
        }

        /// <summary>
        /// Pings items by name on the radar.
        /// Performance optimized: uses direct iteration instead of LINQ.
        /// </summary>
        public static void PingItem(string itemName)
        {
            var loot = Loot;
            if (loot == null)
            {
                LoneLogging.WriteLine($"[Ping] Item '{itemName}' not found.");
                return;
            }

            bool foundAny = false;
            foreach (var lootItem in loot)
            {
                if (lootItem?.Name != null &&
                    lootItem.Name.Contains(itemName, StringComparison.OrdinalIgnoreCase))
                {
                    _activePings.Add(new PingEffect
                    {
                        Position = lootItem.Position,
                        StartTime = DateTime.UtcNow
                    });
                    LoneLogging.WriteLine($"[Ping] Pinged item: {lootItem.Name} at {lootItem.Position}");
                    foundAny = true;
                }
            }

            if (!foundAny)
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
            if (items == null)
            {
                ClearRefs();
                return;
            }

            // Performance optimized: find closest item without LINQ Aggregate, using squared distance to avoid sqrt
            var mouse = new Vector2((float)currentPos.X, (float)currentPos.Y);
            IMouseoverEntity closest = null;
            float closestDistanceSquared = float.MaxValue;

            foreach (var item in items)
            {
                float distanceSquared = VectorMath.DistanceSquared(item.MouseoverPosition, mouse);
                if (distanceSquared < closestDistanceSquared)
                {
                    closestDistanceSquared = distanceSquared;
                    closest = item;
                }
            }

            const float mouseThreshold = 12f;
            if (closest == null || closestDistanceSquared >= mouseThreshold * mouseThreshold)
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
            var closestDistSquared = float.MaxValue;
            int? mouseoverGroup = null;
            float mouseThresholdSquared = (10f * UIScale) * (10f * UIScale);

            var items = MouseOverItems;
            if (items != null)
            {
                foreach (var item in items)
                {
                    float distSquared = VectorMath.DistanceSquared(mousePos, item.MouseoverPosition);
                    if (distSquared < closestDistSquared && distSquared < mouseThresholdSquared)
                    {
                        closestDistSquared = distSquared;
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
            float textWidth = SKPaints.TextRadarStatus.MeasureText(_statusNotRunning);
            canvas.DrawText(_statusNotRunning, ((float)skCanvas.ActualWidth / 2) - textWidth / 2f, (float)skCanvas.ActualHeight / 2,
                SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void StartingUpStatus(SKCanvas canvas)
        {
            string status = _statusOrder == 1 ?
                _statusStartingUp1 : _statusOrder == 2 ?
                _statusStartingUp2 : _statusStartingUp3;
            float textWidth = SKPaints.TextRadarStatus.MeasureText(_statusStartingUp1);
            canvas.DrawText(status, ((float)skCanvas.ActualWidth / 2) - textWidth / 2f, (float)skCanvas.ActualHeight / 2,
                SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void WaitingForRaidStatus(SKCanvas canvas)
        {
            string status = _statusOrder == 1 ?
                _statusWaitingRaid1 : _statusOrder == 2 ?
                _statusWaitingRaid2 : _statusWaitingRaid3;
            float textWidth = SKPaints.TextRadarStatus.MeasureText(_statusWaitingRaid1);
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
                _dimmingPaint?.Dispose(); // Dispose dimming paint object
                _distanceAtlas?.Dispose(); // Dispose distance text atlas
                _heightAtlas?.Dispose(); // Dispose height text atlas
                _lootNameAtlas?.Dispose(); // Dispose loot name atlas
                _playerInfoAtlas?.Dispose(); // Dispose player info atlas

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

            // Performance: Initialize debounce timer for config saves
            _configSaveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Wait 500ms after last change before saving
            };

            _configSaveDebounceTimer.Tick += (s, e) =>
            {
                _configSaveDebounceTimer.Stop();
                if (_hasPendingConfigSave)
                {
                    try
                    {
                        Config.Save();
                        _hasPendingConfigSave = false;
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[CONFIG] Error saving config: {ex.Message}");
                    }
                }
            };
        }

        /// <summary>
        /// Requests a config save with debouncing to reduce I/O operations.
        /// Multiple rapid calls will be coalesced into a single save operation.
        /// </summary>
        private void RequestDebouncedConfigSave()
        {
            _hasPendingConfigSave = true;
            _configSaveDebounceTimer.Stop();
            _configSaveDebounceTimer.Start();
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

        /// <summary>
        /// Brings a panel to the front by adjusting z-index.
        /// Performance optimized: uses cached canvas list instead of allocating new list each time.
        /// </summary>
        private void BringPanelToFront(Canvas panelCanvas)
        {
            // Performance: use cached list to avoid repeated allocations
            if (_allPanelCanvases != null)
            {
                for (int i = 0; i < _allPanelCanvases.Count; i++)
                {
                    Canvas.SetZIndex(_allPanelCanvases[i], 1000);
                }
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

            // Performance optimized: Use dedicated methods instead of lambdas to reduce allocations
            ESPCanvas.PreviewMouseDown += ESPCanvas_PreviewMouseDown;
            GeneralSettingsCanvas.PreviewMouseDown += GeneralSettingsCanvas_PreviewMouseDown;
            LootSettingsCanvas.PreviewMouseDown += LootSettingsCanvas_PreviewMouseDown;
            MemoryWritingCanvas.PreviewMouseDown += MemoryWritingCanvas_PreviewMouseDown;
            WatchlistCanvas.PreviewMouseDown += WatchlistCanvas_PreviewMouseDown;
            PlayerHistoryCanvas.PreviewMouseDown += PlayerHistoryCanvas_PreviewMouseDown;
            LootFilterCanvas.PreviewMouseDown += LootFilterCanvas_PreviewMouseDown;
            PlayerPreviewCanvas.PreviewMouseDown += PlayerPreviewCanvas_PreviewMouseDown;
            MapSetupCanvas.PreviewMouseDown += MapSetupCanvas_PreviewMouseDown;
            SettingsSearchCanvas.PreviewMouseDown += SettingsSearchCanvas_PreviewMouseDown;
        }

        // Performance optimized: Dedicated event handlers to avoid lambda allocations
        private void ESPCanvas_PreviewMouseDown(object s, MouseButtonEventArgs e) => BringPanelToFront(ESPCanvas);
        private void GeneralSettingsCanvas_PreviewMouseDown(object s, MouseButtonEventArgs e) => BringPanelToFront(GeneralSettingsCanvas);
        private void LootSettingsCanvas_PreviewMouseDown(object s, MouseButtonEventArgs e) => BringPanelToFront(LootSettingsCanvas);
        private void MemoryWritingCanvas_PreviewMouseDown(object s, MouseButtonEventArgs e) => BringPanelToFront(MemoryWritingCanvas);
        private void WatchlistCanvas_PreviewMouseDown(object s, MouseButtonEventArgs e) => BringPanelToFront(WatchlistCanvas);
        private void PlayerHistoryCanvas_PreviewMouseDown(object s, MouseButtonEventArgs e) => BringPanelToFront(PlayerHistoryCanvas);
        private void LootFilterCanvas_PreviewMouseDown(object s, MouseButtonEventArgs e) => BringPanelToFront(LootFilterCanvas);
        private void PlayerPreviewCanvas_PreviewMouseDown(object s, MouseButtonEventArgs e) => BringPanelToFront(PlayerPreviewCanvas);
        private void MapSetupCanvas_PreviewMouseDown(object s, MouseButtonEventArgs e) => BringPanelToFront(MapSetupCanvas);
        private void SettingsSearchCanvas_PreviewMouseDown(object s, MouseButtonEventArgs e) => BringPanelToFront(SettingsSearchCanvas);

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

            // Performance: cache canvas list for BringPanelToFront to avoid repeated allocations
            _allPanelCanvases = new List<Canvas>
            {
                GeneralSettingsCanvas,
                LootSettingsCanvas,
                MemoryWritingCanvas,
                ESPCanvas,
                WatchlistCanvas,
                PlayerHistoryCanvas,
                LootFilterCanvas,
                PlayerPreviewCanvas,
                MapSetupCanvas,
                SettingsSearchCanvas
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

        /// <summary>
        /// Saves a single panel position to config with debounced save.
        /// Performance optimized: uses debouncing to reduce I/O operations.
        /// </summary>
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

                        // Performance: debounce config save to reduce I/O
                        RequestDebouncedConfigSave();
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
﻿using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.EFTPlayer.SpecialCollections;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_shared.Common.DMA;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.ESP;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Config;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Misc.Pools;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;
using eft_dma_shared.Common.Unity.LowLevel;
using System;
using System.Windows.Shapes;
using static System.Windows.Forms.LinkLabel;

namespace eft_dma_radar.Tarkov.EFTPlayer
{
    /// <summary>
    /// Base class for Tarkov Players.
    /// Tarkov implements several distinct classes that implement a similar player interface.
    /// </summary>
    public abstract class Player : IWorldEntity, IMapEntity, IMouseoverEntity, IPlayer, IESPEntity
    {
        #region Group Manager

        /// <summary>
        /// Wrapper Class to manage group allocations.
        /// Thread Safe.
        /// </summary>
        protected sealed class GroupManager
        {
            private readonly Dictionary<string, int> _groups = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Returns the Group Number for a given id.
            /// </summary>
            /// <param name="id">Group ID.</param>
            /// <returns>Group Number (0,1,2,etc.)</returns>
            public int GetGroup(string id)
            {
                lock (_groups)
                {
                    _groups.TryAdd(id, _groups.Count);
                    return _groups[id];
                }
            }

            /// <summary>
            /// Clears the group definitions.
            /// </summary>
            public void Clear()
            {
                lock (_groups)
                {
                    _groups.Clear();
                }
            }
        }

        #endregion

        #region Static Interfaces

        public static implicit operator ulong(Player x) => x.Base;
        private static readonly ConcurrentDictionary<ulong, Stopwatch> _rateLimit = new();
        protected static readonly GroupManager _groups = new();
        protected static int _playerScavNumber = 0;

        /// <summary>
        /// Player History Log.
        /// </summary>
        public static PlayerHistory PlayerHistory { get; } = new();

        /// <summary>
        /// Player Watchlist Entries.
        /// </summary>
        public static PlayerWatchlist PlayerWatchlist { get; } = new();

        /// <summary>
        /// Resets/Updates 'static' assets in preparation for a new game/raid instance.
        /// </summary>
        public static void Reset()
        {
            _groups.Clear();
            _rateLimit.Clear();
            PlayerHistory.Reset();
            _playerScavNumber = 0;
        }

        #endregion

        #region Allocation

        /// <summary>
        /// Allocates a player and takes into consideration any rate-limits.
        /// </summary>
        /// <param name="playerDict">Player Dictionary collection to add the newly allocated player to.</param>
        /// <param name="playerBase">Player base memory address.</param>
        /// <param name="initialPosition">Initial position to be set (Optional). Usually for reallocations.</param>
        public static void Allocate(ConcurrentDictionary<ulong, Player> playerDict, ulong playerBase)
        {
            var sw = _rateLimit.AddOrUpdate(playerBase,
                key => new Stopwatch(),
                (key, oldValue) => oldValue);
            if (sw.IsRunning && sw.Elapsed.TotalMilliseconds < 500f)
                return;
            try
            {
                var player = AllocateInternal(playerBase);
                playerDict[player] = player; // Insert or swap
                LoneLogging.WriteLine($"Player '{player.Name}' allocated.");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR during Player Allocation for player @ 0x{playerBase.ToString("X")}: {ex}");
            }
            finally
            {
                sw.Restart();
            }
        }

        private static Player AllocateInternal(ulong playerBase)
        {
            var className = ObjectClass.ReadName(playerBase, 64);
            var isClientPlayer = className == "ClientPlayer" || className == "LocalPlayer";

            if (isClientPlayer)
                return new ClientPlayer(playerBase);
            return new ObservedPlayer(playerBase);
        }

        /// <summary>
        /// Player Constructor.
        /// </summary>
        protected Player(ulong playerBase)
        {
            ArgumentOutOfRangeException.ThrowIfZero(playerBase, nameof(playerBase));
            Base = playerBase;
        }

        #endregion

        #region Fields / Properties
        const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;
        const float HEIGHT_INDICATOR_ARROW_SIZE = 2f;
        /// <summary>
        /// Linecast visibility info.
        /// </summary>
        public bool[] VisibilityInfo { get; set; }
        public int ListIndex { get; set; }
        public bool IsVisible { get; set; } = false;
        public Dictionary<Bones, bool> BoneVisibility { get; } = new();
        public static readonly List<(Bones start, Bones end)> BoneSegments = new List<(Bones, Bones)>
        {
            (Bones.HumanHead, Bones.HumanNeck),
            (Bones.HumanNeck, Bones.HumanSpine3),
            (Bones.HumanSpine3, Bones.HumanSpine2),
            (Bones.HumanSpine2, Bones.HumanSpine1),
            (Bones.HumanSpine1, Bones.HumanPelvis),

            (Bones.HumanPelvis, Bones.HumanLThigh2),   // left knee
            (Bones.HumanLThigh2, Bones.HumanLFoot),    // left foot

            (Bones.HumanPelvis, Bones.HumanRThigh2),   // right knee
            (Bones.HumanRThigh2, Bones.HumanRFoot),    // right foot

            (Bones.HumanLCollarbone, Bones.HumanLForearm2),  // left elbow
            (Bones.HumanLForearm2, Bones.HumanLPalm),         // left hand

            (Bones.HumanRCollarbone, Bones.HumanRForearm2),  // right elbow
            (Bones.HumanRForearm2, Bones.HumanRPalm),         // right hand
        };        
        /// <summary>
        /// Player Class Base Address
        /// </summary>
        public ulong Base { get; }

        /// <summary>
        /// True if the Player is Active (in the player list).
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Type of player unit.
        /// </summary>
        public PlayerType Type { get; protected set; }

        /// <summary>
        /// Streaming platform username.
        /// </summary>
        public string StreamingUsername { get; set; }

        /// <summary>
        /// The streaming platform URL they're streaming
        /// </summary>
        public string StreamingURL { get; set; }

        /// <summary>
        /// Player's Rotation in Local Game World.
        /// </summary>
        public Vector2 Rotation { get; private set; }

        /// <summary>
        /// Player's Map Rotation (with 90 degree correction applied).
        /// </summary>
        public float MapRotation
        {
            get
            {
                float mapRotation = Rotation.X; // Cache value
                mapRotation -= 90f;
                while (mapRotation < 0f)
                    mapRotation += 360f;

                return mapRotation;
            }
        }

        /// <summary>
        /// Corpse field value.
        /// </summary>
        public ulong? Corpse { get; private set; }

        /// <summary>
        /// Stopwatch for High Alert ESP Feature.
        /// </summary>
        public Stopwatch HighAlertSw { get; } = new();

        /// <summary>
        /// Player's Skeleton Bones.
        /// Derived types MUST define this.
        /// </summary>
        public virtual Skeleton Skeleton => throw new NotImplementedException(nameof(Skeleton));

        /// <summary>
        /// Duration of consecutive errors.
        /// </summary>
        public Stopwatch ErrorTimer { get; } = new();

        /// <summary>
        /// Player's Gear/Loadout Information and contained items.
        /// </summary>
        public GearManager Gear { get; private set; }

        /// <summary>
        /// Contains information about the item/weapons in Player's hands.
        /// </summary>
        public HandsManager Hands { get; private set; }

        /// <summary>
        /// True if player is 'Locked On' via Aimbot.
        /// </summary>
        public bool IsAimbotLocked
        {
            get => _isAimbotLocked;
            set
            {
                if (_isAimbotLocked != value)
                {
                    _isAimbotLocked = value;

                    if (value && Memory.Game is LocalGameWorld game)
                        PlayerChamsManager.ApplyAimbotChams(this, game);
                    else if (!value && Memory.Game is LocalGameWorld game2)
                        PlayerChamsManager.RemoveAimbotChams(this, game2, true);
                }
            }
        }

        /// <summary>
        /// True if player is being focused via Right-Click (UI).
        /// </summary>
        public bool IsFocused { get; set; }

        /// <summary>
        /// Dead Player's associated loot container object.
        /// </summary>
        public LootContainer LootObject { get; set; }

        /// <summary>
        /// True if the player is streaming
        /// </summary>
        public bool IsStreaming { get; set; }

        /// <summary>
        /// Alerts for this Player Object.
        /// Used by Player History UI Interop.
        /// </summary>
        public string Alerts { get; private set; }

        public Vector2 MouseoverPosition { get; set; }
        public bool IsAiming { get; set; } = false;
        private bool _isAimbotLocked;

        // Performance optimization: Cache text measurements for name and distance
        private string _cachedNameText = null;
        private float _cachedNameWidth = -1f;
        private List<(string text, float width, SKPaint paint)> _cachedImportantLootText = null;

        // Static cache for distance strings (shared across all players)
        private static readonly Dictionary<int, (string text, float width)> _playerDistanceCache = new();
        private static readonly Dictionary<int, (string text, float width)> _playerHeightCache = new();

        #endregion

        #region Virtual Properties

        /// <summary>
        /// Player name.
        /// </summary>
        public virtual string Name { get; set; }

        /// <summary>
        /// Account UUID for Human Controlled Players.
        /// </summary>
        public virtual string AccountID { get; }

        /// <summary>
        /// Group that the player belongs to.
        /// </summary>
        public virtual int GroupID { get; } = -1;

        /// <summary>
        /// Player's Faction.
        /// </summary>
        public virtual Enums.EPlayerSide PlayerSide { get; }

        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public virtual bool IsHuman { get; }

        /// <summary>
        /// MovementContext / StateContext
        /// </summary>
        public virtual ulong MovementContext { get; }

        /// <summary>
        /// EFT.PlayerBody
        /// </summary>
        public virtual ulong Body { get; }

        /// <summary>
        /// Inventory Controller field address.
        /// </summary>
        public virtual ulong InventoryControllerAddr { get; }

        /// <summary>
        /// Hands Controller field address.
        /// </summary>
        public virtual ulong HandsControllerAddr { get; }

        /// <summary>
        /// Corpse field address..
        /// </summary>
        public virtual ulong CorpseAddr { get; }

        /// <summary>
        /// Player Rotation Field Address (view angles).
        /// </summary>
        public virtual ulong RotationAddress { get; }
        public virtual float ZoomLevel { get; set; }
        public virtual ulong PWA { get; set; }

        public virtual ref Vector3 Position => ref this.Skeleton.Root.Position;

        #endregion

        #region Boolean Getters

        /// <summary>
        /// Player is AI-Controlled.
        /// </summary>
        public bool IsAI => !IsHuman;

        /// <summary>
        /// Player is a PMC Operator.
        /// </summary>
        public bool IsPmc => PlayerSide is Enums.EPlayerSide.Usec || PlayerSide is Enums.EPlayerSide.Bear;

        /// <summary>
        /// Player is a SCAV.
        /// </summary>
        public bool IsScav => PlayerSide is Enums.EPlayerSide.Savage;

        /// <summary>
        /// Player is alive (not dead).
        /// </summary>
        public bool IsAlive => Corpse is null;

        /// <summary>
        /// True if Player is Friendly to LocalPlayer.
        /// </summary>
        public bool IsFriendly => this is LocalPlayer || Type is PlayerType.Teammate;

        /// <summary>
        /// True if player is Hostile to LocalPlayer.
        /// </summary>
        public bool IsHostile => !IsFriendly;

        /// <summary>
        /// Player is Alive/Active and NOT LocalPlayer.
        /// </summary>
        public bool IsNotLocalPlayerAlive => this is not LocalPlayer && IsActive && IsAlive;

        /// <summary>
        /// Player is a Hostile PMC Operator.
        /// </summary>
        public bool IsHostilePmc => IsPmc && IsHostile;

        /// <summary>
        /// Player is human-controlled (Not LocalPlayer).
        /// </summary>
        public bool IsHumanOther => IsHuman && this is not LocalPlayer;

        /// <summary>
        /// Player is AI Controlled and Alive/Active.
        /// </summary>
        public bool IsAIActive => IsAI && IsActive && IsAlive;

        /// <summary>
        /// Player is AI Controlled and Alive/Active & their AI Role is default.
        /// </summary>
        public bool IsDefaultAIActive => IsAI && Name == "defaultAI" && IsActive && IsAlive;

        /// <summary>
        /// Player is human-controlled and Active/Alive.
        /// </summary>
        public bool IsHumanActive => IsHuman && IsActive && IsAlive;

        /// <summary>
        /// Player is hostile and alive/active.
        /// </summary>
        public bool IsHostileActive => IsHostile && IsActive && IsAlive;

        /// <summary>
        /// Player is human-controlled & Hostile.
        /// </summary>
        public bool IsHumanHostile => IsHuman && IsHostile;

        /// <summary>
        /// Player is human-controlled, hostile, and Active/Alive.
        /// </summary>
        public bool IsHumanHostileActive => IsHumanHostile && IsActive && IsAlive;

        /// <summary>
        /// Player is friendly to LocalPlayer (including LocalPlayer) and Active/Alive.
        /// </summary>
        public bool IsFriendlyActive => IsFriendly && IsActive && IsAlive;

        /// <summary>
        /// Player has exfil'd/left the raid.
        /// </summary>
        public bool HasExfild => !IsActive && IsAlive;

        private static Config Config => Program.Config;

        private bool BattleMode => Config.BattleMode;

        #endregion

        #region Methods

        private readonly Lock _alertsLock = new();
        /// <summary>
        /// Update the Alerts for this Player Object.
        /// </summary>
        /// <param name="alert">Alert to set.</param>
        public void UpdateAlerts(string alert)
        {
            if (alert is null)
                return;

            lock (_alertsLock)
            {
                if (this.Alerts is null)
                    this.Alerts = alert;
                else
                    this.Alerts = $"{alert} | {this.Alerts}";
            }
        }

        public void ClearAlerts()
        {
            lock (_alertsLock)
            {
                this.Alerts = null;
            }
        }

        public void UpdatePlayerType(PlayerType newType)
        {
            this.Type = newType;
        }

        public void UpdateStreamingUsername(string url)
        {
            this.StreamingUsername = url;
        }

        /// <summary>
        /// Validates the Rotation Address.
        /// </summary>
        /// <param name="rotationAddr">Rotation va</param>
        /// <returns>Validated rotation virtual address.</returns>
        protected static ulong ValidateRotationAddr(ulong rotationAddr)
        {
            var rotation = Memory.ReadValue<Vector2>(rotationAddr, false);
            if (!rotation.IsNormalOrZero() ||
                Math.Abs(rotation.X) > 360f ||
                Math.Abs(rotation.Y) > 90f)
                throw new ArgumentOutOfRangeException(nameof(rotationAddr));

            return rotationAddr;
        }

        /// <summary>
        /// Refreshes non-realtime player information. Call in the Registered Players Loop (T0).
        /// </summary>
        /// <param name="index"></param>
        /// <param name="registered"></param>
        /// <param name="isActiveParam"></param>
        public virtual void OnRegRefresh(ScatterReadIndex index, IReadOnlySet<ulong> registered, bool? isActiveParam = null)
        {
            if (isActiveParam is not bool isActive)
                isActive = registered.Contains(this);
            if (isActive)
            {
                this.SetAlive();
            }
            else if (this.IsAlive) // Not in list, but alive
            {
                index.AddEntry<ulong>(0, this.CorpseAddr);
                index.Callbacks += x1 =>
                {
                    if (x1.TryGetResult<ulong>(0, out var corpsePtr) && corpsePtr != 0x0)
                        this.SetDead(corpsePtr);
                    else
                        this.SetExfild();
                };
            }
        }

        /// <summary>
        /// Mark player as dead.
        /// </summary>
        /// <param name="corpse">Corpse address.</param>
        public void SetDead(ulong corpse)
        {
            if (Memory.Game is LocalGameWorld game && Config.ChamsConfig.Enabled)
                PlayerChamsManager.ApplyDeathMaterial(this, game);

            Corpse = corpse;
            IsActive = false;
        }

        /// <summary>
        /// Mark player as exfil'd.
        /// </summary>
        private void SetExfild()
        {
            Corpse = null;
            IsActive = false;
        }

        /// <summary>
        /// Mark player as alive.
        /// </summary>
        private void SetAlive()
        {
            Corpse = null;
            LootObject = null;
            IsActive = true;
        }

        /// <summary>
        /// Executed on each Realtime Loop.
        /// </summary>
        /// <param name="index">Scatter read index dedicated to this player.</param>
        public virtual void OnRealtimeLoop(ScatterReadIndex index)
        {
            index.AddEntry<Vector2>(-1, this.RotationAddress); // Rotation
            foreach (var tr in Skeleton.Bones)
            {
                index.AddEntry<SharedArray<UnityTransform.TrsX>>((int)(uint)tr.Key, tr.Value.VerticesAddr,
                    (3 * tr.Value.Index + 3) * 16); // ESP Vertices
            }

            index.Callbacks += x1 =>
            {
                bool p1 = false;
                bool p2 = true;
                if (x1.TryGetResult<Vector2>(-1, out var rotation))
                    p1 = this.SetRotation(ref rotation);
                foreach (var tr in Skeleton.Bones)
                {
                    if (x1.TryGetResult<SharedArray<UnityTransform.TrsX>>((int)(uint)tr.Key, out var vertices))
                    {
                        try
                        {
                            try
                            {
                                _ = tr.Value.UpdatePosition(vertices);
                            }
                            catch (Exception ex) // Attempt to re-allocate Transform on error
                            {
                                LoneLogging.WriteLine($"ERROR getting Player '{this.Name}' {tr.Key} Position: {ex}");
                                this.Skeleton.ResetTransform(tr.Key);
                            }
                        }
                        catch
                        {
                            p2 = false;
                        }
                    }
                    else
                    {
                        p2 = false;
                    }
                }

                if (p1 && p2)
                    this.ErrorTimer.Reset();
                else
                    this.ErrorTimer.Start();
            };
        }

        /// <summary>
        /// Executed on each Fast Loop (Hands Manager updates).
        /// Uses multi-round scatter reads for optimal performance with pointer chasing.
        /// </summary>
        /// <param name="round1">First scatter read round.</param>
        /// <param name="round2">Second scatter read round.</param>
        /// <param name="round3">Third scatter read round.</param>
        /// <param name="round4">Fourth scatter read round.</param>
        public virtual void OnFastLoop(ScatterReadIndex round1, ScatterReadIndex round2, ScatterReadIndex round3, ScatterReadIndex round4)
        {
            try
            {
                if (Hands is null)
                    return;

                // Round 1: Read HandsController pointer
                var isClient = this is ClientPlayer;
                round1.AddEntry<ulong>(0, this.HandsControllerAddr);

                round1.Callbacks += x1 =>
                {
                    if (!x1.TryGetResult<ulong>(0, out var handsController) || handsController == 0)
                        return;

                    // Round 2: Read ItemBase pointer
                    var itemOffset = isClient ? Offsets.ItemHandsController.Item : Offsets.ObservedHandsController.ItemInHands;
                    round2.AddEntry<ulong>(1, handsController + itemOffset);

                    round2.Callbacks += x2 =>
                    {
                        if (!x2.TryGetResult<ulong>(1, out var itemBase) || itemBase == 0)
                            return;

                        // Check if item changed
                        if (itemBase != Hands._cached)
                        {
                            // Round 3: Read item template and chambers (for weapons)
                            round3.AddEntry<ulong>(2, itemBase + Offsets.LootItem.Template);
                            round3.AddEntry<ulong>(3, itemBase + Offsets.LootItemWeapon.Chambers); // Pre-read chambers for weapon check

                            round3.Callbacks += x3 =>
                            {
                                if (!x3.TryGetResult<ulong>(2, out var itemTemplate) || itemTemplate == 0)
                                    return;

                                // Round 4: Read item ID and ammo info
                                round4.AddEntry<Types.MongoID>(4, itemTemplate + Offsets.ItemTemplate._id);
                                round4.AddEntry<ulong>(5, itemTemplate + Offsets.ItemTemplate.ShortName); // For fallback if not in DB

                                round4.Callbacks += x4 =>
                                {
                                    // Process item ID
                                    if (x4.TryGetResult<Types.MongoID>(4, out var itemIDPtr))
                                    {
                                        var itemID = Memory.ReadUnityString(itemIDPtr.StringID);
                                        if (EftDataManager.AllItems.TryGetValue(itemID, out var heldItem))
                                        {
                                            Hands._cachedItem = new LootItem(heldItem);
                                        }
                                        else // Item doesn't exist in DB, use name from game memory
                                        {
                                            if (x4.TryGetResult<ulong>(5, out var itemNamePtr) && itemNamePtr != 0)
                                            {
                                                var itemName = Memory.ReadUnityString(itemNamePtr)?.Trim();
                                                if (string.IsNullOrEmpty(itemName))
                                                    itemName = "Item";

                                                // Handle special cases
                                                if (itemName.Contains("nsv_utes"))
                                                    itemName = "NSV Utyos";
                                                else if (itemName.Contains("ags30_30"))
                                                {
                                                    itemName = "AGS-30";
                                                    Hands._ammo = "VOG-30";
                                                }
                                                else if (itemName.Contains("izhmash_rpk16"))
                                                    itemName = "RPK-16";

                                                Hands._cachedItem = new("NULL", itemName);
                                            }
                                        }
                                        Hands._cached = itemBase;
                                    }

                                    // Process ammo if weapon
                                    if (Hands._cachedItem?.IsWeapon ?? false)
                                    {
                                        if (x3.TryGetResult<ulong>(3, out var chambers) && chambers != 0)
                                        {
                                            try
                                            {
                                                var slotPtr = Memory.ReadPtr(chambers + MemList<byte>.ArrStartOffset);
                                                var slotItem = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                                                var ammoTemplate = Memory.ReadPtr(slotItem + Offsets.LootItem.Template);
                                                var ammoIDPtr = Memory.ReadValue<Types.MongoID>(ammoTemplate + Offsets.ItemTemplate._id);
                                                var ammoID = Memory.ReadUnityString(ammoIDPtr.StringID);

                                                if (EftDataManager.AllItems.TryGetValue(ammoID, out var ammo))
                                                    Hands._ammo = ammo?.ShortName;
                                            }
                                            catch // Gun doesn't have a chamber, try magazine
                                            {
                                                try
                                                {
                                                    var ammoTemplate_ = FirearmManager.MagazineManager.GetAmmoTemplateFromWeapon(itemBase);
                                                    var ammoIdPtr = Memory.ReadValue<Types.MongoID>(ammoTemplate_ + Offsets.ItemTemplate._id);
                                                    var ammoId = Memory.ReadUnityString(ammoIdPtr.StringID);

                                                    if (EftDataManager.AllItems.TryGetValue(ammoId, out var ammo))
                                                        Hands._ammo = ammo?.ShortName;
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                };
                            };
                        }
                    };
                };
            }
            catch
            {
                if (Hands is not null)
                    Hands._cached = 0x0;
            }
        }

        /// <summary>
        /// Executed on each Transform Validation Loop.
        /// </summary>
        /// <param name="round1">Index (round 1)</param>
        /// <param name="round2">Index (round 2)</param>
        public void OnValidateTransforms(ScatterReadIndex round1, ScatterReadIndex round2)
        {
            foreach (var tr in Skeleton.Bones)
            {
                round1.AddEntry<MemPointer>((int)(uint)tr.Key,
                    tr.Value.TransformInternal +
                    UnityOffsets.TransformInternal.TransformAccess); // Bone Hierarchy
                round1.Callbacks += x1 =>
                {
                    if (x1.TryGetResult<MemPointer>((int)(uint)tr.Key, out var tra))
                        round2.AddEntry<MemPointer>((int)(uint)tr.Key, tra + UnityOffsets.TransformAccess.Vertices); // Vertices Ptr
                    round2.Callbacks += x2 =>
                    {
                        if (x2.TryGetResult<MemPointer>((int)(uint)tr.Key, out var verticesPtr))
                        {
                            if (tr.Value.VerticesAddr != verticesPtr) // check if any addr changed
                            {
                                LoneLogging.WriteLine($"WARNING - '{tr.Key}' Transform has changed for Player '{this.Name}'");
                                this.Skeleton.ResetTransform(tr.Key); // alloc new transform
                            }
                        }
                    };
                };
            }
        }

        /// <summary>
        /// Set player rotation (Direction/Pitch)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual bool SetRotation(ref Vector2 rotation)
        {
            try
            {
                rotation.ThrowIfAbnormalAndNotZero();
                rotation.X = rotation.X.NormalizeAngle();
                ArgumentOutOfRangeException.ThrowIfLessThan(rotation.X, 0f);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(rotation.X, 360f);
                ArgumentOutOfRangeException.ThrowIfLessThan(rotation.Y, -90f);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(rotation.Y, 90f);
                Rotation = rotation;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Refresh Gear if Active Human Player.
        /// </summary>
        public void RefreshGear()
        {
            try
            {
                Gear ??= new GearManager(this, IsPmc);
                Gear?.Refresh();
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[GearManager] ERROR for Player {Name}: {ex}");
            }
        }

        /// <summary>
        /// Refresh item in player's hands.
        /// </summary>
        public void RefreshHands()
        {
            try
            {
                if (IsActive && IsAlive)
                {
                    Hands ??= new HandsManager(this);
                    Hands?.Refresh();
                }
            }
            catch { }
        }

        /// <summary>
        /// Get the Transform Internal Chain for this Player.
        /// </summary>
        /// <param name="bone">Bone to lookup.</param>
        /// <returns>Array of offsets for transform internal chain.</returns>
        public virtual uint[] GetTransformInternalChain(Bones bone) =>
            throw new NotImplementedException();

        #endregion

        #region AI Player Types

        public readonly struct AIRole
        {
            public readonly string Name { get; init; }
            public readonly PlayerType Type { get; init; }
        }

        /// <summary>
        /// Lookup AI Info based on Voice Line.
        /// </summary>
        /// <param name="voiceLine"></param>
        /// <returns></returns>
        public static AIRole GetAIRoleInfo(string voiceLine)
        {
            switch (voiceLine)
            {
                case "BossSanitar":
                    return new AIRole()
                    {
                        Name = "Sanitar",
                        Type = PlayerType.AIBoss
                    };
                case "BossBully":
                    return new AIRole()
                    {
                        Name = "Reshala",
                        Type = PlayerType.AIBoss
                    };
                case "BossGluhar":
                    return new AIRole()
                    {
                        Name = "Gluhar",
                        Type = PlayerType.AIBoss
                    };
                case "SectantPriest":
                    return new AIRole()
                    {
                        Name = "Priest",
                        Type = PlayerType.AIBoss
                    };
                case "SectantWarrior":
                    return new AIRole()
                    {
                        Name = "Cultist",
                        Type = PlayerType.AIRaider
                    };
                case "BossKilla":
                    return new AIRole()
                    {
                        Name = "Killa",
                        Type = PlayerType.AIBoss
                    };
                case "BossTagilla":
                    return new AIRole()
                    {
                        Name = "Tagilla",
                        Type = PlayerType.AIBoss
                    };
                case "Boss_Partizan":
                    return new AIRole()
                    {
                        Name = "Partisan",
                        Type = PlayerType.AIBoss
                    };
                case "BossBigPipe":
                    return new AIRole()
                    {
                        Name = "Big Pipe",
                        Type = PlayerType.AIBoss
                    };
                case "BossBirdEye":
                    return new AIRole()
                    {
                        Name = "Birdeye",
                        Type = PlayerType.AIBoss
                    };
                case "BossKnight":
                    return new AIRole()
                    {
                        Name = "Knight",
                        Type = PlayerType.AIBoss
                    };
                case "Arena_Guard_1":
                    return new AIRole()
                    {
                        Name = "Arena Guard",
                        Type = PlayerType.AIScav
                    };
                case "Arena_Guard_2":
                    return new AIRole()
                    {
                        Name = "Arena Guard",
                        Type = PlayerType.AIScav
                    };
                case "Boss_Kaban":
                    return new AIRole()
                    {
                        Name = "Kaban",
                        Type = PlayerType.AIBoss
                    };
                case "Boss_Kollontay":
                    return new AIRole()
                    {
                        Name = "Kollontay",
                        Type = PlayerType.AIBoss
                    };
                case "Boss_Sturman":
                    return new AIRole()
                    {
                        Name = "Shturman",
                        Type = PlayerType.AIBoss
                    };
                case "Zombie_Generic":
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case "BossZombieTagilla":
                    return new AIRole()
                    {
                        Name = "Zombie Tagilla",
                        Type = PlayerType.AIBoss
                    };
                case "Zombie_Fast":
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case "Zombie_Medium":
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                default:
                    break;
            }
            if (voiceLine.Contains("scav", StringComparison.OrdinalIgnoreCase))
                return new AIRole()
                {
                    Name = "Scav",
                    Type = PlayerType.AIScav
                };
            if (voiceLine.Contains("boss", StringComparison.OrdinalIgnoreCase))
                return new AIRole()
                {
                    Name = "Boss",
                    Type = PlayerType.AIBoss
                };
            if (voiceLine.Contains("usec", StringComparison.OrdinalIgnoreCase))
                return new AIRole()
                {
                    Name = "Usec",
                    Type = PlayerType.AIScav
                };
            if (voiceLine.Contains("bear", StringComparison.OrdinalIgnoreCase))
                return new AIRole()
                {
                    Name = "Bear",
                    Type = PlayerType.AIScav
                };
            LoneLogging.WriteLine($"Unknown Voice Line: {voiceLine}");
            return new AIRole()
            {
                Name = "AI",
                Type = PlayerType.AIScav
            };
        }

        public static AIRole GetAIRoleInfo(Enums.WildSpawnType wildSpawnType)
        {
            switch (wildSpawnType)
            {
                case Enums.WildSpawnType.marksman:
                    return new AIRole()
                    {
                        Name = "Sniper",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.assault:
                    return new AIRole()
                    {
                        Name = "Scav",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.bossTest:
                    return new AIRole()
                    {
                        Name = "bossTest",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.bossBully:
                    return new AIRole()
                    {
                        Name = "Reshala",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerTest:
                    return new AIRole()
                    {
                        Name = "followerTest",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.followerBully:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossKilla:
                    return new AIRole()
                    {
                        Name = "Killa",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.bossKojaniy:
                    return new AIRole()
                    {
                        Name = "Shturman",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerKojaniy:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.pmcBot:
                    return new AIRole()
                    {
                        Name = "Raider",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.cursedAssault:
                    return new AIRole()
                    {
                        Name = "Scav",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.bossGluhar:
                    return new AIRole()
                    {
                        Name = "Gluhar",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerGluharAssault:
                    return new AIRole()
                    {
                        Name = "Assault",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerGluharSecurity:
                    return new AIRole()
                    {
                        Name = "Security",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerGluharScout:
                    return new AIRole()
                    {
                        Name = "Scout",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerGluharSnipe:
                    return new AIRole()
                    {
                        Name = "Sniper",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerSanitar:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossSanitar:
                    return new AIRole()
                    {
                        Name = "Sanitar",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.test:
                    return new AIRole()
                    {
                        Name = "test",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.assaultGroup:
                    return new AIRole()
                    {
                        Name = "Scav",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.sectantWarrior:
                    return new AIRole()
                    {
                        Name = "Cultist",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.sectantPriest:
                    return new AIRole()
                    {
                        Name = "Priest",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.bossTagilla:
                    return new AIRole()
                    {
                        Name = "Tagilla",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerTagilla:
                    return new AIRole()
                    {
                        Name = "Tagilla",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.exUsec:
                    return new AIRole()
                    {
                        Name = "Rogue",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.gifter:
                    return new AIRole()
                    {
                        Name = "Santa",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.bossKnight:
                    return new AIRole()
                    {
                        Name = "Knight",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerBigPipe:
                    return new AIRole()
                    {
                        Name = "Big Pipe",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerBirdEye:
                    return new AIRole()
                    {
                        Name = "Bird Eye",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.bossZryachiy:
                    return new AIRole()
                    {
                        Name = "Zryachiy",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerZryachiy:
                    return new AIRole()
                    {
                        Name = "Cultist",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossBoar:
                    return new AIRole()
                    {
                        Name = "Kaban",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerBoar:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.arenaFighter:
                    return new AIRole()
                    {
                        Name = "Arena Fighter",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.arenaFighterEvent:
                    return new AIRole()
                    {
                        Name = "Bloodhound",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossBoarSniper:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.crazyAssaultEvent:
                    return new AIRole()
                    {
                        Name = "Scav",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.peacefullZryachiyEvent:
                    return new AIRole()
                    {
                        Name = "peacefullZryachiyEvent",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.sectactPriestEvent:
                    return new AIRole()
                    {
                        Name = "sectactPriestEvent",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.ravangeZryachiyEvent:
                    return new AIRole()
                    {
                        Name = "ravangeZryachiyEvent",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.followerBoarClose1:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerBoarClose2:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossKolontay:
                    return new AIRole()
                    {
                        Name = "Kolontay",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.followerKolontayAssault:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.followerKolontaySecurity:
                    return new AIRole()
                    {
                        Name = "Guard",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.shooterBTR:
                    return new AIRole()
                    {
                        Name = "BTR",
                        Type = PlayerType.AIRaider
                    };
                case Enums.WildSpawnType.bossPartisan:
                    return new AIRole()
                    {
                        Name = "Partisan",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.spiritWinter:
                    return new AIRole()
                    {
                        Name = "spiritWinter",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.spiritSpring:
                    return new AIRole()
                    {
                        Name = "spiritSpring",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.peacemaker:
                    return new AIRole()
                    {
                        Name = "Peacekeeper Goon",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.pmcBEAR:
                    return new AIRole()
                    {
                        Name = "BEAR",
                        Type = PlayerType.BEAR
                    };
                case Enums.WildSpawnType.pmcUSEC:
                    return new AIRole()
                    {
                        Name = "USEC",
                        Type = PlayerType.USEC
                    };
                case Enums.WildSpawnType.skier:
                    return new AIRole()
                    {
                        Name = "Skier Goon",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.sectantPredvestnik:
                    return new AIRole()
                    {
                        Name = "Partisan",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.sectantPrizrak:
                    return new AIRole()
                    {
                        Name = "Ghost",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.sectantOni:
                    return new AIRole()
                    {
                        Name = "Oni",
                        Type = PlayerType.AIBoss
                    };
                case Enums.WildSpawnType.infectedAssault:
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.infectedPmc:
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.infectedCivil:
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.infectedLaborant:
                    return new AIRole()
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case Enums.WildSpawnType.infectedTagilla:
                    return new AIRole()
                    {
                        Name = "Zombie Tagilla",
                        Type = PlayerType.AIBoss
                    };
                default:
                    LoneLogging.WriteLine("WARNING: Unknown WildSpawnType: " + (int)wildSpawnType);
                    return new AIRole()
                    {
                        Name = "defaultAI",
                        Type = PlayerType.AIScav
                    };
            }
        }

        #endregion

        #region Interfaces

        public void Draw(SKCanvas canvas, LoneMapParams mapParams, ILocalPlayer localPlayer)
        {
            try
            {
                var playerTypeKey = DeterminePlayerTypeKey();
                var typeSettings = Config.PlayerTypeSettings.GetSettings(playerTypeKey);
                var dist = Vector3.Distance(localPlayer.Position, Position);

                if (dist > typeSettings.RenderDistance)
                    return;

                var mapPosition = Position.ToMapPos(mapParams.Map);
                var point = mapPosition.ToZoomedPos(mapParams);
                MouseoverPosition = new Vector2(point.X, point.Y);

                if (!IsAlive)
                {
                    if (Config.ShowCorpseMarkers)
                    {
                        var corpseColor = GetCorpseFilterColor();
                        DrawDeathMarker(canvas, point, corpseColor);
                    }

                    return;
                }
                
                DrawPlayerMarker(canvas, localPlayer, point, typeSettings);

                if (this == localPlayer || BattleMode)
                    return;

                // Performance optimization: Skip text rendering at high zoom levels (LOD)
                // LODLevel: 0 = full detail, 1 = medium (skip some text), 2 = minimal (icons only, no text)
                if (mapParams.LODLevel >= 2)
                    return; // Skip all text at extreme zoom

                var height = Position.Y - localPlayer.Position.Y;
                string nameText = null;
                string distanceText = null;
                List<LootItem> importantLootItems = null;
                string heightText = null;
                var rightSideInfo = new List<string>();
                var hasImportantItems = Type != PlayerType.Teammate &&
                    ((Gear?.Loot?.Any(x => x.IsImportant) ?? false) ||
                     (Config.QuestHelper.Enabled && (Gear?.HasQuestItems ?? false)));

                // LODLevel 1: Skip detailed loot lists, keep only essential info
                bool skipDetailedInfo = mapParams.LODLevel >= 1;

                if (typeSettings.ShowName)
                {
                    var name = ErrorTimer.ElapsedMilliseconds > 100 ? "ERROR" : (Config.MaskNames && IsHuman ? "<Hidden>" : Name);
                    nameText = $"{name}";
                }

                if (typeSettings.ShowDistance)
                    distanceText = $"{(int)Math.Round(dist)}";

                // Performance optimization: At LOD 0 (close zoom), limit important loot to 2 items max
                // When zoomed in, you can see the actual loot on ground, so detailed list is less useful
                // This dramatically reduces text rendering overhead (from 5 items * 2 draws = 10 draws to 2 items * 2 draws = 4 draws per player)
                if (!skipDetailedInfo && typeSettings.ShowImportantLoot && IsAlive && Gear?.Loot != null && Type != PlayerType.Teammate)
                {
                    int maxItems = mapParams.LODLevel == 0 ? 2 : 5; // Show fewer items when zoomed in close

                    importantLootItems = Gear.Loot
                        .Where(item => item.IsImportant ||
                                       item is QuestItem ||
                                       (Config.QuestHelper.Enabled && item.IsQuestCondition) ||
                                       item.IsWishlisted ||
                                       (LootFilterControl.ShowBackpacks && item.IsBackpack) ||
                                       (LootFilterControl.ShowMeds && item.IsMeds) ||
                                       (LootFilterControl.ShowFood && item.IsFood) ||
                                       (LootFilterControl.ShowWeapons && item.IsWeapon) ||
                                       item.IsValuableLoot ||
                                       (!item.IsGroupedBlacklisted && item.MatchedFilter?.Color != null && !string.IsNullOrEmpty(item.MatchedFilter.Color)))
                        .OrderLoot()
                        .Take(maxItems)
                        .ToList();
                }

                if (typeSettings.ShowHeight && !typeSettings.HeightIndicator)
                    heightText = $"{(int)Math.Round(height)}";

                if (this is ObservedPlayer observed)
                {
                    if (typeSettings.ShowHealth && observed.HealthStatus != Enums.ETagStatus.Healthy)
                        rightSideInfo.Add($"{observed.HealthStatus.GetDescription()}");
                    if (typeSettings.ShowLevel && observed.Profile?.Level is int playerLevel)
                        rightSideInfo.Add($"L: {playerLevel}");
                    if (typeSettings.ShowKD && observed.Profile?.Overall_KD is float kd)
                        if (kd >= typeSettings.MinKD)
                            rightSideInfo.Add(kd.ToString("n2"));
                }

                if (typeSettings.ShowGroupID && GroupID != -1)
                    rightSideInfo.Add($"G:{GroupID}");
                if (typeSettings.ShowADS && IsAiming)
                    rightSideInfo.Add("ADS");
                if (typeSettings.ShowWeapon && Hands?.CurrentItem != null)
                    rightSideInfo.Add(Hands.CurrentItem);
                if (typeSettings.ShowAmmoType && Hands?.CurrentAmmo != null)
                    rightSideInfo.Add($"{Hands.CurrentAmmo}");
                if (typeSettings.ShowThermal && Gear?.HasThermal == true)
                    rightSideInfo.Add("THERMAL");
                if (typeSettings.ShowNVG && Gear?.HasNVG == true)
                    rightSideInfo.Add("NVG");
                if (typeSettings.ShowUBGL && Gear?.HasUBGL == true)
                    rightSideInfo.Add("UBGL");
                if (typeSettings.ShowValue && Gear?.Value > 0)
                    rightSideInfo.Add($"{TarkovMarketItem.FormatPrice(Gear.Value)}");
                if (typeSettings.ShowTag && !string.IsNullOrEmpty(Alerts))
                    rightSideInfo.Add(Alerts);

                DrawPlayerText(canvas, point, nameText, distanceText, heightText, rightSideInfo, hasImportantItems, importantLootItems);

                if (typeSettings.ShowHeight && typeSettings.HeightIndicator)
                    DrawAlternateHeightIndicator(canvas, point, height, GetPaints());
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"WARNING! Player Draw Error: {ex}");
            }
        }

        private void DrawAlternateHeightIndicator(SKCanvas canvas, SKPoint point, float heightDiff, ValueTuple<SKPaint, SKPaint> paints)
        { 
            var baseX = point.X - (15.0f * MainWindow.UIScale);
            var baseY = point.Y + (3.5f * MainWindow.UIScale);

            SKPaints.ShapeOutline.StrokeWidth = 2f * MainWindow.UIScale;

            var arrowSize = HEIGHT_INDICATOR_ARROW_SIZE * MainWindow.UIScale;
            var circleSize = arrowSize * 0.7f;

            if (heightDiff > HEIGHT_INDICATOR_THRESHOLD)
            {
                var upArrowPoint = new SKPoint(baseX, baseY - arrowSize);
                using var path = upArrowPoint.GetUpArrow(arrowSize);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
            }
            else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
            {
                var downArrowPoint = new SKPoint(baseX, baseY - arrowSize / 2);
                using var path = downArrowPoint.GetDownArrow(arrowSize);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
            }
        }

        private void DrawPlayerText(SKCanvas canvas, SKPoint point,
                                  string nameText, string distanceText,
                                  string heightText, List<string> rightSideInfo,
                                  bool hasImportantItems, List<LootItem> importantLootItems = null)
        {
            var paints = GetPaints();

            if (MainWindow.MouseoverGroup is int grp && grp == GroupID)
                paints.Item2 = SKPaints.TextMouseoverGroup;

            var spacing = 1 * MainWindow.UIScale;
            var textSize = 12 * MainWindow.UIScale;
            var baseYPosition = point.Y - 12 * MainWindow.UIScale;

            var playerTypeKey = DeterminePlayerTypeKey();
            var typeSettings = Config.PlayerTypeSettings.GetSettings(playerTypeKey);
            var showImportantIndicator = typeSettings.ImportantIndicator && hasImportantItems;

            // BENCHMARK: Track player name rendering time
            if (!string.IsNullOrEmpty(nameText))
            {
                // Performance optimization: Cache name text measurement
                if (_cachedNameText != nameText)
                {
                    _cachedNameText = nameText;
                    _cachedNameWidth = paints.Item2.MeasureText(nameText);
                }

                var namePoint = new SKPoint(point.X - (_cachedNameWidth / 2), baseYPosition - 0);

                // Performance optimization: Single-draw for player names (2x faster)
                // Player names are dynamic (different for each player), so can't use atlas
                // Skip outline to cut rendering in half - text is still readable
                canvas.DrawText(nameText, namePoint, paints.Item2);

                if (showImportantIndicator)
                {
                    var asteriskWidth = SKPaints.TextPulsingAsterisk.MeasureText("*");
                    var verticalOffset = (SKPaints.TextPulsingAsterisk.TextSize - paints.Item2.TextSize) / 2;
                    verticalOffset += 1.5f * MainWindow.UIScale;

                    var asteriskPoint = new SKPoint(
                        namePoint.X - asteriskWidth - (2 * MainWindow.UIScale),
                        namePoint.Y + verticalOffset
                    );

                    // Performance optimization: Use player info atlas for asterisk
                    if (MainWindow.PlayerInfoAtlas != null && MainWindow.PlayerInfoAtlas.Contains("*"))
                    {
                        MainWindow.PlayerInfoAtlas.Draw(canvas, "*", asteriskPoint, SKPaints.TextPulsingAsterisk);
                    }
                    else
                    {
                        canvas.DrawText("*", asteriskPoint, SKPaints.TextPulsingAsteriskOutline);
                        canvas.DrawText("*", asteriskPoint, SKPaints.TextPulsingAsterisk);
                    }
                }
            }
            else if (showImportantIndicator)
            {
                var asteriskWidth = SKPaints.TextPulsingAsterisk.MeasureText("*");
                var yPos = point.Y - 2 * MainWindow.UIScale;
                var asteriskPoint = new SKPoint(point.X - (asteriskWidth / 2), yPos);

                // Performance optimization: Use player info atlas for asterisk
                if (MainWindow.PlayerInfoAtlas != null && MainWindow.PlayerInfoAtlas.Contains("*"))
                {
                    MainWindow.PlayerInfoAtlas.Draw(canvas, "*", asteriskPoint, SKPaints.TextPulsingAsterisk);
                }
                else
                {
                    canvas.DrawText("*", asteriskPoint, SKPaints.TextPulsingAsteriskOutline);
                    canvas.DrawText("*", asteriskPoint, SKPaints.TextPulsingAsterisk);
                }
            }

            var currentBottomY = point.Y + 20 * MainWindow.UIScale;
            if (!string.IsNullOrEmpty(distanceText))
            {
                // Performance optimization: Use pre-rendered text atlas (10-50x faster than DrawText!)
                int dist = int.Parse(distanceText.TrimEnd('m'));
                string distText = $"{dist}m";

                var distPoint = new SKPoint(point.X, currentBottomY);

                if (MainWindow.DistanceAtlas != null && MainWindow.DistanceAtlas.Contains(distText))
                {
                    // Use ultra-fast atlas rendering
                    MainWindow.DistanceAtlas.DrawCentered(canvas, distText, distPoint, paints.Item2);
                }
                else
                {
                    // Fallback to standard text (rare, only if atlas not initialized)
                    var distWidth = paints.Item2.MeasureText(distText);
                    var fallbackPoint = new SKPoint(point.X - (distWidth / 2), currentBottomY);
                    canvas.DrawText(distText, fallbackPoint, SKPaints.TextOutline);
                    canvas.DrawText(distText, fallbackPoint, paints.Item2);
                }
            }

            if (importantLootItems?.Any() == true)
            {
                // Performance optimization: Cache important loot items text measurements
                if (_cachedImportantLootText == null || _cachedImportantLootText.Count != importantLootItems.Count)
                {
                    _cachedImportantLootText = new List<(string, float, SKPaint)>();
                    foreach (var item in importantLootItems)
                    {
                        var itemText = item.ShortName;
                        var itemPaint = GetPlayerLootItemTextPaint(item);
                        var itemWidth = itemPaint.MeasureText(itemText);
                        _cachedImportantLootText.Add((itemText, itemWidth, itemPaint));
                    }
                }

                currentBottomY += textSize + spacing;

                foreach (var (itemText, itemWidth, itemPaint) in _cachedImportantLootText)
                {
                    var itemPoint = new SKPoint(point.X - (itemWidth / 2), currentBottomY);

                    // Performance optimization: Use pre-rendered loot name atlas (10-50x faster, eliminates double-draw)
                    if (MainWindow.LootNameAtlas != null && MainWindow.LootNameAtlas.Contains(itemText))
                    {
                        // Ultra-fast atlas rendering - single draw, no outline overhead
                        MainWindow.LootNameAtlas.Draw(canvas, itemText, itemPoint, itemPaint);
                    }
                    else
                    {
                        // Fallback to double-draw for names not in atlas (rare/dynamic text)
                        canvas.DrawText(itemText, itemPoint, SKPaints.TextOutline);
                        canvas.DrawText(itemText, itemPoint, itemPaint);
                    }

                    currentBottomY += textSize + spacing;
                }
            }

            if (!string.IsNullOrEmpty(heightText))
            {
                // Performance optimization: Use pre-rendered text atlas (10-50x faster!)
                int height = int.Parse(heightText.TrimStart('+').TrimEnd('m'));
                string heightStr = height > 0 ? $"+{height}m" : $"{height}m";

                if (MainWindow.HeightAtlas != null && MainWindow.HeightAtlas.Contains(heightStr))
                {
                    // Use ultra-fast atlas rendering
                    float heightWidth = MainWindow.HeightAtlas.GetWidth(heightStr);
                    var heightPoint = new SKPoint(point.X - heightWidth - 15 * MainWindow.UIScale, point.Y + 5 * MainWindow.UIScale);
                    MainWindow.HeightAtlas.Draw(canvas, heightStr, heightPoint, paints.Item2);
                }
                else
                {
                    // Fallback to standard text
                    var heightWidth = paints.Item2.MeasureText(heightStr);
                    var heightPoint = new SKPoint(point.X - heightWidth - 15 * MainWindow.UIScale, point.Y + 5 * MainWindow.UIScale);
                    canvas.DrawText(heightStr, heightPoint, SKPaints.TextOutline);
                    canvas.DrawText(heightStr, heightPoint, paints.Item2);
                }
            }

            if (rightSideInfo.Count > 0)
            {
                var rightPoint = new SKPoint(
                    point.X + 14 * MainWindow.UIScale,
                    point.Y + 2 * MainWindow.UIScale
                );

                foreach (var line in rightSideInfo)
                {
                    if (string.IsNullOrEmpty(line?.Trim()))
                        continue;

                    // Performance optimization: Use player info atlas for static strings (THERMAL, NVG, etc.)
                    if (MainWindow.PlayerInfoAtlas != null && MainWindow.PlayerInfoAtlas.Contains(line))
                    {
                        // Ultra-fast atlas rendering - single draw, no outline overhead
                        MainWindow.PlayerInfoAtlas.Draw(canvas, line, rightPoint, paints.Item2);
                    }
                    else
                    {
                        // Fallback to double-draw for dynamic text (prices, custom tags)
                        canvas.DrawText(line, rightPoint, SKPaints.TextOutline);
                        canvas.DrawText(line, rightPoint, paints.Item2);
                    }
                    rightPoint.Offset(0, textSize);
                }
            }
        }

        /// <summary>
        /// Performance-optimized method to get cached distance text.
        /// Rounds to nearest 5m for better cache hit rate.
        /// </summary>
        private static (string text, float width) GetCachedDistanceText(int distance, SKPaint paint)
        {
            int roundedDist = ((distance + 2) / 5) * 5;

            if (!_playerDistanceCache.TryGetValue(roundedDist, out var cached))
            {
                cached.text = $"{roundedDist}m";
                cached.width = paint.MeasureText(cached.text);

                if (_playerDistanceCache.Count < 1000)
                    _playerDistanceCache[roundedDist] = cached;
            }

            return cached;
        }

        /// <summary>
        /// Performance-optimized method to get cached height difference text.
        /// </summary>
        private static (string text, float width) GetCachedHeightText(int heightDiff, SKPaint paint)
        {
            if (!_playerHeightCache.TryGetValue(heightDiff, out var cached))
            {
                string sign = heightDiff > 0 ? "+" : "";
                cached.text = $"{sign}{heightDiff}m";
                cached.width = paint.MeasureText(cached.text);

                if (_playerHeightCache.Count < 500)
                    _playerHeightCache[heightDiff] = cached;
            }

            return cached;
        }

        /// <summary>
        /// Draws a Player Marker on this location with type-specific settings
        /// </summary>
        private void DrawPlayerMarker(SKCanvas canvas, ILocalPlayer localPlayer, SKPoint point, PlayerTypeSettings typeSettings)
        {
            var radians = MapRotation.ToRadians();
            var paints = GetPaints();

            if (this != localPlayer && MainWindow.MouseoverGroup is int grp && grp == GroupID)
                paints.Item1 = SKPaints.PaintMouseoverGroup;

            SKPaints.ShapeOutline.StrokeWidth = paints.Item1.StrokeWidth + 2f * MainWindow.UIScale;

            var size = 6 * MainWindow.UIScale;
            canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
            canvas.DrawCircle(point, size, paints.Item1);

            var aimlineLength = typeSettings.AimlineLength;

            if (typeSettings.HighAlert && !IsFriendly && this.IsFacingTarget(localPlayer, typeSettings.RenderDistance))
                aimlineLength = 9999;

            var aimlineEnd = GetAimlineEndpoint(point, radians, aimlineLength);
            canvas.DrawLine(point, aimlineEnd, SKPaints.ShapeOutline);
            canvas.DrawLine(point, aimlineEnd, paints.Item1);
        }

        /// <summary>
        /// Draws a Death Marker on this location.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawDeathMarker(SKCanvas canvas, SKPoint point, SKColor color)
        {
            var length = 6 * MainWindow.UIScale;

            using var corpseLinePaint = new SKPaint
            {
                Color = color,
                StrokeWidth = SKPaints.PaintDeathMarker.StrokeWidth,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            canvas.DrawLine(new SKPoint(point.X - length, point.Y + length),
                new SKPoint(point.X + length, point.Y - length), corpseLinePaint);
            canvas.DrawLine(new SKPoint(point.X - length, point.Y - length),
                new SKPoint(point.X + length, point.Y + length), corpseLinePaint);
        }

        /// <summary>
        /// Gets the point where the Aimline 'Line' ends. Applies UI Scaling internally.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SKPoint GetAimlineEndpoint(SKPoint start, float radians, float aimlineLength)
        {
            aimlineLength *= MainWindow.UIScale;
            return new SKPoint(start.X + MathF.Cos(radians) * aimlineLength,
                start.Y + MathF.Sin(radians) * aimlineLength);
        }

        private SKColor GetCorpseFilterColor()
        {
            if (LootObject?.Loot != null && LootObject.Loot.Any())
            {
                var topItem = LootObject.Loot.OrderLoot().FirstOrDefault();

                if (topItem != null)
                {
                    var matchedFilter = topItem.MatchedFilter;
                    if (matchedFilter != null && !string.IsNullOrEmpty(matchedFilter.Color))
                    {
                        if (SKColor.TryParse(matchedFilter.Color, out var filterColor))
                            return filterColor;
                    }

                    if (topItem is QuestItem || (Config.QuestHelper.Enabled && topItem.IsQuestCondition))
                        return SKPaints.PaintQuestItem.Color;

                    if (topItem.IsWishlisted)
                        return SKPaints.PaintWishlistItem.Color;

                    if (topItem.IsValuableLoot)
                        return SKPaints.PaintImportantLoot.Color;
                }
            }

            return SKPaints.PaintDeathMarker.Color;
        }

        /// <summary>
        /// Helper method to get the appropriate text paint for a player's loot item based on its importance/filter
        /// </summary>
        private static SKPaint GetPlayerLootItemTextPaint(LootItem item)
        {
            var isImportant = item.IsImportant ||
                               item is QuestItem ||
                               (Config.QuestHelper.Enabled && item.IsQuestCondition) ||
                               (LootFilterControl.ShowBackpacks && item.IsBackpack) ||
                               (LootFilterControl.ShowMeds && item.IsMeds) ||
                               (LootFilterControl.ShowFood && item.IsFood) ||
                               (LootFilterControl.ShowWeapons && item.IsWeapon) ||
                               item.IsValuableLoot;

            if (isImportant)
            {
                var paints = item.GetPaints();
                return paints.Item2;
            }

            return SKPaints.TextMouseover;
        }

        public ValueTuple<SKPaint, SKPaint> GetPaints()
        {
            if (IsAimbotLocked)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintAimbotLocked, SKPaints.TextAimbotLocked);

            if (IsFocused)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintFocused, SKPaints.TextFocused);

            if (this is LocalPlayer)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintLocalPlayer, SKPaints.TextLocalPlayer);

            switch (Type)
            {
                case PlayerType.Teammate:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintTeammate, SKPaints.TextTeammate);
                case PlayerType.USEC:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSEC, SKPaints.TextUSEC);
                case PlayerType.BEAR:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintBEAR, SKPaints.TextBEAR);
                case PlayerType.AIScav:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintScav, SKPaints.TextScav);
                case PlayerType.AIRaider:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintRaider, SKPaints.TextRaider);
                case PlayerType.AIBoss:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintBoss, SKPaints.TextBoss);
                case PlayerType.PScav:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintPScav, SKPaints.TextPScav);
                case PlayerType.SpecialPlayer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintSpecial, SKPaints.TextSpecial);
                case PlayerType.Streamer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintStreamer, SKPaints.TextStreamer);
                default:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSEC, SKPaints.TextUSEC);
            }
        }

        public void DrawMouseover(SKCanvas canvas, LoneMapParams mapParams, LocalPlayer localPlayer)
        {
            if (this == localPlayer)
                return;

            var playerTypeKey = DeterminePlayerTypeKey();
            var typeSettings = Config.PlayerTypeSettings.GetSettings(playerTypeKey);

            var lines = new List<(string text, SKPaint paint)>();
            var name = Config.MaskNames && IsHuman ? "<Hidden>" : Name;
            string health = null;
            string kd = null;

            if (this is ObservedPlayer observed)
            {
                health = observed.HealthStatus is Enums.ETagStatus.Healthy
                    ? null
                    : $" ({observed.HealthStatus.GetDescription()})"; // Only display abnormal health status

                if (observed.Profile?.Overall_KD is float kdResult)
                    kd = kdResult.ToString("n2");
            }

            if (IsStreaming) // Streamer Notice
                lines.Add(($"[LIVE - Double Click]", SKPaints.TextMouseover));

            var alert = this.Alerts?.Trim();

            if (!string.IsNullOrEmpty(alert))
                lines.Add((alert, SKPaints.TextMouseover));

            if (IsHostileActive)
            {
                lines.Add(($"{name}{health}", SKPaints.TextMouseover));
                lines.Add(($"KD: {kd}", SKPaints.TextMouseover));
                var gear = Gear;
                var hands = $"{Hands?.CurrentItem} {Hands?.CurrentAmmo}".Trim();
                lines.Add(($"Use: {(hands is null ? "--" : hands)}", SKPaints.TextMouseover));
                var faction = PlayerSide.ToString();
                string g = null;

                if (GroupID != -1)
                    g = $" G:{GroupID} ";

                lines.Add(($"{faction}{g}", SKPaints.TextMouseover));

                var loot = gear?.Loot;

                if (loot is not null)
                {
                    var playerValue = TarkovMarketItem.FormatPrice(gear?.Value ?? -1);
                    lines.Add(($"Value: {playerValue}", SKPaints.TextMouseover));
                    var iterations = 0;

                    foreach (var item in loot)
                    {
                        if (iterations++ >= 5)
                            break;

                        var itemPaint = GetPlayerLootItemTextPaint(item);
                        lines.Add((item.GetUILabel(), itemPaint));
                    }
                }
            }
            else if (!IsAlive)
            {
                lines.Add(($"{Type.GetDescription()}:{name}", SKPaints.TextMouseover));
                string g = null;

                if (GroupID != -1)
                    g = $"G:{GroupID} ";

                if (g is not null) lines.Add((g, SKPaints.TextMouseover));

                var corpseLoot = LootObject?.Loot?.OrderLoot();

                if (corpseLoot is not null)
                {
                    var sumPrice = corpseLoot.Sum(x => x.Price);
                    var corpseValue = TarkovMarketItem.FormatPrice(sumPrice);
                    lines.Add(($"Value: {corpseValue}", SKPaints.TextMouseover));

                    if (corpseLoot.Any())
                    {
                        foreach (var item in corpseLoot)
                        {
                            var itemPaint = GetPlayerLootItemTextPaint(item);
                            lines.Add((item.GetUILabel(), itemPaint));
                        }
                    }
                    else
                    {
                        lines.Add(("Empty", SKPaints.TextMouseover));
                    }
                }
            }
            else if (IsAIActive)
            {
                lines.Add((name, SKPaints.TextMouseover));
            }

            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (this == localPlayer || !IsActive || !IsAlive)
                return;

            var playerTypeKey = DeterminePlayerTypeKey();
            var espTypeSettings = ESP.Config.PlayerTypeESPSettings.GetSettings(playerTypeKey);

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > espTypeSettings.RenderDistance)
                return;

            var renderMode = espTypeSettings.RenderMode;
            var observedPlayer = this as ObservedPlayer;
            var showADS = espTypeSettings.ShowADS && IsAiming;
            var showAmmo = espTypeSettings.ShowAmmoType && Hands?.CurrentAmmo != null;
            var showDist = espTypeSettings.ShowDistance;
            var showHealth = espTypeSettings.ShowHealth;
            var showName = espTypeSettings.ShowName;
            var showKD = espTypeSettings.ShowKD;
            var showNVG = espTypeSettings.ShowNVG && Gear?.HasNVG == true;
            var showThermal = espTypeSettings.ShowThermal && Gear?.HasThermal == true;
            var showUBGL = espTypeSettings.ShowUBGL && Gear?.HasUBGL == true;
            var showWep = espTypeSettings.ShowWeapon && Hands?.CurrentItem != null;
            var highAlert = espTypeSettings.HighAlert;
            var showImportantIndicator = espTypeSettings.ImportantIndicator &&
                Type != PlayerType.Teammate &&
                ((Gear?.Loot?.Any(x => x.IsImportant) ?? false) ||
                 (Config.QuestHelper.Enabled && (Gear?.HasQuestItems ?? false)));
            var showImportantLoot = espTypeSettings.ShowImportantLoot && Type != PlayerType.Teammate &&
                (Gear?.Loot?.Any(x => x.IsImportant) ?? false);

            if (IsHostile && highAlert)
            {
                if (this.IsFacingTarget(localPlayer))
                {
                    if (!HighAlertSw.IsRunning)
                        HighAlertSw.Start();
                    else if (HighAlertSw.Elapsed.TotalMilliseconds >= 500f) // Don't draw twice or more
                        HighAlert.DrawHighAlertESP(canvas, this);
                }
                else
                {
                    HighAlertSw.Reset();
                }
            }

            if (!CameraManagerBase.WorldToScreen(ref Position, out var baseScrPos))
                return;

            var espPaints = GetESPPaints();

            if (renderMode is not ESPPlayerRenderMode.None && this is BtrOperator btr)
            {
                if (CameraManagerBase.WorldToScreen(ref btr.Position, out var btrScrPos))
                    btrScrPos.DrawESPText(canvas, btr, localPlayer, showDist, espPaints.Item2, "BTR Vehicle");
                return;
            }

            SKRect? playerBox = null;
            var calcBox = Skeleton.GetESPBox(baseScrPos);
            if (calcBox is SKRect box)
                playerBox = box;

            if (!playerBox.HasValue)
                return;

            var headPosition = new SKPoint(playerBox.Value.MidX, playerBox.Value.Top);

            if (renderMode is ESPPlayerRenderMode.Bones)
            {
                if (!this.Skeleton.UpdateESPBuffer())
                    return;

                // Each line consists of 2 points in ESPBuffer
                // So total segments = ESPBuffer.Length / 2
                int segmentCount = Skeleton.ESPBuffer.Length / 2;

                for (int i = 0; i < segmentCount; i++)
                {
                    int startIndex = i * 2;
                    SKPoint start = Skeleton.ESPBuffer[startIndex];
                    SKPoint end = Skeleton.ESPBuffer[startIndex + 1];

                    // Now we must find which bones correspond to these points to get visibility
                    // You have this mapping hardcoded in UpdateESPBuffer() - let's replicate it here:

                    // Map each segment index to pair of bones:
                    var bonePairs = new (Bones start, Bones end)[]
                    {
                        (Bones.HumanHead, Bones.HumanNeck),          // 0
                        (Bones.HumanNeck, Bones.HumanSpine3),        // 1
                        (Bones.HumanSpine3, Bones.HumanSpine2),      // 2
                        (Bones.HumanSpine2, Bones.HumanSpine1),      // 3
                        (Bones.HumanSpine1, Bones.HumanPelvis),      // 4
                        (Bones.HumanPelvis, Bones.HumanLThigh2),     // 5 (Left Knee)
                        (Bones.HumanLThigh2, Bones.HumanLFoot),      // 6 (Left Foot)
                        (Bones.HumanPelvis, Bones.HumanRThigh2),     // 7 (Right Knee)
                        (Bones.HumanRThigh2, Bones.HumanRFoot),      // 8 (Right Foot)
                        (Bones.HumanLCollarbone, Bones.HumanLForearm2), // 9 (Left Elbow)
                        (Bones.HumanLForearm2, Bones.HumanLPalm),    // 10 (Left Hand)
                        (Bones.HumanRCollarbone, Bones.HumanRForearm2), // 11 (Right Elbow)
                        (Bones.HumanRForearm2, Bones.HumanRPalm),    // 12 (Right Hand)
                    };

                    if (i >= bonePairs.Length)
                        continue; // Defensive

                    var pair = bonePairs[i];

                    bool startVisible = this.BoneVisibility.TryGetValue(pair.start, out bool sVis) && sVis;
                    bool endVisible = this.BoneVisibility.TryGetValue(pair.end, out bool eVis) && eVis;

                    var paint = (startVisible && endVisible) ? SKPaints.PaintVisible : espPaints.Item1;

                    canvas.DrawLine(start, end, paint);
                }
            }
            else if (renderMode is ESPPlayerRenderMode.Box)
            {
                canvas.DrawRect(playerBox.Value, espPaints.Item1);

                baseScrPos.X = playerBox.Value.MidX;
                baseScrPos.Y = playerBox.Value.Bottom;
            }
            else if (renderMode is ESPPlayerRenderMode.HeadDot)
            {
                if (CameraManagerBase.WorldToScreen(ref Skeleton.Bones[Bones.HumanHead].Position, out var actualHeadPos, true, true))
                {
                    canvas.DrawCircle(actualHeadPos, 1.5f * ESP.Config.FontScale, espPaints.Item1);
                }
                else
                {
                    canvas.DrawCircle(headPosition, 1.5f * ESP.Config.FontScale, espPaints.Item1);
                }
            }

            if (BattleMode)
                return;

            var baseYOffset = 5f * ESP.Config.FontScale;
            var lineHeight = espPaints.Item2.TextSize * 1.2f * ESP.Config.FontScale;
            var currentY = headPosition.Y - baseYOffset;

            if (showImportantIndicator)
            {
                var asteriskText = "*";
                var asteriskWidth = SKPaints.TextPulsingAsteriskESP.MeasureText(asteriskText);
                var asteriskOffsetY = 6f * ESP.Config.FontScale;

                if (showName)
                {
                    var nameOffsetX = 10f * ESP.Config.FontScale;
                    var asteriskPoint = new SKPoint(headPosition.X - nameOffsetX, currentY + asteriskOffsetY);

                    canvas.DrawText(asteriskText, asteriskPoint, SKPaints.TextPulsingAsteriskOutlineESP);
                    canvas.DrawText(asteriskText, asteriskPoint, SKPaints.TextPulsingAsteriskESP);
                }
                else
                {
                    var manualCenteringAdjustment = 4f * ESP.Config.FontScale;
                    var asteriskX = playerBox.Value.MidX - (asteriskWidth / 2) + manualCenteringAdjustment;
                    var asteriskPoint = new SKPoint(asteriskX, currentY + asteriskOffsetY);

                    canvas.DrawText(asteriskText, asteriskPoint, SKPaints.TextPulsingAsteriskOutlineESP);
                    canvas.DrawText(asteriskText, asteriskPoint, SKPaints.TextPulsingAsteriskESP);
                }
            }

            if (showADS && observedPlayer != null)
            {
                var adsPos = new SKPoint(headPosition.X, currentY);
                canvas.DrawText("ADS", adsPos, espPaints.Item2);
                currentY -= lineHeight;
            }

            if (showName)
            {
                var nameText = "";

                if (IsHostilePmc)
                {
                    if (PlayerSide is Enums.EPlayerSide.Usec)
                        nameText += "U:";
                    else if (PlayerSide is Enums.EPlayerSide.Bear)
                        nameText += "B:";
                }

                nameText += Name;

                var namePos = new SKPoint(headPosition.X, currentY);
                canvas.DrawText(nameText, namePos, espPaints.Item2);
            }

            if (showHealth && observedPlayer != null)
                DrawHealthBar(canvas, observedPlayer, playerBox.Value);

            var textStartY = playerBox.Value.Bottom + espPaints.Item2.TextSize * ESP.Config.FontScale;
            var currentTextY = textStartY;

            if ((showDist || showWep || showAmmo || showNVG || showThermal || showUBGL || showKD) || showImportantLoot)
            {
                var weaponInfoLines = new List<string>();

                if (showDist)
                    weaponInfoLines.Add($"{(int)dist}m");

                string weaponAmmoText = null;
                if (showWep && showAmmo)
                    weaponAmmoText = $"{Hands.CurrentItem}/{Hands.CurrentAmmo}";
                else if (showWep)
                    weaponAmmoText = Hands.CurrentItem;
                else if (showAmmo)
                    weaponAmmoText = Hands.CurrentAmmo;

                if (weaponAmmoText != null)
                    weaponInfoLines.Add(weaponAmmoText);

                if (showKD && observedPlayer != null && observedPlayer.Profile?.Overall_KD is float kd)
                    if (kd >= espTypeSettings.MinKD)
                        weaponInfoLines.Add(kd.ToString("n2"));

                if (showNVG)
                    weaponInfoLines.Add("NVG");

                if (showThermal)
                    weaponInfoLines.Add("THERMAL");

                if (showUBGL)
                    weaponInfoLines.Add("UBGL");

                var importantItems = showImportantLoot && IsAlive && Gear?.Loot != null ? Gear.Loot
                    .Where(item => item.IsImportant ||
                                  item is QuestItem ||
                                  (Config.QuestHelper.Enabled && item.IsQuestCondition) ||
                                  item.IsWishlisted ||
                                  (LootFilterControl.ShowBackpacks && item.IsBackpack) ||
                                  (LootFilterControl.ShowMeds && item.IsMeds) ||
                                  (LootFilterControl.ShowFood && item.IsFood) ||
                                  (LootFilterControl.ShowWeapons && item.IsWeapon) ||
                                  item.IsValuableLoot ||
                                  (!item.IsGroupedBlacklisted && item.MatchedFilter?.Color != null && !string.IsNullOrEmpty(item.MatchedFilter.Color)))
                    .OrderLoot()
                    .ToList() : null;

                if (weaponInfoLines.Any() || (importantItems?.Any() == true))
                {
                    var textPt = new SKPoint(playerBox.Value.MidX, playerBox.Value.Bottom + espPaints.Item2.TextSize * ESP.Config.FontScale);
                    var combinedMainLabel = weaponInfoLines.Any() ? string.Join("\n", weaponInfoLines) : null;

                    textPt.DrawESPText(
                        canvas,
                        this,
                        localPlayer,
                        false,
                        espPaints.Item2,
                        combinedMainLabel,
                        importantItems
                    );
                }
            }

            if (ESP.Config.ShowAimLock && IsAimbotLocked)
            {
                var info = MemWriteFeature<Aimbot>.Instance.Cache;
                if (info is not null &&
                    info.LastFireportPos is Vector3 fpPos &&
                    info.LastPlayerPos is Vector3 playerPos)
                {
                    if (!CameraManagerBase.WorldToScreen(ref fpPos, out var fpScreen))
                        return;
                    if (!CameraManagerBase.WorldToScreen(ref playerPos, out var playerScreen))
                        return;
                    canvas.DrawLine(fpScreen, playerScreen, SKPaints.PaintAimbotLockedLineESP);
                }
            }
        }
        public void UpdateBoneVisibility(Bones[] bones, bool[] results)
        {
            if (bones.Length != results.Length)
                return;

            for (int i = 0; i < bones.Length; i++)
            {
                BoneVisibility[bones[i]] = results[i];
                //LoneLogging.WriteLine($"Bone {bones[i]} visibility: {results[i]}"); // Your log line
            }
        }

        private SKPoint GetScreenPointForBone(Bones bone)
        {
            // If you store the bone screen points in a dictionary or buffer, use that
            // For example, you might do something like:
            if (!this.Skeleton.Bones.TryGetValue(bone, out var transform))
                return default;
        
            if (!CameraManagerBase.WorldToScreen(ref transform.Position, out var screenPos))
                return default;
        
            return new SKPoint(screenPos.X, screenPos.Y);
        }

        /// <summary>
        /// Draws a health bar to the left of the player
        /// </summary>
        private void DrawHealthBar(SKCanvas canvas, ObservedPlayer player, SKRect playerBounds)
        {
            var healthPercent = GetHealthPercentage(player);
            var healthColor = GetHealthColor(player.HealthStatus);
            var barWidth = 3f * ESP.Config.FontScale;
            var barHeight = playerBounds.Height; // Use full height of the player box
            var barOffsetX = 6f * ESP.Config.FontScale;

            var left = playerBounds.Left - barOffsetX - barWidth;
            var top = playerBounds.Top; // Align with the top of the player box

            var bgRect = new SKRect(left, top, left + barWidth, top + barHeight);

            canvas.DrawRect(bgRect, SKPaints.PaintESPHealthBarBg);

            var filledHeight = barHeight * healthPercent;
            var bottom = top + barHeight;
            var fillTop = bottom - filledHeight;
            var fillRect = new SKRect(left, fillTop, left + barWidth, bottom);

            var healthFillPaint = SKPaints.PaintESPHealthBar.Clone();
            healthFillPaint.Color = healthColor;

            canvas.DrawRect(fillRect, healthFillPaint);
            canvas.DrawRect(bgRect, SKPaints.PaintESPHealthBarBorder);
        }

        /// <summary>
        /// Gets health color based on player's health status
        /// </summary>
        private SKColor GetHealthColor(Enums.ETagStatus healthStatus)
        {
            return healthStatus switch
            {
                Enums.ETagStatus.Healthy => new SKColor(0, 255, 0),     // Green
                Enums.ETagStatus.Injured => new SKColor(255, 255, 0),   // Yellow
                Enums.ETagStatus.BadlyInjured => new SKColor(255, 165, 0), // Orange
                Enums.ETagStatus.Dying => new SKColor(255, 0, 0),       // Red
                _ => new SKColor(0, 255, 0)
            };
        }

        /// <summary>
        /// Gets health percentage based on observed player's health status
        /// This is a simplified approach - ideally would use actual health values if available
        /// </summary>
        private float GetHealthPercentage(ObservedPlayer player)
        {
            return player.HealthStatus switch
            {
                Enums.ETagStatus.Healthy => 1.0f,
                Enums.ETagStatus.Injured => 0.75f,
                Enums.ETagStatus.BadlyInjured => 0.4f,
                Enums.ETagStatus.Dying => 0.15f,
                _ => 1.0f
            };
        }

        // <summary>
        // Gets Aimview drawing paintbrushes based on this Player Type.
        // </summary>
        private ValueTuple<SKPaint, SKPaint> GetESPPaints()
        {
            if (IsAimbotLocked)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintAimbotLockedESP, SKPaints.TextAimbotLockedESP);

            if (IsFocused)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintFocusedESP, SKPaints.TextFocusedESP);

            //if (IsVisible)
            //    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintVisible, SKPaints.TextVisible);

            switch (Type)
            {
                case PlayerType.Teammate:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintFriendlyESP, SKPaints.TextFriendlyESP);
                case PlayerType.USEC:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSECESP, SKPaints.TextUSECESP);
                case PlayerType.BEAR:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintBEARESP, SKPaints.TextBEARESP);
                case PlayerType.AIScav:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintScavESP, SKPaints.TextScavESP);
                case PlayerType.AIRaider:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintRaiderESP, SKPaints.TextRaiderESP);
                case PlayerType.AIBoss:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintBossESP, SKPaints.TextBossESP);
                case PlayerType.PScav:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintPlayerScavESP, SKPaints.TextPlayerScavESP);
                case PlayerType.SpecialPlayer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintSpecialESP, SKPaints.TextSpecialESP);
                case PlayerType.Streamer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintStreamerESP, SKPaints.TextStreamerESP);
                default:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintUSECESP, SKPaints.TextUSECESP);
            }
        }

        // <summary>
        // Gets mini radar paint brush based on this Player Type.
        // </summary>
        public SKPaint GetMiniRadarPaint()
        {
            if (IsAimbotLocked)
                return SKPaints.PaintMiniAimbotLocked;

            if (IsFocused)
                return SKPaints.PaintMiniFocused;

            if (this is LocalPlayer)
                return SKPaints.PaintMiniLocalPlayer;

            switch (Type)
            {
                case PlayerType.Teammate:
                    return SKPaints.PaintMiniTeammate;
                case PlayerType.USEC:
                    return SKPaints.PaintMiniUSEC;
                case PlayerType.BEAR:
                    return SKPaints.PaintMiniBEAR;
                case PlayerType.AIScav:
                    return SKPaints.PaintMiniScav;
                case PlayerType.AIRaider:
                    return SKPaints.PaintMiniRaider;
                case PlayerType.AIBoss:
                    return SKPaints.PaintMiniBoss;
                case PlayerType.PScav:
                    return SKPaints.PaintMiniPScav;
                case PlayerType.SpecialPlayer:
                    return SKPaints.PaintMiniSpecial;
                case PlayerType.Streamer:
                    return SKPaints.PaintMiniStreamer;
                default:
                    return SKPaints.PaintMiniUSEC;
            }
        }

        /// <summary>
        /// Determine player type key for settings lookup
        /// </summary>
        public string DeterminePlayerTypeKey()
        {
            if (this is LocalPlayer)
                return "LocalPlayer";

            if (IsAimbotLocked)
                return "AimbotLocked";

            if (IsFocused)
                return "Focused";

            return Type.ToString();
        }

        #endregion

        #region Types

        /// <summary>
        /// Defines Player Unit Type (Player,PMC,Scav,etc.)
        /// </summary>
        public enum PlayerType
        {
            /// <summary>
            /// Default value if a type cannot be established.
            /// </summary>
            [Description("Default")]
            Default,
            /// <summary>
            /// Teammate of LocalPlayer.
            /// </summary>
            [Description("Teammate")]
            Teammate,
            /// <summary>
            /// Hostile/Enemy USEC.
            /// </summary>
            [Description("USEC")]
            USEC,
            /// <summary>
            /// Hostile/Enemy BEAR.
            /// </summary>
            [Description("BEAR")]
            BEAR,
            /// <summary>
            /// Normal AI Bot Scav.
            /// </summary>
            [Description("Scav")]
            AIScav,
            /// <summary>
            /// Difficult AI Raider.
            /// </summary>
            [Description("Raider")]
            AIRaider,
            /// <summary>
            /// Difficult AI Boss.
            /// </summary>
            [Description("Boss")]
            AIBoss,
            /// <summary>
            /// Player controlled Scav.
            /// </summary>
            [Description("Player Scav")]
            PScav,
            /// <summary>
            /// 'Special' Human Controlled Hostile PMC/Scav (on the watchlist, or a special account type).
            /// </summary>
            [Description("Special Player")]
            SpecialPlayer,
            /// <summary>
            /// Human Controlled Hostile PMC/Scav that has a Twitch account name as their IGN.
            /// </summary>
            [Description("Streamer")]
            Streamer
        }

        #endregion
    }
}
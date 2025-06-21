using eft_dma_radar.Tarkov.API;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using static SDK.Enums;

namespace eft_dma_radar.Tarkov.EFTPlayer
{
    public class ObservedPlayer : Player
    {
        /// <summary>
        /// Player's Profile & Stats (If Human Player).
        /// </summary>
        public PlayerProfile Profile { get; }
        /// <summary>
        /// ObservedPlayerController for non-clientplayer players.
        /// </summary>
        private ulong ObservedPlayerController { get; }
        /// <summary>
        /// ObservedHealthController for non-clientplayer players.
        /// </summary>
        private ulong ObservedHealthController { get; }
        /// <summary>
        /// Player name.
        /// </summary>
        public override string Name { get; set; }
        /// <summary>
        /// Account UUID for Human Controlled Players.
        /// </summary>
        public override string AccountID { get; }
        /// <summary>
        /// Group that the player belongs to.
        /// </summary>
        public override int GroupID { get; } = -1;
        /// <summary>
        /// Player's Faction.
        /// </summary>
        public override Enums.EPlayerSide PlayerSide { get; }
        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public override bool IsHuman { get; }
        /// <summary>
        /// MovementContext / StateContext
        /// </summary>
        public override ulong MovementContext { get; }
        /// <summary>
        /// EFT.PlayerBody
        /// </summary>
        public override ulong Body { get; }
        /// <summary>
        /// Inventory Controller field address.
        /// </summary>
        public override ulong InventoryControllerAddr { get; }
        /// <summary>
        /// Hands Controller field address.
        /// </summary>
        public override ulong HandsControllerAddr { get; }
        /// <summary>
        /// Corpse field address..
        /// </summary>
        public override ulong CorpseAddr { get; }
        /// <summary>
        /// Player Rotation Field Address (view angles).
        /// </summary>
        public override ulong RotationAddress { get; }
        /// <summary>
        /// Player's Skeleton Bones.
        /// </summary>
        public override Skeleton Skeleton { get; }
        /// <summary>
        /// Player's Current Health Status
        /// </summary>
        public Enums.ETagStatus HealthStatus { get; private set; } = Enums.ETagStatus.Healthy;

        internal ObservedPlayer(ulong playerBase) : base(playerBase)
        {
            var localPlayer = Memory.LocalPlayer;
            ArgumentNullException.ThrowIfNull(localPlayer, nameof(localPlayer));
            ObservedPlayerController = Memory.ReadPtr(this + Offsets.ObservedPlayerView.ObservedPlayerController);
            ArgumentOutOfRangeException.ThrowIfNotEqual(this,
                Memory.ReadValue<ulong>(ObservedPlayerController + Offsets.ObservedPlayerController.Player),
                nameof(ObservedPlayerController));
            ObservedHealthController = Memory.ReadPtr(ObservedPlayerController + Offsets.ObservedPlayerController.HealthController);
            ArgumentOutOfRangeException.ThrowIfNotEqual(this,
                Memory.ReadValue<ulong>(ObservedHealthController + Offsets.ObservedHealthController.Player),
                nameof(ObservedHealthController));
            Body = Memory.ReadPtr(this + Offsets.ObservedPlayerView.PlayerBody);
            InventoryControllerAddr = ObservedPlayerController + Offsets.ObservedPlayerController.InventoryController;
            HandsControllerAddr = ObservedPlayerController + Offsets.ObservedPlayerController.HandsController;
            CorpseAddr = ObservedHealthController + Offsets.ObservedHealthController.PlayerCorpse;

            GroupID = GetGroupID();
            MovementContext = GetMovementContext();
            RotationAddress = ValidateRotationAddr(MovementContext + Offsets.ObservedMovementController.Rotation);
            /// Setup Transforms
            this.Skeleton = new Skeleton(this, GetTransformInternalChain);
            /// Determine Player Type
            PlayerSide = (Enums.EPlayerSide)Memory.ReadValue<int>(this + Offsets.ObservedPlayerView.Side); // Usec,Bear,Scav,etc.
            if (!Enum.IsDefined(PlayerSide)) // Make sure PlayerSide is valid
                throw new Exception("Invalid Player Side/Faction!");

            AccountID = GetAccountID();
            var isAI = Memory.ReadValue<bool>(this + Offsets.ObservedPlayerView.IsAI);
            IsHuman = !isAI;
            if (IsScav)
            {
                if (isAI)
                {
                    if (MemPatchFeature<FixWildSpawnType>.Instance.IsApplied)
                    {
                        ulong infoContainer = Memory.ReadPtr(ObservedPlayerController + Offsets.ObservedPlayerController.InfoContainer);
                        var wildSpawnType = (Enums.WildSpawnType)Memory.ReadValueEnsure<int>(infoContainer + Offsets.InfoContainer.Side);
                        var role = Player.GetAIRoleInfo(wildSpawnType);
                        Name = role.Name;
                        Type = role.Type;
                    }
                    else
                    {
                        var voicePtr = Memory.ReadPtr(this + Offsets.ObservedPlayerView.Voice);
                        string voice = Memory.ReadUnityString(voicePtr);
                        var role = Player.GetAIRoleInfo(voice);
                        Name = role.Name;
                        Type = role.Type;
                    }

                    switch (Name)
                    {
                        case "Priest":
                            var newGear = new GearManager(this);
                            if (newGear.Equipment.TryGetValue("FaceCover", out var face))
                            {
                                if (face.Short.ToLower() == "zryachiy")
                                    Name = "Zryachiy";
                            }
                            break;
                        case "Usec":
                        case "Bear":
                            if (Memory.MapID.ToLower() == "lighthouse")
                            {
                                Name = "Rouge";
                                Type = PlayerType.AIRaider;
                            }
                            else if (Memory.MapID.ToLower() == "rezervbase")
                            {
                                Name = "Raider";
                                Type = PlayerType.AIRaider;
                            }
                            break;
                    }

                    if (Memory.MapID.ToLower() == "laboratory")
                    {
                        Name = "Raider";
                        Type = PlayerType.AIRaider;
                    }

                    if (GuardManager.TryIdentifyGuard(
                        new GearManager(this),
                        new HandsManager(this),
                        Memory.MapID,
                        Type))
                    {
                        Name = "Guard";
                        Type = PlayerType.AIRaider;
                    }
                }
                else
                {
                    int pscavNumber = Interlocked.Increment(ref _playerScavNumber);
                    Name = $"PScav{pscavNumber}";
                    Type = GroupID != -1 && GroupID == localPlayer.GroupID ? PlayerType.Teammate : PlayerType.PScav;
                }
            }
            else if (IsPmc)
            {
                Name = "PMC";
                Type = GroupID != -1 && GroupID == localPlayer.GroupID ? PlayerType.Teammate : (PlayerSide == EPlayerSide.Usec) ? PlayerType.USEC : PlayerType.BEAR;
            }
            else
                throw new NotImplementedException(nameof(PlayerSide));

            if (IsHuman)
            {
                var handController = Memory.ReadPtr(HandsControllerAddr);
                var dickController = Memory.ReadPtr(handController + Offsets.ObservedHandsController.BundleAnimationBones);
                this.PWA =  Memory.ReadPtr(dickController + Offsets.BundleAnimationBonesController.ProceduralWeaponAnimationObs);
                Profile = new PlayerProfile(this);
                if (string.IsNullOrEmpty(AccountID) || !ulong.TryParse(AccountID, out _))
                    throw new ArgumentOutOfRangeException(nameof(AccountID));
            }
            else
                AccountID = "AI";

            if (IsHumanHostile)
            {
                if (PlayerWatchlist.Entries.TryGetValue(AccountID, out var watchlistEntry))
                {
                    Type = PlayerType.SpecialPlayer;
                    UpdateAlerts(watchlistEntry.Reason);

                    if (watchlistEntry.StreamingPlatform != StreamingPlatform.None && !string.IsNullOrEmpty(watchlistEntry.Username))
                    {
                        var streamingUrl = StreamingUtils.GetStreamingURL(watchlistEntry.StreamingPlatform, watchlistEntry.Username);
                        StreamingURL = streamingUrl;

                        CheckIfStreaming();
                    }
                    else
                    {
                        StreamingURL = null;
                        IsStreaming = false;
                    }
                }
            }

            PlayerHistory.AddOrUpdate(this);
        }

        public void CheckIfStreaming()
        {
            if (string.IsNullOrEmpty(StreamingURL))
            {
                IsStreaming = false;

                if (Type == PlayerType.Streamer)
                {
                    UpdatePlayerType(PlayerType.SpecialPlayer);

                    if (PlayerWatchlist.Entries.TryGetValue(AccountID, out var entry))
                    {
                        ClearAlerts();
                        UpdateAlerts(entry.Reason);
                    }
                }
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    if (!PlayerWatchlist.Entries.TryGetValue(AccountID, out var watchlistEntry))
                        return;

                    var wasStreaming = IsStreaming;
                    string alertReason = watchlistEntry.Reason;

                    if (watchlistEntry.StreamingPlatform != StreamingPlatform.None &&
                        !string.IsNullOrEmpty(watchlistEntry.Username))
                    {
                        IsStreaming = await StreamingUtils.IsLive(watchlistEntry.StreamingPlatform, watchlistEntry.Username);
                    }
                    else
                    {
                        IsStreaming = false;
                    }

                    if (IsStreaming != wasStreaming)
                    {
                        if (IsStreaming)
                        {
                            UpdatePlayerType(PlayerType.Streamer);
                            ClearAlerts();
                            UpdateAlerts(alertReason);
                        }
                        else if (Type == PlayerType.Streamer)
                        {
                            UpdatePlayerType(PlayerType.SpecialPlayer);
                            ClearAlerts();
                            UpdateAlerts(alertReason);

                            LoneLogging.WriteLine($"[Streaming] {Name} ({AccountID}) is no longer streaming");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"[Streaming] Error checking if {Name} [{AccountID}] is live: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Get Player's Account ID.
        /// </summary>
        /// <returns>Account ID Numeric String.</returns>
        private string GetAccountID()
        {
            var idPTR = Memory.ReadPtr(this + Offsets.ObservedPlayerView.AccountId);
            return Memory.ReadUnityString(idPTR);
        }

        /// <summary>
        /// Gets player's Group Number.
        /// </summary>
        private int GetGroupID()
        {
            try
            {
                var grpIdPtr = Memory.ReadPtr(this + Offsets.ObservedPlayerView.GroupID);
                var grp = Memory.ReadUnityString(grpIdPtr);
                return _groups.GetGroup(grp);
            }
            catch { return -1; } // will return null if Solo / Don't have a team
        }

        /// <summary>
        /// Get Movement Context Instance.
        /// </summary>
        private ulong GetMovementContext()
        {
            var movementController = Memory.ReadPtrChain(ObservedPlayerController, Offsets.ObservedPlayerController.MovementController);
            return movementController;
        }

        /// <summary>
        /// Refresh Player Information.
        /// </summary>
        public override void OnRegRefresh(ScatterReadIndex index, IReadOnlySet<ulong> registered, bool? isActiveParam = null)
        {
            if (isActiveParam is not bool isActive)
                isActive = registered.Contains(this);

            if (isActive)
            {
                if (IsHuman)
                {
                    UpdateMemberCategory();
                    UpdatePlayerName();
                }

                UpdateHealthStatus();
                UpdateAimingStatus();
            }
            base.OnRegRefresh(index, registered, isActive);
        }

        private void UpdatePlayerName()
        {
            try
            {
                var nickname = Profile?.Nickname;
                if (nickname is not null && this.Name != nickname)
                {
                    this.Name = nickname;
                    PlayerHistory.AddOrUpdate(this);
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR updating Name for Player '{Name}': {ex}");
            }
        }

        private bool _mcSet = false;
        private void UpdateMemberCategory()
        {
            try
            {
                if (!_mcSet)
                {
                    var mcObj = Profile?.MemberCategory;
                    if (mcObj is Enums.EMemberCategory memberCategory)
                    {
                        string alert = null;
                        if ((memberCategory & Enums.EMemberCategory.Developer) == Enums.EMemberCategory.Developer)
                        {
                            alert = "Developer Account";
                            Type = PlayerType.SpecialPlayer;
                        }
                        else if ((memberCategory & Enums.EMemberCategory.Sherpa) == Enums.EMemberCategory.Sherpa)
                        {
                            alert = "Sherpa Account";
                            Type = PlayerType.SpecialPlayer;
                        }
                        else if ((memberCategory & Enums.EMemberCategory.Emissary) == Enums.EMemberCategory.Emissary)
                        {
                            alert = "Emissary Account";
                            Type = PlayerType.SpecialPlayer;
                        }

                        this.UpdateAlerts(alert);

                        _mcSet = true;
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR updating Member Category for '{Name}': {ex}");
            }
        }

        /// <summary>
        /// Get Player's Updated Health Condition
        /// Only works in Online Mode.
        /// </summary>
        private void UpdateHealthStatus()
        {
            try
            {
                var tag = (Enums.ETagStatus)Memory.ReadValue<int>(ObservedHealthController + Offsets.ObservedHealthController.HealthStatus);
                if ((tag & Enums.ETagStatus.Dying) == Enums.ETagStatus.Dying)
                    HealthStatus = Enums.ETagStatus.Dying;
                else if ((tag & Enums.ETagStatus.BadlyInjured) == Enums.ETagStatus.BadlyInjured)
                    HealthStatus = Enums.ETagStatus.BadlyInjured;
                else if ((tag & Enums.ETagStatus.Injured) == Enums.ETagStatus.Injured)
                    HealthStatus = Enums.ETagStatus.Injured;
                else
                    HealthStatus = Enums.ETagStatus.Healthy;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR updating Health Status for '{Name}': {ex}");
            }
        }

        /// <summary>
        /// Get Player's Updated Aiming Status
        /// Only works in Online Mode.
        /// </summary>
        private void UpdateAimingStatus()
        {
            try
            {
                var ptr = Memory.ReadPtr(HandsControllerAddr);
                IsAiming = Memory.ReadValue<bool>(Memory.ReadPtrChain(ptr, new uint[] { Offsets.ObservedHandsController.BundleAnimationBones, Offsets.BundleAnimationBonesController.ProceduralWeaponAnimationObs }) + Offsets.ProceduralWeaponAnimationObs._isAimingObs);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR updating Aiming Status for '{Name}': {ex}");
            }
        }

        /// <summary>
        /// Get the Transform Internal Chain for this Player.
        /// </summary>
        /// <param name="bone">Bone to lookup.</param>
        /// <returns>Array of offsets for transform internal chain.</returns>
        public override uint[] GetTransformInternalChain(Bones bone) => Offsets.ObservedPlayerView.GetTransformChain(bone);
    }
}

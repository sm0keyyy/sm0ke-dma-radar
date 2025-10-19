using System.Collections.Frozen;
using eft_dma_shared.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;

using eft_dma_shared.Common.DMA;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;

namespace eft_dma_radar.Tarkov.Loot
{
    public sealed class LootManager
    {
        #region Fields/Properties/Constructor

        private readonly ulong _lgw;
        private readonly CancellationToken _ct;
        private readonly Lock _filterSync = new();

        /// <summary>
        /// All loot (unfiltered).
        /// </summary>
        public IReadOnlyList<LootItem> UnfilteredLoot { get; private set; }

        /// <summary>
        /// All loot (with filter applied).
        /// </summary>
        public IReadOnlyList<LootItem> FilteredLoot { get; private set; }

        /// <summary>
        /// All Static Loot Containers on the map.
        /// </summary>
        public IReadOnlyList<StaticLootContainer> StaticLootContainers { get; private set; }

        public LootManager(ulong localGameWorld, CancellationToken ct)
        {
            _lgw = localGameWorld;
            _ct = ct;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Force a filter refresh.
        /// Thread Safe.
        /// </summary>
        public void RefreshFilter()
        {
            if (_filterSync.TryEnter())
            {
                try
                {
                    var filter = LootFilterControl.Create();
                    FilteredLoot = UnfilteredLoot?
                        .Where(x => filter(x))
                        .OrderByDescending(x => x.Important)
                        .ThenByDescending(x => (Program.Config.QuestHelper.Enabled && x.IsQuestCondition))
                        .ThenByDescending(x => x.IsWishlisted)
                        .ThenByDescending(x => x.IsValuableLoot)
                        .ThenByDescending(x => x?.Price ?? 0)
                        .ToList();
                }
                catch { }
                finally
                {
                    _filterSync.Exit();
                }
            }
        }

        /// <summary>
        /// Refreshes loot, only call from a memory thread (Non-GUI).
        /// </summary>
        public void Refresh()
        {
            try
            {
                GetLoot();
                RefreshFilter();

                LootItem.CleanupNotificationHistory(UnfilteredLoot);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ERROR - Failed to refresh loot: {ex}");
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// OPTIMIZED: Get BSG IDs early, then only do expensive position updates for filtered items.
        /// UnfilteredLoot still contains ALL items, but filtered-out items get stale positions.
        /// </summary>
        private void GetLootOptimized()
        {
            var lootListAddr = Memory.ReadPtr(_lgw + Offsets.ClientLocalGameWorld.LootList);
            using var lootList = MemList<ulong>.Get(lootListAddr);
            var loot = new List<LootItem>(lootList.Count);
            var containers = new List<StaticLootContainer>(64);
            var deadPlayers = Memory.Players?
                .Where(x => x.Corpse is not null)?.ToList();

            // Create filter once upfront
            var filter = LootFilterControl.Create();

            // Track which items need full position updates (passing filter + corpses/containers)
            var itemsNeedingPositionUpdate = new HashSet<int>();

            using var map = ScatterReadMap.Get();
            var round1 = map.AddRound();
            var round2 = map.AddRound();
            var round3 = map.AddRound();
            var round4 = map.AddRound();
            var round5 = map.AddRound(); // Item/Container base pointers
            var round6 = map.AddRound(); // Item templates
            var round7 = map.AddRound(); // BSG IDs (EARLY!)
            var round8 = map.AddRound(); // Position updates (CONDITIONAL!)

            for (int ix = 0; ix < lootList.Count; ix++)
            {
                var i = ix;
                _ct.ThrowIfCancellationRequested();
                var lootBase = lootList[i];
                round1[i].AddEntry<MemPointer>(0, lootBase + ObjectClass.MonoBehaviourOffset);
                round1[i].AddEntry<MemPointer>(1, lootBase + ObjectClass.To_NamePtr[0]);

                round1[i].Callbacks += x1 =>
                {
                    if (x1.TryGetResult<MemPointer>(0, out var monoBehaviour) && x1.TryGetResult<MemPointer>(1, out var c1))
                    {
                        round2[i].AddEntry<MemPointer>(2, monoBehaviour + MonoBehaviour.ObjectClassOffset);
                        round2[i].AddEntry<MemPointer>(3, monoBehaviour + MonoBehaviour.GameObjectOffset);
                        round2[i].AddEntry<MemPointer>(4, c1 + ObjectClass.To_NamePtr[1]);

                        round2[i].Callbacks += x2 =>
                        {
                            if (x2.TryGetResult<MemPointer>(2, out var interactiveClass) &&
                                x2.TryGetResult<MemPointer>(3, out var gameObject) &&
                                x2.TryGetResult<MemPointer>(4, out var c2))
                            {
                                round3[i].AddEntry<MemPointer>(5, c2 + ObjectClass.To_NamePtr[2]);
                                round3[i].AddEntry<MemPointer>(6, gameObject + GameObject.ComponentsOffset);
                                round3[i].AddEntry<MemPointer>(7, gameObject + GameObject.NameOffset);

                                round3[i].Callbacks += x3 =>
                                {
                                    if (x3.TryGetResult<MemPointer>(5, out var classNamePtr) &&
                                        x3.TryGetResult<MemPointer>(6, out var components) &&
                                        x3.TryGetResult<MemPointer>(7, out var pGameObjectName))
                                    {
                                        round4[i].AddEntry<UTF8String>(8, classNamePtr, 64);
                                        round4[i].AddEntry<UTF8String>(9, pGameObjectName, 64);
                                        round4[i].AddEntry<MemPointer>(10, components + 0x8); // TransformInternal

                                        round4[i].Callbacks += x4 =>
                                        {
                                            if (x4.TryGetResult<UTF8String>(8, out var classNameUtf8) &&
                                                x4.TryGetResult<UTF8String>(9, out var objectNameUtf8) &&
                                                x4.TryGetResult<MemPointer>(10, out var transformInternal))
                                            {
                                                string className = classNameUtf8;
                                                string objectName = objectNameUtf8;

                                                var isCorpse = className.Contains("Corpse", StringComparison.OrdinalIgnoreCase);
                                                var isLooseLoot = className.Equals("ObservedLootItem", StringComparison.OrdinalIgnoreCase);
                                                var isContainer = className.Equals("LootableContainer", StringComparison.OrdinalIgnoreCase);
                                                var skipScript = objectName.Contains("script", StringComparison.OrdinalIgnoreCase);

                                                // Corpses/airdrops always need position updates
                                                if (isCorpse || (isContainer && objectName.Equals("loot_collider", StringComparison.OrdinalIgnoreCase)))
                                                {
                                                    itemsNeedingPositionUpdate.Add(i);
                                                }

                                                // Round 5-7: Get BSG ID EARLY to check filter
                                                if (!skipScript && (isLooseLoot || isContainer))
                                                {
                                                    if (isLooseLoot)
                                                    {
                                                        round5[i].AddEntry<ulong>(11, interactiveClass + Offsets.InteractiveLootItem.Item);
                                                    }

                                                    if (isContainer && !objectName.Equals("loot_collider", StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        round5[i].AddEntry<ulong>(18, interactiveClass + Offsets.LootableContainer.ItemOwner);
                                                        round5[i].AddEntry<ulong>(19, interactiveClass + Offsets.LootableContainer.InteractingPlayer);
                                                    }

                                                    round5[i].Callbacks += x5 =>
                                                    {
                                                        if (isLooseLoot && x5.TryGetResult<ulong>(11, out var lootItemPtr) && lootItemPtr != 0)
                                                        {
                                                            round6[i].AddEntry<ulong>(13, lootItemPtr + Offsets.LootItem.Template);
                                                        }

                                                        if (isContainer && x5.TryGetResult<ulong>(18, out var containerOwnerPtr) && containerOwnerPtr != 0)
                                                        {
                                                            round6[i].AddEntry<ulong>(20, containerOwnerPtr + Offsets.LootableContainerItemOwner.RootItem);
                                                        }

                                                        round6[i].Callbacks += x6 =>
                                                        {
                                                            // Round 7: Get BSG ID early for filtering
                                                            bool needsPosition = false;

                                                            if (isContainer && x6.TryGetResult<ulong>(20, out var containerRootItem))
                                                            {
                                                                round7[i].AddEntry<ulong>(21, containerRootItem + Offsets.LootItem.Template);
                                                                round7[i].Callbacks += x7_container =>
                                                                {
                                                                    if (x7_container.TryGetResult<ulong>(21, out var containerTemplate))
                                                                    {
                                                                        var bsgIdPtr = Memory.ReadValue<Types.MongoID>(containerTemplate + Offsets.ItemTemplate._id);
                                                                        var bsgId = Memory.ReadUnityString(bsgIdPtr.StringID);

                                                                        // Check filter early!
                                                                        if (EftDataManager.AllItems.TryGetValue(bsgId, out var entry))
                                                                        {
                                                                            var tempContainer = new StaticLootContainer(bsgId, false);
                                                                            if (filter(tempContainer))
                                                                            {
                                                                                itemsNeedingPositionUpdate.Add(i);
                                                                                needsPosition = true;
                                                                            }
                                                                        }

                                                                        // Round 8: Position update (ONLY if needed)
                                                                        if (needsPosition || itemsNeedingPositionUpdate.Contains(i))
                                                                        {
                                                                            x5.TryGetResult<ulong>(19, out var interactingPlayer);
                                                                            bool containerOpened = interactingPlayer != 0;

                                                                            map.CompletionCallbacks += () =>
                                                                            {
                                                                                var pos = new UnityTransform(transformInternal, true).UpdatePosition();
                                                                                containers.Add(new StaticLootContainer(bsgId, containerOpened)
                                                                                {
                                                                                    Position = pos,
                                                                                    InteractiveClass = interactiveClass,
                                                                                    GameObject = gameObject
                                                                                });
                                                                            };
                                                                        }
                                                                        else
                                                                        {
                                                                            // Still add to list, but with zero position (stale data acceptable)
                                                                            x5.TryGetResult<ulong>(19, out var interactingPlayer);
                                                                            bool containerOpened = interactingPlayer != 0;

                                                                            containers.Add(new StaticLootContainer(bsgId, containerOpened)
                                                                            {
                                                                                Position = Vector3.Zero,
                                                                                InteractiveClass = interactiveClass,
                                                                                GameObject = gameObject
                                                                            });
                                                                        }
                                                                    }
                                                                };
                                                            }

                                                            if (isLooseLoot && x6.TryGetResult<ulong>(13, out var lootTemplate))
                                                            {
                                                                round7[i].AddEntry<bool>(16, lootTemplate + Offsets.ItemTemplate.QuestItem);
                                                                round7[i].AddEntry<Types.MongoID>(17, lootTemplate + Offsets.ItemTemplate._id);

                                                                round7[i].Callbacks += x7_loot =>
                                                                {
                                                                    if (x7_loot.TryGetResult<bool>(16, out var isQuestItem) &&
                                                                        x7_loot.TryGetResult<Types.MongoID>(17, out var bsgIdPtr))
                                                                    {
                                                                        var bsgId = Memory.ReadUnityString(bsgIdPtr.StringID);

                                                                        // Check filter early!
                                                                        bool passesFilter = false;
                                                                        if (EftDataManager.AllItems.TryGetValue(bsgId, out var entry))
                                                                        {
                                                                            var tempItem = new LootItem(entry) { Position = Vector3.Zero };
                                                                            passesFilter = filter(tempItem);
                                                                        }

                                                                        if (passesFilter || isQuestItem)
                                                                        {
                                                                            itemsNeedingPositionUpdate.Add(i);
                                                                        }

                                                                        // Round 8: Position update (ONLY if passes filter)
                                                                        if (passesFilter || isQuestItem || itemsNeedingPositionUpdate.Contains(i))
                                                                        {
                                                                            map.CompletionCallbacks += () =>
                                                                            {
                                                                                var pos = new UnityTransform(transformInternal, true).UpdatePosition();

                                                                                if (isQuestItem)
                                                                                {
                                                                                    if (EftDataManager.AllItems.TryGetValue(bsgId, out var e))
                                                                                    {
                                                                                        loot.Add(new QuestItem(e) { Position = pos, InteractiveClass = interactiveClass });
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        var shortNamePtr = Memory.ReadPtr(lootTemplate + Offsets.ItemTemplate.ShortName);
                                                                                        var shortName = Memory.ReadUnityString(shortNamePtr)?.Trim();
                                                                                        loot.Add(new QuestItem(bsgId, $"Q_{shortName ?? "Item"}") { Position = pos, InteractiveClass = interactiveClass });
                                                                                    }
                                                                                }
                                                                                else
                                                                                {
                                                                                    if (EftDataManager.AllItems.TryGetValue(bsgId, out var e))
                                                                                    {
                                                                                        loot.Add(new LootItem(e) { Position = pos, InteractiveClass = interactiveClass });
                                                                                    }
                                                                                }
                                                                            };
                                                                        }
                                                                        else
                                                                        {
                                                                            // Still add to UnfilteredLoot, but with zero position (stale)
                                                                            if (EftDataManager.AllItems.TryGetValue(bsgId, out var e))
                                                                            {
                                                                                loot.Add(new LootItem(e) { Position = Vector3.Zero, InteractiveClass = interactiveClass });
                                                                            }
                                                                        }
                                                                    }
                                                                };
                                                            }
                                                        };
                                                    };
                                                }
                                                else
                                                {
                                                    // Corpses and airdrops
                                                    map.CompletionCallbacks += () =>
                                                    {
                                                        _ct.ThrowIfCancellationRequested();
                                                        try
                                                        {
                                                            ProcessLootIndex(loot, containers, deadPlayers,
                                                                interactiveClass, objectName,
                                                                transformInternal, className, gameObject);
                                                        }
                                                        catch { }
                                                    };
                                                }
                                            }
                                        };
                                    }
                                };
                            }
                        };
                    }
                };
            }

            map.Execute();
            this.UnfilteredLoot = loot;
            this.StaticLootContainers = containers;
        }

        /// <summary>
        /// Updates referenced Loot List with fresh values (ORIGINAL - kept as backup).
        /// </summary>
        private void GetLoot()
        {
            var lootListAddr = Memory.ReadPtr(_lgw + Offsets.ClientLocalGameWorld.LootList);
            using var lootList = MemList<ulong>.Get(lootListAddr);
            var loot = new List<LootItem>(lootList.Count);
            var containers = new List<StaticLootContainer>(64);
            var deadPlayers = Memory.Players?
                .Where(x => x.Corpse is not null)?.ToList();
            using var map = ScatterReadMap.Get();
            var round1 = map.AddRound();
            var round2 = map.AddRound();
            var round3 = map.AddRound();
            var round4 = map.AddRound();
            var round5 = map.AddRound(); // Item/Container base pointers
            var round6 = map.AddRound(); // Item templates and metadata
            var round7 = map.AddRound(); // BSG IDs and flags
            var round8 = map.AddRound(); // BSG ID strings
            for (int ix = 0; ix < lootList.Count; ix++)
            {
                var i = ix;
                _ct.ThrowIfCancellationRequested();
                var lootBase = lootList[i];
                round1[i].AddEntry<MemPointer>(0, lootBase + ObjectClass.MonoBehaviourOffset); // MonoBehaviour
                round1[i].AddEntry<MemPointer>(1, lootBase + ObjectClass.To_NamePtr[0]); // C1
                round1[i].Callbacks += x1 =>
                {
                    if (x1.TryGetResult<MemPointer>(0, out var monoBehaviour) && x1.TryGetResult<MemPointer>(1, out var c1))
                    {
                        round2[i].AddEntry<MemPointer>(2,
                            monoBehaviour + MonoBehaviour.ObjectClassOffset); // InteractiveClass
                        round2[i].AddEntry<MemPointer>(3, monoBehaviour + MonoBehaviour.GameObjectOffset); // GameObject
                        round2[i].AddEntry<MemPointer>(4, c1 + ObjectClass.To_NamePtr[1]); // C2
                        round2[i].Callbacks += x2 =>
                        {
                            if (x2.TryGetResult<MemPointer>(2, out var interactiveClass) &&
                                x2.TryGetResult<MemPointer>(3, out var gameObject) &&
                                x2.TryGetResult<MemPointer>(4, out var c2))
                            {
                                round3[i].AddEntry<MemPointer>(5, c2 + ObjectClass.To_NamePtr[2]); // ClassNamePtr
                                round3[i].AddEntry<MemPointer>(6, gameObject + GameObject.ComponentsOffset); // Components
                                round3[i].AddEntry<MemPointer>(7, gameObject + GameObject.NameOffset); // PGameObjectName
                                round3[i].Callbacks += x3 =>
                                {
                                    if (x3.TryGetResult<MemPointer>(5, out var classNamePtr) &&
                                        x3.TryGetResult<MemPointer>(6, out var components)
                                        && x3.TryGetResult<MemPointer>(7, out var pGameObjectName))
                                    {
                                        round4[i].AddEntry<UTF8String>(8, classNamePtr, 64); // ClassName
                                        round4[i].AddEntry<UTF8String>(9, pGameObjectName, 64); // ObjectName
                                        round4[i].AddEntry<MemPointer>(10,
                                            components + 0x8); // T1
                                        round4[i].Callbacks += x4 =>
                                        {
                                            if (x4.TryGetResult<UTF8String>(8, out var classNameUtf8) &&
                                                x4.TryGetResult<UTF8String>(9, out var objectNameUtf8) &&
                                                x4.TryGetResult<MemPointer>(10, out var transformInternal))
                                            {
                                                // Convert UTF8String to string for comparisons (use implicit cast, not .ToString()!)
                                                string className = classNameUtf8;
                                                string objectName = objectNameUtf8;

                                                // Determine loot type early to batch the right reads
                                                var isCorpse = className.Contains("Corpse", StringComparison.OrdinalIgnoreCase);
                                                var isLooseLoot = className.Equals("ObservedLootItem", StringComparison.OrdinalIgnoreCase);
                                                var isContainer = className.Equals("LootableContainer", StringComparison.OrdinalIgnoreCase);
                                                var skipScript = objectName.Contains("script", StringComparison.OrdinalIgnoreCase);

                                                // Round 5: Read base item/container pointers
                                                if (!skipScript && (isLooseLoot || isContainer))
                                                {
                                                    if (isLooseLoot)
                                                    {
                                                        // Loose loot: read Item pointer (index 11)
                                                        round5[i].AddEntry<ulong>(11, interactiveClass + Offsets.InteractiveLootItem.Item);
                                                    }

                                                    if (isContainer && !objectName.Equals("loot_collider", StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        // Container: read ItemOwner pointer (index 18 - unique!)
                                                        round5[i].AddEntry<ulong>(18, interactiveClass + Offsets.LootableContainer.ItemOwner);
                                                        // Also read InteractingPlayer for opened status (index 19)
                                                        round5[i].AddEntry<ulong>(19, interactiveClass + Offsets.LootableContainer.InteractingPlayer);
                                                    }

                                                    round5[i].Callbacks += x5 =>
                                                    {
                                                        // Round 6: Read item template pointer
                                                        // Process loose loot independently
                                                        if (isLooseLoot && x5.TryGetResult<ulong>(11, out var lootItemPtr) && lootItemPtr != 0)
                                                        {
                                                            // Loose loot: item -> template (index 13)
                                                            round6[i].AddEntry<ulong>(13, lootItemPtr + Offsets.LootItem.Template);
                                                        }

                                                        // Process containers independently (not else-if!)
                                                        if (isContainer && x5.TryGetResult<ulong>(18, out var containerOwnerPtr) && containerOwnerPtr != 0)
                                                        {
                                                            // Container: itemOwner -> rootItem -> template (index 20 - unique!)
                                                            round6[i].AddEntry<ulong>(20, containerOwnerPtr + Offsets.LootableContainerItemOwner.RootItem);
                                                        }

                                                        round6[i].Callbacks += x6 =>
                                                        {
                                                            // Process containers independently
                                                            if (isContainer && x6.TryGetResult<ulong>(20, out var containerRootItem))
                                                            {
                                                                // Container: rootItem -> template (need extra hop, index 21)
                                                                round7[i].AddEntry<ulong>(21, containerRootItem + Offsets.LootItem.Template);
                                                                round7[i].Callbacks += x7_container =>
                                                                {
                                                                    if (x7_container.TryGetResult<ulong>(21, out var containerTemplate))
                                                                    {
                                                                        // Round 8: Read BSG ID for container (index 22)
                                                                        round8[i].AddEntry<Types.MongoID>(22, containerTemplate + Offsets.ItemTemplate._id);
                                                                        round8[i].Callbacks += x8_container =>
                                                                        {
                                                                            if (x8_container.TryGetResult<Types.MongoID>(22, out var bsgIdPtr))
                                                                            {
                                                                                var bsgId = Memory.ReadUnityString(bsgIdPtr.StringID);
                                                                                x5.TryGetResult<ulong>(19, out var interactingPlayer);
                                                                                bool containerOpened = interactingPlayer != 0;

                                                                                map.CompletionCallbacks += () =>
                                                                                {
                                                                                    var pos = new UnityTransform(transformInternal, true).UpdatePosition();
                                                                                    containers.Add(new StaticLootContainer(bsgId, containerOpened)
                                                                                    {
                                                                                        Position = pos,
                                                                                        InteractiveClass = interactiveClass,
                                                                                        GameObject = gameObject
                                                                                    });
                                                                                };
                                                                            }
                                                                        };
                                                                    }
                                                                };
                                                            }

                                                            // Process loose loot independently (not else-if!)
                                                            if (isLooseLoot && x6.TryGetResult<ulong>(13, out var lootTemplate))
                                                            {
                                                                // Loose loot: lootTemplate is already the template address
                                                                // Round 7: Read quest item flag and BSG ID for loose loot (indices 16, 17)
                                                                round7[i].AddEntry<bool>(16, lootTemplate + Offsets.ItemTemplate.QuestItem);
                                                                round7[i].AddEntry<Types.MongoID>(17, lootTemplate + Offsets.ItemTemplate._id);

                                                                round7[i].Callbacks += x7_loot =>
                                                                {
                                                                    if (x7_loot.TryGetResult<bool>(16, out var isQuestItem) &&
                                                                        x7_loot.TryGetResult<Types.MongoID>(17, out var bsgIdPtr))
                                                                    {
                                                                        var bsgId = Memory.ReadUnityString(bsgIdPtr.StringID);

                                                                        map.CompletionCallbacks += () =>
                                                                        {
                                                                            var pos = new UnityTransform(transformInternal, true).UpdatePosition();

                                                                            if (isQuestItem)
                                                                            {
                                                                                QuestItem questItem;
                                                                                if (EftDataManager.AllItems.TryGetValue(bsgId, out var entry))
                                                                                {
                                                                                    questItem = new QuestItem(entry)
                                                                                    {
                                                                                        Position = pos,
                                                                                        InteractiveClass = interactiveClass
                                                                                    };
                                                                                }
                                                                                else
                                                                                {
                                                                                    // Fallback: read short name synchronously (rare case)
                                                                                    var shortNamePtr = Memory.ReadPtr(lootTemplate + Offsets.ItemTemplate.ShortName);
                                                                                    var shortName = Memory.ReadUnityString(shortNamePtr)?.Trim();
                                                                                    if (string.IsNullOrEmpty(shortName))
                                                                                        shortName = "Item";
                                                                                    questItem = new QuestItem(bsgId, $"Q_{shortName}")
                                                                                    {
                                                                                        Position = pos,
                                                                                        InteractiveClass = interactiveClass
                                                                                    };
                                                                                }
                                                                                loot.Add(questItem);
                                                                            }
                                                                            else // Regular Loose Loot Item
                                                                            {
                                                                                if (EftDataManager.AllItems.TryGetValue(bsgId, out var entry))
                                                                                {
                                                                                    loot.Add(new LootItem(entry)
                                                                                    {
                                                                                        Position = pos,
                                                                                        InteractiveClass = interactiveClass
                                                                                    });
                                                                                }
                                                                            }
                                                                        };
                                                                    }
                                                                };
                                                            }
                                                        };
                                                    };
                                                }
                                                else
                                                {
                                                    // Corpses and airdrops: process synchronously (no item reads needed)
                                                    map.CompletionCallbacks += () =>
                                                    {
                                                        _ct.ThrowIfCancellationRequested();
                                                        try
                                                        {
                                                            ProcessLootIndex(loot, containers, deadPlayers,
                                                                interactiveClass, objectName,
                                                                transformInternal, className, gameObject);
                                                        }
                                                        catch
                                                        {
                                                        }
                                                    };
                                                }
                                            }
                                        };
                                    }
                                };
                            }
                        };
                    }
                };
            }

            map.Execute(); // execute scatter read
            this.UnfilteredLoot = loot;
            this.StaticLootContainers = containers;
        }

        /// <summary>
        /// Process a single loot index for corpses and airdrops only.
        /// Loose loot and containers are now handled in scatter read chain for performance.
        /// </summary>
        private static void ProcessLootIndex(List<LootItem> loot, List<StaticLootContainer> containers, IReadOnlyList<Player> deadPlayers,
            ulong interactiveClass, string objectName, ulong transformInternal, string className, ulong gameObject)
        {
            var isCorpse = className.Contains("Corpse", StringComparison.OrdinalIgnoreCase);
            var isContainer = className.Equals("LootableContainer", StringComparison.OrdinalIgnoreCase);

            // Get Item Position
            var pos = new UnityTransform(transformInternal, true).UpdatePosition();

            if (isCorpse)
            {
                var player = deadPlayers?.FirstOrDefault(x => x.Corpse == interactiveClass);
                bool isPMC = player?.IsPmc ?? true;
                var corpseLoot = new List<LootItem>();
                GetCorpseLoot(interactiveClass, corpseLoot, isPMC);
                var corpse = new LootCorpse(corpseLoot)
                {
                    Position = pos,
                    PlayerObject = player
                };
                loot.Add(corpse);
                if (player is not null)
                {
                    player.LootObject = corpse;
                }
            }
            else if (isContainer && objectName.Equals("loot_collider", StringComparison.OrdinalIgnoreCase))
            {
                // Airdrops only (regular containers handled in scatter read chain)
                loot.Add(new LootAirdrop()
                {
                    Position = pos,
                    InteractiveClass = interactiveClass
                });
            }
        }
        private static readonly FrozenSet<string> _skipSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SecuredContainer", "Dogtag", "Compass", "Eyewear", "ArmBand"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Recurse slots for gear.
        /// </summary>
        private static void GetItemsInSlots(ulong slotsPtr, List<LootItem> loot, bool isPMC)
        {
            var slotDict = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            using var slots = MemArray<ulong>.Get(slotsPtr);

            foreach (var slot in slots)
            {
                var namePtr = Memory.ReadPtr(slot + Offsets.Slot.ID);
                var name = Memory.ReadUnityString(namePtr);
                if (!_skipSlots.Contains(name))
                    slotDict.TryAdd(name, slot);
            }

            foreach (var slot in slotDict)
            {
                try
                {
                    if (isPMC && slot.Key == "Scabbard")
                        continue;
                    var containedItem = Memory.ReadPtr(slot.Value + Offsets.Slot.ContainedItem);
                    var inventorytemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                    var idPtr = Memory.ReadValue<Types.MongoID>(inventorytemplate + Offsets.ItemTemplate._id);
                    var id = Memory.ReadUnityString(idPtr.StringID);
                    if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                        loot.Add(new LootItem(entry));
                    var childGrids = Memory.ReadPtr(containedItem + Offsets.LootItemMod.Grids);
                    GetItemsInGrid(childGrids, loot); // Recurse the grids (if possible)
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Gets all loot on a corpse.
        /// </summary>
        private static void GetCorpseLoot(ulong lootInteractiveClass, List<LootItem> loot, bool isPMC)
        {
            var itemBase = Memory.ReadPtr(lootInteractiveClass + Offsets.InteractiveLootItem.Item);
            var slots = Memory.ReadPtr(itemBase + Offsets.LootItemMod.Slots);
            try
            {
                GetItemsInSlots(slots, loot, isPMC);
            }
            catch
            {
            }
        }

        #endregion

        #region Static Public Methods

        ///This method recursively searches grids. Grids work as follows:
        ///Take a Groundcache which holds a Blackrock which holds a pistol.
        ///The Groundcache will have 1 grid array, this method searches for whats inside that grid.
        ///Then it finds a Blackrock. This method then invokes itself recursively for the Blackrock.
        ///The Blackrock has 11 grid arrays (not to be confused with slots!! - a grid array contains slots. Look at the blackrock and you'll see it has 20 slots but 11 grids).
        ///In one of those grid arrays is a pistol. This method would recursively search through each item it finds
        ///To Do: add slot logic, so we can recursively search through the pistols slots...maybe it has a high value scope or something.
        public static void GetItemsInGrid(ulong gridsArrayPtr, List<LootItem> containerLoot,
            int recurseDepth = 0)
        {
            ArgumentOutOfRangeException.ThrowIfZero(gridsArrayPtr, nameof(gridsArrayPtr));
            if (recurseDepth++ > 3) return; // Only recurse 3 layers deep (this should be plenty)
            using var gridsArray = MemArray<ulong>.Get(gridsArrayPtr);

            try
            {
                // Check all sections of the container
                foreach (var grid in gridsArray)
                {
                    var gridEnumerableClass =
                        Memory.ReadPtr(grid +
                                       Offsets.Grids
                                           .ContainedItems); // -.GClass178A->gClass1797_0x40 // Offset: 0x0040 (Type: -.GClass1797)

                    var itemListPtr =
                        Memory.ReadPtr(gridEnumerableClass +
                                       Offsets.GridContainedItems.Items); // -.GClass1797->list_0x18 // Offset: 0x0018 (Type: System.Collections.Generic.List<Item>)
                    using var itemList = MemList<ulong>.Get(itemListPtr);

                    foreach (var childItem in itemList)
                        try
                        {
                            var childItemTemplate =
                                Memory.ReadPtr(childItem +
                                               Offsets.LootItem
                                                   .Template); // EFT.InventoryLogic.Item->_template // Offset: 0x0038 (Type: EFT.InventoryLogic.ItemTemplate)
                            var childItemIdPtr = Memory.ReadValue<Types.MongoID>(childItemTemplate + Offsets.ItemTemplate._id);
                            var childItemIdStr = Memory.ReadUnityString(childItemIdPtr.StringID);
                            if (EftDataManager.AllItems.TryGetValue(childItemIdStr, out var entry))
                                containerLoot.Add(new LootItem(entry));

                            // Check to see if the child item has children
                            // Don't throw on nullPtr since GetItemsInGrid needs to record the current item still
                            var childGridsArrayPtr = Memory.ReadValue<ulong>(childItem + Offsets.LootItemMod.Grids); // Pointer
                            GetItemsInGrid(childGridsArrayPtr, containerLoot,
                                recurseDepth); // Recursively add children to the entity
                        }
                        catch { }
                }
            }
            catch { }
        }
        #endregion
    }
}
using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class ShopManager {
    #region EntryId
    private int idCounter;

    /// <summary>
    /// Generates an EntryId unique to this specific manager instance.
    /// </summary>
    /// <returns>Returns a local EntryId</returns>
    private int NextEntryId() => Interlocked.Increment(ref idCounter);
    #endregion
    private readonly GameSession session;
    private readonly IDictionary<int, CharacterShopData> accountShopData;
    private readonly IDictionary<int, CharacterShopData> characterShopData;
    private readonly Dictionary<int, Shop> instancedShops;
    private readonly Dictionary<int, IDictionary<int, CharacterShopItemData>> characterShopItemData;
    private readonly Dictionary<int, IDictionary<int, CharacterShopItemData>> accountShopItemData;
    private readonly Dictionary<int, BuyBackItem> buyBackItems;
    private Shop? activeShop;
    private int shopNpcId;
    // The field npc whose talk opened the shop (null for a system shop). The client keeps such a shop only while
    // the talk is on (UIShopDialog::vfunc_72 closes it once MyPC::FUN_141527e60 is 0), and ends the talk once the
    // npc is gone or out of its talk range (UITalkManager::EndTalkIfNpcGone): only a forged client trades on.
    private FieldNpc? talkNpc;

    private readonly ILogger logger = Log.Logger.ForContext<ShopManager>();

    public ShopManager(GameSession session) {
        this.session = session;
        instancedShops = new Dictionary<int, Shop>();
        buyBackItems = new Dictionary<int, BuyBackItem>();
        using GameStorage.Request db = session.GameStorage.Context();

        accountShopData = db.GetCharacterShopData(session.AccountId);
        characterShopData = db.GetCharacterShopData(session.CharacterId);

        accountShopItemData = new Dictionary<int, IDictionary<int, CharacterShopItemData>>();
        ICollection<CharacterShopItemData> accountShopItemList = db.GetCharacterShopItemData(session.AccountId);
        foreach (CharacterShopItemData itemData in accountShopItemList) {
            if (accountShopItemData.TryGetValue(itemData.ShopId, out IDictionary<int, CharacterShopItemData>? itemDictionary)) {
                if (itemDictionary.ContainsKey(itemData.ShopItemId)) {
                    continue;
                }
                itemDictionary[itemData.ShopItemId] = itemData;
                continue;
            }
            accountShopItemData[itemData.ShopId] = new Dictionary<int, CharacterShopItemData> {
                [itemData.ShopItemId] = itemData,
            };
        }

        characterShopItemData = new Dictionary<int, IDictionary<int, CharacterShopItemData>>();
        ICollection<CharacterShopItemData> characterShopItemList = db.GetCharacterShopItemData(session.CharacterId);
        foreach (CharacterShopItemData itemData in characterShopItemList) {
            if (characterShopItemData.TryGetValue(itemData.ShopId, out IDictionary<int, CharacterShopItemData>? itemDictionary)) {
                if (itemDictionary.ContainsKey(itemData.ShopItemId)) {
                    continue;
                }
                itemDictionary[itemData.ShopItemId] = itemData;
                continue;
            }
            characterShopItemData[itemData.ShopId] = new Dictionary<int, CharacterShopItemData> {
                [itemData.ShopItemId] = itemData,
            };
        }
    }

    public void ClearActiveShop() {
        activeShop = null;
        shopNpcId = 0;
        talkNpc = null;
    }

    // Whether a shop is open: one opened by a talk is closed once its npc has left the player's field or the
    // player its talk range.
    [MemberNotNullWhen(true, nameof(activeShop))]
    private bool ShopOpen() {
        if (activeShop == null || talkNpc == null) {
            return activeShop != null;
        }
        bool stays = session.Field != null && session.Field.Npcs.TryGetValue(talkNpc.ObjectId, out FieldNpc? npc) && npc == talkNpc
            && talkNpc.InTalkRange(session.Player.Position, session.ServerTableMetadata.ConstantsTable.TalkableDistance);
        if (!stays) {
            logger.Debug("Shop {ShopId} closed: its npc {NpcId} is gone or out of talk range", activeShop.Id, talkNpc.Value.Id);
            ClearActiveShop();
            return false;
        }
        return true;
    }

    private bool TryGetShopData(int shopId, [NotNullWhen(true)] out CharacterShopData? data) {
        return accountShopData.TryGetValue(shopId, out data) || characterShopData.TryGetValue(shopId, out data);
    }

    private bool TryRemoveShopData(int shopId) {
        if (accountShopData.Remove(shopId)) {
            return true;
        }
        return characterShopData.Remove(shopId);
    }

    private bool TryGetShopItemData(int shopId, [NotNullWhen(true)] out IDictionary<int, CharacterShopItemData>? data) {
        return accountShopItemData.TryGetValue(shopId, out data) || characterShopItemData.TryGetValue(shopId, out data);
    }

    /// <summary>
    /// Load shop.
    /// </summary>
    /// <param name="shopId">Shop ID</param>
    /// <param name="npcId">Npc ID. Optional parameter. This only is used for displaying the NPC's name on the shop window top bar.</param>
    public void Load(int shopId, int npcId = 0, FieldNpc? fieldNpc = null) {
        if (!session.ServerTableMetadata.ShopTable.Entries.TryGetValue(shopId, out ShopMetadata? metadata)) {
            logger.Warning("Shop {Id} not found", shopId);
            return;
        }
        talkNpc = fieldNpc;

        if (metadata.EnableReset) {
            activeShop = GetInstancedShop(metadata);
        } else {
            activeShop = new Shop(metadata) {
                Items = GetShopItems(metadata),
            };
            // Shops without a reset still limit items with a stock count; the bought count never restocks.
            if (TryGetShopItemData(metadata.Id, out IDictionary<int, CharacterShopItemData>? stockData)) {
                foreach ((int shopItemId, CharacterShopItemData itemData) in stockData) {
                    if (activeShop.Items.TryGetValue(shopItemId, out ShopItem? shopItem) && shopItem.StockCount > 0) {
                        shopItem.StockPurchased = itemData.StockPurchased;
                    }
                }
            }
        }

        shopNpcId = npcId;

        session.Send(ShopPacket.Open(activeShop, shopNpcId));
        session.Send(ShopPacket.LoadItems(activeShop.Items.Values));
        if (!metadata.IsOnlySell) {
            session.Send(ShopPacket.BuyBackItemCount((short) buyBackItems.Count));
            if (buyBackItems.Count > 0) {
                session.Send(ShopPacket.LoadBuyBackItem(buyBackItems.Values.ToArray()));
            }
        }
    }

    private Shop GetInstancedShop(ShopMetadata metadata) {
        if (instancedShops.TryGetValue(metadata.Id, out Shop? instancedShop)) {
            // A shop on a restock schedule that ran out while it was cached is made anew below, as on a login
            // after that time. A shop with a fixed time in its data keeps its list (chosen here: its time does
            // not move, so it would be made anew on every opening).
            if (!ShopRules.RestockDue(metadata.RestockTime, instancedShop.RestockTime, DateTime.Now.ToEpochSeconds())) {
                return instancedShop;
            }
            instancedShops.Remove(metadata.Id);
        }

        if (!TryGetShopData(metadata.Id, out CharacterShopData? data)) {
            long restockTime = GetRestockTime(metadata);
            return CreateInstancedShop(metadata, CreateShopData(metadata, restockTime), restockTime);
        }

        if (data.RestockTime < DateTime.Now.ToEpochSeconds()) {
            using GameStorage.Request db = session.GameStorage.Context();
            if (!TryRemoveShopData(data.ShopId)) {
                throw new InvalidOperationException($"Failed to remove shop data {metadata.Id} for character {session.CharacterId}");
            }
            db.DeleteCharacterShopData(metadata.RestockData.AccountWide ? session.AccountId : session.CharacterId, metadata.Id);

            long restockTime = GetRestockTime(metadata);
            return CreateInstancedShop(metadata, CreateShopData(metadata, restockTime), restockTime);
        }

        // Assemble shop
        // The stored restock count too: the restock cost and the client's reset tooltip count it, and it read 0
        // here after a login.
        instancedShop = new Shop(metadata) {
            RestockTime = data.RestockTime,
            RestockCount = data.RestockCount,
        };

        // Until the restock the shop keeps the products it rolled, with their stored items and bought counts. Before,
        // a login rolled the list anew: a product of the new roll had no stored row, so its buys were not counted and
        // its stock never ran out, and the list changed on every login.
        if (TryGetShopItemData(instancedShop.Id, out IDictionary<int, CharacterShopItemData>? dataDictionary)) {
            instancedShop.Items = StoredShopItems(metadata, dataDictionary);
        } else {
            instancedShop.Items = GetShopItems(metadata);
            CreateShopItemData(instancedShop);
        }

        instancedShops[instancedShop.Id] = instancedShop;
        return instancedShop;
    }

    private Shop CreateInstancedShop(ShopMetadata metadata, CharacterShopData shopData, long restockTime) {
        var shop = new Shop(metadata);
        // First delete any existing shop item data
        long ownerId = metadata.RestockData.AccountWide ? session.AccountId : session.CharacterId;
        using GameStorage.Request db = session.GameStorage.Context();

        if (TryGetShopItemData(metadata.Id, out IDictionary<int, CharacterShopItemData>? shopItemDatas)) {
            foreach ((int shopItemId, CharacterShopItemData itemData) in shopItemDatas) {
                db.DeleteCharacterShopItemData(ownerId, itemData.ShopId, shopItemId);
            }
        }

        // Create new shop data
        shop.Items = GetShopItems(metadata);
        shop.RestockTime = restockTime;
        shop.RestockCount = shopData.RestockCount;
        CreateShopItemData(shop);
        instancedShops[shop.Id] = shop;
        return shop;
    }

    private SortedDictionary<int, ShopItem> GetShopItems(ShopMetadata metadata) {
        var items = new SortedDictionary<int, ShopItem>();

        if (!session.ServerTableMetadata.ShopItemTable.Entries.TryGetValue(metadata.Id, out Dictionary<int, ShopItemMetadata>? allShopItems)) {
            return new SortedDictionary<int, ShopItem>();
        }

        // A running NpcShopShowItem event keeps the shop to the items it names.
        IReadOnlySet<int> hidden = ShopShowItemRules.Hidden(session.ServerTableMetadata.GameEventTable.Entries.Values, metadata.Id, DateTime.Now);
        List<ShopItemMetadata> shopItemMetadatas = ShopShowItemRules.Filter(allShopItems.Values, hidden).ToList();

        if (metadata.EnableReset) {
            int minItemCount = metadata.RestockData.MinItemCount;
            if (shopItemMetadatas.Count < metadata.RestockData.MinItemCount) {
                minItemCount = shopItemMetadatas.Count;
            }

            int failSafe = 0;
            const int maxIterations = 10;

            while (items.Count < minItemCount && failSafe < maxIterations) {
                foreach (ShopItemMetadata shopItemMetadata in shopItemMetadatas) {
                    if (items.Count >= metadata.RestockData.MaxItemCount) {
                        break;
                    }

                    if (items.ContainsKey(shopItemMetadata.Id)) {
                        continue;
                    }

                    if (Random.Shared.Next(10000) > shopItemMetadata.Probability) {
                        continue;
                    }

                    Item? item = session.Field?.ItemDrop.CreateItem(shopItemMetadata.ItemId, shopItemMetadata.Rarity, shopItemMetadata.SellUnit);
                    if (item == null) {
                        continue;
                    }

                    var shopItem = new ShopItem(shopItemMetadata) {
                        Item = item,
                    };
                    items.Add(shopItemMetadata.Id, shopItem);
                }
                failSafe++;
            }
            if (failSafe >= maxIterations) {
                logger.Warning("GetShopItems: Fail safe triggered after {MaxIterations} iterations for shop {ShopId}", maxIterations, metadata.Id);
            }
        } else {
            foreach (ShopItemMetadata shopItemMetadata in shopItemMetadatas) {
                Item? item = session.Field?.ItemDrop.CreateItem(shopItemMetadata.ItemId, shopItemMetadata.Rarity, shopItemMetadata.SellUnit);
                if (item == null) {
                    continue;
                }

                var shopItem = new ShopItem(shopItemMetadata) {
                    Item = item,
                };
                items.Add(shopItemMetadata.Id, shopItem);
            }
        }

        return items;
    }

    // The stored products of a rolled shop that its table still has, and that a running NpcShopShowItem event does not
    // hide (as GetShopItems leaves them out).
    private SortedDictionary<int, ShopItem> StoredShopItems(ShopMetadata metadata, IDictionary<int, CharacterShopItemData> stored) {
        var items = new SortedDictionary<int, ShopItem>();
        if (!session.ServerTableMetadata.ShopItemTable.Entries.TryGetValue(metadata.Id, out Dictionary<int, ShopItemMetadata>? allShopItems)) {
            return items;
        }
        IReadOnlySet<int> hidden = ShopShowItemRules.Hidden(session.ServerTableMetadata.GameEventTable.Entries.Values, metadata.Id, DateTime.Now);
        HashSet<int> shown = ShopShowItemRules.Filter(allShopItems.Values, hidden).Select(product => product.Id).ToHashSet();
        foreach ((int shopItemId, CharacterShopItemData itemData) in stored) {
            if (!shown.Contains(shopItemId) || !allShopItems.TryGetValue(shopItemId, out ShopItemMetadata? product)) {
                continue;
            }
            items[shopItemId] = new ShopItem(product) {
                Item = itemData.Item,
                StockPurchased = itemData.StockPurchased,
            };
        }
        return items;
    }

    private CharacterShopData CreateShopData(ShopMetadata metadata, long restockTime) {
        if (!TryGetShopData(metadata.Id, out CharacterShopData? data)) {
            data = new CharacterShopData {
                Interval = metadata.RestockData.ResetType,
                RestockTime = restockTime,
                ShopId = metadata.Id,
            };

            if (metadata.RestockData.AccountWide) {
                accountShopData[metadata.Id] = data;
            } else {
                characterShopData[metadata.Id] = data;
            }
        }

        // We do not save shops with default reset type to database
        if (metadata.RestockData.ResetType != ResetType.Default) {
            using GameStorage.Request db = session.GameStorage.Context();
            long ownerId = metadata.RestockData.AccountWide ? session.AccountId : session.CharacterId;
            data = db.CreateCharacterShopData(ownerId, data);
            if (data == null) {
                throw new InvalidOperationException($"Failed to create shop data {metadata.Id} for character {session.CharacterId}");
            }
        }

        return data;
    }

    private IDictionary<int, CharacterShopItemData> CreateShopItemData(Shop shop) {
        var itemData = new Dictionary<int, CharacterShopItemData>();
        using GameStorage.Request db = session.GameStorage.Context();
        long ownerId = shop.RestockData.AccountWide ? session.AccountId : session.CharacterId;
        foreach ((int id, ShopItem item) in shop.Items) {
            var data = new CharacterShopItemData {
                ShopId = shop.Id,
                ShopItemId = id,
                Item = item.Item,
            };
            if (shop.RestockData.ResetType != ResetType.Default) {
                data = db.CreateCharacterShopItemData(ownerId, data);
                if (data == null) {
                    logger.Error("Failed to store shop {ShopId} product {ShopItemId} for {OwnerId}", shop.Id, id, ownerId);
                    continue;
                }
            }
            itemData[id] = data;
        }

        if (shop.RestockData.AccountWide) {
            accountShopItemData[shop.Id] = itemData;
            return accountShopItemData[shop.Id];
        }
        characterShopItemData[shop.Id] = itemData;
        return characterShopItemData[shop.Id];
    }

    // See ShopRules.StarEventActive: a star point shop, or a product paid in star points, needs a running StarShop
    // event whose npc exists.
    private bool StarEventOpen(ShopCurrencyType? cost = null) {
        if (activeShop!.Metadata.FrameType != ShopFrameType.Star && cost != ShopCurrencyType.StarPoint) {
            return true;
        }
        return ShopRules.StarEventActive(
            session.FindEvent(GameEventType.StarShop).Select(gameEvent => gameEvent.Metadata.Data is StarShop starShop ? starShop.NpcId : 0),
            npcId => session.NpcMetadata.TryGet(npcId, out _));
    }

    public void InstantRestock() {
        if (!ShopOpen() || !ShopRules.CanInstantRestock(activeShop.Metadata.EnableReset, activeShop.RestockData.ResetType,
                activeShop.RestockData.DisableInstantRestock)) {
            return;
        }

        var shop = new Shop(activeShop.Metadata);

        // The cost the shop packet showed (Shop.RestockCost). Before, with the multiplier on, the table's price was
        // taken in the multiplier's currency, and the multiplier read the base currency, not the excess one.
        (ShopCurrencyType currencyType, int cost) = activeShop.RestockCost;
        // The window shows the price lowered by the shop's NpcShopResetPriceSale event; the packet keeps the full one.
        cost = EventSaleRules.NpcShopResetPrice(cost, EventSaleRules.NpcShopResetRate(session.FindEvent(GameEventType.NpcShopResetPriceSale), activeShop.Id));
        if (!Pay(new ShopCost { Amount = cost, Type = currencyType }, cost)) {
            session.Send(ShopPacket.Error(ShopError.s_err_lack_shopitem)); // not neccessarily the right error.
            return;
        }

        if (!TryGetShopData(shop.Id, out CharacterShopData? data)) {
            data = CreateShopData(shop.Metadata, GetInstantRestockTime(shop.RestockData.ResetType));
        } else {
            data.RestockTime = GetInstantRestockTime(activeShop.RestockData.ResetType);
        }
        data.RestockCount++;
        activeShop = CreateInstancedShop(shop.Metadata, data, data.RestockTime);
        session.Send(ShopPacket.InstantRestock());
        session.Send(ShopPacket.Open(activeShop, shopNpcId));
        session.Send(ShopPacket.LoadItems(activeShop.Items.Values));
    }

    public void Refresh() {
        if (!ShopOpen()) {
            return;
        }

        if (!activeShop.Metadata.EnableReset) {
            return;
        }

        var shop = new Shop(activeShop.Metadata);
        if (!TryGetShopData(shop.Id, out CharacterShopData? data)) {
            data = CreateShopData(shop.Metadata, GetInstantRestockTime(shop.RestockData.ResetType));
        } else {
            data.RestockTime = GetInstantRestockTime(shop.RestockData.ResetType);
        }

        activeShop = CreateInstancedShop(shop.Metadata, data, data.RestockTime);
        session.Send(ShopPacket.Open(activeShop, shopNpcId));
        session.Send(ShopPacket.LoadItems(activeShop.Items.Values));
    }

    // The shown product counts the buy even when its row was not stored (CreateShopItemData skips a failed insert),
    // so its limit holds while this shop instance lives; before, such a product sold without a limit until the restock.
    private void UpdateStockCount(int shopId, ShopItem shopItem, int quantity) {
        shopItem.StockPurchased += quantity;
        if (TryGetShopItemData(shopId, out IDictionary<int, CharacterShopItemData>? data) &&
            data.TryGetValue(shopItem.Id, out CharacterShopItemData? itemData)) {
            itemData.StockPurchased += quantity;
        }
    }

    // Stock bought from a shop without a reset: one row per bought item, created on the first purchase and
    // saved with the other shop rows. These shops have no CharacterShopData row, so nothing restocks them.
    private void UpdateUnresetStockCount(ShopMetadata metadata, ShopItem shopItem, int quantity) {
        if (!TryGetShopItemData(metadata.Id, out IDictionary<int, CharacterShopItemData>? data)) {
            data = new Dictionary<int, CharacterShopItemData>();
            if (metadata.RestockData.AccountWide) {
                accountShopItemData[metadata.Id] = data;
            } else {
                characterShopItemData[metadata.Id] = data;
            }
        }
        // The shown product counts the buy even when its row cannot be created, so its limit holds while the shop is
        // open; a later row starts at the product's count. Before, such a buy was not counted at all.
        shopItem.StockPurchased += quantity;
        if (!data.TryGetValue(shopItem.Id, out CharacterShopItemData? itemData)) {
            using GameStorage.Request db = session.GameStorage.Context();
            long ownerId = metadata.RestockData.AccountWide ? session.AccountId : session.CharacterId;
            itemData = db.CreateCharacterShopItemData(ownerId, new CharacterShopItemData {
                ShopId = metadata.Id,
                ShopItemId = shopItem.Id,
                Item = shopItem.Item,
                StockPurchased = shopItem.StockPurchased,
            });
            if (itemData == null) {
                logger.Error("Failed to create stock data for shop {ShopId} item {ShopItemId}", metadata.Id, shopItem.Id);
                return;
            }
            data[shopItem.Id] = itemData;
            return;
        }
        itemData.StockPurchased += quantity;
    }

    private bool IsUnresetShop(int shopId) {
        return session.ServerTableMetadata.ShopTable.Entries.TryGetValue(shopId, out ShopMetadata? metadata) && !metadata.EnableReset;
    }

    private long GetRestockTime(ShopMetadata metadata) {
        if (metadata.RestockTime > 0) {
            return metadata.RestockTime;
        }
        // Follows a time schedule
        DateTime now = DateTime.Now;
        switch (metadata.RestockData.ResetType) {
            case ResetType.Default:
                return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1).ToEpochSeconds();
            case ResetType.Day:
                return new DateTime(now.Year, now.Month, now.Day, 0, 0, 0).AddDays(1).ToEpochSeconds();
            case ResetType.Week:
                return WeeklyResetRules.NextStart(now).ToEpochSeconds();
            case ResetType.Month:
                return new DateTime(now.Year, now.Month, 1, 0, 0, 0).AddMonths(1).ToEpochSeconds();
            case ResetType.Unlimited:
                return ShopRules.NoRestockTime;
            default:
                logger.Error("Unknown restock interval {Interval}", metadata.RestockData.ResetType);
                return ShopRules.NoRestockTime;
        }
    }

    private long GetInstantRestockTime(ResetType interval) {
        return interval switch {
            ResetType.Default => DateTime.Now.AddMinutes(1).ToEpochSeconds(),
            ResetType.Day => DateTime.Now.AddDays(1).ToEpochSeconds(),
            ResetType.Week => DateTime.Now.AddDays(7).ToEpochSeconds(),
            ResetType.Month => DateTime.Now.AddMonths(1).ToEpochSeconds(),
            ResetType.Unlimited => ShopRules.NoRestockTime,
            _ => ShopRules.NoRestockTime,
        };
    }


    // The command an error answers (its second field): the client's shop window resets the selection it made for that
    // command (UIShopDialog::ReadShopError, Selection::ResetIf: a buy is 4, a buy back 3).
    private const byte BuyBackCommand = 3;
    private const byte BuyCommand = 4;

    // Every buy is answered: the client turns its shop window off when it sends one (uishopdialog.cpp:992, 1514,
    // 3191) and on again only with a reply (ReadShopError, ReadPurchaseResult, ReadProductUpdate). A refusal whose
    // notice the client shows itself before it sends (the trophy, fame and guild npc checks) answers code 8, which
    // turns the window on with no notice.
    public void Buy(int shopItemId, int quantity, int shopId) {
        // The client names the shop of its window (UIShopDialog::SendPurchase); a buy for another shop is dropped.
        if (!ShopOpen() || shopId != activeShop.Id) {
            session.Send(ShopPacket.Error(ShopError.s_err_no_notice, BuyCommand));
            return;
        }

        if (!StarEventOpen()) {
            session.Send(ShopPacket.Error(ShopError.s_shop_no_star_point_event, BuyCommand));
            return;
        }

        if (!activeShop.Items.TryGetValue(shopItemId, out ShopItem? shopItem)) {
            session.Send(ShopPacket.Error(ShopError.s_err_invalid_item, BuyCommand));
            return;
        }


        if (shopItem.Metadata.RestrictedBuyData?.CanBuy(DateTime.Now) == false) {
            session.Send(ShopPacket.Error(ShopError.s_err_invalid_item_cannot_buy_by_period, BuyCommand));
            return;
        }

        if (!StarEventOpen(shopItem.Metadata.Cost.Type)) {
            session.Send(ShopPacket.Error(ShopError.s_shop_no_star_point_event, BuyCommand));
            return;
        }

        int unitPrice = shopItem.Metadata.Cost.SaleAmount > 0 ? shopItem.Metadata.Cost.SaleAmount : shopItem.Metadata.Cost.Amount;
        if (!ShopRules.TryTotalPrice(unitPrice, quantity, out int price)) {
            session.Send(ShopPacket.Error(ShopError.s_err_invalid_item, BuyCommand));
            return;
        }

        if (shopItem.StockCount != 0 && quantity > shopItem.StockCount - shopItem.StockPurchased) {
            session.Send(ShopPacket.Error(ShopError.s_err_lack_shopitem, BuyCommand));
            return;
        }

        if (shopItem.Metadata.Requirements.Achievement.Id > 0 && !session.Achievement.HasAchievement(shopItem.Metadata.Requirements.Achievement.Id, shopItem.Metadata.Requirements.Achievement.Rank)) {
            session.Send(ShopPacket.Error(ShopError.s_err_no_notice, BuyCommand));
            return;
        }

        if (!ShopRules.CheckGuildProduct(shopItem.Metadata.Requirements, shopItem.Metadata.Cost.Type,
                session.Guild.GetMember(session.CharacterId)?.JoinTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                session.Guild.Guild?.AchievementInfo.Total ?? 0, session.Guild.NpcLevel(shopItem.Metadata.Requirements.GuildNpc.Type),
                session.Guild.Guild?.LeaderCharacterId == session.CharacterId, out ShopError? guildError)) {
            session.Send(ShopPacket.Error(guildError ?? ShopError.s_err_no_notice, BuyCommand));
            return;
        }

        // The fame table is read only for a product with a grade.
        ShopItemMetadata.QuestAlliance fame = shopItem.Metadata.Requirements.QuestAlliance;
        if (fame.Grade > 0 && !ShopRules.CheckFameProduct(fame, session.TableMetadata.FameTable, session.Player.Value.Unlock.FamePoints)) {
            session.Send(ShopPacket.Error(ShopError.s_err_no_notice, BuyCommand));
            return;
        }

        // TODO: Championship (no KR product has one)

        // The client buys packs: its product item holds the pack size (CNpcShopProductCli::Read sets the item's
        // count to it, npcshopproductbase.cpp:188) and the quantity it sends is a number of packs (the most it
        // offers is the item count divided by the pack size, :453). So the bag gets quantity x pack size.
        long itemCount = (long) quantity * shopItem.SellUnit;
        if (itemCount > int.MaxValue) {
            session.Send(ShopPacket.Error(ShopError.s_err_invalid_item, BuyCommand));
            return;
        }

        Item item = shopItem.Item.Clone();
        item.Amount = (int) itemCount;
        // A product paid from the guild fund goes to the guild storage, not the bag: the World stores it with the
        // fund in one save, and the result's last flag shows the client's storage notice (UIShopDialog::ReadPurchaseResult, uishopdialog.cpp:1841).
        bool toGuildStorage = shopItem.Metadata.Cost.Type == ShopCurrencyType.GuildFund;
        // Room for all of it before the payment (Add takes all or none; a furnishing goes into furnishing storage).
        if (!toGuildStorage && !session.Item.Inventory.HasRoomFor(item)) {
            session.Send(ShopPacket.Error(ShopError.s_err_inventory, BuyCommand));
            return;
        }

        if (toGuildStorage) {
            BuyFromGuildFund(shopItem, item, quantity, price);
            return;
        }

        // The price is taken in memory first (held: its balance packet waits), then the wallet, the payment items' rows
        // and the bought stock are stored in one transaction; the item is given only once that is kept. A store that
        // fails gives the price back and gives nothing, so a crash at any point never leaves a free item; a crash after
        // the commit and before the item's write loses the item (the accepted cost). Before, the item was added and the
        // stock counted first and the wallet stored after, so a crash in between kept the item with the price unpaid.
        if (HoldPayment(shopItem.Metadata.Cost, price, BuyCommand) is not { } held) {
            return;
        }
        if (!session.TrySavePurchase(db => SavePayment(db, held) && (shopItem.StockCount <= 0 || SaveStock(db, shopItem, quantity)))) {
            ReleasePayment(held);
            logger.Error("Shop {ShopId} buy of item {ItemId} x{Amount} for {CharacterId} not stored; the price {Price} {Currency} is given back",
                activeShop.Id, item.Id, item.Amount, session.CharacterId, price, shopItem.Metadata.Cost.Type);
            session.Send(ShopPacket.Error(ShopError.s_err_no_notice, BuyCommand));
            return;
        }
        SettlePayment(held);

        // The room was checked above, so only a failed item write leaves it out of the bag: then it goes by mail. When
        // the mail is refused too, the price is given back and the bought stock uncounted, stored on their own.
        if (!session.Item.AddOrMail(item, true, commit: true)) {
            logger.Error("Shop {ShopId} item {ItemId} x{Amount} bought by {CharacterId} for {Price} {Currency} was neither added nor mailed; the price is given back",
                activeShop.Id, item.Id, item.Amount, session.CharacterId, price, shopItem.Metadata.Cost.Type);
            RefundPayment(held);
            if (shopItem.StockCount > 0) {
                CountStock(shopItem, -quantity);
            }
            SaveStockRow(shopItem);
            session.Send(ShopPacket.Error(ShopError.s_err_no_notice, BuyCommand));
            return;
        }
        // KMS2 order: the payment, the item, the bought count, then the result.
        if (shopItem.StockCount > 0) {
            session.Send(ShopPacket.Update(shopItem.Id, shopItem.StockPurchased * shopItem.SellUnit));
        }
        session.Send(ShopPacket.Buy(shopItem, shopItem.SellUnit * quantity, price, false));
        CountBuyCondition(shopItem, price);
    }

    // A product paid from the guild fund: the World takes the fund and stores the product in the guild storage in one
    // save, so only the bought stock is stored here, after it.
    private void BuyFromGuildFund(ShopItem shopItem, Item item, int quantity, int price) {
        GuildError fundError = session.Guild.SpendFund(price, new RewardItem(item.Id, (short) item.Rarity, item.Amount));
        if (fundError != GuildError.none) {
            session.Send(ShopPacket.Error(FundBuyError(fundError), BuyCommand));
            if (FundBuyError(fundError) == ShopError.s_err_no_notice) {
                // The World's own failure, or its answer lost (the buy may be stored: the storage list then
                // shows it), not a refusal: the guild's unknown error, not the "master only" notice.
                session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
            }
            return;
        }

        if (shopItem.StockCount > 0) {
            if (activeShop.Metadata.EnableReset) {
                UpdateStockCount(activeShop.Id, shopItem, quantity);
            } else {
                UpdateUnresetStockCount(activeShop.Metadata, shopItem, quantity);
            }
        }
        if (shopItem.StockCount > 0) {
            session.Send(ShopPacket.Update(shopItem.Id, shopItem.StockPurchased * shopItem.SellUnit));
        }
        // The result's last flag shows the client's storage notice (UIShopDialog::ReadPurchaseResult, uishopdialog.cpp:1841).
        session.Send(ShopPacket.Buy(shopItem, shopItem.SellUnit * quantity, price, true));
        CountBuyCondition(shopItem, price);
        SaveStockRow(shopItem);
    }

    // The mesos, Treva or Valor Tokens paid in an NPC shop count for shop_buy, shop_buy_karma_token or
    // shop_buy_honor_token (trophies 23200004, 23200014, 23200012).
    private void CountBuyCondition(ShopItem shopItem, int price) {
        if (ShopRules.BuyCondition(shopItem.Metadata.Cost.Type) is { } buyCondition && price > 0) {
            session.ConditionUpdate(buyCondition, counter: price);
        }
    }

    // Stores the wallet with the product's stock row when the session save stores that row (see Save), now rather
    // than at the next session save.
    private void SaveStockRow(ShopItem shopItem) {
        if (shopItem.StockCount > 0 && TryGetSavedStockRow(activeShop!.Id, shopItem.Id, out long stockOwnerId, out CharacterShopItemData? stockRow)) {
            session.SavePurchase(db => db.SaveCharacterShopItemData(stockOwnerId, [stockRow]));
        } else {
            session.SavePurchase();
        }
    }

    // Counts quantity (less when negative) on the shown product and its stock row in memory.
    private void CountStock(ShopItem shopItem, int quantity) {
        shopItem.StockPurchased += quantity;
        if (TryGetShopItemData(activeShop!.Id, out IDictionary<int, CharacterShopItemData>? data) &&
            data.TryGetValue(shopItem.Id, out CharacterShopItemData? itemData)) {
            itemData.StockPurchased += quantity;
        }
    }

    // Writes the bought stock in db's transaction. The counts change in memory at once (the row is written from them)
    // and go back when the transaction ends without a commit. A shop without a reset makes the product's row at its
    // first buy, in the same transaction, and lists it once that is committed: a row that cannot be made fails the
    // purchase. (Before, the buy went through without its row, and the count was lost at the next login.)
    private bool SaveStock(GameStorage.Request db, ShopItem shopItem, int quantity) {
        ShopMetadata metadata = activeShop!.Metadata;
        bool hasRow = TryGetShopItemData(metadata.Id, out IDictionary<int, CharacterShopItemData>? data) && data.ContainsKey(shopItem.Id);
        if (metadata.EnableReset || hasRow) {
            CountStock(shopItem, quantity);
            db.AfterEnd(() => { }, () => CountStock(shopItem, -quantity));
            return !TryGetSavedStockRow(metadata.Id, shopItem.Id, out long ownerId, out CharacterShopItemData? row)
                   || db.SaveCharacterShopItemData(ownerId, [row]);
        }

        long rowOwnerId = metadata.RestockData.AccountWide ? session.AccountId : session.CharacterId;
        CharacterShopItemData? created = db.CreateCharacterShopItemData(rowOwnerId, new CharacterShopItemData {
            ShopId = metadata.Id,
            ShopItemId = shopItem.Id,
            Item = shopItem.Item,
            StockPurchased = shopItem.StockPurchased + quantity,
        });
        if (created == null) {
            logger.Error("Failed to create stock data for shop {ShopId} item {ShopItemId}", metadata.Id, shopItem.Id);
            return false;
        }
        shopItem.StockPurchased += quantity;
        db.AfterEnd(() => {
            if (!TryGetShopItemData(metadata.Id, out IDictionary<int, CharacterShopItemData>? rows)) {
                rows = new Dictionary<int, CharacterShopItemData>();
                if (metadata.RestockData.AccountWide) {
                    accountShopItemData[metadata.Id] = rows;
                } else {
                    characterShopItemData[metadata.Id] = rows;
                }
            }
            rows[shopItem.Id] = created;
        }, () => shopItem.StockPurchased -= quantity);
        return true;
    }

    // The stock row of a shop item and its owner, when the session save stores that shop's rows (see Save).
    private bool TryGetSavedStockRow(int shopId, int shopItemId, out long ownerId, [NotNullWhen(true)] out CharacterShopItemData? row) {
        bool accountWide = accountShopItemData.ContainsKey(shopId);
        ownerId = accountWide ? session.AccountId : session.CharacterId;
        row = null;
        if (!TryGetShopItemData(shopId, out IDictionary<int, CharacterShopItemData>? data) || !data.TryGetValue(shopItemId, out row)) {
            return false;
        }
        if (IsUnresetShop(shopId)) {
            return true;
        }
        IDictionary<int, CharacterShopData> shops = accountWide ? accountShopData : characterShopData;
        return shops.TryGetValue(shopId, out CharacterShopData? shopData) && shopData.Interval != ResetType.Default;
    }

    // A buy back turns the window off too (UIShopDialog::BuyProduct, uishopdialog.cpp:2464): every one is answered.
    public void PurchaseBuyBack(int id) {
        if (!ShopOpen()) {
            session.Send(ShopPacket.Error(ShopError.s_err_no_notice, BuyBackCommand));
            return;
        }

        if (!StarEventOpen()) {
            session.Send(ShopPacket.Error(ShopError.s_shop_no_star_point_event, BuyBackCommand));
            return;
        }

        // The bag lock is held from the room and meso checks to the meso take and the entry's removal, so an item or
        // meso change made under it on another thread cannot come between them. The store comes after the lock: a
        // purchase save takes the account lock and then the bag lock, so this takes them in that order too.
        BuyBackItem? buyBackItem;
        long sends;
        lock (session.Item) {
            if (!buyBackItems.TryGetValue(id, out buyBackItem)) {
                session.Send(ShopPacket.Error(ShopError.s_err_invalid_item, BuyBackCommand));
                return;
            }

            if (!session.Item.Inventory.CanAdd(buyBackItem.Item)) {
                session.Send(ShopPacket.Error(ShopError.s_err_inventory, BuyBackCommand));
                return;
            }

            // The price is checked and taken in one step, held (its packet is sent once the buy back is stored).
            // Before, a meso change made between the check and the take made the take throw with the item given.
            sends = session.Currency.MesoSends;
            if (!session.Currency.TryTakeMeso(buyBackItem.Price, notify: false)) {
                session.Send(ShopPacket.Error(ShopError.s_err_lack_meso, BuyBackCommand));
                return;
            }
            buyBackItems.Remove(id);
        }

        // The item's row goes back to the character with the wallet in one transaction; the bag takes it once that
        // is kept. A store that fails lists the entry again and gives the mesos back, as they were (nothing was sent).
        // Before, the bag's own write came first and the wallet was stored after, so a crash in between kept the item
        // with the mesos unpaid.
        Item item = buyBackItem.Item;
        if (!session.TrySavePurchase(db => db.SaveItems(session.CharacterId, item))) {
            lock (session.Item) {
                buyBackItems[id] = buyBackItem;
                session.Currency.ReturnHeldMeso(buyBackItem.Price, sends);
            }
            logger.Error("Buy back of item {Uid} for {CharacterId} not stored; the entry and the price {Price} are kept",
                item.Uid, session.CharacterId, buyBackItem.Price);
            session.Send(ShopPacket.Error(ShopError.s_err_no_notice, BuyBackCommand));
            return;
        }

        lock (session.Item) {
            // KMS2 order: the entry leaves the list, the item comes back, then the mesos are taken.
            session.Send(ShopPacket.RemoveBuyBackItem(id));
            // Its room was checked above; a bag filled meanwhile, or a failed write, sends the row by mail.
            if (!session.Item.Inventory.Add(item, true, commit: true) && !session.Item.MailHeldItem(item)) {
                logger.Error("Bought-back item {Uid} ({ItemId} x{Amount}) of {CharacterId}, paid {Price}, stored to the character but neither added nor mailed",
                    item.Uid, item.Id, item.Amount, session.CharacterId, buyBackItem.Price);
            }
            session.Currency.NotifyMeso();
        }
    }

    public void Sell(long itemUid, int quantity) {
        if (!ShopOpen()) {
            return;
        }

        if (activeShop.Metadata.IsOnlySell) {
            session.Send(ShopPacket.Error(ShopError.s_msg_cant_sell_to_only_sell_shop));
            return;
        }

        if (!StarEventOpen()) {
            session.Send(ShopPacket.Error(ShopError.s_shop_no_star_point_event));
            return;
        }

        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null) {
            return;
        }
        // The client sells no summoned pet (RequestSellItem, itemugcmesh.cpp:1091, its own notice
        // s_err_cannot_sell_petitem_summon) and no score being played (UIShopDialog::CanSellItem, uishopdialog.cpp:2099,
        // s_play_instrument_cant_destory_score): both stay in the bag while in use.
        if (session.Pet?.Pet.Uid == item.Uid) {
            session.Send(ShopPacket.Error(ShopError.s_err_cannot_sell_petitem_summon));
            return;
        }
        if (session.Instrument?.Score?.Uid == item.Uid) {
            session.Send(ShopPacket.Error(ShopError.s_err_cannot_sell_item, 5));
            return;
        }
        // Nor a pet whose bag holds items (the client does not test it; the bag's rows would be left with no pet).
        if (Items.InventoryManager.PetBagHasItems(item)) {
            session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_cannot_destroy_petitem_hasitem));
            return;
        }

        bool hasGem = item.Socket?.Sockets.Any(gem => gem != null && gem.ItemId != 0) ?? false;
        if (!ShopRules.CanSell(item.Metadata.Limit.ShopSell, item.IsLocked, item.UnlockTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), hasGem) || SurvivalItemRules.IsMatchOnly(item.Metadata)) {
            // In KMS2 a refused sale is answered with code 5 and the sell command's number.
            session.Send(ShopPacket.Error(ShopError.s_err_cannot_sell_item, 5));
            return;
        }

        // The price is fixed and checked before the item leaves the inventory.
        long unitPrice = Core.Formulas.Shop.SellPrice(item.Metadata, item.Rarity, session.ServerTableMetadata.ConstantsTable);
        if (!ShopRules.TrySellTotal(unitPrice, quantity, item.Amount, out long sellPrice)) {
            session.Send(ShopPacket.Error(ShopError.s_err_invalid_item));
            return;
        }
        // The shop sell rate (SpecialAttribute.ShopBuyingMeso, s_item_opt_sa_improve_shop_buying_meso) raises the sale.
        // The buy-back entry keeps the raised price, so a sale bought back gains nothing.
        sellPrice = KillRate.Apply(sellPrice, session.Player.Stats.Values[SpecialAttribute.ShopBuyingMeso].Multiplier());

        if (session.Currency.CanAddMeso(sellPrice) != sellPrice) {
            logger.Error("Could not add {sellPrice} meso(s) to player {CharacterId}", sellPrice, session.CharacterId);
            return;
        }

        if (!session.Item.Inventory.Remove(item.Uid, out item, quantity)) {
            logger.Error("Failed to remove item {Uid} from inventory", itemUid);
            return;
        }

        if (buyBackItems.Count >= Constant.MaxBuyBackItems && !RemoveBuyBackItem()) {
            session.Currency.ChangeMeso(sellPrice);
            return;
        }

        int entryId = NextEntryId();
        buyBackItems[entryId] = new BuyBackItem {
            Id = entryId,
            Item = item,
            AddedTime = DateTime.Now.ToEpochSeconds(),
            Price = sellPrice,
        };

        // KMS2 order: the item leaves the bag, the buy-back entry is listed, then the mesos are paid.
        session.Send(ShopPacket.LoadBuyBackItem(buyBackItems[entryId]));
        session.Currency.ChangeMeso(sellPrice);
    }

    // A shop price taken in memory and not yet stored (HoldPayment): the purchase's transaction stores it (SavePayment),
    // then SettlePayment sends it, or ReleasePayment gives it back when the transaction was not kept. RefundPayment
    // gives back a stored one. Meso, Merets: the take's packet waits for SettlePayment. Currency: TryTake sends it at
    // once. Items: nothing is taken until the transaction writes their rows (Stacks: the planned stacks).
    private sealed record HeldPayment(ShopCurrencyType Type, int Price, long MesoSends, bool Red, CurrencyType Currency,
                                      int ItemId, List<(long Uid, int Amount, int Rarity)>? Stacks);

    // Takes the price in memory (see HeldPayment), or answers the lack with command's error and returns null.
    private HeldPayment? HoldPayment(ShopCost cost, int price, byte command) {
        switch (cost.Type) {
            case ShopCurrencyType.Meso:
                long sends = session.Currency.MesoSends;
                if (!session.Currency.TryTakeMeso(price, notify: false)) {
                    session.Send(ShopPacket.Error(ShopError.s_err_lack_meso, command));
                    return null;
                }
                return new HeldPayment(cost.Type, price, sends, false, CurrencyType.None, 0, null);
            case ShopCurrencyType.Meret:
            case ShopCurrencyType.EventMeret:
            case ShopCurrencyType.GameMeret:
                // EventMeret takes the red merets when they cover the price, else the plain ones; GameMeret
                // takes red merets only (MeretCost.OfShop).
                if (!session.Currency.TryTakeMerets(MeretCost.OfShop(cost.Type)!.Value, price, out bool red, notify: false)) {
                    session.Send(ShopPacket.Error(ShopError.s_err_lack_merat, command));
                    return null;
                }
                return new HeldPayment(cost.Type, price, 0, red, CurrencyType.None, 0, null);
            case ShopCurrencyType.Item:
                // The client counts the payment item at its item data's rank, any rank when that is 0 (GetMaxAffordableCount,
                // npcshopproductbase.cpp:475: Unk_Inventory::FUN_140a5f180 with GetItemRank, inventory.cpp:782); the
                // server took copies of any rarity, also ones the client did not count.
                int rank = session.ItemMetadata.TryGet(cost.ItemId, out ItemMetadata? payment) ? payment.Property.Rank ?? 0 : 0;
                List<(long Uid, int Amount, int Rarity)>? stacks = session.Item.Inventory.PlanConsume(cost.ItemId, rank > 0 ? rank : -1, price);
                if (stacks == null) {
                    session.Send(ShopPacket.Error(ShopError.s_err_lack_payment_item, command, cost.ItemId));
                    return null;
                }
                return new HeldPayment(cost.Type, price, 0, false, CurrencyType.None, cost.ItemId, stacks);
            default:
                CurrencyType currencyType = cost.Type switch {
                    ShopCurrencyType.ValorToken => CurrencyType.ValorToken,
                    ShopCurrencyType.Treva => CurrencyType.Treva,
                    ShopCurrencyType.Rue => CurrencyType.Rue,
                    ShopCurrencyType.HaviFruit => CurrencyType.HaviFruit,
                    ShopCurrencyType.StarPoint => CurrencyType.StarPoint,
                    ShopCurrencyType.MenteeToken => CurrencyType.MenteeToken,
                    ShopCurrencyType.MentorToken => CurrencyType.MentorToken,
                    ShopCurrencyType.MesoToken => CurrencyType.MesoToken,
                    ShopCurrencyType.ReverseCoin => CurrencyType.ReverseCoin,
                    _ => CurrencyType.None,

                };
                if (currencyType == CurrencyType.None || price < 0 || !session.Currency.TryTake(currencyType, price)) {
                    // Code 11 shows the lack notice of the currency kind in the last int (0: mesos). Chosen
                    // here: the kind is the payment type's number.
                    session.Send(ShopPacket.Error(ShopError.s_err_lack_meso, command, (int) cost.Type));
                    return null;
                }
                return new HeldPayment(cost.Type, price, 0, false, currencyType, 0, null);
        }
    }

    // Takes the price at once, stored by the next session save (the instant restock). The guild fund is spent by the
    // World.
    private bool Pay(ShopCost cost, int price) {
        if (cost.Type == ShopCurrencyType.GuildFund) {
            GuildError fundError = session.Guild.SpendFund(price, default);
            if (fundError != GuildError.none) {
                session.Send(ShopPacket.Error(FundBuyError(fundError)));
                if (FundBuyError(fundError) == ShopError.s_err_no_notice) {
                    session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
                }
                return false;
            }
            return true;
        }
        if (HoldPayment(cost, price, 0) is not { } held) {
            return false;
        }
        foreach ((long uid, int amount, _) in held.Stacks ?? []) {
            if (!session.Item.Inventory.Consume(uid, amount)) {
                logger.Error("Payment item {Uid} x{Amount} of {CharacterId} not taken", uid, amount, session.CharacterId);
            }
        }
        SettlePayment(held);
        return true;
    }

    // Writes the payment items' rows as the take leaves them in db's transaction, and takes them from the bag once it
    // is committed (the wallet is stored by the purchase save itself). False when a stack no longer holds its part.
    private bool SavePayment(GameStorage.Request db, HeldPayment held) {
        if (held.Stacks == null) {
            return true;
        }
        foreach ((long uid, int amount, _) in held.Stacks) {
            if (!session.Item.Inventory.SaveConsumed(db, uid, amount)) {
                return false;
            }
        }
        db.AfterCommit(() => {
            foreach ((long uid, int amount, _) in held.Stacks) {
                if (!session.Item.Inventory.ConsumeStored(uid, amount)) {
                    logger.Error("Payment item {Uid} x{Amount} stored as taken but not in the bag of {CharacterId}", uid, amount, session.CharacterId);
                }
            }
        });
        return true;
    }

    // Sends a held take once its purchase is stored.
    private void SettlePayment(HeldPayment held) {
        switch (held.Type) {
            case ShopCurrencyType.Meso:
                session.Currency.NotifyMeso();
                break;
            case ShopCurrencyType.Meret:
            case ShopCurrencyType.EventMeret:
            case ShopCurrencyType.GameMeret:
                if (held.Red) {
                    session.Currency.NotifyGameMeretTaken(held.Price);
                } else {
                    session.Currency.NotifyMeretTaken(held.Price);
                }
                break;
        }
    }

    // Gives back a held take whose purchase was not stored (the payment items were not taken).
    private void ReleasePayment(HeldPayment held) {
        switch (held.Type) {
            case ShopCurrencyType.Meso:
                session.Currency.ReturnHeldMeso(held.Price, held.MesoSends);
                break;
            case ShopCurrencyType.Meret:
            case ShopCurrencyType.EventMeret:
            case ShopCurrencyType.GameMeret:
                if (held.Red) {
                    session.Currency.ReturnGameMeret(held.Price);
                } else {
                    session.Currency.ReturnMeret(held.Price);
                }
                break;
            case ShopCurrencyType.Item:
                break;
            default:
                session.Currency.Return(held.Currency, held.Price);
                break;
        }
    }

    // Gives back a stored and sent payment (the bought item could be neither added nor mailed). Payment items come
    // back as new copies of the rarities taken, to the bag or else by mail; one that cannot be given is logged.
    private void RefundPayment(HeldPayment held) {
        switch (held.Type) {
            case ShopCurrencyType.Meso:
                session.Currency.ReturnMeso(held.Price);
                break;
            case ShopCurrencyType.Item:
                foreach ((_, int amount, int rarity) in held.Stacks ?? []) {
                    Item? copy = session.Item.CreateItem(held.ItemId, rarity, amount);
                    if (copy == null || !session.Item.AddOrMail(copy, true, commit: true)) {
                        logger.Error("Payment item {ItemId} x{Amount} (rarity {Rarity}) could not be given back to {CharacterId}",
                            held.ItemId, amount, rarity, session.CharacterId);
                    }
                }
                break;
            default:
                ReleasePayment(held);
                break;
        }
    }

    private bool RemoveBuyBackItem() {
        while (buyBackItems.Count >= Constant.MaxBuyBackItems) {
            // The oldest entry by id (ids rise with each sale); the added time is in seconds and ties.
            BuyBackItem? item = buyBackItems.Values.MinBy(entry => entry.Id);
            if (item == null) {
                logger.Error("Failed to remove buyback item");
                return false;
            }

            buyBackItems.Remove(item.Id);
            session.Item.Inventory.Discard(item.Item);
            session.Send(ShopPacket.RemoveBuyBackItem(item.Id));
        }
        return true;
    }

    public void DailyReset() {
        ResetShopData(ResetType.Day);
    }

    public void WeeklyReset() {
        ResetShopData(ResetType.Week);
    }

    private void ResetShopData(ResetType interval) {
        foreach (CharacterShopData data in accountShopData.Values) {
            if (data.Interval != interval) continue;
            data.RestockCount = 0;
            if (!accountShopItemData.TryGetValue(data.ShopId, out IDictionary<int, CharacterShopItemData>? items)) continue;
            foreach (CharacterShopItemData item in items.Values) {
                item.StockPurchased = 0;
            }
        }
        foreach (CharacterShopData data in characterShopData.Values) {
            if (data.Interval != interval) continue;
            data.RestockCount = 0;
            if (!characterShopItemData.TryGetValue(data.ShopId, out IDictionary<int, CharacterShopItemData>? items)) continue;
            foreach (CharacterShopItemData item in items.Values) {
                item.StockPurchased = 0;
            }
        }
        foreach (Shop shop in instancedShops.Values) {
            if (shop.RestockData.ResetType != interval) continue;
            shop.RestockCount = 0;
            foreach (ShopItem item in shop.Items.Values) {
                item.StockPurchased = 0;
            }
        }
    }

    public void Save(GameStorage.Request db) {
        db.SaveItems(0, buyBackItems.Values.Select(item => item.Item).ToArray());

        Dictionary<int, CharacterShopData> saveAccountShops = accountShopData.Values
            .Where(data => data.Interval != ResetType.Default)
            .ToDictionary(data => data.ShopId);

        Dictionary<int, CharacterShopData> saveCharacterShops = characterShopData.Values
            .Where(data => data.Interval != ResetType.Default)
            .ToDictionary(data => data.ShopId);

        db.SaveCharacterShopData(session.AccountId, saveAccountShops.Values.ToList());
        db.SaveCharacterShopData(session.CharacterId, saveCharacterShops.Values.ToList());

        db.SaveCharacterShopItemData(session.AccountId, accountShopItemData
            .Where(kvp => saveAccountShops.ContainsKey(kvp.Key) || IsUnresetShop(kvp.Key))
            .SelectMany(kvp => kvp.Value.Values)
            .ToList());

        db.SaveCharacterShopItemData(session.CharacterId, characterShopItemData
            .Where(kvp => saveCharacterShops.ContainsKey(kvp.Key) || IsUnresetShop(kvp.Key))
            .SelectMany(kvp => kvp.Value.Values)
            .ToList());
    }

    // The shop error (Shop 15) of a refused guild-fund buy: the fund notice, the "master only" notice for a refusal of
    // the buyer, or, for the World's own failure or its lost answer, none (the guild's unknown error is sent with it).
    public static ShopError FundBuyError(GuildError error) {
        return error switch {
            GuildError.s_guild_err_not_enough_guild_fund => ShopError.s_guild_err_not_enough_guild_fund,
            GuildError.s_guild_err_no_master or GuildError.s_guild_err_no_authority or GuildError.s_guild_err_null_member
                or GuildError.s_guild_err_null_guild => ShopError.s_guild_err_buy_no_master,
            _ => ShopError.s_err_no_notice,
        };
    }
}

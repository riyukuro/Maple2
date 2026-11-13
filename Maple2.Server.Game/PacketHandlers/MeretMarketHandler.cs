using System.Diagnostics.CodeAnalysis;
using System.Security;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

public class MeretMarketHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.MeretMarket;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private enum Command : byte {
        LoadPersonalListings = 11,
        LoadSales = 12,
        ListItem = 13,
        RemoveListing = 14,
        UnlistItem = 15,
        RelistItem = 18,
        FreeRelist = 19,
        CollectProfit = 20,
        LoadDesigners = 22,
        AddDesigner = 23,
        RemoveDesigner = 24,
        LoadDesignerItems = 25,
        OpenShop = 27,
        BlueprintClear = 28,
        FindItem = 29,
        Purchase = 30,
        LoadPremiumById = 32,
        LoadUgcById = 33,
        Featured = 101,
        OpenUgcShop = 102,
        AdProducts = 103, // sent when the market dialog opens; answered with 103 (OnAdProducts)
        Search = 104,
        BlueprintSearch = 105,
        SaleSettings = 107, // sent when the market dialog opens; answered with 107 (OnSettleSettings)
        RandomSaleRequest = 111,
        RandomSalePay = 112,
        RandomSaleCreate = 114,
        RandomSaleRequestNotice = 115,
        RandomSalePayNotice = 116,
        RandomSaleRequestOther = 118,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.LoadPersonalListings:
                HandleLoadPersonalListings(session);
                return;
            case Command.LoadSales:
                HandleLoadSales(session);
                return;
            case Command.ListItem:
                HandleListItem(session, packet);
                return;
            case Command.RelistItem:
                HandleRelistItem(session, packet);
                return;
            case Command.CollectProfit:
                HandleCollectProfit(session, packet);
                return;
            case Command.LoadDesigners:
                HandleLoadDesigners(session, packet);
                return;
            case Command.AddDesigner:
                HandleAddDesigner(session, packet);
                return;
            case Command.RemoveDesigner:
                HandleRemoveDesigner(session, packet);
                return;
            case Command.LoadDesignerItems:
                HandleLoadDesignerItems(session, packet);
                return;
            case Command.RemoveListing:
                HandleRemoveListing(session, packet);
                return;
            case Command.UnlistItem:
                HandleUnlistItem(session, packet);
                return;
            case Command.OpenShop:
                HandleOpenShop(session, packet);
                return;
            case Command.Purchase:
                HandlePurchase(session, packet);
                return;
            case Command.Featured:
                HandleFeatured(session, packet);
                return;
            case Command.OpenUgcShop:
                HandleOpenUgcShop(session);
                return;
            case Command.AdProducts:
                session.Send(MeretMarketPacket.AdProducts());
                return;
            case Command.SaleSettings:
                session.Send(MeretMarketPacket.SaleSettings(Constant.UGCShopProfitDelayDays));
                return;
            case Command.FindItem:
                HandleFindItem(session, packet);
                return;
            case Command.Search:
                HandleSearch(session, packet);
                return;
            case Command.BlueprintClear:
            case Command.BlueprintSearch:
                HandleBlueprintSearch(session, packet);
                return;
            case Command.FreeRelist:
                HandleFreeRelist(session, packet);
                return;
            case Command.LoadPremiumById:
                HandleLoadPremiumById(session, packet);
                return;
            case Command.LoadUgcById:
                HandleLoadUgcById(session, packet);
                return;
            case Command.RandomSaleRequest:
            case Command.RandomSalePay:
                HandleRandomSaleBuy(session, packet, command);
                return;
            case Command.RandomSaleCreate:
                HandleRandomSaleCreate(session, packet);
                return;
            case Command.RandomSaleRequestNotice:
            case Command.RandomSalePayNotice:
                HandleRandomSaleNotice(session, packet, command);
                return;
            case Command.RandomSaleRequestOther:
                HandleRandomSaleRequestOther(session, packet);
                return;
        }
    }

    private void HandleLoadPersonalListings(GameSession session) {
        session.UgcMarket.Load();
    }

    private void HandleLoadSales(GameSession session) {
        session.UgcMarket.RefreshSales();
    }

    private void HandleListItem(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        long price = packet.ReadLong();
        bool promote = packet.ReadBool();
        string[] tags = packet.ReadUnicodeString().Split(",");
        string description = packet.ReadUnicodeString();
        packet.ReadLong(); // the fee the dialog worked out; the server charges its own

        // A refusal answers MeretMarket 0x00 with the client's own code, in the order the register dialog checks
        // before it sends (uimeratmarketregisterdialog.cpp:966): already listed, not a design it may list, 100
        // listings, then the fee.
        void Refuse(int error) => session.Send(MeretMarketPacket.Error((byte) Command.ListItem, error, ugcId: itemUid));

        Item? item = GetUgcItem(itemUid);
        // A survival match item may not be listed (SurvivalItemRules); the dialog gets the "cannot register this item"
        // message. Chosen here.
        if (item != null && SurvivalItemRules.IsMatchOnly(item.Metadata)) {
            Refuse(MeretMarketError.DisableUgcItem);
            return;
        }
        if (item?.Template == null || item.Template.Id == 0) {
            Refuse(MeretMarketError.DisableUgcItem);
            return;
        }

        if (session.UgcMarket.IsDesignListed(item.Template.Id)) {
            Refuse(MeretMarketError.AlreadyListed);
            return;
        }

        // The dialog only offers a design the player made (CItem 0x140526060 on the look's owner ids).
        if (item.Template.AccountId != session.AccountId) {
            Refuse(MeretMarketError.DisableUgcItem);
            return;
        }

        if (session.UgcMarket.ListingCount >= MaxListings) {
            Refuse(MeretMarketError.RegisterLimit);
            return;
        }

        if (!TableMetadata.MeretMarketCategoryTable.Entries.TryGetValue((int) MeretMarketSection.Ugc, out IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab>? tab)) {
            Refuse(MeretMarketError.InvalidCategory);
            return;
        }

        if (!UgcMarketRules.InRange(SellPriceRange(item.Metadata.Id), price)) {
            Refuse(MeretMarketError.RegisterPrice);
            return;
        }

        int tabId = tab.FirstOrDefault(kv => kv.Value.Categories.Contains(item.Metadata.Property.Category)).Key;

        bool score = item.Type.IsMusicScore;
        long listingFee = UgcMarketRules.RegisterFee(session.UgcMarket.ActiveListingCount(), score, promote, Constants.UGCShopAdFeeMerat, Target.LOCALE == "CN");
        if (Pay(session, listingFee, MeretMarketCurrencyType.Meret) != StringCode.s_empty_string) {
            Refuse(MeretMarketError.LackMerat);
            return;
        }

        var marketItem = new UgcMarketItem(item.Metadata) {
            SellerAccountId = session.AccountId,
            SellerCharacterId = session.CharacterId,
            SellerCharacterName = session.PlayerName,
            Description = description,
            Tags = tags,
            Look = item.Template,
            Blueprint = item.Blueprint ?? new ItemBlueprint(),
            Status = UgcMarketListingStatus.Active,
            PromotionEndTime = promote ? DateTime.Now.AddHours(Constants.UGCShopAdHour).ToEpochSeconds() : 0,
            ListingEndTime = DateTime.Now.AddDays(Constants.UGCShopSaleDay).ToEpochSeconds(),
            CreationTime = DateTime.Now.ToEpochSeconds(),
            Price = price,
            TabId = tabId,
        };

        using GameStorage.Request db = session.GameStorage.Context();
        marketItem = db.CreateUgcMarketItem(marketItem);
        if (marketItem == null) {
            Logger.Error("Failed to create UGC market item: {ItemUid}", itemUid);
            session.Currency.ChangeMeret(listingFee); // nothing was listed: the fee goes back
            Refuse(MeretMarketError.Common);
            return;
        }
        session.SavePurchase();

        session.UgcMarket.ListItem(marketItem);
        return;

        Item? GetUgcItem(long uid) {
            Item? result = session.Item.Inventory.Get(uid);
            if (result != null) {
                return result;
            }

            result = session.Item.GetOutfit(uid);
            return result ?? session.Item.Furnishing.GetCube(uid);
        }
    }

    private void HandleRelistItem(GameSession session, IByteReader packet) {
        long ugcMarketItemId = packet.ReadLong();
        long price = packet.ReadLong();
        bool promote = packet.ReadBool();
        string[] tags = packet.ReadUnicodeString().Split(",");
        string description = packet.ReadUnicodeString();
        packet.ReadLong(); // the fee the dialog worked out; the server charges its own

        void Refuse(int error) => session.Send(MeretMarketPacket.Error((byte) Command.RelistItem, error, ugcId: ugcMarketItemId));

        if (!session.UgcMarket.TryGetItem(ugcMarketItemId, out UgcMarketItem? item)) {
            Refuse(MeretMarketError.NotSale);
            return;
        }
        // The resale form is only offered for a listing whose time ran out (OnClickRemoveSale, state 3).
        if (item.ListingEndTime > DateTime.Now.ToEpochSeconds()) {
            Refuse(MeretMarketError.AlreadySale);
            return;
        }
        if (!UgcMarketRules.InRange(SellPriceRange(item.ItemMetadata.Id), price)) {
            Refuse(MeretMarketError.RegisterPrice);
            return;
        }

        bool score = new ItemType(item.ItemMetadata.Id).IsMusicScore;
        long listingFee = UgcMarketRules.RegisterFee(session.UgcMarket.ActiveListingCount(), score, promote, Constants.UGCShopAdFeeMerat, Target.LOCALE == "CN");
        if (Pay(session, listingFee, MeretMarketCurrencyType.Meret) != StringCode.s_empty_string) {
            Refuse(MeretMarketError.LackMerat);
            return;
        }

        (long Price, long PromotionEndTime, long ListingEndTime, UgcMarketListingStatus Status, byte RelistKind, bool FreeRelistUsed, string Description, string[] Tags) before =
            (item.Price, item.PromotionEndTime, item.ListingEndTime, item.Status, item.RelistKind, item.FreeRelistUsed, item.Description, item.Tags);
        item.Price = price;
        item.PromotionEndTime = promote ? DateTime.Now.AddHours(Constants.UGCShopAdHour).ToEpochSeconds() : 0;
        item.ListingEndTime = DateTime.Now.AddDays(Constants.UGCShopSaleDay).ToEpochSeconds();
        item.Status = UgcMarketListingStatus.Active;
        item.RelistKind = 0;
        // The free relist is the first relist (s_meratmarket_free_resale_confirm); once relisted for the fee, a later
        // expiry offers the paid relist only. Chosen here: the client does not show either way.
        item.FreeRelistUsed = true;
        item.Description = description;
        item.Tags = tags;

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.SaveUgcMarketItem(item)) {
            // Not stored: the listing stays as it was and the fee goes back.
            (item.Price, item.PromotionEndTime, item.ListingEndTime, item.Status, item.RelistKind, item.FreeRelistUsed, item.Description, item.Tags) = before;
            session.Currency.ChangeMeret(listingFee);
            Refuse(MeretMarketError.Common);
            return;
        }
        session.SavePurchase();

        session.Send(MeretMarketPacket.RelistItem(item));
    }

    // The client's own listing cap (FUN_140ebc4a0, the 0x79 message).
    private const int MaxListings = 100;

    // The register dialog's price range for an item (GetSellPriceRange).
    private (long Min, long Max) SellPriceRange(int itemId) {
        TableMetadata.UgcDesignTable.Entries.TryGetValue(itemId, out UgcDesignTable.Entry? design);
        return UgcMarketRules.PriceRange(Constants.UGCShopSellMinPrice, Constants.UGCShopSellMaxPrice,
            design?.MarketMinPrice ?? 0, design?.MarketMaxPrice ?? 0, new ItemType(itemId).IsMusicScore, Target.LOCALE == "CN");
    }

    private void HandleCollectProfit(GameSession session, IByteReader packet) {
        long saleId = packet.ReadLong();
        session.UgcMarket.CollectProfit(saleId);
    }

    private void HandleLoadDesigners(GameSession session, IByteReader packet) {
        session.UgcMarket.LoadDesigners();
    }

    private void HandleAddDesigner(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        session.UgcMarket.AddDesigner(characterId);
    }

    private void HandleRemoveDesigner(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        session.UgcMarket.RemoveDesigner(characterId);
    }

    private void HandleLoadDesignerItems(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        session.UgcMarket.LoadDesignerItems(characterId);
    }

    private void HandleRemoveListing(GameSession session, IByteReader packet) {
        packet.ReadInt(); // 0
        long ugcMarketItemId = packet.ReadLong();
        packet.ReadLong(); // duplicate id read?

        session.UgcMarket.RemoveListing(ugcMarketItemId);
    }

    private void HandleUnlistItem(GameSession session, IByteReader packet) {
        packet.ReadInt(); // 0
        long ugcMarketItemId = packet.ReadLong();
        packet.ReadLong(); // duplicate id read?

        session.UgcMarket.UnlistItem(ugcMarketItemId);
    }

    private void HandleOpenShop(GameSession session, IByteReader packet) {
        MeretMarketSearch meretMarketSearch = packet.ReadClass<MeretMarketSearch>();

        ICollection<MarketItem> entries = GetItems(session, meretMarketSearch, MeretMarketSection.All).ToList();
        int totalItems = entries.Count;
        entries = TakeLimit(entries, meretMarketSearch.StartPage, meretMarketSearch.ItemsPerPage);

        session.Send(MeretMarketPacket.LoadItems(entries, totalItems, meretMarketSearch.StartPage));
    }

    private void HandlePurchase(GameSession session, IByteReader packet) {
        byte kind = packet.ReadByte();
        int premiumMarketId = packet.ReadInt();
        long ugcItemId = packet.ReadLong();
        int flag = packet.ReadInt();
        int childMarketItemId = packet.ReadInt();
        long unk1 = packet.ReadLong();
        int itemIndex = packet.ReadInt();
        int totalQuantity = packet.ReadInt();
        int unk2 = packet.ReadInt();
        bool gift = packet.ReadBool();
        string playerName = packet.ReadUnicodeString();
        string message = packet.ReadUnicodeString();
        long price = packet.ReadLong(); // price? (but price is auto-determined below)
        // BuyPacket::Write ends with a flag and the pay type the bill dialog chose (its choice 0 meso, 1 meret,
        // 2 red meret, 3 the product's own currency, sent as 1, 3, 9 and 0x11), then four ints the server does not use.
        byte payType = 0;
        if (packet.Available >= 2) {
            packet.ReadBool();
            payType = packet.ReadByte();
        }

        if (gift) {
            int giftError = GiftUgcItem(session, ugcItemId, playerName, message, payType);
            if (giftError != 0) {
                Fail(giftError);
                return;
            }
            session.SavePurchase();
            session.Send(MeretMarketPacket.Purchase(kind, flag, childMarketItemId, unk1, totalQuantity, itemIndex, unk2, price, premiumMarketId, ugcItemId,
                true, playerName, message, payType));
            return;
        }

        int error = PurchaseErrorUnknown;
        Item? item = ugcItemId > 0 ? PurchaseUgcItem(session, ugcItemId) : PurchasePremiumItem(session, premiumMarketId, childMarketItemId, out error);
        if (item == null) {
            Fail(error);
            return;
        }

        if (!session.Item.Inventory.Add(item, true, commit: true)) {
            session.Item.MailItem(item);
        }
        // Store the paid merets now, not only at the next session save.
        session.SavePurchase();

        session.Send(MeretMarketPacket.Purchase(kind, flag, childMarketItemId, unk1, totalQuantity, itemIndex, unk2, price, premiumMarketId, ugcItemId,
            false, playerName, message, payType));
        return;

        // Without a 0x1F answer the client's purchase queue waits for good.
        void Fail(int code) {
            session.Send(MeretMarketPacket.FailPurchase(kind, flag, childMarketItemId, unk1, totalQuantity, itemIndex, unk2, price, premiumMarketId, ugcItemId, code,
                gift, playerName, message, payType));
        }
    }

    // Purchase error codes with their own text in CMeratMarketMan::GetPurchaseErrorText (meratmarketman.cpp).
    // A code without a case there shows the client's generic error text with the number.
    private const int PurchaseErrorUnknown = 1;
    private const int PurchaseErrorAchievementComplete = 0xB1; // s_meratmarket_error_result_buy_achieve_complete
    private const int PurchaseErrorLevel = 0xB4; // s_meratmarket_error_buy_limit_level (or the product's level text)
    private const int PurchaseErrorJob = 0xB5; // s_meratmarket_error_buy_limit_job
    private const int PurchaseErrorGiftCannot = 0xA4; // s_meratmarket_error_result_gift_cannot
    private const int PurchaseErrorLackMeret = 0x88; // s_meratmarket_error_result_buy_lack_merat (the box opens the charge page)
    private const int PurchaseErrorPriceType = 0x87; // s_meratmarket_error_buy_invalid_price_type
    private const int PurchaseErrorGiftOwnProduct = 0x91; // s_meratmarket_error_result_gift_own_product
    private const int PurchaseErrorGiftNoUser = 0xA6; // s_meratmarket_error_result_gift_not_exist_user
    private const int PurchaseErrorGiftSameAccount = 0xB3; // s_cashshop_err_same_account
    // The bill dialog's pay type for merets (its choice 1 is sent as 3). The client offers a gift only on
    // a design (IsUgcProduct sets bGift) and a design's GetCurrency is merets, so no other pay type is a gift.
    private const byte PayTypeMeret = 3;

    private Item? PurchasePremiumItem(GameSession session, int premiumMarketId, int childMarketItemId, out int error) {
        error = PurchaseErrorUnknown;
        PremiumMarketItem? entry = session.GetPremiumMarketItem(premiumMarketId, childMarketItemId);
        if (entry == null) {
            return null;
        }

        // TODO: Find meret market error packets
        if (!OnSale(entry)) {
            return null;
        }

        if ((entry.Metadata.RequireMinLevel > 0 && entry.Metadata.RequireMinLevel > session.Player.Value.Character.Level) ||
            (entry.Metadata.RequireMaxLevel > 0 && entry.Metadata.RequireMaxLevel < session.Player.Value.Character.Level)) {
            error = PurchaseErrorLevel;
            return null;
        }

        // If JobRequirement is None, no job is eligible.
        if ((entry.Metadata.JobRequirement & session.Player.Value.Character.Job.Code().FilterFlag()) == JobFilterFlag.None) {
            error = PurchaseErrorJob;
            return null;
        }

        if (entry.Metadata.RequireAchievementId > 0 && session.Achievement.HasAchievement(entry.Metadata.RequireAchievementId, entry.Metadata.RequireAchievementRank)) {
            error = PurchaseErrorAchievementComplete;
            return null;
        }

        // The item is made before anything is paid: a product whose item cannot be made costs nothing.
        Item? item = session.Field?.ItemDrop.CreateItem(entry.ItemMetadata.Id, entry.Metadata.Rarity, entry.Metadata.Quantity + entry.Metadata.BonusQuantity);
        if (item == null) {
            Logger.Error("Failed to create item {ItemId}, {Rarity}, {Quantity}", entry.ItemMetadata.Id, entry.Metadata.Rarity, entry.Metadata.Quantity + entry.Metadata.BonusQuantity);
            return null;
        }

        long price = entry.Metadata.SalePrice > 0 ? entry.Metadata.SalePrice : entry.Price;
        if (price < 0) {
            return null;
        }
        StringCode payResult = Pay(session, price, entry.Metadata.CurrencyType);
        if (payResult != StringCode.s_empty_string) {
            session.Send(NoticePacket.MessageBox(payResult));
            return null;
        }

        if (entry.Metadata.DurationInDays > 0) {
            item.ExpiryTime = DateTime.Now.AddDays(entry.Metadata.DurationInDays).ToEpochSeconds();
        }

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.CreateSoldMeretMarketItem(entry, session.CharacterId)) {
            Logger.Fatal("Failed to create Sold Meret Market Entry for {itemId}", entry.Id);
        }

        return item;
    }

    // A listing on sale with its design entry, and the item it sells (made, not yet paid for).
    private (UgcMarketItem Listing, Item Item)? PrepareUgcItem(GameSession session, GameStorage.Request db, long id) {
        UgcMarketItem? ugcItem = db.GetUgcMarketItem(id);
        if (ugcItem == null) {
            return null;
        }

        if (!TableMetadata.UgcDesignTable.Entries.TryGetValue(ugcItem.ItemMetadata.Id, out UgcDesignTable.Entry? designMetadata)) {
            return null;
        }

        // Only a listing on sale: one taken off sale or past its sale time is not in any list the client shows.
        if (!UgcMarketRules.CanBuy(ugcItem.Status == UgcMarketListingStatus.Active, ugcItem.ListingEndTime, DateTime.Now.ToEpochSeconds(), ugcItem.Price)) {
            return null;
        }

        Item? item = session.Field?.ItemDrop.CreateItem(ugcItem.ItemMetadata.Id, designMetadata.ItemRarity);
        if (item == null) {
            Logger.Error("Unable to create Item for {itemId}", ugcItem.ItemMetadata.Id);
            return null;
        }
        item.Template = ugcItem.Look;
        item.Blueprint = ugcItem.Blueprint;
        return (ugcItem, item);
    }

    private Item? PurchaseUgcItem(GameSession session, long id) {
        using GameStorage.Request db = session.GameStorage.Context();
        // The item is made before anything is paid, and a sale that cannot be stored gives the merets back.
        if (PrepareUgcItem(session, db, id) is not (UgcMarketItem ugcItem, Item item)) {
            return null;
        }

        StringCode stringCode = Pay(session, ugcItem.Price, MeretMarketCurrencyType.Meret);
        if (stringCode != StringCode.s_empty_string) {
            session.Send(NoticePacket.MessageBox(stringCode));
            return null;
        }

        // The seller's share by the charge table the client shows, at the seller's sales total before this sale.
        SoldUgcMarketItem? soldEntry = UgcMarketManager.RecordSale(db, ugcItem);
        if (soldEntry == null) {
            Logger.Fatal("Unable to create Sold Ugc Market Entry for {itemId}", ugcItem.Id);
            session.Currency.ChangeMeret(ugcItem.Price);
            return null;
        }
        return item;
    }

    // A design bought for another player: the item goes to the receiver's mailbox in a system mail with the client's
    // gift texts (s_meratmarket_gift_mail_*), and the receiver's channel shows the gift notice (Mail 0x16).
    // Returns 0, or the error code for OnBuyFailed.
    private int GiftUgcItem(GameSession session, long ugcItemId, string receiverName, string message, byte payType) {
        if (ugcItemId <= 0) {
            return PurchaseErrorGiftCannot;
        }
        if (payType != PayTypeMeret) {
            return PurchaseErrorPriceType;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        (long AccountId, long CharacterId, string Name)? receiver = string.IsNullOrWhiteSpace(receiverName) ? null : db.FindCharacterByName(receiverName, skipPendingDelete: true);
        if (receiver == null) {
            return PurchaseErrorGiftNoUser;
        }
        if (receiver.Value.AccountId == session.AccountId) {
            return PurchaseErrorGiftSameAccount;
        }

        if (PrepareUgcItem(session, db, ugcItemId) is not (UgcMarketItem ugcItem, Item item)) {
            return PurchaseErrorUnknown;
        }
        if (ugcItem.SellerAccountId == session.AccountId) {
            return PurchaseErrorGiftOwnProduct;
        }
        long price = ugcItem.Price;
        if (session.Currency.CanAddMeret(-price) != -price) {
            return PurchaseErrorLackMeret;
        }

        long receiverId = receiver.Value.CharacterId;
        (Mail? mail, bool undoFailed) = DeliverGift(
            writeMail: () => WriteGiftMail(db, receiverId, session.PlayerName, message, item),
            pay: () => Pay(session, price, MeretMarketCurrencyType.Meret) == StringCode.s_empty_string,
            recordSale: () => UgcMarketManager.RecordSale(db, ugcItem) != null,
            refund: () => session.Currency.ChangeMeret(price),
            undoMail: written => {
                // The failed save's rows are still tracked here and would fail the delete too.
                db.DiscardChanges();
                return db.DeleteMail(written.Id, receiverId);
            });
        if (undoFailed) {
            // The receiver keeps the mail; when the merets were taken they stay taken, so the gift is paid for.
            Logger.Error("Unable to take back gift mail of listing {ListingId} to {CharacterId} from {BuyerId}, paid: {Paid}",
                ugcItem.Id, receiverId, session.CharacterId, mail != null);
        }
        if (mail == null) {
            Logger.Error("Unable to deliver gift {ListingId} to {CharacterId}", ugcItem.Id, receiverId);
            return PurchaseErrorUnknown;
        }

        Item gifted = mail.Items[0];
        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = receiverId,
                MailId = mail.Id,
                Gift = new MailGift {
                    Sender = session.PlayerName,
                    ItemId = gifted.Id,
                    Rarity = gifted.Rarity,
                    Amount = gifted.Amount,
                    UgcUrl = ugcItem.Look.Url,
                    UgcTitle = ugcItem.Look.Name,
                },
            });
        } catch { /* ignored: the mail is there at the next mailbox load */ }
        return 0;
    }

    // The order of a gift: the mail with its item is written first, so nothing is charged when it cannot be; a
    // payment or a sale that fails after that takes the mail back, and what was paid is given back only once the
    // mail is gone. A mail that cannot be taken back after the payment keeps the payment: the gift is delivered and
    // paid for (only the seller's sale record is missing), and the caller logs it. Returns the delivered mail, null
    // when nothing was delivered, and whether a take-back failed.
    internal static (Mail? Mail, bool UndoFailed) DeliverGift(Func<Mail?> writeMail, Func<bool> pay, Func<bool> recordSale, Action refund, Func<Mail, bool> undoMail) {
        Mail? mail = writeMail();
        if (mail == null) {
            return (null, false);
        }
        if (!pay()) {
            return (null, !undoMail(mail));
        }
        if (!recordSale()) {
            if (!undoMail(mail)) {
                return (mail, true);
            }
            refund();
            return (null, false);
        }
        return (mail, false);
    }

    // The gift mail: from s_meratmarket_gift_mail_sender, titled with the item, the content with the buyer's name
    // and the gift message. Null when the mail or its item row cannot be written.
    internal static Mail? WriteGiftMail(GameStorage.Request db, long receiverId, string buyerName, string message, Item item) {
        var mail = new Mail {
            ReceiverId = receiverId,
            Type = MailType.System,
            TitleArgs = [
                ("item", $"{item.Id}"),
            ],
            ContentArgs = [
                ("str", SecurityElement.Escape(buyerName) ?? string.Empty),
                ("str", SecurityElement.Escape(message) ?? string.Empty),
            ],
        };
        mail.SetTitle(StringCode.s_meratmarket_gift_mail_title);
        mail.SetContent(StringCode.s_meratmarket_gift_mail_content);
        mail.SetSenderName(StringCode.s_meratmarket_gift_mail_sender);

        Mail? written = db.CreateMail(mail);
        if (written == null) {
            return null;
        }
        item.Slot = -1;
        Item? row = db.CreateItem(written.Id, item);
        if (row == null) {
            // The failed item row is still tracked and would fail the delete; dropped, the mail goes.
            db.DiscardChanges();
            db.DeleteMail(written.Id, receiverId);
            return null;
        }
        written.Items.Add(row);
        return written;
    }

    private static void HandleFeatured(GameSession session, IByteReader packet) {
        byte section = packet.ReadByte();
        byte tabId = packet.ReadByte();

        IList<MarketItem> entries = ToMarketSection(section) switch {
            MeretMarketSection.All => session.GetPremiumMarketItems(tabId).Cast<MarketItem>().ToList(),
            MeretMarketSection.Premium => session.GetPremiumMarketItems(tabId).Cast<MarketItem>().ToList(),
            MeretMarketSection.Ugc => [],
            MeretMarketSection.RedMeret => [],
            _ => [],
        };
        if (section == 0) {
            IList<SystemBanner> banners = session.GetSystemBanners();
            entries = entries.Where(entry => entry is not PremiumMarketItem premium || DrawsOnHomePage(premium, banners)).ToList();
        }

        // Featured page needs a multiple of 2 slots. Add to the entries total if odd number.
        byte marketSlots = (byte) (entries.Count % 2 == 0 ? entries.Count : (byte) (entries.Count + 1));
        session.Send(MeretMarketPacket.FeaturedPremium(section, tabId, marketSlots, entries));
    }

    private void HandleOpenUgcShop(GameSession session) {
        using GameStorage.Request db = session.GameStorage.Context();

        ICollection<UgcMarketItem> newItems = db.GetUgcMarketNewItems();
        ICollection<UgcMarketItem> promoItems = db.GetUgcMarketPromotedItems();
        session.Send(MeretMarketPacket.FeaturedUgc(promoItems, newItems));
    }

    private void HandleFindItem(GameSession session, IByteReader packet) {
        bool premium = packet.ReadBool();
        // The client sends the id as a long for both kinds (CMeratMarketMan::SendMeretMarketMode29).
        long id = packet.ReadLong();
        // Set when the blueprint market dialog asks; that dialog only shows the blueprint page reply,
        // the market dialog only the search page reply.
        bool blueprintDialog = packet.ReadBool();

        MarketItem? marketItem;
        if (premium) {
            if (id is < int.MinValue or > int.MaxValue) {
                return;
            }
            marketItem = session.GetPremiumMarketItem((int) id);
        } else {
            using GameStorage.Request db = session.GameStorage.Context();
            marketItem = db.GetUgcMarketItem(id);
        }
        if (marketItem is null) {
            return;
        }

        session.Send(blueprintDialog
            ? MeretMarketPacket.LoadBlueprints([marketItem], 1, 1, 1)
            : MeretMarketPacket.LoadItems([marketItem], 1, 1));
    }

    private void HandleSearch(GameSession session, IByteReader packet) {
        MeretMarketSearch meretMarketSearch = packet.ReadClass<MeretMarketSearch>();

        MeretMarketSection section = ToMarketSection(packet.ReadByte());

        ICollection<MarketItem> entries = GetItems(session, meretMarketSearch, section).ToList();
        int totalItems = entries.Count;
        entries = TakeLimit(entries, meretMarketSearch.StartPage, meretMarketSearch.ItemsPerPage);

        session.Send(MeretMarketPacket.LoadItems(entries, totalItems, meretMarketSearch.StartPage));
    }

    private void HandleBlueprintSearch(GameSession session, IByteReader packet) {
        MeretMarketSearch meretMarketSearch = packet.ReadClass<MeretMarketSearch>();

        ICollection<MarketItem> entries = GetItems(session, meretMarketSearch, MeretMarketSection.Ugc).ToList();
        int totalItems = entries.Count;
        entries = TakeLimit(entries, meretMarketSearch.StartPage, meretMarketSearch.ItemsPerPage);

        session.Send(MeretMarketPacket.LoadBlueprints(entries, totalItems, meretMarketSearch.ItemsPerPage, meretMarketSearch.StartPage));
    }

    // A home page product (section 0, UIMeratMarketDialog::FillBannerProducts kind 2) draws its cell from a background:
    // the product's bg number (CDBPremiumProduct +0x9c, the BannerTag the server writes) picks the cell art "bg{n}"
    // (ASMeratMarketLimitedProduct::vfunc_1, asitemslot.cpp:2209-2230). With bg 0 the client links the system banner
    // named by the product's category (the Banner string) and draws its image; with no such banner the cell is drawn
    // with no background, a blank grey slot. Such a product is left off the page.
    internal static bool DrawsOnHomePage(PremiumMarketItem item, IEnumerable<SystemBanner> banners) {
        if (item.Metadata.BannerTag != MeretMarketBannerTag.None) {
            return true;
        }
        return item.Metadata.Banner.Length > 0 &&
               banners.Any(banner => banner.Name == item.Metadata.Banner && banner.Url.Length > 0);
    }

    private static MeretMarketSection ToMarketSection(byte section) {
        return section switch {
            0 => MeretMarketSection.All,
            1 => MeretMarketSection.Premium,
            2 => MeretMarketSection.RedMeret,
            3 => MeretMarketSection.Ugc,
            _ => MeretMarketSection.All,
        };
    }

    #region Helpers
    private IEnumerable<MarketItem> GetItems(GameSession session, MeretMarketSearch meretMarketSearch, MeretMarketSection section) {
        int[] tabIds = [];
        bool sortGender = false;
        bool sortJob = false;
        if (meretMarketSearch.TabId is not 1) {
            if (!GetTab(section, meretMarketSearch.TabId, out MeretMarketCategoryTable.Tab? tab)) {
                return new List<MarketItem>();
            }
            // get any sub tabs
            tabIds = new[] {
                meretMarketSearch.TabId,
            }.Concat(tab.SubTabIds).ToArray();
            sortGender = tab.SortGender;
            sortJob = tab.SortJob;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        IEnumerable<MarketItem> items;
        switch (section) {
            case MeretMarketSection.All:
                items = db.GetUgcMarketItems(tabIds);
                items = items.Concat(session.GetPremiumMarketItems(tabIds));
                items = Filter(items, meretMarketSearch.Gender, meretMarketSearch.Job, meretMarketSearch.SearchString);
                return Sort(items, meretMarketSearch.SortBy, sortJob, sortGender);
            case MeretMarketSection.Premium:
            case MeretMarketSection.RedMeret:
                items = session.GetPremiumMarketItems(tabIds);
                items = Filter(items, meretMarketSearch.Gender, meretMarketSearch.Job, meretMarketSearch.SearchString);
                return Sort(items, meretMarketSearch.SortBy, sortJob, sortGender);
            case MeretMarketSection.Ugc:
                items = db.GetUgcMarketItems(tabIds);
                items = Filter(items, meretMarketSearch.Gender, meretMarketSearch.Job, meretMarketSearch.SearchString);
                return Sort(items, meretMarketSearch.SortBy, sortJob, sortGender);
            default:
                Logger.Warning("Unimplemented Market section {section}", section);
                return new List<MarketItem>();
        }
    }

    private bool GetTab(MeretMarketSection section, int tabId, [NotNullWhen(true)] out MeretMarketCategoryTable.Tab? tab) {
        if (section == MeretMarketSection.All) {
            foreach ((int sectionId, IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab> dictionary) in TableMetadata.MeretMarketCategoryTable.Entries) {
                if (dictionary.TryGetValue(tabId, out tab)) {
                    return true;
                }
            }
        } else {
            return TableMetadata.MeretMarketCategoryTable.Entries.TryGetValue((int) section, tabId, out tab);
        }

        tab = null;
        return false;
    }

    private static StringCode Pay(GameSession session, long price, MeretMarketCurrencyType currencyType) {
        switch (currencyType) {
            case MeretMarketCurrencyType.Meso:
                if (session.Currency.CanAddMeso(-price) != -price) {
                    return StringCode.s_err_lack_meso;
                }
                session.Currency.ChangeMeso(-price);
                return StringCode.s_empty_string;
            case MeretMarketCurrencyType.Meret:
                if (!session.Currency.TryTakeMeret(price)) {
                    return StringCode.s_err_lack_merat;
                }
                return StringCode.s_empty_string;
            case MeretMarketCurrencyType.RedMeret:
                // Pay type 2 is the client's currency 9, red merets only (FUN_14069c3e0, meratmarketbasketinfo.cpp:217).
                if (!session.Currency.TryTakeGameMeret(price)) {
                    return StringCode.s_err_lack_merat_red;
                }
                return StringCode.s_empty_string;
            default:
                return StringCode.s_err_lack_money;
        }
    }

    private static IEnumerable<MarketItem> Filter(IEnumerable<MarketItem> items, GenderFilterFlag gender, JobFilterFlag job, string searchString) {
        if (!string.IsNullOrWhiteSpace(searchString)) {
            items = FilterByString(items, searchString);
        }

        // GenderFilterFlag.None means no genders are eligible.
        if (gender != GenderFilterFlag.All) {
            items = items.Where(entry => (gender & entry.ItemMetadata.Limit.Gender.FilterFlag()) != GenderFilterFlag.None);
        }

        // JobFilterFlag.None means no jobs are eligible.
        return items.Where(entry => entry.ItemMetadata.Limit.JobRecommends.Length == 0 ||
                                    entry.ItemMetadata.Limit.JobRecommends.Contains(JobCode.None) ||
                                    (job & entry.ItemMetadata.Limit.JobRecommends.FilterFlags()) != JobFilterFlag.None);
    }

    private static IEnumerable<MarketItem> FilterByString(IEnumerable<MarketItem> items, string searchString) {
        foreach (MarketItem item in items) {
            if (item is UgcMarketItem ugc) {
                if (ugc.Look.Name.Contains(searchString, StringComparison.OrdinalIgnoreCase)) {
                    yield return ugc;
                    continue;
                }

                if (ugc.Description.Contains(searchString, StringComparison.OrdinalIgnoreCase)) {
                    yield return ugc;
                    continue;
                }

                foreach (string tag in ugc.Tags) {
                    if (tag.Contains(searchString, StringComparison.OrdinalIgnoreCase)) {
                        yield return ugc;
                    }
                }
                continue;
            }

            if (item.ItemMetadata.Name != null && item.ItemMetadata.Name.Contains(searchString, StringComparison.OrdinalIgnoreCase)) {
                yield return item;
            }
        }
    }

    // The client's sort names (asbilldata.cpp:32): 1 Buy, 5 ChartOrder, 6 BuyTotal. ChartOrder is the shop table's own
    // order: shop_merat_custom has no chartOrder column and its rows run by id, so premium products go by id and
    // designs, which have no table order, follow by sales. 1 and 6 both go by sales: only one count is kept and the
    // client names no time window for either.
    internal static IEnumerable<MarketItem> Sort(IEnumerable<MarketItem> entries, MeretMarketSort sort, bool sortJob, bool sortGender) {
        if (sortGender) {
            entries = entries.OrderBy(item => item.ItemMetadata.Limit.Gender);
        }
        if (sortJob) {
            entries = entries.OrderBy(item => item.ItemMetadata.Limit.JobLimits);
        }

        switch (sort) {
            case MeretMarketSort.MostRecent:
                return entries.OrderByDescending(entry => entry.CreationTime);
            case MeretMarketSort.PriceHighest:
                return entries.OrderByDescending(entry => entry.Price);
            case MeretMarketSort.PriceLowest:
                return entries.OrderBy(entry => entry.Price);
            case MeretMarketSort.MostPopularPremium:
                return entries.OrderBy(entry => entry is PremiumMarketItem ? 0 : 1)
                    .ThenBy(entry => entry is PremiumMarketItem premium ? premium.Id : 0)
                    .ThenByDescending(entry => entry.SalesCount);
            case MeretMarketSort.MostPopularUgc:
            case MeretMarketSort.TopSeller:
                return entries.OrderByDescending(entry => entry.SalesCount);
            case MeretMarketSort.None:
            default:
                return entries;
        }
    }

    /// <summary>
    /// Limits the amount of market items returned to the client.
    /// </summary>
    /// <returns>Limited Market Items. 5 * itemsPerPage</returns>
    private static ICollection<MarketItem> TakeLimit(IEnumerable<MarketItem> entries, int startPage, int itemsPerPage) {
        const int numPages = 5;
        int offset = startPage * itemsPerPage - itemsPerPage;
        return entries.Skip(offset).Take(numPages * itemsPerPage + Math.Min(0, offset)).ToList();
    }
    #endregion

    // Client CMeratMarketMan::SendMeretMarketMode19 (meratmarketman.cpp:513): the free first relist of an expired
    // listing, sent by OnAskRemoveSale (uimeratmarketdialog.cpp:4724) once the seller accepts box 0xa37,
    // s_meratmarket_free_resale_confirm. The remove button offers it only for an expired listing of kind 3
    // (OnClickRemoveSale, :4490); any other expired listing opens the paid resale form (18).
    private void HandleFreeRelist(GameSession session, IByteReader packet) {
        long ugcMarketItemId = packet.ReadLong();
        int? error = session.UgcMarket.FreeRelist(ugcMarketItemId, Constants.UGCShopSaleDay);
        if (error != null) {
            session.Send(MeretMarketPacket.Error((byte) Command.FreeRelist, error.Value, ugcId: ugcMarketItemId));
        }
    }

    // Client CMeratMarketMan::SendMeretMarketMode32/33 (meratmarketman.cpp:661/675): products by id, at most 20,
    // sent when a chat item link carries several market serials (UIMeratMarketDialog::OnSearchItemLink,
    // uimeratmarketdialog.cpp:3723). The client reads the answers 0x20 and 0x21 as a search page
    // (meratmarketman.cpp:2735, 2742). Only what the market pages would offer is listed: a premium item inside its
    // sale window and an active listing whose time has not run out; any other id is left out.
    private const int MaxProductsById = 20;

    private void HandleLoadPremiumById(GameSession session, IByteReader packet) {
        int count = packet.ReadInt();
        if (count < 0 || count > MaxProductsById) {
            return;
        }
        var items = new List<MarketItem>();
        for (int i = 0; i < count; i++) {
            PremiumMarketItem? item = session.GetPremiumMarketItem(packet.ReadInt());
            if (item != null && OnSale(item)) {
                items.Add(item);
            }
        }
        session.Send(MeretMarketPacket.PremiumById(items));
    }

    private void HandleLoadUgcById(GameSession session, IByteReader packet) {
        int count = packet.ReadInt();
        if (count < 0 || count > MaxProductsById) {
            return;
        }
        var ids = new long[count];
        for (int i = 0; i < count; i++) {
            ids[i] = packet.ReadLong();
        }

        long now = DateTime.Now.ToEpochSeconds();
        var items = new List<MarketItem>();
        using (GameStorage.Request db = session.GameStorage.Context()) {
            foreach (long id in ids) {
                UgcMarketItem? item = db.GetUgcMarketItem(id);
                if (item is { Status: UgcMarketListingStatus.Active } && item.ListingEndTime > now) {
                    items.Add(item);
                }
            }
        }
        session.Send(MeretMarketPacket.UgcById(items));
    }

    // The premium sale window; 0 leaves that end open.
    private static bool OnSale(PremiumMarketItem item) {
        long now = DateTime.Now.ToEpochSeconds();
        return (item.Metadata.SaleStartTime == 0 || item.Metadata.SaleStartTime <= now) &&
               (item.Metadata.SaleEndTime == 0 || item.Metadata.SaleEndTime >= now);
    }

    // The random-sale notices. UIMeratMarketDialog::SendRandomSaleRequest (uimeratmarketdialog.cpp:2801) runs on
    // every bill open (OnClickBuy, :3844) and sends one of 111 (a type 6 product), 115 (types 7 and 8) or 118 (any
    // other product); the bill payment sends 112 (type 6) or 116 (types 7 and 8, uimeratmarketcommon.cpp:560/583)
    // before the purchase itself (30). The client reads no reply to any of them, and no KR shop row is of type 6, 7
    // or 8 (shop_merat and shop_merat_custom carry no type and the server writes none), so each is only logged.

    // 111 at the bill open and 112 at its payment, for a type 6 product: the product, the pick's fields as the
    // client holds them and the sale price.
    private void HandleRandomSaleBuy(GameSession session, IByteReader packet, Command command) {
        int productId = packet.ReadInt();
        byte arg1 = packet.ReadByte();
        byte arg2 = packet.ReadByte();
        int remainCount = packet.ReadInt();
        long remainSeconds = packet.ReadLong();
        long salePrice = packet.ReadLong();
        Logger.Debug("MeretMarket {Command}: random-sale notice for product {ProductId}, no reply", command, productId);
    }

    // 114 (uimeratmarketdialog.cpp:3890): a type 7 or 8 product's bill opened with no live pick asks for one. The
    // answer would be 0x72 (OnRandomCouponPick, meratmarketman.cpp:2581: id, sale id, rate, count, day); with no
    // such product there is no pick to give, so nothing is sent.
    private void HandleRandomSaleCreate(GameSession session, IByteReader packet) {
        int productId = packet.ReadInt();
        byte type = packet.ReadByte();
        Logger.Debug("MeretMarket random-sale pick for product {ProductId} type {Type}: no random-sale product, no reply", productId, type);
    }

    // 115 at the bill open (uimeratmarketdialog.cpp:2825/2834) and 116 at its payment (uimeratmarketcommon.cpp:583),
    // for a type 7 or 8 product: the product, the pick's rate and the notice string the client shows with it.
    private void HandleRandomSaleNotice(GameSession session, IByteReader packet, Command command) {
        int productId = packet.ReadInt();
        int rate = packet.ReadInt();
        int notice = packet.ReadInt();
        Logger.Debug("MeretMarket {Command}: random-sale notice for product {ProductId}, no reply", command, productId);
    }

    // 118 (uimeratmarketdialog.cpp:2843): the bill of any other product opened, so this comes with every ordinary
    // premium purchase: the product and an int the client keeps with it.
    private void HandleRandomSaleRequestOther(GameSession session, IByteReader packet) {
        int productId = packet.ReadInt();
        int arg = packet.ReadInt();
        Logger.Debug("MeretMarket bill opened for product {ProductId}, no reply", productId);
    }
}

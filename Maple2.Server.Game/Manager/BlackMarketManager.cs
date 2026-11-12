using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class BlackMarketManager {
    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private readonly ILogger logger = Log.Logger.ForContext<BlackMarketManager>();

    public BlackMarketManager(GameSession session) {
        this.session = session;
    }

    public void LoadMyListings() {
        using GameStorage.Request db = session.GameStorage.Context();
        ICollection<BlackMarketListing> entries = db.GetBlackMarketListings(session.CharacterId).ToList();

        session.Send(BlackMarketPacket.MyListings(entries));
    }

    public void Add(long itemUid, long price, int quantity) {
        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_register_not_exist_in_inven, 2));
            return;
        }

        if (item.Amount < quantity) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_lack_sale_count, 2));
            return;
        }
        if (!BlackMarketRules.IsSaleCount(quantity, item.Amount)) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_invalid_sale_count, 2));
            return;
        }
        if (!BlackMarketRules.IsSalePrice(price, Constants.BlackMarketSellMinPrice, Constants.BlackMarketSellMaxPrice)
            || !BlackMarketRules.IsSaleTotal(price, quantity)) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_fail_register, 2));
            return;
        }
        if (!BlackMarketRules.CanList(item.IsLocked, item.Transfer != null, item.Transfer?.Flag ?? TransferFlag.None,
                item.Transfer?.RemainTrades ?? 0, item.Transfer?.Binding != null)) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_disable_registitem, 2));
            return;
        }

        if (item.IsExpired()) {
            return;
        }

        float depositPercent = Lua.CalcBlackMarketRegisterDepositPercent();
        long depositFee = Lua.CalcBlackMarketRegisterDeposit((long) (price * quantity * depositPercent));

        if (session.Currency.CanAddMeso(-depositFee) != -depositFee) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_err_lack_meso, 2));
            return;
        }

        // KMS2 order: Meso (the deposit), the item leaving the bag, then the listing.
        session.Currency.Meso -= depositFee;
        if (!session.Item.Inventory.Remove(itemUid, out item, quantity)) {
            session.Currency.Meso += depositFee;
            return;
        }

        var listing = new BlackMarketListing(item) {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Deposit = depositFee,
            ExpiryTime = DateTime.Now.AddDays(Constants.BlackMarketSellEndDay).ToEpochSeconds(),
            Price = price,
            Quantity = quantity,
        };

        using GameStorage.Request db = session.GameStorage.Context();
        listing = db.CreateBlackMarketingListing(listing);
        if (listing == null) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_fail_register, 2));
            CreateRefundErrorMail(item, depositFee);
            return;
        }

        // Also add to World
        BlackMarketResponse response = session.World.BlackMarket(new BlackMarketRequest {
            Add = new BlackMarketRequest.Types.Add {
                ListingId = listing.Id,
            },
        });

        var error = (BlackMarketError) response.Error;
        if (error != BlackMarketError.none) {
            session.Send(BlackMarketPacket.Error(error, 2));
            CreateRefundErrorMail(item, depositFee);
            return;
        }

        session.Send(BlackMarketPacket.Add(listing));
    }

    public void Remove(long listingId) {
        using GameStorage.Request db = session.GameStorage.Context();

        // The row is taken first, and the item read after it: a buyer racing with the take-back either finds the
        // listing gone or has its part out of the item before the rest is mailed back.
        BlackMarketListing? listing = db.TakeBlackMarketListing(listingId, session.CharacterId);
        if (listing == null) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_close, 3));
            return;
        }

        // The world drops it from the search; its answer is not used, the row is already gone.
        try {
            session.World.BlackMarket(new BlackMarketRequest {
                Remove = new BlackMarketRequest.Types.Remove {
                    ListingId = listingId,
                },
            });
        } catch (Exception ex) {
            logger.Error(ex, "Failed to drop black market listing {ListingId} from the search", listingId);
        }

        long deposit = listing.ExpiryTime < DateTime.Now.ToEpochSeconds() ? listing.Deposit : 0;

        var mail = new Mail {
            ReceiverId = session.CharacterId,
            Type = MailType.BlackMarketListingCancel,
            TitleArgs = [
                ("item", $"{listing.Item.Id}"),
            ],
            ContentArgs = [
                ("key", listing.ExpiryTime < DateTime.Now.ToEpochSeconds() ?
                    $"{StringCode.s_blackmarket_mail_to_cancel_expired}" :
                    $"{StringCode.s_blackmarket_mail_to_cancel_direct}"),
                ("item", $"{listing.Item.Id}"),
                ("str", $"{listing.Quantity}"),
                ("money", $"{listing.Price * listing.Quantity}"),
                ("money", $"{listing.Price}"),
                ("money", $"{deposit}"),
            ],
            Meso = deposit,
        };

        mail.SetTitle(StringCode.s_blackmarket_mail_to_cancel_title);
        mail.SetContent(StringCode.s_blackmarket_mail_to_cancel_content);
        mail.SetSenderName(StringCode.s_blackmarket_mail_to_sender);

        mail = db.CreateMail(mail);
        if (mail == null) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_close, 3));
            logger.Error("Failed to create black market mail for {CharacterId}", session.CharacterId);
            return;
        }

        mail.Items.Add(listing.Item);
        db.SaveItems(mail.Id, listing.Item);
        session.Send(BlackMarketPacket.Remove(listingId));
        session.Mail.Notify(true);
    }

    public void Purchase(long listingId, int quantity) {
        BlackMarketListing? listing;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            listing = db.GetBlackMarketListing(listingId);
        }
        if (listing == null) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_err_lack_itemcount, 5, listingId));
            return;
        }

        if (listing.ExpiryTime < DateTime.Now.ToEpochSeconds()) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_buy_expired, 5, listingId));
            return;
        }

        if (BlackMarketRules.IsOwnListing(listing.AccountId, session.AccountId)) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_cannot_buy_ownProduct, 5, listingId));
            return;
        }
        if (!BlackMarketRules.IsPurchaseCount(quantity, listing.Quantity)) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_purchase_count, 5, listingId));
            return;
        }

        long cost = listing.Price * quantity;
        if (cost > session.Currency.Meso) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_err_lack_meso, 5, listingId));
            return;
        }

        // The listing above is only a look. The buy itself is one transaction that takes the count from the row
        // while it still holds it at this price, so two buyers cannot both get the last of a listing; the one
        // that comes second pays nothing and is told the count is short.
        Mail? receivingMail;
        bool gone;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            listing = db.BuyBlackMarketListing(listingId, quantity, listing.Price, BuyerMail(listing, quantity), out receivingMail, out gone);
        }
        if (listing == null || receivingMail == null) {
            session.Send(BlackMarketPacket.Error(gone ? BlackMarketError.s_err_lack_itemcount : BlackMarketError.s_blackmarket_error_purchase_count, 5, listingId));
            return;
        }

        session.Currency.Meso -= cost;

        // The world refreshes its search copy and tells the seller. The buy is done, so a failure here is logged.
        try {
            session.World.BlackMarket(new BlackMarketRequest {
                Purchase = new BlackMarketRequest.Types.Purchase {
                    ListingId = listingId,
                    SellerId = listing.CharacterId,
                },
            });
        } catch (Exception ex) {
            logger.Error(ex, "Failed to report the purchase of black market listing {ListingId}", listingId);
        }

        // KMS2 order: Meso, the purchase answer, then the mail notice. The buyer's side is done here, so a
        // seller mail that fails below is logged and no longer answered with an error after the success.
        session.Send(BlackMarketPacket.Purchase(listingId, quantity));
        session.Mail.Notify(true);

        // Seller Mail
        Mail? sellerMail = CreateSellerMail(listing, quantity);
        if (sellerMail == null) {
            logger.Error("Failed to create black market sale mail for {CharacterId}", listing.CharacterId);
            return;
        }

        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = sellerMail.ReceiverId,
                MailId = sellerMail.Id,
            });
        } catch { /*ignored*/ }
    }

    private Mail BuyerMail(BlackMarketListing listing, int quantity) {
        var receivingMail = new Mail {
            ReceiverId = session.CharacterId,
            Type = MailType.BlackMarketSale,
            TitleArgs = [
                ("item", $"{listing.Item.Id}"),
            ],
            ContentArgs = [
                ("item", $"{listing.Item.Id}"),
                ("str", $"{quantity}"),
                ("money", $"{listing.Price * quantity}"),
                ("money", $"{listing.Price}"),
            ],
        };

        receivingMail.SetTitle(StringCode.s_blackmarket_mail_to_buyer_title);
        receivingMail.SetContent(StringCode.s_blackmarket_mail_to_buyer_content);
        receivingMail.SetSenderName(StringCode.s_blackmarket_mail_to_sender);

        return receivingMail;
    }

    private Mail? CreateSellerMail(BlackMarketListing listing, int quantity) {
        using GameStorage.Request db = session.GameStorage.Context();

        float costRate = Lua.CalcBlackMarketCostRate();
        long tax = (long) (costRate * (quantity * listing.Price));

        bool sellerIsPremium = false;
        long savings = tax;
        if (session.PlayerInfo.GetOrFetch(listing.CharacterId, out PlayerInfo? seller) && seller.PremiumTime > DateTime.Now.ToEpochSeconds()) {
            savings = (long) (tax * Constant.BlackMarketPremiumClubDiscount);
            sellerIsPremium = true;
        }
        long revenue = quantity * listing.Price - savings;
        List<(string, string)> contentArgs = [
            ("item", $"{listing.Item.Id}"),
            ("str", $"{quantity}"),
            ("money", $"{listing.Price * quantity}"),
            ("money", $"{listing.Price}"),
            ("money", $"{tax}"),
            ("str", $"{costRate * 100}" + "%"),
        ];

        if (listing.Quantity == 0) {
            contentArgs.Add(("money", $"{listing.Deposit}"));
            revenue += listing.Deposit;
        }
        contentArgs.Add(("money", $"{revenue}"));

        if (sellerIsPremium) {
            contentArgs.Add(("str", $"{Constant.BlackMarketPremiumClubDiscount * 100}" + "%"));
            contentArgs.Add(("money", $"{savings}"));
        }

        var mail = new Mail {
            ReceiverId = listing.CharacterId,
            Type = MailType.BlackMarketSale,
            TitleArgs = [
                ("item", $"{listing.Item.Id}"),
            ],
            ContentArgs = contentArgs,
            Meso = revenue,
        };

        if (listing.Quantity == 0) {
            mail.SetContent(sellerIsPremium ?
                StringCode.s_blackmarket_mail_to_vipseller_content_soldout :
                StringCode.s_blackmarket_mail_to_seller_content_soldout
            );
        } else {
            mail.SetContent(sellerIsPremium ?
                StringCode.s_blackmarket_mail_to_vipseller_content :
                StringCode.s_blackmarket_mail_to_seller_content);
        }
        mail.SetTitle(StringCode.s_blackmarket_mail_to_seller_title);
        mail.SetSenderName(StringCode.s_blackmarket_mail_to_sender);

        return db.CreateMail(mail);
    }

    private void CreateRefundErrorMail(Item item, long refund) {
        var mail = new Mail {
            ReceiverId = session.CharacterId,
            Type = MailType.BlackMarketFail,
            TitleArgs = [
                ("item", $"{item.Id}"),
            ],
            Meso = refund,
        };

        mail.SetTitle(StringCode.s_blackmarket_mail_to_fail_add_title);
        mail.SetContent(StringCode.s_blackmarket_mail_to_fail_add_content);
        mail.SetSenderName(StringCode.s_blackmarket_mail_to_sender);

        using GameStorage.Request db = session.GameStorage.Context();
        mail = db.CreateMail(mail);
        if (mail == null) {
            logger.Fatal("Failed to create failure black market mail for {CharacterId}", session.CharacterId);
            return;
        }
        mail.Items.Add(item);
        db.SaveItems(mail.Id, item);

        session.Mail.Notify(true);
    }

    public void Preview(int itemId, int rarity) {
        // We're not checking if the item is in the inventory or not as this is just for previewing. We'll check in Add.
        if (!session.ItemMetadata.TryGet(itemId, out ItemMetadata? metadata)) {
            return;
        }
        long sellPrice = Core.Formulas.Shop.SellPrice(metadata, rarity, Constants);

        session.Send(BlackMarketPacket.Preview(itemId, rarity, sellPrice));
    }
}

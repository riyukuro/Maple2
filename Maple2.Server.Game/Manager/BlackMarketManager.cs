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
        // Only a sender that skips the client's own total check gets here; nothing is taken.
        if (!BlackMarketRules.IsSaleListing(price, quantity, Constants.BlackMarketSellMinPrice, Constants.BlackMarketSellMaxPrice)) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_fail_register, 2));
            return;
        }
        if (!BlackMarketRules.CanList(item.IsLocked, item.Transfer != null, item.Transfer?.Flag ?? TransferFlag.None,
                item.Transfer?.RemainTrades ?? 0, item.Transfer?.Binding != null, item.Music != null && item.Music.Author != string.Empty)
            || SurvivalItemRules.IsMatchOnly(item.Metadata)
            // A gem that may not go with the item (CItemSocket FUN_1406227f0); the client's own gem line has no
            // known error code here, so the generic refusal.
            || !TradeRules.AreGemsTradeable(item.Socket, id => session.ItemMetadata.TryGet(id, out ItemMetadata? gem) ? gem.Limit.TransferType : null,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_disable_registitem, 2));
            return;
        }

        // An item past its time is not listed; the refusal is answered, the client waits for one.
        if (item.IsExpired()) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_disable_registitem, 2));
            return;
        }

        using (GameStorage.Request countDb = session.GameStorage.Context()) {
            if (!BlackMarketRules.CanAddListing(countDb.CountBlackMarketListings(session.CharacterId))) {
                session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_max_register_item, 2));
                return;
            }
        }

        // The client's deposit is CalcBlackMarketRegisterDeposit of the sale's total (FUN_1401bcdb0,
        // blackmarketitemoption.cpp:244); the percent is applied inside it.
        long depositFee = Lua.CalcBlackMarketRegisterDeposit(price * quantity);

        if (session.Currency.CanAddMeso(-depositFee) != -depositFee) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_err_lack_meso, 2));
            return;
        }

        // KMS2 order: Meso (the deposit), the item leaving the bag, then the listing.
        session.Currency.ChangeMeso(-depositFee);
        if (!session.Item.Inventory.Remove(itemUid, out item, quantity)) {
            session.Currency.ChangeMeso(depositFee);
            return;
        }

        var listing = new BlackMarketListing(item) {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Deposit = depositFee,
            // The client's own listings window shows it (uiblackmarketpage.cpp:1717); it was 0 (1970).
            CreationTime = DateTime.Now.ToEpochSeconds(),
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
            // The row is taken back with the item and the deposit mailed, in one transaction: a row left behind is
            // loaded by World at its next start and could be bought, moving the refunded item to the buyer. A request
            // of its own: the listing's request still tracks the rows it wrote.
            using GameStorage.Request takeDb = session.GameStorage.Context();
            if (takeDb.TakeBlackMarketListing(listing.Id, session.CharacterId, taken => RefundErrorMail(taken.Item.Id, depositFee), out _) == null) {
                // The row and its item stay listed, as World will load them; the seller can take them back. The deposit
                // is mailed back now (a cancel before expiry refunds none), and the row's deposit set to 0.
                logger.Error("Failed to take back black market listing {ListingId} that World refused", listing.Id);
                if (takeDb.RefundBlackMarketDeposit(listing.Id, session.CharacterId, deposit => RefundErrorMail(item.Id, deposit)) != null) {
                    session.Mail.Notify(true);
                } else {
                    logger.Error("The deposit of black market listing {ListingId} that World refused was not refunded", listing.Id);
                }
                return;
            }
            session.Mail.Notify(true);
            return;
        }

        session.Send(BlackMarketPacket.Add(listing));
    }

    public void Remove(long listingId) {
        using GameStorage.Request db = session.GameStorage.Context();

        // The row is taken first, and the item read after it: a buyer racing with the take-back either finds the
        // listing gone or has its part out of the item before the rest is mailed back.
        // The mail and the item's move into it are part of the same step: a mail that fails leaves the listing.
        BlackMarketListing? listing = db.TakeBlackMarketListing(listingId, session.CharacterId, CancelMail, out Mail? mail);
        if (listing == null || mail == null) {
            // A listing that is gone (sold out, or taken back from another session) is answered as removed
            // already: the client drops it from its list with s_msg_blackmarket_complete_remove_already
            // (BlackMarketPageItemRegister::FUN_140f13030). One that is still there was not taken: the error.
            if (db.GetBlackMarketListing(listingId) == null) {
                session.Send(BlackMarketPacket.Remove(listingId, already: true));
                return;
            }
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

        session.Send(BlackMarketPacket.Remove(listingId));
        session.Mail.Notify(true);
    }

    private Mail CancelMail(BlackMarketListing listing) {
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

        return mail;
    }

    public void Purchase(long listingId, int quantity) {
        BlackMarketListing? listing;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            listing = db.GetBlackMarketListing(listingId);
        }
        // This look takes no lock, so it can fall between the two saves of another buyer's transaction. A database
        // without transactions (the in-memory one of the tests) then shows the row with nothing left before it is
        // removed: that is a sold listing, the same as no row.
        if (listing == null || listing.Quantity <= 0) {
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

        // The mesos are taken before the buy, checked and taken in one step, and given back when the buy fails. Taken
        // after it, a spend meanwhile (a shop on the field's thread) left the buyer short once the item was mailed: the
        // take threw and the item was free.
        long cost = listing.Price * quantity;
        if (!session.Currency.TryTakeMeso(cost, notify: false)) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_err_lack_meso, 5, listingId));
            return;
        }

        // The listing above is only a look. The buy itself is one transaction that takes the count from the row
        // while it still holds it at this price, so two buyers cannot both get the last of a listing; the one
        // that comes second gets its mesos back and is told the count is short.
        // The seller's mail (the mesos of the sale) is stored in that transaction too: a buy without it would
        // leave the seller without the item and without the mesos.
        Mail? receivingMail;
        Mail? sellerMail;
        bool gone;
        bool stored = false;
        try {
            bool sellerIsPremium = session.PlayerInfo.GetOrFetch(listing.CharacterId, out PlayerInfo? seller) && seller.PremiumTime > DateTime.Now.ToEpochSeconds();
            using GameStorage.Request db = session.GameStorage.Context();
            listing = db.BuyBlackMarketListing(listingId, quantity, listing.Price, BuyerMail(listing, quantity),
                after => SellerMail(after, quantity, sellerIsPremium), out receivingMail, out sellerMail, out gone, out stored);
        } catch {
            // A throw before the commit undoes the transaction, so the mesos go back. A throw after it (the answer's
            // shaping) leaves the buy stored and the item mailed, so the price stays taken.
            if (!stored) {
                session.Currency.ReturnMeso(cost);
            } else {
                session.Currency.NotifyMeso();
            }
            throw;
        }
        if (listing == null || receivingMail == null) {
            session.Currency.ReturnMeso(cost);
            session.Send(BlackMarketPacket.Error(gone ? BlackMarketError.s_err_lack_itemcount : BlackMarketError.s_blackmarket_error_purchase_count, 5, listingId));
            return;
        }

        session.Currency.NotifyMeso();

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

        // KMS2 order: Meso, the purchase answer, then the mail notice.
        session.Send(BlackMarketPacket.Purchase(listingId, quantity));
        session.Mail.Notify(true);

        if (sellerMail == null) {
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

    // The seller's mail for quantity sold of listing, which holds the count left after the sale.
    private static Mail SellerMail(BlackMarketListing listing, int quantity, bool sellerIsPremium) {
        float costRate = Lua.CalcBlackMarketCostRate();
        long tax = (long) (costRate * (quantity * listing.Price));

        long savings = tax;
        if (sellerIsPremium) {
            savings = (long) (tax * Constant.BlackMarketPremiumClubDiscount);
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

        return mail;
    }

    private Mail RefundErrorMail(int itemId, long refund) {
        var mail = new Mail {
            ReceiverId = session.CharacterId,
            Type = MailType.BlackMarketFail,
            TitleArgs = [
                ("item", $"{itemId}"),
            ],
            Meso = refund,
        };

        mail.SetTitle(StringCode.s_blackmarket_mail_to_fail_add_title);
        mail.SetContent(StringCode.s_blackmarket_mail_to_fail_add_content);
        mail.SetSenderName(StringCode.s_blackmarket_mail_to_sender);
        return mail;
    }

    private void CreateRefundErrorMail(Item item, long refund) {
        Mail? mail = RefundErrorMail(item.Id, refund);

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

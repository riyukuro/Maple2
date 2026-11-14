using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using static Maple2.Model.Error.MesoMarketError;

namespace Maple2.Server.Game.PacketHandlers;

public class MesoMarketHandler : FieldPacketHandler {
    // The average price while nothing has been sold.
    private const int DEFAULT_AVERAGE_PRICE = 200;
    // The sales the average price is taken over.
    private const int AVERAGE_PRICE_SALES = 100;
    // The sales it takes before the average price leaves the default.
    private const int AVERAGE_PRICE_MIN_SALES = 10;

    public override RecvOp OpCode => RecvOp.MesoMarket;

    private enum Command : byte {
        Load = 3,
        Create = 5,
        Cancel = 6,
        Search = 7,
        Purchase = 8,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    // The meso pages hang under the black market's meso radio button, which is hidden with MesoMarket off
    // (UIBlackMarketDialog::OnLoadComplete, uiblackmarketdialog.cpp:227); modes 5-8 are sent only from them
    // (uiblackmarketpagemeso.cpp:284/407/620/635). The load (3) is sent on every open of the window and stays.
    public const string Feature = "MesoMarket";
    public const string AdventureProtectFeature = "AdventureProtect";

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        if (command != Command.Load && !session.ServerTableMetadata.FeatureTable.IsOn(Feature)) {
            return;
        }
        switch (command) {
            case Command.Load:
                HandleLoad(session);
                return;
            case Command.Create:
                HandleCreate(session, packet);
                return;
            case Command.Cancel:
                HandleCancel(session, packet);
                return;
            case Command.Search:
                HandleSearch(session, packet);
                return;
            case Command.Purchase:
                HandlePurchase(session, packet);
                return;
        }
    }

    // The price of MesoMarketBasePrice meso over the last sales.
    private long AveragePrice(GameStorage.Request db) {
        return MesoMarketRules.AveragePrice(db.GetSoldMesoListings(AVERAGE_PRICE_SALES), Constants.MesoMarketBasePrice,
            DEFAULT_AVERAGE_PRICE, Constant.MesoMarketMinToken, Constant.MesoMarketMaxToken, AVERAGE_PRICE_MIN_SALES);
    }

    private void HandleLoad(GameSession session) {
        using (GameStorage.Request average = session.GameStorage.Context()) {
            session.Send(MesoMarketPacket.Load(AveragePrice(average)));
        }
        session.Send(MesoMarketPacket.Quota(session.Player.Value.Account.MesoMarketListed, session.Player.Value.Account.MesoMarketPurchased));

        // KMS2 sends the character's black market listings here, between the counts and the own list.
        session.BlackMarket.LoadMyListings();

        using GameStorage.Request db = session.GameStorage.Context();
        ICollection<MesoListing> myListings = db.GetMyMesoListingsByAccountId(session.AccountId);
        session.Send(MesoMarketPacket.MyListings(myListings));
    }

    // Client: BlackMarketPageMesoRegister::SendMesoMarketMode5 (uiblackmarketpagemeso.cpp:614): the meso amount,
    // one of the MesoMarketProductUnit amounts, and the whole price, the price per MesoMarketBasePrice times the
    // amount's count of base prices (GetTotalPrice, :801). The checks and their order are CMesoMarketMan::CanRegister
    // (mesomarketproductcli.cpp:283).
    private void HandleCreate(GameSession session, IByteReader packet) {
        long amount = packet.ReadLong();
        long price = packet.ReadLong();

        if (!MesoMarketRules.IsProductUnit(amount, Constants.MesoMarketBasePrice, ProductUnits())) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_invalidSaleMoney));
            return;
        }

        // The mesos are held here without a packet (a spend on another thread before the old take made it throw
        // with the listing already stored, a listing of mesos never taken) and given back on each refusal below;
        // the packet goes where the take was.
        if (!session.Currency.TryTakeMeso(amount, notify: false)) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_hasNotMeso));
            return;
        }

        MesoListing? listing = null;
        try {
            listing = CreateListing(session, amount, price);
        } finally {
            // Nothing is stored unless the listing is, with the wallet: a refusal, a failed save or a throw gives the
            // mesos back.
            if (listing == null) {
                session.Currency.ReturnMeso(amount, notify: false);
            }
        }
        if (listing == null) {
            return;
        }

        session.Currency.NotifyMeso();
        session.Send(MesoMarketPacket.Create(listing, session.Player.Value.Account.MesoMarketListed));
        session.Send(MesoMarketPacket.Quota(session.Player.Value.Account.MesoMarketListed, session.Player.Value.Account.MesoMarketPurchased));
    }

    // The checks after the balance, in CanRegister's order, and the listing stored; null (the refusal sent) when
    // refused.
    private MesoListing? CreateListing(GameSession session, long amount, long price) {
        using GameStorage.Request db = session.GameStorage.Context();
        if (!MesoMarketRules.HasListingSlot(db.GetMyMesoListingsByAccountId(session.AccountId).Count, Constant.MesoMarketListLimit)) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_maxCountOwnProduct));
            return null;
        }
        if (session.Player.Value.Account.MesoMarketListed >= Constant.MesoMarketListLimitDay) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_maxCountRegister));
            return null;
        }

        long averagePrice = AveragePrice(db);
        (long low, long high) = MesoMarketRules.PriceRange(averagePrice, Constant.MesoMarketRangeRate,
            Constant.MesoMarketMinToken, Constant.MesoMarketMaxToken);
        if (!MesoMarketRules.IsListingPrice(amount, price, Constants.MesoMarketBasePrice, low, high)) {
            // The client tests the price against the average it got when the window opened, and its message
            // names the range from that average (CMesoMarketMan::ShowResultMessage): the limits again first, so
            // the message and the next try use the average of now.
            session.Send(MesoMarketPacket.Load(averagePrice));
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_invalidBuyMerat));
            return null;
        }

        var listing = new MesoListing(TimeSpan.FromDays(Constant.MesoMarketSellEndDay)) {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Price = price,
            Amount = amount,
        };

        // The listing, the wallet with its mesos taken and the day's listing count are one save (TrySavePurchase):
        // a listing stored alone (the wallet left to the session's next save) held the mesos and gave them back too
        // after a crash or a failed save, and the count stored at the next save missed such a listing. The count
        // is raised inside the save, after the resets it takes (a daily reset taken there zeroes it first).
        MesoListing? created = null;
        bool stored = false, resetRunning = false, counted = false;
        try {
            stored = session.TrySavePurchase(save => {
                created = save.CreateMesoListing(listing);
                if (created == null) {
                    return false;
                }
                session.Player.Value.Account.MesoMarketListed++;
                counted = true;
                return save.SaveMarketLimits(session.Player);
            }, out resetRunning);
        } finally {
            // Undone after the save let the account lock go: a daily reset taken meanwhile has zeroed the count already.
            if (!stored && counted && session.Player.Value.Account.MesoMarketListed > 0) {
                session.Player.Value.Account.MesoMarketListed--;
            }
        }
        if (!stored) {
            // 4 (alreadyDbWork) while a reset is being stored: the player can list a few seconds later.
            session.Send(MesoMarketPacket.Error(resetRunning ? s_mesoMarket_error_alreadyDbWork : s_mesoMarket_error_errorDB));
            return null;
        }
        return created;
    }

    private long[] ProductUnits() {
        return [
            Constants.MesoMarketProductUnit0, Constants.MesoMarketProductUnit1, Constants.MesoMarketProductUnit2,
            Constants.MesoMarketProductUnit3, Constants.MesoMarketProductUnit4, Constants.MesoMarketProductUnit5,
            Constants.MesoMarketProductUnit6, Constants.MesoMarketProductUnit7, Constants.MesoMarketProductUnit8,
            Constants.MesoMarketProductUnit9,
        ];
    }

    private static void HandleCancel(GameSession session, IByteReader packet) {
        long listingId = packet.ReadLong();

        using GameStorage.Request db = session.GameStorage.Context();
        MesoListing? listing = db.GetMesoListing(listingId);
        if (listing == null || listing.AccountId != session.AccountId) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_notFoundProduct));
            return;
        }

        if (session.Currency.CanAddMeso(listing.Amount) != listing.Amount) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_unknown));
            return;
        }

        if (!db.DeleteMesoListing(listingId)) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_errorDB));
            return;
        }

        session.Currency.ChangeMeso(listing.Amount);
        session.Send(MesoMarketPacket.Cancel(listingId));
    }

    private static void HandleSearch(GameSession session, IByteReader packet) {
        long minMesos = packet.ReadLong();
        long maxMesos = packet.ReadLong();

        if (!MesoMarketRules.IsSearchRange(minMesos, maxMesos)) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_invalidSearchParam));
            return;
        }

        try {
            using GameStorage.Request db = session.GameStorage.Context();
            ICollection<MesoListing> listings = db.SearchMesoListings(Constant.MesoMarketPageSize, minMesos, maxMesos);

            session.Send(MesoMarketPacket.Search(session.AccountId, listings));
        } catch (SystemException) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_errorDB));
        }
    }

    private void HandlePurchase(GameSession session, IByteReader packet) {
        long listingId = packet.ReadLong();

        // With AdventureProtect on the client asks only once its protection state is 7 (AdventureProtectRules,
        // uiblackmarketpagemeso.cpp:180); the server checks it again. With it off the client does not ask, nor does the server.
        if (session.ServerTableMetadata.FeatureTable.IsOn(AdventureProtectFeature) && !session.AdventureProtect.Cleared) {
            session.Send(MesoMarketPacket.Error(s_adventure_protect_err));
            return;
        }

        if (session.Player.Value.Account.MesoMarketPurchased >= Constant.MesoMarketPurchaseLimitMonth) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_maxCountBuy));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        MesoListing? listing = db.GetMesoListing(listingId);
        if (listing == null) {
            // A listing leaves the table when it is sold or taken back. Client: CMesoMarketMan::OnBuyResult
            // (mesomarketproductcli.cpp:401) drops the row from its list on code 0 and on code 17 only.
            SendSoldOut(session, listingId);
            return;
        }

        if (listing.AccountId == session.AccountId) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_cannotBuyOwnProduct));
            return;
        }

        if (listing.ExpiryTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
            // The listing ended after the search that listed it. The client drops a row on code 0 and 17 only, so
            // the sold-out answer; the listing stays for its owner to take back.
            SendSoldOut(session, listingId);
            return;
        }

        // The price is taken before the sale and given back when it is not stored: the old take after the sale threw
        // when the tokens were spent on another thread meanwhile (a shop, a beauty salon), with the sale and its mails
        // already stored, mesos for nothing.
        Payment? payment = TakePrice(session, Constants.MesoMarketBuyPayType, listing.Price);
        if (payment == null) {
            return;
        }

        // The listing's end, both mails and the buyer's wallet with the price taken are one save (TrySavePurchase):
        // a sale without its mails would lose the buyer's meso or the seller's price, and a sale stored without the
        // wallet (left to the session's next save before) came back paid for nothing after a crash or a failed save,
        // or the price back in the wallet with the mesos mailed. A sale that is not stored, or whose wallet is not,
        // writes nothing and the price is given back.
        // The month's purchase count is stored with the sale too: counted after the save (and stored at the next
        // session save) it was lost with a crash or a failed save, a buy the monthly limit did not count. It is
        // counted inside the save, after the resets it takes (a monthly reset taken there zeroes the count first).
        (Mail Buyer, Mail Seller)? mails = null;
        bool stored = false, gone = false, resetRunning = false, counted = false;
        try {
            (Mail buyerMail, Mail sellerMail) = PurchaseMails(session, listing, Constants.MesoMarketIconType);
            stored = session.TrySavePurchase(save => {
                mails = save.SellMesoListing(listingId, buyerMail, sellerMail, out gone);
                if (mails == null) {
                    return false;
                }
                session.Player.Value.Account.MesoMarketPurchased++;
                counted = true;
                return save.SaveMarketLimits(session.Player);
            }, () => gone, out resetRunning);
        } finally {
            if (!stored) {
                mails = null;
                // Undone after the save let the account lock go: a monthly reset taken meanwhile has zeroed the count
                // already.
                if (counted && session.Player.Value.Account.MesoMarketPurchased > 0) {
                    session.Player.Value.Account.MesoMarketPurchased--;
                }
                GiveBack(session, payment.Value, listing.Price);
            }
        }
        if (mails == null) {
            // Only a listing that is gone (another buyer or its owner was first) is answered sold out: the client drops
            // the row on 17 (CMesoMarketMan::OnBuyResult). A sale refused while it is still for sale keeps its row: 4
            // (s_mesoMarket_error_alreadyDbWork) while a reset is being stored, the player can buy a few seconds later;
            // 2 (errorDB) for a failed save.
            if (gone) {
                SendSoldOut(session, listingId);
            } else {
                session.Send(MesoMarketPacket.Error(resetRunning ? s_mesoMarket_error_alreadyDbWork : s_mesoMarket_error_errorDB));
            }
            return;
        }
        Paid(session, payment.Value, listing.Price);

        foreach (Mail mail in new[] { mails.Value.Buyer, mails.Value.Seller }) {
            try {
                session.World.MailNotification(new MailNotificationRequest {
                    CharacterId = mail.ReceiverId,
                    MailId = mail.Id,
                });
            } catch { /* ignored */ }
        }

        session.Send(MesoMarketPacket.Purchase(listing.Id, session.Player.Value.Account.MesoMarketPurchased));
        session.Send(MesoMarketPacket.Quota(session.Player.Value.Account.MesoMarketListed, session.Player.Value.Account.MesoMarketPurchased));
    }

    // The buy price's currency is MesoMarketBuyPayType, a client currency id: the search page shows the price and
    // the own amount with its icon (GetCurrencyIconHtml, uiblackmarketpagemeso.cpp:306/694), read by
    // GetCurrencyAmount (uicommon.cpp:1020). 16 is the meso market tokens (token type 13, the MesoMarketToken pocket;
    // the NA rows), 3 the merets (KR, CN, JP, TH, TW), 9 the red merets, 10 the red merets when they cover the price,
    // else the merets (MeretCost). A lack is answered as the client answers it (CMesoMarketMan::ShowResultMessage,
    // mesomarketproductcli.cpp:426): 7 asks to charge tokens, 6 merets.
    internal readonly record struct Payment(MeretCost.Wallet? Wallet, bool Red);

    internal const int TokenPayType = 16;

    internal static MeretCost.Wallet? MeretWallet(int payType) {
        return payType switch {
            3 => MeretCost.Wallet.Meret,
            9 => MeretCost.Wallet.Red,
            10 => MeretCost.Wallet.RedElseMeret,
            _ => null,
        };
    }

    // Takes the price, the refusal sent when it cannot; null then. Merets are taken without their packet, which
    // goes out once the sale is stored (Paid) or with the give-back (GiveBack).
    private Payment? TakePrice(GameSession session, int payType, long price) {
        MeretCost.Wallet? wallet = MeretWallet(payType);
        if (wallet != null) {
            if (!session.Currency.TryTakeMerets(wallet.Value, price, out bool red, notify: false)) {
                session.Send(MesoMarketPacket.Error(s_err_lack_merat_ask));
                return null;
            }
            return new Payment(wallet, red);
        }
        if (payType != TokenPayType) {
            Logger.Error("MesoMarketBuyPayType {PayType} is no currency the meso market can take", payType);
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_shutDown));
            return null;
        }
        if (!session.Currency.TryTake(CurrencyType.MesoToken, price)) {
            session.Send(MesoMarketPacket.Error(s_err_lack_meso_makret_token_ask));
            return null;
        }
        return new Payment(null, false);
    }

    private static void GiveBack(GameSession session, Payment payment, long price) {
        if (payment.Wallet == null) {
            session.Currency.Change(CurrencyType.MesoToken, price);
        } else if (payment.Red) {
            session.Currency.ReturnGameMeret(price);
        } else {
            session.Currency.ReturnMeret(price);
        }
    }

    // The merets' packet and use_merat once the sale is stored; the tokens' packet went out with their take.
    private static void Paid(GameSession session, Payment payment, long price) {
        if (payment.Wallet == null) {
            return;
        }
        if (payment.Red) {
            session.Currency.NotifyGameMeretTaken(price);
        } else {
            session.Currency.NotifyMeretTaken(price);
        }
    }

    private static void SendSoldOut(GameSession session, long listingId) {
        session.Send(MesoMarketPacket.Purchase(listingId, session.Player.Value.Account.MesoMarketPurchased, s_mesoMarket_error_alreadySoldProduct));
    }

    // The seller is paid the price less the fee in the currency the register page shows for it (MesoMarketIconType,
    // BlackMarketPageMesoRegister::RefreshAll, uiblackmarketpagemeso.cpp:696): 0 the merets (currency 3), 2 the red
    // merets (currency 9). 1 shows currency 10, the red merets else the merets, which a mail cannot hold as one: paid
    // here in red merets (chosen; no locale's row has it).
    private static (Mail Buyer, Mail Seller) PurchaseMails(GameSession session, MesoListing listing, int iconType) {
        var buyerMail = new Mail {
            ReceiverId = session.CharacterId,
            Type = MailType.MesoMarket,
            ContentArgs = [
                ("money", $"{listing.Amount}"),
                ("money", $"{listing.Price}"),
            ],
            Meso = listing.Amount,
        };
        buyerMail.SetSenderName(StringCode.s_mesoMarket_mail_to_sender);
        buyerMail.SetTitle(StringCode.s_mesoMarket_mail_to_buyer_title);
        buyerMail.SetContent(StringCode.s_mesoMarket_mail_to_buyer_content);

        int meretFee = (int) (listing.Price * Constant.MesoMarketTaxRate);
        var sellerMail = new Mail {
            ReceiverId = listing.CharacterId,
            Type = MailType.MesoMarket,
            ContentArgs = [
                ("money", $"{listing.Amount}"),
                ("money", $"{listing.Price}"),
                ("money", $"{meretFee}"),
                ("money", $"{(int) (Constant.MesoMarketTaxRate * 100)}"),
                ("money", $"{listing.Price - meretFee}"),
            ],
        };
        if (iconType is 1 or 2) {
            sellerMail.GameMeret = listing.Price - meretFee;
        } else {
            sellerMail.Meret = listing.Price - meretFee;
        }
        sellerMail.SetSenderName(StringCode.s_mesoMarket_mail_to_sender);
        sellerMail.SetTitle(StringCode.s_mesoMarket_mail_to_seller_title);
        sellerMail.SetContent(StringCode.s_mesoMarket_mail_to_seller_content);

        return (buyerMail, sellerMail);
    }
}

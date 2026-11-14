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

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
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

        MesoListing? listing;
        bool saving = false;
        try {
            listing = CreateListing(session, amount, price, ref saving);
        } catch {
            // Given back only for a throw before the listing's save: from the save on it may be stored.
            if (!saving) {
                session.Currency.ReturnMeso(amount, notify: false);
            }
            throw;
        }
        if (listing == null) {
            session.Currency.ReturnMeso(amount, notify: false);
            return;
        }

        session.Player.Value.Account.MesoMarketListed++;
        session.Currency.NotifyMeso();
        session.Send(MesoMarketPacket.Create(listing, session.Player.Value.Account.MesoMarketListed));
        session.Send(MesoMarketPacket.Quota(session.Player.Value.Account.MesoMarketListed, session.Player.Value.Account.MesoMarketPurchased));
    }

    // The checks after the balance, in CanRegister's order, and the listing stored; null (the refusal sent) when
    // refused.
    private MesoListing? CreateListing(GameSession session, long amount, long price, ref bool saving) {
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

        saving = true;
        MesoListing? created = db.CreateMesoListing(listing);
        if (created == null) {
            session.Send(MesoMarketPacket.Error(s_mesoMarket_error_errorDB));
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

        // The client asks only once its protection state is 7 (AdventureProtectRules); the server checks it again.
        if (!session.AdventureProtect.Cleared) {
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

        // The tokens are taken before the sale and given back when it is not stored: the old take after the sale threw
        // when the tokens were spent on another thread meanwhile (a shop, a beauty salon), with the sale and its mails
        // already stored, mesos for nothing.
        if (!session.Currency.TryTake(CurrencyType.MesoToken, listing.Price)) {
            session.Send(MesoMarketPacket.Error(s_err_lack_meso_makret_token_ask));
            return;
        }

        // The listing's end and both mails are one save: a sale without its mails would lose the buyer's meso or
        // the seller's price.
        (Mail Buyer, Mail Seller)? mails = null;
        try {
            (Mail buyerMail, Mail sellerMail) = PurchaseMails(session, listing);
            mails = db.SellMesoListing(listingId, buyerMail, sellerMail);
        } finally {
            if (mails == null) {
                session.Currency.Change(CurrencyType.MesoToken, listing.Price);
            }
        }
        if (mails == null) {
            SendSoldOut(session, listingId);
            return;
        }

        session.Player.Value.Account.MesoMarketPurchased++;

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

    private static void SendSoldOut(GameSession session, long listingId) {
        session.Send(MesoMarketPacket.Purchase(listingId, session.Player.Value.Account.MesoMarketPurchased, s_mesoMarket_error_alreadySoldProduct));
    }

    private static (Mail Buyer, Mail Seller) PurchaseMails(GameSession session, MesoListing listing) {
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
            Meret = listing.Price - meretFee,
        };
        sellerMail.SetSenderName(StringCode.s_mesoMarket_mail_to_sender);
        sellerMail.SetTitle(StringCode.s_mesoMarket_mail_to_seller_title);
        sellerMail.SetContent(StringCode.s_mesoMarket_mail_to_seller_content);

        return (buyerMail, sellerMail);
    }
}

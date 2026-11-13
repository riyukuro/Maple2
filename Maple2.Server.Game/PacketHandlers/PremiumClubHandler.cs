using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class PremiumClubHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.PremiumClub;

    // Set once the missing VIPServicePeriodLimitDay has been logged.
    private static int missingLimitLogged;

    private enum Command : byte {
        LoadClaimedItems = 1,
        ClaimItem = 2,
        LoadPackages = 3,
        PurchasePackage = 4,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.LoadClaimedItems:
                HandleLoadClaimedItems(session);
                return;
            case Command.ClaimItem:
                HandleClaimItem(session, packet);
                return;
            case Command.LoadPackages:
                HandleLoadPackages(session);
                return;
            case Command.PurchasePackage:
                HandlePurchasePackage(session, packet);
                break;
        }
    }

    private void HandleLoadClaimedItems(GameSession session) {
        session.Send(PremiumCubPacket.LoadItems(session.Player.Value.Account.PremiumRewardsClaimed));
    }

    private void HandleClaimItem(GameSession session, IByteReader packet) {
        int benefitId = packet.ReadInt();

        if (session.Player.Value.Account.PremiumTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
            return;
        }

        if (!TableMetadata.PremiumClubTable.Items.TryGetValue(benefitId, out PremiumClubTable.Item? premiumMetadata)) {
            return;
        }

        Item? item = session.Field?.ItemDrop.CreateItem(premiumMetadata.Id, premiumMetadata.Rarity, premiumMetadata.Amount);
        if (item == null || !session.Item.Inventory.CanAdd(item)) {
            return;
        }

        if (session.Player.Value.Account.PremiumRewardsClaimed.Contains(benefitId)) {
            return;
        }

        session.Player.Value.Account.PremiumRewardsClaimed.Add(benefitId);

        session.Item.Inventory.Add(item, true);
        session.Send(PremiumCubPacket.ClaimItem(benefitId));
    }

    private void HandleLoadPackages(GameSession session) {
        session.Send(PremiumCubPacket.LoadPackages(Remaining(session.Player.Value.Account, DateTime.Now)));
    }

    private static bool OnSale(PremiumClubTable.Package package) {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return !package.Disabled && (package.StartDate <= 0 || now >= package.StartDate) && (package.EndDate <= 0 || now <= package.EndDate);
    }

    // Not disabled and its sale not over. The dialog hides a goods past its salesEndDate itself and shows one before
    // its salesStartDate greyed (disableType 1), but only a limited goods with an entry above 0
    // (uivipgoodsshopdialog.cpp:330-334, FUN_140fbb940 :399-406): one not yet on sale is listed too.
    private static bool Listed(PremiumClubTable.Package package) {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return !package.Disabled && (package.EndDate <= 0 || now <= package.EndDate);
    }

    // Every listed goods with a purchase limit and the purchases the account has left of it. A limited goods
    // without an entry is hidden, so one whose sale is over (KR 1101) is not listed.
    private List<(int GoodsId, int Remaining)> Remaining(Account account, DateTime now) {
        return TableMetadata.PremiumClubTable.Packages
            .Where(entry => entry.Value.BuyLimit > 0 && Listed(entry.Value))
            .OrderBy(entry => entry.Key)
            .Select(entry => (entry.Key, VipGoodsRules.Remaining(account.VipGoodsPurchases, entry.Key, entry.Value.BuyLimit!.Value,
                entry.Value.BuyLimitResetType, now)))
            .ToList();
    }

    // Everything is checked before anything changes, so a refused purchase leaves the account as it was. The charge,
    // the purchase count and the VIP time (the player row) and the bonus items' bag rows are then stored together in
    // one transaction (TrySavePurchase); when that store fails the session is put back and nothing is given. The
    // bonus items must all fit in the bag: a mail would be stored in its own request.
    private void HandlePurchasePackage(GameSession session, IByteReader packet) {
        int packageId = packet.ReadInt();

        if (!TableMetadata.PremiumClubTable.Packages.TryGetValue(packageId, out PremiumClubTable.Package? premiumMetadata)) {
            return;
        }

        if (!OnSale(premiumMetadata)) {
            return;
        }

        // A metadata db ingested before the price and limit fields: its limit is unknown, so nothing is sold.
        if (premiumMetadata.BuyLimit is null) {
            Logger.Error("VIP goods {GoodsId}: the metadata db has no purchase limit (ingested before it), re-ingest; purchase refused", packageId);
            return;
        }

        DateTime now = DateTime.Now;
        Account account = session.Player.Value.Account;
        // The VIP time can be extended only within VIPServicePeriodLimitDay days of its end: the dialog refuses a VIP
        // whose time left is over that window with s_vip_dialog_goods_buy_limit and sends nothing
        // (UIVIPGoodsShopDialog::OnClickBuyGoods, uivipgoodsshopdialog.cpp:412-433; the constant at +0x6f40 is
        // VIPServicePeriodLimitDay, KMS2_DEV exe 0x1402af3e9). The same check here keeps a crafted packet from it.
        // A server-table db ingested before the constant (or a locale with no row) reads 0: the window is unknown, so
        // the check is skipped (logged once) rather than refusing every active VIP.
        int limitDays = session.ServerTableMetadata.ConstantsTable.VIPServicePeriodLimitDay;
        if (limitDays <= 0) {
            if (Interlocked.Exchange(ref missingLimitLogged, 1) == 0) {
                Logger.Error("VIPServicePeriodLimitDay is {Days} in the server-table db (ingested before it?), re-ingest; the VIP extension window is not checked", limitDays);
            }
        } else if (account.PremiumTime > now.ToEpochSeconds() && account.PremiumTime - now.ToEpochSeconds() > (long) limitDays * 24 * 60 * 60) {
            Logger.Warning("VIP goods {GoodsId}: VIP time ends {PremiumTime}, more than {Days} days away; purchase refused",
                packageId, account.PremiumTime, limitDays);
            return;
        }

        if (premiumMetadata.BuyLimit > 0 && VipGoodsRules.Remaining(account.VipGoodsPurchases, packageId, premiumMetadata.BuyLimit.Value,
                premiumMetadata.BuyLimitResetType, now) <= 0) {
            // The dialog hides the goods once it has the count.
            session.Send(PremiumCubPacket.LoadPackages(Remaining(account, now)));
            return;
        }

        // The client bills the sale price, in red merets for priceType 1 (GetPayType 1 -> pay type 9). A goods without
        // one charges its price, as before (no locale has salePrice 0 with a price), so it is never given for free.
        long price = premiumMetadata.SalePrice > 0 ? premiumMetadata.SalePrice : premiumMetadata.Price;
        if (premiumMetadata.RedMeret ? session.Currency.CanAddGameMeret(-price) != -price : session.Currency.CanAddMeret(-price) != -price) {
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message,
                premiumMetadata.RedMeret ? StringCode.s_err_lack_merat_red : StringCode.s_err_lack_merat));
            return;
        }

        var bonusItems = new List<Item>();
        foreach (PremiumClubTable.Item item in premiumMetadata.BonusItems) {
            Item? bonusItem = session.Field?.ItemDrop.CreateItem(item.Id, item.Rarity, item.Amount);
            if (bonusItem == null) {
                // No field (a map change) or an unknown id: refused before anything is charged.
                Logger.Warning("VIP goods {GoodsId}: bonus item {ItemId} not created, purchase refused", packageId, item.Id);
                return;
            }
            // bonusItemPeriod is in minutes: the bill shows it as CTimeSpan(period * 60) seconds
            // (uivipgoodsshopdialog.cpp:505-507) instead of the count. The item lasts that long from the purchase.
            if (item.Period > 0) {
                // Epoch seconds plus the minutes: adding them to local time would be an hour off across a DST change.
                bonusItem.ExpiryTime = now.ToEpochSeconds() + item.Period * 60L;
            }
            bonusItems.Add(bonusItem);
        }

        if (!session.Item.Inventory.CanAdd(bonusItems)) {
            session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
            return;
        }

        // The charge is taken in one step (another thread may change the merets meanwhile) and given back when the
        // store fails; the other values are put back from these.
        if (price > 0 && !(premiumMetadata.RedMeret
                ? session.Currency.TryTakeGameMeret(price, notify: false)
                : session.Currency.TryTakeMeret(price, notify: false))) {
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message,
                premiumMetadata.RedMeret ? StringCode.s_err_lack_merat_red : StringCode.s_err_lack_merat));
            return;
        }
        long premiumTime = account.PremiumTime, characterPremiumTime = session.Player.Value.Character.PremiumTime;
        List<VipGoodsPurchase> purchases = account.VipGoodsPurchases
            .Select(entry => new VipGoodsPurchase { GoodsId = entry.GoodsId, Count = entry.Count, LastPurchase = entry.LastPurchase })
            .ToList();
        bool started = !session.Config.IsPremiumClubActive();

        if (premiumMetadata.BuyLimit > 0) {
            VipGoodsRules.Record(account.VipGoodsPurchases, packageId, premiumMetadata.BuyLimitResetType, now);
        }
        account.PremiumTime = ConfigManager.PremiumTimeAfter(account.PremiumTime, premiumMetadata.Period, now);
        session.Player.Value.Character.PremiumTime = account.PremiumTime;

        var stored = new List<Item>();
        if (!session.TrySavePurchase(db => {
            if (!db.SavePlayer(session.Player)) {
                return false;
            }
            foreach (Item bonusItem in bonusItems) {
                Item? row = db.CreateItem(session.CharacterId, bonusItem);
                if (row == null) {
                    return false;
                }
                stored.Add(row);
            }
            return true;
        })) {
            Logger.Error("VIP goods {GoodsId}: purchase not stored, nothing charged or given AccountId={AccountId}", packageId, session.AccountId);
            if (premiumMetadata.RedMeret) {
                session.Currency.ReturnGameMeret(price);
            } else {
                session.Currency.ReturnMeret(price);
            }
            account.VipGoodsPurchases = purchases;
            account.PremiumTime = premiumTime;
            session.Player.Value.Character.PremiumTime = characterPremiumTime;
            // The client has no text for a purchase the server could not store; the currency packets put the shown
            // merets back, and the goods stay as they were.
            return;
        }

        foreach (Item row in stored) {
            if (!session.Item.Inventory.Add(row, true)) {
                // The room was checked above; the stored row is the character's bag item, so the next login has it.
                Logger.Error("VIP goods {GoodsId}: bonus item {ItemId} stored but not added to the bag of {CharacterId}", packageId, row.Id, session.CharacterId);
            }
        }
        if (price > 0) {
            if (premiumMetadata.RedMeret) {
                session.Currency.NotifyGameMeretTaken(price);
            } else {
                session.Currency.NotifyMeretTaken(price);
            }
        }
        session.Config.PremiumTimeChanged(started);

        session.Send(PremiumCubPacket.PurchasePackage(packageId));
        if (premiumMetadata.BuyLimit > 0) {
            session.Send(PremiumCubPacket.LoadPackages(Remaining(account, now)));
        }
    }
}

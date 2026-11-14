using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class UgcMarketManager {
    private readonly GameSession session;

    private readonly IDictionary<long, UgcMarketItem> items;
    private IDictionary<long, SoldUgcMarketItem> sales;
    private IDictionary<long, PlayerInfo> favoriteDesigners;

    private readonly ILogger logger = Log.Logger.ForContext<UgcMarketManager>();
    // Guards the listing set: the expiry check (ExpireDue) runs on the field's update, the requests on the session's.
    private readonly object listingLock = new();

    public UgcMarketManager(GameSession session) {
        this.session = session;

        using GameStorage.Request db = session.GameStorage.Context();
        items = db.GetUgcListingsByAccountId(session.AccountId);
        sales = db.GetMySoldUgcListings(session.AccountId);
        favoriteDesigners = new Dictionary<long, PlayerInfo>();
        IList<long> designerList = session.Config.GetFavoriteDesigners();

        foreach (long characterId in designerList) {
            if (!session.PlayerInfo.GetOrFetch(characterId, out PlayerInfo? playerInfo)) {
                continue;
            }
            favoriteDesigners.Add(characterId, playerInfo);
        }
    }

    public void Load() {
        using (GameStorage.Request db = session.GameStorage.Context()) {
            LoadSalesCounts(db);
            ExpireEnded(db, DateTime.Now.ToEpochSeconds());
        }
        session.Send(MeretMarketPacket.LoadPersonalListings(items.Values, SalesTotal(items.Values)));
    }

    // The seller's sales total as far as the server records it: no running total is stored, so this is the
    // price times the sales count of the listings the account still has. A removed listing no longer counts.
    public static long SalesTotal(IEnumerable<UgcMarketItem> listings) {
        long total = 0;
        foreach (UgcMarketItem listing in listings) {
            total += listing.Price * listing.SalesCount;
        }
        return total;
    }

    // The client takes a listing as expired only by its state (OnClickRemoveSale tests state 3), so a listing whose
    // time ran out is stored expired before the list goes out, with its kind: the free relist while it is unused.
    private List<UgcMarketItem> ExpireEnded(GameStorage.Request db, long now) {
        List<UgcMarketItem> ended;
        lock (listingLock) {
            ended = items.Values.Where(listing => listing.ExpireIfEnded(now)).ToList();
        }
        if (ended.Count > 0 && !db.SaveUgcMarketItems(ended)) {
            logger.Error("Unable to store {Count} expired UGC market listings of account {AccountId}.", ended.Count, session.AccountId);
            return [];
        }
        return ended;
    }

    // Once a second (FieldPlayer.Update): a listing whose time runs out while its seller is online is stored expired
    // and sent as 0x15, which the client reads unasked (CMeratMarketMan::OnProductSaleStateChanged,
    // meratmarketman.cpp:2364: it updates the product if it has it and refreshes My Shop), so the remove button
    // offers the relist without a reload. A client without the product ignores it.
    public void ExpireDue(long now) {
        lock (listingLock) {
            if (!items.Values.Any(listing => listing.Status == UgcMarketListingStatus.Active && listing.ListingEndTime <= now)) {
                return;
            }
        }
        using GameStorage.Request db = session.GameStorage.Context();
        foreach (UgcMarketItem item in ExpireEnded(db, now)) {
            session.Send(MeretMarketPacket.UpdateExpiration(item));
        }
    }

    // The sales counts as stored: a buyer's sale changes the stored listing, not this session's copy.
    private void LoadSalesCounts(GameStorage.Request db) {
        foreach ((long id, int count) in db.GetUgcSalesCounts(session.AccountId)) {
            if (items.TryGetValue(id, out UgcMarketItem? item)) {
                item.SalesCount = count;
            }
        }
    }

    // One sale of a listing, stored for its seller: the sale's entry with the seller's share at the sales total
    // before this sale, and the listing's sales count.
    public static SoldUgcMarketItem? RecordSale(GameStorage.Request db, UgcMarketItem listing) {
        long salesTotal = SalesTotal(db.GetUgcListingsByAccountId(listing.SellerAccountId).Values);
        long profit = UgcMarketRules.Profit(UgcMarketRules.CostRows, salesTotal, listing.Price);
        SoldUgcMarketItem? sold = db.AddUgcMarketSale(listing.Id, new SoldUgcMarketItem {
            AccountId = listing.SellerAccountId,
            Price = listing.Price,
            Name = listing.Look.Name,
            Profit = profit,
            // Set here: the column's default only applies when no value is written, and the entry's 0 was
            // written as 1970, which the client shows as the sale's date with the payment already due.
            SoldTime = DateTime.Now.ToEpochSeconds(),
        });
        if (sold != null) {
            listing.SalesCount++;
        }
        return sold;
    }

    public void RefreshSales() {
        using GameStorage.Request db = session.GameStorage.Context();
        Save(db);
        LoadSalesCounts(db);
        sales.Clear();
        sales = db.GetMySoldUgcListings(session.AccountId);
        session.Send(MeretMarketPacket.LoadSales(sales.Values));
    }

    // The count the register fee goes by (CMeratMarketBasketInfo, 0x1406a50b0): the account's listings on sale that
    // are not music scores.
    public int ActiveListingCount() {
        return items.Values.Count(listing => listing.Status == UgcMarketListingStatus.Active && !new ItemType(listing.ItemMetadata.Id).IsMusicScore);
    }

    public void ListItem(UgcMarketItem item) {
        lock (listingLock) {
            items.Add(item.Id, item);
        }
        session.Send(MeretMarketPacket.ListItem(item));
        session.Send(MeretMarketPacket.UpdateExpiration(item));
    }

    public void CollectProfit(long id) {
        if (!sales.TryGetValue(id, out SoldUgcMarketItem? sale) ||
            sale.SoldTime.FromEpochSeconds().AddDays(Constant.UGCShopProfitDelayDays) > DateTime.Now) {
            return;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.DeleteSoldUgcMarketItem(id)) {
            logger.Error("Unable to collect profit and delete SoldUgcMarketItem {Id}.", id);
            return;
        }
        sales.Remove(id);
        LoadSalesCounts(db);

        session.Currency.ChangeMeret(sale.Profit);
        session.Send(MeretMarketPacket.CollectProfit(sale.Id, sale.Profit));
        session.Send(MeretMarketPacket.UpdateProfit(SalesTotal(items.Values)));
    }

    public void RemoveListing(long id) {
        if (!items.TryGetValue(id, out UgcMarketItem? listing) || !UgcRules.ListingDeleteAllowed(listing.Status)) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.DeleteUgcMarketItem(id)) {
            return;
        }

        lock (listingLock) {
            items.Remove(id);
        }
        session.Send(MeretMarketPacket.RemoveListing(id));
    }

    public void UnlistItem(long id) {
        if (!items.TryGetValue(id, out UgcMarketItem? item) || !UgcRules.ListingStopAllowed(item.Status)) {
            return;
        }

        item.ListingEndTime = 0;
        item.PromotionEndTime = 0;
        item.Status = UgcMarketListingStatus.Expired;
        item.RelistKind = UgcMarketItem.StoppedKind; // a stopped sale is relisted for the fee (myshop_ask_stopsale)

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.SaveUgcMarketItem(item)) {
            return;
        }

        session.Send(MeretMarketPacket.UpdateExpiration(item));
    }

    // MeretMarket 19, the free first relist (OnAskRemoveSale after box s_meratmarket_free_resale_confirm): an
    // expired listing of kind FreeRelistKind goes on sale again for saleDays days with no fee. The stored row changes
    // first, in one conditional write (FreeRelistUgcMarketItem); this copy and the answer follow only when it changed,
    // so a repeated request finds the relist spent and is refused. No merets move. Returns the refusal's error code,
    // or null when the listing is on sale again.
    public int? FreeRelist(long id, int saleDays) {
        if (!items.TryGetValue(id, out UgcMarketItem? item)) {
            return MeretMarketError.NotSale;
        }
        long now = DateTime.Now.ToEpochSeconds();
        using GameStorage.Request db = session.GameStorage.Context();
        if (item.ExpireIfEnded(now) && !db.SaveUgcMarketItem(item)) {
            return MeretMarketError.Common;
        }
        if (item.Status == UgcMarketListingStatus.Active) {
            return MeretMarketError.AlreadySale;
        }
        if (item.Status != UgcMarketListingStatus.Expired || item.RelistKind != UgcMarketItem.FreeRelistKind || item.FreeRelistUsed) {
            return MeretMarketError.Common;
        }

        long end = DateTime.Now.AddDays(saleDays).ToEpochSeconds();
        if (!db.FreeRelistUgcMarketItem(id, session.AccountId, end)) {
            return MeretMarketError.Common;
        }
        item.ListingEndTime = end; // before the state, so the expiry check never sees it on sale with the old end
        item.PromotionEndTime = 0;
        item.RelistKind = 0;
        item.FreeRelistUsed = true;
        item.Status = UgcMarketListingStatus.Active;

        session.Send(MeretMarketPacket.FreeRelist(item));
        session.Send(MeretMarketPacket.UpdateExpiration(item));
        return null;
    }

    public const int MaxDesigners = 16;

    public void AddDesigner(long characterId) {
        if (favoriteDesigners.ContainsKey(characterId)) {
            LoadDesignerItems(characterId);
            return;
        }
        // The client keeps 16 bookmarks and drops a 17th unread (CMeratMarketMan::AddBookMark, 0x140d56a40), but
        // sends the add without counting (OnClickBookMark), so the cap is the server's: a 17th is not stored.
        if (favoriteDesigners.Count >= MaxDesigners) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        if (!session.PlayerInfo.GetOrFetch(characterId, out PlayerInfo? info)) {
            return;
        }

        favoriteDesigners.Add(characterId, info);
        session.Config.AddFavoriteDesigner(characterId);
        IList<UgcMarketItem> designerItems = db.GetUgcListingsByCharacterId(characterId);
        session.Send(MeretMarketPacket.AddDesigner(info, designerItems));
    }

    public void RemoveDesigner(long characterId) {
        if (!favoriteDesigners.Remove(characterId)) {
            return;
        }
        session.Config.RemoveFavoriteDesigner(characterId);

        session.Send(MeretMarketPacket.RemoveDesigner(characterId));
    }

    public void LoadDesigners() {
        session.Send(MeretMarketPacket.LoadDesigners(favoriteDesigners.Values));
    }

    /// <summary>
    /// Fetch the designers' active listings.
    /// </summary>
    public void LoadDesignerItems(long characterId) {
        if (!favoriteDesigners.TryGetValue(characterId, out PlayerInfo? playerInfo)) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        IList<UgcMarketItem> designerItems = db.GetUgcListingsByCharacterId(characterId);

        session.Send(MeretMarketPacket.LoadDesignerItems(playerInfo, designerItems));
    }

    // The account's own listings, in any state: the client's product list (m_products), which it caps at 100
    // (FUN_140ebc4a0 before the register dialog opens and before it sends).
    public int ListingCount => items.Count;

    // A design already listed by the account (IsAlreadyListed: FindProductByDesignUid on the item's design uid).
    public bool IsDesignListed(long designUid) {
        return designUid != 0 && items.Values.Any(listing => listing.Look.Id == designUid);
    }

    public bool TryGetItem(long id, [NotNullWhen(true)] out UgcMarketItem? item) {
        return items.TryGetValue(id, out item);
    }

    public void Save(GameStorage.Request db) {
        db.SaveUgcMarketItems(items.Values);
        db.SaveSoldUgcMarketItems(sales.Values);
    }
}

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
        if (!db.AddUgcMarketSale(listing.Id)) {
            return null;
        }
        listing.SalesCount++;

        return db.CreateSoldUgcMarketItem(new SoldUgcMarketItem {
            AccountId = listing.SellerAccountId,
            Price = listing.Price,
            Name = listing.Look.Name,
            Profit = profit,
        });
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
        items.Add(item.Id, item);
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

        session.Currency.Meret += sale.Profit;
        session.Send(MeretMarketPacket.CollectProfit(sale.Id, sale.Profit));
        session.Send(MeretMarketPacket.UpdateProfit(SalesTotal(items.Values)));
    }

    public void RemoveListing(long id) {
        if (!items.ContainsKey(id)) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.DeleteUgcMarketItem(id)) {
            return;
        }

        items.Remove(id);
        session.Send(MeretMarketPacket.RemoveListing(id));
    }

    public void UnlistItem(long id) {
        if (!items.TryGetValue(id, out UgcMarketItem? item)) {
            return;
        }

        item.ListingEndTime = 0;
        item.PromotionEndTime = 0;
        item.Status = UgcMarketListingStatus.Expired;

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.SaveUgcMarketItem(item)) {
            return;
        }

        session.Send(MeretMarketPacket.UpdateExpiration(item));
    }

    public void AddDesigner(long characterId) {
        if (favoriteDesigners.ContainsKey(characterId)) {
            LoadDesignerItems(characterId);
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

    public bool TryGetItem(long id, [NotNullWhen(true)] out UgcMarketItem? item) {
        return items.TryGetValue(id, out item);
    }

    public void Save(GameStorage.Request db) {
        db.SaveUgcMarketItems(items.Values);
        db.SaveSoldUgcMarketItems(sales.Values);
    }
}

using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using BlackMarketListing = Maple2.Model.Game.BlackMarketListing;
using Item = Maple2.Model.Game.Item;
using MesoListing = Maple2.Model.Game.MesoListing;
using PremiumMarketItem = Maple2.Model.Game.PremiumMarketItem;
using SoldUgcMarketItem = Maple2.Model.Game.SoldUgcMarketItem;
using UgcMarketItem = Maple2.Model.Game.UgcMarketItem;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // TODO: Should this filter out your own listings?
        public ICollection<MesoListing> SearchMesoListings(int pageSize, long minAmount = 0, long maxAmount = long.MaxValue) {
            return Context.MesoMarket.Where(listing => listing.ExpiryTime > DateTime.Now)
                .Where(listing => listing.Amount >= minAmount)
                .Where(listing => listing.Amount <= maxAmount)
                .OrderBy(listing => listing.Price)
                .Take(pageSize)
                .AsEnumerable()
                .Select<Model.MesoListing, MesoListing>(listing => listing)
                .ToList();
        }

        public ICollection<MesoListing> GetMyMesoListingsByAccountId(long accountId) {
            return Context.MesoMarket.Where(listing => listing.AccountId == accountId)
                .AsEnumerable()
                .Select<Model.MesoListing, MesoListing>(listing => listing)
                .ToList();
        }

        public ICollection<MesoListing> GetMyMesoListingsByCharacterId(long characterId) {
            return Context.MesoMarket.Where(listing => listing.CharacterId == characterId)
                .AsEnumerable()
                .Select<Model.MesoListing, MesoListing>(listing => listing)
                .ToList();
        }

        // The amount and price of the last sold listings.
        public IList<(long Amount, long Price)> GetSoldMesoListings(int count) {
            return Context.MesoMarketSold.OrderByDescending(listing => listing.SoldTime)
                .ThenByDescending(listing => listing.Id)
                .Take(count)
                .Select(listing => new { listing.Amount, listing.Price })
                .AsEnumerable()
                .Select(listing => (listing.Amount, listing.Price))
                .ToList();
        }

        public MesoListing? GetMesoListing(long listingId) {
            return Context.MesoMarket.Find(listingId);
        }

        public MesoListing? CreateMesoListing(MesoListing listing) {
            Model.MesoListing model = listing;
            Context.MesoMarket.Add(model);

            return Context.TrySaveChanges() ? model : null;
        }

        // A sale in one save: the listing moves to the sold table and both mails are made, or nothing changes
        // (the listing is gone, another buyer or its owner was first, or the save failed).
        public (Maple2.Model.Game.Mail Buyer, Maple2.Model.Game.Mail Seller)? SellMesoListing(long listingId, Maple2.Model.Game.Mail buyerMail, Maple2.Model.Game.Mail sellerMail) {
            Model.MesoListing? listing = Context.MesoMarket.Find(listingId);
            if (listing == null) {
                return null;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            buyerMail.SetDefaultExpiry(game.serverTableMetadata.ConstantsTable.MailExpiryDays, now);
            sellerMail.SetDefaultExpiry(game.serverTableMetadata.ConstantsTable.MailExpiryDays, now);
            Model.Mail buyer = buyerMail!;
            Model.Mail seller = sellerMail!;
            buyer.Id = 0;
            seller.Id = 0;

            SoldMesoListing soldListing = listing;
            Context.MesoMarket.Remove(listing);
            Context.MesoMarketSold.Add(soldListing);
            Context.Mail.Add(buyer);
            Context.Mail.Add(seller);
            if (!Context.TrySaveChanges()) {
                return null;
            }
            return (buyer!, seller!);
        }

        public bool DeleteMesoListing(long listingId, bool sold = false) {
            Model.MesoListing? listing = Context.MesoMarket.Find(listingId);
            if (listing == null) {
                return false;
            }

            if (sold) {
                SoldMesoListing soldListing = listing;
                Context.MesoMarket.Remove(listing);
                Context.MesoMarketSold.Add(soldListing);
                return Context.TrySaveChanges();
            }

            Context.MesoMarket.Remove(listing);
            return Context.TrySaveChanges();
        }

        public IDictionary<long, UgcMarketItem> GetUgcListingsByAccountId(long accountId) {
            return Context.UgcMarketItem.Where(listing => listing.AccountId == accountId)
                .AsEnumerable()
                .Select(ToMarketEntry)
                .Where(x => x is not null)
                .ToDictionary(entry => entry!.Id, entry => entry!);
        }

        /// <summary>
        ///  Get active UGC listings by character Id
        /// </summary>
        public IList<UgcMarketItem> GetUgcListingsByCharacterId(long characterId) {
            return Context.UgcMarketItem.Where(listing => listing.CharacterId == characterId && listing.ListingEndTime > DateTime.Now)
                .AsEnumerable()
                .Select(ToMarketEntry)
                .Where(x => x is not null)
                .ToList()!;
        }

        public IDictionary<long, SoldUgcMarketItem> GetMySoldUgcListings(long accountId) {
            return Context.SoldUgcMarketItem.Where(listing => listing.AccountId == accountId)
                .AsEnumerable()
                .Select<Model.SoldUgcMarketItem, SoldUgcMarketItem>(listing => listing)
                .ToDictionary(entry => entry.Id, entry => entry);
        }

        public UgcMarketItem? CreateUgcMarketItem(UgcMarketItem item) {
            Model.UgcMarketItem model = item;
            Context.UgcMarketItem.Add(model);

            return Context.TrySaveChanges() ? ToMarketEntry(model) : null;
        }

        // A listing is saved from the seller's copy, which is as old as the seller's login. The sales count belongs
        // to the buyers (AddUgcMarketSale), so a save leaves the stored count as it is.
        public bool SaveUgcMarketItems(ICollection<UgcMarketItem> items) {
            foreach (UgcMarketItem item in items) {
                Model.UgcMarketItem model = item;
                Context.UgcMarketItem.Update(model);
                Context.Entry(model).Property(listing => listing.SalesCount).IsModified = false;
            }

            return Context.TrySaveChanges();
        }

        public bool SaveUgcMarketItem(UgcMarketItem item) {
            Model.UgcMarketItem model = item;
            Context.UgcMarketItem.Update(model);
            Context.Entry(model).Property(listing => listing.SalesCount).IsModified = false;
            return Context.TrySaveChanges();
        }

        // One more sale of the listing, added to the stored count.
        public bool AddUgcMarketSale(long id) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            Model.UgcMarketItem? listing = Context.UgcMarketItem.Find(id);
            if (listing == null) {
                return false;
            }

            listing.SalesCount++;
            return Context.TrySaveChanges();
        }

        // The stored sales count of each of the account's listings.
        public IDictionary<long, int> GetUgcSalesCounts(long accountId) {
            return Context.UgcMarketItem.Where(listing => listing.AccountId == accountId)
                .Select(listing => new { listing.Id, listing.SalesCount })
                .AsEnumerable()
                .ToDictionary(listing => listing.Id, listing => listing.SalesCount);
        }

        public bool DeleteUgcMarketItem(long id) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            Model.UgcMarketItem? listing = Context.UgcMarketItem.Find(id);
            if (listing == null) {
                return false;
            }

            Context.UgcMarketItem.Remove(listing);
            return SaveChanges();
        }

        public SoldUgcMarketItem? CreateSoldUgcMarketItem(SoldUgcMarketItem item) {
            Model.SoldUgcMarketItem model = item;
            Context.SoldUgcMarketItem.Add(model);

            return Context.TrySaveChanges() ? model : null;
        }

        public bool SaveSoldUgcMarketItems(ICollection<SoldUgcMarketItem> items) {
            foreach (SoldUgcMarketItem item in items) {
                Model.SoldUgcMarketItem model = item;
                Context.SoldUgcMarketItem.Update(model);
            }

            return Context.TrySaveChanges();
        }

        public bool DeleteSoldUgcMarketItem(long id) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            Model.SoldUgcMarketItem? item = Context.SoldUgcMarketItem.Find(id);
            if (item == null) {
                return false;
            }

            Context.SoldUgcMarketItem.Remove(item);
            return SaveChanges();
        }

        public ICollection<UgcMarketItem> GetUgcMarketItems(params int[] tabIds) {
            if (tabIds.Length == 0) {
                return Context.UgcMarketItem
                    .Where(item => item.ListingEndTime > DateTime.Now)
                    .AsEnumerable()
                    .Select(ToMarketEntry)
                    .ToList()!;
            }

            return Context.UgcMarketItem
                .Where(item => item.ListingEndTime > DateTime.Now && tabIds.Contains(item.TabId))
                .AsEnumerable()
                .Select(ToMarketEntry)
                .ToList()!;
        }

        public ICollection<UgcMarketItem> GetUgcMarketPromotedItems() {
            ICollection<UgcMarketItem> items = Context.UgcMarketItem
                .Where(item => item.PromotionEndTime > DateTime.Now)
                .OrderBy(item => EF.Functions.Random())
                .Take(12)
                .AsEnumerable()
                .Select(ToMarketEntry)
                .ToList()!;

            foreach (UgcMarketItem item in items) {
                item.Category = UgcMarketHomeCategory.Promoted;
            }
            return items;
        }

        public ICollection<UgcMarketItem> GetUgcMarketNewItems() {
            ICollection<UgcMarketItem> items = Context.UgcMarketItem
                .Where(item => item.ListingEndTime > DateTime.Now)
                .OrderByDescending(item => item.CreationTime)
                .Take(6)
                .AsEnumerable()
                .Select(ToMarketEntry)
                .ToList()!;


            foreach (UgcMarketItem item in items) {
                item.Category = UgcMarketHomeCategory.New;
            }
            return items;
        }

        // One query for one blueprint: is a live listing selling it? The blueprint is a JSON column.
        public bool IsUgcMarketBlueprintListed(long blueprintUid) {
            return Context.UgcMarketItem
                .FromSql($"SELECT * FROM `ugc-market-item` WHERE JSON_EXTRACT(Blueprint, '$.BlueprintUid') = {blueprintUid}")
                .Any(item => item.ListingEndTime > DateTime.Now);
        }

        public UgcMarketItem? GetUgcMarketItem(long id) {
            Model.UgcMarketItem? item = Context.UgcMarketItem.Find(id);
            return ToMarketEntry(item);
        }

        // A row whose look or blueprint column is null (only a hand-made row; a listing made in game always
        // has both) cannot be written to the client, so it is left out.
        private UgcMarketItem? ToMarketEntry(Model.UgcMarketItem? model) {
            if (model?.Look == null || model.Blueprint == null) {
                return null;
            }
            model.Tags ??= [];
            model.Description ??= string.Empty;
            model.CharacterName ??= string.Empty;

            return game.TryGetItemMetadata(model.ItemId, out ItemMetadata? metadata) ? model.Convert(metadata) : null;
        }

        public bool CreateSoldMeretMarketItem(PremiumMarketItem item, long characterId) {
            SoldMeretMarketItem model = item;
            model.CharacterId = characterId;
            Context.SoldMeretMarketItem.Add(model);
            return Context.TrySaveChanges();
        }

        public BlackMarketListing? CreateBlackMarketingListing(BlackMarketListing listing) {
            Model.BlackMarketListing model = listing;
            model.Id = 0;
            Context.BlackMarketListing.Add(model);
            if (!SaveChanges()) {
                return null;
            }

            BlackMarketListing? created = ToBlackMarketingListing(model);
            if (created == null) {
                return null;
            }
            SaveItems(created.Id, created.Item);
            return Context.TrySaveChanges() ? created : null;
        }

        public IEnumerable<BlackMarketListing> GetBlackMarketListings(long characterId) {
            Model.BlackMarketListing[] models = Context.BlackMarketListing.Where(listing => listing.CharacterId == characterId)
                .AsEnumerable()
                .ToArray();


            foreach (Model.BlackMarketListing model in models) {
                BlackMarketListing? listing = ToBlackMarketingListing(model);
                if (listing != null) {
                    yield return listing;
                }
            }
        }

        public BlackMarketListing? GetBlackMarketListing(long listingId) {
            Model.BlackMarketListing? model = Context.BlackMarketListing.Find(listingId);
            return ToBlackMarketingListing(model);
        }

        public IEnumerable<BlackMarketListing> GetBlackMarketListings(params long[] listingIds) {
            Model.BlackMarketListing[] models = Context.BlackMarketListing.Where(listing => listingIds.Contains(listing.Id))
                .AsEnumerable()
                .ToArray();

            foreach (Model.BlackMarketListing model in models) {
                BlackMarketListing? listing = ToBlackMarketingListing(model);
                if (listing != null) {
                    yield return listing;
                }
            }
        }

        public IEnumerable<BlackMarketListing> GetAllBlackMarketListings() {
            Model.BlackMarketListing[] models = Context.BlackMarketListing
                .AsEnumerable()
                .ToArray();

            foreach (Model.BlackMarketListing model in models) {
                BlackMarketListing? listing = ToBlackMarketingListing(model);
                if (listing != null) {
                    yield return listing;
                }
            }
        }

        public bool DeleteBlackMarketListing(long listingId) {
            Model.BlackMarketListing? listing = Context.BlackMarketListing.Find(listingId);
            if (listing == null) {
                return false;
            }

            Context.BlackMarketListing.Remove(listing);
            return Context.TrySaveChanges();
        }

        public bool SaveBlackMarketListing(BlackMarketListing listing) {
            Model.BlackMarketListing model = listing;

            Context.BlackMarketListing.Update(model);
            return Context.TrySaveChanges();
        }

        private BlackMarketListing? ToBlackMarketingListing(Model.BlackMarketListing? model) {
            if (model == null) {
                return null;
            }

            Model.Item? itemModel = Context.Item.Find(model.ItemUid);
            if (itemModel == null) {
                return null;
            }

            Item? item = ToItem(itemModel);
            return item == null ? null : model.Convert(item);
        }
    }
}

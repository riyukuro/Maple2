using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
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

        // A sale in one transaction: the listing moves to the sold table and both mails are made, or nothing
        // changes (the listing is gone, another buyer or its owner was first, or a save failed). The listing's
        // row is deleted first, so the request that loses a race stops before it has written anything.
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
            using IDbContextTransaction transaction = Context.Database.BeginTransaction();
            Context.MesoMarket.Remove(listing);
            if (!Context.TrySaveChanges()) {
                return null; // the transaction is rolled back when it is disposed
            }
            Context.MesoMarketSold.Add(soldListing);
            Context.Mail.Add(buyer);
            Context.Mail.Add(seller);
            if (!Context.TrySaveChanges()) {
                return null;
            }
            transaction.Commit();
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
        // to the buyers (AddUgcMarketSale), so a save leaves the stored count as it is. The free relist is written
        // only once it is spent, so no copy gives it back; a copy that has not seen a relist (free or paid) the
        // stored row has (its FreeRelistUsed is false, the row's true) is older than that relist, and saving its
        // state, kind and end time would take the relisted listing off sale again, so it is not saved.
        public bool SaveUgcMarketItems(ICollection<UgcMarketItem> items) {
            HashSet<long> stale = CopiesBeforeRelist(items);
            foreach (UgcMarketItem item in items) {
                if (stale.Contains(item.Id)) {
                    continue;
                }
                Update(item);
            }

            return Context.TrySaveChanges();
        }

        // False for a copy older than the listing's relist (see SaveUgcMarketItems): nothing is written.
        public bool SaveUgcMarketItem(UgcMarketItem item) {
            if (CopiesBeforeRelist([item]).Count > 0) {
                return false;
            }
            Update(item);
            return Context.TrySaveChanges();
        }

        private void Update(UgcMarketItem item) {
            Model.UgcMarketItem model = item;
            Context.UgcMarketItem.Update(model);
            Context.Entry(model).Property(listing => listing.SalesCount).IsModified = false;
            Context.Entry(model).Property(listing => listing.FreeRelistUsed).IsModified = model.FreeRelistUsed;
        }

        // The listings of these copies whose stored free relist is spent while the copy's is not.
        private HashSet<long> CopiesBeforeRelist(ICollection<UgcMarketItem> items) {
            List<long> ids = items.Where(item => !item.FreeRelistUsed).Select(item => item.Id).ToList();
            if (ids.Count == 0) {
                return [];
            }
            return Context.UgcMarketItem.AsNoTracking()
                .Where(listing => ids.Contains(listing.Id) && listing.FreeRelistUsed)
                .Select(listing => listing.Id)
                .ToHashSet();
        }

        // The free first relist (MeretMarket 19): the account's listing goes on sale again until listingEnd and its
        // free relist is spent, in one conditional write. Only a stored listing that expired by time with the free
        // relist unused changes, so a second request, or one from an older copy, changes nothing and gets false.
        public bool FreeRelistUgcMarketItem(long id, long accountId, long listingEnd) {
            DateTime end = listingEnd.FromEpochSeconds();
            DateTime noPromotion = 0L.FromEpochSeconds(); // as a stopped listing stores it
            try {
                if (Context.Database.IsRelational()) {
                    return Context.UgcMarketItem
                        .Where(listing => listing.Id == id && listing.AccountId == accountId
                            && listing.Status == UgcMarketListingStatus.Expired
                            && listing.RelistKind == UgcMarketItem.FreeRelistKind && !listing.FreeRelistUsed)
                        .ExecuteUpdate(update => update
                            .SetProperty(listing => listing.Status, UgcMarketListingStatus.Active)
                            .SetProperty(listing => listing.ListingEndTime, end)
                            .SetProperty(listing => listing.PromotionEndTime, noPromotion)
                            .SetProperty(listing => listing.RelistKind, (byte) 0)
                            .SetProperty(listing => listing.FreeRelistUsed, true)) == 1;
                }

                Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                Model.UgcMarketItem? model = Context.UgcMarketItem.Find(id);
                if (model == null || model.AccountId != accountId || model.Status != UgcMarketListingStatus.Expired
                    || model.RelistKind != UgcMarketItem.FreeRelistKind || model.FreeRelistUsed) {
                    return false;
                }
                model.Status = UgcMarketListingStatus.Active;
                model.ListingEndTime = end;
                model.PromotionEndTime = noPromotion;
                model.RelistKind = 0;
                model.FreeRelistUsed = true;
                return Context.TrySaveChanges();
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed the free relist of UGC market item {Id}", id);
                return false;
            }
        }

        // One sale of the listing: one more on the stored count and the seller's sale entry, written together, so
        // a sale that cannot be stored leaves the count as it was.
        public SoldUgcMarketItem? AddUgcMarketSale(long id, SoldUgcMarketItem sold) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            Model.UgcMarketItem? listing = Context.UgcMarketItem.Find(id);
            if (listing == null) {
                return null;
            }

            listing.SalesCount++;
            Model.SoldUgcMarketItem model = sold;
            Context.SoldUgcMarketItem.Add(model);
            if (Context.TrySaveChanges()) {
                return model;
            }

            // Neither is written; nothing of the sale is left for a later save of this context.
            listing.SalesCount--;
            Context.Entry(listing).State = EntityState.Unchanged;
            Context.Entry(model).State = EntityState.Detached;
            return null;
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

        // The listing row and the item's move to it are one transaction: when either fails the caller mails the item
        // and the deposit back, and a listing row left behind would load at World start and could still be bought.
        public BlackMarketListing? CreateBlackMarketingListing(BlackMarketListing listing) {
            Model.BlackMarketListing model = listing;
            model.Id = 0;
            BeginTransaction();
            Context.BlackMarketListing.Add(model);
            if (!SaveChanges()) {
                Rollback();
                return null;
            }

            BlackMarketListing? created = ToBlackMarketingListing(model);
            if (created == null || !SaveItems(created.Id, created.Item)) {
                Rollback();
                return null;
            }
            return Commit() ? created : null;
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

        public int CountBlackMarketListings(long characterId) {
            return Context.BlackMarketListing.Count(listing => listing.CharacterId == characterId);
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

        // One buy or take-back at a time in this process; the conditional row change below covers other channels.
        private static readonly object BlackMarketLock = new();

        // Buys quantity of a listing at price and mails it to the buyer, all in one transaction: the listing's
        // count is lowered only while it still holds quantity at that price, so of two buyers racing on the last
        // of a listing one gets it and the other gets null (gone: the listing no longer exists). The whole stack
        // moves to the mail and the listing row is deleted when nothing is left; else the stack is split.
        // sellerMail makes the seller's mail from the listing as it is after the buy; it is stored in the same
        // transaction, so a buy whose seller mail cannot be made or saved changes nothing.
        // Returns the listing as it is after the buy; mail is the stored mail holding the bought item.
        public BlackMarketListing? BuyBlackMarketListing(long listingId, int quantity, long price, Maple2.Model.Game.Mail buyerMail,
                                                         Func<BlackMarketListing, Maple2.Model.Game.Mail>? sellerMail,
                                                         out Maple2.Model.Game.Mail? mail, out Maple2.Model.Game.Mail? sellerStored, out bool gone) {
            mail = null;
            sellerStored = null;
            gone = false;
            if (quantity <= 0) {
                return null;
            }

            // The context does not track queries by default; the rows changed below are loaded tracked.
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            Model.BlackMarketListing? model;
            Model.Item? itemModel;
            Model.Item soldModel;
            Model.Mail mailModel;
            Model.Mail? sellerModel = null;
            long written = 0; // the listed item's row: forgotten by item objects once the transaction ends (ForgetRows)
            lock (BlackMarketLock) {
                try {
                    using IDbContextTransaction transaction = Context.Database.BeginTransaction();
                    if (Context.Database.IsRelational()) {
                        int rows = Context.BlackMarketListing
                            .Where(row => row.Id == listingId && row.Quantity >= quantity && row.Price == price)
                            .ExecuteUpdate(update => update.SetProperty(row => row.Quantity, row => row.Quantity - quantity));
                        model = rows == 1 ? Context.BlackMarketListing.Find(listingId) : null;
                    } else {
                        model = Context.BlackMarketListing.Find(listingId);
                        if (model != null && model.Quantity >= quantity && model.Price == price) {
                            model.Quantity -= quantity;
                        } else {
                            model = null;
                        }
                    }
                    if (model == null) {
                        gone = !Context.BlackMarketListing.Any(row => row.Id == listingId);
                        return null;
                    }

                    itemModel = Context.Item.Find(model.ItemUid);
                    Item? listed = itemModel == null ? null : ToItem(itemModel);
                    if (itemModel == null || listed == null) {
                        Context.ChangeTracker.Clear();
                        return null; // the transaction is rolled back when it is disposed
                    }

                    // Both mails are made before the first save: a seller mail that cannot be made stops the buy
                    // while nothing is written.
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (sellerMail != null) {
                        Maple2.Model.Game.Mail seller = sellerMail(model.Convert(listed));
                        seller.SetDefaultExpiry(game.serverTableMetadata.ConstantsTable.MailExpiryDays, now);
                        sellerModel = seller!;
                        sellerModel.Id = 0;
                        Context.Mail.Add(sellerModel);
                    }
                    buyerMail.SetDefaultExpiry(game.serverTableMetadata.ConstantsTable.MailExpiryDays, now);
                    mailModel = buyerMail!;
                    mailModel.Id = 0;
                    Context.Mail.Add(mailModel);
                    Context.SaveChanges();

                    written = itemModel.Id;
                    ForgetRows([written]);
                    if (model.Quantity == 0) {
                        itemModel.OwnerId = mailModel.Id;
                        soldModel = itemModel;
                        Context.BlackMarketListing.Remove(model);
                    } else {
                        Item sold = listed.Clone();
                        sold.Amount = quantity;
                        soldModel = sold;
                        soldModel.OwnerId = mailModel.Id;
                        soldModel.Id = 0;
                        Context.Item.Add(soldModel);
                        itemModel.Amount = model.Quantity;
                    }
                    Context.SaveChanges();
                    transaction.Commit();
                } catch (Exception ex) {
                    Context.ChangeTracker.Clear();
                    Logger.LogError(ex, "Failed to buy {Quantity} of black market listing {ListingId}", quantity, listingId);
                    return null;
                } finally {
                    ForgetRows([written]);
                }
            }

            // The buy is stored. What follows only shapes the answer, outside the catch above: an error here
            // may not be read as a buy that did not happen.
            Maple2.Model.Game.Mail created = mailModel!;
            created.Items.Add(ToItem(soldModel)!);
            mail = created;
            if (sellerModel != null) {
                sellerStored = sellerModel!;
            }
            return model.Convert(ToItem(itemModel)!);
        }

        // Takes a listing off the market for its seller, in one transaction: the row is deleted, and only then
        // is the item read, so what a buyer took just before is not handed back as well; returnMail makes the
        // mail from the listing with the count that is left, and the item moves to that mail. A mail that cannot
        // be made or saved leaves the listing as it was. Null when the listing is gone or not characterId's.
        // The returned count is what is left of the item; mail is the stored mail holding it.
        public BlackMarketListing? TakeBlackMarketListing(long listingId, long characterId,
                                                          Func<BlackMarketListing, Maple2.Model.Game.Mail> returnMail,
                                                          out Maple2.Model.Game.Mail? mail) {
            mail = null;
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            BlackMarketListing listing;
            Model.Mail mailModel;
            Item? item;
            long written = 0; // as in BuyBlackMarket
            lock (BlackMarketLock) {
                try {
                    Model.BlackMarketListing? model = Context.BlackMarketListing.AsNoTracking()
                        .FirstOrDefault(row => row.Id == listingId && row.CharacterId == characterId);
                    if (model == null) {
                        return null;
                    }

                    using IDbContextTransaction transaction = Context.Database.BeginTransaction();
                    bool relational = Context.Database.IsRelational();
                    if (relational && Context.BlackMarketListing.Where(row => row.Id == listingId && row.CharacterId == characterId).ExecuteDelete() != 1) {
                        return null;
                    }

                    Model.Item? itemModel = Context.Item.Find(model.ItemUid);
                    item = itemModel == null ? null : ToItem(itemModel);
                    if (itemModel == null || item == null) {
                        Context.ChangeTracker.Clear();
                        return null; // the transaction is rolled back when it is disposed
                    }

                    model.Quantity = item.Amount;
                    listing = model.Convert(item);
                    Maple2.Model.Game.Mail made = returnMail(listing);
                    made.SetDefaultExpiry(game.serverTableMetadata.ConstantsTable.MailExpiryDays, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    mailModel = made!;
                    mailModel.Id = 0;
                    if (!relational) {
                        // The store of the tests has no transaction: the row goes in the same save as the mail.
                        Context.BlackMarketListing.Remove(model);
                    }
                    Context.Mail.Add(mailModel);
                    Context.SaveChanges();

                    written = itemModel.Id;
                    ForgetRows([written]);
                    itemModel.OwnerId = mailModel.Id;
                    Context.SaveChanges();
                    transaction.Commit();
                } catch (Exception ex) {
                    Context.ChangeTracker.Clear();
                    Logger.LogError(ex, "Failed to take black market listing {ListingId} off the market", listingId);
                    return null;
                } finally {
                    ForgetRows([written]);
                }
            }

            Maple2.Model.Game.Mail created = mailModel!;
            created.Items.Add(item);
            mail = created;
            return listing;
        }

        // Mails the seller the deposit of a listing that stays on the market (World refused it and it could not be
        // taken back), and sets the listing's deposit to 0 in the same transaction, so a later cancel after expiry
        // does not pay it twice. Answers the mail, or null when nothing was refunded.
        public Maple2.Model.Game.Mail? RefundBlackMarketDeposit(long listingId, long characterId, Func<long, Maple2.Model.Game.Mail> refundMail) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            lock (BlackMarketLock) {
                try {
                    using IDbContextTransaction transaction = Context.Database.BeginTransaction();
                    Model.BlackMarketListing? model = Context.BlackMarketListing
                        .FirstOrDefault(row => row.Id == listingId && row.CharacterId == characterId);
                    if (model == null || model.Deposit <= 0) {
                        return null;
                    }

                    Maple2.Model.Game.Mail made = refundMail(model.Deposit);
                    made.SetDefaultExpiry(game.serverTableMetadata.ConstantsTable.MailExpiryDays, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    Model.Mail mailModel = made;
                    mailModel.Id = 0;
                    model.Deposit = 0;
                    Context.Mail.Add(mailModel);
                    Context.SaveChanges();
                    transaction.Commit();
                    return mailModel;
                } catch (Exception ex) {
                    Context.ChangeTracker.Clear();
                    Logger.LogError(ex, "Failed to refund the deposit of black market listing {ListingId}", listingId);
                    return null;
                }
            }
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

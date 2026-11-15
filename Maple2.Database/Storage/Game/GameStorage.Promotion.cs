using Maple2.Database.Model;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // Takes one from a promotion limit's counter (Model.PromotionCount; characterId 0 for the account's): true and
        // stored when the counter, read as 0 once past its end, is below `limit`; the counter then ends at
        // `expiration` if it started again. False when the limit is reached or the write failed. A write that lost a
        // race with another session of the account (the count changed, or both added the row) reads again, three
        // tries in all.
        public bool TakePromotionCount(long accountId, long characterId, int shareId, int limit, long expiration, long now) {
            for (int tries = 0; tries < 3; tries++) {
                Context.ChangeTracker.Clear();
                // Tracked: the storage's queries are not tracked by default, and the changed count must be saved.
                PromotionCount? row = Context.PromotionCount.AsTracking()
                    .FirstOrDefault(count => count.AccountId == accountId && count.CharacterId == characterId && count.ShareId == shareId);
                if (row == null) {
                    if (limit <= 0) {
                        return false;
                    }
                    Context.PromotionCount.Add(new PromotionCount {
                        AccountId = accountId,
                        CharacterId = characterId,
                        ShareId = shareId,
                        Count = 1,
                        ExpirationTime = expiration,
                    });
                } else {
                    bool expired = row.ExpirationTime < now;
                    int count = expired ? 0 : row.Count;
                    if (count >= limit) {
                        return false;
                    }
                    row.Count = count + 1;
                    if (expired) {
                        row.ExpirationTime = expiration;
                    }
                }
                try {
                    Context.SaveChanges();
                    return true;
                } catch (DbUpdateException) {
                    // DbUpdateConcurrencyException (the count moved) or a duplicate key (the row was added): read again.
                }
            }
            Context.ChangeTracker.Clear();
            return false;
        }

        // TakePromotionCount inside a store's transaction (the rows of what the count pays for are written with it):
        // one try, and nothing else of the request is let go. True when taken, false when the limit is reached, null
        // when the write failed (the store is then refused whole). The store holds the account lock, so another
        // session of the account takes its count before or after this one, not in between.
        public bool? TakePromotionCountInStore(long accountId, long characterId, int shareId, int limit, long expiration, long now) {
            PromotionCount? row = Context.PromotionCount.AsTracking()
                .FirstOrDefault(count => count.AccountId == accountId && count.CharacterId == characterId && count.ShareId == shareId);
            if (row == null) {
                if (limit <= 0) {
                    return false;
                }
                Context.PromotionCount.Add(new PromotionCount {
                    AccountId = accountId,
                    CharacterId = characterId,
                    ShareId = shareId,
                    Count = 1,
                    ExpirationTime = expiration,
                });
            } else {
                bool expired = row.ExpirationTime < now;
                int count = expired ? 0 : row.Count;
                if (count >= limit) {
                    return false;
                }
                row.Count = count + 1;
                if (expired) {
                    row.ExpirationTime = expiration;
                }
            }
            try {
                Context.SaveChanges();
                return true;
            } catch (DbUpdateException) {
                return null;
            }
        }
    }
}

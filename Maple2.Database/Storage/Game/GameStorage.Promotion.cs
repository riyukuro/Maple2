using Maple2.Database.Model;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // Whether a promotion limit's counter (Model.PromotionCount; characterId 0 for the account's) is full: a plain
        // read, so a row whose limit is reached costs no store per counted action. The take (TakePromotionCountInStore)
        // checks again inside its store.
        public bool PromotionCountFull(long accountId, long characterId, int shareId, int limit, long now) {
            PromotionCount? row = Context.PromotionCount
                .FirstOrDefault(count => count.AccountId == accountId && count.CharacterId == characterId && count.ShareId == shareId);
            int count = row == null || row.ExpirationTime < now ? 0 : row.Count;
            return count >= limit;
        }

        // Takes one from a promotion limit's counter (Model.PromotionCount; characterId 0 for the account's) inside a
        // store's transaction, with the rows of what the count pays for: the counter, read as 0 once past its end, is
        // below `limit`; it then ends at `expiration` if it started again. One try, and nothing else of the request is
        // let go. True when taken, false when the limit is reached, null when the write failed (the counter moved
        // under it: the store is then refused whole). The store holds the account lock, so another session of the
        // account takes its count before or after this one, not in between.
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

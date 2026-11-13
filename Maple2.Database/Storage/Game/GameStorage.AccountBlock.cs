using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using AccountBlock = Maple2.Model.Game.AccountBlock;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The account's restrictions that have not run out.
        public IList<AccountBlock> GetAccountBlocks(long accountId) {
            DateTime now = DateTime.UtcNow;
            return Context.AccountBlock.AsNoTracking()
                .Where(block => block.AccountId == accountId && block.ExpiryTime >= now)
                .OrderBy(block => block.Kind)
                .AsEnumerable()
                .Select(ToAccountBlock)
                .ToList();
        }

        // A restriction that has not run out.
        public AccountBlock? GetAccountBlock(long id) {
            DateTime now = DateTime.UtcNow;
            Model.AccountBlock? block = Context.AccountBlock.AsNoTracking()
                .FirstOrDefault(block => block.Id == id && block.ExpiryTime >= now);
            return block == null ? null : ToAccountBlock(block);
        }

        public AccountBlock? CreateAccountBlock(long accountId, int kind, string reason, DateTime expiryTime, long issuerId) {
            var block = new Model.AccountBlock {
                AccountId = accountId,
                Kind = kind,
                Reason = reason,
                StartTime = DateTime.UtcNow,
                ExpiryTime = expiryTime,
                IssuerId = issuerId,
            };
            Context.AccountBlock.Add(block);
            return Context.TrySaveChanges() ? ToAccountBlock(block) : null;
        }

        // expiryTime in epoch seconds.
        public AccountBlock? SetAccountBlockExpiry(long id, long expiryTime) {
            Model.AccountBlock? block = Context.AccountBlock.Find(id);
            if (block == null) {
                return null;
            }
            block.ExpiryTime = DateTime.UnixEpoch.AddSeconds(expiryTime);
            return Context.TrySaveChanges() ? ToAccountBlock(block) : null;
        }

        public AccountBlock? RemoveAccountBlock(long id) {
            Model.AccountBlock? block = Context.AccountBlock.Find(id);
            if (block == null) {
                return null;
            }
            Context.AccountBlock.Remove(block);
            return Context.TrySaveChanges() ? ToAccountBlock(block) : null;
        }

        private static AccountBlock ToAccountBlock(Model.AccountBlock block) {
            return new AccountBlock {
                Id = block.Id,
                AccountId = block.AccountId,
                Kind = block.Kind,
                Reason = block.Reason,
                StartTime = DateTime.SpecifyKind(block.StartTime, DateTimeKind.Utc).ToEpochSeconds(),
                ExpiryTime = DateTime.SpecifyKind(block.ExpiryTime, DateTimeKind.Utc).ToEpochSeconds(),
            };
        }
    }
}

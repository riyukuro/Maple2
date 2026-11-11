using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // A row whose character was deleted is not part of the list: it is not shown, does not block and
        // does not count (DeleteCharacter removes such rows; this keeps older rows out as well).
        private IQueryable<HomeBlock> LiveHomeBlocks(long ownerId) {
            return from block in Context.HomeBlock
                   where block.OwnerId == ownerId
                   join character in Context.Character on block.CharacterId equals character.Id
                   select block;
        }

        public IList<HomeBlockEntry> GetHomeBlocks(long ownerId) {
            return (from block in Context.HomeBlock.AsNoTracking()
                    where block.OwnerId == ownerId
                    join character in Context.Character.AsNoTracking() on block.CharacterId equals character.Id
                    select new { block, character })
                .AsEnumerable()
                .Select(row => ToEntry(row.block, row.character))
                .ToList();
        }

        // The account a character belongs to; null when there is no such character.
        public long? GetHomeBlockAccountId(long characterId) {
            return Context.Character.AsNoTracking()
                .Where(character => character.Id == characterId)
                .Select(character => (long?) character.AccountId)
                .FirstOrDefault();
        }

        public bool IsHomeBlocked(long ownerId, long accountId) {
            return LiveHomeBlocks(ownerId).Any(block => block.AccountId == accountId);
        }

        public int CountHomeBlocks(long ownerId) {
            return LiveHomeBlocks(ownerId).Count();
        }

        // Rows that name a deleted character, and every row of an account without characters, go away
        // with the character.
        public void RemoveHomeBlocksOf(long characterId) {
            Context.HomeBlock.RemoveRange(Context.HomeBlock.Where(block => block.CharacterId == characterId));
        }

        // Null when the character does not exist. The caller checks the account and the existing rows first.
        public HomeBlockEntry? CreateHomeBlock(long ownerId, long characterId, string memo) {
            Model.Character? character = Context.Character.AsNoTracking().FirstOrDefault(character => character.Id == characterId);
            if (character == null) {
                return null;
            }

            // A leftover row for the same account (its character was deleted) would collide with the new one.
            Context.HomeBlock.RemoveRange(Context.HomeBlock.Where(old => old.OwnerId == ownerId && old.AccountId == character.AccountId));

            var block = new HomeBlock {
                OwnerId = ownerId,
                CharacterId = characterId,
                AccountId = character.AccountId,
                Memo = memo,
            };
            Context.HomeBlock.Add(block);
            return Context.TrySaveChanges() ? ToEntry(block, character) : null;
        }

        public bool RemoveHomeBlock(long ownerId, long id) {
            HomeBlock? block = Context.HomeBlock.FirstOrDefault(block => block.Id == id && block.OwnerId == ownerId);
            if (block == null) {
                return false;
            }

            Context.HomeBlock.Remove(block);
            return Context.TrySaveChanges();
        }

        public bool UpdateHomeBlockMemo(long ownerId, long id, string memo) {
            HomeBlock? block = Context.HomeBlock.FirstOrDefault(block => block.Id == id && block.OwnerId == ownerId);
            if (block == null) {
                return false;
            }

            block.Memo = memo;
            return Context.TrySaveChanges();
        }

        private static HomeBlockEntry ToEntry(HomeBlock block, Model.Character character) {
            return new HomeBlockEntry {
                Id = block.Id,
                CharacterId = block.CharacterId,
                AccountId = block.AccountId,
                Name = character.Name,
                Level = character.Level,
                Job = character.Job,
                Picture = character.Profile.Picture,
                Memo = block.Memo,
            };
        }
    }
}

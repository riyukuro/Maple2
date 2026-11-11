using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
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
            return Context.HomeBlock.Any(block => block.OwnerId == ownerId && block.AccountId == accountId);
        }

        public int CountHomeBlocks(long ownerId) {
            return Context.HomeBlock.Count(block => block.OwnerId == ownerId);
        }

        // Null when the character does not exist. The caller checks the account and the existing rows first.
        public HomeBlockEntry? CreateHomeBlock(long ownerId, long characterId, string memo) {
            Model.Character? character = Context.Character.AsNoTracking().FirstOrDefault(character => character.Id == characterId);
            if (character == null) {
                return null;
            }

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

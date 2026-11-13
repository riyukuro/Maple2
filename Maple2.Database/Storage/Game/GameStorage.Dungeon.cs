using Maple2.Database.Extensions;
using Maple2.Model.Game.Dungeon;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public Dictionary<int, DungeonRecord> GetDungeonRecords(long ownerId) {
            return Context.DungeonRecord.Where(record => record.OwnerId == ownerId)
                .AsEnumerable()
                .Select<Model.DungeonRecord, DungeonRecord>(record => record)
                .ToDictionary(record => record.DungeonId);
        }

        public DungeonRecord? CreateDungeonRecord(DungeonRecord dungeonRecord, long ownerId) {
            Model.DungeonRecord model = dungeonRecord;
            model.OwnerId = ownerId;
            Context.DungeonRecord.Add(model);

            return Context.TrySaveChanges() ? model : null;
        }

        public bool SaveDungeonRecords(long ownerId, params DungeonRecord[] records) {
            var models = new Model.DungeonRecord[records.Length];
            for (int i = 0; i < records.Length; i++) {
                models[i] = records[i];
                models[i].OwnerId = ownerId;
                Context.DungeonRecord.Update(models[i]);
            }

            return Context.TrySaveChanges();
        }

        // The account reward rooms' counts of the account (dungeon-account-record), by room.
        public Dictionary<int, DungeonRecord> GetDungeonAccountRecords(long accountId) {
            return Context.DungeonAccountRecord.Where(record => record.AccountId == accountId)
                .AsEnumerable()
                .ToDictionary(record => record.DungeonId, record => {
                    var result = new DungeonRecord(record.DungeonId);
                    record.CopyTo(result);
                    return result;
                });
        }

        // The rows of a room that the account's characters keep (dungeon-record), which an account count is first
        // made from.
        public List<DungeonRecord> GetAccountCharacterDungeonRecords(long accountId, int dungeonId) {
            return Context.DungeonRecord
                .Where(record => record.DungeonId == dungeonId
                                 && Context.Character.Any(character => character.Id == record.OwnerId && character.AccountId == accountId))
                .AsEnumerable()
                .Select<Model.DungeonRecord, DungeonRecord>(record => record)
                .ToList();
        }

        public bool CreateDungeonAccountRecord(long accountId, DungeonRecord record) {
            Context.DungeonAccountRecord.Add(Model.DungeonAccountRecord.From(accountId, record));
            return Context.TrySaveChanges();
        }

        // The counts of the given account rooms' records, with the character's rows.
        public bool SaveDungeonRecords(long accountId, long ownerId, IReadOnlySet<int> accountRooms, params DungeonRecord[] records) {
            foreach (DungeonRecord record in records) {
                Model.DungeonRecord model = record;
                model.OwnerId = ownerId;
                Context.DungeonRecord.Update(model);
                if (accountRooms.Contains(record.DungeonId)) {
                    Context.DungeonAccountRecord.Update(Model.DungeonAccountRecord.From(accountId, record));
                }
            }

            return Context.TrySaveChanges();
        }
    }
}

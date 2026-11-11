using Maple2.Database.Extensions;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Nurturing = Maple2.Database.Model.Nurturing;
using NurturingDaily = Maple2.Database.Model.NurturingDaily;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public Maple2.Model.Game.Nurturing? GetNurturing(long accountId, int interactObjectCode, FunctionCubeMetadata.NurturingData metadata) {
            Nurturing? result = Context.Nurturing.Find(accountId, interactObjectCode);
            return result is null ? null : new Maple2.Model.Game.Nurturing(result.Exp, result.ClaimedGiftForStage, result.PlayedBy, result.CreationTime, result.LastFeedTime, metadata);
        }

        public Maple2.Model.Game.Nurturing? CreateNurturing(long accountId, FunctionCubeMetadata.NurturingData metadata, int interactId) {
            var nurturing = new Nurturing {
                AccountId = accountId,
                InteractId = interactId,
                Exp = 0,
                ClaimedGiftForStage = 1,
                CreationTime = DateTime.Now,
                LastFeedTime = DateTime.Now,
                PlayedBy = [],
            };
            Context.Nurturing.Add(nurturing);
            if (!Context.TrySaveChanges()) {
                return null;
            }

            return new Maple2.Model.Game.Nurturing(nurturing.Exp, nurturing.ClaimedGiftForStage, nurturing.PlayedBy, nurturing.CreationTime, nurturing.LastFeedTime, metadata);
        }

        public void UpdateNurturing(long accountId, InteractCube cube) {
            Nurturing? result = Context.Nurturing.Find(accountId, cube.Metadata.Id);
            if (result is null) {
                return;
            }
            Maple2.Model.Game.Nurturing? interactNurturing = cube.Nurturing;
            if (interactNurturing is null) {
                return;
            }
            result.Exp = interactNurturing.Exp;
            result.ClaimedGiftForStage = interactNurturing.ClaimedGiftForStage;
            result.PlayedBy = interactNurturing.PlayedBy.ToArray();
            result.LastFeedTime = interactNurturing.LastFeedTime.DateTime;

            Context.Nurturing.Update(result);
            Context.TrySaveChanges();
        }

        // Saves the pet and adds the row of the daily record for it, in one save: `accountId` fed its own pet
        // (`played` false) or played with the pet of `ownerId`.
        public bool RecordNurturing(long accountId, long ownerId, InteractCube cube, bool played) {
            Nurturing? result = Context.Nurturing.Find(ownerId, cube.Metadata.Id);
            Maple2.Model.Game.Nurturing? interactNurturing = cube.Nurturing;
            if (result is null || interactNurturing is null) {
                return false;
            }
            result.Exp = interactNurturing.Exp;
            result.ClaimedGiftForStage = interactNurturing.ClaimedGiftForStage;
            result.PlayedBy = interactNurturing.PlayedBy.ToArray();
            result.LastFeedTime = interactNurturing.LastFeedTime.DateTime;
            Context.Nurturing.Update(result);

            Context.NurturingDaily.Add(new NurturingDaily {
                AccountId = accountId,
                OwnerId = ownerId,
                ItemId = cube.Metadata.Id,
                Played = played,
            });
            return Context.TrySaveChanges();
        }

        // How often the account fed its pets (`played` false) or played with the pets of others since the daily
        // reset. The client counts the same lists (MyPC, mypc.cpp:3978 and 3985).
        public int CountNurturingDaily(long accountId, bool played) {
            return Context.NurturingDaily.Count(row => row.AccountId == accountId && row.Played == played);
        }

        // The daily record of the account, oldest first, one entry per feeding or play, each with the uid of the
        // pet's placed cube (the lowest uid when the item is placed more than once, 0 when none is placed). Two
        // queries: the account's rows, then the cubes of all their pets at once.
        public IList<NurturingRecord> GetNurturingDaily(long accountId, bool played) {
            var rows = Context.NurturingDaily
                .Where(row => row.AccountId == accountId && row.Played == played)
                .OrderBy(row => row.Id)
                .Select(row => new { row.OwnerId, row.ItemId })
                .ToList();
            if (rows.Count == 0) {
                return [];
            }

            List<long> owners = rows.Select(row => row.OwnerId).Distinct().ToList();
            List<int> items = rows.Select(row => row.ItemId).Distinct().ToList();
            var cubes = Context.UgcMap.Where(map => owners.Contains(map.OwnerId))
                .Join(Context.UgcMapCube, ugcMap => ugcMap.Id, cube => cube.UgcMapId, (ugcMap, cube) => new { ugcMap.OwnerId, cube.ItemId, cube.Id })
                .Where(cube => items.Contains(cube.ItemId))
                .ToList();
            var lowest = new Dictionary<(long, int), long>();
            foreach (var cube in cubes) {
                (long, int) key = (cube.OwnerId, cube.ItemId);
                if (!lowest.TryGetValue(key, out long id) || cube.Id < id) {
                    lowest[key] = cube.Id;
                }
            }

            return rows.Select(row => new NurturingRecord(row.OwnerId, row.ItemId, lowest.GetValueOrDefault((row.OwnerId, row.ItemId)))).ToList();
        }
    }
}

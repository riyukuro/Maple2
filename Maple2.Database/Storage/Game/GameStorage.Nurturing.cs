using Maple2.Database.Extensions;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Nurturing = Maple2.Database.Model.Nurturing;

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

        // The pets the account played with since the daily reset (PlayedBy is emptied by it), of every owner and
        // every pet item: the client counts one list for all of them (MyPC, mypc.cpp:3985).
        public IList<NurturingRecord> GetNurturingPlayedBy(long accountId) {
            return Context.Nurturing.AsEnumerable()
                .Where(row => row.PlayedBy.Contains(accountId))
                .Select(row => (row.AccountId, row.InteractId))
                .ToList()
                .Select(row => new NurturingRecord(row.AccountId, row.InteractId, FindNurturingCubeId(row.AccountId, row.InteractId)))
                .ToList();
        }

        // The account's own pets that grew since `since`. LastFeedTime is the only time kept per pet: it is also
        // written when the pet is placed and when a visitor plays with it, and it holds one time, not a count.
        public IList<NurturingRecord> GetNurturingFedSince(long accountId, DateTime since) {
            return Context.Nurturing
                .Where(row => row.AccountId == accountId && row.LastFeedTime >= since)
                .Select(row => row.InteractId)
                .ToList()
                .Select(itemId => new NurturingRecord(accountId, itemId, FindNurturingCubeId(accountId, itemId)))
                .ToList();
        }

        // The placed cube of a pet (the lowest uid when the item is placed more than once), 0 when none is placed.
        private long FindNurturingCubeId(long ownerId, int itemId) {
            return Context.UgcMap.Where(map => map.OwnerId == ownerId)
                .Join(Context.UgcMapCube, ugcMap => ugcMap.Id, cube => cube.UgcMapId, (ugcMap, cube) => cube)
                .Where(cube => cube.ItemId == itemId)
                .OrderBy(cube => cube.Id)
                .Select(cube => cube.Id)
                .FirstOrDefault();
        }
    }
}

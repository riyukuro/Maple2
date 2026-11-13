using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public IList<BuddyEntry> ListBuddies(long ownerId) {
            return Context.Buddy.Where(buddy => buddy.OwnerId == ownerId)
                .Select<Model.Buddy, BuddyEntry>(buddy => buddy)
                .ToList();
        }

        public BuddyEntry? GetBuddy(long id) {
            return Context.Buddy.Find(id);
        }

        public BuddyEntry? GetBuddy(long ownerId, long buddyId) {
            return Context.Buddy.FirstOrDefault(buddy => buddy.OwnerId == ownerId && buddy.BuddyId == buddyId);
        }

        public BuddyType? GetBuddyType(long ownerId, long buddyId) {
            return Context.Buddy.FirstOrDefault(buddy => buddy.OwnerId == ownerId && buddy.BuddyId == buddyId)?.Type;
        }

        public BuddyEntry? CreateBuddy(long ownerId, long buddyId, BuddyType type, string message = "") {
            var model = new Model.Buddy {
                OwnerId = ownerId,
                BuddyId = buddyId,
                Type = type,
                Message = message,
            };
            Context.Buddy.Add(model);

            return Context.TrySaveChanges() ? model : null;
        }

        public int CountBuddy(long ownerId) {
            return Context.Buddy.Count(buddy => buddy.OwnerId == ownerId && buddy.Type != BuddyType.Blocked);
        }

        public bool UpdateBuddy(params BuddyEntry[] buddies) {
            Context.Buddy.UpdateRange(buddies.Select<BuddyEntry, Model.Buddy>(buddy => buddy));
            return Context.TrySaveChanges();
        }

        public bool RemoveBuddy(params BuddyEntry[] buddies) {
            Context.Buddy.RemoveRange(buddies.Select<BuddyEntry, Model.Buddy>(buddy => buddy));
            return Context.TrySaveChanges();
        }

        public bool CreateBuddyNotice(long ownerId, BuddyNotice notice) {
            Context.BuddyNotice.Add(new Model.BuddyNotice {
                OwnerId = ownerId,
                Name = notice.Name,
                Type = notice.Type,
                EntryId = notice.EntryId,
            });
            return Context.TrySaveChanges();
        }

        // The character's friend notices in the order they were kept; they are deleted as they are read.
        public IList<BuddyNotice> TakeBuddyNotices(long ownerId) {
            List<Model.BuddyNotice> rows = Context.BuddyNotice
                .Where(notice => notice.OwnerId == ownerId)
                .OrderBy(notice => notice.Id)
                .ToList();
            if (rows.Count == 0) {
                return Array.Empty<BuddyNotice>();
            }

            Context.BuddyNotice.RemoveRange(rows);
            if (!Context.TrySaveChanges()) {
                return Array.Empty<BuddyNotice>();
            }
            return rows.Select(row => new BuddyNotice(row.Name, row.Type, row.EntryId)).ToList();
        }
    }
}

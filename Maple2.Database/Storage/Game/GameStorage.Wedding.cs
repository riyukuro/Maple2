using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Marriage = Maple2.Model.Game.Marriage;
using MarriageExp = Maple2.Model.Game.MarriageExp;
using WeddingHall = Maple2.Model.Game.WeddingHall;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public Marriage? CreateMarriage(long partner1Id, long partner2Id) {
            BeginTransaction();

            Model.Marriage marriage = new Model.Marriage {
                Partner1Id = partner1Id,
                Partner2Id = partner2Id,
                Status = MaritalStatus.Engaged,
                Profile = string.Empty,
                Partner1Message = string.Empty,
                Partner2Message = string.Empty,
                CreationTime = DateTime.Now,
            };
            Context.Marriage.Add(marriage);
            if (!SaveChanges()) {
                Rollback();
                return null;
            }

            return Commit() ? GetMarriage(partner1Id) : null;
        }

        // Writes only the message of one partner, so a stale copy of the other partner's text is never written back.
        public bool SaveCoupleMessage(long marriageId, long characterId, string message) {
            Model.Marriage? model = Context.Marriage.Find(marriageId);
            if (model == null) {
                return false;
            }

            if (model.Partner1Id == characterId) {
                model.Partner1Message = message;
            } else if (model.Partner2Id == characterId) {
                model.Partner2Message = message;
            } else {
                return false;
            }

            Context.Marriage.Update(model); // requests do not track what they load
            return Context.TrySaveChanges();
        }

        // A wedding mission's intimacy (quest weddingExp) added to the marriage's exp log by one partner, under the
        // type of that partner by the stored order. Refused unless the marriage counts it and the character is in it.
        public bool AddWeddingMissionExp(long marriageId, long characterId, long amount, DateTime time) {
            Model.Marriage? model = Context.Marriage.Find(marriageId);
            if (model == null || amount <= 0 || !MarriageExpRules.CountsWeddingExp(model.Status)
                || (model.Partner1Id != characterId && model.Partner2Id != characterId)) {
                return false;
            }

            model.ExpHistory = new List<Model.MarriageExp>(model.ExpHistory) {
                new() {
                    Type = MarriageExpRules.WeddingMission(characterId, model.Partner1Id),
                    Amount = amount,
                    Time = time,
                },
            };
            Context.Marriage.Update(model);
            return Context.TrySaveChanges();
        }

        // A Blessing Charm used by one partner (function WeddingExpItem): its line by the stored order, refused like a
        // wedding mission's and while this partner's last use is younger than the cooldown.
        public bool AddWeddingExpItem(long marriageId, long characterId, long amount, TimeSpan? cooldown, DateTime time) {
            Model.Marriage? model = Context.Marriage.Find(marriageId);
            if (model == null || amount <= 0 || !MarriageExpRules.CountsWeddingExp(model.Status)
                || (model.Partner1Id != characterId && model.Partner2Id != characterId)) {
                return false;
            }

            MarriageExpType type = MarriageExpRules.WeddingExpItem(characterId, model.Partner1Id);
            if (!MarriageExpRules.Ready(model.ExpHistory.Select(exp => (exp.Type, exp.Time)), type, cooldown, time)) {
                return false;
            }

            model.ExpHistory = new List<Model.MarriageExp>(model.ExpHistory) {
                new() {
                    Type = type,
                    Amount = amount,
                    Time = time,
                },
            };
            Context.Marriage.Update(model);
            return Context.TrySaveChanges();
        }

        // The vow was completed: an engaged couple becomes married at the given time. Refused for any other
        // state, so a ceremony cannot marry a couple twice or bring back a marriage that is being ended.
        public bool SetMarried(long marriageId, DateTime time) {
            Model.Marriage? model = Context.Marriage.Find(marriageId);
            if (model == null || !Marriage.CanMarry(model.Status)) {
                return false;
            }

            model.Status = MaritalStatus.Married;
            model.MarriageTime = time;
            Context.Marriage.Update(model); // requests do not track what they load
            return Context.TrySaveChanges();
        }

        // One partner files a divorce alone. Only from married; the stored state names the filer by the stored
        // order of the partners. The state changes in one update that tests the stored state, so of two filings
        // at the same time one is stored. With a payer, that player's currency (the fee already taken off) is
        // saved in the same transaction: the filing and the paid fee are stored together or not at all.
        public bool FileDivorce(long marriageId, long filerId, DateTime time, Player? payer = null) {
            Model.Marriage? model = Context.Marriage.Find(marriageId);
            if (model == null || model.Status != MaritalStatus.Married || (model.Partner1Id != filerId && model.Partner2Id != filerId)) {
                return false;
            }

            MaritalStatus filed = DivorceRules.FiledStatus(filerId, model.Partner1Id);
            DateTime? divorceTime = time;
            QueryTrackingBehavior tracking = Context.ChangeTracker.QueryTrackingBehavior;
            try {
                using IDbContextTransaction transaction = Context.Database.BeginTransaction();
                int rows = Context.Marriage
                    .Where(marriage => marriage.Id == marriageId && marriage.Status == MaritalStatus.Married)
                    .ExecuteUpdate(update => update
                        .SetProperty(marriage => marriage.Status, filed)
                        .SetProperty(marriage => marriage.DivorceTime, divorceTime));
                if (!DivorceRules.Stored(rows) || (payer != null && !SaveCurrency(payer))) {
                    return false; // the transaction is rolled back when it is disposed
                }

                transaction.Commit();
                return true;
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to file the divorce of marriage {MarriageId}", marriageId);
                return false;
            } finally {
                Context.ChangeTracker.QueryTrackingBehavior = tracking;
            }
        }

        // The filer takes the divorce back: married again, as if it was never filed. One update that tests the
        // stored state.
        public bool CancelDivorce(long marriageId, long filerId) {
            Model.Marriage? model = Context.Marriage.Find(marriageId);
            if (model == null || !DivorceRules.IsFiled(model.Status) || DivorceRules.FilerId(model.Status, model.Partner1Id, model.Partner2Id) != filerId) {
                return false;
            }

            MaritalStatus filed = model.Status;
            DateTime? divorceTime = null;
            return TryUpdate(() => Context.Marriage
                .Where(marriage => marriage.Id == marriageId && marriage.Status == filed)
                .ExecuteUpdate(update => update
                    .SetProperty(marriage => marriage.Status, MaritalStatus.Married)
                    .SetProperty(marriage => marriage.DivorceTime, divorceTime)));
        }

        // The divorce is complete at the given time: from a filed one (the other partner agreed or the cooling
        // off ended) or, for a mutual one, from married. One update that tests the stored state, so of two
        // requests that complete the same divorce one changes the row and only that one sends its mails.
        public bool CompleteDivorce(long marriageId, bool filed, DateTime time) {
            DateTime? divorceTime = time;
            IQueryable<Model.Marriage> from = filed
                ? Context.Marriage.Where(marriage => marriage.Id == marriageId
                    && (marriage.Status == MaritalStatus.DivorceFiledByGroom || marriage.Status == MaritalStatus.DivorceFiledByBride))
                : Context.Marriage.Where(marriage => marriage.Id == marriageId && marriage.Status == MaritalStatus.Married);
            return TryUpdate(() => from.ExecuteUpdate(update => update
                .SetProperty(marriage => marriage.Status, MaritalStatus.Divorced)
                .SetProperty(marriage => marriage.DivorceTime, divorceTime)));
        }

        private bool TryUpdate(Func<int> update) {
            try {
                return DivorceRules.Stored(update());
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to update a marriage");
                return false;
            }
        }

        // The propose cooltime after a divorce is over: the marriage is gone.
        public bool DeleteDivorced(long marriageId) {
            Model.Marriage? model = Context.Marriage.Find(marriageId);
            if (model == null || model.Status != MaritalStatus.Divorced) {
                return false;
            }

            Context.Marriage.Remove(model);
            return Context.TrySaveChanges();
        }

        public bool DeleteMarriage(long marriageId) {
            Model.Marriage? marriage = Context.Marriage.Find(marriageId);
            if (marriage == null) {
                return false;
            }

            Context.Marriage.Remove(marriage);
            return Context.TrySaveChanges();
        }

        public Marriage? GetMarriage(long characterId = 0, long weddingId = 0) {
            Model.Marriage? marriage = weddingId > 0 ? Context.Marriage.Find(weddingId) :
                Context.Marriage.FirstOrDefault(marriage => marriage.Partner1Id == characterId || marriage.Partner2Id == characterId);
            if (marriage == null) {
                return null;
            }

            PlayerInfo? partner1 = GetPlayerInfo(marriage.Partner1Id);
            PlayerInfo? partner2 = GetPlayerInfo(marriage.Partner2Id);
            if (partner1 == null || partner2 == null) {
                return null;
            }

            return new Marriage {
                Id = marriage.Id,
                CreationTime = marriage.CreationTime.ToEpochSeconds(),
                MarriageTime = marriage.MarriageTime?.ToEpochSeconds() ?? 0,
                DivorceTime = marriage.DivorceTime?.ToEpochSeconds() ?? 0,
                // The log's groom and bride lines follow the order the partners are sent in, as the filed states do.
                ExpHistory = marriage.ExpHistory.Select<Model.MarriageExp, MarriageExp>(exp => exp)
                    .Select(exp => {
                        if (partner1.CharacterId == characterId) {
                            exp.Type = MarriageExpRules.Swapped(exp.Type);
                        }
                        return exp;
                    }).ToList(),
                Partner1 = new MarriagePartner {
                    Info = partner1.CharacterId == characterId ? partner2 : partner1,
                    Message = partner1.CharacterId == characterId ? marriage.Partner2Message : marriage.Partner1Message,
                },
                Partner2 = new MarriagePartner {
                    Info = partner1.CharacterId == characterId ? partner1 : partner2,
                    Message = partner1.CharacterId == characterId ? marriage.Partner1Message : marriage.Partner2Message,
                },
                // The stored state names the filer of a divorce by the stored order; the first stored partner
                // is given the two the other way round.
                Status = partner1.CharacterId == characterId ? DivorceRules.Swapped(marriage.Status) : marriage.Status,
            };
        }

        public MarriageInfo GetMarriageInfo(long characterId) {
            Model.Marriage? model = Context.Marriage.FirstOrDefault(member => member.Partner1Id == characterId || member.Partner2Id == characterId);
            if (model == null) {
                return new MarriageInfo();
            }

            PlayerInfo? owner = GetPlayerInfo(model.Partner1Id);
            PlayerInfo? partner = GetPlayerInfo(model.Partner2Id);

            return owner == null || partner == null ? new MarriageInfo() : new MarriageInfo {
                Status = model.Status,
                CreationTime = model.CreationTime.ToEpochSeconds(),
                MarriageTime = model.MarriageTime?.ToEpochSeconds() ?? 0,
                Partner1Name = owner.Name,
                Partner2Name = partner.Name,
            };
        }

        // Inside a transaction the request already holds (the payment's), the row is stored in it, and the caller
        // commits or rolls back both.
        public WeddingHall? CreateWeddingHall(WeddingHall hall, Marriage marriage) {
            bool own = !IsTransaction;
            if (own) {
                BeginTransaction();
            }

            // A marriage has one hall row. The row of a ceremony whose time has passed gives way to the new
            // reservation, as the client allows it (WeddingProxy::CheckVisit); a row still ahead refuses it.
            Model.WeddingHall? held = Context.WeddingHall.FirstOrDefault(row => row.MarriageId == marriage.Id);
            if (held != null) {
                if (held.CeremonyTime > DateTime.Now) {
                    if (own) {
                        Rollback();
                    }
                    return null;
                }
                Context.WeddingHall.Remove(held);
                if (!SaveChanges()) {
                    if (own) {
                        Rollback();
                    }
                    return null;
                }
            }

            Model.WeddingHall model = hall;
            model.MarriageId = marriage.Id;
            model.OwnerId = marriage.Partner1.CharacterId;
            model.CreationTime = DateTime.Now;
            Context.WeddingHall.Add(model);
            if (!SaveChanges()) {
                if (own) {
                    Rollback();
                }
                return null;
            }

            return !own || Commit() ? GetWeddingHall(marriage) : null;
        }

        public WeddingHall? GetWeddingHall(Marriage marriage) {
            Model.WeddingHall? model = Context.WeddingHall.FirstOrDefault(hall => hall.MarriageId == marriage.Id);
            return model?.Convert(marriage);
        }

        public WeddingHall? GetWeddingHall(long hallId = 0, long marriageId = 0) {
            Model.WeddingHall? model = hallId > 0 ? Context.WeddingHall.Find(hallId) :
                Context.WeddingHall.FirstOrDefault(hall => hall.MarriageId == marriageId);

            if (model == null) {
                return null;
            }

            Marriage? marriage = GetMarriage(weddingId: marriageId);
            return marriage == null ? null : model.Convert(marriage);
        }

        public bool DeleteWeddingHall(long hallId) {
            Model.WeddingHall? hall = Context.WeddingHall.Find(hallId);
            if (hall == null) {
                return false;
            }

            Context.WeddingHall.Remove(hall);
            return Context.TrySaveChanges();
        }

        // A cancelled reservation: the hall row is deleted under the same lock as in AddWeddingGuests, and
        // the guests it held at that moment are returned for the cancel mails. An invitation is stored before
        // the cancel (its guest is in the returned list) or finds the hall gone. Null when the hall is gone or
        // the delete failed. alsoStore, when given, writes on this request in the same transaction (the cancel's
        // coupon mail): it returning false keeps the hall, so the delete and those writes are kept together or not.
        public Dictionary<long, long>? CancelWeddingHall(long hallId, Func<bool>? alsoStore = null) {
            try {
                using IDbContextTransaction transaction = Context.Database.BeginTransaction();
                // The in-memory store (the tests) has no row lock to take.
                if (Context.Database.IsRelational()) {
                    Context.Database.ExecuteSqlRaw("SELECT `Id` FROM `wedding-hall` WHERE `Id` = {0} FOR UPDATE", hallId);
                }
                Model.WeddingHall? hall = Context.WeddingHall.Find(hallId);
                if (hall == null) {
                    return null;
                }
                Context.Entry(hall).Reload();
                if (Context.Entry(hall).State == EntityState.Detached) {
                    return null;
                }

                var guests = new Dictionary<long, long>(hall.GuestList);
                Context.WeddingHall.Remove(hall);
                if (!Context.TrySaveChanges() || (alsoStore != null && !alsoStore())) {
                    // The writes left pending would be written by the request's next save.
                    Context.ChangeTracker.Clear();
                    return null;
                }

                transaction.Commit();
                return guests;
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to cancel wedding hall {HallId}", hallId);
                Context.ChangeTracker.Clear();
                return null;
            }
        }

        // A changed reservation: the old hall row is replaced by a new one that keeps the old row's guests.
        // The old row is locked as in AddWeddingGuests for the read of its guests, the delete and the insert,
        // so an invitation is stored either before the change (and is carried over) or finds the old hall gone
        // (and is refused, unpaid). Null when the old hall is gone or the save failed; nothing is changed then.
        // Inside a transaction the request already holds (the payment's), the change is made in it, and the caller
        // commits or rolls back both.
        public WeddingHall? ChangeWeddingHall(long hallId, WeddingHall hall, Marriage marriage) {
            try {
                using (IDbContextTransaction? transaction = IsTransaction ? null : Context.Database.BeginTransaction()) {
                    Context.Database.ExecuteSqlRaw("SELECT `Id` FROM `wedding-hall` WHERE `Id` = {0} FOR UPDATE", hallId);
                    Model.WeddingHall? old = Context.WeddingHall.Find(hallId);
                    if (old == null) {
                        return null;
                    }
                    Context.Entry(old).Reload();
                    if (Context.Entry(old).State == EntityState.Detached) {
                        return null;
                    }

                    Model.WeddingHall model = hall;
                    model.Id = 0; // a new row with its own id
                    model.MarriageId = marriage.Id;
                    model.OwnerId = marriage.Partner1.CharacterId;
                    model.CreationTime = DateTime.Now;
                    model.GuestList = new Dictionary<long, long>(old.GuestList);
                    Context.WeddingHall.Remove(old);
                    Context.WeddingHall.Add(model);
                    if (!Context.TrySaveChanges()) {
                        return null;
                    }

                    transaction?.Commit();
                }
                return GetWeddingHall(marriage);
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to change wedding hall {HallId}", hallId);
                return null;
            }
        }

        // Adds guests (character id, account id) to a hall's stored list. The whole list after it, or null:
        // with an error when the stored list does not take them (WeddingInviteRules.Merge), with error none
        // when the hall is gone or the save failed. The two partners can send from different channel
        // processes, so the hall's row is locked in the database for the read, the merge and the save: the
        // second of two senders waits and then merges into the first one's list.
        public Dictionary<long, long>? AddWeddingGuests(long hallId, IReadOnlyDictionary<long, long> guests, int max, out WeddingError error) {
            error = WeddingError.none;
            try {
                using IDbContextTransaction transaction = Context.Database.BeginTransaction();
                Context.Database.ExecuteSqlRaw("SELECT `Id` FROM `wedding-hall` WHERE `Id` = {0} FOR UPDATE", hallId);
                Model.WeddingHall? hall = Context.WeddingHall.Find(hallId);
                if (hall == null) {
                    return null;
                }
                // The context may hold the row from before the lock. A row that was deleted while this
                // request waited for the lock (a cancelled or changed reservation) is detached by the reload.
                Context.Entry(hall).Reload();
                if (Context.Entry(hall).State == EntityState.Detached) {
                    return null;
                }

                Dictionary<long, long>? list = WeddingInviteRules.Merge(hall.GuestList, guests, max, out error);
                if (list == null) {
                    return null; // the transaction is rolled back when it is disposed
                }
                hall.GuestList = list;
                if (!Context.TrySaveChanges()) {
                    return null;
                }

                transaction.Commit();
                return list;
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to add guests to wedding hall {HallId}", hallId);
                return null;
            }
        }

        // The halls with a ceremony time from..to (epoch seconds) that have the character on the guest list.
        // Each hall with its couple's state, which decides whether the ticket is still valid.
        public List<(WeddingHall Hall, MaritalStatus CoupleStatus)> GetGuestWeddingHalls(long characterId, long from, long to) {
            DateTime fromTime = from.FromEpochSeconds();
            DateTime toTime = to.FromEpochSeconds();
            List<Model.WeddingHall> entries = Context.WeddingHall
                .Where(hall => hall.CeremonyTime >= fromTime && hall.CeremonyTime <= toTime)
                .ToList();

            var result = new List<(WeddingHall, MaritalStatus)>();
            foreach (Model.WeddingHall entry in entries) {
                if (!entry.GuestList.ContainsKey(characterId)) {
                    continue;
                }
                Marriage? marriage = GetMarriage(weddingId: entry.MarriageId);
                if (marriage != null) {
                    result.Add((entry.Convert(marriage), marriage.Status));
                }
            }
            return result;
        }

        public bool WeddingHallTimeIsAvailable(long ceremonyTime) {
            DateTime ceremonyDateTime = ceremonyTime.FromEpochSeconds();
            return Context.WeddingHall.FirstOrDefault(hall => hall.CeremonyTime == ceremonyDateTime) == null;
        }

        public IEnumerable<WeddingHall> GetWeddingHalls() {
            List<Model.WeddingHall> entries = Context.WeddingHall
                .Where(hall => hall.CeremonyTime > DateTime.Now)
                .AsEnumerable()
                .ToList();

            foreach (Model.WeddingHall entry in entries) {
                Marriage? marriage = GetMarriage(weddingId: entry.MarriageId);
                if (marriage == null) {
                    continue;
                }
                yield return entry.Convert(marriage);
            }
        }
    }
}

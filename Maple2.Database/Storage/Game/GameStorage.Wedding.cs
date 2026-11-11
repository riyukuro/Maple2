using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
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
                ExpHistory = marriage.ExpHistory.Select<Model.MarriageExp, MarriageExp>(exp => exp).ToList(),
                Partner1 = new MarriagePartner {
                    Info = partner1.CharacterId == characterId ? partner2 : partner1,
                    Message = partner1.CharacterId == characterId ? marriage.Partner2Message : marriage.Partner1Message,
                },
                Partner2 = new MarriagePartner {
                    Info = partner1.CharacterId == characterId ? partner1 : partner2,
                    Message = partner1.CharacterId == characterId ? marriage.Partner1Message : marriage.Partner2Message,
                },
                Status = marriage.Status,
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

        public WeddingHall? CreateWeddingHall(WeddingHall hall, Marriage marriage) {
            BeginTransaction();

            Model.WeddingHall model = hall;
            model.MarriageId = marriage.Id;
            model.OwnerId = marriage.Partner1.CharacterId;
            model.CreationTime = DateTime.Now;
            Context.WeddingHall.Add(model);
            if (!SaveChanges()) {
                return null;
            }

            return Commit() ? GetWeddingHall(marriage) : null;
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

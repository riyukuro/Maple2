using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The rows of the season that name the character as mentor or as mentee, oldest first.
        public IList<MentoringEntry> GetMentoring(long characterId, int season) {
            return Context.Mentoring.AsNoTracking()
                .Where(row => row.Season == season && (row.MentorId == characterId || row.MenteeId == characterId))
                .OrderBy(row => row.Id)
                .Select(row => new MentoringEntry(row.Id, row.MentorId, row.MenteeId, row.Accepted))
                .ToList();
        }

        // The rows stored before rows had a season (season 0, AddMentoringSeason) that name the character: moved to
        // `season`, the season running at the character's first login since; to RetiredSeason when none runs then
        // (`season` null), so they stay with the season they were made in and a later season does not take them. A row of
        // a mentor and mentee that already have a row of `season` is retired instead. False when the store failed.
        public bool MoveUnseasonedMentoring(long characterId, int? season) {
            List<Model.Mentoring> rows = Context.Mentoring.AsTracking()
                .Where(row => row.Season == 0 && (row.MentorId == characterId || row.MenteeId == characterId))
                .ToList();
            if (rows.Count == 0) {
                return true;
            }
            foreach (Model.Mentoring row in rows) {
                bool taken = season is { } running
                    && Context.Mentoring.Any(other => other.Season == running && other.MentorId == row.MentorId && other.MenteeId == row.MenteeId);
                row.Season = taken || season == null ? Model.Mentoring.RetiredSeason : season.Value;
            }
            return Context.TrySaveChanges();
        }

        // The row of the season with the id.
        public MentoringEntry? GetMentoringEntry(long id, int season) {
            return Context.Mentoring.AsNoTracking()
                .Where(row => row.Id == id && row.Season == season)
                .Select(row => new MentoringEntry(row.Id, row.MentorId, row.MenteeId, row.Accepted))
                .FirstOrDefault();
        }

        // Stores the mentor's request to the mentee in the season, one row per mentor, mentee and season; the limits
        // count the season's rows only. The error when the mentee has a
        // mentor (7), the mentor asked this mentee already (8), the mentor has maxMentees mentees (3), maxWaiting
        // requests waiting (4) or the mentee maxReceived requests (5). A limit of 0 refuses every request.
        public MentorError? AddMentoringRequest(long mentorId, long menteeId, int season, int maxMentees, int maxWaiting, int maxReceived,
                out MentoringEntry? entry) {
            entry = null;
            var rows = Context.Mentoring.AsNoTracking()
                .Where(row => row.Season == season && (row.MentorId == mentorId || row.MenteeId == menteeId))
                .Select(row => new { row.MentorId, row.MenteeId, row.Accepted })
                .ToList();
            if (rows.Any(row => row.MenteeId == menteeId && row.Accepted)) {
                return MentorError.MenteeHasAlreadyMentor;
            }
            if (rows.Any(row => row.MentorId == mentorId && row.MenteeId == menteeId)) {
                return MentorError.AlreadyRequestedToMentee;
            }
            if (rows.Count(row => row.MentorId == mentorId && row.Accepted) >= maxMentees) {
                return MentorError.InvalidTarget;
            }
            if (rows.Count(row => row.MentorId == mentorId && !row.Accepted) >= maxWaiting) {
                return MentorError.MentorHasMaxCountWaiting;
            }
            if (rows.Count(row => row.MenteeId == menteeId && !row.Accepted) >= maxReceived) {
                return MentorError.MenteeHasMaxCountReceived;
            }

            var model = new Model.Mentoring {
                MentorId = mentorId,
                MenteeId = menteeId,
                Season = season,
                // In the server's local time, as every datetime column is read back (ToEpochSeconds).
                CreationTime = DateTime.Now,
            };
            Context.Mentoring.Add(model);
            if (!Context.TrySaveChanges()) {
                // The unique (mentor, mentee) index refused it: the same request was stored meanwhile.
                return MentorError.AlreadyRequestedToMentee;
            }
            entry = new MentoringEntry(model.Id, mentorId, menteeId, false);
            return null;
        }

        // The mentee's accept of the request id: only a waiting request of the season to this mentee. The pair is stored and the
        // mentee's other waiting requests are removed. The error when no such request waits (2), the mentee has a
        // mentor (7) or the mentor has maxMentees mentees (3); on 7 and 3 the request is removed.
        public MentorError? AcceptMentoring(long id, long menteeId, int season, int maxMentees, out MentoringEntry? entry) {
            entry = null;
            Model.Mentoring? request = Context.Mentoring.AsTracking()
                .FirstOrDefault(row => row.Id == id && row.MenteeId == menteeId && row.Season == season && !row.Accepted);
            if (request == null) {
                return MentorError.TargetNotExists;
            }
            if (Context.Mentoring.Any(row => row.MenteeId == menteeId && row.Season == season && row.Accepted)) {
                // A request stored on another game server while this mentee's pair was accepted: it can never be
                // accepted, so it is removed.
                Context.Mentoring.Remove(request);
                Context.TrySaveChanges();
                return MentorError.MenteeHasAlreadyMentor;
            }
            if (Context.Mentoring.Count(row => row.MentorId == request.MentorId && row.Season == season && row.Accepted) >= maxMentees) {
                Context.Mentoring.Remove(request);
                Context.TrySaveChanges();
                return MentorError.InvalidTarget;
            }

            request.Accepted = true;
            Context.Mentoring.RemoveRange(Context.Mentoring.AsTracking().Where(row => row.MenteeId == menteeId && row.Season == season && row.Id != id && !row.Accepted));
            if (!Context.TrySaveChanges()) {
                return MentorError.TargetNotExists;
            }
            entry = new MentoringEntry(request.Id, request.MentorId, request.MenteeId, true);
            return null;
        }

        // The mentee's decline of the request id: removes it when it is a waiting request of the season to this mentee.
        public bool DeclineMentoring(long id, long menteeId, int season) {
            Model.Mentoring? request = Context.Mentoring.AsTracking()
                .FirstOrDefault(row => row.Id == id && row.MenteeId == menteeId && row.Season == season && !row.Accepted);
            if (request == null) {
                return false;
            }
            Context.Mentoring.Remove(request);
            return Context.TrySaveChanges();
        }

        // Adds coins to the mentor coins of the mentee's pair of the season, in one statement, so adds made at the
        // same time all count; written in the store of what pays the mentee's coin (MentoringManager), so neither is
        // kept without the other. mentorId: the pair's mentor, 0 when the mentee has none (true: nothing to add).
        // False when the write failed: the store is refused.
        public bool AddMentorCoins(long menteeId, int season, long coins, out long mentorId) {
            mentorId = 0;
            if (coins <= 0) {
                return true;
            }
            IQueryable<Model.Mentoring> pair = Context.Mentoring.Where(row => row.MenteeId == menteeId && row.Season == season && row.Accepted);
            long mentor = pair.AsNoTracking().OrderBy(row => row.Id).Select(row => row.MentorId).FirstOrDefault();
            if (mentor == 0) {
                return true;
            }
            if (Context.Database.IsRelational()) {
                // No row updated: the pair was removed since the read, so there is no mentor to pay.
                if (pair.ExecuteUpdate(update => update.SetProperty(row => row.MentorCoins, row => row.MentorCoins + coins)) <= 0) {
                    return true;
                }
            } else {
                foreach (Model.Mentoring row in pair.AsTracking()) {
                    row.MentorCoins += coins;
                }
                if (!Context.TrySaveChanges()) {
                    return false;
                }
            }
            mentorId = mentor;
            return true;
        }

        // The mentor's pairs of the season that hold mentor coins, and how many, oldest first.
        public IList<(long Id, long Coins)> GetMentorCoins(long mentorId, int season) {
            return Context.Mentoring.AsNoTracking()
                .Where(row => row.MentorId == mentorId && row.Season == season && row.Accepted && row.MentorCoins > 0)
                .OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.MentorCoins })
                .AsEnumerable()
                .Select(row => (row.Id, row.MentorCoins))
                .ToList();
        }

        // Takes the coins read (GetMentorCoins) off each pair, one statement per pair that refuses a pair holding
        // fewer: false when one did (another take came first), so the caller's transaction is not committed. An add
        // made since the read stays on the pair.
        public bool TakeMentorCoins(IEnumerable<(long Id, long Coins)> taken) {
            foreach ((long id, long coins) in taken) {
                IQueryable<Model.Mentoring> pair = Context.Mentoring.Where(row => row.Id == id && row.MentorCoins >= coins);
                if (Context.Database.IsRelational()) {
                    if (pair.ExecuteUpdate(update => update.SetProperty(row => row.MentorCoins, row => row.MentorCoins - coins)) != 1) {
                        return false;
                    }
                    continue;
                }
                Model.Mentoring? row = pair.AsTracking().FirstOrDefault();
                if (row == null) {
                    return false;
                }
                row.MentorCoins -= coins;
                if (!Context.TrySaveChanges()) {
                    return false;
                }
            }
            return true;
        }
    }
}

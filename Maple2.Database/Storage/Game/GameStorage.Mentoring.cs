using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The rows that name the character as mentor or as mentee, oldest first.
        public IList<MentoringEntry> GetMentoring(long characterId) {
            return Context.Mentoring.AsNoTracking()
                .Where(row => row.MentorId == characterId || row.MenteeId == characterId)
                .OrderBy(row => row.Id)
                .Select(row => new MentoringEntry(row.Id, row.MentorId, row.MenteeId, row.Accepted))
                .ToList();
        }

        public MentoringEntry? GetMentoringEntry(long id) {
            return Context.Mentoring.AsNoTracking()
                .Where(row => row.Id == id)
                .Select(row => new MentoringEntry(row.Id, row.MentorId, row.MenteeId, row.Accepted))
                .FirstOrDefault();
        }

        // Stores the mentor's request to the mentee, one row per mentor and mentee. The error when the mentee has a
        // mentor (7), the mentor asked this mentee already (8), the mentor has maxMentees mentees (3), maxWaiting
        // requests waiting (4) or the mentee maxReceived requests (5). A limit of 0 refuses every request.
        public MentorError? AddMentoringRequest(long mentorId, long menteeId, int maxMentees, int maxWaiting, int maxReceived,
                out MentoringEntry? entry) {
            entry = null;
            var rows = Context.Mentoring.AsNoTracking()
                .Where(row => row.MentorId == mentorId || row.MenteeId == menteeId)
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

        // The mentee's accept of the request id: only a waiting request to this mentee. The pair is stored and the
        // mentee's other waiting requests are removed. The error when no such request waits (2), the mentee has a
        // mentor (7) or the mentor has maxMentees mentees (3); on 7 and 3 the request is removed.
        public MentorError? AcceptMentoring(long id, long menteeId, int maxMentees, out MentoringEntry? entry) {
            entry = null;
            Model.Mentoring? request = Context.Mentoring.AsTracking()
                .FirstOrDefault(row => row.Id == id && row.MenteeId == menteeId && !row.Accepted);
            if (request == null) {
                return MentorError.TargetNotExists;
            }
            if (Context.Mentoring.Any(row => row.MenteeId == menteeId && row.Accepted)) {
                // A request stored on another game server while this mentee's pair was accepted: it can never be
                // accepted, so it is removed.
                Context.Mentoring.Remove(request);
                Context.TrySaveChanges();
                return MentorError.MenteeHasAlreadyMentor;
            }
            if (Context.Mentoring.Count(row => row.MentorId == request.MentorId && row.Accepted) >= maxMentees) {
                Context.Mentoring.Remove(request);
                Context.TrySaveChanges();
                return MentorError.InvalidTarget;
            }

            request.Accepted = true;
            Context.Mentoring.RemoveRange(Context.Mentoring.AsTracking().Where(row => row.MenteeId == menteeId && row.Id != id && !row.Accepted));
            if (!Context.TrySaveChanges()) {
                return MentorError.TargetNotExists;
            }
            entry = new MentoringEntry(request.Id, request.MentorId, request.MenteeId, true);
            return null;
        }

        // The mentee's decline of the request id: removes it when it is a waiting request to this mentee.
        public bool DeclineMentoring(long id, long menteeId) {
            Model.Mentoring? request = Context.Mentoring.AsTracking()
                .FirstOrDefault(row => row.Id == id && row.MenteeId == menteeId && !row.Accepted);
            if (request == null) {
                return false;
            }
            Context.Mentoring.Remove(request);
            return Context.TrySaveChanges();
        }
    }
}

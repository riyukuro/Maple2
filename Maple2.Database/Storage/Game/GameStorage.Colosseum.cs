using Maple2.Database.Extensions;
using Maple2.Database.Model.Ranking;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The rows of the Colosseum board are read with their characters, so a row whose character is gone is not
        // shown (the database removes it with the character).
        public ColosseumBest? GetColosseumBest(long characterId, int seasonId) {
            Model.ColosseumRecord? record = Context.ColosseumRecord.AsNoTracking()
                .FirstOrDefault(row => row.CharacterId == characterId && row.SeasonId == seasonId);
            return record == null ? null : new ColosseumBest(record.Rounds, record.Seconds, record.SetTime.ToEpochSeconds());
        }

        // Keeps the run as the character's best of the season when it replaces it (ColosseumBest.Replaces).
        // True when it was saved.
        public bool SaveColosseumRun(long characterId, int seasonId, int rounds, int seconds, long time) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            if (!Context.Character.Any(character => character.Id == characterId)) {
                return false;
            }

            Model.ColosseumRecord? record = Context.ColosseumRecord
                .FirstOrDefault(row => row.CharacterId == characterId && row.SeasonId == seasonId);
            ColosseumBest? best = record == null ? null : new ColosseumBest(record.Rounds, record.Seconds, record.SetTime.ToEpochSeconds());
            if (!ColosseumBest.Replaces(best, rounds, seconds)) {
                return false;
            }

            if (record == null) {
                Context.ColosseumRecord.Add(new Model.ColosseumRecord {
                    CharacterId = characterId,
                    SeasonId = seasonId,
                    Rounds = rounds,
                    Seconds = seconds,
                    SetTime = time.FromEpochSeconds(),
                });
            } else {
                record.Rounds = rounds;
                record.Seconds = seconds;
                record.SetTime = time.FromEpochSeconds();
                Context.ColosseumRecord.Update(record);
            }
            return Context.TrySaveChanges();
        }

        // The rows of a season and job tab with their characters, in the database: a job code other than 0 keeps the
        // characters of that job (as the Dark Stream tabs: Job.Code() is the job / 10, the game master is its own code).
        private IQueryable<ColosseumRow> ColosseumRows(int seasonId, int jobCode) {
            IQueryable<ColosseumRow> rows = from record in Context.ColosseumRecord.AsNoTracking()
                                            join character in Context.Character.AsNoTracking() on record.CharacterId equals character.Id
                                            where record.SeasonId == seasonId
                                            select new ColosseumRow {
                                                CharacterId = record.CharacterId,
                                                Rounds = record.Rounds,
                                                Seconds = record.Seconds,
                                                SetTime = record.SetTime,
                                                Name = character.Name,
                                                Job = character.Job,
                                                Profile = character.Profile,
                                            };
            if (jobCode == (int) JobCode.GameMaster) {
                return rows.Where(row => row.Job == Job.GameMaster);
            }
            if (jobCode != 0) {
                var low = (Job) (jobCode * 10);
                var high = (Job) (jobCode * 10 + 9);
                return rows.Where(row => row.Job != Job.GameMaster && row.Job >= low && row.Job <= high);
            }
            return rows;
        }

        // Most rounds first, then fewer seconds; equal runs rank by the time they were set, the earlier one first,
        // then by the character id.
        private static IQueryable<ColosseumRow> InColosseumOrder(IQueryable<ColosseumRow> rows) {
            return rows.OrderByDescending(row => row.Rounds)
                .ThenBy(row => row.Seconds)
                .ThenBy(row => row.SetTime)
                .ThenBy(row => row.CharacterId);
        }

        private static ColosseumRankInfo ToRankInfo(ColosseumRow row, int rank) {
            return new ColosseumRankInfo(
                Rank: rank,
                CharacterId: row.CharacterId,
                Name: row.Name,
                Profile: row.Profile.Picture ?? string.Empty,
                Rounds: row.Rounds,
                Seconds: row.Seconds,
                SetTime: row.SetTime.ToEpochSeconds());
        }

        // The rank of a row: 1 + the rows of the same tab that come before it in the board order.
        private int ColosseumRank(int seasonId, int jobCode, ColosseumRow row) {
            return 1 + ColosseumRows(seasonId, jobCode).Count(other => other.Rounds > row.Rounds
                || (other.Rounds == row.Rounds && (other.Seconds < row.Seconds
                    || (other.Seconds == row.Seconds && (other.SetTime < row.SetTime
                        || (other.SetTime == row.SetTime && other.CharacterId < row.CharacterId))))));
        }

        public IList<ColosseumRankInfo> GetColosseumRankings(int seasonId, int jobCode) {
            return InColosseumOrder(ColosseumRows(seasonId, jobCode))
                .Take(200)
                .AsEnumerable()
                .Select((row, index) => ToRankInfo(row, index + 1))
                .ToList();
        }

        public ColosseumRankInfo? GetColosseumRankInfo(int seasonId, int jobCode, long characterId) {
            ColosseumRow? row = ColosseumRows(seasonId, jobCode).FirstOrDefault(row => row.CharacterId == characterId);
            return row == null ? null : ToRankInfo(row, ColosseumRank(seasonId, jobCode, row));
        }

        // The search by name finds the character as the trophy search does (GetCharacterId, any case).
        public ColosseumRankInfo? GetColosseumRankInfo(int seasonId, int jobCode, string name) {
            long characterId = GetCharacterId(name);
            return characterId == default ? null : GetColosseumRankInfo(seasonId, jobCode, characterId);
        }

        private class ColosseumRow {
            public long CharacterId { get; init; }
            public int Rounds { get; init; }
            public int Seconds { get; init; }
            public DateTime SetTime { get; init; }
            public string Name { get; init; } = string.Empty;
            public Job Job { get; init; }
            public Model.Profile Profile { get; init; } = null!;
        }
    }
}

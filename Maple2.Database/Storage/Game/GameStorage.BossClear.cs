using Maple2.Database.Extensions;
using Maple2.Database.Model.Ranking;
using Maple2.Model;
using Maple2.Model.Enum;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // Keeps one clear of a room with a boss board, with the members that still have a character. Returns the
        // row id, 0 when no member is left (nothing is kept then).
        public long AddBossClear(int rankingId, int dungeonId, long leaderId, int seconds, long clearTime, IEnumerable<long> characterIds) {
            long[] ids = characterIds.Distinct().ToArray();
            long[] members = Context.Character.AsNoTracking()
                .Where(character => ids.Contains(character.Id))
                .Select(character => character.Id)
                .ToArray();
            if (members.Length == 0) {
                return 0;
            }

            // The clear and its members go in one SaveChanges (one transaction), so no clear is left without them.
            var clear = new Model.BossClear {
                RankingId = rankingId,
                DungeonId = dungeonId,
                LeaderId = leaderId,
                Seconds = seconds,
                ClearTime = clearTime.FromEpochSeconds(),
                Members = members.Select(characterId => new Model.BossClearMember { CharacterId = characterId }).ToList(),
            };
            Context.BossClear.Add(clear);
            if (!Context.TrySaveChanges()) {
                return 0;
            }
            return clear.Id;
        }

        // The members of the clears, with their characters (a member whose character is gone is not read).
        private IQueryable<BossClearMemberRow> BossClearMembers() {
            return from member in Context.BossClearMember.AsNoTracking()
                   join character in Context.Character.AsNoTracking() on member.CharacterId equals character.Id
                   select new BossClearMemberRow {
                       ClearId = member.ClearId,
                       CharacterId = member.CharacterId,
                       Name = character.Name,
                       Profile = character.Profile,
                       Level = character.Level,
                       Job = character.Job,
                   };
        }

        // The clears of a board that still have a member.
        private IQueryable<Model.BossClear> BossClears(int rankingId) {
            IQueryable<BossClearMemberRow> members = BossClearMembers();
            return Context.BossClear.AsNoTracking()
                .Where(clear => clear.RankingId == rankingId && members.Any(member => member.ClearId == clear.Id));
        }

        // 0x51: the first clears of the board, the earliest first. 0x5b: the fastest clears, then the earlier one.
        // Equal ones go by the row id (the earlier row first).
        public IList<BossClearRankInfo> GetBossFirstClears(int rankingId) {
            return BossClearBoard(BossClears(rankingId)
                .OrderBy(clear => clear.ClearTime)
                .ThenBy(clear => clear.Id));
        }

        public IList<BossClearRankInfo> GetBossShortestClears(int rankingId) {
            return BossClearBoard(BossClears(rankingId)
                .OrderBy(clear => clear.Seconds)
                .ThenBy(clear => clear.ClearTime)
                .ThenBy(clear => clear.Id));
        }

        private IList<BossClearRankInfo> BossClearBoard(IQueryable<Model.BossClear> ordered) {
            List<Model.BossClear> clears = ordered.Take(200).ToList();
            long[] clearIds = clears.Select(clear => clear.Id).ToArray();
            Dictionary<long, List<BossClearMemberInfo>> members = BossClearMembers()
                .Where(member => clearIds.Contains(member.ClearId))
                .AsEnumerable()
                .GroupBy(member => member.ClearId)
                .ToDictionary(group => group.Key, group => group
                    .OrderBy(member => member.CharacterId)
                    .Select(member => new BossClearMemberInfo(member.CharacterId, member.Name,
                        member.Profile.Picture ?? string.Empty, member.Level, member.Job.Code()))
                    .ToList());

            return clears.Select((clear, index) => new BossClearRankInfo(
                    Rank: index + 1,
                    ClearId: clear.Id,
                    RankingId: clear.RankingId,
                    LeaderId: clear.LeaderId,
                    Seconds: clear.Seconds,
                    ClearTime: clear.ClearTime.ToEpochSeconds(),
                    Members: members.GetValueOrDefault(clear.Id) ?? []))
                .ToList();
        }

        // The clears per character on a board, with the character's last clear of it.
        private IQueryable<BossClearCountRow> BossClearCounts(int rankingId) {
            IQueryable<BossClearCountRow> counts = from member in Context.BossClearMember.AsNoTracking()
                                                   join clear in Context.BossClear.AsNoTracking() on member.ClearId equals clear.Id
                                                   where clear.RankingId == rankingId
                                                   group clear by member.CharacterId into clears
                                                   select new BossClearCountRow {
                                                       CharacterId = clears.Key,
                                                       Clears = clears.Count(),
                                                       LastClear = clears.Max(clear => clear.ClearTime),
                                                   };
            return from count in counts
                   join character in Context.Character.AsNoTracking() on count.CharacterId equals character.Id
                   select new BossClearCountRow {
                       CharacterId = count.CharacterId,
                       Clears = count.Clears,
                       LastClear = count.LastClear,
                       Name = character.Name,
                       Profile = character.Profile,
                   };
        }

        // 0x48: most clears first; equal counts go by the earlier last clear (who got there first), then the
        // character id.
        public IList<BossClearCountRankInfo> GetBossClearCounts(int rankingId) {
            return BossClearCounts(rankingId)
                .OrderByDescending(row => row.Clears)
                .ThenBy(row => row.LastClear)
                .ThenBy(row => row.CharacterId)
                .Take(200)
                .AsEnumerable()
                .Select((row, index) => ToCountRankInfo(row, index + 1))
                .ToList();
        }

        // The character's row of 0x48 (its rank is 1 + the rows before it), none without a clear of the board.
        public BossClearCountRankInfo? GetBossClearCount(int rankingId, long characterId) {
            BossClearCountRow? row = BossClearCounts(rankingId).FirstOrDefault(row => row.CharacterId == characterId);
            if (row == null) {
                return null;
            }
            int before = BossClearCounts(rankingId).Count(other => other.Clears > row.Clears
                || (other.Clears == row.Clears && (other.LastClear < row.LastClear
                    || (other.LastClear == row.LastClear && other.CharacterId < row.CharacterId))));
            return ToCountRankInfo(row, before + 1);
        }

        public BossClearCountRankInfo? GetBossClearCount(int rankingId, string name) {
            long characterId = GetCharacterId(name);
            return characterId == default ? null : GetBossClearCount(rankingId, characterId);
        }

        // The fame challenge boards 0x97 and 0xa2 have one row per character: its first clear of the board (the
        // earliest first), or its fastest one (then the earlier one). Equal ones go by the row id. The own rank
        // (0xa1) is the character's place on the fastest board.
        public IList<BossClearRankInfo> GetFameChallengeFirstClears(int rankingId) {
            return FameChallengeBoard(rankingId, FameChallengeBestClears(rankingId, fastest: false).Take(200).ToList());
        }

        public IList<BossClearRankInfo> GetFameChallengeShortestClears(int rankingId) {
            return FameChallengeBoard(rankingId, FameChallengeBestClears(rankingId, fastest: true).Take(200).ToList());
        }

        // 0 when the character has no clear of the board.
        public int GetFameChallengeShortestClearRank(int rankingId, long characterId) {
            int index = FameChallengeBestClears(rankingId, fastest: true).FindIndex(row => row.CharacterId == characterId);
            return index + 1;
        }

        // The best clear of each character with one (the first or the fastest), in the board's order.
        private List<FameChallengeClearRow> FameChallengeBestClears(int rankingId, bool fastest) {
            IEnumerable<FameChallengeClearRow> rows = (from member in BossClearMembers()
                                                      join clear in Context.BossClear.AsNoTracking() on member.ClearId equals clear.Id
                                                      where clear.RankingId == rankingId
                                                      select new FameChallengeClearRow {
                                                          CharacterId = member.CharacterId,
                                                          ClearId = clear.Id,
                                                          Seconds = clear.Seconds,
                                                          ClearTime = clear.ClearTime,
                                                      }).AsEnumerable();
            IOrderedEnumerable<FameChallengeClearRow> ordered = fastest
                ? rows.OrderBy(row => row.Seconds).ThenBy(row => row.ClearTime).ThenBy(row => row.ClearId)
                : rows.OrderBy(row => row.ClearTime).ThenBy(row => row.ClearId);
            // GroupBy keeps the order of each key's first row, and that row is the character's best.
            return ordered.GroupBy(row => row.CharacterId).Select(group => group.First()).ToList();
        }

        private IList<BossClearRankInfo> FameChallengeBoard(int rankingId, List<FameChallengeClearRow> rows) {
            long[] characterIds = rows.Select(row => row.CharacterId).ToArray();
            Dictionary<long, BossClearMemberInfo> characters = Context.Character.AsNoTracking()
                .Where(character => characterIds.Contains(character.Id))
                .Select(character => new { character.Id, character.Name, character.Profile, character.Level, character.Job })
                .AsEnumerable()
                .ToDictionary(character => character.Id, character => new BossClearMemberInfo(character.Id, character.Name,
                    character.Profile.Picture ?? string.Empty, character.Level, character.Job.Code()));

            return rows.Where(row => characters.ContainsKey(row.CharacterId))
                .Select((row, index) => new BossClearRankInfo(
                    Rank: index + 1,
                    ClearId: row.ClearId,
                    RankingId: rankingId,
                    LeaderId: row.CharacterId,
                    Seconds: row.Seconds,
                    ClearTime: row.ClearTime.ToEpochSeconds(),
                    Members: [characters[row.CharacterId]]))
                .ToList();
        }

        private static BossClearCountRankInfo ToCountRankInfo(BossClearCountRow row, int rank) {
            return new BossClearCountRankInfo(rank, row.CharacterId, row.Name, row.Profile?.Picture ?? string.Empty, row.Clears);
        }

        private class BossClearMemberRow {
            public long ClearId { get; init; }
            public long CharacterId { get; init; }
            public string Name { get; init; } = string.Empty;
            public Model.Profile Profile { get; init; } = null!;
            public short Level { get; init; }
            public Job Job { get; init; }
        }

        private class FameChallengeClearRow {
            public long CharacterId { get; init; }
            public long ClearId { get; init; }
            public int Seconds { get; init; }
            public DateTime ClearTime { get; init; }
        }

        private class BossClearCountRow {
            public long CharacterId { get; init; }
            public int Clears { get; init; }
            public DateTime LastClear { get; init; }
            public string Name { get; init; } = string.Empty;
            public Model.Profile? Profile { get; init; }
        }
    }
}

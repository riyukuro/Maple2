using Maple2.Database.Extensions;
using Maple2.Database.Model.Ranking;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The time (epoch seconds) of the account's last house commendation, 0 when it has none.
        public long GetLastHomeCommend(long accountId) {
            DateTime? time = Context.HomeCommend.AsNoTracking()
                .Where(commend => commend.AccountId == accountId)
                .OrderByDescending(commend => commend.Time)
                .Select(commend => (DateTime?) commend.Time)
                .FirstOrDefault();
            return time?.ToEpochSeconds() ?? 0;
        }

        // Saves a commendation of the owner's home by the account. False when the home or the account is not there or
        // the row was not saved.
        public bool AddHomeCommend(long ownerId, long accountId, long time) {
            if (!Context.Home.Any(home => home.AccountId == ownerId) || !Context.Account.Any(account => account.Id == accountId)) {
                return false;
            }

            Context.HomeCommend.Add(new Model.HomeCommend {
                OwnerId = ownerId,
                AccountId = accountId,
                Time = time.FromEpochSeconds(),
            });
            return Context.TrySaveChanges();
        }

        // The home's commendations in [monthBegin, monthEnd) and of all time (epoch seconds).
        public (int Current, int Total) GetHomeCommendScore(long ownerId, long monthBegin, long monthEnd) {
            DateTime begin = monthBegin.FromEpochSeconds();
            DateTime end = monthEnd.FromEpochSeconds();
            IQueryable<Model.HomeCommend> rows = Context.HomeCommend.AsNoTracking().Where(commend => commend.OwnerId == ownerId);
            int total = rows.Count();
            int current = total == 0 ? 0 : rows.Count(commend => commend.Time >= begin && commend.Time < end);
            return (current, total);
        }

        // The homes with their commendations in [begin, end): most commendations first, then the one that reached its
        // count first, then by the owner's account id.
        private List<HomeCommendCount> HomeCommendCounts(long begin, long end) {
            DateTime from = begin.FromEpochSeconds();
            DateTime to = end.FromEpochSeconds();
            return Context.HomeCommend.AsNoTracking()
                .Where(commend => commend.Time >= from && commend.Time < to)
                .GroupBy(commend => commend.OwnerId)
                .Select(group => new HomeCommendCount {
                    OwnerId = group.Key,
                    Commends = group.Count(),
                    Last = group.Max(commend => commend.Time),
                })
                .OrderByDescending(count => count.Commends)
                .ThenBy(count => count.Last)
                .ThenBy(count => count.OwnerId)
                .ToList();
        }

        // The row of a home on the board, null when its owner has no character (the board shows a character).
        private HomeCommendRankInfo? ToRankInfo(HomeCommendCount count, int rank) {
            var character = Context.Character.AsNoTracking()
                .Where(row => row.AccountId == count.OwnerId)
                .OrderByDescending(row => row.LastModified)
                .ThenBy(row => row.Id)
                .Select(row => new { row.Id, row.Name, row.Profile })
                .FirstOrDefault();
            if (character == null) {
                return null;
            }

            string[] names = Context.UgcMap.AsNoTracking()
                .Where(map => map.OwnerId == count.OwnerId)
                .OrderBy(map => map.Indoor)
                .Select(map => map.Name)
                .ToArray();
            return new HomeCommendRankInfo(
                Rank: rank,
                CharacterId: character.Id,
                Name: character.Name,
                Profile: character.Profile.Picture ?? string.Empty,
                Commends: count.Commends,
                AccountId: count.OwnerId,
                HomeName: names.FirstOrDefault() ?? string.Empty);
        }

        // The board of the span: the homes whose owner has a character, ranked 1 and on, the first 200.
        private IEnumerable<HomeCommendRankInfo> HomeCommendBoard(long begin, long end) {
            int rank = 0;
            foreach (HomeCommendCount count in HomeCommendCounts(begin, end)) {
                HomeCommendRankInfo? info = ToRankInfo(count, rank + 1);
                if (info == null) {
                    continue;
                }
                rank++;
                yield return info;
            }
        }

        public IList<HomeCommendRankInfo> GetHomeCommendRankings(long begin, long end) {
            return HomeCommendBoard(begin, end).Take(200).ToList();
        }

        public HomeCommendRankInfo? GetHomeCommendRankInfo(long begin, long end, long accountId) {
            return HomeCommendBoard(begin, end).FirstOrDefault(info => info.AccountId == accountId);
        }

        // The search by name finds the character as the trophy search does (GetCharacterId, any case), then its
        // account's home.
        public HomeCommendRankInfo? GetHomeCommendRankInfo(long begin, long end, string name) {
            long characterId = GetCharacterId(name);
            if (characterId == default) {
                return null;
            }
            long accountId = Context.Character.AsNoTracking()
                .Where(character => character.Id == characterId)
                .Select(character => character.AccountId)
                .FirstOrDefault();
            return accountId == default ? null : GetHomeCommendRankInfo(begin, end, accountId);
        }

        private class HomeCommendCount {
            public long OwnerId { get; init; }
            public int Commends { get; init; }
            public DateTime Last { get; init; }
        }
    }
}

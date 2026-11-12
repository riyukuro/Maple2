using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Enum;
using Microsoft.EntityFrameworkCore;
using GameEventUserValue = Maple2.Model.Game.GameEventUserValue;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public IList<GameEventUserValue> GetEventUserValues(long characterId) {
            return Context.GameEventUserValue.Where(model => model.CharacterId == characterId)
                .Select<Model.GameEventUserValue, GameEventUserValue>(userValue => userValue)
                .ToList();
        }

        // The event values kept for the account (a stamp card's claims), whatever its characters.
        public IList<GameEventUserValue> GetAccountEventValues(long accountId) {
            return Context.AccountEventValue.Where(model => model.AccountId == accountId)
                .Select<Model.AccountEventValue, GameEventUserValue>(userValue => userValue)
                .ToList();
        }

        public bool SaveAccountEventValues(long accountId, IList<GameEventUserValue> values) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            List<Model.AccountEventValue> existing = Context.AccountEventValue.Where(model => model.AccountId == accountId).ToList();
            foreach (GameEventUserValue value in values) {
                Model.AccountEventValue? model = existing.FirstOrDefault(row => row.EventId == value.EventId && row.Type == value.Type);
                if (model != null) {
                    model.Value = value.Value;
                    model.ExpirationTime = value.ExpirationTime;
                } else {
                    model = value;
                    model.AccountId = accountId;
                    Context.AccountEventValue.Add(model);
                }
            }

            return Context.TrySaveChanges();
        }

        public bool RemoveAccountEventValue(GameEventUserValue userValue, long accountId) {
            Model.AccountEventValue? model = Context.AccountEventValue.Find(accountId, userValue.EventId, userValue.Type);
            if (model == null) {
                return true;
            }
            Context.AccountEventValue.Remove(model);
            return Context.TrySaveChanges();
        }

        // The run of an event whose values last as long as it does (a stamp card, a long-term attendance). The end
        // of the run is kept under the event's id. While that end is not over, the event is the same run, also
        // when its end has moved (a replayed event is moved by every ingest): the values stay and the new end is
        // kept. Once the kept end is over and the event is live again, it is a new run: every character and account
        // value of the event and the given quests (a card's stamps) are dropped, so that it starts from nothing.
        // False for the same run. An event that is over itself is left alone. Times in epoch seconds.
        public bool BeginEventRun(int eventId, long end, long now, IReadOnlyCollection<int> questIds) {
            if (end <= now) {
                return false;
            }

            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            string key = EventRunKey(eventId);
            ServerInfo? run = Context.ServerInfo.Find(key);
            if (run == null) {
                Context.ServerInfo.Add(new ServerInfo { Key = key, LastModified = DateTimeOffset.FromUnixTimeSeconds(end).UtcDateTime });
                Context.SaveChanges();
                return false;
            }
            long keptEnd = new DateTimeOffset(DateTime.SpecifyKind(run.LastModified, DateTimeKind.Utc)).ToUnixTimeSeconds();
            if (keptEnd == end) {
                return false;
            }

            bool newRun = keptEnd <= now;
            if (newRun) {
                Context.GameEventUserValue.RemoveRange(Context.GameEventUserValue.Where(model => model.EventId == eventId).ToList());
                Context.AccountEventValue.RemoveRange(Context.AccountEventValue.Where(model => model.EventId == eventId).ToList());
                if (questIds.Count > 0) {
                    Context.Quest.RemoveRange(Context.Quest.Where(model => questIds.Contains(model.Id)).ToList());
                }
            }
            run.LastModified = DateTimeOffset.FromUnixTimeSeconds(end).UtcDateTime;
            Context.SaveChanges();
            return newRun;
        }

        public static string EventRunKey(int eventId) => $"EventRun:{eventId}";

        public void RemoveGameEventUserValue(long characterId, int eventId) {
            List<Model.GameEventUserValue> list = Context.GameEventUserValue
                .Where(model => model.CharacterId == characterId && model.EventId == eventId)
                .ToList();

            foreach (Model.GameEventUserValue model in list) {
                Context.GameEventUserValue.Remove(model);
            }
        }

        public bool RemoveGameEventUserValue(GameEventUserValue userValue, long characterId) {
            Model.GameEventUserValue model = userValue;
            model.CharacterId = characterId;
            Context.GameEventUserValue.Remove(model);
            return Context.TrySaveChanges();
        }

        public bool SaveGameEventUserValues(long characterId, IList<GameEventUserValue> values) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            Dictionary<int, Dictionary<GameEventUserValueType, Model.GameEventUserValue>> existing = Context.GameEventUserValue
                .Where(model => model.CharacterId == characterId)
                // Grouped here, not in the query: a GroupBy that ends a query is not translated by every provider.
                .AsEnumerable()
                .GroupBy(model => model.EventId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToDictionary(model => model.Type, model => model)
                );

            foreach (GameEventUserValue value in values) {
                if (existing.TryGetValue(value.EventId, out Dictionary<GameEventUserValueType, Model.GameEventUserValue>? modelDictionary) &&
                    modelDictionary.TryGetValue(value.Type, out Model.GameEventUserValue? model)) {
                    model.Value = value.Value;
                    model.ExpirationTime = value.ExpirationTime;
                    Context.GameEventUserValue.Update(model);
                } else {
                    model = value;
                    model.CharacterId = characterId;
                    Context.GameEventUserValue.Add(model);
                }
            }

            return Context.TrySaveChanges();
        }
    }
}

using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Helpers;
using Serilog;

namespace Maple2.Server.Game.Util;

// The returner gifts of the running ReturnUserYearRound rows (KR 180005: the returner box mail after 30 days away,
// once in 90 days), stored at entry in the load's transaction: each gift's mail, the mail's item and the account's
// gift time go in with the entry's stamp, so a failed entry stores none of them and a stored gift always comes with
// the time that stops it next time (EventMailRules.YearRound).
public static class ReturnUserGift {
    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(ReturnUserGift));

    // False when a write failed: the caller does not commit the load. A gift whose item cannot be made (no metadata,
    // not a played item) is left out and not marked.
    public static bool Store(GameStorage.Request db, long accountId, long characterId, IEnumerable<GameEventMetadata> running,
            long lastSave, long now, Func<RewardItem, Item?> makeItem) {
        List<GameEventMetadata> rows = running.Where(meta => meta.Data is ReturnUserYearRound).ToList();
        if (rows.Count == 0 || lastSave <= 0) {
            return true;
        }
        IList<GameEventUserValue> held = db.GetAccountEventValues(accountId);
        long? LastGiven(int eventId) {
            GameEventUserValue? value = held.FirstOrDefault(value => value.EventId == eventId && value.Type == GameEventUserValueType.ReturnUserGiftTime);
            return value != null && long.TryParse(value.Value, out long time) ? time : null;
        }

        var times = new List<GameEventUserValue>();
        foreach (EventMailRules.YearRoundGift gift in EventMailRules.YearRound(rows, characterId, lastSave, now, LastGiven)) {
            Item? item = makeItem(gift.Item);
            if (item == null) {
                Logger.Error("Returner gift of event {EventId}: item {ItemId} cannot be made, not given to {CharacterId}", gift.EventId, gift.Item.ItemId, characterId);
                continue;
            }
            Mail? mail = db.CreateMail(gift.Mail);
            if (mail == null) {
                return false;
            }
            item.Slot = -1;
            if (db.CreateItem(mail.Id, item) == null) {
                return false;
            }
            var time = new GameEventUserValue(GameEventUserValueType.ReturnUserGiftTime, now + gift.WaitDays * 86400L, gift.EventId);
            time.SetValue(now.ToString());
            times.Add(time);
        }
        return times.Count == 0 || db.SaveAccountEventValues(accountId, times);
    }
}

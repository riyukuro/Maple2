using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class EventRewardHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.EventReward;

    private enum Command : byte {
        ClaimStampReward = 0,
        ClaimStampCompleteReward = 1,
        OpenEventBox = 2,
        ClaimTimeRunFinalReward = 3,
        ClaimTimeRunStepReward = 4,
        VipGmClick = 5,
        FlipGalleryCard = 7,
        ClaimGalleryQuestReward = 8,
        ClaimGalleryReward = 9,
        ClaimSnowmanDailyReward = 12,
        ClaimSnowmanAccumReward = 13,
        ClaimLevelUpEventReward = 15,
        SetEventCharacter = 16,
        ClaimCollectItemGroupReward = 19,
        OpenBingo = 20,
        CrossOffBingoNumber = 22,
        ClaimBingoReward = 23,
        ClaimLongTermAttendReward = 25,
        LongTermAttend = 26,
        LongTermAttendOpen = 27,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.ClaimTimeRunStepReward:
                HandleClaimTimeRunStepReward(session, packet);
                return;
            case Command.ClaimTimeRunFinalReward:
                HandleClaimTimeRunFinalReward(session);
                return;
            case Command.FlipGalleryCard:
                HandleFlipGalleryCard(session, packet);
                return;
            case Command.ClaimGalleryQuestReward:
                HandleClaimGalleryQuestReward(session, packet);
                return;
            case Command.ClaimGalleryReward:
                HandleClaimGalleryReward(session);
                return;
            case Command.OpenBingo:
                HandleOpenBingo(session);
                return;
            case Command.CrossOffBingoNumber:
                HandleCrossOffBingoNumber(session, packet);
                return;
            case Command.ClaimBingoReward:
                HandleClaimBingoReward(session, packet);
                return;
            case Command.LongTermAttendOpen:
                session.GameEvent.GrantLongTermAttendPoints();
                return;
            case Command.LongTermAttend:
                HandleLongTermAttend(session, packet);
                return;
            case Command.ClaimLongTermAttendReward:
                HandleClaimLongTermAttendReward(session, packet);
                return;
            case Command.ClaimStampReward:
                HandleClaimStampReward(session, packet);
                return;
            case Command.ClaimCollectItemGroupReward:
                HandleClaimCollectItemGroupReward(session, packet);
                return;
            case Command.ClaimStampCompleteReward:
                HandleClaimStampCompleteReward(session);
                return;
            case Command.OpenEventBox:
                HandleOpenEventBox(session);
                return;
            case Command.ClaimSnowmanDailyReward:
                HandleClaimSnowman(session, packet.ReadByte(), false);
                return;
            case Command.ClaimSnowmanAccumReward:
                HandleClaimSnowman(session, packet.ReadByte(), true);
                return;
            // Sub-types of event kinds the server neither stores nor sends (GameEventTable has no record for the
            // level-up package and burning level-up events, and GameEvent writes none of them), so the client's
            // dialogs have no event to show and there is nothing to pay out. The client has no refusal text for
            // them: read and logged.
            // Client writers:
            //   5      uibuddyeventlistener.cpp:667 (needs client feature 0x82)
            //   15     uileveluppackagewidgetdialog.cpp:580
            //   16     uicharacterlistdialog.cpp:2093
            case Command.VipGmClick: // always 1; sent by any player when the buddy event page opens, not a GM mode
                Logger.Debug("EventReward {Command} from {CharacterId}: {Value}", command, session.CharacterId, packet.ReadByte());
                return;
            case Command.ClaimLevelUpEventReward: // event id
                Logger.Debug("EventReward {Command} from {CharacterId}: {Value}", command, session.CharacterId, packet.ReadInt());
                return;
            case Command.SetEventCharacter: // character id picked in the character list
                Logger.Debug("EventReward {Command} from {CharacterId}: character {Target}", command, session.CharacterId, packet.ReadLong());
                return;
        }
    }

    // Long-term attendance. 27 (no body; gameeventcli.cpp:1194 when the event loads,
    // uilongtermattendgiftdialog.cpp:348 when the dialog asks again) gives the day's attend point. 26 + byte
    // (uilongtermattendgiftdialog.cpp:385 sends 1, :535 sends 0) attends one missed day with a point (0) or for
    // the event's merat price in merets (1). 25 + int day (:511) claims the reward row of an attended day. As in
    // KMS2 the user values are updated first, then the reply (26 sends the points, then the days attended).
    private void HandleLongTermAttend(GameSession session, IByteReader packet) {
        bool withMerat = packet.ReadByte() == 1;
        GameEvent? gameEvent = session.FindEvent(GameEventType.LongTermAttendGift).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not LongTermAttendGift gift) {
            return;
        }

        int points = session.GameEvent.Get(GameEventUserValueType.LongTermAttendPoints, gameEvent.Id, gameEvent.EndTime).Int();
        int attended = session.GameEvent.Get(GameEventUserValueType.LongTermAttendDays, gameEvent.Id, gameEvent.EndTime).Int();
        int missed = LongTermAttendRules.MissedDays(gameEvent.Metadata.StartTime, DateTime.Now, attended, gift.MaxAttendance);
        LongTermAttendRules.AttendStep step = LongTermAttendRules.CheckAttend(withMerat, points, missed, session.Currency.Meret, gift.Merat);
        switch (step) {
            case LongTermAttendRules.AttendStep.Point:
                session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.LongTermAttendPoints, points - 1);
                break;
            case LongTermAttendRules.AttendStep.Merat:
                if (!session.Currency.TryTakeMeret(gift.Merat)) {
                    Logger.Debug("LongTermAttend from {CharacterId} refused: {Step}", session.CharacterId, LongTermAttendRules.AttendStep.NoMerat);
                    return;
                }
                break;
            default:
                // The client checks the same before it sends; it has no reply for a refusal.
                Logger.Debug("LongTermAttend from {CharacterId} refused: {Step}", session.CharacterId, step);
                return;
        }

        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.LongTermAttendDays, attended + 1);
        session.Send(EventRewardPacket.LongTermAttend());
    }

    // Item collection. 19 + int event id (uicollectitemgroupeventdialog.cpp:415) claims the result of the running
    // CollectItemGroup event of that id, once per account: the player has every condition item, in the bag or,
    // for a count of 1, worn (EventSaleRules.CollectMissing, the test the window's button makes). The client has
    // no reply to it; its window refreshes when user value 2200 of the event changes, and shows the event as
    // received while that is above 0. Chosen here: the condition items stay with the player. The client only
    // tests that they are there, counts a worn piece and shows no cost.
    private void HandleClaimCollectItemGroupReward(GameSession session, IByteReader packet) {
        int eventId = packet.ReadInt();
        GameEvent? gameEvent = session.FindEvent(GameEventType.CollectItemGroup).FirstOrDefault(found => found.Id == eventId);
        if (gameEvent?.Metadata.Data is not CollectItemGroup collect || session.Field == null) {
            Logger.Debug("ClaimCollectItemGroupReward from {CharacterId} refused: no event {EventId}", session.CharacterId, eventId);
            return;
        }

        if (session.GameEvent.Get(GameEventUserValueType.CollectItemGroup, gameEvent.Id, gameEvent.EndTime).Int() > 0) {
            Logger.Debug("ClaimCollectItemGroupReward from {CharacterId} refused: event {EventId} received", session.CharacterId, eventId);
            return;
        }

        RewardItem? missing = EventSaleRules.CollectMissing(collect,
            (itemId, rarity) => session.Item.Inventory.Find(itemId, rarity).Sum(item => item.Amount),
            itemId => session.Item.Equips.Gear.Values.Concat(session.Item.Equips.Outfit.Values).Concat(session.Item.Equips.Badge.Values)
                .Any(item => item.Id == itemId));
        if (missing != null) {
            Logger.Debug("ClaimCollectItemGroupReward from {CharacterId} refused: event {EventId} item {ItemId}", session.CharacterId, eventId, missing.Value.ItemId);
            return;
        }

        Item? result = session.Field.ItemDrop.CreateItem(collect.Result.ItemId, collect.Result.Rarity, collect.Result.Amount);
        if (result == null) {
            Logger.Error("ClaimCollectItemGroupReward: event {EventId} item {ItemId} could not be made", gameEvent.Id, collect.Result.ItemId);
            return;
        }
        if (!session.Item.AddOrMail(result, true)) {
            Logger.Error("ClaimCollectItemGroupReward: event {EventId} item {ItemId} went to neither bag nor mail", gameEvent.Id, result.Id);
            return;
        }
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.CollectItemGroup, 1);
        // Stored with the reward, not at logout: the result may sit in a stored mail already.
        if (!session.GameEvent.Store(gameEvent.Id, GameEventUserValueType.CollectItemGroup)) {
            Logger.Error("ClaimCollectItemGroupReward: the claim of event {EventId} by {CharacterId} was not stored", gameEvent.Id, session.CharacterId);
        }
    }

    private void HandleClaimLongTermAttendReward(GameSession session, IByteReader packet) {
        int day = packet.ReadInt();
        GameEvent? gameEvent = session.FindEvent(GameEventType.LongTermAttendGift).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not LongTermAttendGift gift || session.Field == null) {
            return;
        }

        int attended = session.GameEvent.Get(GameEventUserValueType.LongTermAttendDays, gameEvent.Id, gameEvent.EndTime).Int();
        GameEventUserValue claimed = session.GameEvent.Get(GameEventUserValueType.LongTermAttendClaimedDays, gameEvent.Id, gameEvent.EndTime);
        LongTermAttendGift.Reward? reward = LongTermAttendRules.CheckClaim(gift, day, attended, claimed.Value);
        if (reward == null) {
            Logger.Debug("ClaimLongTermAttendReward from {CharacterId} refused: day {Day}", session.CharacterId, day);
            return;
        }

        // Both items of the row, as the data gives them; an empty slot gives nothing.
        var items = new List<Item>();
        foreach (RewardItem rewardItem in new[] { reward.Item01, reward.Item02 }) {
            if (rewardItem.ItemId == 0) {
                continue;
            }
            Item? item = session.Field.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
            if (item == null) {
                Logger.Error("ClaimLongTermAttendReward: event {EventId} day {Day} item {ItemId} could not be made", gameEvent.Id, day, rewardItem.ItemId);
                return;
            }
            items.Add(item);
        }

        // Each item goes to the bag, or by mail when the bag does not take it. The day is marked once an item
        // has landed; when the first one lands nowhere the day stays open and there is no reply.
        int given = 0;
        foreach (Item item in items) {
            if (session.Item.AddOrMail(item, true)) {
                given++;
                continue;
            }
            Logger.Error("ClaimLongTermAttendReward: event {EventId} day {Day} item {ItemId} went to neither bag nor mail", gameEvent.Id, day, item.Id);
            if (given == 0) {
                return;
            }
        }
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.LongTermAttendClaimedDays, LongTermAttendRules.AddClaimedDay(claimed.Value, day));
        session.Send(EventRewardPacket.ClaimLongTermAttendReward());
    }

    // Stamp card. 0 + int count + byte special (uistampeventdialog.cpp:319) claims the reward row of that count
    // and kind; 1 (no body, :331) the complete reward. The user value is updated first, then the reply with the
    // items for the reward notice. The client has no reply for a refusal.
    private void HandleClaimStampReward(GameSession session, IByteReader packet) {
        int count = packet.ReadInt();
        bool special = packet.ReadByte() != 0;
        GameEvent? gameEvent = StampEvent(session);
        if (gameEvent?.Metadata.Data is not StampEvent stampEvent || session.Field == null) {
            return;
        }
        session.Quest.CompleteStampQuests();

        GameEventUserValueType type = special ? GameEventUserValueType.StampSpecialRewardsClaimed : GameEventUserValueType.StampRewardsClaimed;
        GameEventUserValue claimed = session.GameEvent.Get(type, gameEvent.Id, gameEvent.EndTime);
        StampEvent.Reward? reward = StampEventRules.CheckReward(stampEvent, count, special, claimed.Value, questId => QuestCompleted(session, questId));
        if (reward == null) {
            Logger.Debug("ClaimStampReward from {CharacterId} refused: count {Count}, special {Special}", session.CharacterId, count, special);
            return;
        }

        List<RewardItem>? given = GiveStampItems(session, gameEvent.Id, [reward.Item01, reward.Item02]);
        if (given == null) {
            return;
        }
        session.GameEvent.Set(gameEvent.Id, type, LongTermAttendRules.AddClaimedDay(claimed.Value, count));
        session.Send(EventRewardPacket.ClaimStampReward(given));
    }

    private void HandleClaimStampCompleteReward(GameSession session) {
        GameEvent? gameEvent = StampEvent(session);
        if (gameEvent?.Metadata.Data is not StampEvent stampEvent || session.Field == null) {
            return;
        }
        session.Quest.CompleteStampQuests();

        GameEventUserValue claimed = session.GameEvent.Get(GameEventUserValueType.StampCompleteRewardClaimed, gameEvent.Id, gameEvent.EndTime);
        if (!StampEventRules.CanClaimComplete(stampEvent, claimed.Value, questId => QuestCompleted(session, questId))) {
            Logger.Debug("ClaimStampCompleteReward from {CharacterId} refused", session.CharacterId);
            return;
        }

        List<RewardItem>? given = GiveStampItems(session, gameEvent.Id, [stampEvent.CompleteReward]);
        if (given == null) {
            return;
        }
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.StampCompleteRewardClaimed, 1);
        session.Send(EventRewardPacket.ClaimStampCompleteReward(given));
    }

    private static GameEvent? StampEvent(GameSession session) {
        return StampEventRules.PickEvent(session.FindEvent(GameEventType.StampEvent), gameEvent => gameEvent.Metadata.Data as StampEvent);
    }

    private static bool QuestCompleted(GameSession session, int questId) {
        return session.Quest.TryGetQuest(questId, out Quest? quest) && quest.State == QuestState.Completed;
    }

    // The items of a stamp reward, each to the bag or by mail when the bag does not take it; an empty slot gives
    // nothing. Null (nothing claimed, no reply) when an item cannot be made or the first one lands nowhere.
    private List<RewardItem>? GiveStampItems(GameSession session, int eventId, RewardItem[] rewardItems) {
        var items = new List<(Item Item, RewardItem Reward)>();
        foreach (RewardItem rewardItem in rewardItems) {
            if (rewardItem.ItemId == 0) {
                continue;
            }
            Item? item = session.Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
            if (item == null) {
                Logger.Error("Stamp reward: event {EventId} item {ItemId} could not be made", eventId, rewardItem.ItemId);
                return null;
            }
            items.Add((item, rewardItem));
        }

        var given = new List<RewardItem>();
        foreach ((Item item, RewardItem rewardItem) in items) {
            if (session.Item.AddOrMail(item, true)) {
                given.Add(rewardItem);
                continue;
            }
            Logger.Error("Stamp reward: event {EventId} item {ItemId} went to neither bag nor mail", eventId, item.Id);
            if (given.Count == 0) {
                return null;
            }
        }
        return given;
    }

    // Time run. 4 + int key (uitimeruneventdialog.cpp onClickReward) claims the step reward of that key, sent only
    // for the next key after the one taken (user value 1501) once the distance run reaches it; 3 (no body,
    // onClickFinalReward) the final reward, sent only while 1500 is 0 and once the whole distance is run. The
    // distance is the sum of the rewarded quests' distances (GetEarnedDistance). The client has no reply for a
    // refusal.
    private void HandleClaimTimeRunStepReward(GameSession session, IByteReader packet) {
        int key = packet.ReadInt();

        GameEvent? gameEvent = session.FindEvent(GameEventType.TimeRunEvent).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not TimeRunEvent timeRunEvent || session.Field == null) {
            return;
        }

        int taken = session.GameEvent.Get(GameEventUserValueType.TimeRunStepRewardKey, gameEvent.Id, gameEvent.EndTime).Int();
        if (!TimeRunEventRules.CanClaimStep(timeRunEvent, key, taken, questId => QuestCompleted(session, questId))
            || !timeRunEvent.StepRewards.TryGetValue(key, out RewardItem rewardItem)) {
            Logger.Debug("ClaimTimeRunStepReward from {CharacterId} refused: key {Key}, taken {Taken}", session.CharacterId, key, taken);
            return;
        }

        Item? item = session.Field.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
        if (item == null) {
            Logger.Error("Time run reward: event {EventId} item {ItemId} could not be made", gameEvent.Id, rewardItem.ItemId);
            return;
        }

        if (!session.Item.AddOrMail(item, true)) {
            Logger.Error("Time run reward: event {EventId} item {ItemId} went to neither bag nor mail", gameEvent.Id, item.Id);
            return;
        }
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.TimeRunStepRewardKey, key);
        StoreClaim(session, gameEvent.Id, GameEventUserValueType.TimeRunStepRewardKey);
        session.Send(EventRewardPacket.ClaimTimeRunStepReward(rewardItem));
    }

    // The item box (ItemBox): 2, no body, from the open box dialog's button (uiitemeventopenboxdialog.cpp:226).
    // The dialog opens on entering the event's field while the event runs, unless the day the box was last taken
    // (user value 1400, yyyymmdd of the server's time, keyed by the event id) is today or later
    // (uiitemeventopenboxdialog.cpp:164). The server checks the same, gives the box item (to the bag, else by mail),
    // stores the day and replies 2 with true; any refusal replies false, which shows the client's failure notice.
    // Chosen here: the day is kept for the account (no client code says which), so that the account's other
    // characters do not take the day's box again.
    private void HandleOpenEventBox(GameSession session) {
        if (session.Field == null) {
            return;
        }
        int today = EventItemBoxRules.Day(session.GameEvent.Clock());
        GameEvent? gameEvent = session.FindEvent(GameEventType.ItemBox)
            .Where(found => found.Metadata.Data is ItemBox box && box.FieldId == session.Field.MapId)
            .MinBy(found => found.Id);
        if (gameEvent?.Metadata.Data is not ItemBox itemBox) {
            Logger.Debug("OpenEventBox from {CharacterId} refused: no item box in field {MapId}", session.CharacterId, session.Field.MapId);
            session.Send(EventRewardPacket.OpenEventBox(false));
            return;
        }

        int last = session.GameEvent.Get(GameEventUserValueType.ItemBoxReceivedDay, gameEvent.Id, gameEvent.EndTime).Int();
        if (!EventItemBoxRules.CanOpen(last, today)) {
            Logger.Debug("OpenEventBox from {CharacterId} refused: taken on {Day}", session.CharacterId, last);
            session.Send(EventRewardPacket.OpenEventBox(false));
            return;
        }

        Item? item = session.Field.ItemDrop.CreateItem(itemBox.BoxItemId);
        if (item == null) {
            Logger.Error("Item box: event {EventId} item {ItemId} could not be made", gameEvent.Id, itemBox.BoxItemId);
            session.Send(EventRewardPacket.OpenEventBox(false));
            return;
        }
        if (!session.Item.AddOrMail(item, true)) {
            Logger.Error("Item box: event {EventId} item {ItemId} went to neither bag nor mail", gameEvent.Id, item.Id);
            session.Send(EventRewardPacket.OpenEventBox(false));
            return;
        }
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.ItemBoxReceivedDay, today);
        StoreClaim(session, gameEvent.Id, GameEventUserValueType.ItemBoxReceivedDay);
        session.Send(EventRewardPacket.OpenEventBox(true));
    }

    // The snowman (Snowman): 12 + byte index (uisnowmaneventdialog.cpp:343) takes a snowman's daily reward once it
    // has its goal, 13 + byte index (:365) its accum reward once all its completes reach the accum count. The server
    // checks what the dialog's buttons test (SnowmanRules), gives the items (to the bag, else by mail) and stores
    // the values; a daily claim starts the snowman again (its snowflakes to 0) and counts one more complete for the
    // day and for the event. The reply (12 or 13 + the items) shows the reward notice (notify 20061). The
    // client has no refusal text: a refused claim is logged and has no reply.
    // Chosen here: the snowflakes and today's completes run out at the day's end, all completes and the accum
    // claims at the event's end.
    private void HandleClaimSnowman(GameSession session, byte index, bool accum) {
        if (session.Field == null) {
            return;
        }
        GameEvent? gameEvent = session.GameEvent.SnowmanEvent();
        if (gameEvent?.Metadata.Data is not Snowman snowman || index >= snowman.Snowmen.Length) {
            Logger.Debug("Snowman claim {Index} from {CharacterId} refused: no such snowman", index, session.CharacterId);
            return;
        }
        Snowman.Entry entry = snowman.Snowmen[index];
        int length = snowman.Snowmen.Length;
        DateTime time = session.GameEvent.Clock();
        long now = time.ToEpochSeconds();
        long dayEnd = GameEventManager.DtRewardExpiry(time);
        int[] snowflakes = SnowmanRules.Counts(session.GameEvent.Get(GameEventUserValueType.SnowflakeCount, gameEvent.Id, dayEnd, now).Value, length);
        int[] daily = SnowmanRules.Counts(session.GameEvent.Get(GameEventUserValueType.DailyCompleteCount, gameEvent.Id, dayEnd, now).Value, length);
        int[] accumCompletes = SnowmanRules.Counts(session.GameEvent.Get(GameEventUserValueType.AccumCompleteCount, gameEvent.Id, gameEvent.EndTime, now).Value, length);
        HashSet<int> received = SnowmanRules.Indexes(session.GameEvent.Get(GameEventUserValueType.AccumCompleteRewardReceived, gameEvent.Id, gameEvent.EndTime, now).Value);

        RewardItem[] rewards;
        if (accum) {
            if (!SnowmanRules.CanClaimAccum(entry, accumCompletes[index], received.Contains(index))) {
                Logger.Debug("Snowman accum claim {Index} from {CharacterId} refused: {Count} completes", index, session.CharacterId, accumCompletes[index]);
                return;
            }
            rewards = entry.AccumRewards;
            received.Add(index);
            session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.AccumCompleteRewardReceived, SnowmanRules.Join(received.Order()));
            StoreClaim(session, gameEvent.Id, GameEventUserValueType.AccumCompleteRewardReceived);
        } else {
            if (!SnowmanRules.CanClaimDaily(entry, snowflakes[index], daily[index])) {
                Logger.Debug("Snowman daily claim {Index} from {CharacterId} refused: {Snowflakes} snowflakes, {Daily} today", index, session.CharacterId, snowflakes[index], daily[index]);
                return;
            }
            rewards = entry.Rewards;
            snowflakes[index] = 0;
            daily[index]++;
            accumCompletes[index]++;
            session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.SnowflakeCount, SnowmanRules.Join(snowflakes));
            session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.DailyCompleteCount, SnowmanRules.Join(daily));
            session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.AccumCompleteCount, SnowmanRules.Join(accumCompletes));
            StoreClaim(session, gameEvent.Id, GameEventUserValueType.SnowflakeCount);
            StoreClaim(session, gameEvent.Id, GameEventUserValueType.DailyCompleteCount);
            StoreClaim(session, gameEvent.Id, GameEventUserValueType.AccumCompleteCount);
        }

        var given = new List<RewardItem>();
        foreach (RewardItem rewardItem in rewards) {
            Item? item = session.Field.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
            if (item == null) {
                Logger.Error("Snowman: event {EventId} item {ItemId} could not be made", gameEvent.Id, rewardItem.ItemId);
                continue;
            }
            if (!session.Item.AddOrMail(item, true)) {
                Logger.Error("Snowman: event {EventId} item {ItemId} went to neither bag nor mail", gameEvent.Id, item.Id);
                continue;
            }
            given.Add(rewardItem);
        }
        session.Send(EventRewardPacket.ClaimSnowmanReward(accum, given));
    }

    private void HandleClaimTimeRunFinalReward(GameSession session) {
        GameEvent? gameEvent = session.FindEvent(GameEventType.TimeRunEvent).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not TimeRunEvent timeRunEvent || session.Field == null) {
            return;
        }

        int received = session.GameEvent.Get(GameEventUserValueType.TimeRunFinalRewardReceived, gameEvent.Id, gameEvent.EndTime).Int();
        if (!TimeRunEventRules.CanClaimFinal(timeRunEvent, received, questId => QuestCompleted(session, questId))) {
            Logger.Debug("ClaimTimeRunFinalReward from {CharacterId} refused: received {Received}", session.CharacterId, received);
            return;
        }

        RewardItem rewardItem = timeRunEvent.FinalReward;
        Item? item = session.Field.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
        if (item == null) {
            Logger.Error("Time run reward: event {EventId} item {ItemId} could not be made", gameEvent.Id, rewardItem.ItemId);
            return;
        }

        if (!session.Item.AddOrMail(item, true)) {
            Logger.Error("Time run reward: event {EventId} item {ItemId} went to neither bag nor mail", gameEvent.Id, item.Id);
            return;
        }
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.TimeRunFinalRewardReceived, 1);
        StoreClaim(session, gameEvent.Id, GameEventUserValueType.TimeRunFinalRewardReceived);
        session.Send(EventRewardPacket.ClaimTimeRunFinalReward(rewardItem));
    }

    private void HandleFlipGalleryCard(GameSession session, IByteReader packet) {
        byte index = packet.ReadByte();

        // Get only the first gallery event. There should only be one.
        GameEvent? gameEvent = session.FindEvent(GameEventType.Gallery).FirstOrDefault();

        if (gameEvent?.Metadata.Data is not Gallery gallery) {
            return;
        }

        if (index >= gallery.QuestIds.Length) {
            return;
        }

        int questId = gallery.QuestIds[index];

        // Check flip limit before starting the quest
        GameEventUserValue userValue = session.GameEvent.Get(
            GameEventUserValueType.GalleryCardFlipCount,
            gameEvent.Id,
            DateTime.Today.AddDays(1).ToEpochSeconds());
        int value = userValue.Int();
        if (value >= gallery.RevealDayLimit) {
            return;
        }

        QuestError error = session.Quest.Start(questId);
        if (error != QuestError.none) {
            session.Send(QuestPacket.Error(error));
            return;
        }

        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.GalleryCardFlipCount, value + 1);

        session.Send(EventRewardPacket.FlipGalleryCard(index));
    }

    private void HandleClaimGalleryQuestReward(GameSession session, IByteReader packet) {
        byte index = packet.ReadByte();

        GameEvent? gameEvent = session.FindEvent(GameEventType.Gallery).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not Gallery gallery || index >= gallery.QuestIds.Length) {
            return;
        }

        // The client does not show what the card's reward is; here it is the reward of the card's quest.
        // A card is flipped by sub-type 7, which starts its quest: no reward for a card that is not flipped.
        if (!session.Quest.TryGetQuest(gallery.QuestIds[index], out Quest? quest) || quest.State != QuestState.Started) {
            return;
        }

        if (!session.Quest.Complete(quest)) {
            return;
        }

        session.Send(EventRewardPacket.ClaimGalleryQuestReward(index));
    }

    private void HandleClaimGalleryReward(GameSession session) {
        GameEvent? gameEvent = session.FindEvent(GameEventType.Gallery).FirstOrDefault();

        if (gameEvent?.Metadata.Data is not Gallery gallery) {
            return;
        }

        bool allQuestsCompleted = gallery.QuestIds.All(questId =>
            session.Quest.TryGetQuest(questId, out Quest? quest) && quest.State == QuestState.Completed);
        if (!allQuestsCompleted) {
            // All quests need to be completed.
            return;
        }

        GameEventUserValue userValue = session.GameEvent.Get(GameEventUserValueType.GalleryClaimReward, gameEvent.Id, gameEvent.EndTime);
        int value = userValue.Int();
        if (value > 0) {
            // already claimed
            return;
        }

        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.GalleryClaimReward, value + 1);

        foreach (RewardItem rewardItem in gallery.RewardItems) {
            Item? item = session.Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
            if (item == null) {
                continue;
            }

            if (!session.Item.Inventory.Add(item)) {
                session.Item.MailItem(item);
            }
        }

        session.Send(EventRewardPacket.ClaimGalleryReward(gallery.RewardItems));
    }

    // Bingo. 20 opens the card: the board seed, the checked numbers, the claimed rows and today's numbers. The
    // dialog (uibingoeventdialog.cpp) crosses a number with 22 + int pencil + int number: the free pencil only for a
    // number called today, the cash pencil for any number of the board; 21 answers with the new lists. 23 + int row
    // claims a reward row, offered once its line count is crossed (BingoEventRules). Refusals get no reply.
    private void HandleOpenBingo(GameSession session) {
        GameEvent? gameEvent = session.FindEvent(GameEventType.BingoEvent).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not BingoEvent bingoEvent) {
            return;
        }

        // This uid sets up what board layout the user will have.
        GameEventUserValue uid = session.GameEvent.Get(GameEventUserValueType.BingoUid, gameEvent.Id, gameEvent.EndTime);
        // The client draws no board for seed 0 (UIBingoEventDialog), so a 0 is drawn again too.
        if (!int.TryParse(uid.Value, out int seed) || seed == 0) {
            seed = Random.Shared.Next(1, int.MaxValue);
            session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.BingoUid, seed);
        }

        GameEventUserValue checkedNumbers = session.GameEvent.Get(GameEventUserValueType.BingoNumbersChecked, gameEvent.Id, gameEvent.EndTime);
        GameEventUserValue rewardsClaimed = session.GameEvent.Get(GameEventUserValueType.BingoRewardsClaimed, gameEvent.Id, gameEvent.EndTime);

        int[] today = BingoEventRules.TodayNumbers(bingoEvent, gameEvent.StartTime.FromEpochSeconds(), DateTime.Now);
        session.Send(EventRewardPacket.OpenBingo(seed, checkedNumbers.Value, rewardsClaimed.Value, today));
    }

    private void HandleCrossOffBingoNumber(GameSession session, IByteReader packet) {
        GameEvent? gameEvent = session.FindEvent(GameEventType.BingoEvent).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not BingoEvent bingoEvent) {
            return;
        }

        int itemId = packet.ReadInt();
        int bingoNumber = packet.ReadInt();

        // Check if the item is a pencil
        if (itemId != bingoEvent.PencilItemId && itemId != bingoEvent.PencilPlusItemId || !BingoEventRules.OnBoard(bingoNumber)) {
            return;
        }

        GameEventUserValue checkedNumbers = session.GameEvent.Get(GameEventUserValueType.BingoNumbersChecked, gameEvent.Id, gameEvent.EndTime);
        List<int> checkedNumbersList = BingoEventRules.Parse(checkedNumbers.Value);
        if (checkedNumbersList.Contains(bingoNumber)) {
            return;
        }

        if (itemId == bingoEvent.PencilItemId) {
            int[] today = BingoEventRules.TodayNumbers(bingoEvent, gameEvent.StartTime.FromEpochSeconds(), DateTime.Now);
            if (!today.Contains(bingoNumber)) {
                return;
            }
        }

        Item? item = session.Item.Inventory.Find(itemId).FirstOrDefault();
        if (item == null || !session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        checkedNumbersList.Add(bingoNumber);
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.BingoNumbersChecked, string.Join(',', checkedNumbersList));

        GameEventUserValue rewardsClaimed = session.GameEvent.Get(GameEventUserValueType.BingoRewardsClaimed, gameEvent.Id, gameEvent.EndTime);

        session.Send(EventRewardPacket.UpdateBingo(checkedNumbers.Value, rewardsClaimed.Value));
    }

    private void HandleClaimBingoReward(GameSession session, IByteReader packet) {
        int index = packet.ReadInt();
        GameEvent? gameEvent = session.FindEvent(GameEventType.BingoEvent).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not BingoEvent bingoEvent || session.Field == null) {
            return;
        }

        GameEventUserValue rewardsClaimed = session.GameEvent.Get(GameEventUserValueType.BingoRewardsClaimed, gameEvent.Id, gameEvent.EndTime);
        List<int> rewardsClaimedList = BingoEventRules.Parse(rewardsClaimed.Value);
        if (rewardsClaimedList.Contains(index)) {
            return;
        }

        GameEventUserValue uid = session.GameEvent.Get(GameEventUserValueType.BingoUid, gameEvent.Id, gameEvent.EndTime);
        GameEventUserValue checkedNumbers = session.GameEvent.Get(GameEventUserValueType.BingoNumbersChecked, gameEvent.Id, gameEvent.EndTime);
        int lines = int.TryParse(uid.Value, out int seed) ? BingoEventRules.CountLines(seed, BingoEventRules.Parse(checkedNumbers.Value)) : 0;
        if (!BingoEventRules.CanClaim(bingoEvent, index, lines)) {
            Logger.Debug("ClaimBingoReward from {CharacterId} refused: row {Index}, lines {Lines}", session.CharacterId, index, lines);
            return;
        }

        rewardsClaimedList.Add(index);
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.BingoRewardsClaimed, string.Join(',', rewardsClaimedList));
        StoreClaim(session, gameEvent.Id, GameEventUserValueType.BingoRewardsClaimed);

        foreach (RewardItem rewardItem in bingoEvent.Rewards[index].Items) {
            Item? item = session.Field.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
            if (item == null) {
                continue;
            }

            if (!session.Item.AddOrMail(item, true)) {
                Logger.Error("Bingo reward: event {EventId} item {ItemId} went to neither bag nor mail", gameEvent.Id, item.Id);
            }
        }
        session.Send(EventRewardPacket.ClaimBingoReward(bingoEvent.Rewards[index].Items));
        session.Send(EventRewardPacket.UpdateBingo(checkedNumbers.Value, rewardsClaimed.Value));
    }

    // A claim is stored with its reward, not at logout: the reward may sit in a stored mail already (a full bag),
    // and a claim lost with the session would pay again after a restart.
    private void StoreClaim(GameSession session, int eventId, GameEventUserValueType type) {
        if (!session.GameEvent.Store(eventId, type)) {
            Logger.Error("The claim {Type} of event {EventId} by {CharacterId} was not stored", type, eventId, session.CharacterId);
        }
    }
}

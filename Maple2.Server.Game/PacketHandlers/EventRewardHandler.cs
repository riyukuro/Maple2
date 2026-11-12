using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
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
            case Command.ClaimStampCompleteReward:
                HandleClaimStampCompleteReward(session);
                return;
            // Sub-types of event kinds the server neither stores nor sends (GameEventTable has no record for the
            // item box, snowman, level-up package, burning level-up and item collection events, and
            // GameEvent writes none of them), so the client's dialogs have no event to show and
            // there is nothing to pay out. The client has no refusal text for them: read and logged.
            // Client writers:
            //   2      uiitemeventopenboxdialog.cpp:226
            //   5      uibuddyeventlistener.cpp:667 (needs client feature 0x82)
            //   12, 13 uisnowmaneventdialog.cpp:343, :365
            //   15     uileveluppackagewidgetdialog.cpp:580
            //   16     uicharacterlistdialog.cpp:2093
            //   19     uicollectitemgroupeventdialog.cpp:415
            case Command.OpenEventBox: // the event box dialog's open button; no body
                Logger.Debug("EventReward {Command} from {CharacterId}", command, session.CharacterId);
                return;
            case Command.VipGmClick: // always 1; sent by any player when the buddy event page opens, not a GM mode
            case Command.ClaimSnowmanDailyReward: // snowman index
            case Command.ClaimSnowmanAccumReward: // snowman index
                Logger.Debug("EventReward {Command} from {CharacterId}: {Value}", command, session.CharacterId, packet.ReadByte());
                return;
            case Command.ClaimLevelUpEventReward: // event id
            case Command.ClaimCollectItemGroupReward: // event id
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
                session.Currency.Meret -= gift.Merat;
                break;
            default:
                // The client checks the same before it sends; it has no reply for a refusal.
                Logger.Debug("LongTermAttend from {CharacterId} refused: {Step}", session.CharacterId, step);
                return;
        }

        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.LongTermAttendDays, attended + 1);
        session.Send(EventRewardPacket.LongTermAttend());
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

    private void HandleClaimTimeRunStepReward(GameSession session, IByteReader packet) {
        int steps = packet.ReadInt();

        GameEvent? gameEvent = session.FindEvent(GameEventType.TimeRunEvent).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not TimeRunEvent timeRunEvent) {
            return;
        }

        if (!timeRunEvent.StepRewards.TryGetValue(steps, out RewardItem rewardItem)) {
            return;
        }

        Item? item = session.Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
        if (item == null) {
            return;
        }

        if (!session.Item.Inventory.Add(item, true)) {
            session.Item.MailItem(item);
        }

        session.Send(EventRewardPacket.ClaimTimeRunStepReward(rewardItem));
    }

    private void HandleClaimTimeRunFinalReward(GameSession session) {
        GameEvent? gameEvent = session.FindEvent(GameEventType.TimeRunEvent).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not TimeRunEvent timeRunEvent) {
            return;
        }

        RewardItem rewardItem = timeRunEvent.FinalReward;

        Item? item = session.Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
        if (item == null) {
            return;
        }

        if (!session.Item.Inventory.Add(item, true)) {
            session.Item.MailItem(item);
        }

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

    private void HandleOpenBingo(GameSession session) {
        GameEvent? gameEvent = session.FindEvent(GameEventType.BingoEvent).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not BingoEvent bingoEvent) {
            return;
        }

        DateTime startTime = gameEvent.StartTime.FromEpochSeconds();
        // days since event started
        int days = Math.Max(0, (int) (DateTime.UtcNow - startTime).TotalDays);
        if (days >= bingoEvent.Numbers.Length) {
            Logger.Error("Bingo event is incorrectly set up. There are not enough days for the event.");
            return;
        }

        // This uid sets up what board layout the user will have.
        GameEventUserValue uid = session.GameEvent.Get(GameEventUserValueType.BingoUid, gameEvent.Id, gameEvent.EndTime);
        if (string.IsNullOrEmpty(uid.Value)) {
            session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.BingoUid, Random.Shared.Next().ToString());
        }

        GameEventUserValue checkedNumbers = session.GameEvent.Get(GameEventUserValueType.BingoNumbersChecked, gameEvent.Id, gameEvent.EndTime);
        GameEventUserValue rewardsClaimed = session.GameEvent.Get(GameEventUserValueType.BingoRewardsClaimed, gameEvent.Id, gameEvent.EndTime);

        session.Send(EventRewardPacket.OpenBingo(int.Parse(uid.Value), checkedNumbers.Value, rewardsClaimed.Value, bingoEvent.Numbers[days]));
    }

    private void HandleCrossOffBingoNumber(GameSession session, IByteReader packet) {
        GameEvent? gameEvent = session.FindEvent(GameEventType.BingoEvent).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not BingoEvent bingoEvent) {
            return;
        }

        int itemId = packet.ReadInt();
        int bingoNumber = packet.ReadInt();

        // Check if the item is a pencil
        if (itemId != bingoEvent.PencilItemId && itemId != bingoEvent.PencilPlusItemId) {
            return;
        }

        GameEventUserValue checkedNumbers = session.GameEvent.Get(GameEventUserValueType.BingoNumbersChecked, gameEvent.Id, gameEvent.EndTime);
        List<int> checkedNumbersList = string.IsNullOrEmpty(checkedNumbers.Value) ? [] : checkedNumbers.Value.Split(',').Select(int.Parse).ToList();
        if (checkedNumbersList.Contains(bingoNumber)) {
            return;
        }

        if (itemId == bingoEvent.PencilItemId) {
            DateTime startTime = gameEvent.StartTime.FromEpochSeconds();
            int days = Math.Max(0, (int) (DateTime.UtcNow - startTime).TotalDays);
            if (!bingoEvent.Numbers[days].Contains(bingoNumber)) {
                return;
            }
        }

        Item? item = session.Item.Inventory.Find(itemId).FirstOrDefault();
        if (item == null) {
            return;
        }

        checkedNumbersList.Add(bingoNumber);
        session.Item.Inventory.Consume(item.Uid, 1);
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.BingoNumbersChecked, checkedNumbers.Value);

        GameEventUserValue rewardsClaimed = session.GameEvent.Get(GameEventUserValueType.BingoRewardsClaimed, gameEvent.Id, gameEvent.EndTime);

        session.Send(EventRewardPacket.UpdateBingo(checkedNumbers.Value, rewardsClaimed.Value));
    }

    private void HandleClaimBingoReward(GameSession session, IByteReader packet) {
        int index = packet.ReadInt();
        GameEvent? gameEvent = session.FindEvent(GameEventType.BingoEvent).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not BingoEvent bingoEvent) {
            return;
        }

        if (index >= bingoEvent.Rewards.Length) {
            return;
        }

        GameEventUserValue rewardsClaimed = session.GameEvent.Get(GameEventUserValueType.BingoRewardsClaimed, gameEvent.Id, gameEvent.EndTime);
        List<int> rewardsClaimedList = string.IsNullOrEmpty(rewardsClaimed.Value) ? [] : rewardsClaimed.Value.Split(',').Select(int.Parse).ToList();
        if (rewardsClaimedList.Contains(index)) {
            return;
        }
        rewardsClaimedList.Add(index);
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.BingoRewardsClaimed, string.Join(',', rewardsClaimedList));

        GameEventUserValue checkedNumbers = session.GameEvent.Get(GameEventUserValueType.BingoNumbersChecked, gameEvent.Id, gameEvent.EndTime);

        foreach (RewardItem rewardItem in bingoEvent.Rewards[index].Items) {
            Item? item = session.Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
            if (item == null) {
                continue;
            }

            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }
        session.Send(EventRewardPacket.ClaimBingoReward(bingoEvent.Rewards[index].Items));
        session.Send(EventRewardPacket.UpdateBingo(checkedNumbers.Value, rewardsClaimed.Value));
    }
}

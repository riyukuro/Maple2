using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The event reward claims through their handlers: the attendance gift (Attendance 0), the long-term attendance
// calendar (EventReward 25, 26, 27) and the stamp card (EventReward 0, 1).
public class EventRewardClaimTests {
    private const int EventId = 7001;
    private const int ItemA = 20000001;
    private const int ItemB = 20000002;
    private const int ItemC = 20000003;
    private const int Filler = 20000009;
    private const int QuestA = 60100001;
    private const int QuestB = 60100002;
    private const int QuestS = 60100003;

    private TestGameSession test = null!;
    private GameEvent gameEvent = null!;

    // fullBag: the Misc tab has one slot, taken by another item, so no reward fits.
    private GameSession Login(GameEventType type, GameEventData data, bool fullBag = false, DateTime? start = null,
                              Action<TestGameSession>? before = null) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ItemA), TestGameSession.Metadata(ItemB), TestGameSession.Metadata(ItemC));
        if (fullBag) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = [1, 0] });
            test.Store(TestGameSession.Metadata(Filler));
        }
        gameEvent = test.AddEvent(EventId, type, data, start);
        before?.Invoke(test);
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private List<string> SentHex() => test.TakeSent().Select(Convert.ToHexString).ToList();

    private static int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    private string? Value(GameSession session, GameEventUserValueType type) => session.GameEvent.Peek(type, EventId);

    private string UserValue(GameSession session, GameEventUserValueType type) {
        return Hex(GameEventUserValuePacket.Update(session.GameEvent.Get(type, EventId, gameEvent.EndTime)));
    }

    // The items of the character's mails, by item id.
    private List<int> MailedItems() {
        using var context = new Ms2Context(test.Options);
        List<long> mails = context.Mail.Where(mail => mail.ReceiverId == test.CharacterId).Select(mail => mail.Id).ToList();
        // The filler is the character's: it is left out, since a mail's id can be the same number as the character's.
        return context.Item.Where(item => mails.Contains(item.OwnerId) && item.ItemId != Filler).Select(item => item.ItemId).OrderBy(id => id).ToList();
    }

    #region AttendGift
    private static AttendGift Gift(int playSeconds = 0, params int[] itemIds) {
        return new AttendGift(itemIds.Select(id => new RewardItem(id, 1, 2)).ToArray(), "gift", "title", "content", string.Empty, playSeconds, null);
    }

    private void Attend(GameSession session) {
        new AttendanceHandler { ItemMetadata = null! }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));
    }

    // The day's gift goes by mail; the count of gifts (100) is sent before the day's completion time (101).
    [Test]
    public void AttendGift_Claim_MailsTheNextRewardAndCountsTheDay() {
        GameSession session = Login(GameEventType.AttendGift, Gift(0, ItemA, ItemB));
        long before = DateTime.Now.ToEpochSeconds();

        Attend(session);

        Assert.That(MailedItems(), Is.EqualTo(new[] { ItemA }));
        Assert.That(Held(session, ItemA), Is.EqualTo(0));
        Assert.That(Value(session, GameEventUserValueType.AttendanceRewardsClaimed), Is.EqualTo("1"));
        Assert.That(long.Parse(Value(session, GameEventUserValueType.AttendanceCompletedTimestamp)!), Is.InRange(before, before + 60));
        Assert.That(SentHex(), Is.EqualTo(new[] {
            UserValue(session, GameEventUserValueType.AttendanceRewardsClaimed),
            UserValue(session, GameEventUserValueType.AttendanceCompletedTimestamp),
        }));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void AttendGift_SecondClaimOfTheDay_IsRefusedWithTheUsedError() {
        GameSession session = Login(GameEventType.AttendGift, Gift(0, ItemA, ItemB));
        Attend(session);
        test.TakeSent();

        Attend(session);

        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(AttendancePacket.Error(AttendanceError.s_attendGift_item_attend_already_used)) }));
        Assert.That(MailedItems(), Is.EqualTo(new[] { ItemA }));
        Assert.That(Value(session, GameEventUserValueType.AttendanceRewardsClaimed), Is.EqualTo("1"));
    }

    // The client sends the claim when its countdown ends; before the event's play seconds nothing is given or sent.
    [Test]
    public void AttendGift_ClaimBeforeThePlayTime_GivesNothing() {
        GameSession session = Login(GameEventType.AttendGift, Gift(3600, ItemA));

        Attend(session);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(MailedItems(), Is.Empty);
        Assert.That(Value(session, GameEventUserValueType.AttendanceCompletedTimestamp), Is.Null);
    }

    // The seconds of earlier logins of the day (106) count toward the play time.
    [Test]
    public void AttendGift_StoredPlaySeconds_CountTowardThePlayTime() {
        GameSession session = Login(GameEventType.AttendGift, Gift(3600, ItemA));
        session.GameEvent.Get(GameEventUserValueType.AttendanceAccumulatedTime, EventId, DateTime.Now.Date.AddDays(1).ToEpochSeconds());
        session.GameEvent.Set(EventId, GameEventUserValueType.AttendanceAccumulatedTime, 3600);
        test.TakeSent();

        Attend(session);

        Assert.That(MailedItems(), Is.EqualTo(new[] { ItemA }));
    }

    // A day already completed yesterday: today's claim gives the second reward. After the last reward a claim
    // gives nothing and the day stays open.
    [Test]
    public void AttendGift_NextDay_GivesTheNextRewardThenNothing() {
        GameSession session = Login(GameEventType.AttendGift, Gift(0, ItemA, ItemB));
        Attend(session);
        session.GameEvent.Set(EventId, GameEventUserValueType.AttendanceCompletedTimestamp, DateTime.Now.AddDays(-1).ToEpochSeconds());

        Attend(session);

        Assert.That(MailedItems(), Is.EqualTo(new[] { ItemA, ItemB }));
        Assert.That(Value(session, GameEventUserValueType.AttendanceRewardsClaimed), Is.EqualTo("2"));

        long yesterday = DateTime.Now.AddDays(-1).ToEpochSeconds();
        session.GameEvent.Set(EventId, GameEventUserValueType.AttendanceCompletedTimestamp, yesterday);
        test.TakeSent();
        Attend(session);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(MailedItems(), Is.EqualTo(new[] { ItemA, ItemB }));
        Assert.That(Value(session, GameEventUserValueType.AttendanceCompletedTimestamp), Is.EqualTo(yesterday.ToString()));
    }

    // The gift is mailed, so a full bag changes nothing.
    [Test]
    public void AttendGift_FullBag_StillMailsTheReward() {
        GameSession session = Login(GameEventType.AttendGift, Gift(0, ItemA), fullBag: true);

        Attend(session);

        Assert.That(MailedItems(), Is.EqualTo(new[] { ItemA }));
        Assert.That(Value(session, GameEventUserValueType.AttendanceRewardsClaimed), Is.EqualTo("1"));
        Assert.That(test.Errors, Is.Empty);
    }

    // An item the item data lacks: nothing is counted, so the day can be claimed once the data is there.
    [Test]
    public void AttendGift_UnknownItem_CountsNothing() {
        GameSession session = Login(GameEventType.AttendGift, Gift(0, 29999999));

        Attend(session);

        Assert.That(MailedItems(), Is.Empty);
        Assert.That(Value(session, GameEventUserValueType.AttendanceCompletedTimestamp), Is.Null.Or.Empty);
        Assert.That(Value(session, GameEventUserValueType.AttendanceRewardsClaimed) ?? "", Is.Not.EqualTo("1"));
    }
    #endregion

    #region LongTermAttendGift
    private static LongTermAttendGift LongTerm(int merat = 30) {
        return new LongTermAttendGift("calendar", "tag", 5, merat, [
            new LongTermAttendGift.Reward(1, new RewardItem(ItemA, 1, 2), new RewardItem(ItemB, 1, 1)),
            new LongTermAttendGift.Reward(2, new RewardItem(ItemC, 1, 1), default),
        ]);
    }

    private static void Reward(GameSession session, byte command, Action<ByteWriter>? body = null) {
        new EventRewardHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            body?.Invoke(writer);
        }));
    }

    private static void Open(GameSession session) => Reward(session, 27);
    private static void AttendDay(GameSession session, bool merat) => Reward(session, 26, writer => writer.WriteByte((byte) (merat ? 1 : 0)));
    private static void ClaimDay(GameSession session, int day) => Reward(session, 25, writer => writer.WriteInt(day));

    // 27 gives the day's point once: the points, the time of the grant, then the reply.
    [Test]
    public void LongTerm_Open_GivesOnePointADay() {
        GameSession session = Login(GameEventType.LongTermAttendGift, LongTerm());

        Open(session);

        Assert.That(Value(session, GameEventUserValueType.LongTermAttendPoints), Is.EqualTo("1"));
        Assert.That(SentHex(), Is.EqualTo(new[] {
            UserValue(session, GameEventUserValueType.LongTermAttendPoints),
            UserValue(session, GameEventUserValueType.LongTermAttendPointTime),
            Hex(EventRewardPacket.LongTermAttendPoint()),
        }));

        Open(session);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Value(session, GameEventUserValueType.LongTermAttendPoints), Is.EqualTo("1"));
    }

    // 26 with a point: the points, then the days attended, then the reply. Without a point it is refused silently.
    [Test]
    public void LongTerm_AttendWithPoint_TakesThePointAndCountsTheDay() {
        GameSession session = Login(GameEventType.LongTermAttendGift, LongTerm());
        Open(session);
        test.TakeSent();

        AttendDay(session, merat: false);

        Assert.That(Value(session, GameEventUserValueType.LongTermAttendPoints), Is.EqualTo("0"));
        Assert.That(Value(session, GameEventUserValueType.LongTermAttendDays), Is.EqualTo("1"));
        Assert.That(SentHex(), Is.EqualTo(new[] {
            UserValue(session, GameEventUserValueType.LongTermAttendPoints),
            UserValue(session, GameEventUserValueType.LongTermAttendDays),
            Hex(EventRewardPacket.LongTermAttend()),
        }));

        AttendDay(session, merat: false);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Value(session, GameEventUserValueType.LongTermAttendDays), Is.EqualTo("1"));
        Assert.That(test.Errors, Is.Empty);
    }

    // 26 with merets: the price is taken, the day counted; without the merets nothing changes.
    [Test]
    public void LongTerm_AttendWithMerets_TakesThePrice() {
        GameSession session = Login(GameEventType.LongTermAttendGift, LongTerm(merat: 30));

        AttendDay(session, merat: true);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Value(session, GameEventUserValueType.LongTermAttendDays), Is.Null.Or.Empty.Or.EqualTo("0"));

        session.Currency.Meret = 50;
        test.TakeSent();
        AttendDay(session, merat: true);

        Assert.That(session.Currency.Meret, Is.EqualTo(20));
        Assert.That(Value(session, GameEventUserValueType.LongTermAttendDays), Is.EqualTo("1"));
        List<string> sent = SentHex();
        Assert.That(sent[^2..], Is.EqualTo(new[] {
            UserValue(session, GameEventUserValueType.LongTermAttendDays),
            Hex(EventRewardPacket.LongTermAttend()),
        }));
        Assert.That(test.Errors, Is.Empty);
    }

    private GameSession LoginAttended(int days, bool fullBag = false) {
        GameSession session = Login(GameEventType.LongTermAttendGift, LongTerm(), fullBag);
        session.GameEvent.Get(GameEventUserValueType.LongTermAttendDays, EventId, gameEvent.EndTime);
        session.GameEvent.Set(EventId, GameEventUserValueType.LongTermAttendDays, days);
        test.TakeSent();
        return session;
    }

    // 25: both items of the day's row land in the bag; the claimed days are sent, then the reply last.
    [Test]
    public void LongTerm_ClaimDay_GivesBothItemsAndMarksTheDay() {
        GameSession session = LoginAttended(1);

        ClaimDay(session, 1);

        Assert.That(Held(session, ItemA), Is.EqualTo(2));
        Assert.That(Held(session, ItemB), Is.EqualTo(1));
        Assert.That(Value(session, GameEventUserValueType.LongTermAttendClaimedDays), Is.EqualTo("1"));
        List<string> sent = SentHex();
        Assert.That(sent[^2..], Is.EqualTo(new[] {
            UserValue(session, GameEventUserValueType.LongTermAttendClaimedDays),
            Hex(EventRewardPacket.ClaimLongTermAttendReward()),
        }));
        Assert.That(MailedItems(), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void LongTerm_ClaimDayTwice_IsRefused() {
        GameSession session = LoginAttended(1);
        ClaimDay(session, 1);
        test.TakeSent();

        ClaimDay(session, 1);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Held(session, ItemA), Is.EqualTo(2));
        Assert.That(Value(session, GameEventUserValueType.LongTermAttendClaimedDays), Is.EqualTo("1"));
    }

    // A day not attended yet, and a day with no reward row.
    [Test]
    public void LongTerm_ClaimDayNotAttendedOrUnknown_IsRefused() {
        GameSession session = LoginAttended(1);

        ClaimDay(session, 2);
        ClaimDay(session, 3);
        ClaimDay(session, 0);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Held(session, ItemC), Is.EqualTo(0));
        Assert.That(Value(session, GameEventUserValueType.LongTermAttendClaimedDays) ?? "", Is.Empty);
    }

    [Test]
    public void LongTerm_ClaimDay_FullBagMailsTheItems() {
        GameSession session = LoginAttended(1, fullBag: true);

        ClaimDay(session, 1);

        Assert.That(Held(session, ItemA), Is.EqualTo(0));
        Assert.That(MailedItems(), Is.EqualTo(new[] { ItemA, ItemB }));
        Assert.That(Value(session, GameEventUserValueType.LongTermAttendClaimedDays), Is.EqualTo("1"));
        Assert.That(SentHex()[^1], Is.EqualTo(Hex(EventRewardPacket.ClaimLongTermAttendReward())));
        Assert.That(test.Errors, Is.Empty);
    }

    // The values of an earlier login are read at the next one, so a claimed day stays claimed.
    [Test]
    public void LongTerm_ClaimedDay_StaysClaimedAfterRelog() {
        GameSession session = LoginAttended(2);
        ClaimDay(session, 1);
        var rows = new List<(GameEventUserValueType, string)> {
            (GameEventUserValueType.LongTermAttendDays, Value(session, GameEventUserValueType.LongTermAttendDays)!),
            (GameEventUserValueType.LongTermAttendClaimedDays, Value(session, GameEventUserValueType.LongTermAttendClaimedDays)!),
        };

        session = Login(GameEventType.LongTermAttendGift, LongTerm(), before: next => Insert(next, rows));
        ClaimDay(session, 1);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Held(session, ItemA), Is.EqualTo(0));

        ClaimDay(session, 2);

        Assert.That(Held(session, ItemC), Is.EqualTo(1));
        Assert.That(Value(session, GameEventUserValueType.LongTermAttendClaimedDays), Is.EqualTo("1,2"));
    }
    #endregion

    #region StampEvent
    // Two stamps and a special one; a reward at one stamp (two items), at two stamps and at one special stamp.
    private static StampEvent Stamps() {
        return new StampEvent("tag", 1, [
            new StampEvent.Stamp(QuestA, false, 0, 0),
            new StampEvent.Stamp(QuestB, false, 1, 0),
            new StampEvent.Stamp(QuestS, true, 2, 0),
        ], [
            new StampEvent.Reward(1, false, new RewardItem(ItemA, 1, 2), new RewardItem(ItemB, 1, 1)),
            new StampEvent.Reward(2, false, new RewardItem(ItemB, 1, 3), default),
            new StampEvent.Reward(1, true, new RewardItem(ItemC, 1, 1), default),
        ], new RewardItem(ItemC, 4, 5));
    }

    private GameSession LoginStamps(bool fullBag = false, params int[] completed) {
        GameSession session = Login(GameEventType.StampEvent, Stamps(), fullBag);
        foreach (int questId in completed) {
            test.SetQuest(questId, QuestState.Completed);
        }
        return session;
    }

    private static void ClaimStamp(GameSession session, int count, bool special = false) {
        Reward(session, 0, writer => {
            writer.WriteInt(count);
            writer.WriteByte((byte) (special ? 1 : 0));
        });
    }

    private static void ClaimComplete(GameSession session) => Reward(session, 1);

    // 0: the row's items land in the bag; the claimed counts are sent, then the reply with the items.
    [Test]
    public void Stamp_ClaimReward_GivesTheRowAndMarksIt() {
        GameSession session = LoginStamps(false, QuestA);

        ClaimStamp(session, 1);

        Assert.That(Held(session, ItemA), Is.EqualTo(2));
        Assert.That(Held(session, ItemB), Is.EqualTo(1));
        Assert.That(Value(session, GameEventUserValueType.StampRewardsClaimed), Is.EqualTo("1"));
        Assert.That(Value(session, GameEventUserValueType.StampSpecialRewardsClaimed) ?? "", Is.Empty);
        List<string> sent = SentHex();
        Assert.That(sent[^2..], Is.EqualTo(new[] {
            UserValue(session, GameEventUserValueType.StampRewardsClaimed),
            Hex(EventRewardPacket.ClaimStampReward([new RewardItem(ItemA, 1, 2), new RewardItem(ItemB, 1, 1)])),
        }));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Stamp_ClaimRewardTwice_IsRefused() {
        GameSession session = LoginStamps(false, QuestA);
        ClaimStamp(session, 1);
        test.TakeSent();

        ClaimStamp(session, 1);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Held(session, ItemA), Is.EqualTo(2));
        Assert.That(Value(session, GameEventUserValueType.StampRewardsClaimed), Is.EqualTo("1"));
    }

    // A row needs as many completed stamps of its kind: a started quest is no stamp, a special stamp counts only
    // for the special rows.
    [Test]
    public void Stamp_ClaimRewardWithoutTheStamps_IsRefused() {
        GameSession session = LoginStamps(false, QuestA, QuestS);
        Quest started = test.SetQuest(QuestB, QuestState.Started);
        started.Conditions.Add(0, new Quest.Condition(new ConditionMetadata(ConditionType.map, 1, null, null)));

        ClaimStamp(session, 2);
        ClaimStamp(session, 3);
        ClaimStamp(session, 2, special: true);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Held(session, ItemB), Is.EqualTo(0));
        Assert.That(Value(session, GameEventUserValueType.StampRewardsClaimed) ?? "", Is.Empty);
        Assert.That(started.State, Is.EqualTo(QuestState.Started));
    }

    // A started stamp quest that is due (its completion failed before) is completed by the claim, which then
    // counts its stamp.
    [Test]
    public void Stamp_Claim_CompletesADueStampQuestFirst() {
        GameSession session = LoginStamps(false, QuestA);
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        Quest due = test.SetQuest(QuestB, QuestState.Started);

        ClaimStamp(session, 2);

        Assert.That(due.State, Is.EqualTo(QuestState.Completed));
        Assert.That(Held(session, ItemB), Is.EqualTo(3));
        Assert.That(Value(session, GameEventUserValueType.StampRewardsClaimed), Is.EqualTo("2"));
    }

    [Test]
    public void Stamp_ClaimSpecialReward_MarksTheSpecialValue() {
        GameSession session = LoginStamps(false, QuestS);

        ClaimStamp(session, 1, special: true);

        Assert.That(Held(session, ItemC), Is.EqualTo(1));
        Assert.That(Value(session, GameEventUserValueType.StampSpecialRewardsClaimed), Is.EqualTo("1"));
        Assert.That(Value(session, GameEventUserValueType.StampRewardsClaimed) ?? "", Is.Empty);
        Assert.That(SentHex()[^1], Is.EqualTo(Hex(EventRewardPacket.ClaimStampReward([new RewardItem(ItemC, 1, 1)]))));

        ClaimStamp(session, 1);

        Assert.That(SentHex(), Is.Empty);
    }

    [Test]
    public void Stamp_ClaimReward_FullBagMailsTheItems() {
        GameSession session = LoginStamps(true, QuestA);

        ClaimStamp(session, 1);

        Assert.That(Held(session, ItemA), Is.EqualTo(0));
        Assert.That(MailedItems(), Is.EqualTo(new[] { ItemA, ItemB }));
        Assert.That(Value(session, GameEventUserValueType.StampRewardsClaimed), Is.EqualTo("1"));
        Assert.That(SentHex()[^1], Is.EqualTo(Hex(EventRewardPacket.ClaimStampReward([new RewardItem(ItemA, 1, 2), new RewardItem(ItemB, 1, 1)]))));
        Assert.That(test.Errors, Is.Empty);
    }

    // 1: the complete reward, once every stamp is done.
    [Test]
    public void Stamp_ClaimComplete_NeedsEveryStampAndIsGivenOnce() {
        GameSession session = LoginStamps(false, QuestA, QuestB);

        ClaimComplete(session);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Held(session, ItemC), Is.EqualTo(0));

        test.SetQuest(QuestS, QuestState.Completed);
        ClaimComplete(session);

        Assert.That(Held(session, ItemC), Is.EqualTo(5));
        Assert.That(Value(session, GameEventUserValueType.StampCompleteRewardClaimed), Is.EqualTo("1"));
        List<string> sent = SentHex();
        Assert.That(sent[^2..], Is.EqualTo(new[] {
            UserValue(session, GameEventUserValueType.StampCompleteRewardClaimed),
            Hex(EventRewardPacket.ClaimStampCompleteReward([new RewardItem(ItemC, 4, 5)])),
        }));

        ClaimComplete(session);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Held(session, ItemC), Is.EqualTo(5));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Stamp_ClaimComplete_FullBagMailsTheItem() {
        GameSession session = LoginStamps(true, QuestA, QuestB, QuestS);

        ClaimComplete(session);

        Assert.That(MailedItems(), Is.EqualTo(new[] { ItemC }));
        Assert.That(Value(session, GameEventUserValueType.StampCompleteRewardClaimed), Is.EqualTo("1"));
        Assert.That(test.Errors, Is.Empty);
    }

    // An item the item data lacks: nothing is given or marked, and there is no reply.
    [Test]
    public void Stamp_ClaimRewardOfAnUnknownItem_MarksNothing() {
        GameSession session = Login(GameEventType.StampEvent, Stamps() with { CompleteReward = new RewardItem(29999999, 1, 1) });
        foreach (int questId in new[] { QuestA, QuestB, QuestS }) {
            test.SetQuest(questId, QuestState.Completed);
        }

        ClaimComplete(session);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Value(session, GameEventUserValueType.StampCompleteRewardClaimed) ?? "", Is.Empty);
        Assert.That(Held(session, 29999999), Is.EqualTo(0));
    }

    // The claims are the account's: what another character of the account claimed is claimed here too.
    [Test]
    public void Stamp_ClaimOfTheAccount_IsRefusedHere() {
        GameSession session = Login(GameEventType.StampEvent, Stamps(), before: next => {
            using var context = new Ms2Context(next.Options);
            long end = (long) (DateTime.Now.AddDays(30).ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
            foreach (GameEventUserValueType type in new[] { GameEventUserValueType.StampRewardsClaimed, GameEventUserValueType.StampCompleteRewardClaimed }) {
                context.AccountEventValue.Add(new Maple2.Database.Model.AccountEventValue {
                    AccountId = next.AccountId, EventId = EventId, Type = type, Value = "1", ExpirationTime = end,
                });
            }
            context.SaveChanges();
        });
        foreach (int questId in new[] { QuestA, QuestB, QuestS }) {
            test.SetQuest(questId, QuestState.Completed);
        }

        ClaimStamp(session, 1);
        ClaimComplete(session);

        Assert.That(SentHex(), Is.Empty);
        Assert.That(Held(session, ItemA), Is.EqualTo(0));

        ClaimStamp(session, 2);

        Assert.That(Held(session, ItemB), Is.EqualTo(3));
        Assert.That(Value(session, GameEventUserValueType.StampRewardsClaimed), Is.EqualTo("1,2"));
    }
    #endregion

    // Stored user value rows of the event, ending with it.
    private void Insert(TestGameSession target, List<(GameEventUserValueType Type, string Value)> rows) {
        using var context = new Ms2Context(target.Options);
        long end = (long) (DateTime.Now.AddDays(30).ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
        foreach ((GameEventUserValueType type, string value) in rows) {
            context.GameEventUserValue.Add(new Maple2.Database.Model.GameEventUserValue {
                CharacterId = target.CharacterId, EventId = EventId, Type = type, Value = value, ExpirationTime = end,
            });
        }
        context.SaveChanges();
    }
}

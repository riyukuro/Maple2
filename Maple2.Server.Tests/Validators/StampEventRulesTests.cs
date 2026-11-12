using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Validators;

// The stamp card (GameEventStampEvent); made-up ids, quests and items.
public class StampEventRulesTests {
    private static readonly StampEvent Card = new("testcard", 0, [
        new StampEvent.Stamp(880001, false, 10, 20),
        new StampEvent.Stamp(880002, false, 30, 40),
        new StampEvent.Stamp(880003, true, 50, 60),
    ], [
        new StampEvent.Reward(1, false, new RewardItem(900001, 1, 5), LongTermAttendRules.EmptyItem),
        new StampEvent.Reward(2, false, new RewardItem(900002, 2, 1), new RewardItem(900003, 1, 3)),
        new StampEvent.Reward(1, true, new RewardItem(900004, 4, 1), LongTermAttendRules.EmptyItem),
    ], new RewardItem(900005, 3, 2));

    private const string Value1 = "<ms2><tag event=\"testcard\"/><stamp quest=\"880001\" special=\"0\" x=\"10\" y=\"20\"/>"
                                  + "<stamp quest=\"880002\" x=\"30\" y=\"40\"/><stamp quest=\"880003\" special=\"1\" x=\"50\" y=\"60\"/></ms2>";
    private const string Value2 = "<ms2><reward count=\"1\" item01=\"900001,1,5\"/><reward count=\"2\" item01=\"900002,2,1\" item02=\"900003,1,3\"/>"
                                  + "<reward special=\"1\" count=\"1\" item01=\"900004,4,1\"/></ms2>";

    private static Func<int, bool> Done(params int[] quests) => quests.Contains;

    [Test]
    public void Reader_ParsesStampsRewardsCompleteRewardAndKey() {
        StampEvent? read = StampEventReader.Parse(Value1, Value2, "900005,3,2", "");
        Assert.That(read, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(read!.EventTag, Is.EqualTo("testcard"));
            Assert.That(read.Key, Is.EqualTo(0));
            Assert.That(read.Stamps, Is.EqualTo(Card.Stamps));
            Assert.That(read.Rewards, Is.EqualTo(Card.Rewards));
            Assert.That(read.CompleteReward, Is.EqualTo(Card.CompleteReward));
            Assert.That(StampEventReader.Parse(Value1, Value2, "900005,3,2", "10001")!.Key, Is.EqualTo(10001));
        });
    }

    // A row that cannot be read is skipped, it does not stop the ingest.
    [Test]
    public void Reader_ReturnsNullForARowItCannotRead() {
        Assert.Multiple(() => {
            Assert.That(StampEventReader.Parse("", Value2, "", ""), Is.Null);
            Assert.That(StampEventReader.Parse("some text", Value2, "", ""), Is.Null);
            Assert.That(StampEventReader.Parse("<ms2><stamp", Value2, "", ""), Is.Null);
            Assert.That(StampEventReader.Parse(Value1, "<ms2><reward count=\"1\"", "", ""), Is.Null);
            Assert.That(StampEventReader.Parse("<ms2><tag event=\"testcard\"/></ms2>", Value2, "", ""), Is.Null); // no stamp
            Assert.That(StampEventReader.Parse(Value1, "<ms2></ms2>", "", "")!.Rewards, Is.Empty);
        });
    }

    [Test]
    public void GameEvent_WritesWhatTheClientReads() {
        var gameEvent = new GameEvent(Event(990040, "2030-06-15-08-00", "2030-07-15-08-00", Card));
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("StampEvent"));
        Assert.That(reader.ReadInt(), Is.EqualTo(990040));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("testcard"));
        Assert.That(reader.ReadLong(), Is.EqualTo(gameEvent.EndTime));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadBool(), Is.True);
        foreach ((int quest, byte special, int x, int y) in new[] { (880001, (byte) 0, 10, 20), (880002, (byte) 0, 30, 40), (880003, (byte) 1, 50, 60) }) {
            Assert.That(reader.ReadInt(), Is.EqualTo(quest));
            Assert.That(reader.ReadByte(), Is.EqualTo(special));
            Assert.That(reader.ReadInt(), Is.EqualTo(x));
            Assert.That(reader.ReadInt(), Is.EqualTo(y));
        }
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        AssertItem(reader, 900001, 1, 5);
        AssertItem(reader, 0, 1, 1);
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        AssertItem(reader, 900002, 2, 1);
        AssertItem(reader, 900003, 1, 3);
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        AssertItem(reader, 900004, 4, 1);
        AssertItem(reader, 0, 1, 1);
        AssertItem(reader, 900005, 3, 2);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
        Assert.That(gameEvent.SentToClient, Is.True);
    }

    private static void AssertItem(ByteReader reader, int itemId, short rarity, int amount) {
        Assert.That(reader.ReadInt(), Is.EqualTo(itemId));
        Assert.That(reader.ReadShort(), Is.EqualTo(rarity));
        Assert.That(reader.ReadInt(), Is.EqualTo(amount));
        for (int i = 0; i < 4; i++) {
            Assert.That(reader.ReadBool(), Is.False);
        }
    }

    // A reward row opens when as many stamps of its kind are done, once.
    [Test]
    public void CheckReward_RowOfTheCountAndKindWithEnoughStampsUnclaimed() {
        Assert.Multiple(() => {
            Assert.That(StampEventRules.CheckReward(Card, 1, false, "", Done()), Is.Null);
            Assert.That(StampEventRules.CheckReward(Card, 1, false, "", Done(880003)), Is.Null); // a special stamp is not a normal one
            Assert.That(StampEventRules.CheckReward(Card, 1, false, null, Done(880002)), Is.SameAs(Card.Rewards[0]));
            Assert.That(StampEventRules.CheckReward(Card, 2, false, "1", Done(880002)), Is.Null);
            Assert.That(StampEventRules.CheckReward(Card, 2, false, "1", Done(880001, 880002)), Is.SameAs(Card.Rewards[1]));
            Assert.That(StampEventRules.CheckReward(Card, 2, false, "1,2", Done(880001, 880002)), Is.Null); // claimed
            Assert.That(StampEventRules.CheckReward(Card, 1, true, "", Done(880001, 880002)), Is.Null);
            Assert.That(StampEventRules.CheckReward(Card, 1, true, "", Done(880003)), Is.SameAs(Card.Rewards[2]));
            Assert.That(StampEventRules.CheckReward(Card, 1, true, "1", Done(880003)), Is.Null);
            Assert.That(StampEventRules.CheckReward(Card, 3, false, "", Done(880001, 880002, 880003)), Is.Null); // no such row
            Assert.That(StampEventRules.CheckReward(Card, 0, false, "", Done(880001)), Is.Null);
            Assert.That(StampEventRules.CheckReward(Card, -1, false, "", Done(880001)), Is.Null);
        });
    }

    [Test]
    public void CanClaimComplete_EveryStampDoneAndNotClaimed() {
        Assert.Multiple(() => {
            Assert.That(StampEventRules.CanClaimComplete(Card, "", Done(880001, 880002)), Is.False);
            Assert.That(StampEventRules.CanClaimComplete(Card, null, Done(880001, 880002, 880003)), Is.True);
            Assert.That(StampEventRules.CanClaimComplete(Card, "0", Done(880001, 880002, 880003)), Is.True);
            Assert.That(StampEventRules.CanClaimComplete(Card, "1", Done(880001, 880002, 880003)), Is.False);
            Assert.That(StampEventRules.CanClaimComplete(Card with { Stamps = [] }, "", Done()), Is.False);
        });
    }

    [Test]
    public void Quests_OfEveryCard() {
        Assert.Multiple(() => {
            Assert.That(StampEventRules.Quests([Card, Card with { Stamps = [new StampEvent.Stamp(880009, false, 0, 0)] }]),
                Is.EquivalentTo(new[] { 880001, 880002, 880003, 880009 }));
        });
    }

    // Several live cards: the one without a key, the last of them as the client keeps it, else the first.
    [Test]
    public void PickEvent_TheLastCardWithoutAKeyElseTheFirst() {
        StampEvent keyed = Card with { Key = 10001 };
        StampEvent other = Card with { Key = 10002 };
        StampEvent later = Card with { Key = 0 };
        Assert.Multiple(() => {
            Assert.That(StampEventRules.PickEvent<StampEvent>([keyed, Card, other], card => card), Is.SameAs(Card));
            Assert.That(StampEventRules.PickEvent<StampEvent>([Card, keyed, later, other], card => card), Is.SameAs(later));
            Assert.That(StampEventRules.PickEvent<StampEvent>([keyed, other], card => card), Is.SameAs(keyed));
            Assert.That(StampEventRules.PickEvent<StampEvent>([], card => card), Is.Null);
            Assert.That(StampEventRules.PickEvent<string>(["x"], _ => null), Is.Null);
        });
    }

    // The replies: the sub-type, the item count, then an int id and a short rank for each item given.
    [Test]
    public void Replies_ListTheItemsGiven() {
        List<RewardItem> items = [new RewardItem(900002, 2, 1), new RewardItem(900003, 1, 3)];
        Assert.That(Bytes(EventRewardPacket.ClaimStampReward(items)), Is.EqualTo(new byte[] {
            0x00, 0x02, 0x00, 0x00, 0x00, 0xA2, 0xBB, 0x0D, 0x00, 0x02, 0x00, 0xA3, 0xBB, 0x0D, 0x00, 0x01, 0x00,
        }));
        Assert.That(Bytes(EventRewardPacket.ClaimStampCompleteReward([Card.CompleteReward])), Is.EqualTo(new byte[] {
            0x01, 0x01, 0x00, 0x00, 0x00, 0xA5, 0xBB, 0x0D, 0x00, 0x03, 0x00,
        }));
    }

    private static byte[] Bytes(ByteWriter writer) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.EventReward));
        return reader.ReadBytes(writer.Length - reader.Position);
    }

    [Test]
    public void UserValueTypes_AreTheClientKeys() {
        Assert.That((int) GameEventUserValueType.StampCompleteRewardClaimed, Is.EqualTo(1200));
        Assert.That((int) GameEventUserValueType.StampRewardsClaimed, Is.EqualTo(1201));
        Assert.That((int) GameEventUserValueType.StampSpecialRewardsClaimed, Is.EqualTo(1202));
    }

    // The replay takes the latest ended card whose reward items the client can rank and whose quests it has.
    [Test]
    public void Replay_PassesOverACardWithAQuestOrItemTheClientLacks() {
        var now = new DateTime(2030, 6, 15, 9, 30, 0);
        GameEventMetadata[] events = [
            Event(990041, "2029-01-01-00-00", "2029-02-01-00-00", Card),
            Event(990042, "2029-03-01-00-00", "2029-04-01-00-00", Card with { Stamps = [new StampEvent.Stamp(880099, false, 0, 0)] }),
            Event(990043, "2029-05-01-00-00", "2029-06-01-00-00", Card with { CompleteReward = new RewardItem(900099, 1, 1) }),
            Event(990044, "2030-06-01-00-00", "2030-07-01-00-00", Card), // not ended
        ];
        Func<int, int> rank = itemId => itemId == 900099 ? 0 : 1;
        Func<int, bool> quest = questId => questId != 880099;
        Assert.Multiple(() => {
            Assert.That(AttendGiftReplay.Types, Does.Contain(GameEventType.StampEvent));
            Assert.That(AttendGiftReplay.Pick(events, now, GameEventType.StampEvent)?.Id, Is.EqualTo(990043));
            Assert.That(AttendGiftReplay.Pick(events, now, GameEventType.StampEvent, rank)?.Id, Is.EqualTo(990042));
            Assert.That(AttendGiftReplay.Pick(events, now, GameEventType.StampEvent, rank, quest)?.Id, Is.EqualTo(990041));
            Assert.That(AttendGiftReplay.Pick(events, now, GameEventType.AttendGift, rank, quest), Is.Null);
            // An empty reward slot is not an item.
            Assert.That(AttendGiftReplay.ClientHasRewards(events[0], itemId => itemId == 0 ? 0 : 1), Is.True);
        });
    }

    // A later ingest moves a replayed card again; the stored claims follow the moved end.
    [Test]
    public void StoredClaims_FollowTheMovedEventEnd() {
        var first = new GameEvent(Event(990045, "2030-06-15-08-00", "2030-07-15-08-00", Card));
        var moved = new GameEvent(Event(990045, "2030-06-25-08-00", "2030-07-25-08-00", Card));
        var claimed = new GameEventUserValue(GameEventUserValueType.StampRewardsClaimed, first.EndTime, 990045);
        claimed.SetValue("1,2");
        GameEventUserValue claimedNow = GameEventManager.FollowEventEnd(claimed, moved);
        Assert.Multiple(() => {
            Assert.That((claimedNow.Type, claimedNow.Value, claimedNow.ExpirationTime),
                Is.EqualTo((GameEventUserValueType.StampRewardsClaimed, "1,2", moved.EndTime)));
            Assert.That(GameEventManager.FollowEventEnd(claimed, first), Is.SameAs(claimed));
            var other = new GameEventUserValue(GameEventUserValueType.LongTermAttendDays, first.EndTime, 990045);
            Assert.That(GameEventManager.FollowEventEnd(other, moved), Is.SameAs(other));
        });
    }

    // A re-ingest while the card is live moves its end. The claims stored at the old end are still loaded after
    // that old end: they are moved to the new end before the expiry test. A value that does not follow its event
    // is dropped at its own end, and so is any value of an event that is not live.
    [Test]
    public void LoadValue_AfterTheOldEndOfAMovedEvent_KeepsTheClaims() {
        var first = new GameEvent(Event(990045, "2030-06-15-08-00", "2030-07-15-08-00", Card));
        var moved = new GameEvent(Event(990045, "2030-06-25-08-00", "2030-07-25-08-00", Card));
        long afterOldEnd = first.EndTime + 3600;
        var claimed = new GameEventUserValue(GameEventUserValueType.StampRewardsClaimed, first.EndTime, 990045);
        claimed.SetValue("1,2");
        var daily = new GameEventUserValue(GameEventUserValueType.AttendanceAccumulatedTime, first.EndTime, 990045);
        Assert.Multiple(() => {
            GameEventUserValue? loaded = GameEventManager.LoadValue(claimed, [moved], afterOldEnd);
            Assert.That((loaded?.Value, loaded?.ExpirationTime), Is.EqualTo(("1,2", moved.EndTime)));
            Assert.That(GameEventManager.LoadValue(claimed, [first], afterOldEnd), Is.Null, "the end did not move");
            Assert.That(GameEventManager.LoadValue(daily, [moved], afterOldEnd), Is.Null, "a value with its own end");
            Assert.That(GameEventManager.LoadValue(claimed, [], first.EndTime - 10), Is.Null, "no live event");
            Assert.That(GameEventManager.LoadValue(claimed, [moved], moved.EndTime + 1), Is.Null, "after the new end");
        });
    }

    // The run of an event: a moved end while the kept end is not over is the same run and keeps everything; an
    // event live again after the kept end is a new run that starts without the old values, claims and quests.
    [Test]
    public void BeginEventRun_ReplayAfterTheEnd_DropsValuesClaimsAndStampQuests() {
        var test = new TestGameSession();
        const int eventId = 990046;
        const long end1 = 2000000000, end2 = 2000500000, end3 = 2100000000;
        int[] quests = [880001, 880002];
        void Fill() {
            using var context = new Maple2.Database.Context.Ms2Context(test.Options);
            context.GameEventUserValue.Add(new Maple2.Database.Model.GameEventUserValue {
                CharacterId = test.CharacterId, Type = GameEventUserValueType.LongTermAttendDays, EventId = eventId, Value = "3", ExpirationTime = end1,
            });
            context.GameEventUserValue.Add(new Maple2.Database.Model.GameEventUserValue {
                CharacterId = test.CharacterId, Type = GameEventUserValueType.LongTermAttendDays, EventId = 990047, Value = "7", ExpirationTime = end1,
            });
            context.AccountEventValue.Add(new Maple2.Database.Model.AccountEventValue {
                AccountId = test.AccountId, Type = GameEventUserValueType.StampRewardsClaimed, EventId = eventId, Value = "1,2", ExpirationTime = end1,
            });
            context.Quest.Add(new Maple2.Database.Model.Quest { OwnerId = test.AccountId, Id = 880001, State = QuestState.Completed });
            context.Quest.Add(new Maple2.Database.Model.Quest { OwnerId = test.AccountId, Id = 880009, State = QuestState.Completed });
            context.SaveChanges();
        }
        (int Values, int Claims, int[] Quests) Stored() {
            using var context = new Maple2.Database.Context.Ms2Context(test.Options);
            return (context.GameEventUserValue.Count(value => value.EventId == eventId), context.AccountEventValue.Count(value => value.EventId == eventId),
                context.Quest.Where(quest => quest.OwnerId == test.AccountId).Select(quest => quest.Id).OrderBy(id => id).ToArray());
        }

        Fill();
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.BeginEventRun(eventId, end1, end1 - 1000, quests), Is.False, "first run");
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.BeginEventRun(eventId, end1, end1 - 500, quests), Is.False, "same end");
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.BeginEventRun(eventId, end2, end1 - 100, quests), Is.False, "moved while live");
        }
        Assert.That(Stored(), Is.EqualTo((1, 1, new[] { 880001, 880009 })));
        // The old end is over, but the kept end is the moved one: still the same run.
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.BeginEventRun(eventId, end2, end1 + 100, quests), Is.False);
            Assert.That(db.BeginEventRun(eventId, end2 - 5, end2 + 100, quests), Is.False, "an event that is over is left alone");
        }
        Assert.That(Stored(), Is.EqualTo((1, 1, new[] { 880001, 880009 })));
        // Replayed after its end: a new run.
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.BeginEventRun(eventId, end3, end2 + 100, quests), Is.True);
        }
        Assert.That(Stored(), Is.EqualTo((0, 0, new[] { 880009 })));
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            Assert.That(context.GameEventUserValue.Count(value => value.EventId == 990047), Is.EqualTo(1), "another event's value stays");
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.BeginEventRun(eventId, end3, end2 + 200, quests), Is.False);
        }
    }

    // The claims are stored for the account: another character of the account reads them, another account does
    // not, and they stay when the character that claimed is deleted.
    [Test]
    public void AccountClaims_AreTheAccounts_AndOutliveTheCharacter() {
        var test = new TestGameSession();
        var stranger = new TestGameSession(test.Options);
        var claimed = new GameEventUserValue(GameEventUserValueType.StampRewardsClaimed, 2000000000, 990046);
        claimed.SetValue("1");
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveAccountEventValues(test.AccountId, [claimed]), Is.True);
        }
        claimed.SetValue("1,2");
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveAccountEventValues(test.AccountId, [claimed]), Is.True);
        }
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            context.Character.Remove(context.Character.Single(character => character.Id == test.CharacterId));
            context.SaveChanges();
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.Multiple(() => {
                Assert.That(db.GetAccountEventValues(test.AccountId).Select(value => (value.Type, value.EventId, value.Value)),
                    Is.EqualTo(new[] { (GameEventUserValueType.StampRewardsClaimed, 990046, "1,2") }));
                Assert.That(db.GetAccountEventValues(stranger.AccountId), Is.Empty);
                Assert.That(GameEventManager.AccountValue(GameEventUserValueType.StampCompleteRewardClaimed), Is.True);
                Assert.That(GameEventManager.AccountValue(GameEventUserValueType.LongTermAttendDays), Is.False);
            });
            Assert.That(db.RemoveAccountEventValue(claimed, test.AccountId), Is.True);
            Assert.That(db.GetAccountEventValues(test.AccountId), Is.Empty);
        }
    }

    // A started stamp quest with nothing left to count (its completion failed before, or it had no condition) is
    // completed by the retry; a quest that is not a live card's is left alone.
    [Test]
    public void CompleteStampQuests_CompletesAStartedStampQuestThatIsDue() {
        var test = new TestGameSession();
        Maple2.Server.Game.Session.GameSession session = test.Start();
        var server = typeof(Maple2.Server.Game.Session.GameSession).GetField("server", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
        var cache = (System.Collections.IDictionary) server.GetType().GetField("eventCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(server)!;
        cache.Add(990048, new GameEvent(new GameEventMetadata(990048, GameEventType.StampEvent, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(5), TimeSpan.Zero,
            TimeSpan.Zero, [], Card, "", "", "", "")));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        Quest stamp = test.StartQuest(TestGameSession.QuestData(880001));
        Quest other = test.StartQuest(TestGameSession.QuestData(880777));

        session.Quest.CompleteStampQuests();

        Assert.Multiple(() => {
            Assert.That(stamp.State, Is.EqualTo(QuestState.Completed));
            Assert.That(other.State, Is.EqualTo(QuestState.Started));
        });
    }

    // An event taken out before its end (a replay switched off) and put back before that end is the same run: a
    // login in between does not read the claims and does not delete them; only a value that has run out goes.
    [Test]
    public void Login_WhileTheEventIsAbsentBeforeItsEnd_KeepsTheClaims() {
        var test = new TestGameSession();
        Maple2.Server.Game.Session.GameSession session = test.Start();
        const int eventId = 990049;
        long end = DateTimeOffset.Now.AddDays(5).ToUnixTimeSeconds();
        long past = DateTimeOffset.Now.AddDays(-1).ToUnixTimeSeconds();
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            context.AccountEventValue.Add(new Maple2.Database.Model.AccountEventValue {
                AccountId = test.AccountId, Type = GameEventUserValueType.StampRewardsClaimed, EventId = eventId, Value = "1,2", ExpirationTime = end,
            });
            context.AccountEventValue.Add(new Maple2.Database.Model.AccountEventValue {
                AccountId = test.AccountId, Type = GameEventUserValueType.StampRewardsClaimed, EventId = 990050, Value = "1", ExpirationTime = past,
            });
            context.GameEventUserValue.Add(new Maple2.Database.Model.GameEventUserValue {
                CharacterId = test.CharacterId, Type = GameEventUserValueType.LongTermAttendDays, EventId = eventId, Value = "3", ExpirationTime = end,
            });
            context.GameEventUserValue.Add(new Maple2.Database.Model.GameEventUserValue {
                CharacterId = test.CharacterId, Type = GameEventUserValueType.LongTermAttendDays, EventId = 990050, Value = "4", ExpirationTime = past,
            });
            context.SaveChanges();
        }
        (int[] Claims, int[] Values) Stored() {
            using var context = new Maple2.Database.Context.Ms2Context(test.Options);
            return (context.AccountEventValue.Where(value => value.AccountId == test.AccountId).Select(value => value.EventId).ToArray(),
                context.GameEventUserValue.Where(value => value.CharacterId == test.CharacterId).Select(value => value.EventId).ToArray());
        }

        var absent = new GameEventManager(session);
        Assert.Multiple(() => {
            Assert.That(absent.Peek(GameEventUserValueType.StampRewardsClaimed, eventId), Is.Null.Or.Empty, "not read while absent");
            Assert.That(Stored(), Is.EqualTo((new[] { eventId }, new[] { eventId })), "kept until their end; the ones that ran out are deleted");
        });

        var server = typeof(Maple2.Server.Game.Session.GameSession).GetField("server", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
        var cache = (System.Collections.IDictionary) server.GetType().GetField("eventCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(server)!;
        cache.Add(eventId, new GameEvent(new GameEventMetadata(eventId, GameEventType.StampEvent, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(9), TimeSpan.Zero,
            TimeSpan.Zero, [], Card, "", "", "", "")));
        var back = new GameEventManager(session);
        Assert.Multiple(() => {
            Assert.That(back.Peek(GameEventUserValueType.StampRewardsClaimed, eventId), Is.EqualTo("1,2"), "the same run: the claims are back");
            Assert.That(Stored(), Is.EqualTo((new[] { eventId }, new[] { eventId })));
        });
    }

    [Test]
    public void Outdated_OnlyAfterTheValuesEnd() {
        var live = new GameEvent(Event(990045, "2030-06-25-08-00", "2030-07-25-08-00", Card));
        long oldEnd = live.EndTime - 86400;
        var claimed = new GameEventUserValue(GameEventUserValueType.StampRewardsClaimed, oldEnd, 990045);
        Assert.Multiple(() => {
            Assert.That(GameEventManager.Outdated(claimed, [], oldEnd - 10), Is.False, "absent before the end");
            Assert.That(GameEventManager.Outdated(claimed, [], oldEnd + 10), Is.True, "absent after the end");
            Assert.That(GameEventManager.Outdated(claimed, [live], oldEnd + 10), Is.False, "follows the moved end");
            Assert.That(GameEventManager.Outdated(claimed, [live], live.EndTime + 10), Is.True);
        });
    }

    // A new run deletes only the card's own stamp quests: not a quest that is no stamp event mission, and not a
    // quest another card has as a stamp too.
    [Test]
    public void RunQuests_OnlyTheCardsOwnStampQuests() {
        var other = new StampEvent("othercard", 5, [new StampEvent.Stamp(880002, false, 0, 0), new StampEvent.Stamp(880010, false, 0, 0)], [], Card.CompleteReward);
        var twice = Card with { Stamps = [.. Card.Stamps, new StampEvent.Stamp(880001, true, 0, 0)] };
        Assert.Multiple(() => {
            Assert.That(StampEventRules.RunQuests(Card, [], _ => true), Is.EqualTo(new[] { 880001, 880002, 880003 }));
            Assert.That(StampEventRules.RunQuests(Card, [other], _ => true), Is.EqualTo(new[] { 880001, 880003 }), "880002 is the other card's too");
            Assert.That(StampEventRules.RunQuests(Card, [], questId => questId != 880003), Is.EqualTo(new[] { 880001, 880002 }), "no stamp event mission");
            Assert.That(StampEventRules.RunQuests(twice, [], _ => true), Is.EqualTo(new[] { 880001, 880002, 880003 }));
            Assert.That(StampEventRules.RunQuests(Card, [], _ => false), Is.Empty);
        });

        // The delete takes the ids it is given for every owner, and no other quest.
        var test = new TestGameSession();
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            foreach (int questId in new[] { 880001, 880002, 880003, 880010 }) {
                context.Quest.Add(new Maple2.Database.Model.Quest { OwnerId = test.AccountId, Id = questId, State = QuestState.Completed });
            }
            context.Quest.Add(new Maple2.Database.Model.Quest { OwnerId = test.CharacterId + 77, Id = 880001, State = QuestState.Completed });
            context.SaveChanges();
        }
        int[] quests = StampEventRules.RunQuests(Card, [other], questId => questId != 880003);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.BeginEventRun(990051, 2000000000, 1999990000, quests), Is.False);
            Assert.That(db.BeginEventRun(990051, 2100000000, 2000000100, quests), Is.True);
        }
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            Assert.That(context.Quest.Select(quest => quest.Id).OrderBy(id => id).ToArray(), Is.EqualTo(new[] { 880002, 880003, 880010 }));
        }
    }

    private static GameEventMetadata Event(int id, string start, string end, StampEvent card) {
        return new GameEventMetadata(id, GameEventType.StampEvent, AttendGiftReplay.ParseTime(start), AttendGiftReplay.ParseTime(end), TimeSpan.Zero,
            TimeSpan.Zero, [], card, "", "", "", "");
    }
}

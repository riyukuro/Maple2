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

    // The claims of the account's characters are put together.
    [Test]
    public void MergeClaimed_UnionInFirstOrder() {
        Assert.Multiple(() => {
            Assert.That(StampEventRules.MergeClaimed(["1,3", null, "", "3,2", "x"]), Is.EqualTo("1,3,2"));
            Assert.That(StampEventRules.MergeClaimed([]), Is.EqualTo(""));
            Assert.That(StampEventRules.Quests([Card, Card with { Stamps = [new StampEvent.Stamp(880009, false, 0, 0)] }]),
                Is.EquivalentTo(new[] { 880001, 880002, 880003, 880009 }));
        });
    }

    // Several live cards: the one without a key, else the first.
    [Test]
    public void PickEvent_TheCardWithoutAKeyElseTheFirst() {
        StampEvent keyed = Card with { Key = 10001 };
        StampEvent other = Card with { Key = 10002 };
        Assert.Multiple(() => {
            Assert.That(StampEventRules.PickEvent<StampEvent>([keyed, Card, other], card => card), Is.SameAs(Card));
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

    // The claims of the account's other characters, for the load-time merge: not this character's, not another
    // account's, not another event's or type's.
    [Test]
    public void Storage_ReadsTheAccountsOtherCharactersClaims() {
        var test = new TestGameSession();
        var stranger = new TestGameSession(test.Options);
        using GameStorage.Request db = test.Storage.Context();
        Character second = db.CreateCharacter(new Character {
            AccountId = test.AccountId, Name = Guid.NewGuid().ToString("N")[..12], Gender = Gender.Male, Job = Job.Newbie, Mastery = new Mastery(),
            MapId = 2000023, ReturnMaps = new Maple2.Tools.LimitedStack<int>(3, 2000023),
        })!;
        // The rows are added directly: the storage's save groups in SQL, which the in-memory provider lacks.
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            void Add(long characterId, GameEventUserValueType type, int eventId, string text) {
                context.GameEventUserValue.Add(new Maple2.Database.Model.GameEventUserValue {
                    CharacterId = characterId, Type = type, EventId = eventId, Value = text, ExpirationTime = 2000000000,
                });
            }
            Add(test.CharacterId, GameEventUserValueType.StampRewardsClaimed, 990046, "1");
            Add(second.Id, GameEventUserValueType.StampRewardsClaimed, 990046, "2");
            Add(second.Id, GameEventUserValueType.StampCompleteRewardClaimed, 990046, "1");
            Add(second.Id, GameEventUserValueType.LongTermAttendDays, 990046, "9");
            Add(second.Id, GameEventUserValueType.StampRewardsClaimed, 990047, "5");
            Add(stranger.CharacterId, GameEventUserValueType.StampRewardsClaimed, 990046, "3");
            context.SaveChanges();
        }

        GameEventUserValueType[] types = [GameEventUserValueType.StampCompleteRewardClaimed, GameEventUserValueType.StampRewardsClaimed,
            GameEventUserValueType.StampSpecialRewardsClaimed];
        IList<GameEventUserValue> others = db.GetAccountEventUserValues(test.AccountId, test.CharacterId, 990046, types);
        Assert.That(others.Select(value => (value.Type, value.Value)), Is.EquivalentTo(new[] {
            (GameEventUserValueType.StampRewardsClaimed, "2"), (GameEventUserValueType.StampCompleteRewardClaimed, "1"),
        }));
    }

    private static GameEventMetadata Event(int id, string start, string end, StampEvent card) {
        return new GameEventMetadata(id, GameEventType.StampEvent, AttendGiftReplay.ParseTime(start), AttendGiftReplay.ParseTime(end), TimeSpan.Zero,
            TimeSpan.Zero, [], card, "", "", "", "");
    }
}

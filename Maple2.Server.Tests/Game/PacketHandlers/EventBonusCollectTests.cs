using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The dungeon clear bonus (DungeonBonusReward) and the item collection (CollectItemGroup): the row readers, the
// event list entries, the bonus of a clear and the claim of a collection (EventReward 19).
public class EventBonusCollectTests {
    private const int EventId = 7001;
    private const int Room = 90000200;
    private const int RoomBox = 100;
    private const int EventBox = 200;
    private const int ItemA = 20000001;
    private const int ItemB = 20000002;
    private const int Token = 20000003;
    private const int Piece = 11300001;
    private const int Result = 20000005;

    private TestGameSession test = null!;
    private GameEvent gameEvent = null!;

    private static string Name(string name) {
        var writer = new ByteWriter();
        writer.WriteUnicodeString(name);
        return Convert.ToHexString(writer.ToArray());
    }

    #region DungeonBonusReward
    private static DungeonBonusReward Bonus(int dungeonId = Room, DungeonGroupType group = DungeonGroupType.none, float meso = 0f,
                                            float exp = 0f, int rolls = 0, params int[] boxIds) {
        return new DungeonBonusReward(dungeonId, group, meso, exp, rolls, boxIds);
    }

    [Test]
    public void DungeonBonusReward_WritesTheRoomTheGroupTheRatesTheRollsAndTheBoxes() {
        var session = new TestGameSession();
        GameEvent written = session.AddEvent(EventId, GameEventType.DungeonBonusReward, Bonus(23120001, DungeonGroupType.raid, 1f, 9f, 2, 300, 301));
        var writer = new ByteWriter();
        written.WriteTo(writer);

        Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo(Name("DungeonBonusReward") + "591B0000"
            + "81C86001" + "02" + "0000803F" + "00001041" + "02000000" + "02000000" + "2C010000" + "2D010000"));
    }

    [Test]
    public void DungeonBonusReader_ReadsTheFirstElement() {
        DungeonBonusReward? room = EventXmlReader.DungeonBonus("<ms2><v dungeonID=\"23120001\" itemRolls=\"1\" /><v dungeonID=\"5\" itemRolls=\"7\" /></ms2>");
        Assert.That(room, Is.Not.Null);
        Assert.That((room!.DungeonId, room.GroupType, room.MesoRate, room.ExpRate, room.ItemRolls), Is.EqualTo((23120001, DungeonGroupType.none, 0f, 0f, 1)));
        Assert.That(room.DropBoxIds, Is.Empty);

        DungeonBonusReward? group = EventXmlReader.DungeonBonus("<ms2><x groupType=\"raid\" mesoRate=\"1\" expRate=\"0.5\" customDropBoxIds=\"300, 301\" /></ms2>");
        Assert.That(group, Is.Not.Null);
        Assert.That((group!.DungeonId, group.GroupType, group.MesoRate, group.ExpRate, group.ItemRolls), Is.EqualTo((0, DungeonGroupType.raid, 1f, 0.5f, 0)));
        Assert.That(group.DropBoxIds, Is.EqualTo(new[] { 300, 301 }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("1")]
    [TestCase("<ms2")]
    [TestCase("<ms2/>")]
    [TestCase("<other><v dungeonID=\"1\" itemRolls=\"1\" /></other>")]
    [TestCase("<ms2><v itemRolls=\"1\" /></ms2>")] // neither a room nor a group
    [TestCase("<ms2><v groupType=\"2\" itemRolls=\"1\" /></ms2>")] // the group is a name
    [TestCase("<ms2><v groupType=\"none\" itemRolls=\"1\" /></ms2>")]
    [TestCase("<ms2><v dungeonID=\"1\" /></ms2>")] // adds nothing
    [TestCase("<ms2><v dungeonID=\"1\" itemRolls=\"-1\" expRate=\"-2\" mesoRate=\"x\" /></ms2>")]
    public void DungeonBonusReader_DropsARowThatChangesNothing(string? value1) {
        Assert.That(EventXmlReader.DungeonBonus(value1), Is.Null);
    }

    [Test]
    public void DungeonBonus_MergesTheEventsOfTheRoomAndItsGroup() {
        DungeonBonusReward? bonus = EventSaleRules.DungeonBonus([
            Bonus(Room, rolls: 1, boxIds: [300]),
            Bonus(0, DungeonGroupType.raid, meso: 1f, exp: 0.5f, rolls: 2, boxIds: [301]),
            Bonus(Room, DungeonGroupType.raid, exp: 1f), // both the room and the group: once
            Bonus(Room + 1, DungeonGroupType.normal, meso: 5f, exp: 5f, rolls: 5, boxIds: [302]),
        ], Room, DungeonGroupType.raid);

        Assert.That(bonus, Is.Not.Null);
        Assert.That((bonus!.MesoRate, bonus.ExpRate, bonus.ItemRolls), Is.EqualTo((1f, 1.5f, 3)));
        Assert.That(bonus.DropBoxIds, Is.EqualTo(new[] { 300, 301 }));

        Assert.That(EventSaleRules.DungeonBonus([Bonus(Room + 1, DungeonGroupType.raid, rolls: 1)], Room, DungeonGroupType.normal), Is.Null);
        // A room without a group is not in group none's events.
        Assert.That(EventSaleRules.DungeonBonus([Bonus(0, DungeonGroupType.none, rolls: 1)], Room, DungeonGroupType.none), Is.Null);
        Assert.That(EventSaleRules.DungeonBonus([Bonus(rolls: 8), Bonus(rolls: 8)], Room, DungeonGroupType.none)!.ItemRolls,
            Is.EqualTo(EventSaleRules.MaxDungeonBonusRolls));
    }

    [TestCase(1000, 1f, 1000)]
    [TestCase(1000, 9f, 9000)]
    [TestCase(999, 0.5f, 499)]
    [TestCase(10, 0.1f, 1)] // 1.0000000149 + 0.001
    [TestCase(1000, 0f, 0)]
    [TestCase(1000, -1f, 0)]
    [TestCase(0, 1f, 0)]
    [TestCase(-5, 1f, 0)]
    [TestCase(2000000000, 9f, int.MaxValue)]
    public void DungeonBonusAmount_IsTheClientsRounding(long amount, float rate, int expected) {
        Assert.That(EventSaleRules.DungeonBonusAmount(amount, rate), Is.EqualTo(expected));
    }

    // A room whose clear gives 1000 exp, 500 mesos and box 100 (ItemA x1).
    private GameSession StartInRoom(DungeonGroupType group, int rewardCount, params (DungeonBonusReward Data, bool Running)[] events) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 90000201, rewardCount) with { GroupType = group };
        room = room with { Reward = room.Reward with { Exp = 1000, Meso = 500, UnlimitedDropBoxIds = [RoomBox] } };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.Register(TestGameSession.Metadata(ItemA), TestGameSession.Metadata(ItemB));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [RoomBox] = TestGameSession.DropBox([(ItemA, 1)]),
            [EventBox] = TestGameSession.DropBox([(ItemB, 4)]),
        }));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        for (int i = 0; i < events.Length; i++) {
            if (events[i].Running) {
                test.AddEvent(EventId + i, GameEventType.DungeonBonusReward, events[i].Data);
            } else {
                test.AddEvent(EventId + i, GameEventType.DungeonBonusReward, events[i].Data, DateTime.Now.AddDays(-9), DateTime.Now.AddDays(-2));
            }
        }
        GameSession session = test.Start();
        test.EnterDungeon(room).State = DungeonState.Clear;
        test.TakeSent();
        return session;
    }

    private static int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    private static (int Exp, int Meso, long Wallet, int ItemA, int ItemB, bool EventLine) Clear(GameSession session) {
        long before = session.Currency.Meso;
        session.Dungeon.CompleteDungeon(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return (session.Dungeon.UserRecord!.Rewards[DungeonRewardType.Exp], session.Dungeon.UserRecord.Rewards[DungeonRewardType.Meso],
            session.Currency.Meso - before, Held(session, ItemA), Held(session, ItemB), session.Dungeon.UserRecord.Flag.HasFlag(DungeonBonusFlag.Event));
    }

    [Test]
    public void Clear_WithoutAnEvent_GivesTheRoomsReward() {
        GameSession session = StartInRoom(DungeonGroupType.raid, 10,
            (Bonus(Room, exp: 9f, rolls: 3), false), // ended
            (Bonus(Room + 1, DungeonGroupType.normal, exp: 9f, rolls: 3), true)); // another room and group

        Assert.That(Clear(session), Is.EqualTo((1000, 500, 500L, 1, 0, false)));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Clear_WithEvents_AddsTheRatesTheRollsAndTheBoxes() {
        GameSession session = StartInRoom(DungeonGroupType.raid, 10,
            (Bonus(Room, exp: 9f, rolls: 1), true),
            (Bonus(0, DungeonGroupType.raid, meso: 1f, rolls: 1, boxIds: [EventBox]), true));

        // 1000 + 9000 exp, 500 + 500 mesos, the room's box three times, the event's box once.
        Assert.That(Clear(session), Is.EqualTo((10000, 1000, 1000L, 3, 4, true)));
        Assert.That(session.Dungeon.UserRecord!.RewardItems.Select(item => (item.ItemId, item.Amount)),
            Is.EquivalentTo(new[] { (ItemA, 1), (ItemA, 1), (ItemA, 1), (ItemB, 4) }));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Clear_WithoutARewardCountLeft_GivesNoBonus() {
        GameSession session = StartInRoom(DungeonGroupType.raid, 0, (Bonus(Room, meso: 1f, exp: 9f, rolls: 1, boxIds: [EventBox]), true));

        Assert.That(Clear(session), Is.EqualTo((0, 0, 0L, 0, 0, false)));
    }
    #endregion

    #region CollectItemGroup
    private static CollectItemGroup Collect(params RewardItem[] conditions) {
        return new CollectItemGroup("s_collect_title", conditions.Length > 0 ? conditions : [new RewardItem(Piece, 2, 1), new RewardItem(Token, 1, 5)],
            new RewardItem(Result, 3, 1));
    }

    [Test]
    public void CollectItemGroup_WritesTheIdTheNameTheTimesTheConditionsAndTheResult() {
        var session = new TestGameSession();
        GameEvent written = session.AddEvent(EventId, GameEventType.CollectItemGroup, Collect());
        var writer = new ByteWriter();
        written.WriteTo(writer);
        var times = new ByteWriter();
        times.WriteLong(written.StartTime);
        times.WriteLong(written.EndTime);

        Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo(Name("CollectItemGroup") + "591B0000"
            + "591B0000" + Name("s_collect_title") + Convert.ToHexString(times.ToArray()) + "02000000"
            + "A16CAC00" + "0200" + "01000000" + "00000000"
            + "032D3101" + "0100" + "05000000" + "00000000"
            + "052D3101" + "0300" + "01000000" + "00000000"));
    }

    [Test]
    public void CollectReader_ReadsTheNameTheResultAndTheConditions() {
        CollectItemGroup? collect = EventXmlReader.CollectItemGroup("<ms2><info name=\"s_collect_title\" /><result itemID=\"20000005\" count=\"1\" grade=\"3\" />"
            + "<condition itemID=\"11300001\" count=\"1\" grade=\"2\" /><other itemID=\"1\" count=\"1\" /><condition itemID=\"20000003\" count=\"5\" grade=\"1\" /></ms2>");

        Assert.That(collect, Is.Not.Null);
        Assert.That(collect!.Name, Is.EqualTo("s_collect_title"));
        Assert.That(collect.Result, Is.EqualTo(new RewardItem(Result, 3, 1)));
        Assert.That(collect.Conditions, Is.EqualTo(new[] { new RewardItem(Piece, 2, 1), new RewardItem(Token, 1, 5) }));
    }

    // A row without a grade has the client's default rank 1, the rank the bag is searched with.
    [Test]
    public void CollectReader_ReadsAnItemWithoutGradeAsRank1() {
        CollectItemGroup? collect = EventXmlReader.CollectItemGroup("<ms2><info name=\"a\" /><result itemID=\"20000005\" count=\"1\" />"
            + "<condition itemID=\"11300001\" count=\"1\" /><condition itemID=\"20000003\" count=\"5\" grade=\"x\" /></ms2>");

        Assert.That(collect!.Result, Is.EqualTo(new RewardItem(Result, 1, 1)));
        Assert.That(collect.Conditions, Is.EqualTo(new[] { new RewardItem(Piece, 1, 1), new RewardItem(Token, 1, 5) }));
    }

    private const string Condition = "<condition itemID=\"20000003\" count=\"5\" grade=\"1\" />";

    [TestCase(null)]
    [TestCase("")]
    [TestCase("<ms2")]
    [TestCase("<ms2><result itemID=\"20000005\" count=\"1\" grade=\"3\" />" + Condition + Condition + "</ms2>")] // no info
    [TestCase("<ms2><info name=\"a\" />" + Condition + Condition + "</ms2>")] // no result
    [TestCase("<ms2><info name=\"a\" /><result itemID=\"0\" count=\"1\" grade=\"3\" />" + Condition + Condition + "</ms2>")]
    [TestCase("<ms2><info name=\"a\" /><result itemID=\"20000005\" count=\"1\" grade=\"3\" />" + Condition + "</ms2>")] // 1 condition
    [TestCase("<ms2><info name=\"a\" /><result itemID=\"20000005\" count=\"1\" grade=\"3\" />" + Condition + Condition + Condition + Condition + Condition + Condition + "</ms2>")] // 6
    [TestCase("<ms2><info name=\"a\" /><result itemID=\"20000005\" count=\"1\" grade=\"3\" />" + Condition + "<condition itemID=\"20000003\" count=\"0\" /></ms2>")]
    public void CollectReader_DropsARowTheWindowCannotShow(string? value1) {
        Assert.That(EventXmlReader.CollectItemGroup(value1), Is.Null);
    }

    [Test]
    public void Replay_PassesOverACollectionWithAnItemTheClientLacks() {
        var metadata = new GameEventMetadata(EventId, GameEventType.CollectItemGroup, DateTime.Now.AddDays(-9), DateTime.Now.AddDays(-2),
            TimeSpan.Zero, TimeSpan.Zero, [], Collect(), string.Empty, string.Empty, string.Empty, string.Empty);

        Assert.That(AttendGiftReplay.ClientHasRewards(metadata, _ => 1), Is.True);
        Assert.That(AttendGiftReplay.ClientHasRewards(metadata, id => id == Result ? 0 : 1), Is.False);
        Assert.That(AttendGiftReplay.ClientHasRewards(metadata, id => id == Token ? 0 : 1), Is.False);
    }

    // The piece (rank 2) and the tokens: in the bag, worn or not there.
    private GameSession Login(CollectItemGroup? data = null, int tokens = 5, bool pieceInBag = true, bool pieceWorn = false, bool running = true) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Result));
        ItemMetadata piece = TestGameSession.Metadata(Piece, EquipSlot.CL);
        if (pieceWorn) {
            test.Store(piece, group: ItemGroup.Gear, slot: EquipSlot.CL);
        } else if (pieceInBag) {
            test.Store(piece);
        }
        if (tokens > 0) {
            test.Store(TestGameSession.Metadata(Token), amount: tokens);
        }
        gameEvent = running
            ? test.AddEvent(EventId, GameEventType.CollectItemGroup, data ?? Collect())
            : test.AddEvent(EventId, GameEventType.CollectItemGroup, data ?? Collect(), DateTime.Now.AddDays(-9), DateTime.Now.AddDays(-2));
        GameSession session = test.Start();
        foreach (Item item in session.Item.Inventory.Find(Piece)) {
            item.Rarity = 2;
        }
        foreach (Item item in session.Item.Equips.Gear.Values) {
            item.Rarity = 2;
        }
        test.TakeSent();
        return session;
    }

    private static void Claim(GameSession session, int eventId = EventId) {
        new EventRewardHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(19);
            writer.WriteInt(eventId);
        }));
    }

    private string? Received(GameSession session) => session.GameEvent.Peek(GameEventUserValueType.CollectItemGroup, EventId);

    [Test]
    public void Claim_GivesTheResultOnce_AndKeepsTheConditionItems() {
        GameSession session = Login();

        Claim(session);

        Assert.That(Held(session, Result), Is.EqualTo(1));
        Assert.That(session.Item.Inventory.Find(Result).Single().Rarity, Is.EqualTo(3));
        Assert.That(Held(session, Piece), Is.EqualTo(1));
        Assert.That(Held(session, Token), Is.EqualTo(5));
        Assert.That(Received(session), Is.EqualTo("1"));
        // The client has no reply to the claim: the user value is the last packet.
        List<string> sent = test.TakeSent().Select(Convert.ToHexString).ToList();
        Assert.That(sent[^1], Is.EqualTo(Convert.ToHexString(GameEventUserValuePacket.Update(
            session.GameEvent.Get(GameEventUserValueType.CollectItemGroup, EventId, gameEvent.EndTime)).ToArray())));

        Claim(session);
        Assert.That(Held(session, Result), Is.EqualTo(1));
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Claim_CountsAWornPiece() {
        GameSession session = Login(pieceWorn: true);
        Assert.That(Held(session, Piece), Is.EqualTo(0));

        Claim(session);

        Assert.That(Held(session, Result), Is.EqualTo(1));
        Assert.That(session.Item.Equips.Gear.Values.Select(item => item.Id), Is.EqualTo(new[] { Piece }));
    }

    [Test]
    public void Claim_IsRefused_WithoutAConditionItem() {
        GameSession noPiece = Login(pieceInBag: false);
        Claim(noPiece);
        Assert.That(Held(noPiece, Result), Is.EqualTo(0));
        Assert.That(Received(noPiece), Is.Null.Or.Empty.Or.EqualTo("0"));
        Assert.That(test.TakeSent(), Is.Empty);

        GameSession fewTokens = Login(tokens: 4);
        Claim(fewTokens);
        Assert.That(Held(fewTokens, Result), Is.EqualTo(0));

        // The piece is there, but of another rank than the condition's.
        GameSession otherRank = Login(Collect(new RewardItem(Piece, 1, 1), new RewardItem(Token, 1, 5)));
        Claim(otherRank);
        Assert.That(Held(otherRank, Result), Is.EqualTo(0));
    }

    [Test]
    public void Claim_IsRefused_ForAnEventThatDoesNotRun() {
        GameSession ended = Login(running: false);
        Claim(ended);
        Assert.That(Held(ended, Result), Is.EqualTo(0));

        GameSession other = Login();
        Claim(other, EventId + 1);
        Assert.That(Held(other, Result), Is.EqualTo(0));
        Assert.That(Received(other), Is.Null.Or.Empty.Or.EqualTo("0"));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The claim is stored with the reward, for the account: a crash before logout or another character of the
    // account finds it.
    [Test]
    public void Claim_IsStoredAtOnceForTheAccount() {
        GameSession session = Login();

        Claim(session);

        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetAccountEventValues(test.AccountId).Select(value => (value.Type, value.EventId, value.Value, value.ExpirationTime)),
                Is.EqualTo(new[] { (GameEventUserValueType.CollectItemGroup, EventId, "1", gameEvent.EndTime) }));
        }
        Assert.That(test.StoredEventValues(EventId), Is.Empty);
        Assert.That(GameEventManager.AccountValue(GameEventUserValueType.CollectItemGroup), Is.True);
        Assert.That(test.Errors, Is.Empty);
    }

    // A later ingest moves a replayed event again. The claim stored at the first end follows the moved end: after
    // the first end, inside the moved run, it is still loaded, so the result is not given a second time.
    [Test]
    public void StoredClaim_FollowsTheMovedEventEnd() {
        DateTime now = DateTime.Now;
        var first = new GameEvent(new GameEventMetadata(EventId, GameEventType.CollectItemGroup, now.AddDays(-30), now.AddDays(-2), TimeSpan.Zero,
            TimeSpan.Zero, [], Collect(), string.Empty, string.Empty, string.Empty, string.Empty));
        var moved = new GameEvent(new GameEventMetadata(EventId, GameEventType.CollectItemGroup, now.AddDays(-20), now.AddDays(8), TimeSpan.Zero,
            TimeSpan.Zero, [], Collect(), string.Empty, string.Empty, string.Empty, string.Empty));
        var claimed = new GameEventUserValue(GameEventUserValueType.CollectItemGroup, first.EndTime, EventId);
        claimed.SetValue("1");

        GameEventUserValue? loaded = GameEventManager.LoadValue(claimed, [moved], now.ToEpochSeconds());

        Assert.That(loaded, Is.Not.Null);
        Assert.That((loaded!.Int(), loaded.ExpirationTime), Is.EqualTo((1, moved.EndTime)));
        Assert.That(GameEventManager.Outdated(claimed, [moved], now.ToEpochSeconds()), Is.False);
        // The same run that was not moved has ended: the value is not loaded.
        Assert.That(GameEventManager.LoadValue(claimed, [first], now.ToEpochSeconds()), Is.Null);
    }

    [Test]
    public void CollectMissing_NamesTheFirstItemNotHeld() {
        CollectItemGroup collect = Collect();
        Assert.That(EventSaleRules.CollectMissing(collect, (_, _) => 5, _ => false), Is.Null);
        Assert.That(EventSaleRules.CollectMissing(collect, (id, _) => id == Token ? 5 : 0, id => id == Piece), Is.Null);
        Assert.That(EventSaleRules.CollectMissing(collect, (id, _) => id == Token ? 5 : 0, _ => false)!.Value.ItemId, Is.EqualTo(Piece));
        // Worn tokens do not count: the count is above 1.
        Assert.That(EventSaleRules.CollectMissing(collect, (id, _) => id == Piece ? 1 : 4, _ => true)!.Value.ItemId, Is.EqualTo(Token));
        // An event the window does not show cannot be claimed.
        Assert.That(EventSaleRules.CollectMissing(Collect(new RewardItem(Token, 1, 1)), (_, _) => 5, _ => true), Is.Not.Null);
    }
    #endregion
}

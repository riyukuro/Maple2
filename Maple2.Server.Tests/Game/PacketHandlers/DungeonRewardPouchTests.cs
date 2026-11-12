using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Database.Extensions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The dungeon reward pouches (item function OpenDungeonItemBox, client @0x140d24e70): the parameters name a dungeon
// room (dungeon * 1000 + index); the client opens the pouch only while that room's reward room has a count left
// (GetBonusRewardCountNow < GetMaxCount), and its confirm box sends a plain item use. The server uses one pouch and
// one reward count and gives the room's clear boxes.
public class DungeonRewardPouchTests {
    private const int Room = 20003001, Union = 1006, BoxId = 900101, PrizeId = 30000001, PouchId = 20300190;

    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;
    private Item pouch = null!;

    private GameSession Start(string parameters = "20003,1", int rewardCount = 3, int amount = 2, bool withRoom = true) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 2000023);
        room = room with { Reward = room.Reward with { UnionRewardId = Union, LimitedDropBoxIds = [BoxId] } };
        var rooms = new Dictionary<int, DungeonRoomMetadata> {
            // A weekly union room (dayOfWeeks 2: the period ends on Monday).
            [Union] = TestGameSession.DungeonRoom(Union, 2000023, rewardCount) with { CooldownValue = 2 },
        };
        if (withRoom) {
            rooms[Room] = room;
        }
        test.SetTable(new DungeonRoomTable(rooms));
        test.Register(TestGameSession.Metadata(PrizeId));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [BoxId] = TestGameSession.DropBox([(PrizeId, 5)]),
        }));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        pouch = test.Store(TestGameSession.Metadata(PouchId,
            function: new ItemMetadataFunction(ItemFunction.OpenDungeonItemBox, "OpenDungeonItemBox", parameters, false), type: 10), amount: amount);
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session) => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteLong(pouch.Uid);
        writer.WriteUnicodeString(string.Empty);
    }));

    private static long Now => DateTime.Now.ToEpochSeconds();

    private static int Prizes(GameSession session) =>
        session.Item.Inventory.Filter(item => item.Id == PrizeId).Sum(item => item.Amount);

    [Test]
    public void Opens_UsesOnePouchAndOneCountOfTheUnionRoom() {
        GameSession session = Start();

        Use(session);

        Assert.That(session.Item.Inventory.Get(pouch.Uid)!.Amount, Is.EqualTo(1));
        Assert.That(Prizes(session), Is.EqualTo(5));
        DungeonRecord record = session.Dungeon.Records[Union];
        Assert.That(record.UnionClears, Is.EqualTo(1));
        Assert.That(record.UnionCooldownTimestamp, Is.GreaterThan(Now));
        Assert.That(session.Dungeon.Records.TryGetValue(Room, out DungeonRecord? own) ? own.UnionClears : 0, Is.EqualTo(0));
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.RoomDungeon));
    }

    // GetBonusRewardCountNow >= GetMaxCount: the client shows s_msg_dungeon_item_open_dungeon_count and sends nothing.
    [Test]
    public void CountUsedUp_IsRefused() {
        GameSession session = Start();
        session.Dungeon.Records[Union] = new DungeonRecord(Union) { UnionClears = 3, UnionCooldownTimestamp = Now + 3600 };

        Use(session);

        Assert.That(session.Item.Inventory.Get(pouch.Uid)!.Amount, Is.EqualTo(2));
        Assert.That(Prizes(session), Is.EqualTo(0));
        Assert.That(session.Dungeon.Records[Union].UnionClears, Is.EqualTo(3));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // A bought extra count (m_bonusCount) raises GetMaxCount while the period lasts.
    [Test]
    public void BoughtCount_LetsItOpen() {
        GameSession session = Start();
        session.Dungeon.Records[Union] = new DungeonRecord(Union) { UnionClears = 3, ExtraSubClears = 1, UnionCooldownTimestamp = Now + 3600 };

        Use(session);

        Assert.That(Prizes(session), Is.EqualTo(5));
        Assert.That(session.Dungeon.Records[Union].UnionClears, Is.EqualTo(4));
    }

    // Once the period is over the counts read 0: the pouch opens and the record starts a new period.
    [Test]
    public void PeriodOver_OpensAgain() {
        GameSession session = Start();
        session.Dungeon.Records[Union] = new DungeonRecord(Union) { UnionClears = 3, ExtraSubClears = 2, UnionCooldownTimestamp = Now - 10 };

        Use(session);

        DungeonRecord record = session.Dungeon.Records[Union];
        Assert.That((record.UnionClears, record.ExtraSubClears), Is.EqualTo(((byte) 1, (byte) 0)));
        Assert.That(record.UnionCooldownTimestamp, Is.GreaterThan(Now));
        Assert.That(Prizes(session), Is.EqualTo(5));
    }

    // GetRoom returns null: the client does nothing. 16 KR pouches name rooms the table does not have.
    [TestCase("20003,1", false)]
    [TestCase("20003,2", true)]
    [TestCase("x,1", true)]
    public void MissingRoom_IsRefused(string parameters, bool withRoom) {
        GameSession session = Start(parameters, withRoom: withRoom);

        Use(session);

        Assert.That(session.Item.Inventory.Get(pouch.Uid)!.Amount, Is.EqualTo(2));
        Assert.That(Prizes(session), Is.EqualTo(0));
        Assert.That(session.Dungeon.Records.Values.Select(record => record.UnionClears), Is.All.EqualTo(0));
    }

    // The last pouch goes from the bag; another use of its uid finds no item.
    [Test]
    public void LastPouch_IsUsedUp() {
        GameSession session = Start(amount: 1);

        Use(session);
        Use(session);

        Assert.That(session.Item.Inventory.Get(pouch.Uid), Is.Null);
        Assert.That(Prizes(session), Is.EqualTo(5));
        Assert.That(session.Dungeon.Records[Union].UnionClears, Is.EqualTo(1));
    }

    [TestCase("20003,1", 20003001)]
    [TestCase(" 21003 , 1 ", 21003001)]
    [TestCase("20003", 20003000)]
    [TestCase("", 0)]
    public void PouchRoomId(string parameters, int expected) {
        Assert.That(DungeonRoomRules.PouchRoomId(parameters), Is.EqualTo(expected));
    }

    [TestCase(0, 0, 100, true)]     // count left
    [TestCase(3, 0, 100, false)]    // used up
    [TestCase(3, 1, 100, true)]     // a bought count
    [TestCase(4, 1, 100, false)]
    [TestCase(3, 1, -100, true)]    // the period is over
    public void PouchHasCountLeft(int clears, int bought, long expiresIn, bool expected) {
        var record = new DungeonRecord(Union) { UnionClears = (byte) clears, ExtraSubClears = (byte) bought, UnionCooldownTimestamp = Now + expiresIn };
        Assert.That(DungeonRoomRules.PouchHasCountLeft(record, 3, Now), Is.EqualTo(expected));
        Assert.That(DungeonRoomRules.PouchHasCountLeft(null, 3, Now), Is.True);
    }
}

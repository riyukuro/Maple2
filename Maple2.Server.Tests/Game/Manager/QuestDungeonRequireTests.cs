using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// require dungeon, unrequireDungeon, enterDungeon and dungeonBeginner (CQuestManagerCommon::CanAcceptQuest,
// questdata.cpp:588-607): the clear count is the record's TotalClears (the client's m_5c), the enter state the one
// FieldEntrancePacket sends. They gate accepting only: a held quest stays held when a clear or the state changes.
public class QuestDungeonRequireTests {
    private const int Main = 10001004;
    private const int Room = 21005001;
    private const int OtherRoom = 21009001;

    private TestGameSession test = null!;
    private GameSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session = test.Session;
    }

    private QuestMetadata Register(QuestMetadataRequire require, int id = Main) {
        QuestMetadata quest = TestGameSession.QuestData(id) with { Require = require };
        test.Register(quest);
        return quest;
    }

    private static QuestMetadataRequire Require() => TestGameSession.QuestData(Main).Require;

    private void Clears(int room, int count, bool veteran = false) {
        session.Dungeon.Records[room] = new DungeonRecord(room) {
            TotalClears = count,
            Flag = veteran ? DungeonRecordFlag.Veteran : DungeonRecordFlag.None,
        };
    }

    // Rooms whose helper reward needs 3 clears.
    private void SetRooms(params int[] ids) {
        var limit = new DungeonRoomLimitMetadata(0, 4, 0, 0, 0, false, [], [], [], false, false, false, false, false, 0);
        var reward = new DungeonRoomRewardMetadata(false, 0, 0, 0, 0, 0, [], [], 0, 0, 0);
        var rooms = new Dictionary<int, DungeonRoomMetadata>();
        foreach (int id in ids) {
            rooms[id] = new DungeonRoomMetadata(id, 0, DungeonPlayType.none, DungeonGroupType.normal, DungeonCooldownType.none, 0, 0, 0, [],
                reward, limit, 0, 0, 3, false, 0, 0, false, [], [], false, default, false);
        }
        test.SetTable(new DungeonRoomTable(rooms));
    }

    [TestCase(null, false)] // no record
    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(5, true)]
    public void Dungeon(int? clears, bool canStart) {
        if (clears != null) {
            Clears(Room, clears.Value);
        }
        Clears(OtherRoom, 3); // another room's clears do not count
        Assert.That(session.Quest.CanStart(Register(Require() with { Dungeon = Room })), Is.EqualTo(canStart));
    }

    [TestCase(null, true)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    public void UnrequireDungeon(int? clears, bool canStart) {
        if (clears != null) {
            Clears(Room, clears.Value);
        }
        Assert.That(session.Quest.CanStart(Register(Require() with { UnrequireDungeon = Room })), Is.EqualTo(canStart));
    }

    [TestCase(null, true)] // a room not sent reads 0
    [TestCase(DungeonEnterLimit.Rookie, true)]
    [TestCase(DungeonEnterLimit.Veteran, true)]
    [TestCase(DungeonEnterLimit.MinLevel, false)]
    [TestCase(DungeonEnterLimit.RecommendedWeapon, false)]
    public void EnterDungeon(DungeonEnterLimit? limit, bool canStart) {
        Dictionary<int, DungeonEnterLimit> limits = session.Player.Value.Character.DungeonEnterLimits;
        limits.Remove(Room);
        if (limit != null) {
            limits[Room] = limit.Value;
        }
        Assert.That(session.Quest.CanStart(Register(Require() with { EnterDungeon = Room })), Is.EqualTo(canStart));
    }

    // Refused only when the player can claim the helper reward of every listed room: a veteran's record with at least
    // the room's 3 clears. A room the table lacks, a rookie's record or too few clears reads "cannot claim".
    [TestCase(true, 3, true, 3, false)]
    [TestCase(true, 3, true, 2, true)]
    [TestCase(true, 3, false, 9, true)]
    [TestCase(false, 0, false, 0, true)]
    public void DungeonBeginner(bool veteranA, int clearsA, bool veteranB, int clearsB, bool canStart) {
        SetRooms(Room, OtherRoom);
        Clears(Room, clearsA, veteranA);
        Clears(OtherRoom, clearsB, veteranB);
        Assert.That(session.Quest.CanStart(Register(Require() with { DungeonBeginner = [Room, OtherRoom] })), Is.EqualTo(canStart));
    }

    [Test]
    public void DungeonBeginner_RoomNotInTheTable() {
        SetRooms(Room);
        Clears(Room, 9, true);
        Clears(OtherRoom, 9, true);
        Assert.Multiple(() => {
            Assert.That(session.Quest.CanStart(Register(Require() with { DungeonBeginner = [Room] })), Is.False);
            Assert.That(session.Quest.CanStart(Register(Require() with { DungeonBeginner = [Room, OtherRoom] }, Main + 1)), Is.True);
        });
    }

    // Accepted with no clear of the room; the player then clears it. The quest is no longer offered, but the held one
    // stays in progress and may still be completed.
    [Test]
    public void HeldQuest_StaysAfterAClear() {
        QuestMetadata metadata = Register(Require() with { UnrequireDungeon = Room });
        Assume.That(session.Quest.CanStart(metadata), Is.True);
        test.StartQuest(metadata);

        Clears(Room, 1);
        Assert.Multiple(() => {
            Assert.That(session.Quest.CanStart(metadata), Is.False);
            Assert.That(session.Quest.TryGetQuest(Main, out Quest? quest) && quest.State == QuestState.Started, Is.True);
            Assert.That(session.Quest.TryGetQuest(Main, out Quest? held) && session.Quest.CanComplete(held), Is.True);
        });
    }
}

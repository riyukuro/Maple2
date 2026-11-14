using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// Room 25008001 as the KR row has it (a weekly cooldown, day 6). A Dark Stream room's scripts have no dungeon clear,
// so its run counts against the room's rewardCount when the last round is cleared (DungeonManager.CountDarkStreamRun):
// each run takes a count and sends the room records, and a run with no count left gives no reward and leaves the
// records alone.
public class DarkStreamRunCountTests {
    private const int Room = 25008001;
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    private GameSession Enter(int rewardCount) {
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 2000350, rewardCount) with {
            GroupType = DungeonGroupType.darkStream, CooldownType = DungeonCooldownType.dayOfWeeks, CooldownValue = 6,
        };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        GameSession session = test.Start();
        test.EnterDungeon(room);
        test.TakeSent();
        return session;
    }

    [Test]
    public void EachRun_TakesACount_UntilNoneIsLeft() {
        GameSession session = Enter(rewardCount: 2);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.That(session.Dungeon.CountDarkStreamRun(session.Field, now), Is.True);
        Assert.That(test.TakeSent().Count(packet => TestGameSession.Op(packet) == SendOp.RoomDungeon), Is.EqualTo(2));
        Assert.That(session.Dungeon.CountDarkStreamRun(session.Field, now + 1), Is.True);
        DungeonRecord record = session.Dungeon.Records[Room];
        Assert.That(record.UnionClears, Is.EqualTo(2));
        Assert.That(record.TotalClears, Is.EqualTo(2));
        test.TakeSent();

        Assert.That(session.Dungeon.CountDarkStreamRun(session.Field, now + 2), Is.False);
        Assert.That(record.UnionClears, Is.EqualTo(2));
        Assert.That(record.TotalClears, Is.EqualTo(2));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.RoomDungeon), Is.Empty);
    }

    // Outside a dungeon room (a test field running the scripts) there is no count to take, and the reward is given.
    [Test]
    public void OutsideADungeonRoom_GivesTheReward() {
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata>()));
        GameSession session = test.Start();

        Assert.That(session.Dungeon.CountDarkStreamRun(session.Field, DateTimeOffset.UtcNow.ToUnixTimeSeconds()), Is.True);
        Assert.That(session.Dungeon.Records, Is.Empty);
    }

    // The room entered last stays set after the player leaves it: a run on another field takes none of its counts,
    // even with none left, and the reward is given.
    [Test]
    public void RunOutsideTheEnteredRoom_TakesNoCountOfIt() {
        GameSession session = Enter(rewardCount: 1);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(session.Dungeon.CountDarkStreamRun(session.Field, now), Is.True);
        DungeonRecord record = session.Dungeon.Records[Room];
        test.EnterMap(TestGameSession.Map(2000498));
        test.TakeSent();

        Assert.That(session.Dungeon.CountDarkStreamRun(session.Field, now + 1), Is.True);
        Assert.That(record.UnionClears, Is.EqualTo(1));
        Assert.That(record.TotalClears, Is.EqualTo(1));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.RoomDungeon), Is.Empty);
    }

    // A room field of the player's room (its lobby is the room's lobby) counts as the lobby does.
    [Test]
    public void RunOnARoomFieldOfTheRoom_Counts() {
        GameSession session = Enter(rewardCount: 2);
        var roomField = test.AddRoomField(2000350);

        Assert.That(session.Dungeon.CountDarkStreamRun(roomField, DateTimeOffset.UtcNow.ToUnixTimeSeconds()), Is.True);
        Assert.That(session.Dungeon.Records[Room].UnionClears, Is.EqualTo(1));
    }

    private int StoredTotalClears(GameSession session) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetDungeonRecords(session.CharacterId).TryGetValue(Room, out DungeonRecord? record) ? record.TotalClears : 0;
    }

    private void FailEveryWrite() =>
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State is EntityState.Added or EntityState.Modified);

    // TakeDarkStreamRun: the count taken is in the database before the reward is given, so a crash after the reward
    // cannot give it back; a count not stored gives no reward (the count stays taken in memory).
    [Test]
    public void TakeRun_StoresTheCountBeforeTheReward() {
        GameSession session = Enter(rewardCount: 3);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.That(session.Dungeon.TakeDarkStreamRun(session.Field, now), Is.True);
        Assert.That(StoredTotalClears(session), Is.EqualTo(1));

        FailEveryWrite();
        Assert.That(session.Dungeon.TakeDarkStreamRun(session.Field, now + 1), Is.False);
        Assert.That(session.Dungeon.Records[Room].TotalClears, Is.EqualTo(2));
        test.FailSave = null;
        Assert.That(StoredTotalClears(session), Is.EqualTo(1));
        test.Errors.Clear();
    }

    // A run that takes no count (outside the player's room) has nothing to store: a store that would fail does not
    // withhold its reward.
    [Test]
    public void TakeRun_OutsideTheRoom_PaysWithoutAStore() {
        GameSession session = Enter(rewardCount: 1);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(session.Dungeon.TakeDarkStreamRun(session.Field, now), Is.True);
        test.EnterMap(TestGameSession.Map(2000498));
        test.TakeSent();

        FailEveryWrite();
        Assert.That(session.Dungeon.TakeDarkStreamRun(session.Field, now + 1), Is.True);
        test.FailSave = null;
    }
}

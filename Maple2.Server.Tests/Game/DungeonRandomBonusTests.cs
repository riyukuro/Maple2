using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game;

// dungeon_random_bonus: a clear of a room the matchmaker made (Party 32 or 33) counts for each member; a room the
// party leader made counts for no one. The trophy ids are made up.
public class DungeonRandomBonusTests {
    private const int Room = 90000300;
    private const int AnyRoom = 990001; // no code, as the live rows
    private const int ThisRoom = 990002; // code: this room, as the CN rows
    private const int OtherRoom = 990003; // code: another room

    private TestGameSession test = null!;

    private void Clear(bool matchmade) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 90000301) with { GroupType = DungeonGroupType.normal };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.AddAchievement(
            TestGameSession.Trophy(AnyRoom, ConditionType.dungeon_random_bonus, [1]),
            TestGameSession.Trophy(ThisRoom, ConditionType.dungeon_random_bonus, [1], codes: [Room]),
            TestGameSession.Trophy(OtherRoom, ConditionType.dungeon_random_bonus, [1], codes: [Room + 1]));
        test.Start();
        DungeonRoomRecord record = test.EnterDungeon(room, randomMatch: matchmade);
        record.UserResults[test.CharacterId] = test.Session.Dungeon.UserRecord!;
        test.TakeSent();

        ((DungeonFieldManager) test.Session.Field!).ChangeState(DungeonState.Clear);
    }

    [Test]
    public void AMemberOfAMatchmadeRoomCountsTheClear() {
        Clear(matchmade: true);
        Assert.That((test.TrophyCounter(AnyRoom), test.TrophyCounter(ThisRoom), test.TrophyCounter(OtherRoom)),
            Is.EqualTo(((long?) 1, (long?) 1, (long?) null)));
    }

    [Test]
    public void ALeaderMadeRoomCountsNothing() {
        Clear(matchmade: false);
        Assert.That((test.TrophyCounter(AnyRoom), test.TrophyCounter(ThisRoom), test.TrophyCounter(OtherRoom)),
            Is.EqualTo(((long?) null, (long?) null, (long?) null)));
    }
}

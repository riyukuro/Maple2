using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game;

// The quest condition of a rookie's clear with a veteran, from a real KR row, through DungeonFieldManager.ChangeState.
public class DungeonHelpeeConditionTests {
    private const int Room = 90000300;

    // 89010201: <condition type="dungeon_help_beginner_helpee" value="1" />: a rookie that clears the room with a
    // veteran counts; the veteran does not, nor a rookie that clears with another rookie.
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void Helpee_89010201_ARookiesClearWithAVeteran(bool otherIsVeteran, int counted) {
        var rookie = new TestGameSession();
        var other = new TestGameSession(rookie.Options);
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 90000301) with { GroupType = DungeonGroupType.normal };
        foreach (TestGameSession test in new[] { rookie, other }) {
            test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
            test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
            test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
                new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
            test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
            test.Start();
        }
        Quest Watch(TestGameSession test) => test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [new ConditionMetadata(ConditionType.dungeon_help_beginner_helpee, 1, null, null)],
        });
        Quest rookieQuest = Watch(rookie);
        Quest otherQuest = Watch(other);
        DungeonRoomRecord record = rookie.EnterDungeon(room);
        var field = (DungeonFieldManager) rookie.Session.Field!;
        other.Session.Field = field;
        field.Players[other.Session.Player.ObjectId + 1000] = other.Session.Player;
        other.Session.Dungeon.Metadata = room;
        other.Session.Dungeon.UserRecord = new DungeonUserRecord(room.Id, other.CharacterId);
        if (otherIsVeteran) {
            other.Session.Dungeon.Records[Room] = new DungeonRecord(Room) { Flag = DungeonRecordFlag.Veteran };
        }
        record.UserResults[rookie.CharacterId] = rookie.Session.Dungeon.UserRecord!;
        record.UserResults[other.CharacterId] = other.Session.Dungeon.UserRecord;

        field.ChangeState(DungeonState.Clear);

        Assert.That((rookieQuest.Conditions[0].Counter, otherQuest.Conditions[0].Counter), Is.EqualTo((counted, 0)));
    }
}

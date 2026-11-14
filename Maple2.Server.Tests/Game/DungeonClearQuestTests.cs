using System.Collections.Generic;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game;

// The quest conditions of a room's clear (DungeonFieldManager.ChangeState, DungeonManager.CompleteDungeon), with
// the KR quest rows' conditions as the ingest reads them (ConvertCodes). Each test fails when its report is taken out.
public class DungeonClearQuestTests {
    private const int QuestId = 91000001;

    private TestGameSession test = null!;

    private static ConditionMetadata Condition(ConditionType type, long value, string code) =>
        new(type, value, code.Split(',').ConvertCodes(), new string[0].ConvertCodes());

    // A solo clear of the room, with the quests started before it; a normal room ranks on table 1.
    private Quest[] Clear(int roomId, params ConditionMetadata[] conditions) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(roomId, 90000301) with { GroupType = DungeonGroupType.normal };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [roomId] = room }));
        test.SetTable(new DungeonConfigTable(new Dictionary<int, int>(),
            new Dictionary<int, DungeonMissionRankMetadata> { [1] = new(1, string.Empty, 100, []) }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.Start();
        var quests = new Quest[conditions.Length];
        for (int i = 0; i < conditions.Length; i++) {
            quests[i] = test.StartQuest(TestGameSession.QuestData(QuestId + i) with { Conditions = [conditions[i]] });
        }
        DungeonRoomRecord record = test.EnterDungeon(room);
        record.UserResults[test.CharacterId] = test.Session.Dungeon.UserRecord!;
        test.TakeSent();

        ((DungeonFieldManager) test.Session.Field!).ChangeState(DungeonState.Clear);
        return quests;
    }

    // KR 50001601: dungeon_clear 25004001, value 1, counts the room's clear; 80010310 (23037002) names another room.
    [Test]
    public void DungeonClear_50001601_TheRoomsClear() {
        Quest[] quests = Clear(25004001,
            Condition(ConditionType.dungeon_clear, 1, "25004001"),
            Condition(ConditionType.dungeon_clear, 1, "23037002"));

        Assert.That((quests[0].Conditions[0].Counter, quests[1].Conditions[0].Counter), Is.EqualTo((1, 0)));
    }

    // KR 93000174: dungeon_rank_clear 24091005, value 1, counts a clear of that room, which ranks.
    [Test]
    public void DungeonRankClear_93000174_TheRankedRoomsClear() {
        Quest[] quests = Clear(24091005,
            Condition(ConditionType.dungeon_rank_clear, 1, "24091005"),
            Condition(ConditionType.dungeon_rank_clear, 1, "24091006"));

        Assert.That((quests[0].Conditions[0].Counter, quests[1].Conditions[0].Counter), Is.EqualTo((1, 0)));
    }
}

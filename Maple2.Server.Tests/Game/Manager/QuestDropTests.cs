using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The quest-only global drops (globalDropItemSet constraintsQuest; KR has 39 sets, all in drop box 1): the item drops
// for a player whose started quest still needs it on this map (an item_exist condition below its count), and never
// when no player is given.
public class QuestDropTests {
    private const int DropBoxId = 1;
    private const int GroupId = 60000003;
    private const int QuestItem = 30000043;
    private const int QuestId = 10001270;
    private const int MapId = 2000023;

    private TestGameSession test = null!;
    private GameSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(QuestItem));
        var group = new GlobalDropItemBoxTable.Group(GroupId, 0, 0, [new GlobalDropItemBoxTable.Group.DropCount(1, 100)], false, (MapType) 0,
            (Continent) 0);
        var item = new GlobalDropItemBoxTable.Item(QuestItem, 1, 99, new GlobalDropItemBoxTable.Range<int>(1, 1), 1, 1, [MapId], true);
        test.SetServerTable(new GlobalDropItemBoxTable(
            new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>> { [DropBoxId] = new() { [DropBoxId] = [group] } },
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>> { [GroupId] = [item] }));
        test.Start();
        session = test.Session;
    }

    // The quest that needs 15 of the item from the monsters of the map (or of another map).
    private Quest Start(int map = MapId, int itemId = QuestItem) {
        QuestMetadata data = TestGameSession.QuestData(QuestId);
        return test.StartQuest(data with {
            Conditions = [new ConditionMetadata(ConditionType.item_exist, 15, new ConditionMetadata.Parameters(Integers: [itemId]),
                new ConditionMetadata.Parameters(Integers: [map]))],
        });
    }

    private int[] Drops(GameSession? player) =>
        session.Field!.ItemDrop.GetGlobalDropItems(DropBoxId, 10, player).Select(item => item.Id).ToArray();

    [Test]
    public void Drops_ForThePlayerWhoseQuestNeedsIt() {
        Start();

        Assert.That(Drops(session), Is.EqualTo(new[] { QuestItem }));
    }

    // No player (a trigger or dungeon roll), no such quest, a quest for another item or map, a quest that has its
    // count, or one that is not started: nothing drops.
    [Test]
    public void NoPlayer() {
        Start();

        Assert.That(Drops(null), Is.Empty);
    }

    [Test]
    public void NoQuest() {
        Assert.That(Drops(session), Is.Empty);
    }

    [TestCase(MapId + 1, QuestItem)]
    [TestCase(MapId, QuestItem + 1)]
    public void QuestForAnotherMapOrItem(int map, int itemId) {
        Start(map, itemId);

        Assert.That(Drops(session), Is.Empty);
    }

    [Test]
    public void QuestHasItsCount() {
        Start().Conditions[0].Counter = 15;

        Assert.That(Drops(session), Is.Empty);
    }

    [TestCase(QuestState.Completed)]
    [TestCase(QuestState.None)]
    public void QuestNotStarted(QuestState state) {
        Start().State = state;

        Assert.That(Drops(session), Is.Empty);
    }
}

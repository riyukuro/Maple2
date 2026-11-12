using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The quest-only individual drop rows (individualItemDrop constraintsQuest; KR has 432): the item drops for a player
// whose started quest still needs it on this map (an item_exist condition below its count), and never from a box
// rolled without a player. A plain row in the same group still drops for everyone.
public class IndividualQuestDropTests {
    private const int BoxId = 9000001;
    private const int QuestItem = 30000043;
    private const int PlainItem = 30000044;
    private const int QuestId = 10001270;
    private const int MapId = 2000023;

    private TestGameSession test = null!;
    private GameSession session = null!;

    private static IndividualDropItemTable.Item Row(int id, bool quest) =>
        new([id, 0], false, 0, 0, 0, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], quest);

    // Two groups that always give their one row: the quest row, and a plain row.
    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(QuestItem));
        test.Register(TestGameSession.Metadata(PlainItem));
        IndividualDropItemTable.Entry.DropCount[] always = [new(1, 100)];
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [BoxId] = new Dictionary<int, IndividualDropItemTable.Entry> {
                [1] = new(1, 0, always, 0, false, false, [Row(QuestItem, true)]),
                [2] = new(2, 0, always, 0, false, false, [Row(PlainItem, false)]),
            },
        }));
        test.Start();
        session = test.Session;
    }

    private Quest Start(int map = MapId, int itemId = QuestItem) {
        QuestMetadata data = TestGameSession.QuestData(QuestId);
        return test.StartQuest(data with {
            Conditions = [new ConditionMetadata(ConditionType.item_exist, 15, new ConditionMetadata.Parameters(Integers: [itemId]),
                new ConditionMetadata.Parameters(Integers: [map]))],
        });
    }

    private int[] Drops() => session.Field!.ItemDrop.GetIndividualDropItems(session, 10, BoxId).Select(item => item.Id).Order().ToArray();

    [Test]
    public void TheMapIsTheTestField() {
        Assert.That(session.Field!.Metadata.Id, Is.EqualTo(MapId));
    }

    [Test]
    public void Drops_ForThePlayerWhoseQuestNeedsIt() {
        Start();

        Assert.That(Drops(), Is.EqualTo(new[] { QuestItem, PlainItem }));
    }

    [Test]
    public void NoPlayer_OnlyThePlainRow() {
        Start();

        Assert.That(session.Field!.ItemDrop.GetIndividualDropItems(BoxId).Select(item => item.Id), Is.EqualTo(new[] { PlainItem }));
    }

    [Test]
    public void NoQuest() {
        Assert.That(Drops(), Is.EqualTo(new[] { PlainItem }));
    }

    [TestCase(MapId + 1, QuestItem)]
    [TestCase(MapId, QuestItem + 2)]
    public void QuestForAnotherMapOrItem(int map, int itemId) {
        Start(map, itemId);

        Assert.That(Drops(), Is.EqualTo(new[] { PlainItem }));
    }

    [Test]
    public void QuestHasItsCount() {
        Start().Conditions[0].Counter = 15;

        Assert.That(Drops(), Is.EqualTo(new[] { PlainItem }));
    }

    [TestCase(QuestState.Completed)]
    [TestCase(QuestState.None)]
    public void QuestNotStarted(QuestState state) {
        Start().State = state;

        Assert.That(Drops(), Is.EqualTo(new[] { PlainItem }));
    }
}

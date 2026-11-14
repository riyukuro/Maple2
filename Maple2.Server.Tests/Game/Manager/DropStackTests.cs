using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Manager;

// A dropped count split by the item's slotMax, as the client's drop table does (ItemDropTable::DropGlobal and
// DropIndividualGroup): KR box 20002001 gives two of the gear 13100039 (slotMax 1), two drops of one each; an item
// without a slotMax (meso) drops its whole count at once.
public class DropStackTests {
    private const int Gear = 13100039;
    private const int Potion = 20000017;
    private const int Meso = 90000001;
    private const int BoxId = 20002001;

    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Gear, slotMax: 1), TestGameSession.Metadata(Potion, slotMax: 4),
            TestGameSession.Metadata(Meso, slotMax: 0));
    }

    private static IndividualDropItemTable.Item Row(int id, int count, bool announce = false) =>
        new([id, 0], announce, 0, 0, 100, new IndividualDropItemTable.Range<int>(count, count), [], 0, 0, false, false, false, false, [], false);

    private void Box(params IndividualDropItemTable.Item[] rows) {
        IDictionary<int, IndividualDropItemTable.Entry> groups = rows.Select((row, i) =>
                new IndividualDropItemTable.Entry(i + 1, 0, [new IndividualDropItemTable.Entry.DropCount(1, 100)], 0, false, false, [row]))
            .ToDictionary(group => group.GroupId);
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> { [BoxId] = groups }));
        test.Start();
    }

    [Test]
    public void IndividualDrop_SplitsTheCountBySlotMax() {
        Box(Row(Gear, 2, announce: true), Row(Potion, 10), Row(Meso, 500));
        List<Maple2.Model.Game.Item> items = test.Session.Field!.ItemDrop.GetIndividualDropItems(BoxId).ToList();

        Assert.That(items.Where(item => item.Id == Gear).Select(item => item.Amount), Is.EqualTo(new[] { 1, 1 }));
        Assert.That(items.Where(item => item.Id == Gear).Select(item => item.AnnounceOnOpen), Is.EqualTo(new[] { true, false }));
        Assert.That(items.Where(item => item.Id == Potion).Select(item => item.Amount), Is.EqualTo(new[] { 4, 4, 2 }));
        Assert.That(items.Where(item => item.Id == Meso).Select(item => item.Amount), Is.EqualTo(new[] { 500 }));
    }

    // The row's second id drops beside the first, but only the first id's first drop is announced.
    [Test]
    public void IndividualDrop_AnnouncesOnlyTheFirstId() {
        Box(new IndividualDropItemTable.Item([Gear, Potion], true, 0, 0, 100, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false,
            false, false, false, [], false));
        List<Maple2.Model.Game.Item> items = test.Session.Field!.ItemDrop.GetIndividualDropItems(BoxId).ToList();

        Assert.That(items.Select(item => (item.Id, item.AnnounceOnOpen)), Is.EqualTo(new[] { (Gear, true), (Potion, false) }));
    }

    [Test]
    public void GlobalDrop_SplitsTheCountBySlotMax() {
        var group = new GlobalDropItemBoxTable.Group(1, 0, 0, [new GlobalDropItemBoxTable.Group.DropCount(1, 100)], false, (MapType) 0, (Continent) 0);
        test.SetServerTable(new GlobalDropItemBoxTable(
            new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>> { [1] = new() { [1] = [group] } },
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>> {
                [1] = [new GlobalDropItemBoxTable.Item(Gear, 1, 99, new GlobalDropItemBoxTable.Range<int>(3, 3), 1, 1, [], false)],
            }));
        test.Start();

        Assert.That(test.Session.Field!.ItemDrop.GetGlobalDropItems(1, 39).Select(item => item.Amount), Is.EqualTo(new[] { 1, 1, 1 }));
    }
}

using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// A smart drop group (smartDropRate 100) whose rows carry only a proper job weight gives an opener only the rows for
// their job: another job's row has no weight and never drops. KR box 330002088 group 7: Knight 14150236 and Priest
// 14050227, weight and properJobWeight 10000 each.
public class SmartJobDropTests {
    private const int BoxId = 330002088;
    private const int KnightItem = 14150236;
    private const int PriestItem = 14050227;

    private static IndividualDropItemTable.Item Row(int itemId) =>
        new([itemId], false, 10000, 0, 10000, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, true, false, false, false, [], false);

    private static HashSet<int> Open(Job job) {
        var test = new TestGameSession();
        foreach ((int id, JobCode code) in new[] { (KnightItem, JobCode.Knight), (PriestItem, JobCode.Priest) }) {
            ItemMetadata data = TestGameSession.Metadata(id);
            test.Register(data with { Limit = data.Limit with { JobRecommends = [code] } });
        }
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [BoxId] = new Dictionary<int, IndividualDropItemTable.Entry> {
                [7] = new(7, 100, [new IndividualDropItemTable.Entry.DropCount(1, 100)], 0, false, false, [Row(KnightItem), Row(PriestItem)]),
            },
        }));
        GameSession session = test.Start();
        session.Player.Value.Character.Job = job;

        var got = new HashSet<int>();
        for (int i = 0; i < 40; i++) {
            got.UnionWith(session.Field!.ItemDrop.GetIndividualDropItems(session, 1, BoxId).Select(item => item.Id));
        }
        return got;
    }

    [Test]
    public void Knight_GetsTheKnightRowOnly() => Assert.That(Open(Job.Knight), Is.EquivalentTo(new[] { KnightItem }));

    [Test]
    public void Priest_GetsThePriestRowOnly() => Assert.That(Open(Job.Priest), Is.EquivalentTo(new[] { PriestItem }));

    // No row has a weight for an Archer: the group gives nothing rather than another job's gear.
    [Test]
    public void Archer_GetsNothing() => Assert.That(Open(Job.Archer), Is.Empty);
}

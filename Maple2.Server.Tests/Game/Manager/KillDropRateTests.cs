using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using NUnit.Framework;
using DropCount = Maple2.Model.Metadata.GlobalDropItemBoxTable.Group.DropCount;

namespace Maple2.Server.Tests.Game.Manager;

// The npc kill drop rate (SpecialAttribute.DropRate, KR gear options npckilldropitemincrate) as the drop rate of a
// kill's global boxes: ItemDropGlobalBox::GetDropCount rescales the box's drop count weights by it
// (ItemDropPicker::Rescale), each count's weight but count 0's scaled and truncated, count 0 taking the difference.
public class KillDropRateTests {
    private const int Gear = 13100039;

    [Test]
    public void DropCountWeights_RescaleAllButCountZero() {
        DropCount[] half = [new(0, 50), new(1, 50)];
        // At 1 the weights are as read; a weight of 0 is left out.
        Assert.That(ItemDropManager.DropCountWeights([new(0, 50), new(1, 50), new(2, 0)], 1f), Is.EqualTo(new[] { (0, 50), (1, 50) }));
        // 1.5: count 1 gets 75, count 0 what is left of the 100 (registered last, as the client does).
        Assert.That(ItemDropManager.DropCountWeights(half, 1.5f), Is.EqualTo(new[] { (1, 75), (0, 25) }));
        // 0.5: count 1 gives up 25 to count 0.
        Assert.That(ItemDropManager.DropCountWeights(half, 0.5f), Is.EqualTo(new[] { (1, 25), (0, 75) }));
        // 3: count 1 gains more than count 0 had, so count 0 is gone and the total grows to 150.
        Assert.That(ItemDropManager.DropCountWeights(half, 3f), Is.EqualTo(new[] { (1, 150) }));
        // A box without count 0 keeps its shares; the weights truncate (7 * 1.02 = 7.14 -> 7).
        Assert.That(ItemDropManager.DropCountWeights([new(1, 10), new(2, 10)], 2f), Is.EqualTo(new[] { (1, 20), (2, 20) }));
        Assert.That(ItemDropManager.DropCountWeights([new(0, 90), new(1, 7), new(2, 3)], 1.02f), Is.EqualTo(new[] { (1, 7), (2, 3), (0, 90) }));

    }

    // The client's precomputed keys 150, 200 and 300: a rate whose (int) (rate * 100) is one of them rolls at that key's
    // rate (1.505 as 1.5, 2.009 as 2); another rescales at the rate itself (1.25: 1250).
    [Test]
    public void DropCountWeights_PrecomputedKeys() {
        DropCount[] even = [new(0, 1000), new(1, 1000)];
        Assert.That(ItemDropManager.DropCountWeights(even, 1.505f), Is.EqualTo(new[] { (1, 1500), (0, 500) }));
        Assert.That(ItemDropManager.DropCountWeights(even, 2.009f), Is.EqualTo(new[] { (1, 2000) }));
        Assert.That(ItemDropManager.DropCountWeights(even, 3.005f), Is.EqualTo(new[] { (1, 3000) }));
        Assert.That(ItemDropManager.DropCountWeights(even, 1.25f), Is.EqualTo(new[] { (1, 1250), (0, 750) }));
    }

    [Test]
    public void GlobalDrop_RollsAtTheRate() {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Gear, slotMax: 1));
        var group = new GlobalDropItemBoxTable.Group(1, 0, 0, [new DropCount(0, 100), new DropCount(1, 50)], false, (MapType) 0, (Continent) 0);
        test.SetServerTable(new GlobalDropItemBoxTable(
            new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>> { [1] = new() { [1] = [group] } },
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>> {
                [1] = [new GlobalDropItemBoxTable.Item(Gear, 1, 99, new GlobalDropItemBoxTable.Range<int>(1, 1), 1, 1, [], false)],
            }));
        test.Start();

        // At 3 count 0 is gone: every roll drops the gear.
        for (int i = 0; i < 20; i++) {
            Assert.That(test.Session.Field!.ItemDrop.GetGlobalDropItems(1, 39, dropRate: 3f).Select(item => item.Id), Is.EqualTo(new[] { Gear }));
        }
    }

    [Test]
    public void KillDropRate_ReadsThePlayersAbility() {
        var test = new TestGameSession();
        test.Start();
        Assert.That(FieldNpc.KillDropRate(test.Session.Player), Is.EqualTo(1f));

        // 50 thousandths: 5 % more.
        test.Session.Player.Stats.Values[SpecialAttribute.DropRate].AddTotal(50);
        Assert.That(FieldNpc.KillDropRate(test.Session.Player), Is.EqualTo(1.05f));

        // A total under -100 % counts as no drops, not a negative rate.
        test.Session.Player.Stats.Values[SpecialAttribute.DropRate].AddTotal(-2000);
        Assert.That(FieldNpc.KillDropRate(test.Session.Player), Is.EqualTo(0f));
    }
}

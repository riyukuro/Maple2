using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Session;
using StoredItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// The bag's delete list is written by the packet thread (a discarded item: a dropped item, a used box) and by other
// threads (the field's: a currency or medal item added as a reward; the session save). Each entry is added under the
// bag lock, so none is lost. Before, Discard added its entry without the lock.
public class BagThreadsTests {
    private const int Count = 20000;
    private const int ItemId = 50000102;
    private const int MesoId = 90000001;

    private static List<StoredItem> Delete(GameSession session) =>
        (List<StoredItem>) typeof(InventoryManager).GetField("delete", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session.Item.Inventory)!;

    [Test]
    public void DiscardAgainstRewardAdd_NoEntryLost() {
        var test = new TestGameSession();
        ItemMetadata item = TestGameSession.Metadata(ItemId);
        ItemMetadata meso = TestGameSession.Metadata(MesoId);
        test.Register(item, meso);
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023));

        using var start = new Barrier(2);
        var reward = new Thread(() => {
            start.SignalAndWait();
            for (int i = 0; i < Count; i++) {
                session.Item.Inventory.Add(new StoredItem(meso, amount: 1) { Uid = 2 * Count + i + 1 });
            }
        });
        reward.Start();
        start.SignalAndWait();
        for (int i = 0; i < Count; i++) {
            session.Item.Inventory.Discard(new StoredItem(item) { Uid = i + 1 });
        }
        reward.Join();

        Assert.That((Delete(session).Count, session.Currency.Meso), Is.EqualTo((2 * Count, (long) Count)));
    }
}

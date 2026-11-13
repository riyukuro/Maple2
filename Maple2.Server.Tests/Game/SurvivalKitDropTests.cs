using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// SurvivalGameField on a real field with two sessions: the starting kit (KitBox) at the game start and the death drop
// (own match items plus one DeathBox roll, no owner) at the body. Made-up item and box ids.
public class SurvivalKitDropTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int KitBox = 99930001;
    private const int DeathBox = 99930002;
    private static readonly int[] KitItems = [20099911, 20099912, 20099913];
    private const int DeathItem = 20099921;
    private const int NormalItem = 20099931;

    private static ItemMetadata Match(int id) => TestGameSession.Metadata(id) with { Property = TestGameSession.Metadata(id).Property with { Survival = true } };

    private static void Prepare(TestGameSession test) {
        test.Register(KitItems.Select(Match).Append(Match(DeathItem)).Append(TestGameSession.Metadata(NormalItem)).ToArray());
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [KitBox] = TestGameSession.DropBox(KitItems.Select(id => new[] { (id, 1) }).ToArray()),
            [DeathBox] = TestGameSession.DropBox([(DeathItem, 1)]),
        }));
    }

    private static ICollection<FieldItem> Ground(FieldManager field) =>
        ((ConcurrentDictionary<int, FieldItem>) typeof(FieldManager).GetField("fieldItems", Any)!.GetValue(field)!).Values;

    [Test]
    public void KitAtStart_DropAtDeathWithNoOwner() {
        MapMetadata map = TestGameSession.Map(82000000);
        map = map with { Property = map.Property with { Survival = new MapMetadataSurvival(1, DeathBox, 1, KitBox, 1) } };
        var a = new TestGameSession();
        Prepare(a);
        a.AddMap(map);
        a.Start();
        a.EnterMap(map);
        var b = new TestGameSession(a.Options);
        Prepare(b);
        b.Start();
        FieldManager field = a.Session.Field!;
        typeof(Maple2.Server.Game.Model.Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(b.Session.Player, 2);
        field.Players[2] = b.Session.Player;
        b.Session.Field = field;
        // The test field has no constructor run: its widget table (read at the game end) is set here.
        typeof(FieldManager).GetField(nameof(FieldManager.Widgets))!.SetValue(field, new Dictionary<string, Maple2.Server.Game.Model.Widget.Widget>());
        a.Session.Item.Inventory.Add(new Item(TestGameSession.Metadata(NormalItem)), notifyNew: false);

        var game = new SurvivalGameField(field, [a.Session.Player, b.Session.Player]);

        int[] Held(TestGameSession test) => test.Session.Item.Inventory.Filter(item => item.Id != NormalItem).Select(item => item.Id).Order().ToArray();
        Assert.Multiple(() => {
            Assert.That(Held(a), Is.EqualTo(KitItems));
            Assert.That(Held(b), Is.EqualTo(KitItems));
            Assert.That(Ground(field), Is.Empty);
        });

        game.Death(a.Session.Player);

        List<FieldItem> ground = Ground(field).ToList();
        Assert.Multiple(() => {
            Assert.That(ground.Select(item => item.Value.Id).Order(), Is.EqualTo(KitItems.Append(DeathItem).Order()));
            Assert.That(ground.All(item => item.Owner == null && item.ReceiverId == 0), Is.True);
            Assert.That(Held(a), Is.Empty);
            Assert.That(a.Session.Item.Inventory.Find(NormalItem).Count(), Is.EqualTo(1));
            Assert.That(Held(b), Is.EqualTo(KitItems));
            Assert.That(game.Ended, Is.True);
        });
        // The own items are detached from the victim in the db, as a player drop is.
        long uid = ground.First(item => item.Value.Id == KitItems[0]).Value.Uid;
        Assert.That(a.Row(uid)?.OwnerId, Is.EqualTo(0));

        // A second death report of the fallen participant drops nothing more.
        game.Death(a.Session.Player);
        Assert.That(Ground(field), Has.Count.EqualTo(ground.Count));
    }
}

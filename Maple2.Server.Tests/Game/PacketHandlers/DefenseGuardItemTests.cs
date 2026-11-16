using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// KR 20000985 (DefenseGuard "21001023,30,150"): the client sends the plain use; the server places npc 21001023
// (Blockade) 150 ahead of the player for 30 seconds and takes one item.
public class DefenseGuardItemTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int GuardItemId = 20000985;
    private const int BlockadeId = 21001023;

    private Item Login() {
        Start();
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        test.SetServerTable(new NpcStatFactorTable(new System.Collections.Generic.Dictionary<int,
            System.Collections.Generic.IReadOnlyDictionary<int, System.Collections.Generic.IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        // The debug marker items a placed npc's movement state reads.
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        var npcs = TriggerNpcSpawnTests.NpcTable(TriggerNpcSpawnTests.Npc(BlockadeId, 0));
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field, npcs);
        typeof(GameSession).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(test.Session, npcs);
        ItemMetadata guard = TestGameSession.Metadata(GuardItemId,
            function: new ItemMetadataFunction(ItemFunction.DefenseGuard, "DefenseGuard", "21001023,30,150", false));
        test.Register(guard);
        Assert.That(test.Session.Item.Inventory.Add(new Item(guard, amount: 2), commit: true), Is.True);
        test.TakeSent();
        return test.Session.Item.Inventory.Find(GuardItemId).Single();
    }

    private void Use(Item item) {
        new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! }
            .Handle(test.Session, TestGameSession.Packet(writer => writer.WriteLong(item.Uid)));
    }

    [Test]
    public void DefenseGuard_PlacesTheBlockadeAheadAndTakesOne() {
        Item item = Login();
        Vector3 ahead = test.Session.Player.Transform.Position + test.Session.Player.Transform.FrontAxis * 150;

        Use(item);

        FieldNpc[] placed = Field.Mobs.Values.Concat(Field.Npcs.Values).Where(npc => npc.Value.Id == BlockadeId).ToArray();
        Assert.That(placed, Has.Length.EqualTo(1));
        Assert.That(placed[0].PlayerSummon, Is.True);
        Assert.That(Vector3.Distance(placed[0].Position, ahead), Is.LessThan(1f));
        Assert.That(test.Session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }
}

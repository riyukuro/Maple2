using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The dismantle dialog for gear without a break table row, as KMS2 answers it: onyx by the sell price and
// the chaos onyx row, listed at 0 too.
public class ItemDismantleHandlerTests {
    private const int Onyx = 40100023;
    private const int ChaosOnyx = 40100024;

    private TestGameSession test = null!;
    private ItemDismantleHandler handler = null!;
    private Item gear = null!;

    private GameSession Login(int rarity, EquipSlot slot = EquipSlot.CL) {
        test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(11400001, slot, type: 1);
        metadata = metadata with { Property = metadata.Property with { SellPrices = [100, 200, 7139, 9000, 0, 0] } };
        gear = test.Store(metadata);
        test.Register(TestGameSession.Metadata(Onyx, type: 4, slotMax: 9999), TestGameSession.Metadata(ChaosOnyx, type: 4, slotMax: 9999));
        test.SetTable(new ItemBreakTable(new Dictionary<int, IReadOnlyList<ItemBreakTable.Ingredient>>()));
        test.SetTable(new GachaInfoTable(new Dictionary<int, GachaInfoTable.Entry>()));
        GameSession session = test.Start();
        handler = new ItemDismantleHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata };
        session.Item.Inventory.Get(gear.Uid)!.Rarity = rarity;
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteByte(1);
        }));
        test.TakeSent();
        return session;
    }

    private void Stage(GameSession session) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(-1);
            writer.WriteLong(gear.Uid);
            writer.WriteInt(1);
        }));
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [TestCase(3, EquipSlot.CL, 142, 0, 0)]
    [TestCase(4, EquipSlot.CL, 190, 3, 3)]
    [TestCase(4, EquipSlot.OH, 95, 1, 2)]
    public void Stage_ListsOnyxAndTheChaosOnyxRow(int rarity, EquipSlot slot, int onyx, int chaosMin, int chaosMax) {
        GameSession session = Login(rarity, slot);

        Stage(session);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(ItemDismantlePacket.Stage(gear.Uid, 0, 1)),
            Hex(ItemDismantlePacket.Preview(new Dictionary<int, (int Min, int Max)> { [Onyx] = (onyx, onyx), [ChaosOnyx] = (chaosMin, chaosMax) })),
        }));
    }

    // The confirm: the item leaves the bag, the onyx comes in, and the result lists the chaos onyx with 0.
    [Test]
    public void Confirm_GivesOnyxAndReportsNoChaosOnyx() {
        GameSession session = Login(rarity: 3);
        Stage(session);
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(ItemInventoryPacket.Remove(gear.Uid))));
        Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo(Hex(ItemDismantlePacket.Result(1, new Dictionary<int, int> { [Onyx] = 142, [ChaosOnyx] = 0 }))));
        Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Null);
        Assert.That(session.Item.Inventory.Find(Onyx).Sum(item => item.Amount), Is.EqualTo(142));
        Assert.That(session.Item.Inventory.Find(ChaosOnyx), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // Rarity 4 pays 3 chaos onyx: the listed max is reached.
    [Test]
    public void Confirm_PaysTheListedMax() {
        GameSession session = Login(rarity: 4);
        Stage(session);
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(Convert.ToHexString(test.TakeSent()[^1]), Is.EqualTo(Hex(ItemDismantlePacket.Result(1, new Dictionary<int, int> { [Onyx] = 190, [ChaosOnyx] = 3 }))));
        Assert.That(session.Item.Inventory.Find(ChaosOnyx).Sum(item => item.Amount), Is.EqualTo(3));
    }
}

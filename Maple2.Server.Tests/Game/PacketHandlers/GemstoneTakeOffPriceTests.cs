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
using static Maple2.Model.Error.ItemSocketError;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemSocketSystem 10, a gem taken out of a socket. The client's price (FUN_1406284f0) is crystal fragments by the
// gem's level, 8 + 2 x level outside region 3 (a level 1 gem: 10), and nothing for an outfit (skin type above 0);
// fragments short are paid at 1000 mesos each when the mesos cover them (FUN_140f71530).
public class GemstoneTakeOffPriceTests {
    private TestGameSession test = null!;
    private Item gear = null!;
    private Item? chips;

    private GameSession Start(int skinType, int chipCount, long mesos) {
        test = new TestGameSession();
        test.SetTable(new GemstoneUpgradeTable(new Dictionary<int, GemstoneUpgradeTable.Entry> {
            [40200001] = new(1, 40200002, [], 3),
        }));
        ItemMetadata gearData = TestGameSession.Metadata(11200001, type: 1);
        gear = test.Store(gearData with { Property = gearData.Property with { SkinType = skinType } });
        if (chipCount > 0) {
            ItemMetadata chipData = TestGameSession.Metadata(40100001, type: 2);
            chips = test.Store(chipData with { Property = chipData.Property with { Tag = ItemTag.CrystalPiece } }, amount: chipCount);
        }
        test.Register(TestGameSession.Metadata(40200001, type: 13));
        GameSession session = test.Start();
        test.TakeSent();
        session.Currency.Meso = mesos;
        gear = session.Item.Inventory.Get(gear.Uid)!;
        gear.Socket = new ItemSocket(3, 1);
        gear.Socket.Sockets[0] = new ItemGemstone(40200001, stats: new ItemStats());
        return session;
    }

    private void TakeOff(GameSession session) {
        new ItemSocketHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.WriteLong(gear.Uid);
            writer.WriteByte(0);
        }));
    }

    private int Chips(GameSession session) => chips == null ? 0 : session.Item.Inventory.Get(chips.Uid)?.Amount ?? 0;

    private byte[][] Sent() {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemSocketSystem).ToArray();
    }

    [Test]
    public void OutfitGem_IsTakenOutFree() {
        GameSession session = Start(skinType: 1, chipCount: 0, mesos: 0);

        TakeOff(session);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.Sockets[0], Is.Null);
            Assert.That(session.Item.Inventory.Find(40200001).Count(), Is.EqualTo(1));
            Assert.That(session.Currency.Meso, Is.Zero);
        });
    }

    [Test]
    public void FragmentsShort_ArePaidInMesos() {
        GameSession session = Start(skinType: 0, chipCount: 4, mesos: 6500);

        TakeOff(session);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.Sockets[0], Is.Null);
            Assert.That(session.Item.Inventory.Find(40200001).Count(), Is.EqualTo(1));
            Assert.That(Chips(session), Is.Zero);
            Assert.That(session.Currency.Meso, Is.EqualTo(500));
        });
    }

    [Test]
    public void FragmentsHeld_AreTakenAlone() {
        GameSession session = Start(skinType: 0, chipCount: 12, mesos: 6500);

        TakeOff(session);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.Sockets[0], Is.Null);
            Assert.That(Chips(session), Is.EqualTo(2));
            Assert.That(session.Currency.Meso, Is.EqualTo(6500));
        });
    }

    [Test]
    public void MesosShort_TakeNothing() {
        GameSession session = Start(skinType: 0, chipCount: 4, mesos: 5999);

        TakeOff(session);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.Sockets[0], Is.Not.Null);
            Assert.That(session.Item.Inventory.Find(40200001), Is.Empty);
            Assert.That(Chips(session), Is.EqualTo(4));
            Assert.That(session.Currency.Meso, Is.EqualTo(5999));
            Assert.That(Sent().Select(Convert.ToHexString),
                Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.Error(error: s_itemsocketsystem_error_lack_price).ToArray()) }));
        });
    }
}

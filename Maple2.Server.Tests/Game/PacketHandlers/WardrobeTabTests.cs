using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The closet dialog's tab requests (uiclosetdialog.cpp): SendSave (0: index, layer) saves the layer's worn items in
// the tab and gets the tab back; the key binding (4: index, key id) is kept without an answer; the rename (6: index,
// name) renames a tab, makes one past the last, and refuses a banned name with a message box and the tab as it was.
public class WardrobeTabTests {
    private TestGameSession test = null!;

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private void Send(GameSession session, Action<Maple2.PacketLib.Tools.ByteWriter> write) {
        BanWordStorage words = BanWordStorage.WithDynamicWords(
            new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options), () => ["badword"]);
        new WardrobeHandler { BanWordStorage = words }.Handle(session, TestGameSession.Packet(write));
    }

    [Test]
    public void Save_KeepsTheWornGearAndSendsTheTab() {
        test = new TestGameSession();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1));
        GameSession session = test.Start();
        Assert.That(session.Item.Equips.Equip(cap.Uid, EquipSlot.CP, isSkin: false), Is.True);
        Wardrobe wardrobe = PetLapenshardWardrobeHandlerTests.Closet(session, 1);
        test.TakeSent();

        Send(session, writer => {
            writer.WriteByte(0);
            writer.WriteInt(0);
            writer.WriteInt(0);
        });

        Assert.Multiple(() => {
            Assert.That(wardrobe.Type, Is.EqualTo(0));
            Assert.That(wardrobe.Equips.Select(pair => (pair.Key, pair.Value.ItemUid)), Is.EqualTo(new[] { (EquipSlot.CP, cap.Uid) }));
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(WardrobePacket.Load(0, wardrobe)) }));
        });

        // A layer past the second outfit or a tab that does not exist changes nothing.
        Send(session, writer => {
            writer.WriteByte(0);
            writer.WriteInt(0);
            writer.WriteInt(3);
        });
        Send(session, writer => {
            writer.WriteByte(0);
            writer.WriteInt(5);
            writer.WriteInt(0);
        });
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(wardrobe.Type, Is.EqualTo(0));
    }

    [Test]
    public void SetKey_KeepsTheKeyWithoutAnAnswer() {
        test = new TestGameSession();
        GameSession session = test.Start();
        Wardrobe wardrobe = PetLapenshardWardrobeHandlerTests.Closet(session, 1);
        test.TakeSent();

        Send(session, writer => {
            writer.WriteByte(4);
            writer.WriteInt(0);
            writer.WriteInt(57);
        });

        Assert.That(wardrobe.KeyId, Is.EqualTo(57));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Rename_RenamesMakesOrRefuses() {
        test = new TestGameSession();
        GameSession session = test.Start();
        Wardrobe wardrobe = PetLapenshardWardrobeHandlerTests.Closet(session, 1);
        test.TakeSent();
        void Rename(int index, string name) => Send(session, writer => {
            writer.WriteByte(6);
            writer.WriteInt(index);
            writer.WriteUnicodeString(name);
        });

        Rename(0, "summer");
        Assert.That(wardrobe.Name, Is.EqualTo("summer"));
        Rename(1, "winter");
        Assert.That(session.Config.TryGetWardrobe(1, out Wardrobe? made) ? made.Name : null, Is.EqualTo("winter"));
        Assert.That(test.TakeSent(), Is.Empty);

        Rename(0, "badword");

        Assert.That(wardrobe.Name, Is.EqualTo("summer"));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(NoticePacket.MessageBox(StringCode.s_ban_check_err_any)), Hex(WardrobePacket.Load(0, wardrobe)),
        }));
    }
}

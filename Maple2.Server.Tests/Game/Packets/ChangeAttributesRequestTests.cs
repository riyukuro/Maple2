using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.Packets;

// ChangeAttributes 0x0075 mode 7 from the client: RemakeOptionDebug::vfunc_1 (commonfiles/itemremakescrolldata.cpp,
// 0x1406175b0), i64 uid, i64, u8 lock count, count x (u8 kind, i16 id), u8 kind, i32 value.
public class ChangeAttributesRequestTests {
    [Test]
    public void DebugReadsTheLockCountKindAndValue() {
        // UIItemRemakeOptionDialog::OnClickForceRemake (unnamed/unit_14129cfa0_itemremakeoptiondialog.cpp:438):
        // one lock (stat 0x0D), kind 1, value 300.
        var writer = new ByteWriter();
        writer.WriteLong(0x0102030405060708);
        writer.WriteLong(0);
        writer.WriteByte(1);
        writer.WriteByte(0);
        writer.WriteShort(0x0D);
        writer.WriteByte(1);
        writer.WriteInt(300);

        var reader = new ByteReader(writer.ToArray(), 0);
        ChangeAttributesHandler.HandleDebug(reader);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void DebugWithoutLocks() {
        // TabContentsPetRemakeOption (client/ms2/tabcontentspetremakeoption.cpp:1028): no lock, kind 0, value 0.
        var writer = new ByteWriter();
        writer.WriteLong(0x0102030405060708);
        writer.WriteLong(0);
        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteInt(0);

        var reader = new ByteReader(writer.ToArray(), 0);
        ChangeAttributesHandler.HandleDebug(reader);
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

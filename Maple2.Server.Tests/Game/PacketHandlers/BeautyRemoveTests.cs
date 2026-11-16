using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Beauty 4: the client's Customize::Equipment::SendRemoved (customize.cpp:1490), the uid of a cosmetic taken off in
// the beauty preview. The server applies the change with the style's own mode, so this one is read and dropped.
public class BeautyRemoveTests {
    [Test]
    public void Remove_ReadsTheUid_AndChangesNothing() {
        var test = new TestGameSession();
        test.Start();
        test.TakeSent();
        long meso = test.Session.Currency.Meso;
        ByteReader packet = TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteLong(0x0102030405060708);
        });

        new BeautyHandler {
            ItemMetadata = test.Session.ItemMetadata,
            TableMetadata = test.TableMetadata,
            NpcMetadata = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(test.Session, packet);

        Assert.Multiple(() => {
            Assert.That(packet.Available, Is.Zero);
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(test.Session.Currency.Meso, Is.EqualTo(meso));
        });
    }
}

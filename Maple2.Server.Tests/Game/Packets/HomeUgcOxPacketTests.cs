using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The UGC OX events' HomeAction 10 packets: the byte of the action, the sub command, then its fields, to the last byte.
// Each layout is the client's reader of that sub command (CSceneStateGame::OnHomeAction 10, then the UGC OX event
// handler: 0 fees, 1 and 4 promote state, 2 create confirm, 5 move, 12 host dialog, 13 to 15 stage).
public class HomeUgcOxPacketTests {
    private static ByteReader Open(ByteWriter packet, byte sub) {
        byte[] bytes = packet.ToArray();
        Assert.That(TestGameSession.Op(bytes), Is.EqualTo(SendOp.HomeAction));
        var reader = new ByteReader(bytes, 2);
        Assert.That(reader.ReadByte(), Is.EqualTo(10));
        Assert.That(reader.ReadByte(), Is.EqualTo(sub));
        return reader;
    }

    [Test]
    public void Fees_AreCountedInts() {
        ByteReader reader = Open(HomeActionPacket.UgcOxFees([100, 200, 300]), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 100, 200, 300 }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Promote_IsAZeroByteAndFourInts() {
        ByteReader reader = Open(HomeActionPacket.UgcOxPromote(2, 3000, 5, 60000), 1);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 2, 3000, 5, 60000 }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Confirm_IsTheHostFeeMaxAndShare() {
        ByteReader reader = Open(HomeActionPacket.UgcOxConfirm("Host", 5000, 20, 30), 2);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Host"));
        Assert.That(reader.ReadLong(), Is.EqualTo(5000));
        Assert.That(reader.ReadInt(), Is.EqualTo(20));
        Assert.That(reader.ReadInt(), Is.EqualTo(30));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Created_IsTheFeeAndThePromoteState() {
        ByteReader reader = Open(HomeActionPacket.UgcOxCreated(5000, 1, 2000, 5, 60000), 4);
        Assert.That(reader.ReadLong(), Is.EqualTo(5000));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 1, 2000, 5, 60000 }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Move_IsTheMapThePortalAndTheEventsTerms() {
        ByteReader reader = Open(HomeActionPacket.UgcOxMove(2000062, 4, 99, "Host", 5000, 20, 30), 5);
        Assert.That(reader.ReadInt(), Is.EqualTo(2000062));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadLong(), Is.EqualTo(99));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Host"));
        Assert.That(reader.ReadLong(), Is.EqualTo(5000));
        Assert.That(reader.ReadInt(), Is.EqualTo(20));
        Assert.That(reader.ReadInt(), Is.EqualTo(30));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void HostDialog_IsTheModeAndTheCount() {
        ByteReader reader = Open(HomeActionPacket.UgcOxHostDialog(HomeActionPacket.UgcOxHostMode.SelectAnswer, 7), 12);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Stage_IsTheHostTheStepTheRoundAndThePrizes() {
        ByteReader reader = Open(HomeActionPacket.UgcOxStage(99, default, 3, 1000, 2000), 13);
        Assert.That(reader.ReadLong(), Is.EqualTo(99));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(1000));
        Assert.That(reader.ReadLong(), Is.EqualTo(2000));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

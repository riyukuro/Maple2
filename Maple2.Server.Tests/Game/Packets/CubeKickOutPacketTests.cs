using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class CubeKickOutPacketTests {
    // Client: CSceneStateGame::OnResponseCube case 0x1a reads no field after the mode and opens the
    // userOutNotice box (s_home_password_user_out_chat_msg, button s_home_password_user_out_button).
    [Test]
    public void KickOut_IsModeOnly() {
        ByteWriter writer = CubePacket.KickOut();
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x1a));
        Assert.That(writer.Length, Is.EqualTo(3));
    }
}

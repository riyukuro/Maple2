using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Login.PacketHandlers;

// The lobby's login notice sends this when it loads (UILoginNotice::SendNoticeDialog, mode 1). The client reads no
// answer to it; the server's notice dialog (the web banner page) is not used here. Chosen here: read the mode and do
// nothing.
public class NoticeDialogHandler : PacketHandler<LoginSession> {
    public override RecvOp OpCode => RecvOp.NoticeDialog;

    public override void Handle(LoginSession session, IByteReader packet) {
        packet.ReadByte(); // mode, 1 from the login notice
    }
}

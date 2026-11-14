using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Login.PacketHandlers;

// RequestServerList 0x14 has no body. The client sends it when it leaves the character list: on a LoginToGame that
// succeeded (uicharacterlistdialog.cpp:592-601) and on the back-to-server-list button, before it shows the server list
// (uicharacterlistdialog.cpp:1644-1648). Picking a server there opens a new connection (uiserverlistdialog.cpp:512)
// that logs in again; the account's entry is released here so that login is not refused as already logged in while
// this socket is still closing. Nothing is sent back: the dialog shows the list it already has. The session acts for
// the account no more (LoginSession.LeftCharacterList): the client keeps this socket open on the server list, and a
// login of the same account from elsewhere is let in meanwhile.
public class RequestServerListHandler : PacketHandler<LoginSession> {
    public override RecvOp OpCode => RecvOp.RequestServerList;

    public override void Handle(LoginSession session, IByteReader packet) {
        session.LeaveCharacterList();
    }
}

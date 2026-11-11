using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class HomeInvitePacket {
    private enum Command : byte {
        Result = 0,
        Invite = 2,
    }

    // CSceneStateGame::OnHomeInviteResult (scenestategame.cpp:6637): each code shows one notice. The codes 1, 2, 5
    // and 6 are also the answer types the invited client sends back (CSceneStateGame::FUN_141740280, :8985).
    public enum InviteResult : byte {
        Accepted = 1, // name
        Declined = 2, // name
        Waiting = 3, // the invite was delivered
        Offline = 4, // name
        AutoDeclined = 5, // name; the invited player blocks invites in the options
        TimedOut = 6, // name
        Self = 7,
        CannotInviteNow = 9, // name
    }

    public static ByteWriter Result(InviteResult result, string name) {
        var pWriter = Packet.Of(SendOp.HomeInvite);
        pWriter.Write(Command.Result);
        pWriter.WriteByte((byte) result);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    public static ByteWriter Invite(Character character) {
        var pWriter = Packet.Of(SendOp.HomeInvite);
        pWriter.Write(Command.Invite);
        pWriter.WriteLong(character.AccountId);
        pWriter.WriteUnicodeString(character.Name);

        return pWriter;
    }
}

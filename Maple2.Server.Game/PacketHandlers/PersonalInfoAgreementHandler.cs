using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class PersonalInfoAgreementHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.PersonalInfoAgreement;

    public override void Handle(GameSession session, IByteReader packet) {
        // CPersonalInfoAgreement::SendAgreement: the group the player answered (0 from the game option,
        // 8 or 0x10 from the home-visit prompts) and the client's whole agreement flag set. The client
        // keeps the flags in its own game options and reads no reply.
        int group = packet.ReadInt();
        int flags = packet.ReadInt();
        Logger.Debug("Personal info agreement from {CharacterId}: group={Group} flags={Flags:X}", session.CharacterId, group, flags);
    }
}

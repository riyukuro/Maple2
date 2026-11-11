using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// Client senders: client/ms2/uicharacterabilitydialog.cpp:408-448, 495-508 (modes 2 and 1) and the GM chat
// command at client/ms2/usercommandcli.cpp:1957 (mode 3, no body; not built).
public class CharacterAbilityHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.CharacterAbility;

    public override void Handle(GameSession session, IByteReader packet) {
        byte mode = packet.ReadByte();
        switch (mode) {
            case CharacterAbilityRules.Reset:
                session.Config.ResetCharacterAbilities();
                return;
            case CharacterAbilityRules.Learn:
                session.Config.LearnCharacterAbility(packet.ReadInt());
                return;
        }
    }
}

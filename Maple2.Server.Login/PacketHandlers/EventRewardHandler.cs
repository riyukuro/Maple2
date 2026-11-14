using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Login.PacketHandlers;

// The character list sends one EventReward: 16 + long character id, the burning levelup's character pick
// (UICharacterListDialog::SendEventReward, uicharacterlistdialog.cpp:2093, on the OK of the popup after a create).
// The other sub-types come from game dialogs: read and logged.
public class EventRewardHandler : PacketHandler<LoginSession> {
    public override RecvOp OpCode => RecvOp.EventReward;

    private const byte PickBurningLevelupCharacter = 0x10;

    public override void Handle(LoginSession session, IByteReader packet) {
        byte command = packet.ReadByte();
        if (command != PickBurningLevelupCharacter) {
            Logger.Debug("EventReward {Command} from account {AccountId} at the login", command, session.AccountId);
            return;
        }
        session.PickBurningLevelup(packet.ReadLong());
    }
}

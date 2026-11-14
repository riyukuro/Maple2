using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// The PC bang dialog (UIPcBangDialog): the top bar's PC bang button asks for the info with the dialog to open
// (UIUpperHudDialog::SendPcBangBonus @0x141356d30 writes 0, 1), the daily reset with the dialog open asks again
// without it (OnResetBonus writes 0, 0), the dialog's load asks for the info (3), a play bonus row's button claims it
// and a gift's button receives it.
public class PcBangHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.PcBangBonus;

    private enum Command : byte {
        Request = 0,
        ClaimPlayBonus = 1,
        ReceiveGift = 2,
        Load = 3,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = (Command) packet.ReadByte();
        switch (command) {
            case Command.Request: {
                // The info's last byte opens the dialog when it is closed (OnPacketInfo).
                bool show = packet.ReadBool();
                if (session.PcBang.On) {
                    session.PcBang.SendInfo(show);
                }
                return;
            }
            case Command.Load:
                if (session.PcBang.On) {
                    session.PcBang.SendInfo(false);
                }
                return;
            case Command.ClaimPlayBonus:
                session.PcBang.Claim(packet.ReadByte());
                return;
            case Command.ReceiveGift:
                session.PcBang.ReceiveGift(packet.ReadInt());
                return;
        }
    }
}

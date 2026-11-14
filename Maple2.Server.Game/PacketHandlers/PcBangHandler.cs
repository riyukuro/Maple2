using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// The PC bang dialog (UIPcBangDialog): its load asks for the info, a play bonus row's button claims it, a gift's
// button receives it, and the daily reset with the dialog open asks again.
public class PcBangHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.PcBangBonus;

    private enum Command : byte {
        Reset = 0,
        ClaimPlayBonus = 1,
        ReceiveGift = 2,
        Load = 3,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = (Command) packet.ReadByte();
        switch (command) {
            case Command.Reset:
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

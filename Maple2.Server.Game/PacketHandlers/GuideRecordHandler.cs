using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class GuideRecordHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.GuideRecord;


    public override void Handle(GameSession session, IByteReader packet) {
        GuideRecordRules.Read(packet, session.Config.GuideRecords);
    }
}

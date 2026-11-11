using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class TakeBoatHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.TakeBoat;

    public override void Handle(GameSession session, IByteReader packet) {
        int npcObjectId = NpcTalkRules.ReadNpc(packet);

        // The client closed the talk before this (cinematiccomponent.cpp:1157-1161). Nothing is built for it:
        // the boat ride is handled in the job script.
    }
}

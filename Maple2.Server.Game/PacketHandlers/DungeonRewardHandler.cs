using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class DungeonRewardHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.DungeonReward;

    public override void Handle(GameSession session, IByteReader packet) {
        byte mode = packet.ReadByte();
        var dungeon = session.Field as DungeonFieldManager;
        bool ended = dungeon != null && dungeon.DungeonRoomRecord.State != DungeonState.None;
        if (!DungeonRewardRules.Leaves(mode, dungeon != null, dungeon?.DungeonMetadata.LeaveAfterCloseReward ?? false, ended)) {
            Logger.Debug("DungeonReward {Mode} from {CharacterId} not answered", mode, session.CharacterId);
            return;
        }

        session.ReturnField();
    }
}

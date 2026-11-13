using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class DungeonRewardHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.DungeonReward;

    public override void Handle(GameSession session, IByteReader packet) {
        byte mode = packet.ReadByte();
        var dungeon = session.Field as DungeonFieldManager;
        bool ended = dungeon != null && dungeon.DungeonRoomRecord.State != DungeonState.None;
        if (mode == DungeonRewardRules.GiveUp) {
            HandleGiveUp(session, dungeon, ended);
            return;
        }
        if (!DungeonRewardRules.Leaves(mode, dungeon != null, dungeon?.DungeonMetadata.LeaveAfterCloseReward ?? false, ended)) {
            Logger.Debug("DungeonReward {Mode} from {CharacterId} not answered", mode, session.CharacterId);
            return;
        }

        session.ReturnField();
    }

    private void HandleGiveUp(GameSession session, DungeonFieldManager? dungeon, bool ended) {
        bool inRoom = dungeon != null && dungeon.FieldInstance.Type != InstanceType.DungeonLobby;
        bool isLeader = session.Party.Party?.LeaderCharacterId == session.CharacterId;
        switch (DungeonRewardRules.GiveUpOf(DungeonRewardRules.GiveUp, inRoom, isLeader, ended, dungeon?.GiveUpEnabled ?? false)) {
            case DungeonRewardRules.GiveUpResult.TooEarly:
                session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_cannot_giveup_yet));
                return;
            case DungeonRewardRules.GiveUpResult.GiveUp:
                dungeon!.Broadcast(DungeonMissionPacket.GiveUp());
                dungeon.ChangeState(DungeonState.Fail);
                return;
            default:
                Logger.Debug("DungeonReward give-up from {CharacterId} ignored", session.CharacterId);
                return;
        }
    }
}

using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class DungeonRoomHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.RoomDungeon;

    private enum Command : byte {
        Reset = 1,
        Create = 2,
        EnterLobby = 3,
        AddExtraRewardCount = 8,
        EnterField = 9,
        FindHelper = 15,
        FindMember = 16,
        ChangeExpert = 17,
        Favorite = 24,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Reset:
                HandleReset(session);
                break;
            case Command.Create:
                HandleCreate(session, packet);
                break;
            case Command.EnterLobby:
                HandleEnter(session, packet);
                break;
            case Command.EnterField:
                HandleEnterField(session);
                break;
            case Command.Favorite:
                HandleFavorite(session, packet);
                break;
            case Command.AddExtraRewardCount:
                // Not handled: the dungeon whose extra reward count is bought with the coupon item.
            case Command.FindHelper:
            case Command.FindMember:
                // Not handled: the dungeon the party asks helpers or members for.
            case Command.ChangeExpert:
                // Not handled: the dungeon the player switches to expert for.
                Logger.Debug("RoomDungeon {Command} from {CharacterId}: dungeon {Id}", command, session.CharacterId, packet.ReadInt());
                break;
        }
    }

    private void HandleReset(GameSession session) {
        session.Dungeon.Reset();
    }

    private void HandleCreate(GameSession session, IByteReader packet) {
        int dungeonId = packet.ReadInt();
        bool withParty = packet.ReadBool();
        int unknown = packet.ReadInt();
        byte unknown2 = packet.ReadByte();

        session.Dungeon.CreateDungeonRoom(dungeonId, withParty);
    }

    private void HandleEnter(GameSession session, IByteReader packet) {
        int unknown = packet.ReadInt();
        session.Dungeon.EnterLobby();
    }

    private void HandleFavorite(GameSession session, IByteReader packet) {
        int dungeonId = packet.ReadInt();
        bool favorite = packet.ReadBool();

        if (!session.Dungeon.Records.TryGetValue(dungeonId, out DungeonRecord? record)) {
            return;
        }

        // The client shows the mark from the record's flag, so the record is sent back.
        record.Flag = favorite ? record.Flag | DungeonRecordFlag.Favorite : record.Flag & ~DungeonRecordFlag.Favorite;
        session.Send(DungeonRoomPacket.Update(record));
    }

    private void HandleEnterField(GameSession session) {
        // enter first field of dungeon
        session.Dungeon.EnterInitField();
    }
}

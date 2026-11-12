using System.Net;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;

namespace Maple2.Server.Game.PacketHandlers;

// The client's Pvp requests. The rank duel and the individual arena have a queue in the World server (PvpLookup)
// and a field per match (RankDuelField, ArenaMatchField); guild PvP has none.
public class PvpHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Pvp;

    private enum Command : byte {
        GuildRegister = 1, // uiguilddialog OnRegistPVP
        GuildUnregister = 2,
        Guild3 = 3, // CGuildManager::SendPVPMode3
        Guild4 = 4, // CGuildManager::SendPVPMode4
        ArenaRegister = 9, // UIIndividualPvPDialog, with the picked arena
        ArenaUnregister = 10,
        ArenaJoin = 11, // CPVPManager::SendCancelRequest, after a match
        RankDuelRegister = 14, // UIRankPvPDialog::FUN_140f95750
        RankDuelUnregister = 15, // UIRankPvPDialog::SendPVPMode15
        RankDuelJoin = 16, // CPvpRankDuelComponent::RequestCancelMatch, the match-found box's button
        ResultClose = 104, // the result dialogs
        TeamResultClose = 110,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = (Command) packet.ReadByte();
        switch (command) {
            case Command.ArenaRegister:
                HandleArenaRegister(session, packet.ReadInt());
                return;
            case Command.ArenaUnregister:
                PvpResponse? unregistered = Request(session, new PvpRequest { RequesterId = session.CharacterId, ArenaUnregister = new PvpRequest.Types.ArenaUnregister() });
                session.Send(PvpPacket.ArenaUnregistered());
                SendWaits(session, unregistered);
                return;
            case Command.ArenaJoin:
                HandleArenaJoin(session);
                return;
            case Command.RankDuelRegister:
                HandleRankDuelRegister(session);
                return;
            case Command.RankDuelUnregister:
                HandleRankDuelUnregister(session);
                return;
            case Command.RankDuelJoin:
                HandleRankDuelJoin(session);
                return;
            default:
                // Guild PvP has no refusal the client reads here; the result dialogs' close needs no answer.
                return;
        }
    }

    // The register is answered first (state 1), then the World queues the character: a match the World makes at
    // once is then told after it. A refusal takes the client back to state 0 with the matching failed box.
    private static void HandleRankDuelRegister(GameSession session) {
        if (session.Field?.RankDuel != null || session.Field?.Arena != null) {
            session.Send(PvpPacket.RankDuelInArenaField());
            return;
        }
        if (!CanLeave(session)) {
            session.Send(PvpPacket.RankDuelMatchFailed());
            return;
        }

        session.Send(PvpPacket.RankDuelRegistered());
        // Queued already: the World holds the character queued, as state 1 now says. In a match already, or no
        // answer: state 0 with the failed box.
        int? error = Request(session, new PvpRequest { RequesterId = session.CharacterId, Register = new PvpRequest.Types.Register { Rating = RankDuelStanding.Rating(session) } })?.Error;
        if (error is not ((int) RankDuelError.none or (int) RankDuelError.Queued)) {
            session.Send(PvpPacket.RankDuelMatchFailed());
        }
    }

    // RankDuelField.CanLeaveFor for the session's player and field.
    private static bool CanLeave(GameSession session) {
        InstanceFieldMetadata? instance = session.Field?.FieldInstance;
        return RankDuelField.CanLeaveFor(session.Player.IsDead, session.Field is DungeonFieldManager,
            instance != null && RankDuelField.RefusesInstance(instance.Type),
            session.Field?.RankDuel != null || session.Field?.Arena != null);
    }

    private static void HandleRankDuelUnregister(GameSession session) {
        Request(session, new PvpRequest { RequesterId = session.CharacterId, Unregister = new PvpRequest.Types.Unregister() });
        session.Send(PvpPacket.RankDuelUnregistered());
    }

    // The match-found box's button: the character moves into the match field, on the channel that holds it.
    private static void HandleRankDuelJoin(GameSession session) {
        if (session.Field?.RankDuel != null) {
            return;
        }
        // A join the server refuses: the client leaves its state 2 with the failed box; the match counts the
        // character as not come.
        if (!CanLeave(session)) {
            session.Send(PvpPacket.RankDuelMatchFailed());
            return;
        }
        PvpResponse? response = Request(session, new PvpRequest { RequesterId = session.CharacterId, Join = new PvpRequest.Types.Join() });
        if (response is not { Error: (int) RankDuelError.none }) {
            session.Send(PvpPacket.RankDuelMatchFailed());
            return;
        }
        MoveToMatch(session, response, PvpPacket.RankDuelMatchFailed());
    }

    // The individual arena (Pvp 9 with the arena; CSceneStateGame::OnPvp 9/10/11). Only an arena whose field runs a
    // round the server builds is queued (PvpMatchMaps.PlayableArenaFields); the others get Pvp 10, the client's
    // state 0, as before. The register is answered first (state 1), then the World queues the character.
    private static void HandleArenaRegister(GameSession session, int arenaType) {
        if (session.Field?.RankDuel != null || session.Field?.Arena != null || !CanLeave(session)
            || !session.TableMetadata.IndividualArenaTable.Entries.TryGetValue(arenaType, out IndividualArenaTable.Entry? arena)
            || !PvpMatchMaps.PlayableArenaFields.Contains(arena.FieldId)) {
            session.Send(PvpPacket.ArenaUnregistered());
            return;
        }

        session.Send(PvpPacket.ArenaRegistered(arenaType));
        PvpResponse? response = Request(session, new PvpRequest {
            RequesterId = session.CharacterId,
            ArenaRegister = new PvpRequest.Types.ArenaRegister { Arena = arenaType, PlayerCount = arena.PlayerCount },
        });
        if (response?.Error is not ((int) RankDuelError.none or (int) RankDuelError.Queued)) {
            session.Send(PvpPacket.ArenaUnregistered());
        }
        SendWaits(session, response);
    }

    // Pvp 12 with the arena queues' waits (the arena window's wait labels).
    private static void SendWaits(GameSession session, PvpResponse? response) {
        if (response == null) {
            return;
        }
        session.Send(PvpPacket.GameRuleValues(response.ArenaWaits.Select(wait => (wait.Key, wait.Value)).ToList()));
    }

    // The yes of the individualArenaJoin box: the character moves into the arena match's field.
    private static void HandleArenaJoin(GameSession session) {
        if (session.Field?.Arena != null) {
            return;
        }
        if (!CanLeave(session)) {
            session.Send(PvpPacket.ArenaUnregistered());
            return;
        }
        PvpResponse? response = Request(session, new PvpRequest { RequesterId = session.CharacterId, ArenaJoin = new PvpRequest.Types.ArenaJoin() });
        if (response is not { Error: (int) RankDuelError.none }) {
            session.Send(PvpPacket.ArenaUnregistered());
            return;
        }
        MoveToMatch(session, response, PvpPacket.ArenaUnregistered());
    }

    // Moves the character into the match field the World named; a failed move sends `failed` (the client's way back to
    // state 0).
    private static void MoveToMatch(GameSession session, PvpResponse response, Maple2.PacketLib.Tools.ByteWriter failed) {
        // Back to this spot after the match (GameSession.ReturnField), when this is the return field.
        Maple2.Model.Game.Character character = session.Player.Value.Character;
        if (character.MapId == character.ReturnMaps.Peek()) {
            character.ReturnPosition = session.Player.Position;
        }

        session.MigrationSave();
        try {
            MigrateOutResponse migrate = session.World.MigrateOut(new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                MachineId = session.MachineId.ToString(),
                Server = Maple2.Server.World.Service.Server.Game,
                Channel = response.Channel,
                MapId = response.MapId,
                RoomId = response.RoomId,
                Type = Maple2.Server.World.Service.MigrationType.PvpMatch,
                PetUid = session.SummonedPetUid,
            });
            if (!PlayerWarpUtil.TryEndpoint(migrate.IpAddress, migrate.Port, out IPEndPoint? endpoint)) {
                session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
                session.Send(failed);
                return;
            }
            session.Send(MigrationPacket.GameToGame(endpoint, migrate.Token, response.MapId));
            session.State = Maple2.Model.Enum.SessionState.ChangeMap;
        } catch (RpcException) {
            // The World counts the character as joined (it is not told again at the end): its client goes back to
            // state 0 here.
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            session.Send(failed);
            return;
        }
        session.Disconnect();
    }

    private static PvpResponse? Request(GameSession session, PvpRequest request) {
        try {
            return session.World.Pvp(request, deadline: DateTime.UtcNow.AddSeconds(5));
        } catch (RpcException) {
            return null;
        }
    }
}

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
// and a field per match (RankDuelField, ArenaMatchField); the guild championship's queue is the World's
// GuildPvpLookup and its field the guild arena's (GuildPvpZoneField).
public class PvpHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Pvp;

    private enum Command : byte {
        GuildRegister = 1, // uiguilddialog OnRegistPVP
        GuildUnregister = 2,
        GuildJoin = 3, // CGuildManager::SendPVPMode3: the matched guild's member enters the match
        GuildChampion = 4, // CGuildManager::SendPVPMode4: the player cheers for its own guild this season
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
            case Command.GuildRegister:
                HandleGuildRegister(session, true);
                return;
            case Command.GuildUnregister:
                HandleGuildRegister(session, false);
                return;
            case Command.GuildJoin:
                HandleGuildJoin(session);
                return;
            case Command.GuildChampion:
                HandleGuildChampion(session);
                return;
            default:
                // The result dialogs' close needs no answer.
                return;
        }
    }

    // The level the rank duel and arena windows register from (CFeatureManager::FUN_14036dc20; UIRankPvPDialog,
    // UIIndividualPvPDialog: string 0x42a under it, nothing sent).
    public const short MinLevel = 50;

    // The register is answered first (state 1), then the World queues the character: a match the World makes at
    // once is then told after it. A refusal takes the client back to state 0 with the matching failed box.
    private static void HandleRankDuelRegister(GameSession session) {
        if (session.Field?.RankDuel != null || session.Field?.Arena != null) {
            session.Send(PvpPacket.RankDuelInArenaField());
            return;
        }
        // The rank duel arenas are the maps of the PvPRankingDuelMode feature (KR level 13): with the feature off in the
        // operator's feature.xml there is no arena to fight in, so the register is refused before the World is asked.
        if (!CanLeave(session) || !session.ServerTableMetadata.FeatureTable.IsOn(RankDuelFeature)
            || session.Player.Value.Character.Level < MinLevel) {
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

    // The feature.xml name of the rank duel arenas' environments (map/65010001.xml to 65010010.xml).
    public const string RankDuelFeature = "PvPRankingDuelMode";

    // Whether a player takes the rank duel's observe offer (Pvp 0x19): one its join would not refuse.
    public static bool TakesObserveOffer(GameSession session) {
        return session.Field is { RankDuel: null, Arena: null, PvpZone: null } && CanLeave(session);
    }

    // RankDuelField.CanLeaveFor for the session's player and field.
    internal static bool CanLeave(GameSession session) {
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
        // The same request comes from the observe offer's box: with no match of its own the World names the duel to
        // watch (response.Observer); the move is the same.
        PvpResponse? response = Request(session, new PvpRequest { RequesterId = session.CharacterId, Join = new PvpRequest.Types.Join() });
        if (response is not { Error: (int) RankDuelError.none }) {
            session.Send(PvpPacket.RankDuelMatchFailed());
            return;
        }
        MoveToMatch(session, response, PvpPacket.RankDuelMatchFailed());
    }

    // The individual arena (Pvp 9 with the arena; CSceneStateGame::OnPvp 9/10/11), from MinLevel. Only an arena
    // whose field runs a round the server builds is queued (PvpMatchMaps.PlayableArenaFields); the others get Pvp 10,
    // the client's state 0, as before. The register is answered first (state 1), then the World queues the character.
    private static void HandleArenaRegister(GameSession session, int arenaType) {
        if (session.Field?.RankDuel != null || session.Field?.Arena != null || !CanLeave(session)
            || session.Player.Value.Character.Level < MinLevel
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
        // Every arena of the table, 0 for an empty queue: the client keeps a value until it is sent again.
        List<(int, int)> waits = session.TableMetadata.IndividualArenaTable.Entries.Keys.Order()
            .Select(arena => (arena, response.ArenaWaits.GetValueOrDefault(arena))).ToList();
        session.Send(PvpPacket.GameRuleValues(waits));
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

    // Pvp 1 and 2: the World checks the right and the event; its notices (Guild 0x2b, 0x2c, 0x27) go to the guild. A
    // refusal is the guild error box.
    private static void HandleGuildRegister(GameSession session, bool register) {
        long guildId = session.Guild.Id;
        if (guildId == 0) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_null_guild));
            return;
        }
        var request = new GuildRequest { RequestorId = session.CharacterId };
        if (register) {
            request.PvpRegister = new GuildRequest.Types.PvpRegister { GuildId = guildId };
        } else {
            request.PvpUnregister = new GuildRequest.Types.PvpUnregister { GuildId = guildId };
        }
        GuildResponse? response = GuildRequest(session, request);
        if (response == null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        } else if ((GuildError) response.Error != GuildError.none) {
            session.Send(GuildPacket.Error((GuildError) response.Error, response.ErrorArg));
        }
    }

    // Pvp 3: the client sends it only in state 2 (matched); the match is the one of the player's own guild (the
    // cheered guild of Pvp 4 is not checked), and the World names the match's field.
    private static void HandleGuildJoin(GameSession session) {
        long guildId = session.Guild.Id;
        if (guildId == 0) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_null_guild));
            return;
        }
        if (session.Field?.GuildPvp != null || !CanLeave(session)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_cant_during_pvp));
            return;
        }
        GuildResponse? response = GuildRequest(session, new GuildRequest {
            RequestorId = session.CharacterId,
            PvpJoin = new GuildRequest.Types.PvpJoin { GuildId = guildId },
        });
        if (response == null || (GuildError) response.Error != GuildError.none) {
            session.Send(GuildPacket.Error(response == null ? GuildError.s_guild_err_unknown : (GuildError) response.Error, response?.ErrorArg ?? 0));
            return;
        }
        MoveToMatch(session, response.Channel, response.MapId, response.RoomId, GuildPacket.Error(GuildError.s_guild_err_unknown));
    }

    // Pvp 4 (the guild dialog's button): the player's championship guild becomes its own guild for the current season
    // (Character ChampionSeasonId, ChampionGuildId, ChampionGuildName; ServerEnter sends them back), answered with Pvp
    // 4. Outside a season, or without a guild, nothing changes.
    private static void HandleGuildChampion(GameSession session) {
        if (session.Guild.Guild is not { } guild) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_null_guild));
            return;
        }
        SeasonDataTable.Entry? season = Maple2.Model.Game.GuildPvp.CurrentSeason(session.TableMetadata.SeasonDataTable.GuildPvp, DateTime.Now);
        if (season == null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_isNotVsGameTime));
            return;
        }
        Maple2.Model.Game.Character character = session.Player.Value.Character;
        character.ChampionSeasonId = season.Id;
        character.ChampionGuildId = guild.Id;
        character.ChampionGuildName = guild.Name;
        session.Send(PvpPacket.ChampionGuild(guild.Id, guild.Name));
    }

    private static GuildResponse? GuildRequest(GameSession session, GuildRequest request) {
        try {
            return session.World.Guild(request, deadline: DateTime.UtcNow.AddSeconds(5));
        } catch (RpcException) {
            return null;
        }
    }

    internal static void MoveToMatch(GameSession session, PvpResponse response, Maple2.PacketLib.Tools.ByteWriter failed) {
        MoveToMatch(session, response.Channel, response.MapId, response.RoomId, failed);
    }

    // Moves the character into the match field the World named; a failed move sends `failed` (the client's way back to
    // state 0).
    private static void MoveToMatch(GameSession session, int channel, int mapId, int roomId, Maple2.PacketLib.Tools.ByteWriter failed) {
        // Back to this spot after the match (GameSession.ReturnField), when this is the return field. The field's own
        // map: an instanced field (a dungeon hall, a channelScale map) leaves Character.MapId at the open field it was
        // entered from, so a join from a hall kept the hall's spot as the town's return position.
        Maple2.Model.Game.Character character = session.Player.Value.Character;
        if (Maple2.Model.Game.Dungeon.DungeonHallRules.KeepsReturnPosition(session.Field?.MapId ?? 0, character.ReturnMaps.Peek())) {
            character.ReturnPosition = session.Player.Position;
        }

        session.MigrationSave();
        try {
            MigrateOutResponse migrate = session.World.MigrateOut(new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                MachineId = session.MachineId.ToString(),
                Server = Maple2.Server.World.Service.Server.Game,
                Channel = channel,
                MapId = mapId,
                RoomId = roomId,
                Type = Maple2.Server.World.Service.MigrationType.PvpMatch,
                PetUid = session.SummonedPetUid,
            });
            if (!PlayerWarpUtil.TryEndpoint(migrate.IpAddress, migrate.Port, out IPEndPoint? endpoint)) {
                session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
                session.Send(failed);
                session.MigrationRefused();
                return;
            }
            session.Send(MigrationPacket.GameToGame(endpoint, migrate.Token, mapId));
            session.State = Maple2.Model.Enum.SessionState.ChangeMap;
        } catch (RpcException) {
            // The World counts the character as joined (it is not told again at the end): its client goes back to
            // state 0 here.
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            session.Send(failed);
            session.MigrationRefused();
            return;
        }
        session.Disconnect();
    }

    internal static PvpResponse? Request(GameSession session, PvpRequest request) {
        try {
            return session.World.Pvp(request, deadline: DateTime.UtcNow.AddSeconds(5));
        } catch (RpcException) {
            return null;
        }
    }
}

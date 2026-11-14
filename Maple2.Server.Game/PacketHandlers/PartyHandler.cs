using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class PartyHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Party;

    private enum Command : byte {
        Invite = 1,
        InviteResponse = 2,
        Leave = 3,
        Kick = 4,
        SetLeader = 17,
        SearchPartyJoin = 23,
        SummonParty = 29,
        DungeonMatch = 32,
        PartySearch = 33,
        CancelPartySearch = 34,
        VoteKick = 45,
        ReadyCheck = 46,
        VoteReply = 48,
        Unknown39 = 39,
        DungeonMemberConfirm = 43,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Invite:
                HandleInvite(session, packet);
                return;
            case Command.InviteResponse:
                HandleInviteResponse(session, packet);
                return;
            case Command.Leave:
                HandleLeave(session, packet);
                return;
            case Command.Kick:
                HandleKick(session, packet);
                return;
            case Command.SetLeader:
                HandleSetLeader(session, packet);
                return;
            case Command.SearchPartyJoin:
                HandleMatchPartyJoin(session, packet);
                return;
            case Command.SummonParty:
                HandleSummonParty(session);
                return;
            case Command.DungeonMatch:
                HandleDungeonMatch(session, packet);
                return;
            case Command.PartySearch:
                HandlePartySearch(session, packet);
                return;
            case Command.CancelPartySearch:
                HandleCancelPartySearch(session, packet);
                return;
            case Command.VoteKick:
                HandleVoteKick(session, packet);
                return;
            case Command.ReadyCheck:
                HandleReadyCheck(session);
                return;
            case Command.VoteReply:
                HandleVoteReply(session, packet);
                return;
            case Command.Unknown39:
            case Command.DungeonMemberConfirm:
                HandleJoinDungeonRecruit(session, packet);
                return;
        }

    }

    private void HandleInvite(GameSession session, IByteReader packet) {
        string playerName = packet.ReadUnicodeString();

        if (string.Equals(playerName, session.PlayerName, StringComparison.OrdinalIgnoreCase)) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_myself));
            return;
        }

        if (session.Party.Party == null) {
            // Create new party
            PartyResponse response = World.Party(new PartyRequest {
                RequestorId = session.CharacterId,
                Create = new PartyRequest.Types.Create { },
            });

            var error = (PartyError) response.Error;
            if (error != PartyError.none) {
                session.Send(PartyPacket.Error(error));
                return;
            }

            session.Party.SetParty(response.Party, joinNotify: true);
        } else if (session.Party.Party.LeaderCharacterId != session.CharacterId) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_chief));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(playerName);
        if (characterId == 0) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_cannot_invite));
            return;
        }

        try {
            var request = new PartyRequest {
                RequestorId = session.CharacterId,
                Invite = new PartyRequest.Types.Invite {
                    PartyId = session.Party.Id,
                    ReceiverId = characterId,
                },
            };
            PartyResponse response = World.Party(request);
            var error = (PartyError) response.Error;
            if (error != PartyError.none) {
                session.Send(PartyPacket.Error(error, playerName));
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to invite {Name} to party", playerName);
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
        }
    }

    private void HandleInviteResponse(GameSession session, IByteReader packet) {
        string name = packet.ReadUnicodeString();
        PartyInviteResponse response = (PartyInviteResponse) packet.ReadByte();
        int partyId = packet.ReadInt();

        if (session.Party.Party != null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_already));
            return;
        }

        PartyResponse partyResponse = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            RespondInvite = new PartyRequest.Types.RespondInvite {
                PartyId = partyId,
                Reply = (int) response,
            },
        });
        var error = (PartyError) partyResponse.Error;
        if (error != PartyError.none) {
            session.Send(PartyPacket.Error(error));
            return;
        }

        if (response == PartyInviteResponse.Accept) {
            session.Party.SetParty(partyResponse.Party, joinNotify: true);
        }
    }

    private void HandleLeave(GameSession session, IByteReader packet) {
        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            Leave = new PartyRequest.Types.Leave {
                PartyId = session.Party.Id,
            },
        });
        SendError(session, response);
    }

    private void HandleKick(GameSession session, IByteReader packet) {
        long targetCharacterId = packet.ReadLong();
        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            Kick = new PartyRequest.Types.Kick {
                PartyId = session.Party.Id,
                ReceiverId = targetCharacterId,
            },
        });

        SendError(session, response);
    }

    private void HandleSetLeader(GameSession session, IByteReader packet) {
        string targetName = packet.ReadUnicodeString();
        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long targetCharacterId = db.GetCharacterId(targetName);
        if (targetCharacterId == 0) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_exist));
            return;
        }

        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            UpdateLeader = new PartyRequest.Types.UpdateLeader {
                PartyId = session.Party.Id,
                CharacterId = targetCharacterId,
            },
        });
        SendError(session, response);
    }

    // World refuses a party command with an error code (not leader, not found, ...); KMS2 shows it to the player.
    private static void SendError(GameSession session, PartyResponse response) {
        var error = (PartyError) response.Error;
        if (error != PartyError.none) {
            session.Send(PartyPacket.Error(error));
        }
    }

    private void HandleMatchPartyJoin(GameSession session, IByteReader packet) {
        int partyId = packet.ReadInt();
        string leaderName = packet.ReadUnicodeString();
        long partySearchId = packet.ReadLong();

        if (session.Party.Party != null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_already));
            return;
        }

        PartyResponse? response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            JoinByPartySearch = new PartyRequest.Types.JoinByPartySearch {
                PartyId = partyId,
                PartySearchId = partySearchId,
            },
        });

        // 0x20 (a listing with approval): "You requested to join {0}'s party." with the leader's name.
        if (response.Error != (int) PartyError.none) {
            session.Send(PartyPacket.Error((PartyError) response.Error, response.LeaderName));
            return;
        }

        session.Party.SetParty(response.Party, joinNotify: true);
    }

    // UIMessenger::FUN_141a17bc0 (uimessenger.cpp:2502, join as a dungeon helper) and
    // UIMessenger::OnDungeonMemberConfirm (uimessenger.cpp:2520, join as a member): party id and dungeon room id from
    // the confirm box of a recruiting notice's chat link (DungeonHelp 0x0109). Both join as an ordinary member, held
    // to the room's limits; the party does not keep which link was used (r63, FORK.md).
    private void HandleJoinDungeonRecruit(GameSession session, IByteReader packet) {
        int partyId = packet.ReadInt();
        int roomId = packet.ReadInt();

        if (session.Party.Party != null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_already));
            return;
        }

        PartyResponse? response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            JoinDungeonRecruit = new PartyRequest.Types.JoinDungeonRecruit {
                PartyId = partyId,
                RoomId = roomId,
            },
        });

        if (response.Error != (int) PartyError.none) {
            session.Send(PartyPacket.Error((PartyError) response.Error));
            return;
        }

        session.Party.SetParty(response.Party, joinNotify: true);
    }

    // StateRecall with no scroll: the player agreed to pay PartyRecallMerat merets instead.
    private void HandleSummonParty(GameSession session) {
        int price = Constants.PartyRecallMerat;
        // Taken before the recall goes out, shown after it as before; given back when the recall is not sent.
        if (!session.Currency.TryTakeMeret(price, notify: false)) {
            return;
        }
        if (!session.Party.Recall()) {
            session.Currency.ReturnMeret(price);
            return;
        }

        session.Currency.NotifyMeretTaken(price);
    }

    // Party 32: the dungeon match search (PartyDungeonMatch.ReadSearch). World runs the matchmaker (the user's
    // rules, FORK.md); Game picks the rooms the search may use. With no picked room the client asks for the
    // recommended rooms (DungeonRoomTable::GetMatchRooms of the lowest member's level and gear score).
    private void HandleDungeonMatch(GameSession session, IByteReader packet) {
        PartyDungeonMatchRequest? request = PartyDungeonMatch.ReadSearch(packet);
        if (request == null) {
            return;
        }
        (short level, int gearScore) = LowestMember(session);
        IReadOnlyDictionary<int, DungeonRoomMetadata> table = session.TableMetadata.DungeonRoomTable.Entries;
        bool picked = request.Value.RoomIds.Count > 0;
        List<DungeonRoomMetadata?> candidates = picked
            ? request.Value.RoomIds.Distinct().Select(id => table.GetValueOrDefault(id)).ToList()
            : request.Value.Kind == 1
                ? DungeonMatchRules.MatchRooms(table.Values, level, gearScore, Constants.DungeonMatchRecommendPickCount).ToList<DungeonRoomMetadata?>()
                : [];
        SearchDungeonMatch(session, request.Value.Kind, picked, candidates, level);
    }

    // Party 33: the party search for one dungeon room (PartyDungeonMatch.ReadPartySearch), under the match kind of
    // the room's group type, the kind the room dialog's cancel (34) and IsMatchSearching use.
    private void HandlePartySearch(GameSession session, IByteReader packet) {
        int? roomId = PartyDungeonMatch.ReadPartySearch(packet);
        if (roomId == null) {
            return;
        }
        session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(roomId.Value, out DungeonRoomMetadata? room);
        byte kind = room == null ? (byte) 0 : DungeonMatchRules.KindOf(room.GroupType);
        (short level, _) = LowestMember(session);
        SearchDungeonMatch(session, kind, true, [room], level);
    }

    // The rooms are checked (DungeonMatchRules.RoomError) and a refused search gets DungeonMatch 0 with the client's
    // error (SearchError). The leader and offline checks stay on World, which answers them with Party 40 and 41, the
    // same isNotChief and hasOfflineUser texts (partymanager.cpp:1409). The cooldown errors 8 and 9 are not sent:
    // they are for the [Comradery] buff, which this server never gives (FORK.md).
    private void SearchDungeonMatch(GameSession session, byte kind, bool picked, List<DungeonRoomMetadata?> candidates, short level) {
        if (DungeonMatchRules.InsideDungeon(MemberDungeonIds(session))) {
            session.Send(DungeonMatchPacket.Error(DungeonMatchError.s_dungeonMatch_error_insideDungeonUser));
            return;
        }

        int members = session.Party.Party?.Members.Count ?? 1;
        IDictionary<int, DungeonEnterLimit> limits = session.Player.Value.Character.DungeonEnterLimits;
        var accepted = new List<DungeonRoomMetadata>();
        var refused = new List<(DungeonMatchError Error, int MaxUsers)>();
        foreach (DungeonRoomMetadata? room in candidates) {
            bool closed = room != null && session.Dungeon.ClosedError(room) != null;
            DungeonEnterLimit? limit = room != null && limits.TryGetValue(room.Id, out DungeonEnterLimit value) ? value : null;
            DungeonMatchError error = DungeonMatchRules.RoomError(room, kind, level, members, closed, limit);
            if (error == DungeonMatchError.ok) {
                accepted.Add(room!);
            } else {
                refused.Add((error, room?.Limit.MaxUserCount ?? 0));
            }
        }

        (DungeonMatchError searchError, int param) = DungeonMatchRules.SearchError(picked, candidates.Count, accepted.Count, refused);
        if (searchError != DungeonMatchError.ok) {
            session.Send(DungeonMatchPacket.Error(searchError, param));
            return;
        }
        RequestDungeonMatch(session, kind, accepted);
    }

    // The dungeon ids of the party members' fields (or the player's alone), as World's member info carries them.
    private static IEnumerable<int> MemberDungeonIds(GameSession session) {
        if (session.Party.Party != null) {
            return session.Party.Party.Members.Values.Select(member => member.Info.DungeonId).ToArray();
        }
        return [session.Field?.DungeonId ?? session.Player.Value.Character.DungeonId];
    }

    // Party 34: cancel the search; the byte is the match kind. Any member can send it; World takes the leader's
    // (rule 2: the party goes with the members found so far when they are enough) and answers others with error 40.
    private void HandleCancelPartySearch(GameSession session, IByteReader packet) {
        byte kind = packet.ReadByte();
        if (session.Party.Party == null) {
            return;
        }

        try {
            PartyResponse response = World.Party(new PartyRequest {
                RequestorId = session.CharacterId,
                CancelDungeonMatch = new PartyRequest.Types.CancelDungeonMatch {
                    PartyId = session.Party.Id,
                    Kind = kind,
                },
            });
            if (response.Error != (int) PartyError.none) {
                session.Send(PartyPacket.Error((PartyError) response.Error));
            }
        } catch (RpcException) { }
    }

    private void RequestDungeonMatch(GameSession session, byte kind, List<DungeonRoomMetadata> rooms) {
        if (kind == 0 || rooms.Count == 0) {
            return;
        }

        var match = new PartyRequest.Types.DungeonMatch { Kind = kind };
        match.Rooms.AddRange(rooms.Select(room => new PartyRequest.Types.DungeonMatch.Types.Room {
            Id = room.Id,
            MinUsers = room.Limit.MinUserCount,
            MaxUsers = room.Limit.MaxUserCount,
            ChangeMaxUsers = room.Limit.ChangeMaxUsers,
        }));

        PartyResponse response;
        try {
            response = World.Party(new PartyRequest {
                RequestorId = session.CharacterId,
                DungeonMatch = match,
            });
        } catch (RpcException) {
            return;
        }
        // World tells every member, a joiner or mover too, once it has loaded the party (LoadParty, DungeonMatch,
        // SetDungeon).
        if (response.Error != (int) PartyError.none) {
            session.Send(PartyPacket.Error((PartyError) response.Error));
        }
    }

    // The lowest level and gear score among the party (or the player alone): the client's GetAvailableRooms.
    private static (short Level, int GearScore) LowestMember(GameSession session) {
        if (session.Party.Party != null) {
            PlayerInfo[] infos = session.Party.Party.Members.Values.Select(member => member.Info).ToArray();
            if (infos.Length > 0) {
                return (infos.Min(info => info.Level), infos.Min(info => info.GearScore));
            }
        }
        return session.PlayerInfo.GetOrFetch(session.CharacterId, out PlayerInfo? self)
            ? (self.Level, self.GearScore)
            : (session.Player.Value.Character.Level, 0);
    }

    // The party list offers the vote kick (popup menu 0xcc) only while PartyVoteKick (id 224, 0xe0) is on
    // (UIPopUpMenu, uipartydialog.cpp:2940 and 2951); off, the request is dropped.
    public const string VoteKickFeature = "PartyVoteKick";

    private void HandleVoteKick(GameSession session, IByteReader packet) {
        long targetCharacterId = packet.ReadLong();
        if (!session.ServerTableMetadata.FeatureTable.IsOn(VoteKickFeature)) {
            return;
        }
        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        if (targetCharacterId == session.CharacterId) {
            return;
        }

        if (session.Party.GetMember(targetCharacterId) == null) {
            return;
        }

        if (session.Party.Party.LastVoteTime.FromEpochSeconds().AddSeconds(Constants.PartyVoteReadyDurationSeconds) >
            DateTime.Now && session.Party.Party.Vote != null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_already_vote));
            return;
        }

        if (session.Party.Party.Members.Values.Count(member => member.Info.Online) < 2 ||
            session.Party.Party.Members.Count < 4) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_vote_need_more_people));
            return;
        }

        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            VoteKick = new PartyRequest.Types.VoteKick {
                PartyId = session.Party.Id,
                TargetUserId = targetCharacterId,
            },
        });
        var error = (PartyError) response.Error;
        if (error != PartyError.none) {
            session.Send(PartyPacket.Error(error));
        }
    }

    private void HandleReadyCheck(GameSession session) {
        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        if (session.Party.Party?.LeaderCharacterId != session.CharacterId) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_chief));
            return;
        }

        if (session.Party.Party.LastVoteTime.FromEpochSeconds().AddSeconds(Constants.PartyVoteReadyDurationSeconds) >
            DateTime.Now && session.Party.Party.Vote != null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_already_vote));
            return;
        }

        if (session.Party.Party.Members.Values.Count(member => member.Info.Online) < 2) {
            return;
        }

        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            ReadyCheck = new PartyRequest.Types.ReadyCheck {
                PartyId = session.Party.Id,
            },
        });

        var error = (PartyError) response.Error;
        if (error != PartyError.none) {
            session.Send(PartyPacket.Error(error));
        }
    }

    private void HandleVoteReply(GameSession session, IByteReader packet) {
        int voteId = packet.ReadInt();
        bool isReady = packet.ReadBool();

        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            VoteReply = new PartyRequest.Types.VoteReply {
                PartyId = session.Party.Id,
                Reply = isReady,
                VoteId = voteId,
            },
        });

        var error = (PartyError) response.Error;
        if (error != PartyError.none) {
            session.Send(PartyPacket.Error(error));
        }
    }
}

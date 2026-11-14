using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Party;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class PartySearchHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.PartySearch;

    private enum Command : byte {
        Add = 0,
        Remove = 1,
        Load = 2,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    public required BanWordStorage BanWordStorage { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Add:
                HandleAdd(session, packet);
                break;
            case Command.Remove:
                HandleRemove(session);
                break;
            case Command.Load:
                HandleLoad(session, packet);
                break;
        }
    }

    private void HandleAdd(GameSession session, IByteReader packet) {
        // Read and checked before a party is made for the listing, so a refused request leaves none behind.
        (string listingName, bool noApproval, int size) = PartySearchRules.ReadAdd(packet);
        if (!PartySearchRules.ValidAdd(listingName, size, session.ServerTableMetadata.ConstantsTable.PartySearchRegisterComboValues)) {
            session.Send(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_invalid_type));
            return;
        }
        // A banned title: 108 with request type 0 (the title); checked before a party is made for it.
        if (BanWordStorage.ContainsBannedWord(listingName)) {
            session.Send(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_banword_title, 0));
            return;
        }

        int madePartyId = 0;
        if (session.Party.Party == null) {
            // Create new party
            PartyResponse partyResponse;
            try {
                partyResponse = World.Party(new PartyRequest {
                    RequestorId = session.CharacterId,
                    Create = new PartyRequest.Types.Create { },
                });
            } catch (RpcException ex) {
                WorldDown(session, ex, "create party");
                return;
            }

            var error = (PartyError) partyResponse.Error;
            if (error != PartyError.none) {
                session.Send(PartyPacket.Error(error));
                return;
            }

            session.Party.SetParty(partyResponse.Party, joinNotify: true);
            madePartyId = partyResponse.Party.Id;
        }

        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        if (session.Party.Party.Search != null) {
            session.Send(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_already_register));
            return;
        }

        if (session.Party.Party.LeaderCharacterId != session.CharacterId) {
            session.Send(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_not_chief));
            return;
        }

        if (session.Party.Party.Members.Count >= size) {
            session.Send(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_max_member));
            return;
        }

        PartySearchResponse? response = TryPartySearch(session, new PartySearchRequest {
            Create = new PartySearchRequest.Types.Create {
                Name = listingName,
                NoApproval = noApproval,
                Size = size,
                RequestorId = session.CharacterId,
            },
            PartyId = session.Party.Party.Id,
        });
        if (response == null) {
            UndoMadeParty(session, madePartyId);
            return;
        }

        if (response.Error != (int) PartySearchError.none) {
            session.Send(PartySearchPacket.Error((PartySearchError) response.Error, response.ErrorCategory));
            UndoMadeParty(session, madePartyId);
        }
    }

    // A party made for a listing that was not made goes again: the lone leader leaves it on the World (which ends a
    // party of one), and the session drops it with a Disband so the client's party goes too.
    private void UndoMadeParty(GameSession session, int partyId) {
        if (partyId == 0 || session.Party.Party?.Id != partyId || session.Party.Party.Members.Count > 1) {
            return;
        }
        try {
            World.Party(new PartyRequest {
                RequestorId = session.CharacterId,
                Leave = new PartyRequest.Types.Leave {
                    PartyId = partyId,
                },
            });
        } catch (RpcException ex) {
            Logger.Error(ex, "Party search: leaving the party made for a failed listing failed for {CharacterId}", session.CharacterId);
        }
        if (session.Party.Party?.Id == partyId) {
            session.Party.Disband(notify: true);
        }
    }

    private void HandleRemove(GameSession session) {
        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        if (session.Party.Party.LeaderCharacterId != session.CharacterId) {
            session.Send(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_not_chief));
            return;
        }

        if (session.Party.Party.Search == null) {
            session.Send(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_not_find_recruit, 1));
            return;
        }

        long partySearchId = session.Party.Party.Search.Id;
        PartySearchResponse? response = TryPartySearch(session, new PartySearchRequest {
            Remove = new PartySearchRequest.Types.Remove(),
            PartyId = session.Party.Party.Id,
            Id = session.Party.Party.Search.Id,
        });
        if (response == null) {
            return;
        }

        if (response.Error != (int) PartySearchError.none) {
            session.Send(PartySearchPacket.Error((PartySearchError) response.Error, response.ErrorCategory));
            return;
        }

        session.Send(PartySearchPacket.Remove(partySearchId));

        if (session.Party.Party.Members.Count <= 1) {
            // The listing is gone already; a party left behind by an unreachable world is only logged.
            try {
                World.Party(new PartyRequest {
                    RequestorId = session.CharacterId,
                    Leave = new PartyRequest.Types.Leave {
                        PartyId = session.Party.Id,
                    },
                });
            } catch (RpcException ex) {
                Logger.Error(ex, "Party search: leaving the listing's party failed for {CharacterId}", session.CharacterId);
            }
        }
    }

    private void HandleLoad(GameSession session, IByteReader packet) {
        int unknown = packet.ReadInt();
        int unknown2 = packet.ReadInt();
        byte sort = packet.ReadByte();
        string searchString = packet.ReadUnicodeString();
        int pageNumber = packet.ReadInt();
        int unknown3 = packet.ReadInt();

        if (!Enum.IsDefined(typeof(PartySearchSort), sort)) {
            session.Send(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_invalid_type));
            return;
        }
        // A banned search word: 108 with request type 2 (the search word).
        if (searchString.Length > 0 && BanWordStorage.ContainsBannedWord(searchString)) {
            session.Send(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_banword_findword, 2));
            return;
        }

        PartySearchResponse? response = TryPartySearch(session, new PartySearchRequest {
            Fetch = new PartySearchRequest.Types.Fetch {
                Page = pageNumber,
                SearchString = searchString,
                SortBy = sort,
            },
        });
        if (response == null) {
            return;
        }

        if (response.Error != (int) PartySearchError.none) {
            session.Send(PartySearchPacket.Error((PartySearchError) response.Error, response.ErrorCategory));
            return;
        }

        ICollection<PartySearch> entries = response.PartySearches.Select(info => new PartySearch(info.Id, info.Name, info.Size) {
            Id = info.Id,
            PartyId = info.PartyId,
            CreationTime = info.CreationTime,
            LeaderAccountId = info.LeaderAccountId,
            LeaderCharacterId = info.LeaderCharacterId,
            LeaderName = info.LeaderName,
            MemberCount = info.MemberCount,
            Name = info.Name,
            NoApproval = info.NoApproval,
            Size = info.Size,
        }).ToList();

        session.Send(PartySearchPacket.Load(entries));
    }

    // A World server that cannot be reached would end the session (Session.StartInternal); the player gets the
    // client's database error (2) instead.
    private PartySearchResponse? TryPartySearch(GameSession session, PartySearchRequest request) {
        try {
            return World.PartySearch(request);
        } catch (RpcException ex) {
            WorldDown(session, ex, request.PartySearchCase.ToString());
            return null;
        }
    }

    private void WorldDown(GameSession session, RpcException ex, string what) {
        Logger.Error(ex, "Party search {What} failed for {CharacterId}", what, session.CharacterId);
        session.Send(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_db));
    }
}

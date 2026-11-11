using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;

namespace Maple2.Server.World.Containers;

public class PartyLookup : IDisposable {
    #region Autofac Autowired
    private readonly ChannelClientLookup channelClients;
    private readonly PlayerInfoLookup playerLookup;
    private readonly PartySearchLookup partySearchLookup;
    private readonly ServerTableMetadataStorage serverTableMetadata;
    #endregion

    private readonly ConcurrentDictionary<int, PartyManager> parties;
    // The Party 32/33 searches. Every matchmaker step (a search, a cancel, a member count change of a searching
    // party) runs under DungeonMatchLock, so a join and the room made for the group it fills do not interleave.
    public DungeonMatchQueue DungeonMatch { get; } = new();
    public readonly object DungeonMatchLock = new();
    private int nextPartyId = 1;

    public PartyLookup(ChannelClientLookup channelClients, PlayerInfoLookup playerLookup, PartySearchLookup partySearchLookup, ServerTableMetadataStorage serverTableMetadata) {
        this.channelClients = channelClients;
        this.playerLookup = playerLookup;
        this.partySearchLookup = partySearchLookup;
        this.serverTableMetadata = serverTableMetadata;

        parties = new ConcurrentDictionary<int, PartyManager>();
    }

    public void Dispose() {
        foreach (PartyManager manager in parties.Values) {
            manager.Dispose();
        }

        if (partySearchLookup.TryGetByPartyId(nextPartyId, out PartySearchManager? partySearch)) {
            partySearch.Dispose();
        }
    }

    public bool TryGet(int partyId, [NotNullWhen(true)] out PartyManager? party) {
        return parties.TryGetValue(partyId, out party);
    }

    public bool TryGetByCharacter(long characterId, [NotNullWhen(true)] out PartyManager? party) {
        party = null;
        foreach (PartyManager manager in parties.Values) {
            if (manager.Party.Members.TryGetValue(characterId, out PartyMember? member)) {
                party = manager;
                return true;
            }
        }
        return false;
    }

    public PartyError Create(long leaderId, out int partyId) {
        partyId = nextPartyId++;

        PlayerInfo? leaderInfo = playerLookup.GetPlayerInfo(leaderId);
        if (leaderInfo == null) {
            return PartyError.s_party_err_not_found;
        }

        var party = new Party(partyId, leaderInfo.AccountId, leaderInfo.CharacterId, leaderInfo.Name);
        var manager = new PartyManager(party) {
            ChannelClients = channelClients,
            PartyLookup = this,
            ServerTableMetadata = serverTableMetadata
        };

        if (!parties.TryAdd(partyId, manager)) {
            return PartyError.s_party_err_not_found;
        }

        return manager.Join(leaderInfo);
    }

    public PartyError Disband(int partyId) {
        if (!TryGet(partyId, out PartyManager? manager)) {
            return PartyError.s_party_err_not_found;
        }

        if (!parties.TryRemove(partyId, out manager)) {
            // Failed to remove party after validating.
            return PartyError.s_party_err_not_found;
        }
        lock (DungeonMatchLock) {
            DungeonMatch.Remove(partyId);
        }
        manager.Dispose();

        return PartyError.none;
    }

    // A member joined or left a party: a searching party's group takes the new count.
    public void DungeonMatchResize(PartyManager manager) {
        lock (DungeonMatchLock) {
            DungeonMatchResult result = DungeonMatch.Resize(manager.Party.Id, manager.OnlineMembers());
            if (result.Outcome != DungeonMatchOutcome.NotSearching) {
                CompleteDungeonMatch(manager, result);
            }
        }
    }

    // What a queue step leaves to the party: a matched group gets its room (made on the first channel, as the
    // leader's CreateDungeon would) and the enter box; a stopped one is told the search ended. Searching: nothing.
    // False when the room could not be made (the search has ended all the same).
    public bool CompleteDungeonMatch(PartyManager manager, DungeonMatchResult result) {
        lock (DungeonMatchLock) {
            switch (result.Outcome) {
                case DungeonMatchOutcome.Matched:
                    BroadcastDungeonMatch(manager, result.Kind, false);
                    int lobbyRoomId = CreateMatchedRoom(manager, result.RoomId);
                    if (lobbyRoomId == 0) {
                        return false;
                    }
                    manager.SetDungeon(manager.Party.LeaderCharacterId, result.RoomId, true, lobbyRoomId, matched: true);
                    return true;
                case DungeonMatchOutcome.Stopped:
                    BroadcastDungeonMatch(manager, result.Kind, false);
                    return true;
                default:
                    return true;
            }
        }
    }

    public static void BroadcastDungeonMatch(PartyManager manager, byte kind, bool registered) {
        manager.Broadcast(new Channel.Service.PartyRequest {
            DungeonMatch = new Channel.Service.PartyRequest.Types.DungeonMatch {
                Kind = kind,
                Registered = registered,
            },
        });
    }

    private int CreateMatchedRoom(PartyManager manager, int dungeonId) {
        if (!channelClients.TryGetClient(channelClients.FirstChannel(), out Channel.Service.Channel.ChannelClient? client)) {
            return 0;
        }
        try {
            FieldResponse response = client.Field(new FieldRequest {
                RequesterId = manager.Party.LeaderCharacterId,
                CreateDungeon = new FieldRequest.Types.CreateDungeon {
                    DungeonId = dungeonId,
                    Size = manager.Party.Members.Count,
                    PartyId = manager.Party.Id,
                },
            });
            return response.Error == (int) MigrationError.ok ? response.RoomId : 0;
        } catch (RpcException) {
            return 0;
        }
    }
}

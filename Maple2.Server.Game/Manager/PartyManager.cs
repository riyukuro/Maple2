using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util.Sync;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.Manager;

public class PartyManager : IDisposable {
    private readonly GameSession session;
    private readonly WorldClient world;

    public Party? Party { get; private set; }
    public int Id => Party?.Id ?? 0;

    private readonly CancellationTokenSource tokenSource;

    private readonly ILogger logger = Log.Logger.ForContext<PartyManager>();

    public PartyManager(WorldClient world, GameSession session) {
        this.session = session;
        this.world = world;
        tokenSource = new CancellationTokenSource();

        PartyInfoResponse response = session.World.PartyInfo(new PartyInfoRequest {
            CharacterId = session.CharacterId,
        });
        if (response.Party != null) {
            SetParty(response.Party);
        }
    }

    // How long a recall offer stays open. The server pack has no RecallPartyPortalKeepTime row; this is a guess.
    private const int RecallKeepSeconds = 300;

    /// <summary>
    /// Offers a party recall to the other members (s2c RecallScroll on their channels).
    /// False when there is no one to call, the field forbids recalls or the World refuses.
    /// </summary>
    public bool Recall() {
        if (Party == null || session.Field == null) {
            return false;
        }
        // UIPartyDialog checks the field's cashCall recall flag and that someone else is in the party.
        if (!session.Field.Metadata.CashCall.Recall || Party.Members.Count <= 1) {
            return false;
        }

        try {
            PartyResponse response = world.Party(new PartyRequest {
                RequestorId = session.CharacterId,
                Recall = new PartyRequest.Types.Recall {
                    PartyId = Party.Id,
                    MapId = session.Field.MapId,
                    EndTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + RecallKeepSeconds,
                },
            });
            return response.Error == (int) PartyError.none;
        } catch (Grpc.Core.RpcException ex) {
            logger.Error(ex, "Party recall by {CharacterId} failed", session.CharacterId);
            return false;
        }
    }

    public void Dispose() {
        session.Dispose();
        tokenSource.Dispose();

        if (Party == null) {
            return;
        }

        foreach (PartyMember member in Party.Members.Values) {
            member.Dispose();
        }

        if (Party.Vote != null) {
            if (!Party.Vote.Approvals.Contains(session.CharacterId) && !Party.Vote.Disapprovals.Contains(session.CharacterId)) {
                world.Party(new PartyRequest {
                    RequestorId = session.CharacterId,
                    VoteReply = new PartyRequest.Types.VoteReply {
                        PartyId = Party.Id,
                        Reply = false,
                    },
                });
            }
        }
    }

    // One Load per party: KMS2 sends a single Load, with the join byte set, to a player who makes or joins a party.
    public bool SetParty(PartyInfo info, bool joinNotify = false) {
        if (Party != null) {
            return false;
        }

        PartyMember[] members = info.Members.Select(member => {
            if (!session.PlayerInfo.GetOrFetch(member.CharacterId, out PlayerInfo? playerInfo)) {
                return null;
            }

            var result = new PartyMember {
                PartyId = info.Id,
                Info = playerInfo.Clone(),
                JoinTime = member.JoinTime,
            };
            return result;
        }).WhereNotNull().ToArray();

        PartyMember? leader = members.SingleOrDefault(member => member.CharacterId == info.LeaderCharacterId);
        if (leader == null) {
            logger.Error("Party {PartyId} does not have a valid leader", info.Id);
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return false;
        }

        var party = new Party(info.Id, leader) {
            CreationTime = info.CreationTime,
        };
        ApplyInfo(party, info);
        Party = party;
        foreach (PartyMember member in members) {
            party.Members.TryAdd(member.CharacterId, member);
        }

        session.Send(PartyPacket.Load(Party, joinNotify));

        session.Dungeon.SetDungeon(Party.DungeonId, Party.DungeonLobbyRoomId, Party.DungeonSet);

        // Listening happens after loading the party
        foreach (PartyMember member in party.Members.Values) {
            BeginListen(member);
        }

        session.Player.Value.Character.PartyId = Party.Id;

        if (info.PartySearch != null) {
            Party.Search = new PartySearch(info.PartySearch.Id, info.PartySearch.Name, info.PartySearch.Size) {
                PartyId = Party.Id,
                CreationTime = info.PartySearch.CreationTime,
                NoApproval = info.PartySearch.NoApproval,
                LeaderAccountId = info.PartySearch.LeaderAccountId,
                LeaderCharacterId = info.PartySearch.LeaderCharacterId,
                LeaderName = info.PartySearch.LeaderName,
                MemberCount = info.PartySearch.MemberCount,
            };
        }

        return true;
    }

    // The party state World sends with the party: the dungeon set and the dungeon match search, so a member who
    // loads the party (relog, channel change, invite) during a search sees it in the Load bits.
    public static void ApplyInfo(Party party, PartyInfo info) {
        party.DungeonSet = info.DungeonSet;
        party.DungeonId = info.DungeonId;
        party.DungeonLobbyRoomId = info.DungeonRoomId;
        party.DungeonMatchKind = (byte) info.DungeonMatchKind;
    }

    public void RemoveParty() {
        if (Party == null) {
            return;
        }

        Party = null;
        session.Player.Value.Character.PartyId = 0;
    }

    public bool AddMember(PartyMember member) {
        if (Party == null) {
            return false;
        }
        if (!Party.Members.TryAdd(member.CharacterId, member)) {
            return false;
        }

        BeginListen(member);
        session.Send(PartyPacket.Joined(member));
        return true;
    }

    public bool RemoveMember(long characterId, bool isKick, bool isSelf) {
        if (Party == null) {
            return false;
        }
        if (!Party.Members.TryRemove(characterId, out PartyMember? member)) {
            return false;
        }
        EndListen(member);

        if (Party.Vote != null && Party.Vote.TargetMember?.CharacterId == characterId) {
            EndVote();
        }

        session.Send(isKick ? PartyPacket.Kicked(member.CharacterId) : PartyPacket.Leave(member.CharacterId, isSelf));
        return true;
    }

    public bool UpdateLeader(long newLeaderCharacterId) {
        if (Party == null) {
            return false;
        }

        PartyMember? leader = Party.Members.Values.SingleOrDefault(member => member.CharacterId == newLeaderCharacterId);
        if (leader == null) {
            logger.Error("Party {PartyId} does not have a valid leader", Party.Id);
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return false;
        }

        Party.LeaderCharacterId = leader.CharacterId;
        Party.LeaderAccountId = leader.Info.AccountId;
        Party.LeaderName = leader.Info.Name;

        session.Send(PartyPacket.NotifyUpdateLeader(newLeaderCharacterId));
        return true;
    }

    // notify: tell the client even for a party of one (World's dungeon match party of one whose search ended).
    public bool Disband(bool notify = false) {
        if (Party == null) {
            return false;
        }

        foreach (PartyMember member in Party.Members.Values) {
            EndListen(member);
        }

        if (Party.Members.Count > 1 || notify) {
            session.Send(PartyPacket.Disband());
        }
        RemoveParty();
        return true;
    }

    // World moved this player into a party (a dungeon match join or merge, or the party of one made for a solo
    // searcher): the old party goes without a packet (the client's Load replaces it) and the new one is loaded.
    public void LoadMoved(int partyId) {
        if (Party != null) {
            foreach (PartyMember member in Party.Members.Values) {
                EndListen(member);
            }
            RemoveParty();
        }

        PartyInfoResponse response = world.PartyInfo(new PartyInfoRequest { PartyId = partyId });
        if (response.Party != null) {
            SetParty(response.Party, joinNotify: true);
        }
    }

    public void CheckDisband() {
        if (Party == null) {
            return;
        }

        world.Party(new PartyRequest {
            Disband = new PartyRequest.Types.Disband {
                PartyId = Id,
            },
            RequestorId = session.CharacterId,
        });
    }

    public void StartReadyCheck(long characterId) {
        if (Party == null) {
            return;
        }

        Party.LastVoteTime = DateTime.Now.ToEpochSeconds();
        Party.Vote = new PartyVote(PartyVoteType.ReadyCheck, Party.Members.Keys, characterId) {
            EndTime = VoteEndTime(Party.LastVoteTime),
        };
        session.Send(PartyPacket.StartVote(Party.Vote));
    }

    public void ReadyCheckReply(long characterId, bool ready) {
        if (Party?.Vote == null) {
            return;
        }

        if (!Party.Members.TryGetValue(characterId, out PartyMember? member)) {
            return;
        }

        if (Party.Vote.Approvals.Contains(characterId) ||
            Party.Vote.Disapprovals.Contains(characterId)) {
            return;
        }

        if (ready) {
            Party.Vote.Approvals.Add(characterId);
        } else {
            Party.Vote.Disapprovals.Add(characterId);
        }

        session.Send(PartyPacket.ReadyCheck(characterId, ready));
    }

    public void EndVote() {
        if (Party?.Vote == null) {
            return;
        }

        if (Party.Vote.Type == PartyVoteType.Kick &&
            Party.Vote.VotesNeeded > Party.Vote.Approvals.Count) {
            session.Send(PartyPacket.PartyNotice(PartyMessage.s_party_vote_rejected_kick_user));
        }

        session.Send(PartyPacket.EndVote());
        Party.Vote = null;
    }

    public void ExpiredVote() {
        if (Party?.Vote == null) {
            return;
        }

        session.Send(PartyPacket.PartyNotice(PartyMessage.s_party_vote_expired));
        EndVote();
    }

    public void StartVoteKick(long requestorId, long targetId, ICollection<long> receiverIds) {
        if (Party == null) {
            return;
        }

        if (!Party.Members.TryGetValue(targetId, out PartyMember? target)) {
            return;
        }

        Party.LastVoteTime = DateTime.Now.ToEpochSeconds();
        Party.Vote = new PartyVote(PartyVoteType.Kick, receiverIds, requestorId) {
            TargetMember = target,
            EndTime = VoteEndTime(Party.LastVoteTime),
        };
        session.Send(PartyPacket.StartVote(Party.Vote));
    }

    // World ends both kinds of vote after PartyVoteReadyDurationSeconds.
    private long VoteEndTime(long startTime) =>
        startTime + session.ServerTableMetadata.ConstantsTable.PartyVoteReadyDurationSeconds;

    public void SetDungeon(int dungeonId, int dungeonRoomId, bool set, bool matched = false) {
        if (Party == null) {
            return;
        }
        Party.DungeonId = dungeonId;
        Party.DungeonLobbyRoomId = dungeonRoomId;
        Party.DungeonSet = set;
        session.Dungeon.SetDungeon(dungeonId, dungeonRoomId, set);

        session.Send(PartyPacket.DungeonReset(set, dungeonId));
        if (set && dungeonId != 0 && matched) {
            // The dungeon matchmaker made the room: every member, the leader too, gets the match box, whose OK
            // enters the party's room (RoomDungeon 3).
            session.Send(PartyPacket.PartySearchDungeon());
        } else if (set && dungeonId != 0 && Party.LeaderCharacterId != session.CharacterId) {
            // The leader made a dungeon room: the client asks this member whether to enter it.
            session.Send(PartyPacket.DungeonRoomCreate(dungeonId));
        }
        if (Party.DungeonId == 0) {
            session.Send(PartyPacket.PartyNotice(PartyMessage.s_field_enteracne_party_notify_reset_dungeon, "Field_Enterance_Reset_Dungeon"));
        }
    }

    // The party's dungeon match search started or ended (World DungeonMatch).
    public void SetDungeonMatch(byte kind, bool registered) {
        if (Party == null) {
            return;
        }
        Party.DungeonMatchKind = registered ? kind : (byte) 0;
        session.Send(PartyPacket.PartySearch(kind, registered));
    }

    public int GuildMemberCount() {
        return Party?.Members.Values.Count(member => member.Info.GuildId == session.Player.Value.Character.GuildId) ?? 0;
    }

    public void SetPartySearch(PartySearch? partySearch) {
        if (Party == null) {
            return;
        }

        if (partySearch != null && Party.Id != partySearch.PartyId) {
            logger.Error("Party {PartyId} does not match search info {SearchPartyId}", Party.Id, partySearch.PartyId);
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        Party.Search = partySearch;
        session.Send(PartyPacket.LoadPartySearchListing(Party));
    }

    public PartyMember? GetMember(string name) {
        return Party?.Members.Values.FirstOrDefault(member => member.Name == name);
    }

    public PartyMember? GetMember(long characterId) {
        if (Party?.Members.TryGetValue(characterId, out PartyMember? member) == true) {
            return member;
        }

        return null;
    }

    #region PlayerInfo Events
    private void BeginListen(PartyMember member) {
        // Clean up previous token if necessary
        if (member.TokenSource != null) {
            logger.Warning("BeginListen called on Member {Id} that was already listening", member.CharacterId);
            EndListen(member);
        }

        member.TokenSource = CancellationTokenSource.CreateLinkedTokenSource(tokenSource.Token);
        CancellationToken token = member.TokenSource.Token;
        var listener = new PlayerInfoListener(UpdateField.Party, (type, info) => SyncUpdate(token, member.CharacterId, type, info));
        session.PlayerInfo.Listen(member.Info.CharacterId, listener);
    }

    private void EndListen(PartyMember member) {
        member.TokenSource?.Cancel();
        member.TokenSource?.Dispose();
        member.TokenSource = null;
    }

    // Two dungeon limit tables with the same dungeons and the same limit for each.
    internal static bool SameLimits(IReadOnlyDictionary<int, DungeonEnterLimit> before, IReadOnlyDictionary<int, DungeonEnterLimit> after) {
        if (before.Count != after.Count) {
            return false;
        }
        foreach ((int dungeonId, DungeonEnterLimit limit) in before) {
            if (!after.TryGetValue(dungeonId, out DungeonEnterLimit other) || other != limit) {
                return false;
            }
        }
        return true;
    }

    // The party is told once that a member is under a tombstone: when the member goes from alive to dead.
    internal static bool DiedNow(DeathState before, DeathState after) {
        return before == DeathState.Alive && after != DeathState.Alive;
    }

    private bool SyncUpdate(CancellationToken cancel, long id, UpdateField type, IPlayerInfo info) {
        if (cancel.IsCancellationRequested || Party == null || !Party.Members.TryGetValue(id, out PartyMember? member)) {
            return true;
        }

        bool wasOnline = member.Info.Online;
        var previousDungeonLimits = new Dictionary<int, DungeonEnterLimit>(member.Info.DungeonEnterLimits);
        DeathState previousDeathState = member.Info.DeathState;

        member.Info.Update(type, info);

        if (type is UpdateField.Health or UpdateField.Death) {
            session.Send(PartyPacket.UpdateStats(member));
        } else {
            session.Send(PartyPacket.Update(member));
        }

        if (DiedNow(previousDeathState, member.Info.DeathState)) {
            session.Send(PartyPacket.Tombstone(member.CharacterId, member.Info.DeathState == DeathState.Metal));
        }

        if (!SameLimits(previousDungeonLimits, member.Info.DungeonEnterLimits)) {
            session.Send(PartyPacket.UpdateDungeonInfo(member));
        }

        if (session.CharacterId != member.CharacterId && member.Info.Online != wasOnline) {
            session.Send(member.Info.Online
                ? PartyPacket.NotifyLogin(member)
                : PartyPacket.NotifyLogout(member.CharacterId));
        }
        return false;
    }
    #endregion
}

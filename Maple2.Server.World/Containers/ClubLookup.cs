using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Club;
using Serilog;

namespace Maple2.Server.World.Containers;

public class ClubLookup : IDisposable {
    private readonly ILogger logger = Log.ForContext<ClubLookup>();
    private readonly GameStorage gameStorage;
    private readonly ChannelClientLookup channelClients;
    private readonly PlayerInfoLookup playerLookup;
    private readonly PartyLookup partyLookup;

    private readonly ConcurrentDictionary<long, ClubManager> clubs;

    public ClubLookup(ChannelClientLookup channelClients, PlayerInfoLookup playerLookup, GameStorage gameStorage, PartyLookup partyLookup) {
        this.gameStorage = gameStorage;
        this.channelClients = channelClients;
        this.playerLookup = playerLookup;
        this.partyLookup = partyLookup;

        clubs = new ConcurrentDictionary<long, ClubManager>();
    }

    public void Dispose() {
        // We must dispose all ClubManager to save state.
        foreach (ClubManager manager in clubs.Values) {
            manager.Dispose();
        }
    }

    public bool TryGet(long clubId, [NotNullWhen(true)] out ClubManager? club) {
        if (clubs.TryGetValue(clubId, out club)) {
            return true;
        }

        club = FetchClub(clubId);
        return club != null;
    }

    // A logout ends the invites still open to that player; each club that sent one tells its members (code 2).
    public void InviteeLoggedOut(long characterId, string name) {
        foreach (ClubManager manager in clubs.Values) {
            manager.InviteeLoggedOut(characterId, name);
        }
    }

    public List<ClubManager> TryGetByCharacterId(long characterId) {
        List<ClubManager> clubManagers = [];
        using GameStorage.Request db = gameStorage.Context();
        IList<long> clubIds = db.ListClubs(characterId);
        foreach (long clubId in clubIds) {
            if (TryGet(clubId, out ClubManager? club)) {
                // If the club is staged, we can dispose it.
                if (club.Club.State == ClubState.Staged) {
                    club.Dispose();
                    continue;
                }
                clubManagers.Add(club);
            }
        }
        return clubManagers;
    }

    private ClubManager? FetchClub(long clubId) {
        using GameStorage.Request db = gameStorage.Context();
        Club? club = db.GetClub(playerLookup, clubId);
        if (club == null) {
            return null;
        }

        var manager = new ClubManager(club) {
            GameStorage = gameStorage,
            ChannelClients = channelClients,
        };

        return clubs.TryAdd(clubId, manager) ? manager : null;
    }

    public ClubError Create(string name, long leaderId, out long clubId) {
        clubId = 0;
        using GameStorage.Request db = gameStorage.Context();
        if (db.ClubExists(name)) {
            return ClubError.s_club_err_name_exist;
        }

        if (!partyLookup.TryGetByCharacter(leaderId, out PartyManager? party)) {
            return ClubError.s_club_err_unknown;
        }

        Club? club = db.CreateClub(playerLookup, name, leaderId, party.Party.Members.Values.Select(member => member.Info).ToList());
        if (club == null) {
            return ClubError.s_club_err_unknown;
        }

        clubId = club.Id;
        clubs.TryAdd(clubId, new ClubManager(club) {
            GameStorage = gameStorage,
            ChannelClients = channelClients,
        });

        return ClubError.none;
    }

    // The database first: a failed delete leaves the club in memory and on every channel as it was.
    public ClubError Disband(long clubId) {
        if (!TryGet(clubId, out ClubManager? manager)) {
            return ClubError.s_club_err_null_club;
        }

        using (GameStorage.Request db = gameStorage.Context()) {
            if (!db.DeleteClub(clubId)) {
                return ClubError.s_club_err_unknown;
            }
        }

        clubs.TryRemove(clubId, out _);
        manager.Disband();
        return ClubError.none;
    }

    // The character is about to be deleted: it leaves each of its clubs as a member who leaves would (a club of two
    // is disbanded, a leader hands the club to another member) and the other members get Club 0x1c instead of 0x12.
    // A staged club is disbanded, as a declined invite disbands it: it could never reach all its members' accepts.
    // No club blocks the deletion: a club that fails to load or to change is logged and forgotten here, so it loads
    // again from the database as the deletion leaves it (its member rows go with the character, and a club the
    // character still leads goes too). A club whose stored leader is no member loads with another member leading.
    public void RemoveCharacter(long characterId) {
        IList<long> clubIds;
        using (GameStorage.Request db = gameStorage.Context()) {
            clubIds = db.ListClubs(characterId);
        }

        foreach (long clubId in clubIds) {
            ClubError error;
            try {
                if (!TryGet(clubId, out ClubManager? manager)) {
                    logger.Warning("Delete of character {CharacterId}: club {ClubId} did not load", characterId, clubId);
                    continue;
                }

                error = manager.Club.State == ClubState.Staged || manager.Club.Members.Count <= 2
                    ? Disband(clubId)
                    : manager.Leave(characterId, deleted: true);
            } catch (Exception ex) {
                logger.Warning(ex, "Delete of character {CharacterId}: club {ClubId} failed", characterId, clubId);
                error = ClubError.s_club_err_unknown;
            }

            if (error != ClubError.none) {
                logger.Warning("Delete of character {CharacterId}: club {ClubId} kept it ({Error})", characterId, clubId, error);
                clubs.TryRemove(clubId, out _);
            }
        }
    }

}

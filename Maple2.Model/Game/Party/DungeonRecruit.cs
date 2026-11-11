using Maple2.Model.Error;
using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

// The recruiting notice's two forms (DungeonHelp 0x0109, UIMessengerBrowser::FUN_141a23060, uimessenger.cpp:3003):
// mode 0 asks for helpers (RoomDungeon 15), mode 1 for members (RoomDungeon 16).
public enum DungeonRecruitKind : byte {
    Helper = 0,
    Member = 1,
}

// The room a party recruits for, with the limits a joiner is held to.
public readonly record struct DungeonRecruitRoom(int Id, int MinLevel, int GearScore, int MaxUsers) {
    public static DungeonRecruitRoom Of(DungeonRoomMetadata room) =>
        new(room.Id, room.Limit.MinLevel, room.Limit.GearScore, room.Limit.MaxUserCount);
}

public sealed record DungeonRecruit(int PartyId, DungeonRecruitKind Kind, DungeonRecruitRoom Room, DateTime OpenedAt);

// The coordinator's rules for RoomDungeon 15/16 and Party 39/43 (r63).
public static class DungeonRecruitRules {
    // Between two notices of one party (the client's own wait after a notice is 10 s, CPartyManager::SetPartyDeadline).
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);
    // A recruit closes this long after its notice (chosen here).
    public static readonly TimeSpan Open = TimeSpan.FromSeconds(60);

    // Mode 1's needMember: the room's places left, counting every member, online or not (chosen here).
    public static int NeedMembers(DungeonRecruitRoom room, int members) => Math.Max(0, room.MaxUsers - members);

    // A player outside the recruiting party gets the notice: online, at the room's level, and not in a party that
    // is full or matching (a party member could not join anyway).
    public static bool Receives(PlayerInfo player, DungeonRecruitRoom room, bool partyFull, bool partyMatching) =>
        player.Online && player.Level >= room.MinLevel && !partyFull && !partyMatching;

    // Why a player cannot join through the chat link; none: they may.
    public static PartyError RefusesJoin(PlayerInfo player, DungeonRecruitRoom room, int members) {
        if (player.Level < room.MinLevel) {
            return PartyError.s_party_err_lack_level;
        }
        if (room.GearScore > 0 && player.GearScore < room.GearScore) {
            return PartyError.s_party_err_lack_gear_score;
        }
        if (room.MaxUsers > 0 && members >= room.MaxUsers) {
            return PartyError.s_party_err_full_limit_player;
        }
        return PartyError.none;
    }
}

// The open recruits and each party's last notice. Not thread-safe: World uses it inside its matchmaker steps.
public class DungeonRecruitBoard {
    private readonly Func<DateTime> now;
    private readonly Dictionary<int, DungeonRecruit> open = new();
    private readonly Dictionary<int, DateTime> lastNotice = new();

    public DungeonRecruitBoard(Func<DateTime>? now = null) {
        this.now = now ?? (() => DateTime.UtcNow);
    }

    // Zero: the party may send a notice now.
    public TimeSpan CooldownLeft(int partyId) {
        if (!lastNotice.TryGetValue(partyId, out DateTime last)) {
            return TimeSpan.Zero;
        }
        TimeSpan left = last + DungeonRecruitRules.Cooldown - now();
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    // A new notice: it takes the place of the party's open recruit.
    public DungeonRecruit Start(int partyId, DungeonRecruitKind kind, DungeonRecruitRoom room) {
        DateTime at = now();
        var recruit = new DungeonRecruit(partyId, kind, room, at);
        open[partyId] = recruit;
        lastNotice[partyId] = at;
        return recruit;
    }

    // The party's open recruit; one past its time is closed.
    public DungeonRecruit? Get(int partyId) {
        if (!open.TryGetValue(partyId, out DungeonRecruit? recruit)) {
            return null;
        }
        if (now() - recruit.OpenedAt >= DungeonRecruitRules.Open) {
            open.Remove(partyId);
            return null;
        }
        return recruit;
    }

    // The open recruit for this party and room (a chat link names both).
    public DungeonRecruit? Find(int partyId, int roomId) {
        DungeonRecruit? recruit = Get(partyId);
        return recruit?.Room.Id == roomId ? recruit : null;
    }

    public void Close(int partyId) => open.Remove(partyId);

    // The party is gone.
    public void Forget(int partyId) {
        open.Remove(partyId);
        lastNotice.Remove(partyId);
    }
}

namespace Maple2.Model.Game.Pvp;

public enum GuildPvpZoneEventType {
    // A round's fight started (TeamPvp 's' with its end time and fighters; the fighters go into the arena).
    RoundStart,
    // A fighter was out (death, ring-out or leaving the field): TeamPvp 's' again with the fighters left.
    Out,
    // A round ended (its winner team, 0 for a draw): TeamPvp 's' without a time; the fighters left go back.
    RoundEnd,
    // The match ended (its winner team, 0 for a draw): TeamPvp 't', then the World's result.
    End,
}

public readonly record struct GuildPvpZoneEvent(GuildPvpZoneEventType Type, int Team = 0, long CharacterId = 0);

// One round: each team's fighters and the winner team (0 a draw). A walkover (a team had nobody to send) is not
// fought: it has no fighters, and it counts for the match's winner only.
public sealed record GuildPvpRound(IReadOnlyList<long> FightersA, IReadOnlyList<long> FightersB, int Winner, bool Fought = true);

// The guild arena's PvP zone (64000001, set_pvp_zone type 4 with the boxes 102, 103, 112, 113, 10, 11, 1, 3; the
// client's PvP mode 4). From the map and the client: guild 1 waits on its stand (portal 10, box 102 "Guild1_player"
// inside box 112 "Guild1_all"), guild 2 on its own (portal 11, boxes 103 and 113), the fight is in box 101 around
// portal 1; the team dialog shows up to 3 fighters a side, the round number, its end time and each guild's score
// (TeamPvp 's'), the history keeps up to 5 rounds (CGuildPVPHistoryData), and the script's last box id (3) is the
// round wins a guild needs. Chosen here (the client and the data do not settle them): a round's fighters are up to
// FightersPerTeam players of the team standing in its stand box (102, 103), else as many of the team's other players
// in the field; a fighter is out when it dies, leaves the field or leaves the fight box (the ring-out under the
// arena); a team with no fighter left loses the round, and at the round's time (the script's match_time) the team
// with more fighters left wins it, equal counts are a draw round; a team with nobody to send loses the round without
// a fight (a walkover: no fighters, not a fought round); a team that was seen in the field and then has no player
// left in it after the start forfeits the match (both gone, or the other team never seen: a draw), the round it cut
// short going to the other team for the result only (not fought); a team never seen forfeits nothing, it loses its
// rounds by walkover once both were seen or WalkoverGraceMs after the start (so the first guild in an empty field
// wins no fought round, and a late guild still gets to fight); the match ends when a team has the wins it needs or after MaxRounds
// rounds, the team with more round wins winning, equal wins a draw; BreakMs passes between rounds.
public sealed class GuildPvpZoneState {
    public const int NoTeam = 0;
    public const int TeamA = 1;
    public const int TeamB = 2;
    public const int FightersPerTeam = 3;
    public const int MaxRounds = 5;
    public const int DefaultWins = 3;
    public const long BreakMs = 10_000;
    // Chosen here: with present given, no round is given by walkover before both teams were seen or this long after
    // the start, so a guild that accepted the join box late and loads slowly still gets to fight (without it the
    // walkovers at the start, +10 s and +20 s end the match before it arrives).
    public const long WalkoverGraceMs = 60_000;

    public int BoxId { get; }
    public int StandA { get; }
    public int StandB { get; }
    public int AreaA { get; }
    public int AreaB { get; }
    public int PortalA { get; }
    public int PortalB { get; }
    public int ArenaPortal { get; }
    public int WinsNeeded { get; }
    public long RoundMs { get; }

    public long StartTick { get; }
    public int Round { get; private set; }
    public bool Fighting { get; private set; }
    public bool Ended { get; private set; }
    public long RoundEndTick { get; private set; }
    public long LastEndTick { get; private set; }
    public IReadOnlyList<GuildPvpRound> Rounds => rounds;
    // The rounds fought (not walkovers) and their fighters, each once: the match's points, rewards and
    // participation count go by them.
    public int FoughtRounds => rounds.Count(round => round.Fought);
    public IEnumerable<long> RoundFighters => rounds.Where(round => round.Fought).SelectMany(round => round.FightersA.Concat(round.FightersB)).Distinct();

    private readonly List<GuildPvpRound> rounds = [];
    private readonly int[] wins = new int[3];
    private readonly List<long>[] fighters = [[], [], []];
    private readonly HashSet<long>[] left = [[], [], []];
    private readonly bool[] seen = new bool[3];
    private long nextRoundTick;
    private bool roundFought;

    public GuildPvpZoneState(int boxId, long tick, int prepareSeconds, int matchSeconds, IReadOnlyList<int> boxIds) {
        BoxId = boxId;
        StandA = At(boxIds, 0);
        StandB = At(boxIds, 1);
        AreaA = At(boxIds, 2);
        AreaB = At(boxIds, 3);
        PortalA = At(boxIds, 4);
        PortalB = At(boxIds, 5);
        ArenaPortal = At(boxIds, 6);
        int needed = At(boxIds, 7);
        WinsNeeded = needed is > 0 and <= MaxRounds ? needed : DefaultWins;
        RoundMs = Math.Max(1, matchSeconds) * 1000L;
        StartTick = tick + Math.Max(0, prepareSeconds) * 1000L;
        nextRoundTick = StartTick;
    }

    private static int At(IReadOnlyList<int> values, int index) => index < values.Count ? values[index] : 0;

    // The team's stand box (102, 103).
    public int StandOf(int team) => team == TeamA ? StandA : team == TeamB ? StandB : 0;

    public int Wins(int team) => IsTeam(team) ? wins[team] : 0;

    // The team's fighters of the round (as it started) and those still in it.
    public IReadOnlyList<long> Fighters(int team) => IsTeam(team) ? fighters[team] : [];
    public bool InFight(long characterId) => Fighting && (left[TeamA].Contains(characterId) || left[TeamB].Contains(characterId));
    public IEnumerable<long> FightersLeft => left[TeamA].Concat(left[TeamB]);

    public int TeamOfFighter(long characterId) => left[TeamA].Contains(characterId) ? TeamA : left[TeamB].Contains(characterId) ? TeamB : NoTeam;

    // Two fighters of the running round on opposite teams.
    public bool Hostile(long caster, long target) {
        if (!Fighting) {
            return false;
        }
        int a = TeamOfFighter(caster);
        int b = TeamOfFighter(target);
        return a != NoTeam && b != NoTeam && a != b;
    }

    // The team with more round wins.
    public int Winner => wins[TeamA] > wins[TeamB] ? TeamA : wins[TeamB] > wins[TeamA] ? TeamB : NoTeam;
    // The match's winner once it ended (a forfeit's is the team that stayed).
    public int MatchWinner { get; private set; }

    // Whether the team won a round that was fought (a walkover or a forfeit's cut round is not one): only such a
    // win counts for the win's trophies and achievements.
    public bool WonFoughtRound(int team) => IsTeam(team) && rounds.Any(round => round.Fought && round.Winner == team);

    // Whether the team had a player in the field at some point (a team never seen cannot forfeit).
    public bool Seen(int team) => IsTeam(team) && seen[team];

    // The zone's clock. stand(team): the team's living players in its stand box, others(team): its other living
    // players in the field, both in the caller's order; inFight(id): whether a fighter is still in the fight box;
    // present(team): whether the team has a player in the field at all (a team seen before and gone now forfeits
    // once the match started).
    public List<GuildPvpZoneEvent> Update(long tick, Func<int, IReadOnlyList<long>> stand, Func<int, IReadOnlyList<long>> others,
                                          Func<long, bool> inFight, Func<int, bool>? present = null) {
        var events = new List<GuildPvpZoneEvent>();
        if (Ended) {
            return events;
        }
        if (present != null) {
            bool presentA = present(TeamA);
            bool presentB = present(TeamB);
            seen[TeamA] |= presentA;
            seen[TeamB] |= presentB;
            bool goneA = seen[TeamA] && !presentA;
            bool goneB = seen[TeamB] && !presentB;
            if (tick >= StartTick && (goneA || goneB)) {
                Forfeit(goneA && goneB ? NoTeam : goneA ? SeenOrNone(TeamB) : SeenOrNone(TeamA), tick, events);
                return events;
            }
        }
        if (Fighting) {
            foreach (long characterId in FightersLeft.Where(id => !inFight(id)).ToList()) {
                Out(characterId, tick, events);
            }
            if (Fighting && tick >= RoundEndTick) {
                int a = left[TeamA].Count;
                int b = left[TeamB].Count;
                EndRound(tick, a > b ? TeamA : b > a ? TeamB : NoTeam, events);
            }
            return events;
        }
        if (tick < nextRoundTick) {
            return events;
        }

        var picks = new List<long>[TeamB + 1];
        for (int team = TeamA; team <= TeamB; team++) {
            IReadOnlyList<long> picked = stand(team);
            if (picked.Count == 0) {
                picked = others(team);
            }
            picks[team] = picked.Distinct().Take(FightersPerTeam).ToList();
        }
        bool hasA = picks[TeamA].Count > 0;
        bool hasB = picks[TeamB].Count > 0;
        if ((!hasA || !hasB) && present != null && !(seen[TeamA] && seen[TeamB]) && tick < StartTick + WalkoverGraceMs) {
            // A team not seen yet may still be loading: wait for it (no round) until the grace ends.
            return events;
        }
        Round++;
        for (int team = TeamA; team <= TeamB; team++) {
            fighters[team] = picks[team];
            left[team] = fighters[team].ToHashSet();
        }
        if (!hasA || !hasB) {
            // Nobody to send: the round goes to the other team without a fight (both empty: a draw round).
            fighters[TeamA] = [];
            fighters[TeamB] = [];
            left[TeamA] = [];
            left[TeamB] = [];
            roundFought = false;
            EndRound(tick, hasA ? TeamA : hasB ? TeamB : NoTeam, events);
            return events;
        }
        roundFought = true;
        Fighting = true;
        RoundEndTick = tick + RoundMs;
        events.Add(new GuildPvpZoneEvent(GuildPvpZoneEventType.RoundStart));
        return events;
    }

    // The team's last player left the field: once the match started, a team that was seen forfeits (the other
    // team wins if it was seen too, else the match is a draw: a guild never in the field wins nothing). Nothing for
    // a team never seen or before the start.
    public List<GuildPvpZoneEvent> Forfeit(int team, long tick) {
        var events = new List<GuildPvpZoneEvent>();
        if (!Ended && IsTeam(team) && seen[team] && tick >= StartTick) {
            Forfeit(SeenOrNone(team == TeamA ? TeamB : TeamA), tick, events);
        }
        return events;
    }

    private int SeenOrNone(int team) => seen[team] ? team : NoTeam;

    // The match ends with the winner (NoTeam: both gone, a draw). A running round is cut short: it goes to the
    // winner for the result only (no fighters, not fought); the fought rounds before it stay as they were.
    private void Forfeit(int winner, long tick, List<GuildPvpZoneEvent> events) {
        if (Fighting) {
            // The round's fighters stay listed (the field sends those still in the arena back), the round has none.
            Fighting = false;
            left[TeamA] = [];
            left[TeamB] = [];
            rounds.Add(new GuildPvpRound([], [], winner, false));
            if (IsTeam(winner)) {
                wins[winner]++;
            }
            events.Add(new GuildPvpZoneEvent(GuildPvpZoneEventType.RoundEnd, winner));
        }
        End(tick, winner, events);
    }

    // A fighter died or left the field.
    public List<GuildPvpZoneEvent> Out(long characterId, long tick) {
        var events = new List<GuildPvpZoneEvent>();
        if (Fighting && !Ended) {
            Out(characterId, tick, events);
        }
        return events;
    }

    private void Out(long characterId, long tick, List<GuildPvpZoneEvent> events) {
        int team = TeamOfFighter(characterId);
        if (team == NoTeam) {
            return;
        }
        left[team].Remove(characterId);
        events.Add(new GuildPvpZoneEvent(GuildPvpZoneEventType.Out, team, characterId));
        int other = team == TeamA ? TeamB : TeamA;
        if (left[team].Count == 0) {
            EndRound(tick, left[other].Count > 0 ? other : NoTeam, events);
        }
    }

    private void EndRound(long tick, int winner, List<GuildPvpZoneEvent> events) {
        Fighting = false;
        rounds.Add(new GuildPvpRound(fighters[TeamA].ToList(), fighters[TeamB].ToList(), winner, roundFought));
        if (IsTeam(winner)) {
            wins[winner]++;
        }
        events.Add(new GuildPvpZoneEvent(GuildPvpZoneEventType.RoundEnd, winner));
        if (wins[TeamA] >= WinsNeeded || wins[TeamB] >= WinsNeeded || Round >= MaxRounds) {
            End(tick, Winner, events);
            return;
        }
        nextRoundTick = tick + BreakMs;
    }

    private void End(long tick, int winner, List<GuildPvpZoneEvent> events) {
        Fighting = false;
        Ended = true;
        LastEndTick = tick;
        MatchWinner = winner;
        events.Add(new GuildPvpZoneEvent(GuildPvpZoneEventType.End, winner));
    }

    // The field ends the match early (a field without a match found no two guilds): the rounds stand as they are.
    public List<GuildPvpZoneEvent> Stop(long tick) {
        if (Ended) {
            return [];
        }
        var events = new List<GuildPvpZoneEvent>();
        End(tick, Winner, events);
        return events;
    }

    private static bool IsTeam(int team) => team is TeamA or TeamB;
}

namespace Maple2.Model.Game.Pvp;

public enum TeamZoneEventType {
    // The score changed (Pvp 0x6c and the gauge 0x6f).
    Score,
    // A player's row changed (Pvp 0x6d).
    Record,
    // A team took a flag (Pvp 0x70: the team, the flag).
    Flag,
    // A player plundered the other team's treasure (Pvp 0x71: the name, the treasure's team).
    Plunder,
}

public readonly record struct TeamZoneEvent(TeamZoneEventType Type, long CharacterId = 0, int Team = 0, int Flag = 0);

public sealed class TeamZoneEntry {
    public required long CharacterId { get; init; }
    public required int Team { get; init; }
    public int Kill { get; set; }
    public int Death { get; set; }
    public bool Here { get; set; } = true;
    public long LastPlunderTick { get; set; } = long.MinValue;
}

// Treasure Island's PvP zone (set_pvp_zone type 3 with the boxes 1, 2, 101, 102, 103; the client's PvP mode 3). From
// the client: two teams by PvP point, red 201 (0xc9) and blue 202 (0xca) (the name tag, the minimap and the team
// tables key them so); the score of both (0x6c), each player's row (0x6d), the flag notice "the red/blue team is
// capturing the north/center/south flag" (0x70 with 1 red or 2 blue and 0 north, 1 center, 2 south), the plunder
// notice "X plundered the red/blue team's treasure" (0x71 with the name and 1 red or 2 blue) and the red team's share
// of a gauge (0x6f, in tenths of a percent). Chosen here (the client and the data do not settle them): the zone's
// extra boxes are the red team's treasure, the blue team's treasure, then the north, center and south flags, in that
// order; the participants (the players in the zone's box when the fight starts, in random order) are dealt to red
// and blue in turn; a flag is taken by a team whose players are in its box with none of the other team's for
// CaptureMs; each held flag gives FlagScore to its team every ScoreTickMs; a player in the other team's treasure box
// plunders it for PlunderScore, once per PlunderCooldownMs; a kill gives KillScore; the team with the higher score
// wins, equal scores are a draw. The gauge is the red team's share of both scores.
public sealed class TeamZoneState {
    public const int Red = 201;
    public const int Blue = 202;
    public const long CaptureMs = 5_000;
    public const long ScoreTickMs = 5_000;
    public const long PlunderCooldownMs = 30_000;
    public const int FlagScore = 1;
    public const int PlunderScore = 5;
    public const int KillScore = 1;

    public int BoxId { get; }
    public bool Active { get; private set; }
    public bool Ended { get; private set; }
    public long ActiveTick { get; }
    public long EndTick { get; }
    public int RedScore { get; private set; }
    public int BlueScore { get; private set; }
    public IReadOnlyList<int> FlagBoxes { get; }
    public int RedTreasureBox { get; }
    public int BlueTreasureBox { get; }

    private readonly Dictionary<long, TeamZoneEntry> entries = [];
    private readonly int[] flagOwner;
    private readonly int[] captureTeam;
    private readonly long[] captureSince;
    private long nextScoreTick;

    public TeamZoneState(int boxId, long tick, int prepareSeconds, int matchSeconds, IReadOnlyList<int> boxIds) {
        BoxId = boxId;
        ActiveTick = tick + Math.Max(0, prepareSeconds) * 1000L;
        EndTick = ActiveTick + Math.Max(0, matchSeconds) * 1000L;
        RedTreasureBox = boxIds.Count > 0 ? boxIds[0] : 0;
        BlueTreasureBox = boxIds.Count > 1 ? boxIds[1] : 0;
        FlagBoxes = boxIds.Skip(2).Take(3).ToList();
        flagOwner = new int[FlagBoxes.Count];
        captureTeam = new int[FlagBoxes.Count];
        captureSince = new long[FlagBoxes.Count];
    }

    public IReadOnlyCollection<TeamZoneEntry> Entries => entries.Values;
    public TeamZoneEntry? Entry(long characterId) => entries.GetValueOrDefault(characterId);
    public int FlagOwner(int flag) => flagOwner[flag];
    public bool DueToActivate(long tick) => !Active && !Ended && tick >= ActiveTick;
    public bool DueToEnd(long tick) => Active && !Ended && tick >= EndTick;

    // The participants in the order to deal them: red, blue, red...
    public IReadOnlyList<TeamZoneEntry> Activate(IEnumerable<long> players, long tick) {
        int index = 0;
        foreach (long characterId in players.Distinct()) {
            entries[characterId] = new TeamZoneEntry { CharacterId = characterId, Team = index++ % 2 == 0 ? Red : Blue };
        }
        Active = true;
        nextScoreTick = tick + ScoreTickMs;
        return entries.Values.ToList();
    }

    public bool Hostile(long caster, long target) {
        return Active && !Ended && entries.TryGetValue(caster, out TeamZoneEntry? a) && entries.TryGetValue(target, out TeamZoneEntry? b) && a.Team != b.Team;
    }

    public List<TeamZoneEvent> Death(long victim, long killer) {
        var events = new List<TeamZoneEvent>();
        if (!Active || Ended || !entries.TryGetValue(victim, out TeamZoneEntry? dead)) {
            return events;
        }
        dead.Death++;
        events.Add(new TeamZoneEvent(TeamZoneEventType.Record, victim));
        if (killer != 0 && entries.TryGetValue(killer, out TeamZoneEntry? winner) && winner.Team != dead.Team) {
            winner.Kill++;
            AddScore(winner.Team, KillScore);
            events.Add(new TeamZoneEvent(TeamZoneEventType.Record, killer));
            events.Add(new TeamZoneEvent(TeamZoneEventType.Score));
        }
        return events;
    }

    public void SetHere(long characterId, bool here) {
        if (entries.TryGetValue(characterId, out TeamZoneEntry? entry)) {
            entry.Here = here;
        }
    }

    // inBox: the participants (character ids) in a box now.
    public List<TeamZoneEvent> Update(long tick, Func<int, IEnumerable<long>> inBox) {
        var events = new List<TeamZoneEvent>();
        if (!Active || Ended) {
            return events;
        }
        bool scored = false;
        for (int flag = 0; flag < FlagBoxes.Count; flag++) {
            var teams = inBox(FlagBoxes[flag]).Select(id => entries.GetValueOrDefault(id)?.Team ?? 0).Where(team => team != 0).Distinct().ToList();
            int team = teams.Count == 1 ? teams[0] : 0;
            if (team == 0 || team == flagOwner[flag]) {
                captureTeam[flag] = 0;
                continue;
            }
            if (captureTeam[flag] != team) {
                captureTeam[flag] = team;
                captureSince[flag] = tick;
            } else if (tick - captureSince[flag] >= CaptureMs) {
                flagOwner[flag] = team;
                captureTeam[flag] = 0;
                events.Add(new TeamZoneEvent(TeamZoneEventType.Flag, Team: team, Flag: flag));
            }
        }
        foreach ((int box, int owner) in new[] { (RedTreasureBox, Red), (BlueTreasureBox, Blue) }) {
            if (box == 0) {
                continue;
            }
            foreach (long characterId in inBox(box)) {
                if (!entries.TryGetValue(characterId, out TeamZoneEntry? entry) || entry.Team == owner) {
                    continue;
                }
                if (entry.LastPlunderTick != long.MinValue && tick - entry.LastPlunderTick < PlunderCooldownMs) {
                    continue;
                }
                entry.LastPlunderTick = tick;
                AddScore(entry.Team, PlunderScore);
                scored = true;
                events.Add(new TeamZoneEvent(TeamZoneEventType.Plunder, characterId, owner));
            }
        }
        while (tick >= nextScoreTick) {
            nextScoreTick += ScoreTickMs;
            foreach (int owner in flagOwner.Where(owner => owner != 0)) {
                AddScore(owner, FlagScore);
                scored = true;
            }
        }
        if (scored) {
            events.Add(new TeamZoneEvent(TeamZoneEventType.Score));
        }
        return events;
    }

    // The winning team (Red or Blue), 0 for a draw.
    public int End() {
        Ended = true;
        return RedScore > BlueScore ? Red : BlueScore > RedScore ? Blue : 0;
    }

    // The red team's share of both scores in tenths of a percent (the client shows it divided by 10); half with none.
    public int RedGauge() {
        int total = RedScore + BlueScore;
        return total == 0 ? 500 : RedScore * 1000 / total;
    }

    private void AddScore(int team, int score) {
        if (team == Red) {
            RedScore += score;
        } else if (team == Blue) {
            BlueScore += score;
        }
    }
}

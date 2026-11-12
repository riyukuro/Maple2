namespace Maple2.Model.Game.Pvp;

public enum PvpZonePhase {
    // The zone is set; the fight starts after its prepare time.
    Preparing,
    // The participants can hit each other; the ranking runs.
    Active,
    // The match time ran out; the result was given.
    Ended,
}

// One participant's row: the PvP point the client keys its enemies by (PC::vfunc_116: two players with points that
// differ are enemies, CPvPInfo::ReadPointTeamMode), and the ranking values of Pvp 0x67 and 0x6a.
public sealed class PvpZoneEntry {
    public required long CharacterId { get; init; }
    public required int Point { get; init; }
    public int Score { get; set; }
    public int Kill { get; set; }
    public int Death { get; set; }
    public long Damage { get; set; }
    public bool Here { get; set; } = true;
}

// A trigger's PvP zone (set_pvp_zone: box, prepare seconds, match seconds, additional effect, type) in its free-for-all
// mode, the one the Crimson Tear Mine (65000002) sets with type 2 and the client ranks (CPvPInfo m_nMode 2,
// Unk_PvpTimer::FUN_141018010). Not thread safe; times are ticks in milliseconds passed in by the caller.
//
// From the data: a kill is worth ModePvPScoreKill (100) and a death ModePvPScoreDead (-50) (server constants). From the
// client: the ranking order (FUN_141018720: score, kills, fewer deaths, damage, then the higher character id) and
// the reward of a rank (UIPvPResultDialog::FUN_141344210: PvpFFAReward<rank>Count plus PvpFFAAdditionRewardRate per
// kill for ranks 1 to 10, PvpFFAReward10Count otherwise, of PvpFFARewardItem). Chosen here: the participants are
// the players in the zone's box when the fight starts, each with its own point from FirstPoint up (as KMS2's one
// recorded free-for-all numbers them); a death counts for the player that hit the victim last, if any; every
// participant gets a rank.
public sealed class PvpZoneState {
    public const int FfaMode = 2;
    public const int FirstPoint = 101;

    public int BoxId { get; }
    public int Mode { get; }
    public PvpZonePhase Phase { get; private set; } = PvpZonePhase.Preparing;
    public long ActiveTick { get; }
    public long EndTick { get; }

    private readonly int scoreKill;
    private readonly int scoreDead;
    private readonly Dictionary<long, PvpZoneEntry> entries = [];
    private readonly Dictionary<long, long> lastHitBy = [];

    public PvpZoneState(int boxId, int mode, long tick, int prepareSeconds, int matchSeconds, int scoreKill, int scoreDead) {
        BoxId = boxId;
        Mode = mode;
        ActiveTick = tick + Math.Max(0, prepareSeconds) * 1000L;
        EndTick = ActiveTick + Math.Max(0, matchSeconds) * 1000L;
        this.scoreKill = scoreKill;
        this.scoreDead = scoreDead;
    }

    public IReadOnlyCollection<PvpZoneEntry> Entries => entries.Values;
    public PvpZoneEntry? Entry(long characterId) => entries.GetValueOrDefault(characterId);

    // Whether it is time to start the fight (the caller then calls Activate with the players in the box).
    public bool DueToActivate(long tick) => Phase == PvpZonePhase.Preparing && tick >= ActiveTick;
    public bool DueToEnd(long tick) => Phase == PvpZonePhase.Active && tick >= EndTick;

    public IReadOnlyList<PvpZoneEntry> Activate(IEnumerable<long> participants) {
        int point = FirstPoint;
        foreach (long characterId in participants.Distinct()) {
            entries[characterId] = new PvpZoneEntry { CharacterId = characterId, Point = point++ };
        }
        Phase = PvpZonePhase.Active;
        return entries.Values.ToList();
    }

    public bool Hostile(long caster, long target) {
        return Phase == PvpZonePhase.Active && caster != target && entries.ContainsKey(caster) && entries.ContainsKey(target);
    }

    // A hit between participants: the attacker's damage grows; the victim's last attacker is kept for its death.
    public PvpZoneEntry? Damage(long attacker, long victim, long amount) {
        if (!Hostile(attacker, victim) || amount <= 0) {
            return null;
        }
        PvpZoneEntry entry = entries[attacker];
        entry.Damage += amount;
        lastHitBy[victim] = attacker;
        return entry;
    }

    // A participant died: its death counts, and the kill goes to its last attacker. The changed rows are returned
    // (the killer first, when there is one).
    public List<PvpZoneEntry> Death(long victim, out long killer) {
        killer = 0;
        var changed = new List<PvpZoneEntry>();
        if (Phase != PvpZonePhase.Active || !entries.TryGetValue(victim, out PvpZoneEntry? dead)) {
            return changed;
        }
        if (lastHitBy.Remove(victim, out long last) && entries.TryGetValue(last, out PvpZoneEntry? winner)) {
            killer = last;
            winner.Kill++;
            winner.Score += scoreKill;
            changed.Add(winner);
        }
        dead.Death++;
        dead.Score += scoreDead;
        changed.Add(dead);
        return changed;
    }

    public void SetHere(long characterId, bool here) {
        if (entries.TryGetValue(characterId, out PvpZoneEntry? entry)) {
            entry.Here = here;
        }
    }

    public List<PvpZoneEntry> End() {
        Phase = PvpZonePhase.Ended;
        return Ranking();
    }

    // The client's order (FUN_141018720): those here first, then score, kills, fewer deaths, damage, the higher id.
    public List<PvpZoneEntry> Ranking() {
        return entries.Values
            .OrderByDescending(entry => entry.Here)
            .ThenByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.Kill)
            .ThenBy(entry => entry.Death)
            .ThenByDescending(entry => entry.Damage)
            .ThenByDescending(entry => entry.CharacterId)
            .ToList();
    }

    // The tokens of a rank as the result dialog shows them: rewardCounts are PvpFFAReward1Count to 10.
    public static int Reward(int rank, int kills, IReadOnlyList<int> rewardCounts, int additionRate) {
        if (rewardCounts.Count < 10) {
            return 0;
        }
        if (rank is >= 1 and <= 10) {
            return rewardCounts[rank - 1] + additionRate * kills;
        }
        return rewardCounts[9];
    }
}

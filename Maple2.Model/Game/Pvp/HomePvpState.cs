namespace Maple2.Model.Game.Pvp;

// The six choices of the home's PvP control dialog (UIUGCPVPControlDialog, its OnStart; HomeAction 14, 1), each the
// selected index of its list: type 0 every player for themselves, 1 by party; buff 0 the PvP default, 1 to 4 the 50x,
// 100x, 300x and 500x health; potion 0 no one may drink, 1 anyone may (its list is disable, enable); revive 0 the revive
// in place is not offered, 1 it is (the same order); rule 0 any number of revives, 1 survival; time 0 no limit, 1 3
// minutes, 2 5 minutes, 3 10 minutes. The dialog starts at 0 everywhere. Under survival the dialog sets revive to 0 and turns its list off (onRuleChange).
public readonly record struct HomePvpSettings(byte Type, byte Buff, byte Potion, byte Revive, byte Rule, byte Time) {
    public static readonly HomePvpSettings Default = new(0, 0, 0, 0, 0, 0);

    // The client's lists: anything else is not from the dialog.
    public bool Valid => Type <= 1 && Buff <= 4 && Potion <= 1 && Revive <= 1 && Rule <= 1 && Time <= 3;

    public bool ByParty => Type == 1;
    public bool Survival => Rule == 1;
    public bool NoPotion => Potion == 0;
    // The dialog sends revive 0 under survival; a 1 there is not the dialog's and is not taken.
    public bool NoRevive => Revive == 0 || Rule == 1;

    public int Seconds => Time switch {
        1 => 180,
        2 => 300,
        3 => 600,
        _ => 0,
    };

    // The buff of the choice (additionaleffect 70000171, the PvP default: finalhprate and super armor; 70000173 level
    // 1 to 4: finalhprate 50, 100, 300 and 500).
    public (int Id, short Level) Effect => Buff == 0 ? (DefaultEffectId, (short) 1) : (HealthEffectId, (short) Buff);

    public const int DefaultEffectId = 70000171;
    public const int HealthEffectId = 70000173;
}

public enum HomePvpPhase {
    // The owner started it; the fight starts after CountdownMs.
    Countdown,
    // The participants can hit each other.
    Active,
    // The result was given (or the start was called off).
    Ended,
}

// One participant: its PvP point, the one the client keys its enemies by (two players with points above 0 that differ
// are enemies, CPvPInfo::ReadPointTeamMode). The sides are numbered from PvpPoints.First (101): the client's hit test
// never hits a target of team 2 and lets one of team 2 hit anyone, and team 1 is a PC without a point (PvpPoints).
// Out once it left the home, or died under survival.
public sealed class HomePvpEntry {
    public required long CharacterId { get; init; }
    public required int Point { get; init; }
    public bool Out { get; set; }
}

// A home PvP match (the client has the dialog and its strings, no rule): every player in the home when the fight
// starts is a participant, each with a point of its own, or under the party type one point per party (a player in no
// party alone). A death counts as a kill for the other side's participant that hit the victim last, within
// KillCreditMs. The match ends when fewer than two sides are left in play, at the time limit, or when the owner uses the
// console again; the side left alone wins, otherwise the side in play with the most kills, and a tie is a draw.
// Chosen here, the client has none of it: the participants, the sides, the kill credit, the end and the winner.
public sealed class HomePvpState {
    public const long CountdownMs = 5_000;
    public const long KillCreditMs = PvpZoneState.KillCreditMs;

    public HomePvpSettings Settings { get; }
    public HomePvpPhase Phase { get; private set; } = HomePvpPhase.Countdown;

    private readonly long activateAt;
    private long endAt = long.MaxValue;
    private readonly Dictionary<long, HomePvpEntry> entries = new();
    private readonly Dictionary<int, int> sideKills = new();
    private readonly Dictionary<long, (long Attacker, long Tick)> lastHitBy = new();

    public HomePvpState(HomePvpSettings settings, long tick) {
        Settings = settings;
        activateAt = tick + CountdownMs;
    }

    public IEnumerable<HomePvpEntry> Entries => entries.Values;

    public HomePvpEntry? Entry(long characterId) => entries.GetValueOrDefault(characterId);

    public bool DueToActivate(long tick) => Phase == HomePvpPhase.Countdown && tick >= activateAt;

    // The players here at the start (character id, party id; 0 for none) become the participants. Fewer than two
    // sides call the match off: the result is empty and the phase is Ended.
    public IReadOnlyList<HomePvpEntry> Activate(IEnumerable<(long CharacterId, long PartyId)> players, long tick) {
        var points = new Dictionary<long, int>();
        var added = new List<HomePvpEntry>();
        foreach ((long characterId, long partyId) in players) {
            if (entries.ContainsKey(characterId)) {
                continue;
            }
            int point;
            if (Settings.ByParty && partyId != 0) {
                if (!points.TryGetValue(partyId, out point)) {
                    point = PvpPoints.First + points.Count;
                    points[partyId] = point;
                }
            } else {
                point = PvpPoints.First + points.Count;
                // A key no party id has: a player's own side.
                points[-characterId] = point;
            }
            var entry = new HomePvpEntry { CharacterId = characterId, Point = point };
            entries[characterId] = entry;
            added.Add(entry);
        }
        if (SidesInPlay < 2) {
            entries.Clear();
            Phase = HomePvpPhase.Ended;
            return [];
        }
        Phase = HomePvpPhase.Active;
        if (Settings.Seconds > 0) {
            endAt = tick + Settings.Seconds * 1000L;
        }
        return added;
    }

    public int SidesInPlay => entries.Values.Where(entry => !entry.Out).Select(entry => entry.Point).Distinct().Count();

    public bool InPlay(long characterId) => Phase == HomePvpPhase.Active && entries.TryGetValue(characterId, out HomePvpEntry? entry) && !entry.Out;

    public bool Hostile(long caster, long target) {
        return caster != target && InPlay(caster) && InPlay(target) && entries[caster].Point != entries[target].Point;
    }

    // A hit between enemies, for the kill credit.
    public void Damage(long attacker, long victim, long tick) {
        if (Hostile(attacker, victim)) {
            lastHitBy[victim] = (attacker, tick);
        }
    }

    // A participant in play died: the kill of the enemy that hit it last within KillCreditMs (0 for none), and under
    // survival the victim is out.
    public long Death(long victim, long tick) {
        if (!InPlay(victim)) {
            return 0;
        }
        long killer = 0;
        if (lastHitBy.Remove(victim, out (long Attacker, long Tick) hit) && tick - hit.Tick <= KillCreditMs
            && entries.TryGetValue(hit.Attacker, out HomePvpEntry? attacker) && attacker.Point != entries[victim].Point) {
            killer = hit.Attacker;
            sideKills[attacker.Point] = sideKills.GetValueOrDefault(attacker.Point) + 1;
        }
        if (Settings.Survival) {
            entries[victim].Out = true;
        }
        return killer;
    }

    // A participant left the home: out of the match (chosen here; it does not come back into it).
    public bool Leave(long characterId) {
        if (Phase != HomePvpPhase.Active || !entries.TryGetValue(characterId, out HomePvpEntry? entry) || entry.Out) {
            return false;
        }
        entry.Out = true;
        lastHitBy.Remove(characterId);
        return true;
    }

    public int Kills(int point) => sideKills.GetValueOrDefault(point);

    public bool DueToEnd(long tick) => Phase == HomePvpPhase.Active && (SidesInPlay < 2 || tick >= endAt);

    // The winning side's point, or 0 for a draw.
    public int End() {
        Phase = HomePvpPhase.Ended;
        List<int> inPlay = entries.Values.Where(entry => !entry.Out).Select(entry => entry.Point).Distinct().ToList();
        if (inPlay.Count == 1) {
            return inPlay[0];
        }
        if (inPlay.Count == 0) {
            return 0;
        }
        int most = inPlay.Max(Kills);
        List<int> best = inPlay.Where(point => Kills(point) == most).ToList();
        return best.Count == 1 ? best[0] : 0;
    }

    public IEnumerable<long> Members(int point) => entries.Values.Where(entry => entry.Point == point).Select(entry => entry.CharacterId);
}

using Maple2.Model.Enum;

namespace Maple2.Model.Game.Pvp;

// One Maple Survival game (the field game MapleSurvival of the survival maps' scripts): the participants, who is still
// alive, the order they fell in and their kills. Not thread safe.
//
// From the scripts: the game starts with CreateFieldGame MapleSurvival, the script's round runs while
// IsPlayingMapleSurvival holds. Chosen here (not settled by the client or the data): the participants are the players
// in the field when the game is created; a participant's death or departure ends its game; the kill goes to the
// participant that hit it last within KillCreditMs; the game ends when one participant (the winner) or none is left;
// a participant's rank is the number of squads still alive when its squad's last member fell, plus one (a solo
// player is a squad of one); the last squad standing wins (rank 1), and squad members are no enemies.
public sealed class SurvivalGameState {
    public const long KillCreditMs = 10_000;

    public sealed class Entry {
        public required long CharacterId { get; init; }
        // The squad it plays in (its first member's id; a solo player is its own).
        public long Unit { get; init; }
        public bool Alive { get; set; } = true;
        public int Kills { get; set; }
        public int Rank { get; set; }
        // This match's play, for the survival trophies (SurvivalConditions): the monsters it defeated, the breakable
        // objects it broke, whether it completed an interact object (a chest), the items that entered its inventory
        // (id: amount) and its kills by the skill of the killing hit (id: kills).
        public int NpcKills { get; set; }
        public int Breakables { get; set; }
        public bool Interacted { get; set; }
        public Dictionary<int, int> Items { get; } = [];
        public Dictionary<int, int> KillSkills { get; } = [];
        // The tick it fell at, or the game's end for one still alive then (-1: not yet).
        public long EndTick { get; set; } = -1;
        // The tick of the hit behind its last kill (-1: none), for the double kill.
        public long LastKillHitTick { get; set; } = -1;
    }

    private readonly Dictionary<long, Entry> entries = [];
    private readonly Dictionary<long, (long Attacker, long Tick, int SkillId)> lastHitBy = [];
    private readonly List<long> fallen = [];

    public bool Ended { get; private set; }
    public long Winner { get; private set; }
    public IReadOnlyList<long> Fallen => fallen;
    public IReadOnlyCollection<Entry> Entries => entries.Values;
    public int Alive => entries.Values.Count(entry => entry.Alive);

    // unitOf: a participant's squad (Maple Survival squads); without it every participant is its own.
    public SurvivalGameState(IEnumerable<long> participants, Func<long, long>? unitOf = null) {
        foreach (long characterId in participants.Distinct()) {
            entries[characterId] = new Entry { CharacterId = characterId, Unit = unitOf?.Invoke(characterId) ?? characterId };
        }
        if (AliveUnits <= 1) {
            End(0);
        }
    }

    // The squads (units) with a member still alive.
    public int AliveUnits => entries.Values.Where(entry => entry.Alive).Select(entry => entry.Unit).Distinct().Count();

    // The participants of a unit.
    public IEnumerable<Entry> UnitOf(long characterId) {
        return entries.TryGetValue(characterId, out Entry? entry)
            ? entries.Values.Where(other => other.Unit == entry.Unit)
            : [];
    }

    public Entry? Get(long characterId) => entries.GetValueOrDefault(characterId);
    public bool IsAlive(long characterId) => entries.TryGetValue(characterId, out Entry? entry) && entry.Alive && !Ended;

    // Living participants of different squads fight each other while the game runs.
    public bool Hostile(long caster, long target) => caster != target && IsAlive(caster) && IsAlive(target)
                                                     && entries[caster].Unit != entries[target].Unit;

    // skillId: the hit's skill (0 when not known).
    public void Hit(long attacker, long victim, long tick, int skillId = 0) {
        if (Hostile(attacker, victim)) {
            lastHitBy[victim] = (attacker, tick, skillId);
        }
    }

    // A monster defeated, a breakable object broken, an interact object completed, items that entered the inventory:
    // counted for a participant still playing.
    public void NpcKill(long characterId) {
        if (IsAlive(characterId)) {
            entries[characterId].NpcKills++;
        }
    }

    public void Break(long characterId) {
        if (IsAlive(characterId)) {
            entries[characterId].Breakables++;
        }
    }

    public void Interact(long characterId) {
        if (IsAlive(characterId)) {
            entries[characterId].Interacted = true;
        }
    }

    public void ItemGot(long characterId, int itemId, int amount) {
        if (amount > 0 && IsAlive(characterId)) {
            Dictionary<int, int> items = entries[characterId].Items;
            items[itemId] = items.GetValueOrDefault(itemId) + amount;
        }
    }

    public bool Fall(long victim, long tick, out long killer) => Fall(victim, tick, out killer, out _, out _);

    // A participant fell (died or left): its rank, and the killer when there is one. False when it was not playing.
    // skillId: the killing hit's skill (0 when not known). doubleKill: the killing hit landed in the same tick as the hit behind the killer's previous kill (two players
    // defeated at once; Chosen here, the client has no window for it).
    public bool Fall(long victim, long tick, out long killer, out int skillId, out bool doubleKill) {
        killer = 0;
        skillId = 0;
        doubleKill = false;
        if (!IsAlive(victim)) {
            return false;
        }
        Entry dead = entries[victim];
        dead.Alive = false;
        dead.EndTick = tick;
        // A squad's rank is set when its last member falls: the squads still alive, plus one; all its members share it.
        if (UnitOf(victim).All(member => !member.Alive)) {
            int rank = AliveUnits + 1;
            foreach (Entry member in UnitOf(victim)) {
                member.Rank = rank;
            }
        }
        fallen.Add(victim);
        if (lastHitBy.Remove(victim, out (long Attacker, long Tick, int SkillId) hit) && tick - hit.Tick <= KillCreditMs && entries.TryGetValue(hit.Attacker, out Entry? winner)) {
            killer = hit.Attacker;
            winner.Kills++;
            skillId = hit.SkillId;
            if (hit.SkillId != 0) {
                winner.KillSkills[hit.SkillId] = winner.KillSkills.GetValueOrDefault(hit.SkillId) + 1;
            }
            doubleKill = winner.LastKillHitTick == hit.Tick;
            winner.LastKillHitTick = hit.Tick;
        }
        if (AliveUnits <= 1) {
            End(tick);
        }
        return true;
    }

    // The game ends with one squad (or nobody) left: its members, fallen or not, rank 1; Winner is a living member.
    private void End(long tick) {
        Ended = true;
        foreach (Entry entry in entries.Values.Where(entry => entry.EndTick < 0)) {
            entry.EndTick = tick;
        }
        Entry? last = entries.Values.FirstOrDefault(entry => entry.Alive);
        if (last != null) {
            foreach (Entry member in UnitOf(last.CharacterId)) {
                member.Rank = 1;
            }
            Winner = last.CharacterId;
        }
    }
}

// The storm of the survival maps' SurvivalContents widget (03_storm.xml: StormData, then EnterStep 1 to 8, each
// until TimeOver). Not settled by the client or the data (no table holds the storm): chosen here, the safe zone is a
// circle at the field's center whose radius goes down in StepCount equal steps from StartRadius to MinRadius, each step
// lasting StepMs and announced ReadyMs before it shrinks.
public sealed class SurvivalStorm {
    public const int StepCount = 8;
    public const long StepMs = 60_000;
    public const long ReadyMs = 10_000;

    public float StartRadius { get; }
    public float MinRadius { get; }
    public int Step { get; private set; }
    public long StepEnd { get; private set; }

    public SurvivalStorm(float startRadius, float minRadius) {
        StartRadius = startRadius;
        MinRadius = Math.Min(minRadius, startRadius);
    }

    // The safe zone's radius after a step (0: before any).
    public float Radius(int step) {
        int done = Math.Clamp(step, 0, StepCount);
        return StartRadius - (StartRadius - MinRadius) * done / StepCount;
    }

    public float SafeRadius => Radius(Step);

    public void Enter(int step, long tick) {
        Step = Math.Clamp(step, 0, StepCount);
        StepEnd = tick + StepMs;
    }

    public bool TimeOver(long tick) => Step > 0 && tick >= StepEnd;

    public static bool Outside(float dx, float dy, float radius) => dx * dx + dy * dy > radius * radius;
}

// The Maple Survival trophy and quest conditions of a participant's match, raised with its result (the trophies
// 21300042-21300099 "... in one Mushking Royale match" count one match's play as the target): the kills (survival_kill),
// the rank with the kills (survival_rank_with_kill: code the rank, target the kills, each at most), the monsters, the
// breakable objects, each item's amount (survival_item_get, code the item), each killing skill's kills
// (survival_kill_use_skill, code the skill), the seconds survived on the map (survive_map, the counter) and, for the
// winning squad (rank 1), a win without an interact object, without a monster and with every kill by one skill.
public static class SurvivalConditions {
    public readonly record struct Raise(ConditionType Type, long Counter = 1, long Target = 0, long Code = 0);

    public static List<Raise> Result(SurvivalGameState.Entry entry, int rank, long startTick, int mapId) {
        var raises = new List<Raise> {
            new(ConditionType.survival_kill, Target: entry.Kills),
            new(ConditionType.survival_rank_with_kill, Target: entry.Kills, Code: rank),
            new(ConditionType.survival_npc_kill, Target: entry.NpcKills),
            new(ConditionType.survival_breakable_object, Target: entry.Breakables),
        };
        raises.AddRange(entry.Items.Select(item => new Raise(ConditionType.survival_item_get, Target: item.Value, Code: item.Key)));
        raises.AddRange(entry.KillSkills.Select(skill => new Raise(ConditionType.survival_kill_use_skill, Target: skill.Value, Code: skill.Key)));
        long seconds = entry.EndTick < startTick ? 0 : (entry.EndTick - startTick) / 1000;
        if (seconds > 0) {
            raises.Add(new Raise(ConditionType.survive_map, Counter: seconds, Code: mapId));
        }
        if (rank == 1) {
            if (!entry.Interacted) {
                raises.Add(new Raise(ConditionType.survival_win_without_interact));
            }
            if (entry.NpcKills == 0) {
                raises.Add(new Raise(ConditionType.survival_win_without_npckill));
            }
            if (entry.KillSkills.Count == 1 && entry.KillSkills.Values.Sum() == entry.Kills) {
                raises.Add(new Raise(ConditionType.survival_win_use_one_skill, Code: entry.KillSkills.Keys.First()));
            }
        }
        return raises;
    }
}

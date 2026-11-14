namespace Maple2.Server.Game.Util;

/// <summary>
/// The RainbowMonster widget's score (02100002, 01_maincontrol). The client has no code for it: the score shows only as
/// the trigger meshes the server lights, so every rule here is the server's, read from the script's own parameters.
/// Chosen here: a group's npc killed by a killer npc (SetKillerNpc, the furnace) adds 1 to that group; a bad npc
/// (SetBadNpc) killed by a killer npc takes a random amount in the SetBadNpcScore range from every group; every
/// SetLoseScoreTick ms each group loses 1; no score goes below 0. InitRandomMaxScore gives each created group (by group
/// number) one value of the list drawn without replacement. A group's lit meshes are min(meshes, score * meshes / max),
/// and IsMissionSuccess "a-b" holds when every created group's lit count lies within a..b.
/// </summary>
public sealed class RainbowMonsterScore {
    private sealed class Group {
        public required HashSet<int> NpcIds;
        public int Score;
        public int MaxScore;
        public int MeshBase;
        public int MeshCount;
    }

    private readonly object mutex = new();
    private readonly SortedDictionary<int, Group> groups = new();
    private readonly HashSet<int> killerNpcs = [];
    private readonly HashSet<int> badNpcs = [];
    private readonly Random random;
    private int badScoreMin;
    private int badScoreMax;
    private int loseScoreTick;
    private long nextLoseTick;

    public string BadNpcSoundKey { get; private set; } = string.Empty;

    public RainbowMonsterScore(Random? random = null) {
        this.random = random ?? Random.Shared;
    }

    public IReadOnlyCollection<int> Groups {
        get {
            lock (mutex) {
                return groups.Keys.ToArray();
            }
        }
    }

    public void CreateGroup(int group, string npcIds) {
        lock (mutex) {
            groups[group] = new Group { NpcIds = ParseIds(npcIds) };
        }
    }

    public void SetKillerNpc(string npcIds) {
        lock (mutex) {
            killerNpcs.Clear();
            killerNpcs.UnionWith(ParseIds(npcIds));
        }
    }

    public void SetBadNpc(string npcIds) {
        lock (mutex) {
            badNpcs.Clear();
            badNpcs.UnionWith(ParseIds(npcIds));
        }
    }

    // "2-5", or one number for a fixed loss.
    public void SetBadNpcScore(string range) {
        if (!TryParseRange(range, out int min, out int max)) {
            return;
        }
        lock (mutex) {
            badScoreMin = Math.Max(0, min);
            badScoreMax = Math.Max(badScoreMin, max);
        }
    }

    public void SetBadNpcSoundKey(string key) {
        BadNpcSoundKey = key.Trim();
    }

    // The loss runs from the call: the first point goes after one tick.
    public void SetLoseScoreTick(int milliseconds, long now) {
        lock (mutex) {
            loseScoreTick = Math.Max(0, milliseconds);
            nextLoseTick = now + loseScoreTick;
        }
    }

    public void InitRandomMaxScore(string values) {
        List<int> pool = values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out int parsed) ? parsed : 0)
            .Where(value => value > 0)
            .ToList();
        lock (mutex) {
            foreach (Group group in groups.Values) {
                if (pool.Count == 0) {
                    group.MaxScore = 0;
                    continue;
                }
                int index = random.Next(pool.Count);
                group.MaxScore = pool[index];
                pool.RemoveAt(index);
            }
        }
    }

    public void InitScoreMesh(int group, int meshBase, int meshCount) {
        lock (mutex) {
            if (groups.TryGetValue(group, out Group? found)) {
                found.MeshBase = meshBase;
                found.MeshCount = Math.Max(0, meshCount);
            }
        }
    }

    // The groups whose score changed. bad: a bad npc was killed.
    public IReadOnlyList<int> NpcDied(int npcId, int killerNpcId, out bool bad) {
        bad = false;
        lock (mutex) {
            if (!killerNpcs.Contains(killerNpcId)) {
                return [];
            }
            if (badNpcs.Contains(npcId)) {
                bad = true;
                var changed = new List<int>();
                foreach ((int number, Group group) in groups) {
                    int loss = badScoreMax > 0 ? random.Next(badScoreMin, badScoreMax + 1) : 0;
                    int before = group.Score;
                    group.Score = Math.Max(0, group.Score - loss);
                    if (group.Score != before) {
                        changed.Add(number);
                    }
                }
                return changed;
            }
            foreach ((int number, Group group) in groups) {
                if (group.NpcIds.Contains(npcId)) {
                    group.Score++;
                    return [number];
                }
            }
            return [];
        }
    }

    // The groups whose score the lose ticks passed by now changed.
    public IReadOnlyList<int> Update(long now) {
        lock (mutex) {
            if (loseScoreTick <= 0 || now < nextLoseTick) {
                return [];
            }
            int ticks = (int) Math.Min(int.MaxValue, (now - nextLoseTick) / loseScoreTick + 1);
            nextLoseTick += (long) ticks * loseScoreTick;
            var changed = new List<int>();
            foreach ((int number, Group group) in groups) {
                int before = group.Score;
                group.Score = Math.Max(0, group.Score - ticks);
                if (group.Score != before) {
                    changed.Add(number);
                }
            }
            return changed;
        }
    }

    public int Score(int group) {
        lock (mutex) {
            return groups.TryGetValue(group, out Group? found) ? found.Score : 0;
        }
    }

    public int MaxScore(int group) {
        lock (mutex) {
            return groups.TryGetValue(group, out Group? found) ? found.MaxScore : 0;
        }
    }

    public (int MeshBase, int MeshCount, int Lit) Meshes(int group) {
        lock (mutex) {
            return groups.TryGetValue(group, out Group? found) ? (found.MeshBase, found.MeshCount, Lit(found)) : (0, 0, 0);
        }
    }

    public bool IsMissionSuccess(string range) {
        if (!TryParseRange(range, out int min, out int max)) {
            return false;
        }
        lock (mutex) {
            return groups.Count > 0 && groups.Values.All(group => Lit(group) >= min && Lit(group) <= max);
        }
    }

    private static int Lit(Group group) {
        if (group.MaxScore <= 0 || group.MeshCount <= 0) {
            return 0;
        }
        return (int) Math.Min(group.MeshCount, (long) group.Score * group.MeshCount / group.MaxScore);
    }

    private static HashSet<int> ParseIds(string ids) {
        return ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => int.TryParse(id, out int parsed) ? parsed : 0)
            .Where(id => id > 0)
            .ToHashSet();
    }

    private static bool TryParseRange(string text, out int min, out int max) {
        string[] parts = text.Split('-', StringSplitOptions.TrimEntries);
        min = max = 0;
        if (parts.Length == 1) {
            if (!int.TryParse(parts[0], out min)) {
                return false;
            }
            max = min;
            return true;
        }
        return parts.Length == 2 && int.TryParse(parts[0], out min) && int.TryParse(parts[1], out max) && min <= max;
    }
}

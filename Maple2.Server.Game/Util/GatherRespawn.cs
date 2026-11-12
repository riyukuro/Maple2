namespace Maple2.Server.Game.Util;

// A gathered object comes back for each player alone: the client hides it for itself on the answer to
// its end of interaction and shows it again resetTime later (CInteractGatheringObject, interactobject.cpp:
// m_nRespawnTick), and the object's state on the field does not change. The server keeps the same time
// per character, so one player's gather takes nothing from another's.
public sealed class GatherRespawn {
    private readonly Dictionary<long, long> next = new();

    // True when the object can give to this character now; it then cannot again before now + resetTime.
    public bool Take(long characterId, long now, int resetTime) {
        lock (next) {
            if (next.TryGetValue(characterId, out long tick) && now < tick) {
                return false;
            }

            if (next.Count >= 64) {
                foreach (long id in next.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList()) {
                    next.Remove(id);
                }
            }
            if (resetTime > 0) {
                next[characterId] = now + resetTime;
            }
            return true;
        }
    }
}

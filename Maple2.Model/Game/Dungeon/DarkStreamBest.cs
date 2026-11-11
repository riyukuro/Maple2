namespace Maple2.Model.Game.Dungeon;

// A character's best Dark Stream score and the time it was set (epoch seconds). A run replaces it only with a
// higher score: the first run sets it, an equal score keeps the older record.
public record DarkStreamBest(int Score, long SetTime) {
    public static bool Replaces(DarkStreamBest? best, int score) => best == null || score > best.Score;
}

using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The dead node of an npc's data (&lt;dead time="5" revival="21001016" count="5" lifeTime="99999"/&gt;): when the dead
/// npc is removed, count npcs of the revival id stand where it died. 33 KR npcs set revival (Sleepy Shroom into five Little
/// Sleepy Shrooms, Rainbow Slime into smaller slimes, Mano into a friendly npc, the ghosts into Onrung); the client only
/// reads the node, so the server spawns them.
/// </summary>
public static class NpcRevivalRules {
    // More than one revival npc is scattered this far (game units) over the navmesh around the dead npc.
    public const float ScatterRadius = 150f;

    // The revival npcs to spawn: none without a revival id; every KR row with one has count 1 to 5.
    public static int Count(NpcMetadataDead dead) => dead.Revival > 0 ? Math.Max(dead.Count, 1) : 0;

    // lifeTime (seconds) removes each revival npc that long after it stands; 0 keeps it until it is killed.
    public static TimeSpan LifeTime(NpcMetadataDead dead) =>
        dead.LifeTime > 0 ? TimeSpan.FromSeconds(dead.LifeTime) : TimeSpan.Zero;
}

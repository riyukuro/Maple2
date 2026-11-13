using Maple2.Model.Enum;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Util;

/// <summary>
/// A player an npc's grab skill pins to one of its nodes is in state 43 (GrabTarget) and names the grabber in its
/// state sync. The client lets go by itself (state 5) only when the grabber's object or node is gone, and a dead npc
/// stays in the field for its dead time, so a player held by an npc that dies would hang on the corpse.
/// SkillDamage 7 (scenestategame.cpp:7128) puts each named player still held by that grabber back into state 1.
/// Chosen here: the release is sent when the grabber dies, to the whole field, naming the players whose last state
/// sync shows them held by it; no data or client code says when the server sends it.
/// </summary>
public static class GrabReleaseRules {
    public static List<int> HeldBy(IEnumerable<FieldPlayer> players, int grabberId) {
        if (grabberId == 0) {
            return [];
        }
        return players.Where(player => player.State == ActorState.GrabTarget && player.StateActorId == grabberId)
            .Select(player => player.ObjectId)
            .ToList();
    }

    public static void OnGrabberDeath(FieldManager field, int grabberId) {
        List<int> held = HeldBy(field.Players.Values, grabberId);
        if (held.Count > 0) {
            field.Broadcast(SkillDamagePacket.GrabRelease(grabberId, held));
        }
    }
}

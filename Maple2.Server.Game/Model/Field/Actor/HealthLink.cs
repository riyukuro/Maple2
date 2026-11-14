using Maple2.Model.Enum;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model;

// The npcs that share one health through summon option linkHP (AiSummon.SharedHealth): the summoner and its linked
// summons. Each living member shares before its own update, so a member's death check sees the shared health, and a
// member killed by a hit passes its last change on before it leaves the link.
public sealed class HealthLink {
    private readonly FieldNpc owner;
    private readonly List<(FieldNpc Npc, long Seen)> members = [];
    private long shared;

    public HealthLink(FieldNpc owner) {
        this.owner = owner;
        shared = owner.Stats.Values[BasicAttribute.Health].Current;
        members.Add((owner, shared));
    }

    public int Count {
        get {
            lock (members) {
                return members.Count;
            }
        }
    }

    public void Add(FieldNpc npc) {
        lock (members) {
            Share();
            Stat health = npc.Stats.Values[BasicAttribute.Health];
            health.Current = Math.Min(shared, health.Total);
            members.Add((npc, health.Current));
        }
    }

    // A member that ends itself (an AI Suicide node) leaves the link first: its health goes to 0 on its own, not
    // through the shared health, whose damage floor (AiSummon.MemberFloor) would hold a no_die member at 1 and undo
    // the node. The others keep the shared health.
    public void Leave(FieldNpc npc) {
        lock (members) {
            Share();
            members.RemoveAll(member => member.Npc == npc);
        }
    }

    public void Share() {
        lock (members) {
            IReadOnlyList<(long Current, long Seen)> changes = members
                .Select(member => (member.Npc.Stats.Values[BasicAttribute.Health].Current, member.Seen)).ToList();
            members.RemoveAll(member => member.Npc.IsDead || !member.Npc.Field.Mobs.ContainsKey(member.Npc.ObjectId) && !member.Npc.Field.Npcs.ContainsKey(member.Npc.ObjectId));
            // The floor of the members that stay: a dead or gone member's own floor no longer holds the link.
            shared = AiSummon.SharedHealth(shared, owner.Stats.Values[BasicAttribute.Health].Total, changes,
                members.Select(member => AiSummon.MemberFloor(member.Npc.NoDie, member.Npc.MinimumHealth)).DefaultIfEmpty(0).Max());
            for (int i = 0; i < members.Count; i++) {
                FieldNpc npc = members[i].Npc;
                Stat health = npc.Stats.Values[BasicAttribute.Health];
                long value = Math.Min(shared, health.Total);
                if (health.Current != value) {
                    health.Current = value;
                    npc.Field.Broadcast(StatsPacket.Update(npc, BasicAttribute.Health));
                }
                members[i] = (npc, value);
            }
        }
    }
}

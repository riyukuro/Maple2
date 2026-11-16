using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Serilog;
using static Maple2.Model.Metadata.AiMetadata;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public class BattleState {
    private readonly FieldNpc actor;

    public NodeTargetType TargetType {
        get {
            if (targetNode is null) {
                return NodeTargetType.Near;
            }

            return targetNode.Type;
        }
    }
    public TargetNode? TargetNode {
        get => targetNode;
        set {
            targetNode = value;
            changedTargetType = false;
            keptPastNode = null;
        }
    }
    public bool KeepBattle = false;
    public bool CanBattle = true;
    public bool InBattle { get => TargetId != 0 || KeepBattle; }

    // Why the last target was dropped, for the battle end log line (AiState): "dropped" when the path that dropped it
    // names no reason (SetTarget(null), a ranked pick of nobody), else the reason that path sets after the drop.
    public string LostReason { get; private set; } = "";
    private static readonly ILogger Logger = Log.ForContext<BattleState>();
    public int TargetId => Target?.ObjectId ?? 0;
    public IActor? Target {
        get => target;
        private set {
            if (value != target) {
                keptPastNode = null;
                LostReason = value == null ? "dropped" : "";
                if (actor is FieldNpc npc) {
                    Logger.Verbose("Npc {NpcId} object {ObjectId} target -> {TargetId}", npc.Value.Id, npc.ObjectId, value?.ObjectId ?? 0);
                }
            }
            target = value;
        }
    }
    private IActor? target;
    public IActor? GrabbedUser { get; set; }
    private long nextTargetSearchTick = 0;
    private bool changedTargetType = false;
    private TargetNode? targetNode = null;
    // The target a target node kept because no actor was in its window; the node's window does not let go of it. Any
    // change of target ends it.
    private IActor? keptPastNode = null;

    public BattleState(FieldNpc actor) {
        this.actor = actor;
    }

    // A GM's mob-attack command aims the npc at its player (debug only); the npc's own update may drop it again.
    public void ForceTarget(IActor target) {
        Target = target;
        actor.AppendDebugMessage($"Forced target '{GetTargetName()}'\n");
    }

    // A target node picks the target when it runs, by its own rule (KR AI_Alex_AnchorLizard.xml: a standby every 10 s
    // whose target node, rand 10 to 1500, is commented as the periodic target switch). With no actor in the node's
    // window the npc keeps the target it has, until it is out of the npc's sight (ShouldKeepTarget). A rankAssociate
    // node takes the player at its rank by damage dealt (RankedTarget). A hasAdditional node with
    // noChangeWhenNoTarget takes no target when the npc has none, as in Update.
    public void Retarget() {
        if (!CanBattle || actor.IsDead || actor is FieldPet { OwnerId: 0, TamingPoint: <= 0 }) {
            return;
        }
        if (TargetId == 0 && TargetNode is not null && TargetType == NodeTargetType.HasAdditional && TargetNode.NoChangeWhenNoTarget) {
            return;
        }

        IActor? previous = Target;
        FindNewTarget();
        if (Target is null && previous is not null) {
            Target = previous;
            keptPastNode = previous;
        }
        if (Target != previous) {
            actor.AppendDebugMessage($"New target '{GetTargetName()}' (target node)\n");
        }
    }

    private string GetTargetName() {
        if (Target is null) {
            return "null";
        }

        if (Target is FieldPlayer player) {
            return player.Value.Character.Name;
        }

        if (Target is FieldNpc npc) {
            return npc.Value.Metadata.Name ?? "npc";
        }

        return "unknown";
    }

    public void Update(long tickCount) {
        if (actor.IsDead) {
            Target = null;
            LostReason = "dead";

            return;
        }

        if (TargetId == 0 && TargetNode is not null && TargetType == NodeTargetType.HasAdditional && TargetNode.NoChangeWhenNoTarget) {
            return;
        }

        bool gone = TargetId != 0 && TargetGone();
        if (TargetId != 0 && NpcTargetRules.LosesTarget(gone, !ShouldKeepTarget() || !CanBattle, KeepBattle)) {
            actor.AppendDebugMessage($"Lost target '{GetTargetName()}'\n");
            Target = null;
            LostReason = gone ? "target gone" : CanBattle ? "target out of range" : "cannot battle";
            if (actor is FieldPet { OwnerId: 0 } pet) {
                actor.Field.Broadcast(PetPacket.SyncTaming(0, pet));
            }
        }
        // A battle pet whose owner turned its attack off leaves the fight.
        if (TargetId != 0 && actor is FieldPet { IsBattlePet: true, Battles: false }) {
            Target = null;
            LostReason = "attack turned off";
        }

        bool holdsPath = actor is FieldNpc { OnScriptPath: true } && NpcTargetRules.HoldsScriptPath(actor.Value.Metadata.Basic.Friendly, true);
        if (holdsPath && Target is FieldNpc) {
            actor.AppendDebugMessage($"Lost target '{GetTargetName()}' (script path)\n");
            Target = null;
            LostReason = "script path";
        }

        if (CanBattle && TargetId == 0 && nextTargetSearchTick <= tickCount) {
            if (actor is FieldPet { OwnerId: 0, TamingPoint: <= 0 }) {
                return;
            }
            FindNewTarget();

            nextTargetSearchTick = tickCount + 500;

            if (TargetId != 0) {
                actor.AppendDebugMessage($"New target '{GetTargetName()}'\n");
            }
        }
    }

    public bool IsInRange(float distanceSquared, float verticalOffset, float radiusSquared, float heightUp, float heightDown) {
        return distanceSquared < radiusSquared && verticalOffset <= heightUp && verticalOffset >= heightDown - 10;
    }

    public bool ShouldTargetActor(IActor target, float radiusSquared, float heightUp, float heightDown) {
        float dummyMatch = -1;

        return ShouldTargetActor(target, radiusSquared, heightUp, heightDown, ref dummyMatch, null);
    }

    public bool ShouldTargetActor(IActor target, float radiusSquared, float heightUp, float heightDown, ref float currentBestMatch, List<(float, IActor)>? targetCandidates) {
        Vector3 position = actor.Position;

        if (TargetNode is not null && TargetType == NodeTargetType.Near) {
            if (TargetNode.From == 0 && TargetNode.To == 0) {
                position = TargetNode.Center;
            }
        }

        float distanceSquared = (target.Position - position).LengthSquared();
        float verticalOffset = target.Position.Z - position.Z;

        // A battle pet takes a target in its sight radius that it can get to, whatever the height step (FieldPet.Reaches,
        // chosen here): its height window (sightHeightDown 0 on every KR pet) hid the monsters by its owner below it on a
        // slope or a block down, and showed it one on a ledge it cannot climb.
        if (actor is FieldPet { Battles: true } reachingPet && distanceSquared < radiusSquared && reachingPet.Reaches(target.Position) is { } reaches) {
            if (!reaches) {
                return false;
            }
        } else if (!IsInRange(distanceSquared, verticalOffset, radiusSquared, heightUp, -heightDown)
            // Off the navmesh, a battle pet above its owner also sees what stands in its height window from its owner's
            // height (below, on another floor, the owner's window would show it monsters it cannot reach).
            && !(actor is FieldPet { Battles: true, OwnerPlayer: { } petOwner } && position.Z > petOwner.Position.Z
                 && IsInRange(distanceSquared, target.Position.Z - petOwner.Position.Z, radiusSquared, heightUp, -heightDown))) {
            return false;
        }
        // A battle pet fights only what stands within its battleTraceDistance of its owner.
        if (actor is FieldPet { Battles: true } battlePet && !battlePet.InBattleRange(target.Position)) {
            return false;
        }

        if (TargetNode is null) {
            // A battle pet's AI (Pet/AI_DefaultPetBattle.xml) has no target node, so nothing in it orders the monsters
            // in its sight. Chosen here: a new search takes the nearest (a match test outside a search passes -1 and
            // is not ranked); the pet keeps it after that, as the AI has nothing that changes the target.
            if (actor is FieldPet { Battles: true } && currentBestMatch != -1) {
                if (distanceSquared >= currentBestMatch) {
                    return false;
                }
                currentBestMatch = distanceSquared;
            }
            return true;
        }
        if (scanDealers is not null && !scanDealers.Contains(target.ObjectId)) {
            return false;
        }

        bool canMatch;
        float from = TargetNode.From * TargetNode.From;
        float to = TargetNode.To * TargetNode.To;

        switch (TargetType) {
            case NodeTargetType.Rand:
            case NodeTargetType.RandAssociated:
                // KR: 17 rand, 15 randAssociated and 9 random nodes have no window, so pick among everyone in sight.
                canMatch = InWindow(distanceSquared, from, to);
                if (canMatch && targetCandidates is not null) {
                    targetCandidates.Add((distanceSquared, target));
                }
                return canMatch;

            case NodeTargetType.Near:
            case NodeTargetType.NearAssociated:
                if (currentBestMatch != -1 && distanceSquared >= currentBestMatch) {
                    return false;
                }
                canMatch = InWindow(distanceSquared, from, to);
                if (canMatch && currentBestMatch != -1) {
                    currentBestMatch = distanceSquared;
                }
                return canMatch;

            // The farthest in the window. FindNewTarget starts a far search below any distance (FarthestUnset).
            case NodeTargetType.Far:
                if (currentBestMatch != -1 && distanceSquared <= currentBestMatch) {
                    return false;
                }
                canMatch = InWindow(distanceSquared, from, to);
                if (canMatch && currentBestMatch != -1) {
                    currentBestMatch = distanceSquared;
                }
                return canMatch;

            case NodeTargetType.Mid:
                canMatch = InWindow(distanceSquared, from, to);
                if (canMatch && targetCandidates is not null) {
                    targetCandidates.Add((distanceSquared, target));
                }
                return canMatch;

            // The nearest actor of the node's side (GetTargetType, which FindNewTarget scans) that has the node's
            // additional effect (KR: 55 nodes; their comments: target the player the boss's effect marked, and with
            // none so marked go on to the next target node). An actor without it is no match.
            case NodeTargetType.HasAdditional:
                if (currentBestMatch != -1 && distanceSquared >= currentBestMatch) {
                    return false;
                }
                canMatch = InWindow(distanceSquared, from, to)
                    && target.Buffs.HasBuff(TargetNode.AdditionalId, TargetNode.AdditionalLevel, 0);
                if (canMatch && currentBestMatch != -1) {
                    currentBestMatch = distanceSquared;
                }
                return canMatch;

            case NodeTargetType.GrabbedUser:
                return target == GrabbedUser && distanceSquared >= from && distanceSquared <= to;

            default:
                break;
        }

        return true;
    }

    // A target node with neither from nor to has no window: every actor in sight is a candidate (KR: 687 near nodes
    // name only a center, 4 far nodes and 41 rand nodes nothing). The parser leaves both 0, which as a window held no one.
    private bool InWindow(float distanceSquared, float fromSquared, float toSquared) {
        if (TargetNode is { From: 0, To: 0 }) {
            return true;
        }
        return distanceSquared >= fromSquared && distanceSquared <= toSquared;
    }

    // A far search's best distance before any match: below every distance, and not the -1 that means no search.
    private const float FarthestUnset = float.MinValue;

    // The target died or left the field.
    private bool TargetGone() {
        if (actor.Field.Players.TryGetValue(TargetId, out FieldPlayer? targetPlayer)) {
            return !NpcTargetRules.CanTarget(targetPlayer.IsDead, invisibleFromNpc: false);
        }
        if (actor.Field.Npcs.TryGetValue(TargetId, out FieldNpc? targetNpc) || actor.Field.Mobs.TryGetValue(TargetId, out targetNpc)) {
            return !NpcTargetRules.CanTarget(targetNpc.IsDead, invisibleFromNpc: false);
        }
        return true;
    }

    private bool ShouldKeepTarget() {
        if (changedTargetType || TargetId == 0) {
            return true;
        }

        IActor? target = null;

        if (actor.Field.Players.TryGetValue(TargetId, out FieldPlayer? targetPlayer)) {
            if (!NpcTargetRules.CanTarget(targetPlayer.IsDead, targetPlayer.InvisibleFromNpc)) {
                return false;
            }
            target = targetPlayer;
        } else if (actor.Field.Npcs.TryGetValue(TargetId, out FieldNpc? targetNpc) || actor.Field.Mobs.TryGetValue(TargetId, out targetNpc)) {
            if (!NpcTargetRules.CanTarget(targetNpc.IsDead, invisibleFromNpc: false)) {
                return false;
            }
            target = targetNpc;
        }


        if (target is null) {
            return false;
        }

        if (target == keptPastNode) {
            Vector3 offset = target.Position - actor.Position;
            return IsInRange(offset.LengthSquared(), offset.Z, actor.Value.Metadata.Distance.LastSightRadius * actor.Value.Metadata.Distance.LastSightRadius,
                actor.Value.Metadata.Distance.SightHeightUp, -actor.Value.Metadata.Distance.SightHeightDown);
        }

        return ShouldTargetActor(target, actor.Value.Metadata.Distance.LastSightRadius * actor.Value.Metadata.Distance.LastSightRadius, actor.Value.Metadata.Distance.SightHeightUp, actor.Value.Metadata.Distance.SightHeightDown);
    }

    private int GetTargetType() {
        // A summoned battle pet is on its owner's side: it fights the monsters, never a player.
        if (actor is FieldPet { Battles: true }) {
            return 1;
        }
        int friendlyType = actor.Value.Metadata.Basic.Friendly;

        if (friendlyType != 2 && TargetNode is not null && TargetType == NodeTargetType.HasAdditional) {
            friendlyType = TargetNode.Target switch {
                NodeAiTarget.Hostile => friendlyType == 0 ? 0 : 1, // enemies target players (0), friendlies target enemies (1)
                NodeAiTarget.Friendly => friendlyType == 1 ? 0 : 1, // enemies target enemies (1), friendlies target players (0)
                _ => friendlyType,
            };
        }

        return friendlyType;
    }

    private bool FightsNpc(FieldNpc target) {
        NpcMetadataBasic own = actor.Value.Metadata.Basic;
        NpcMetadataBasic other = target.Value.Metadata.Basic;
        return !target.IsScriptMarker && NpcTargetRules.FightsNpc(own.Friendly, own.AttackGroup, other.Friendly, other.DefenseGroup);
    }

    // The player at the rank node's place by the damage it dealt to this npc (NpcTargetRules.RankedDealer), among
    // the players in the field the npc can target (a summoned pet's damage is its owner's). Null when nobody holds the rank.
    private FieldPlayer? RankedTarget() {
        IEnumerable<(int ObjectId, long Damage)> dealers = actor.CreditedDamage().Where(dealer =>
            actor.Field.Players.TryGetValue(dealer.ObjectId, out FieldPlayer? player) && NpcTargetRules.CanTarget(player.IsDead, player.InvisibleFromNpc));
        int objectId = NpcTargetRules.RankedDealer(dealers, TargetNode?.Rank ?? 1);
        return actor.Field.Players.GetValueOrDefault(objectId);
    }

    // nearAssociated and randAssociated choose among the players who dealt the npc damage (KR: the comment on 15
    // randAssociated nodes: a random target among the players who attacked it; 116 nearAssociated nodes), the
    // dealers rankAssociate ranks. Chosen here, as for rankAssociate: with no such player in the node's reach the node
    // picks as its plain type (near, rand), so an npc nobody has hit still finds a target.
    private HashSet<int>? scanDealers;

    private void FindNewTarget(bool dealersFirst = true) {
        // Chosen here: with nobody at the rank (no player has dealt damage yet), an npc with no target looks for
        // one in its sight as with no node, and Retarget keeps the one it has.
        if (TargetType == NodeTargetType.RankAssociated && RankedTarget() is { } ranked) {
            Target = ranked;
            return;
        }

        scanDealers = null;
        if (dealersFirst && TargetType is NodeTargetType.NearAssociated or NodeTargetType.RandAssociated) {
            scanDealers = actor.CreditedDamage().Select(dealer => dealer.ObjectId).ToHashSet();
            if (scanDealers.Count == 0) {
                scanDealers = null;
            }
        }

        int friendlyType = GetTargetType();
        float sightSquared = actor.Value.Metadata.Distance.Sight;
        float sightHeightUp = actor.Value.Metadata.Distance.SightHeightUp;
        float sightHeightDown = actor.Value.Metadata.Distance.SightHeightDown;

        sightSquared *= sightSquared;

        IActor? nextTarget = null;
        float nextTargetDistance = TargetType == NodeTargetType.Far ? FarthestUnset : float.MaxValue;
        List<(float, IActor)>? candidates = null;

        if (TargetType == NodeTargetType.Rand || TargetType == NodeTargetType.RandAssociated || TargetType == NodeTargetType.Mid) {
            candidates = new List<(float, IActor)>();
        }

        // A summoned pet runs the taming AI too; its npc is hostile, so it would pick its own owner (or another
        // player) as the target to trace and run from. A summoned pet follows its owner instead (FieldPet).
        if (friendlyType == 0 && actor is not FieldPet { OwnerId: not 0 }) {
            foreach (FieldPlayer player in actor.Field.Players.Values) {
                if (!NpcTargetRules.CanTarget(player.IsDead, player.InvisibleFromNpc)) {
                    continue;
                }
                if (ShouldTargetActor(player, sightSquared, sightHeightUp, sightHeightDown, ref nextTargetDistance, candidates)) {
                    nextTarget = player;
                }
            }
        }

        // A monster's side also holds the friendly npcs it fights (NpcTargetRules.FightsNpc): a barrier or an
        // escorted npc a script waits on to be killed.
        int ownFriendly = actor.Value.Metadata.Basic.Friendly;
        if (friendlyType == 0 && ownFriendly == 0 && actor is not FieldPet) {
            foreach (FieldNpc npc in actor.Field.Npcs.Values) {
                if (!FightsNpc(npc) || !NpcTargetRules.CanTarget(npc.IsDead, invisibleFromNpc: false)) {
                    continue;
                }
                if (ShouldTargetActor(npc, sightSquared, sightHeightUp, sightHeightDown, ref nextTargetDistance, candidates)) {
                    nextTarget = npc;
                }
            }
        }

        // The monsters' side: a friendly npc's enemies, a monster's allies. The field keeps the monsters in Mobs
        // (Npcs holds the friendly npcs only).
        // A friendly npc on a script's path does not leave it for a monster (NpcTargetRules.HoldsScriptPath).
        bool holdsPath = actor is FieldNpc { OnScriptPath: true } && NpcTargetRules.HoldsScriptPath(ownFriendly, true);
        if (friendlyType == 1 && !holdsPath) {
            foreach (FieldNpc npc in actor.Field.Mobs.Values) {
                if (npc == actor || (ownFriendly != 0 && !FightsNpc(npc)) || !NpcTargetRules.CanTarget(npc.IsDead, invisibleFromNpc: false)) {
                    continue;
                }

                if (ShouldTargetActor(npc, sightSquared, sightHeightUp, sightHeightDown, ref nextTargetDistance, candidates)) {
                    nextTarget = npc;
                }
            }
        }

        if (TargetType == NodeTargetType.Rand || TargetType == NodeTargetType.RandAssociated) {
            if (candidates!.Count == 0) {
                nextTarget = null;
            } else {
                nextTarget = candidates[Random.Shared.Next(0, candidates.Count)].Item2;
            }
        }

        if (TargetType == NodeTargetType.Mid) {
            if (candidates!.Count == 0) {
                nextTarget = null;
            } else {
                candidates.Sort((entry1, entry2) => entry1.Item1.CompareTo(entry2.Item1));

                nextTarget = candidates[candidates.Count / 2].Item2;
            }
        }

        if (nextTarget is null && scanDealers is not null) {
            FindNewTarget(dealersFirst: false);
            return;
        }
        scanDealers = null;
        Target = nextTarget;
    }
}

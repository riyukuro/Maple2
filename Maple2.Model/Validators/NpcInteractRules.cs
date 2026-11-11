using System.Numerics;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// InteractNpc 0x0032 (i32 npc object id): the client offers the interact key for the nearest npc within the
// InteractionDistance constant that is alive, has an interactFunction and is talkable (InteractNpcAction::FindInteractNpc,
// client/ms2/actionkeyobject.cpp; talkable is CNpc+0xe50). It casts for interactCastingTime, then sends 0x0032
// (CNpc::SendInteractNpc), and drops the cast when the npc is out of that distance. The talkable byte comes in
// FieldAddNpc after the pet uid (OnFieldAddNpc, scenestategame.cpp:3533) and in InteractNpc 0x0059 (i32 npc object
// id, u8 talkable; CSceneStateGame::OnInteractNpc, scenestategame.cpp:3634). The client never reads the function
// itself; every interactFunction of the played KR data is "UseSkill,<skill id>,<level>".
public static class NpcInteractRules {
    public const string UseSkill = "UseSkill";

    // The interact of an npc data row, null when it has no function or one the server does not run.
    public static NpcMetadataInteract? Parse(string? function, int castingTime, int coolTime) {
        if (string.IsNullOrWhiteSpace(function)) {
            return null;
        }
        string[] parts = function.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || parts[0] != UseSkill || !int.TryParse(parts[1], out int skillId) || skillId <= 0
                || !short.TryParse(parts[2], out short level) || level <= 0) {
            return null;
        }
        return new NpcMetadataInteract(skillId, level, Math.Max(0, castingTime), Math.Max(0, coolTime));
    }

    public enum Verdict {
        Ok,
        NoInteract,
        Dead,
        OutOfReach,
        CoolingDown,
    }

    // readyTick: the field tick from which the npc is talkable again. reach is the InteractionDistance constant;
    // InteractRules.InReach adds the slack for the position lag.
    public static Verdict CanUse(NpcMetadataInteract? interact, bool dead, Vector3 player, Vector3 npc, float reach, long readyTick, long now) {
        if (interact == null) {
            return Verdict.NoInteract;
        }
        if (dead) {
            return Verdict.Dead;
        }
        if (!InteractRules.InReach(player, npc, reach)) {
            return Verdict.OutOfReach;
        }
        if (now < readyTick) {
            return Verdict.CoolingDown;
        }
        return Verdict.Ok;
    }

    // The talkable byte of an npc: one with an interact, outside its cool time.
    public static bool Talkable(NpcMetadataInteract? interact, long readyTick, long now) => interact != null && now >= readyTick;
}

using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// ResolvePenalty 0x0044 and TakeBoat 0x0045: one i32, the object id of the npc of the open talk
// (UITalkManager::GetNpcId, 0 when no talk is open). The client sends ResolvePenalty from the talk
// button before it closes the talk, and TakeBoat right after it closed the talk.
public static class NpcTalkRules {
    public const int DoctorKind = 81;

    public static int ReadNpc(IByteReader packet) {
        return packet.ReadInt();
    }

    // The doctor cures only the player whose open talk is with that doctor.
    public static bool PenaltyResolveOpen(int talkNpcObjectId, int npcObjectId, int npcKind) {
        return npcObjectId != 0 && talkNpcObjectId == npcObjectId && npcKind == DoctorKind;
    }
}

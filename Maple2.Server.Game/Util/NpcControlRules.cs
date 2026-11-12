using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// What an NpcControl entry carries for an npc's state (client reader: CNpc::ApplySyncBlock).
public static class NpcControlRules {
    // Bit 1 of the flags byte is the battle flag (the reader hands it to SetEmotion). When it rises the client shows
    // the npc's battle talk line and refreshes its HP bar. KMS2 sends 2 while the npc is in battle or stunned and 0
    // otherwise.
    public const byte BattleFlag = 2;

    public static byte Flags(bool inBattle, bool stunned) => inBattle || stunned ? BattleFlag : (byte) 0;

    // A cast goes out as state 0 with the skill's sequence: the reader leaves the state alone for 0 and plays the
    // sequence. KMS2 never sends state 16 (the client's StateSkill) for an npc.
    public static ActorState WireState(ActorState state) => state == ActorState.PcSkill ? ActorState.None : state;

    // A stunned npc is sent with sequence -1.
    public static short WireSequence(ActorState state, short sequence) => state == ActorState.Stun ? (short) -1 : sequence;

    // The additional effect stun values that put a monster in the stun state. KMS2 sends the state for 1 and 3;
    // nothing shows it for the other values (2, 4..8, 99), so they are left out.
    public static bool Stuns(int stun) => stun is 1 or 3;
}

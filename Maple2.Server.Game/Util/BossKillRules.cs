namespace Maple2.Server.Game.Util;

// The kill notices and the field boss bonus exp. In KMS2 an elite or boss kill (npc class 3, 4, 6 or 7) sends
// AppendMessageKillBoss with the killer's name; a field boss kill (class 4) also sends AppendMessageAssistBonus and an
// ExpUp s_msg_take_assist_bonus_exp after the kill exp. No class 1, 2 or 5 kill sends either.
public static class BossKillRules {
    // No table or constant holds this rate: the one KMS2 field boss kill (lv18, monster exp 5026) gave 1507, 30% rounded
    // down. Chosen here.
    public const double FieldBossBonusRate = 0.3;

    public static bool KillNotice(int npcClass) => npcClass is 3 or 4 or 6 or 7;

    public static bool FieldBoss(int npcClass) => npcClass == 4;

    // In KMS2 a party member who had hit an elite and died 1.5 s before its death got no kill ExpUp, and still got its
    // own meso drop. Chosen here: the field boss bonus exp and its notice follow the kill exp (no sample of one).
    public static bool DealerGetsExp(bool dead) => !dead;

    // The bonus is taken from the kill exp before the exp rate (the party buff, gear options).
    public static long FieldBossBonus(long killExp) => killExp <= 0 ? 0 : (long) (killExp * FieldBossBonusRate);
}

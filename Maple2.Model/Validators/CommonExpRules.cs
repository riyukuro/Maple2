using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// The common exp table gives every exp type a curve (an exp per level) and a factor; the client builds
// the same per-type curve (CExpTable::LoadCommonExp, exptable.cpp:266). Which level picks the row of the
// curve depends on the type.
public static class CommonExpRules {
    public enum LevelSource { None, Character, Field }

    public static LevelSource Level(ExpType expType) {
        switch (expType) {
            case ExpType.fishing:
            case ExpType.musicMastery1:
            case ExpType.musicMastery2:
            case ExpType.musicMastery3:
            case ExpType.musicMastery4:
            case ExpType.manufacturing:
            case ExpType.gathering:
            case ExpType.arcade:
            case ExpType.expDrop:
            case ExpType.miniGame:
            case ExpType.userMiniGame:
            // Guild check-in: the member's own level; the guild's attendUserExpFactor (guilddef_2.cpp:349)
            // scales the result.
            case ExpType.guildUserExp:
            // A rank duel's win or loss: the duelist's own level (the client's commonexptable rows rankDuelWin and
            // rankDuelLose, on the level curve of their expTableID).
            case ExpType.rankDuelWin:
            case ExpType.rankDuelLose:
                return LevelSource.Character;
            case ExpType.taxi:
            case ExpType.mapCommon:
            case ExpType.mapHidden:
            case ExpType.telescope:
            case ExpType.normalChest:
            case ExpType.rareChest:
            case ExpType.rareChestFirst:
                return LevelSource.Field;
            default:
                return LevelSource.None;
        }
    }

    // The field's level, but never above the character's.
    public static int FieldLevel(int fieldLevel, int characterLevel) {
        return fieldLevel > characterLevel ? characterLevel : fieldLevel;
    }

    // The exp of one award: the curve's value for the level, times the caller's rate, times the type's factor.
    public static long Amount(long expValue, float modifier, float factor, long additionalExp) {
        return (long) ((expValue * modifier) * factor) + additionalExp;
    }
}

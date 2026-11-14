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
            // The individual arenas' results: the Red Arena's champion and the others (redDuelWin, redDuelLose) and the
            // free-for-all's places (bloodMineRank1 to 3, bloodMineRankOther), at the player's own level as the rank
            // duel's rows.
            case ExpType.redDuelWin:
            case ExpType.redDuelLose:
            case ExpType.bloodMineRank1:
            case ExpType.bloodMineRank2:
            case ExpType.bloodMineRank3:
            case ExpType.bloodMineRankOther:
            // Treasure Island's team result (btiTeamWin, btiTeamLose), at the player's own level as the other arenas'.
            case ExpType.btiTeamWin:
            case ExpType.btiTeamLose:
            // Decorating a home (ConstructExpRules): the decorator's own level.
            case ExpType.construct:
            // A pet capture: the tamer's own level, as the other life skills.
            case ExpType.petTaming:
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

    // The special attribute that raises a type's exp, as a fraction (0.01 = +1%): fishing (s_item_opt_sa_improve_acquire_
    // fishing_exp), arcade clears (_arcade_exp) and performing (_playinstrument_exp). The client only names them in item
    // tooltips. KR sets them together on 10000061 (+1% at level 1 to +2.4%).
    public static SpecialAttribute? RateAttribute(ExpType expType) {
        return expType switch {
            ExpType.fishing => SpecialAttribute.FishingExp,
            ExpType.arcade => SpecialAttribute.ArcadeExp,
            ExpType.musicMastery1 or ExpType.musicMastery2 or ExpType.musicMastery3 or ExpType.musicMastery4 =>
                SpecialAttribute.PlayInstrumentExp,
            _ => null,
        };
    }

    // The caller's rate raised by the attribute's rate; a rate below -100% gives nothing, as a kill's (KillRate).
    public static float WithRate(float modifier, double rate) {
        return (float) (modifier * Math.Max(0d, 1d + rate));
    }

    // The exp of one award: the curve's value for the level, times the caller's rate, times the type's factor.
    public static long Amount(long expValue, float modifier, float factor, long additionalExp) {
        return (long) ((expValue * modifier) * factor) + additionalExp;
    }
}

namespace Maple2.Model.Validators;

// CharacterAbility (the ability window). The client's own tests are in client/ms2/uicharacterabilitydialog.cpp
// and commonfiles/characterability.cpp; the server repeats them because the client only hides the buttons.
public static class CharacterAbilityRules {
    public const byte Reset = 1;
    public const byte Learn = 2;
    public const byte LearnAll = 3; // sent by a GM chat command only

    // The level the feature starts at (CFeatureManager, 0x32).
    public const int OpenLevel = 50;

    // The client opens the window at level 50 with the open quest rewarded (uicharacterabilitydialog.cpp:388).
    public static bool IsOpen(int level, bool openQuestDone) {
        return level >= OpenLevel && openQuestDone;
    }

    // characterability.cpp:280: one point per level above 50, plus the default points and the bonus points
    // of the load packet.
    public static int TotalPoints(int level, int defaultPoints, int bonusPoints) {
        return level - OpenLevel + defaultPoints + bonusPoints;
    }

    public static int FreePoints(int level, int defaultPoints, int bonusPoints, int learned) {
        return TotalPoints(level, defaultPoints, bonusPoints) - learned;
    }

    // Mode 2: the ability is a table row (requireLevel < 0 when it is not), not learned yet, its level is
    // reached and a point is free (uicharacterabilitydialog.cpp:408-425).
    public static bool CanLearn(int requireLevel, bool learned, int level, int freePoints) {
        return requireLevel >= 0 && !learned && requireLevel <= level && freePoints > 0;
    }

    // Seconds until the next reset is allowed: the cool time runs from the last reset; 0 = never reset.
    // The client compares whole minutes (uicharacterabilitydialog.cpp:276-287).
    public static long ResetWait(long lastReset, long now, int coolTimeMinutes) {
        if (lastReset == 0) {
            return 0;
        }
        long elapsed = now - lastReset;
        if (elapsed / 60 >= coolTimeMinutes) {
            return 0;
        }
        return coolTimeMinutes * 60L - elapsed;
    }

    public enum ResetStep { Refuse, Reset }

    // Mode 1: something is learned, the cool time is over and the meso price is held
    // (uicharacterabilitydialog.cpp:430-448, 495-508).
    public static ResetStep CanReset(int learned, long wait, long meso, long price) {
        if (learned <= 0 || wait > 0 || price < 0 || meso < price) {
            return ResetStep.Refuse;
        }
        return ResetStep.Reset;
    }

    // The effects of the learned abilities that the player does not have. They have no duration, so the
    // logout save does not keep them; the server gives them on learn and at every field entry.
    public static List<(int Id, short Level)> MissingEffects(IEnumerable<int> learned,
            Func<int, (int Id, short Level)?> effectOf, Func<int, short, bool> has) {
        var missing = new List<(int Id, short Level)>();
        foreach (int abilityId in learned) {
            (int Id, short Level)? effect = effectOf(abilityId);
            if (effect is not { Id: > 0 } value || has(value.Id, value.Level) || missing.Contains(value)) {
                continue;
            }
            missing.Add(value);
        }
        return missing;
    }

    // Mode 3 (GM chat command LearnAllAbility, usercommandcli.cpp:1958): every table ability not learned yet, in id
    // order. Chosen here: level, points and the open quest are not tested, as for a GM tool.
    public static List<int> LearnAllIds(IEnumerable<int> tableIds, ICollection<int> learned) {
        var ids = new List<int>();
        foreach (int id in tableIds.Order()) {
            if (!learned.Contains(id) && !ids.Contains(id)) {
                ids.Add(id);
            }
        }
        return ids;
    }
}

namespace Maple2.Server.Game.Util;

// Rules for GM debug buttons the client only shows in debug mode (GM level 50 or more).
public static class DebugButtonRules {
    // Mapleopoly 5: the client sends the two dice only when both are 1-6 (UIBlueMarbleGameDialog, onClickDebugDice).
    public static bool DiceAllowed(int dice1, int dice2) {
        return dice1 is >= 1 and <= 6 && dice2 is >= 1 and <= 6;
    }

    // One roll: the token moves by the sum and a double gives one free roll.
    public static (int TotalSlot, int FreeRoll) AfterRoll(int totalSlot, int freeRoll, int dice1, int dice2) {
        return (totalSlot + dice1 + dice2, dice1 == dice2 ? freeRoll + 1 : freeRoll);
    }

    // Achieve 5 buttons: 0 onDbgUp, 1 onDbgDown, 2 onDbgUp30, 3 onDbgUp99 (UIAchieveDialog).
    // The server's reading of the names: up finishes the grade in progress, up30 and up99 set the counter to 30 %
    // and 99 % of that grade's goal, down is handled apart (one grade back).
    public static bool KnownAchieveButton(byte button) {
        return button <= 3;
    }

    public static bool TryUpCounter(byte button, long goal, out long counter) {
        counter = button switch {
            0 => goal,
            2 => goal * 30 / 100,
            3 => goal * 99 / 100,
            _ => -1,
        };
        return counter >= 0 && goal > 0;
    }

    // RequestItemEnchant 15 ("onForceEnchant"): the staged enchant succeeds; an item at the last step has none left.
    public static bool ForceEnchantAllowed(int enchants) {
        return enchants is >= 0 and < 15;
    }

    // RequestItemEnchant 16: m_debugBtnFillIngredient sends 0, m_debugBtnFillIngredientUnit sends 10.
    // The server's reading of the names: 0 tops the bag up to what the staged enchant needs, a number gives
    // that many of each ingredient.
    public static int FillAmount(int requested, int need, int held) {
        if (requested < 0 || need <= 0) {
            return 0;
        }
        return requested > 0 ? requested : Math.Max(0, need - Math.Max(0, held));
    }

    // Enchant materials (onyx, chaos onyx, crystal fragment) sit in this item id range.
    public const int MaterialIdFirst = 40100001;
    public const int MaterialIdLast = 40100040;

    // RequestItemEnchant 17 (m_debugBtnFillFailStack) and 18 (m_debugBtnUseAllFailStack): the item's stored
    // fail charges become enough for a full charge rate, or none.
    public static int FailCharges(bool fill, int maxRate, int chargeRate) {
        if (!fill || chargeRate <= 0) {
            return 0;
        }
        return (maxRate + chargeRate - 1) / chargeRate;
    }
}

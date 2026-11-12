namespace Maple2.Model.Metadata;

// table/weaponaction.xml, as CWeaponActionTable::Load reads it (weaponactiontable.cpp:76): the weapon actions, one per
// pair of right and left hand weapon subtypes (<weapon rh lh normalAttack>), and the left hand subtypes allowed
// with a right hand subtype (<allowWeapon rh lh>, a right hand without a row allowing any left hand).
public record WeaponActionTable(
    IReadOnlyList<WeaponActionTable.Action> Actions,
    IReadOnlyDictionary<int, int[]> Allows) : Table {
    public record Action(int RightHand, int LeftHand, int NormalAttack);

    // CWeaponActionTable::IsAllowed (weaponactiontable.cpp:124).
    public bool IsAllowed(int rightHand, int leftHand) {
        return !Allows.TryGetValue(rightHand, out int[]? leftHands) || leftHands.Contains(leftHand);
    }

    // CWeaponActionTable::FindAction (weaponactiontable.cpp:111): the first action of exactly these hands.
    public Action? Find(int rightHand, int leftHand) {
        return Actions.FirstOrDefault(action => action.RightHand == rightHand && action.LeftHand == leftHand);
    }

    // The weapon action of the hands held, as PC::UpdateWeaponAction (pc.cpp:2839) picks it: the left hand counts as
    // empty when the pair is not allowed; null when no action has these hands.
    public Action? Held(int rightHand, int leftHand) {
        return Find(rightHand, IsAllowed(rightHand, leftHand) ? leftHand : 0);
    }
}

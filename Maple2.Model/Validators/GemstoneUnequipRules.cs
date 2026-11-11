namespace Maple2.Model.Validators;

// ItemSocketSystem mode 10 (take a gemstone out of a socket): i64 gear uid, u8 socket slot.
// The price is taken last: a gemstone that cannot be made or has no free slot to go to costs nothing.
public static class GemstoneUnequipRules {
    public enum Result {
        Ok,
        NoGemstone,
        NoRoom,
        NotPaid,
    }

    public static Result TakeOut(bool gemstoneMade, bool hasRoom, Func<bool> pay) {
        if (!gemstoneMade) {
            return Result.NoGemstone;
        }
        if (!hasRoom) {
            return Result.NoRoom;
        }
        return pay() ? Result.Ok : Result.NotPaid;
    }
}

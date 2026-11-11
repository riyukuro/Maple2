namespace Maple2.Model.Validators;

// Fishing 0x0073 mode 8 (catch): u8 result. The client sends 1 once the bite tick the server gave it
// in the start answer has passed (or the result of the reel game after that), one catch per cast.
// The server decided at the start whether the fish bites and when: a catch is only a success when
// that cast had a bite and its tick has come.
public static class FishingRules {
    // Room for the gap between the client's server clock and the field's tick.
    public const int BiteSlackTicks = 1000;

    // Fishing start: whether this cast opens the reel game. Decided anew for every cast, from this cast's
    // bite and roll only, so the start answer never promises a game to a cast whose fish does not bite.
    public static bool IsFightGame(bool bite, bool autoFish, int roll, int fightChance) {
        return bite && !autoFish && roll < fightChance;
    }

    public static bool IsCatch(bool clientSuccess, bool bite, long nowTick, long biteTick) {
        return clientSuccess && bite && nowTick >= biteTick - BiteSlackTicks;
    }
}

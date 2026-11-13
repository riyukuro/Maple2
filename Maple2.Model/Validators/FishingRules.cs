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

    // A result 0 on a cast without the reel game is the casting bar running out before the bite tick: the client
    // sends result 1 right after (CFishingManager::Update) and KMS2 answers that one alone.
    public static bool IsIgnoredResult(bool clientSuccess, bool fightGame) {
        return !clientSuccess && !fightGame;
    }

    public static bool IsCatch(bool clientSuccess, bool bite, long nowTick, long biteTick) {
        return clientSuccess && bite && nowTick >= biteTick - BiteSlackTicks;
    }

    // Fishing 0x0073 mode 10 (auto fishing off): the client keeps the auto flag of the last catch answer (mode 8) and
    // recasts and uses bait by itself while it is on. When the last AutoFish buff ends while the player fishes, mode 10 with 0 turns
    // the flag off at once. The start of the buff is the client's own: CFishingManager::OnNotifyChangeBuff recasts
    // when a buff of event type 1 is added, so the server sends nothing then (a mode 10 with 1 would cast twice).
    // Chosen here: mode 10 at the buff's end (no client code or data says when the server sends it).
    public static bool AutoFishEndNotice(bool removedAutoFish, bool stillAutoFish, bool fishing) {
        return removedAutoFish && !stillAutoFish && fishing;
    }
}

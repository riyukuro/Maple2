using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// The party buffs of the server constants (partyBuffID0, partyBuffID1). KMS2 gives a party member who plays near
// other members of the party the monster exp and meso buff (70000066) and the prize fish buff (90000309). Their three
// levels say how many members are near: one, two, three or more (the fishing buff's text says so outright).
// "Near" is the same field here: the client gets the buff from the server and has no distance of its own.
public static class PartyBuff {
    public const int MaxLevel = 3;

    /// <param name="othersHere">The other members of the player's party in the player's field.</param>
    /// <returns>The buff level, 0 for no buff.</returns>
    public static short Level(int othersHere) => (short) Math.Clamp(othersHere, 0, MaxLevel);

    // The returning-user party buff (returnUserPartyBuffID0, KMS2 90000685 "Merin's Hero": monster exp +15%, monster
    // meso +30%, prize fish +7%, revival fee -50%; it never ends by itself, keepCondition 99). The client only shows
    // its tooltip on the mentoring buff page; no table says who gets it. Chosen here: a party member while a season
    // runs and the player's field holds a registered returning character (role 2) of the party, the player or one
    // of at least one other member there, as the party buffs above count members near. returnUserPartyBuffID1
    // (90000686) has no stat and no text and is not given.
    /// <param name="self">The player's mentoring role.</param>
    /// <param name="othersHere">The roles of the other members of the player's party in the player's field.</param>
    public static bool ReturnUser(MentorRole self, IReadOnlyCollection<MentorRole> othersHere) =>
        othersHere.Count > 0 && (self == MentorRole.Mentee || othersHere.Contains(MentorRole.Mentee));
}

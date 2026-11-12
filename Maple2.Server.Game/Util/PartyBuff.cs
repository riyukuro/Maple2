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
}

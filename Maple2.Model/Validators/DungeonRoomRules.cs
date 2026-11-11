namespace Maple2.Model.Validators;

// RoomDungeon 0x002B mode 2 (create): i32 dungeon id, bool with party, i32 portal id, bool use ticket.
// The client's room window enables the solo button only for a dungeon without a minimum user
// count (or in its debug state) and the party button only for a party in a dungeon that holds
// more than one user.
public static class DungeonRoomRules {
    public enum CreateVerdict {
        Ok,
        UnderMinUserCount,
        OverMaxUserCount,
    }

    public static CreateVerdict CanCreate(bool withParty, int partySize, int minUserCount, int maxUserCount, bool gm) {
        if (!withParty) {
            return minUserCount == 0 || gm ? CreateVerdict.Ok : CreateVerdict.UnderMinUserCount;
        }
        if (maxUserCount <= 1 || partySize > maxUserCount) {
            return CreateVerdict.OverMaxUserCount;
        }
        return CreateVerdict.Ok;
    }
}

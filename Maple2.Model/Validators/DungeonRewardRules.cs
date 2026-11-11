namespace Maple2.Model.Validators;

// DungeonReward from the client: one byte, the mode, and nothing else.
// Mode 6 (uidungeonrewarddialog.cpp:553): the reward window is closed in a room whose isLeaveAfterCloseReward is set;
// the window's close button reads "leave", and the player expects to be taken out of the dungeon.
// Mode 5 (uipartydialog.cpp:787): the party leader's confirmed "give up the dungeon". What the party is shown and
// where it goes is not in the client, so the mode is read and not answered.
public static class DungeonRewardRules {
    public const byte GiveUp = 5;
    public const byte Leave = 6;

    // The sender leaves: only from a dungeon room that leaves on close, and only once the dungeon has ended
    // (the reward window is shown at the end).
    public static bool Leaves(byte mode, bool inDungeon, bool leaveAfterCloseReward, bool ended) {
        return mode == Leave && inDungeon && leaveAfterCloseReward && ended;
    }
}

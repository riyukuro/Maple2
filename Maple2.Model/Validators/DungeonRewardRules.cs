namespace Maple2.Model.Validators;

// DungeonReward from the client: one byte, the mode, and nothing else.
// Mode 6 (uidungeonrewarddialog.cpp:553): the reward window is closed in a room whose isLeaveAfterCloseReward is set;
// the window's close button reads "leave", and the player expects to be taken out of the dungeon.
// Mode 5 (UIPartyDialog::SendDungeonReward, uipartydialog.cpp:787): the party leader's confirmed "give up the
// dungeon". The party window shows the button only to the leader while the stage flag is set (stage 0 and the
// script's give-up flag, UpdateGiveUpDungeonButton :1168), and its confirm box (s_party_dungeon_giveup) says the
// challenge fails for every party member. The client has two answers: DungeonMission mode 5
// (RoomStageDungeonCli::OnGiveUp, the s_party_notify_dungeon_giveup notice) and the RoomDungeon error 0x18
// (s_room_dungeon_cannot_giveup_yet, DungeonRecordManagerCli::OnError).
public static class DungeonRewardRules {
    public const byte GiveUp = 5;
    public const byte Leave = 6;

    public enum GiveUpResult {
        Ignored,
        TooEarly,
        GiveUp,
    }

    // Only the party leader in a dungeon room (not the lobby) that has no result yet gives up; the room fails. Before the
    // room's script allows it (dungeon_enable_give_up) the answer is the "too early" error.
    public static GiveUpResult GiveUpOf(byte mode, bool inDungeonRoom, bool isLeader, bool ended, bool enabled) {
        if (mode != GiveUp || !inDungeonRoom || !isLeader || ended) {
            return GiveUpResult.Ignored;
        }
        return enabled ? GiveUpResult.GiveUp : GiveUpResult.TooEarly;
    }

    // The sender leaves: only from a dungeon room that leaves on close, and only once the dungeon has ended
    // (the reward window is shown at the end).
    public static bool Leaves(byte mode, bool inDungeon, bool leaveAfterCloseReward, bool ended) {
        return mode == Leave && inDungeon && leaveAfterCloseReward && ended;
    }
}

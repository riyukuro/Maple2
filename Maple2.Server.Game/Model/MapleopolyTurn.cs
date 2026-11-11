using Maple2.Model.Enum;

namespace Maple2.Server.Game.Model;

// The state of a player's Mapleopoly turn between a roll and its result. Kept in the session only: a result that
// is still open when the player logs out is not paid.
public sealed class MapleopolyTurn {
    private bool resultPending;

    public void Rolled() {
        resultPending = true;
    }

    // A result that cannot be paid (the tile's item cannot be made) is left open instead of taken.
    public bool ResultPending => resultPending;

    // True when a result may be paid now. A roll opens one result. After a backward or forward tile the client
    // asks for the result of the tile it walked to (UIBlueMarbleGameDialog, uibluemarblegamedialog.cpp:234, :244),
    // so those two keep the result open; every other tile closes it.
    public bool TakeResult(BlueMarbleSlotType tile) {
        if (!resultPending) {
            return false;
        }

        resultPending = tile is BlueMarbleSlotType.Backward or BlueMarbleSlotType.Forward;
        return true;
    }

    // The client keeps one running position and never wraps it below the start (it divides it by the board
    // size for the lap count, uibluemarblegamedialog.cpp:491), so a backward move stops at 0.
    public static int MoveBackward(int totalSlotValue, int moveAmount) {
        return Math.Max(0, totalSlotValue - moveAmount);
    }
}

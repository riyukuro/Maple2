using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// FunctionCube 7: the client closed the dialog it opened from a function cube.
public static class FunctionCubeCloseRule {
    // True when the close hands the cube back: the closer is the one who holds it in use.
    public static bool Releases(InteractCubeControlType type, InteractCubeState state, InteractCubeState defaultState, long holderId, long closerId) {
        switch (type) {
            case InteractCubeControlType.Farming:
            case InteractCubeControlType.Breeding:
            case InteractCubeControlType.Nurturing:
            case InteractCubeControlType.Switch:
                return false; // no dialog; a switch keeps its state
        }

        return state == InteractCubeState.InUse && defaultState != InteractCubeState.InUse && holderId != 0 && holderId == closerId;
    }
}

using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// The state a use takes a function cube to from its default state.
public static class FunctionCubeUseRule {
    // A FunctionUI cube (a bank, the trigger tool, a cube sub scene, the home password and summon) opens its dialog only
    // in its second state (2, or its move 5): CFunctionCubeService's use reply calls OpenSubKindDialog
    // (functioncubeobject.cpp:2440-2443), which returns unless FUN_14122e6d0 (functioncubeobject.cpp:464-467) finds
    // state 2 or 5. Every FunctionUI cube of the data has DefaultState 1 and its open sequences on the second state
    // (60000001: SecondIngSeq Opening_A, SecondSeq Opened_A). Any other cube is taken to 1.
    public static InteractCubeState TakenState(InteractCubeControlType type) =>
        type == InteractCubeControlType.FunctionUI ? InteractCubeState.Available : InteractCubeState.InUse;

    // The use reply's flag (FunctionCube 6): set while the cube is in its taken state.
    public static bool IsTaken(InteractCubeControlType type, InteractCubeState state) => state == TakenState(type);
}

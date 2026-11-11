using System.Numerics;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// InteractObject 0x0031 modes 11 (start) and 12 (end): i32 count, then that many object names.
// The client sends the start list when the interaction begins and, when it ends, the end list
// with those of the same objects that can still be used.
public static class InteractRules {
    // Server rule: the names kept from one start packet.
    public const int MaxStarted = 64;

    public static List<string> ReadNames(IByteReader packet) {
        int count = packet.ReadInt();
        var names = new List<string>();
        for (int i = 0; i < count; i++) {
            names.Add(packet.ReadString());
        }
        return names;
    }

    // Server rule: room for the lag between the client's position and the one the server holds (one block).
    public const float ReachSlack = 150f;
    // Server rule: the reach of an object that is not gathered. The client tests the object row's own
    // interactionDistance, which the server's object data does not carry.
    public const float OtherReach = 1500f;

    // The client lists a gathered object or a farming cube only within the MasteryObjectInteractionDistance
    // constant of the character. A reach of 0 or less (constant not loaded) tests nothing.
    public static bool InReach(Vector3 player, Vector3 target, float reach) {
        if (reach <= 0) {
            return true;
        }
        return Vector3.Distance(player, target) <= reach + ReachSlack;
    }

    // FunctionCube 0x002E: the client's farming state sends mode 4 (begin) for the cubes in reach and mode 5
    // (use) for the same cubes when the cast completes. Furniture and nurturing uses send mode 5 alone.
    public static bool NeedsBegin(InteractCubeControlType type) {
        return type is InteractCubeControlType.Farming or InteractCubeControlType.Breeding;
    }

    // An end is taken only for an object the last start packet named.
    public static bool CanEnd(ICollection<string> started, string name) {
        return started.Contains(name);
    }
}

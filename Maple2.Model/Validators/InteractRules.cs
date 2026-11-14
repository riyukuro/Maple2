using System.Numerics;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// InteractObject 0x0031 modes 11 (start) and 12 (end): i32 count, then that many object names.
// Gathered objects (category 6): the start and the end list come from the same list of objects in reach
// (stateinteractdata.cpp:278-289). Other objects: the start names the nearest enabled object whose range
// covers the character (stateinteractdata.cpp:268, worldservice_1.cpp:4557), the end names the object the
// player chose (mouseinteract.cpp:451), so the two can differ and an end can come with no start.
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
    // Server rule: the reach of an object that is not gathered when its row's interactionDistance is not loaded
    // (data from before the ingest read it, or a row of 0).
    public const float OtherReach = 1500f;

    // Liftable 1: the client lifts only an object within its ActionDistance (LiftCubeAction::FUN_140957890,
    // actionkeyobject.cpp:1320; CLiftableObject::BindEntity, liftableobject.cpp:191). The flat default is 150 and the
    // KR xblocks set 120 (100 objects) or 150 (6); the server keeps no per-object value, so it allows the largest.
    public const float LiftReach = 150f;

    // NpcTalk 1: the client talks only to an npc FindNearestNpc finds (MyPC::FindTalkableNpc, mypc.cpp:2827): one whose
    // 3D distance is within the TalkableDistance constant (default 400) plus the npc's capsule radius
    // (worldservice_1.cpp:6438). The action key and the mouse pick of an npc both go through it.
    public const int DefaultTalkableDistance = 400;
    public static float TalkReach(int talkableDistance, float capsuleRadius) =>
        (talkableDistance > 0 ? talkableDistance : DefaultTalkableDistance) + Math.Max(0f, capsuleRadius);

    // The reach of an object that is not gathered: the client picks it and keeps the interaction only within the
    // row's interactionDistance (WorldService::FUN_141bc6090, worldservice_1.cpp:4567; stateinteractdata.cpp:382;
    // KR rows 70 to 900, most 155).
    public static float ObjectReach(float rowDistance) => rowDistance > 0 ? rowDistance : OtherReach;

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
    // KMS2 sends no state update (4) for the end of a telescope or a UI object: it stays usable.
    public static bool KeepsState(InteractType type) => type is InteractType.Telescope or InteractType.Ui;

    // What KMS2 answers the end of an interaction with, after the object's state update if its state changes:
    // InteractObject 5 (the interaction) and 13 (the result). A mesh object gets the result alone; a telescope,
    // a UI object and a gathered object get both; the other kinds get neither.
    public static (bool Interact, bool Result) Answer(InteractType type) {
        return type switch {
            InteractType.Mesh => (false, true),
            InteractType.Telescope or InteractType.Ui or InteractType.Gathering => (true, true),
            _ => (false, false),
        };
    }

    public static bool NeedsBegin(InteractCubeControlType type) {
        return type is InteractCubeControlType.Farming or InteractCubeControlType.Breeding;
    }

    // The names one start packet leaves in the session's set: the old names go, each named object the
    // field has within reach is kept, MaxStarted at most. lookup gives null for a name the field does
    // not have, else whether the object is in reach.
    public static void TakeStarts(ISet<string> started, IEnumerable<string> names, Func<string, bool?> lookup) {
        started.Clear();
        foreach (string name in names) {
            if (started.Count >= MaxStarted) {
                break;
            }
            if (lookup(name) == true) {
                started.Add(name);
            }
        }
    }

    // The ends to take from one end packet, each name once. A gathered object needs its name in the last
    // start packet and is taken off it; any other object needs only to be in reach. lookup gives null for
    // a name the field does not have.
    public static List<string> TakeEnds(ISet<string> started, IEnumerable<string> names, Func<string, (bool Gathered, bool InReach)?> lookup) {
        var ends = new List<string>();
        var seen = new HashSet<string>();
        foreach (string name in names) {
            if (seen.Count >= MaxStarted) {
                break;
            }
            if (!seen.Add(name) || lookup(name) is not { } found) {
                continue;
            }
            if (found.Gathered ? started.Remove(name) : found.InReach) {
                ends.Add(name);
            }
        }
        return ends;
    }

    // Server rule: an object's npc spawn list (interactobject spawn, which the client reads and never uses, so the
    // spawn is the server's). One roll in [0, 10000) takes the first entry whose running sum of probabilities is
    // above it; a list summing under 10000 spawns nothing for the rest (a single 3000 entry spawns 30% of uses).
    // -1: nothing spawns.
    public const int SpawnRollRange = 10000;

    public static int PickSpawn(IReadOnlyList<int> probabilities, int roll) {
        int sum = 0;
        for (int i = 0; i < probabilities.Count; i++) {
            sum += Math.Max(probabilities[i], 0);
            if (roll < sum) {
                return i;
            }
        }
        return -1;
    }
}

using System.Numerics;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// Rules of a placed reactor (flower pot, chocolate). The cell and the name are the client's
// (CheckReactorPlacement); the client does not show when a state ends, so the timeline is the server's
// reading of reactorState.xml.
public static class ReactorRules {
    public const float CellSize = 150f;
    // CheckReactorPlacement: no interact object and no friendly NPC closer than this to the cell centre.
    public const float PlacementDistance = 230f;
    // Server limits the client does not have: how long a state without times lasts, and how many
    // reactors one field holds.
    public const long NoTimeLimitMs = 10 * 60 * 1000;
    public const int MaxPerField = 100;

    public enum Step {
        Stay,
        Advance,
        Remove,
    }

    // The grid cell of a position: x and y round to the nearest cell, z rounds down.
    public static (int X, int Y, int Z) Cell(Vector3 position) {
        int x = (int) ((MathF.Abs(position.X) + CellSize / 2) / CellSize);
        int y = (int) ((MathF.Abs(position.Y) + CellSize / 2) / CellSize);
        int z = (int) (position.Z / CellSize);
        if (position.X < 0) x = -x;
        if (position.Y < 0) y = -y;
        if (position.Z < 0) z--;
        return (x, y, z);
    }

    public static int CellKey((int X, int Y, int Z) cell) {
        return (cell.X & 0xff) + ((cell.Y & 0xff) << 8) + ((cell.Z & 0xff) << 16);
    }

    public static Vector3 CellCenter((int X, int Y, int Z) cell) {
        return new Vector3(cell.X * CellSize, cell.Y * CellSize, cell.Z * CellSize);
    }

    // The client looks a reactor up by this name to see whether the cell is taken.
    public static string Name(int cellKey) {
        return $"Reactor_{cellKey}";
    }

    // nextTimeSec: the state moves on after that long. expireTimeSec: the state ends; a connect state with
    // at least one connected player moves on, any other state removes the reactor. Past the last state the
    // reactor is removed.
    // A state with neither time would never end; it is removed after NoTimeLimitMs.
    public static Step Tick(ReactorTable.State state, bool isLast, bool anyConnected, long elapsedMs) {
        if (state.NextTimeSec <= 0 && state.ExpireTimeSec <= 0) {
            return elapsedMs >= NoTimeLimitMs ? Step.Remove : Step.Stay;
        }
        if (state.NextTimeSec > 0 && elapsedMs >= state.NextTimeSec * 1000L) {
            return isLast ? Step.Remove : Step.Advance;
        }
        if (state.ExpireTimeSec > 0 && elapsedMs >= state.ExpireTimeSec * 1000L) {
            return state.Type == ReactorStateType.Connect && anyConnected && !isLast ? Step.Advance : Step.Remove;
        }
        return Step.Stay;
    }

    // CReactor::CanUse: a connect state takes players who are not connected, a reward state only connected ones.
    public static bool CanReact(ReactorStateType type, bool connected) {
        return type switch {
            ReactorStateType.Connect => !connected,
            ReactorStateType.Reward => connected,
            _ => false,
        };
    }

    // True when none of the points is closer to the cell centre than PlacementDistance.
    public static bool IsClear(Vector3 center, IEnumerable<Vector3> points) {
        foreach (Vector3 point in points) {
            if (Vector3.Distance(point, center) < PlacementDistance) {
                return false;
            }
        }
        return true;
    }

    // True when the point lies in the box of the given size centred on the position (a portal's box).
    public static bool InBox(Vector3 point, Vector3 position, Vector3 dimension) {
        return MathF.Abs(point.X - position.X) <= dimension.X / 2
               && MathF.Abs(point.Y - position.Y) <= dimension.Y / 2
               && MathF.Abs(point.Z - position.Z) <= dimension.Z / 2;
    }

    // Which stacks pay a state's conditions. Null when the player does not hold enough.
    // Every condition sets aside the amount it needs to be held (Needed) from stacks no other condition
    // has set aside, so two conditions on the same item add up, also when one names a rarity and the
    // other does not. Conditions with a rarity pick first; the Consume part of each is what is taken.
    public static List<(long Uid, int Amount)>? Plan(IEnumerable<ReactorTable.ConditionItem> conditions, IReadOnlyList<(long Uid, int ItemId, int Rarity, int Amount)> stacks) {
        int[] free = stacks.Select(stack => stack.Amount).ToArray();
        var take = new Dictionary<long, int>();
        foreach (ReactorTable.ConditionItem condition in conditions.Where(condition => condition.ItemId != 0).OrderByDescending(condition => condition.Rarity > 0)) {
            int need = Needed(condition);
            int consume = condition.Consume;
            for (int i = 0; i < stacks.Count && need > 0; i++) {
                if (stacks[i].ItemId != condition.ItemId || (condition.Rarity > 0 && stacks[i].Rarity != condition.Rarity)) {
                    continue;
                }

                int reserved = Math.Min(free[i], need);
                if (reserved <= 0) {
                    continue;
                }
                free[i] -= reserved;
                need -= reserved;

                int taken = Math.Min(reserved, consume);
                if (taken > 0) {
                    consume -= taken;
                    take[stacks[i].Uid] = take.GetValueOrDefault(stacks[i].Uid) + taken;
                }
            }
            if (need > 0) {
                return null;
            }
        }
        return take.Select(entry => (entry.Key, entry.Value)).ToList();
    }

    // The amount of a condition item the player must hold.
    public static int Needed(ReactorTable.ConditionItem item) {
        return Math.Max(item.Consume, item.CheckCount);
    }
}

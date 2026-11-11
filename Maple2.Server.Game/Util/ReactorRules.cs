using System.Numerics;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// Rules of a placed reactor (flower pot, chocolate). The cell and the name are the client's
// (CheckReactorPlacement); the client does not show when a state ends, so the timeline is the server's
// reading of reactorState.xml.
public static class ReactorRules {
    public const float CellSize = 150f;

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
    public static Step Tick(ReactorTable.State state, bool isLast, bool anyConnected, long elapsedMs) {
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

    // The amount of a condition item the player must hold.
    public static int Needed(ReactorTable.ConditionItem item) {
        return Math.Max(item.Consume, item.CheckCount);
    }
}

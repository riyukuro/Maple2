namespace Maple2.Server.Game.Util;

// FunctionCube 5 on a seat. The client sends the same request to sit down and to stand up. When it leaves
// through a portal or a menu move it sends one (MyPC::SendFunctionCube), and the answer that frees the seat
// ends its sitting state, which sends one more (StateUseFurniture::vfunc_5). If the player is still on the
// field then (a portal inside the field, a refused move), that second request would take the seat again.
public class SeatReleaseEcho {
    // The client has no delay of its own here: the answer handler (functioncubeobject.cpp:2453) ends the sitting
    // state at once and the state's exit sends the request, so the second request follows the answer by one
    // round trip. A player who stood up by key gets no such second request, and the server cannot tell the two
    // apart, so the request is matched by time. One second covers a slow round trip; sitting down again by
    // hand takes a walk to the seat and a key press.
    public const long WindowMs = 1000;

    private string cubeId = string.Empty;
    private long releasedTick;

    // The player gave up this seat by a request of their own.
    public void Released(string id, long tick) {
        cubeId = id;
        releasedTick = tick;
    }

    // True for the one request on the same seat that follows the release within the window.
    public bool Swallow(string id, long tick) {
        if (cubeId.Length == 0 || cubeId != id) {
            return false;
        }

        cubeId = string.Empty;
        return tick >= releasedTick && tick - releasedTick <= WindowMs;
    }
}

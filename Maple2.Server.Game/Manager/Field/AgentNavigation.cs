using System.Numerics;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using Maple2.Server.Game.Model;
using Maple2.Tools.DotRecast;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

public sealed class AgentNavigation {
    private static readonly ILogger Logger = Log.Logger.ForContext<AgentNavigation>();

    private readonly FieldNpc npc;
    private FieldManager field => npc.Field;
    public readonly DtCrowdAgent agent;
    private readonly DtCrowd crowd;

    public List<RcVec3f>? currentPath = [];
    private int currentPathIndex = 0;
    private float currentPathProgress = 0;

    public AgentNavigation(FieldNpc fieldNpc, DtCrowdAgent dtAgent, DtCrowd dtCrowd) {
        npc = fieldNpc;
        agent = dtAgent;
        crowd = dtCrowd;
    }

    public List<RcVec3f>? FindPath(Vector3 startVec, Vector3 targetVec) {
        return FindPath(crowd, DotRecastHelper.ToNavMeshSpace(startVec), DotRecastHelper.ToNavMeshSpace(targetVec));
    }

    public List<RcVec3f>? FindPath(RcVec3f startVec, RcVec3f targetVec) {
        return FindPath(crowd, startVec, targetVec);
    }

    private List<RcVec3f>? FindPath(DtCrowd crowd, RcVec3f startVec, RcVec3f targetVec) {
        DtNavMesh navMesh = crowd.GetNavMesh();
        DtNavMeshQuery navMeshQuery = crowd.GetNavMeshQuery();
        IDtQueryFilter filter = crowd.GetFilter(0);
        if (!field.FindNearestPoly(startVec, out long pos1Ref, out RcVec3f _)) {
            Logger.Error("Failed to find nearest poly at {StartVec}", startVec);
            return null;
        }

        if (!field.FindNearestPoly(targetVec, out long posRef2, out RcVec3f _)) {
            Logger.Error("Failed to find nearest poly at {TargetVec}", targetVec);
            return null;
        }

        List<RcVec3f>? smoothPath = SmoothPath(navMesh, navMeshQuery, filter, pos1Ref, posRef2, startVec, targetVec);
        if (smoothPath == null) {
            Logger.Error("Failed to find path from {StartVec} to {TargetVec}", startVec, targetVec);
        }

        return smoothPath;
    }

    // The path from startVec to targetVec as points on the navmesh surface, one per STEP_SIZE. The last point is the
    // target's closest point on the last poly, so a target above the mesh (a jumping player, or one standing on
    // something the navmesh leaves out) does not lift the end of the path off the ground.
    public static List<RcVec3f>? SmoothPath(DtNavMesh navMesh, DtNavMeshQuery navMeshQuery, IDtQueryFilter filter, long pos1Ref, long posRef2,
                                            RcVec3f startVec, RcVec3f targetVec) {
        List<long> pathIterPolys = [];
        navMeshQuery.FindPath(pos1Ref, posRef2, startVec, targetVec, filter, ref pathIterPolys, new DtFindPathOption(0, float.MaxValue));
        if (pathIterPolys.Count == 0) {
            return null;
        }

        int pathIterPolysCount = pathIterPolys.Count;

        navMeshQuery.ClosestPointOnPoly(pos1Ref, startVec, out RcVec3f iterPos, out bool _);
        navMeshQuery.ClosestPointOnPoly(pathIterPolys[pathIterPolysCount - 1], targetVec, out RcVec3f endPos, out bool _);

        Span<long> visited = stackalloc long[16];
        int nvisited = 0;

        int MAX_POLYS = 256;
        // Room for a path of 204,800 map units, past the length of any KR map. At 256 points (12,800 units) a longer
        // path was cut where the points ran out and the npc stopped there: 02000331_bf, npc 11001165 on
        // MS2PatrolData_1013 stopped 1,078 short of its waypoint. Chosen here: 4096, only long paths reach it.
        int MAX_SMOOTH = 4096;
        List<RcVec3f> smoothPath = [];
        while (pathIterPolysCount > 0 && smoothPath.Count < MAX_SMOOTH) {
            // Find location to steer towards.
            if (!DtPathUtils.GetSteerTarget(navMeshQuery, iterPos, endPos, DotRecastHelper.MIN_TARGET_DIST,
                pathIterPolys, pathIterPolysCount, out var steerPos, out int steerPosFlag, out long steerPosRef)) {
                break;
            }

            bool endOfPath = (steerPosFlag & DtStraightPathFlags.DT_STRAIGHTPATH_END) != 0;
            bool offMeshConnection = (steerPosFlag & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) != 0;

            // Find movement delta.
            RcVec3f delta = RcVec3f.Subtract(steerPos, iterPos);
            float len = MathF.Sqrt(RcVec3f.Dot(delta, delta));
            // If the steer target is end of path or off-mesh link, do not move past the location.
            if ((endOfPath || offMeshConnection) && len < DotRecastHelper.STEP_SIZE) {
                len = 1;
            } else {
                len = DotRecastHelper.STEP_SIZE / len;
            }

            RcVec3f moveTgt = RcVecUtils.Mad(iterPos, delta, len);

            // Move
            navMeshQuery.MoveAlongSurface(pathIterPolys[0], iterPos, moveTgt, filter, out var result, visited, out nvisited, 16);

            iterPos = result;

            pathIterPolysCount = DtPathUtils.MergeCorridorStartMoved(ref pathIterPolys, pathIterPolysCount, MAX_POLYS, visited, nvisited);
            pathIterPolysCount = DtPathUtils.FixupShortcuts(ref pathIterPolys, pathIterPolysCount, navMeshQuery);

            if (navMeshQuery.GetPolyHeight(pathIterPolys[0], result, out float h).Succeeded()) {
                iterPos.Y = h;
            }

            // Handle end of path and off-mesh links when close enough.
            if (endOfPath && DtPathUtils.InRange(iterPos, steerPos, DotRecastHelper.MIN_TARGET_DIST, 1.0f)) {
                // Reached end of path.
                iterPos = endPos;
                if (smoothPath.Count < MAX_SMOOTH) {
                    smoothPath.Add(iterPos);
                }

                break;
            } else if (offMeshConnection && DtPathUtils.InRange(iterPos, steerPos, DotRecastHelper.MIN_TARGET_DIST, 1.0f)) {
                // Reached off-mesh connection.
                RcVec3f startPosition = RcVec3f.Zero;
                RcVec3f endPosition = RcVec3f.Zero;

                // Advance the path up to and over the off-mesh connection.
                long prevRef = 0;
                long polyRef = pathIterPolys[0];
                int npos = 0;
                while (npos < pathIterPolysCount && polyRef != steerPosRef) {
                    prevRef = polyRef;
                    polyRef = pathIterPolys[npos];
                    npos++;
                }

                pathIterPolys = pathIterPolys.GetRange(npos, pathIterPolys.Count - npos);
                pathIterPolysCount -= npos;

                // Handle the connection.
                var status4 = navMesh.GetOffMeshConnectionPolyEndPoints(prevRef, polyRef, ref startPosition, ref endPosition);
                if (status4.Succeeded()) {
                    if (smoothPath.Count < MAX_SMOOTH) {
                        smoothPath.Add(startPosition);
                        // Hack to make the dotted path not visible during off-mesh connection.
                        if ((smoothPath.Count & 1) != 0) {
                            smoothPath.Add(startPosition);
                        }
                    }

                    // Move position at the other side of the off-mesh link.
                    iterPos = endPosition;
                    navMeshQuery.GetPolyHeight(pathIterPolys[0], iterPos, out float eh);
                    iterPos.Y = eh;
                }
            }

            // Store results.
            if (smoothPath.Count < MAX_SMOOTH) {
                smoothPath.Add(iterPos);
            }
        }

        return smoothPath;
    }

    private bool loggedOffMesh;

    // A goal with no navmesh polygon near it is outside the walkable map: no path is tried. Logged once per actor.
    private void LogOffMesh(Vector3 goal) {
        if (loggedOffMesh) {
            return;
        }
        loggedOffMesh = true;
        Logger.Warning("No path for npc {NpcId} (object {ObjectId}): goal {Goal} is off the navmesh of field {MapId}",
            npc.Value.Metadata.Id, npc.ObjectId, goal, field.MapId);
    }

    public bool HasPath => currentPath != null && currentPath.Count > 0;

    // The search box around a point for its nearest poly, in navmesh space: 200 across and 400 up and down in game units.
    public static readonly RcVec3f NearestPolyExtents = new(2, 4, 2);

    // The point's place on the navmesh, if a poly lies within NearestPolyExtents. Detour's FindNearestPoly reports
    // success with poly ref 0 and the point itself when none does, so the ref is what tells an off-mesh point.
    public static bool SnapToMesh(DtNavMeshQuery query, IDtQueryFilter filter, RcVec3f point, out RcVec3f position) {
        position = point;
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) {
            return false;
        }

        DtStatus status = query.FindNearestPoly(point, NearestPolyExtents, filter, out long nearestRef, out RcVec3f nearest, out _);
        if (status.Failed() || nearestRef == 0) {
            return false;
        }

        position = nearest;
        return true;
    }

    // A random point on the navmesh within the radius of the center, all in navmesh space, reached only over polys
    // linked to the center's own poly. Snapping a random point to its nearest poly instead can pick a poly on top of
    // a breakable (a barrel or a woodpile carries its own navmesh island over the floor): a mob placed there has no
    // path off it and floats once the breakable is gone. False when the center is off the mesh.
    public static bool RandomPointNear(DtNavMeshQuery query, IDtQueryFilter filter, RcVec3f center, float radius, IRcRand random, out RcVec3f point) {
        point = center;
        if (!float.IsFinite(radius) || radius <= 0 || !SnapToMesh(query, filter, center, out RcVec3f start)) {
            return false;
        }
        DtStatus status = query.FindNearestPoly(start, NearestPolyExtents, filter, out long startRef, out _, out _);
        if (status.Failed() || startRef == 0) {
            return false;
        }

        status = query.FindRandomPointWithinCircle(startRef, start, radius, filter, random, out _, out RcVec3f randomPoint);
        point = status.Succeeded() && float.IsFinite(randomPoint.X) && float.IsFinite(randomPoint.Y) && float.IsFinite(randomPoint.Z) ? randomPoint : start;
        return true;
    }

    // The end of a straight move over the navmesh from one point towards another: the target when nothing is in the
    // way, else the point where the line meets the first edge without a neighbour (a wall or a ledge). The start
    // snaps to the mesh first; false when it is off the mesh or a point is not finite.
    public static bool StraightMove(DtNavMeshQuery query, IDtQueryFilter filter, RcVec3f from, RcVec3f to, out RcVec3f end) {
        end = from;
        if (!float.IsFinite(to.X) || !float.IsFinite(to.Y) || !float.IsFinite(to.Z) || !SnapToMesh(query, filter, from, out RcVec3f start)) {
            return false;
        }
        DtStatus status = query.FindNearestPoly(start, NearestPolyExtents, filter, out long startRef, out _, out _);
        if (status.Failed() || startRef == 0) {
            return false;
        }

        var path = new List<long>();
        status = query.Raycast(startRef, start, to, filter, out float t, out _, ref path);
        if (status.Failed()) {
            return false;
        }
        t = Math.Clamp(t, 0f, 1f); // above 1 (float.MaxValue): the target was reached
        end = new RcVec3f(start.X + (to.X - start.X) * t, start.Y, start.Z + (to.Z - start.Z) * t);
        if (path.Count > 0 && query.GetPolyHeight(path[^1], end, out float height).Succeeded()) {
            end.Y = height;
        }
        return true;
    }

    // How far past a ledge (navmesh units) a push or a dash looks for the floor below, and the least drop that
    // counts as one. The navmesh erodes each floor by the agent radius (0.3) from a drop, so a bare ledge leaves a
    // gap of about 0.6 to 0.7 across to the floor below; a wall leaves that plus its thickness. A floor further than
    // LEDGE_REACH past the edge is behind a wall, and so is one at about the edge's height.
    public const float LEDGE_REACH = 0.75f;
    public const float LEDGE_MIN_DROP = 0.3f;
    public const int LEDGE_MAX_POLYS = 256;

    // A straight move that steps down off a ledge as the client's push and dash do. Each frame the client probes the
    // ground a step ahead and goes on while it finds ground within a limit below (PCBase::FUN_1416725c0: 150 for a
    // push, 750 for a dash); a push with pushFall goes on over any drop and the actor falls (CPushControl). On the
    // navmesh: the move stops at the first mesh edge (StraightMove); when it stopped short, the floor past the edge
    // (FloorPastLedge) is where the npc lands, and the move goes on from there for the rest of its length. A move
    // that ends within the gap ends on that floor's near side. No such floor (a wall, or too far down) ends the
    // move at the edge. False only as StraightMove is.
    public static bool LedgeMove(DtNavMeshQuery query, IDtQueryFilter filter, RcVec3f from, RcVec3f to, float maxDrop, out RcVec3f end) {
        if (!StraightMove(query, filter, from, to, out end)) {
            return false;
        }
        for (int drops = 0; drops < 4 && maxDrop > LEDGE_MIN_DROP; drops++) {
            float restX = to.X - end.X;
            float restZ = to.Z - end.Z;
            float rest = MathF.Sqrt(restX * restX + restZ * restZ);
            if (rest < 0.01f || !FloorPastLedge(query, filter, end, restX / rest, restZ / rest, maxDrop, out long floorRef, out RcVec3f floor)) {
                return true;
            }
            float done = MathF.Sqrt((floor.X - end.X) * (floor.X - end.X) + (floor.Z - end.Z) * (floor.Z - end.Z));
            var target = new RcVec3f(to.X, floor.Y, to.Z);
            if (done >= rest) {
                // The line meets the floor past the move's end (a short move, or one at a steep angle to the ledge):
                // the npc lands on that floor at the point nearest its target, not out along the line.
                end = query.ClosestPointOnPoly(floorRef, target, out RcVec3f landing, out _).Succeeded() ? landing : floor;
                return true;
            }
            if (!StraightMove(query, filter, floor, target, out RcVec3f next)) {
                end = floor;
                return true;
            }
            end = next;
        }
        return true;
    }

    // The floor past an edge along (dirX, dirZ): of the polys more than LEDGE_MIN_DROP and at most maxDrop below the
    // edge whose nearest point lies within LEDGE_REACH across of it (the gap square to the ledge, whatever the
    // move's angle), the one the move's line passes over first, where it does (the ground the client's probe meets
    // first), the higher of two met as soon. A poly the line does not pass over within a few gaps (a breakable's
    // raised island off to the side) is not taken. A search that fills its LEDGE_MAX_POLYS (a dense multi-level
    // spot) may have missed the near floor, so it finds none and the move stops at the edge.
    private static bool FloorPastLedge(DtNavMeshQuery query, IDtQueryFilter filter, RcVec3f edge, float dirX, float dirZ,
            float maxDrop, out long floorRef, out RcVec3f floor) {
        floorRef = 0;
        const float step = 0.05f;
        const float along = 4 * LEDGE_REACH;
        floor = edge;
        float depth = Math.Min(maxDrop, 1000f);
        var center = new RcVec3f(edge.X + dirX * along / 2, edge.Y - depth / 2, edge.Z + dirZ * along / 2);
        long[] polys = new long[LEDGE_MAX_POLYS];
        if (query.QueryPolygons(center, new RcVec3f(along / 2 + LEDGE_REACH, depth / 2 + 0.1f, along / 2 + LEDGE_REACH), filter, polys,
                out int count, polys.Length).Failed() || count >= polys.Length) {
            return false;
        }
        float first = float.MaxValue;
        for (int i = 0; i < count; i++) {
            if (query.ClosestPointOnPoly(polys[i], edge, out RcVec3f nearest, out _).Failed()) {
                continue;
            }
            float gapX = nearest.X - edge.X;
            float gapZ = nearest.Z - edge.Z;
            float nearDrop = edge.Y - nearest.Y;
            if (gapX * gapX + gapZ * gapZ > LEDGE_REACH * LEDGE_REACH || nearDrop <= LEDGE_MIN_DROP || nearDrop > maxDrop + 0.1f) {
                continue;
            }
            for (float s = step; s <= along && s < first + 0.01f; s += step) {
                var probe = new RcVec3f(edge.X + dirX * s, edge.Y, edge.Z + dirZ * s);
                if (query.ClosestPointOnPoly(polys[i], probe, out RcVec3f point, out _).Failed()) {
                    break;
                }
                float offX = point.X - probe.X;
                float offZ = point.Z - probe.Z;
                if (offX * offX + offZ * offZ > 0.0001f) {
                    continue;
                }
                float drop = edge.Y - point.Y;
                if (drop > LEDGE_MIN_DROP && drop <= maxDrop && (s < first - 0.01f || point.Y > floor.Y)) {
                    floor = point;
                    floorRef = polys[i];
                    first = Math.Min(first, s);
                }
                break;
            }
        }
        return first < float.MaxValue;
    }

    // How far up (navmesh units) a standing point may be above a breakable's base and still be on the floor: the
    // navmesh's agentMaxClimb.
    public const float BREAKABLE_CLIMB = 0.7f;

    // The floor for an npc left standing on a breakable that broke or went away. The navmesh is built with the
    // breakables in it (their tops are walkable polys, the ground under them is not), and the client does not
    // drop a live npc: it puts the npc where its NpcControl says. An npc on such a top is on a poly island that
    // no path leaves, so it would stay in the air. Only an npc on this breakable's own top is taken: it stands
    // within topRadius (navmesh units) across of the breakable's base point, more than a climb and at most a climb
    // over topHeight above it, and every poly its stand poly reaches lies within topRadius across of the base point
    // (a neighbouring breakable's top, a platform, a tower or a stage reaches further and is left alone), with no
    // path from it to the floor (a ramp or a ledge, not a top). The floor is the nearest poly at the base's height
    // within floorReach across, which keeps it beside the breakable, on the side of a wall the breakable stands on.
    public static bool FloorBelowBreakable(DtNavMeshQuery query, IDtQueryFilter filter, RcVec3f point, RcVec3f basePoint,
            float topRadius, float topHeight, float floorReach, out RcVec3f floor) {
        floor = point;
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z) || !WithinAcross(point, basePoint, topRadius)
            || point.Y - basePoint.Y <= BREAKABLE_CLIMB) {
            return false;
        }

        DtStatus status = query.FindNearestPoly(point, new RcVec3f(0.5f, 1f, 0.5f), filter, out long standRef, out RcVec3f stand, out _);
        if (status.Failed() || standRef == 0 || stand.Y - basePoint.Y <= BREAKABLE_CLIMB || stand.Y - basePoint.Y > topHeight + BREAKABLE_CLIMB) {
            return false;
        }

        var island = new List<long>();
        var parents = new List<long>();
        var costs = new List<float>();
        status = query.FindPolysAroundCircle(standRef, stand, 4 * topRadius, filter, ref island, ref parents, ref costs);
        if (status.Failed()) {
            return false;
        }
        DtNavMesh navMesh = query.GetAttachedNavMesh();
        foreach (long polyRef in island) {
            navMesh.GetTileAndPolyByRefUnsafe(polyRef, out DtMeshTile tile, out DtPoly poly);
            for (int i = 0; i < poly.vertCount; i++) {
                int v = poly.verts[i] * 3;
                if (!WithinAcross(new RcVec3f(tile.data.verts[v], tile.data.verts[v + 1], tile.data.verts[v + 2]), basePoint, topRadius)) {
                    return false;
                }
            }
        }

        status = query.FindNearestPoly(basePoint, new RcVec3f(floorReach, BREAKABLE_CLIMB, floorReach), filter, out long floorRef, out RcVec3f floorPoint, out _);
        if (status.Failed() || floorRef == 0 || !WithinAcross(floorPoint, basePoint, floorReach)) {
            return false;
        }

        var path = new List<long>();
        status = query.FindPath(standRef, floorRef, stand, floorPoint, filter, ref path, new DtFindPathOption(0, float.MaxValue));
        if (status.Succeeded() && path.Count > 0 && path[^1] == floorRef) {
            return false;
        }

        floor = floorPoint;
        return true;
    }

    private static bool WithinAcross(RcVec3f point, RcVec3f center, float radius) {
        float x = point.X - center.X;
        float z = point.Z - center.Z;
        return x * x + z * z <= radius * radius;
    }

    // Puts the npc on the floor when it stands on the top of the breakable that broke (FloorBelowBreakable), with
    // the sizes in map units. Its walk ends there: the rest of a path on the top is no longer walkable.
    public bool DropOffBreakable(Vector3 breakablePosition, float topRadius, float topHeight, float floorReach, out Vector3 dropped) {
        float scale = DotRecastHelper.MapRotation.GetRightAxis().Length();
        dropped = npc.Position;
        if (!FloorBelowBreakable(crowd.GetNavMeshQuery(), crowd.GetFilter(0), DotRecastHelper.ToNavMeshSpace(npc.Position),
                DotRecastHelper.ToNavMeshSpace(breakablePosition), topRadius * scale, topHeight * scale, floorReach * scale, out RcVec3f floor)) {
            return false;
        }

        currentPath = [];
        currentPathIndex = 0;
        currentPathProgress = 0;
        agent.npos = floor;
        npc.Position = DotRecastHelper.FromNavMeshSpace(floor);
        dropped = npc.Position;
        return true;
    }

    // Moves the npc in a straight line over the navmesh towards the position and stops it at the first wall or
    // ledge (StraightMove); with a maxDrop (map units) it steps down a ledge whose floor is at most that far below
    // (LedgeMove). False when it could not move at all.
    public bool MoveStraight(Vector3 position, float maxDrop = 0) {
        float scale = DotRecastHelper.MapRotation.GetRightAxis().Length();
        if (!LedgeMove(crowd.GetNavMeshQuery(), crowd.GetFilter(0), DotRecastHelper.ToNavMeshSpace(npc.Position),
                DotRecastHelper.ToNavMeshSpace(position), maxDrop * scale, out RcVec3f end)) {
            return false;
        }

        agent.npos = end;
        npc.Position = DotRecastHelper.FromNavMeshSpace(end);
        return true;
    }

    // Puts the npc on the navmesh at its position. Off the mesh it stays where it is.
    public bool UpdatePosition() {
        return UpdatePosition(npc.Position);
    }

    // Moves the npc to the position's place on the navmesh. A position off the mesh (a walk or a skill's cast move
    // past a ledge, or a NaN one) is refused and the npc stays where it is; the caller decides on any fallback.
    public bool UpdatePosition(Vector3 position) {
        if (!SnapToMesh(crowd.GetNavMeshQuery(), crowd.GetFilter(0), DotRecastHelper.ToNavMeshSpace(position), out RcVec3f rcPosition)) {
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)) {
                Logger.Error("Invalid position {Position} for npc {NpcId} (object {ObjectId}) at {Source}",
                    position, npc.Value.Metadata.Id, npc.ObjectId, npc.Position);
            }
            return false;
        }

        agent.npos = rcPosition;
        npc.Position = DotRecastHelper.FromNavMeshSpace(rcPosition);
        return true;
    }

    public Vector3 GetAgentPosition() {
        return DotRecastHelper.FromNavMeshSpace(agent.npos);
    }

    public (Vector3 Start, Vector3 End) Advance(TimeSpan timeSpan, float speed, out bool followSegment) {
        followSegment = false;
        if (currentPath?.Count < 2) {
            return default;
        }

        Vector3 start = DotRecastHelper.FromNavMeshSpace(currentPath![currentPathIndex]);

        if (currentPathIndex >= currentPath.Count - 1) {
            return default;
        }

        followSegment = true;
        float distance = RcVec3f.Distance(currentPath[currentPathIndex], currentPath[currentPathIndex + 1]);

        speed *= DotRecastHelper.MapRotation.GetRightAxis().Length(); // changing speed to navmesh space
        float timeLeft = distance * (1 - currentPathProgress) / speed;
        while (timeLeft < timeSpan.TotalSeconds) {
            timeSpan -= TimeSpan.FromSeconds(timeLeft);
            currentPathIndex++;
            currentPathProgress = 0;

            if (currentPathIndex >= currentPath.Count - 1) {
                return (start, DotRecastHelper.FromNavMeshSpace(currentPath[^1]));
            }

            distance = RcVec3f.Distance(currentPath[currentPathIndex], currentPath[currentPathIndex + 1]);
            timeLeft = distance * (1 - currentPathProgress) / speed;
        }

        // A zero-length segment (a repeated path point) with no time left over, or no speed, adds nothing:
        // dividing by its length gave a NaN progress and a NaN end point.
        if (distance > 0 && speed > 0) {
            currentPathProgress += (float) timeSpan.TotalSeconds * speed / distance;
        }
        RcVec3f end = RcVec3f.Lerp(currentPath[currentPathIndex], currentPath[currentPathIndex + 1], currentPathProgress);

        return (start, DotRecastHelper.FromNavMeshSpace(end));
    }

    /// <summary>
    /// Advances the actor in a straight line towards the target position, ignoring navmesh.
    /// </summary>
    /// <param name="start">Current position</param>
    /// <param name="target">Target position</param>
    /// <param name="speed">Movement speed</param>
    /// <param name="deltaTime">Time in seconds</param>
    /// <returns>Tuple of (new position, reachedTarget)</returns>
    public (Vector3 newPosition, bool reachedTarget) FlyAdvance(Vector3 start, Vector3 target, float speed, float deltaTime) {
        Vector3 direction = target - start;
        float distance = direction.Length();
        if (distance == 0) return (target, true);

        float moveDist = speed * deltaTime;
        if (moveDist >= distance) {
            return (target, true);
        }
        Vector3 step = direction / distance * moveDist;
        return (start + step, false);
    }

    public Vector3 GetRandomPatrolPoint() {
        if (!field.FindNearestPoly(npc.Origin, out long startRef, out RcVec3f startVec)) {
            return npc.Position;
        }

        float moveArea = npc.Value.Metadata.Action.MoveArea;
        if (moveArea == 0) {
            return npc.Origin;
        }

        moveArea *= DotRecastHelper.MapRotation.GetRightAxis().Length(); // changing moveArea to navmesh space
        DtStatus end = crowd.GetNavMeshQuery().FindRandomPointWithinCircle(startRef, startVec, moveArea, crowd.GetFilter(0), new RcRand(), out _, out RcVec3f randomPt);
        if (end.Failed()) {
            return npc.Origin;
        }

        return DotRecastHelper.FromNavMeshSpace(randomPt);
    }

    public Vector3 FindRandomPointAround(Vector3 point, int maxDistance, Vector3 fallback) {
        if (!field.FindNearestPoly(point, out long closest, out RcVec3f position)) {
            return fallback;
        }

        float distance = maxDistance * DotRecastHelper.MapRotation.GetRightAxis().Length(); // changing distance to navmesh space
        var status = crowd.GetNavMeshQuery().FindRandomPointAroundCircle(closest, position, distance, crowd.GetFilter(0), new RcRand(), out _, out RcVec3f randomPt);

        if (status.Failed()) {
            return fallback;
        }

        return DotRecastHelper.FromNavMeshSpace(randomPt);
    }

    public Vector3 FindRandomPointAround(Vector3 point, int maxDistance) {
        return FindRandomPointAround(point, maxDistance, DotRecastHelper.FromNavMeshSpace(agent.npos));
    }

    public bool PathTo(Vector3 goal) {
        if (!field.FindNearestPoly(agent.npos, out _, out _)) {
            return false;
        }

        if (!field.FindNearestPoly(goal, out long endRef, out RcVec3f end)) {
            return false;
        }

        if (endRef == 0) {
            LogOffMesh(goal);
            return false;
        }

        return SetPathTo(end);
    }

    // True when the goal is off the navmesh or the npc itself stands off it after a flown leg (no path starts there;
    // Chosen here: such an npc flies the leg back, as it flew off). A goal is off the mesh when no poly lies near it
    // (PathTo fails), or when the nearest poly's closest point is more than OffMeshDistance from it across the ground,
    // so a path would stop at the mesh's edge short of it: 63000015_cs's MS2PatrolData_201, 204 and 206 end 80 beyond
    // the mesh, 52010018_qd's MS2PatrolData_1007_B 449, 52010055_qd's PatrolDataBalrog_Open_115 105.
    public bool GoalOffMesh(Vector3 goal) {
        if (!NpcPoly(out long startRef, out _) || startRef == 0) {
            return true;
        }
        if (!field.FindNearestPoly(goal, out long endRef, out RcVec3f end)) {
            return false;
        }
        if (endRef == 0) {
            return true;
        }
        Vector3 onMesh = DotRecastHelper.FromNavMeshSpace(end);
        return new Vector2(onMesh.X - goal.X, onMesh.Y - goal.Y).Length() > OffMeshDistance;
    }

    // How far across the ground the goal may lie from the navmesh and still be walked to. Chosen here: a third of a
    // block, well above the mesh's own rounding at its edges (a few units) and below the 80 of 63000015_cs.
    public const float OffMeshDistance = 50;

    // True when the npc and the goal both stand on the navmesh but no path joins them (a ledge one block up, a gap):
    // PathTo then walks the npc only to the edge of its own part of the mesh.
    public bool GoalCutOff(Vector3 goal) {
        if (!NpcPoly(out long startRef, out RcVec3f start) || startRef == 0) {
            return false;
        }
        if (!field.FindNearestPoly(goal, out long endRef, out RcVec3f end) || endRef == 0) {
            return false;
        }
        return PathCutOff(crowd.GetNavMeshQuery(), crowd.GetFilter(0), startRef, endRef, start, end);
    }

    // The poly under the npc's own position. The agent is not used: a flown leg (MovementState's fly advance) moves
    // the npc and leaves the agent where the last walk ended, so checks from the agent judged the next leg from
    // there (02000422_bf: the navy's agent stayed on the lower floor by its spawn after it flew onto the deck).
    private bool NpcPoly(out long polyRef, out RcVec3f position) {
        return field.FindNearestPoly(npc.Position, out polyRef, out position);
    }

    // The polys a walker on startRef can get to over poly links: startRef's navmesh island.
    public static HashSet<long> LinkedPolys(DtNavMesh navMesh, long startRef) {
        HashSet<long> seen = [startRef];
        Stack<long> open = new([startRef]);
        while (open.Count > 0) {
            if (navMesh.GetTileAndPolyByRef(open.Pop(), out DtMeshTile tile, out DtPoly poly).Failed()) {
                continue;
            }
            for (int i = poly.firstLink; i != DtDetour.DT_NULL_LINK; i = tile.links[i].next) {
                long next = tile.links[i].refs;
                if (next != 0 && seen.Add(next)) {
                    open.Push(next);
                }
            }
        }
        return seen;
    }

    // The point nearest to the given one, within the box of half extents around it, on a poly linked to originRef
    // (all in navmesh space). False when no such poly is in the box.
    public static bool NearestLinkedPoint(DtNavMeshQuery query, IDtQueryFilter filter, long originRef, RcVec3f point, RcVec3f extents, out RcVec3f position) {
        position = point;
        if (originRef == 0) {
            return false;
        }
        var linked = new LinkedFilter(LinkedPolys(query.GetAttachedNavMesh(), originRef), filter);
        DtStatus status = query.FindNearestPoly(point, extents, linked, out long nearestRef, out RcVec3f nearest, out _);
        if (status.Failed() || nearestRef == 0) {
            return false;
        }
        position = nearest;
        return true;
    }

    private sealed class LinkedFilter(HashSet<long> polys, IDtQueryFilter inner) : IDtQueryFilter {
        public bool PassFilter(long polyRef, DtMeshTile tile, DtPoly poly) => polys.Contains(polyRef) && inner.PassFilter(polyRef, tile, poly);

        public float GetCost(RcVec3f pa, RcVec3f pb, long prevRef, DtMeshTile prevTile, DtPoly prevPoly, long curRef, DtMeshTile curTile, DtPoly curPoly,
                long nextRef, DtMeshTile nextTile, DtPoly nextPoly) =>
            inner.GetCost(pa, pb, prevRef, prevTile, prevPoly, curRef, curTile, curPoly, nextRef, nextTile, nextPoly);
    }

    // True when the best path from startRef ends on a poly other than endRef (a partial path).
    public static bool PathCutOff(DtNavMeshQuery query, IDtQueryFilter filter, long startRef, long endRef, RcVec3f start, RcVec3f end) {
        List<long> polys = [];
        query.FindPath(startRef, endRef, start, end, filter, ref polys, new DtFindPathOption(0, float.MaxValue));
        return polys.Count == 0 || polys[^1] != endRef;
    }

    private bool SetPathTo(RcVec3f target) {
        currentPath = [];
        currentPathIndex = 0;
        currentPathProgress = 0;
        try {
            currentPath = FindPath(agent.npos, target);
        } catch (Exception ex) {
            Logger.Error(ex, "Failed to find path to {Target}", target);
        }

        return currentPath is not null;
    }

    public bool PathAwayFrom(Vector3 goal, int distance) {
        if (!field.FindNearestPoly(agent.npos, out _, out RcVec3f position)) {
            return false;
        }

        // get target in navmesh space
        RcVec3f target = DotRecastHelper.ToNavMeshSpace(goal);

        if (distance <= 0) {
            return false;
        }

        // get distance in navmesh space
        float fDistance = distance * DotRecastHelper.MapRotation.GetRightAxis().Length();

        // get direction from agent to target; on the goal's own spot there is no way away from it
        RcVec3f offset = RcVec3f.Subtract(target, position);
        float offsetLength = offset.Length();
        if (!float.IsFinite(offsetLength) || offsetLength < 1e-4f) {
            return false;
        }
        RcVec3f direction = offset * (1 / offsetLength);

        // get the point that is fDistance away from the target in the opposite direction
        RcVec3f positionAway = RcVec3f.Add(position, direction * -fDistance);

        // find the nearest poly to the positionAway
        if (!field.FindNearestPoly(positionAway, out long awayRef, out RcVec3f positionAwayNavMesh)) {
            return false;
        }

        if (awayRef == 0) {
            LogOffMesh(DotRecastHelper.FromNavMeshSpace(positionAway));
            return false;
        }

        return SetPathAway(positionAwayNavMesh);
    }

    private bool SetPathAway(RcVec3f target) {
        currentPath = [];
        currentPathIndex = 0;
        currentPathProgress = 0;
        try {
            currentPath = FindPath(agent.npos, target);
        } catch (Exception ex) {
            Logger.Error(ex, "Failed to find path away from {Target}", target);
        }

        return currentPath is not null;
    }
}

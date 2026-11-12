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
        int MAX_SMOOTH = 256;
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

    // Moves the npc in a straight line over the navmesh towards the position and stops it at the first wall or
    // ledge (StraightMove). False when it could not move at all.
    public bool MoveStraight(Vector3 position) {
        if (!StraightMove(crowd.GetNavMeshQuery(), crowd.GetFilter(0), DotRecastHelper.ToNavMeshSpace(npc.Position),
                DotRecastHelper.ToNavMeshSpace(position), out RcVec3f end)) {
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

    // True when the npc stands on the navmesh but no navmesh poly lies near the goal, so PathTo fails for it.
    public bool GoalOffMesh(Vector3 goal) {
        if (!field.FindNearestPoly(agent.npos, out long startRef, out _) || startRef == 0) {
            return false;
        }
        return field.FindNearestPoly(goal, out long endRef, out _) && endRef == 0;
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

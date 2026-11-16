using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Maple2.Server.Game.Manager.Field;
using Maple2.Tools.DotRecast;

namespace Maple2.Server.Tests.Game.Manager;

public class AgentNavigationTests {
    // One flat quad poly, 10 x 10 navmesh units at height 0.
    private static DtNavMesh FlatQuad() {
        var option = new DtNavMeshCreateParams {
            verts = [0, 0, 0, 0, 0, 100, 100, 0, 100, 100, 0, 0],
            vertCount = 4,
            polys = [0, 1, 2, 3, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff],
            polyAreas = [0],
            polyFlags = [1],
            polyCount = 1,
            nvp = 6,
            walkableHeight = 2,
            walkableRadius = 0.5f,
            walkableClimb = 0.5f,
            bmin = new RcVec3f(0, 0, 0),
            bmax = new RcVec3f(10, 1, 10),
            cs = 0.1f,
            ch = 0.1f,
            buildBvTree = true,
            offMeshConVerts = [],
            offMeshConRad = [],
            offMeshConDir = [],
            offMeshConAreas = [],
            offMeshConFlags = [],
            offMeshConUserID = [],
        };
        DtMeshData? data = DtNavMeshBuilder.CreateNavMeshData(option);
        Assert.That(data, Is.Not.Null);
        var navMesh = new DtNavMesh();
        Assert.That(navMesh.Init(data, 6, 0).Succeeded(), Is.True);
        return navMesh;
    }

    // A target above the mesh (a player in a jump, or on something the navmesh leaves out) must not lift the end of
    // the path: the mob would stop on the path's last segment in the air.
    [Test]
    public void SmoothPath_EndsOnTheMeshBelowATargetAboveIt() {
        DtNavMesh navMesh = FlatQuad();
        var query = new DtNavMeshQuery(navMesh);
        var filter = new DtQueryDefaultFilter();
        var extents = new RcVec3f(2, 4, 2);
        var start = new RcVec3f(1, 0, 1);
        var target = new RcVec3f(8, 3, 8);
        query.FindNearestPoly(start, extents, filter, out long startRef, out _, out _);
        query.FindNearestPoly(target, extents, filter, out long endRef, out _, out _);
        Assert.That(startRef, Is.Not.Zero);
        Assert.That(endRef, Is.Not.Zero);

        List<RcVec3f>? path = AgentNavigation.SmoothPath(navMesh, query, filter, startRef, endRef, start, target);

        Assert.That(path, Is.Not.Null.And.Not.Empty);
        RcVec3f end = path![^1];
        Assert.That(end.X, Is.EqualTo(8).Within(0.01));
        Assert.That(end.Z, Is.EqualTo(8).Within(0.01));
        Assert.That(end.Y, Is.EqualTo(0).Within(0.01));
        Assert.That(path.All(point => Math.Abs(point.Y) < 0.01), Is.True);
    }

    // One flat strip, 200 x 10 navmesh units (20,000 by 1,000 map units) at height 0.
    private static DtNavMesh LongStrip() {
        var option = new DtNavMeshCreateParams {
            verts = [0, 0, 0, 0, 0, 100, 2000, 0, 100, 2000, 0, 0],
            vertCount = 4,
            polys = [0, 1, 2, 3, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff],
            polyAreas = [0],
            polyFlags = [1],
            polyCount = 1,
            nvp = 6,
            walkableHeight = 2,
            walkableRadius = 0.5f,
            walkableClimb = 0.5f,
            bmin = new RcVec3f(0, 0, 0),
            bmax = new RcVec3f(200, 1, 10),
            cs = 0.1f,
            ch = 0.1f,
            buildBvTree = true,
            offMeshConVerts = [],
            offMeshConRad = [],
            offMeshConDir = [],
            offMeshConAreas = [],
            offMeshConFlags = [],
            offMeshConUserID = [],
        };
        DtMeshData? data = DtNavMeshBuilder.CreateNavMeshData(option);
        Assert.That(data, Is.Not.Null);
        var navMesh = new DtNavMesh();
        Assert.That(navMesh.Init(data, 6, 0).Succeeded(), Is.True);
        return navMesh;
    }

    // A path longer than 256 steps (12,800 map units) still ends at its target. Before, the smooth path stopped at 256
    // points: 02000331_bf's seeker01 sends npc 11001165 along MS2PatrolData_1013, whose waypoint (-5025, -4650, 5550)
    // is a full path of 109 polys from the npc, and the npc stopped 1,078 short of it and never arrived.
    [Test]
    public void SmoothPath_ReachesAFarTarget() {
        DtNavMesh navMesh = LongStrip();
        var query = new DtNavMeshQuery(navMesh);
        var filter = new DtQueryDefaultFilter();
        var extents = new RcVec3f(2, 4, 2);
        var start = new RcVec3f(1, 0, 5);
        var target = new RcVec3f(199, 0, 5);
        query.FindNearestPoly(start, extents, filter, out long startRef, out _, out _);
        query.FindNearestPoly(target, extents, filter, out long endRef, out _, out _);

        List<RcVec3f>? path = AgentNavigation.SmoothPath(navMesh, query, filter, startRef, endRef, start, target);

        Assert.That(path, Is.Not.Null.And.Not.Empty);
        Assert.That(path![^1].X, Is.EqualTo(199).Within(0.01));
    }

    // A point over the mesh lands on it; one beyond the search box across, or a NaN one, is refused instead of kept as it is.
    [Test]
    public void SnapToMesh_RefusesPointsOffTheMesh() {
        var query = new DtNavMeshQuery(FlatQuad());
        var filter = new DtQueryDefaultFilter();

        Assert.That(AgentNavigation.SnapToMesh(query, filter, new RcVec3f(5, 3, 5), out RcVec3f onMesh), Is.True);
        Assert.That(onMesh.X, Is.EqualTo(5).Within(0.01));
        Assert.That(onMesh.Y, Is.EqualTo(0).Within(0.01));
        Assert.That(onMesh.Z, Is.EqualTo(5).Within(0.01));

        Assert.That(AgentNavigation.SnapToMesh(query, filter, new RcVec3f(30, 0, 30), out RcVec3f offMesh), Is.False);
        Assert.That(offMesh.X, Is.EqualTo(30));
        Assert.That(AgentNavigation.SnapToMesh(query, filter, new RcVec3f(float.NaN, 0, 5), out _), Is.False);
    }

    // Two flat quads, x 0..10 and x 11.5..21.5, with a gap of 1.5 navmesh units (a one-block wall) between them.
    private static DtNavMesh TwoQuads() {
        var option = new DtNavMeshCreateParams {
            verts = [0, 0, 0, 0, 0, 100, 100, 0, 100, 100, 0, 0, 115, 0, 0, 115, 0, 100, 215, 0, 100, 215, 0, 0],
            vertCount = 8,
            polys = [
                0, 1, 2, 3, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff,
                4, 5, 6, 7, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff,
            ],
            polyAreas = [0, 0],
            polyFlags = [1, 1],
            polyCount = 2,
            nvp = 6,
            walkableHeight = 2,
            walkableRadius = 0.5f,
            walkableClimb = 0.5f,
            bmin = new RcVec3f(0, 0, 0),
            bmax = new RcVec3f(21.5f, 1, 10),
            cs = 0.1f,
            ch = 0.1f,
            buildBvTree = true,
            offMeshConVerts = [],
            offMeshConRad = [],
            offMeshConDir = [],
            offMeshConAreas = [],
            offMeshConFlags = [],
            offMeshConUserID = [],
        };
        DtMeshData? data = DtNavMeshBuilder.CreateNavMeshData(option);
        Assert.That(data, Is.Not.Null);
        var navMesh = new DtNavMesh();
        Assert.That(navMesh.Init(data, 6, 0).Succeeded(), Is.True);
        return navMesh;
    }

    // A patrol leg across the gap has polys at both ends but no path: it is cut off; one on the same quad is not.
    [Test]
    public void PathCutOff_TrueOnlyWhenNoPathJoinsTheEnds() {
        var query = new DtNavMeshQuery(TwoQuads());
        var filter = new DtQueryDefaultFilter();
        var extents = new RcVec3f(2, 4, 2);
        var start = new RcVec3f(5, 0, 5);
        var near = new RcVec3f(8, 0, 2);
        var across = new RcVec3f(18, 0, 5);
        query.FindNearestPoly(start, extents, filter, out long startRef, out _, out _);
        query.FindNearestPoly(near, extents, filter, out long nearRef, out _, out _);
        query.FindNearestPoly(across, extents, filter, out long acrossRef, out _, out _);
        Assert.That(startRef, Is.Not.Zero);
        Assert.That(acrossRef, Is.Not.Zero.And.Not.EqualTo(startRef));

        Assert.That(AgentNavigation.PathCutOff(query, filter, startRef, acrossRef, start, across), Is.True);
        Assert.That(AgentNavigation.PathCutOff(query, filter, startRef, nearRef, start, near), Is.False);
    }

    // A push of 1.5 units from 0.1 before a one-block wall: snapping the end to the nearest poly would put the
    // npc on the far side; the straight move stops at the wall.
    [Test]
    public void StraightMove_StopsAtAWall() {
        var query = new DtNavMeshQuery(TwoQuads());
        var filter = new DtQueryDefaultFilter();
        var from = new RcVec3f(9.9f, 0, 5);
        var to = new RcVec3f(11.4f, 0, 5);

        Assert.That(AgentNavigation.SnapToMesh(query, filter, to, out RcVec3f snapped), Is.True);
        Assert.That(snapped.X, Is.EqualTo(11.5f).Within(0.01)); // the far side

        Assert.That(AgentNavigation.StraightMove(query, filter, from, to, out RcVec3f end), Is.True);
        Assert.That(end.X, Is.EqualTo(10).Within(0.01));
        Assert.That(end.Z, Is.EqualTo(5).Within(0.01));
        Assert.That(end.Y, Is.EqualTo(0).Within(0.01));
    }

    // With nothing in the way the move ends at the target; a start off the mesh or a NaN target is refused.
    [Test]
    public void StraightMove_ReachesAFreeTarget() {
        var query = new DtNavMeshQuery(TwoQuads());
        var filter = new DtQueryDefaultFilter();

        Assert.That(AgentNavigation.StraightMove(query, filter, new RcVec3f(2, 0, 5), new RcVec3f(3.5f, 0, 6), out RcVec3f end), Is.True);
        Assert.That(end.X, Is.EqualTo(3.5f).Within(0.01));
        Assert.That(end.Z, Is.EqualTo(6).Within(0.01));

        Assert.That(AgentNavigation.StraightMove(query, filter, new RcVec3f(40, 0, 40), new RcVec3f(41, 0, 40), out _), Is.False);
        Assert.That(AgentNavigation.StraightMove(query, filter, new RcVec3f(2, 0, 5), new RcVec3f(float.NaN, 0, 5), out RcVec3f kept), Is.False);
        Assert.That(kept.X, Is.EqualTo(2));
    }

    // A floor, x 0..5, and the top of a breakable, x 5.5..9.5 and 1.5 navmesh units up, with no floor under it and no
    // link to the floor: the navmesh of a woodpile or barrel standing on the ground.
    private static DtNavMesh FloorAndTop() {
        var option = new DtNavMeshCreateParams {
            verts = [0, 0, 0, 0, 0, 100, 50, 0, 100, 50, 0, 0, 55, 15, 0, 55, 15, 100, 95, 15, 100, 95, 15, 0],
            vertCount = 8,
            polys = [
                0, 1, 2, 3, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff,
                4, 5, 6, 7, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff,
            ],
            polyAreas = [0, 0],
            polyFlags = [1, 1],
            polyCount = 2,
            nvp = 6,
            walkableHeight = 2,
            walkableRadius = 0.5f,
            walkableClimb = 0.5f,
            bmin = new RcVec3f(0, 0, 0),
            bmax = new RcVec3f(9.5f, 2, 10),
            cs = 0.1f,
            ch = 0.1f,
            buildBvTree = true,
            offMeshConVerts = [],
            offMeshConRad = [],
            offMeshConDir = [],
            offMeshConAreas = [],
            offMeshConFlags = [],
            offMeshConUserID = [],
        };
        DtMeshData? data = DtNavMeshBuilder.CreateNavMeshData(option);
        Assert.That(data, Is.Not.Null);
        var navMesh = new DtNavMesh();
        Assert.That(navMesh.Init(data, 6, 0).Succeeded(), Is.True);
        return navMesh;
    }

    // A spawn spread from a floor point must stay on the floor: a random point snapped to its nearest poly lands on
    // the breakable's top (a mob there has no path down and floats once the breakable is broken).
    [Test]
    public void RandomPointNear_StaysOnPolysLinkedToTheCenter() {
        var query = new DtNavMeshQuery(FloorAndTop());
        var filter = new DtQueryDefaultFilter();
        var center = new RcVec3f(3, 0, 5);

        Assert.That(AgentNavigation.SnapToMesh(query, filter, new RcVec3f(8, 0, 5), out RcVec3f snapped), Is.True);
        Assert.That(snapped.Y, Is.EqualTo(1.5f).Within(0.01)); // the nearest poly is the breakable's top

        var random = new RcRand(7);
        for (int i = 0; i < 500; i++) {
            Assert.That(AgentNavigation.RandomPointNear(query, filter, center, 6, random, out RcVec3f point), Is.True);
            Assert.That(point.Y, Is.EqualTo(0).Within(0.01), $"draw {i}: {point}");
            Assert.That(point.X, Is.InRange(0f, 5f), $"draw {i}: {point}");
        }

        Assert.That(AgentNavigation.RandomPointNear(query, filter, new RcVec3f(30, 0, 30), 6, random, out RcVec3f off), Is.False);
        Assert.That(off.X, Is.EqualTo(30));
        Assert.That(AgentNavigation.RandomPointNear(query, filter, center, float.NaN, random, out _), Is.False);
    }

    // A floor (x 0..5, z 0..10, height 0) and raised quads (x 5.5..7.5, height 3) beside it. With long, one quad
    // runs the floor's length (z 0..10: a platform, not a breakable's top); else two 2 by 2 quads at z 4..6 and z
    // 7..9, the tops of two breakables side by side. With ramp, a ramp poly joins the floor and the long quad (a
    // ledge reached on foot); else the tops are islands.
    private static DtNavMesh TopsOverFloor(bool long_, bool ramp = false) {
        const int none = 0xffff;
        int[] polys = ramp
            ? [
                0, 1, 2, 3, none, none, none, none, 1, none, none, none,
                3, 2, 4, 5, none, none, 0, none, 2, none, none, none,
                5, 4, 6, 7, none, none, 1, none, none, none, none, none,
            ]
            : long_
                ? [
                    0, 1, 2, 3, none, none, none, none, none, none, none, none,
                    5, 4, 6, 7, none, none, none, none, none, none, none, none,
                ]
                : [
                    0, 1, 2, 3, none, none, none, none, none, none, none, none,
                    4, 5, 6, 7, none, none, none, none, none, none, none, none,
                    8, 9, 10, 11, none, none, none, none, none, none, none, none,
                ];
        int[] verts = long_
            ? [0, 0, 0, 0, 0, 100, 50, 0, 100, 50, 0, 0, 55, 30, 100, 55, 30, 0, 75, 30, 100, 75, 30, 0]
            : [0, 0, 0, 0, 0, 100, 50, 0, 100, 50, 0, 0, 55, 30, 40, 55, 30, 60, 75, 30, 60, 75, 30, 40, 55, 30, 70, 55, 30, 90, 75, 30, 90, 75, 30, 70];
        int count = polys.Length / 12;
        var option = new DtNavMeshCreateParams {
            verts = verts,
            vertCount = verts.Length / 3,
            polys = polys,
            polyAreas = new int[count],
            polyFlags = Enumerable.Repeat(1, count).ToArray(),
            polyCount = count,
            nvp = 6,
            walkableHeight = 2,
            walkableRadius = 0.5f,
            walkableClimb = 0.5f,
            bmin = new RcVec3f(0, 0, 0),
            bmax = new RcVec3f(7.5f, 4, 10),
            cs = 0.1f,
            ch = 0.1f,
            buildBvTree = true,
            offMeshConVerts = [],
            offMeshConRad = [],
            offMeshConDir = [],
            offMeshConAreas = [],
            offMeshConFlags = [],
            offMeshConUserID = [],
        };
        DtMeshData? data = DtNavMeshBuilder.CreateNavMeshData(option);
        Assert.That(data, Is.Not.Null);
        var navMesh = new DtNavMesh();
        Assert.That(navMesh.Init(data, 6, 0).Succeeded(), Is.True);
        return navMesh;
    }

    // The breakable under the first 2 by 2 top: its base point at the floor's height, its top 1.5 across and 3 up.
    private static readonly RcVec3f BrokenBase = new(6.5f, 0, 5);

    // An npc on the top of the breakable that broke is on a poly island 3 units up: its floor is the nearest point
    // at the breakable's base height, beside the breakable. Before, nothing moved it and it stayed in the air.
    [Test]
    public void FloorBelowBreakable_PutsAnNpcOnTheBrokenTopOnTheFloor() {
        var query = new DtNavMeshQuery(TopsOverFloor(long_: false));
        var filter = new DtQueryDefaultFilter();

        Assert.That(AgentNavigation.FloorBelowBreakable(query, filter, new RcVec3f(6.5f, 3, 5), BrokenBase, 1.5f, 3, 2, out RcVec3f floor), Is.True);
        Assert.That(floor.X, Is.EqualTo(5).Within(0.01));
        Assert.That(floor.Y, Is.EqualTo(0).Within(0.01));
        Assert.That(floor.Z, Is.EqualTo(5).Within(0.01));
    }

    // Only the broken breakable's own top is cleared: an npc on the intact breakable beside it, one on a platform
    // that reaches past the breakable's block (a tower, a stage), one on the floor, one on a top a path leaves (a
    // ledge), one higher than the top can be and one beyond the reach of any floor stay where they are.
    [Test]
    public void FloorBelowBreakable_LeavesNpcsThatAreNotOnTheBrokenTop() {
        var filter = new DtQueryDefaultFilter();
        var tops = new DtNavMeshQuery(TopsOverFloor(long_: false));
        Assert.That(AgentNavigation.FloorBelowBreakable(tops, filter, new RcVec3f(6.5f, 3, 8), BrokenBase, 1.5f, 3, 2, out RcVec3f kept), Is.False);
        Assert.That(kept.Z, Is.EqualTo(8));
        Assert.That(AgentNavigation.FloorBelowBreakable(tops, filter, new RcVec3f(6.5f, 3, 6.9f), BrokenBase, 3f, 3, 2, out _), Is.False,
            "the next top within the radius, but its island reaches past it");
        Assert.That(AgentNavigation.FloorBelowBreakable(tops, filter, new RcVec3f(5.5f, 0, 5), BrokenBase, 1.5f, 3, 2, out _), Is.False);
        Assert.That(AgentNavigation.FloorBelowBreakable(tops, filter, new RcVec3f(6.5f, 3, 5), BrokenBase, 1.5f, 1, 2, out _), Is.False);
        Assert.That(AgentNavigation.FloorBelowBreakable(tops, filter, new RcVec3f(6.5f, 3, 5), BrokenBase, 1.5f, 3, 1, out _), Is.False);
        Assert.That(AgentNavigation.FloorBelowBreakable(tops, filter, new RcVec3f(float.NaN, 3, 5), BrokenBase, 1.5f, 3, 2, out _), Is.False);

        var platform = new DtNavMeshQuery(TopsOverFloor(long_: true));
        Assert.That(AgentNavigation.FloorBelowBreakable(platform, filter, new RcVec3f(6.5f, 3, 5), BrokenBase, 1.5f, 3, 2, out _), Is.False);

        var ledge = new DtNavMeshQuery(TopsOverFloor(long_: true, ramp: true));
        Assert.That(AgentNavigation.FloorBelowBreakable(ledge, filter, new RcVec3f(6.5f, 3, 5), BrokenBase, 1.5f, 3, 10, out _), Is.False);
    }

    // A corridor of two linked quads (x 0-3 and 3-6) and a room past a wall (x 7-10) with no link to them, 10 deep.
    private static DtNavMesh CorridorAndSealedRoom() {
        const int none = 0xffff;
        int[] polys = [
            0, 1, 2, 3, none, none, none, none, 1, none, none, none,
            3, 2, 4, 5, none, none, 0, none, none, none, none, none,
            6, 7, 8, 9, none, none, none, none, none, none, none, none,
        ];
        int[] verts = [0, 0, 0, 0, 0, 100, 30, 0, 100, 30, 0, 0, 60, 0, 100, 60, 0, 0, 70, 0, 0, 70, 0, 100, 100, 0, 100, 100, 0, 0];
        var option = new DtNavMeshCreateParams {
            verts = verts,
            vertCount = verts.Length / 3,
            polys = polys,
            polyAreas = new int[3],
            polyFlags = [1, 1, 1],
            polyCount = 3,
            nvp = 6,
            walkableHeight = 2,
            walkableRadius = 0.5f,
            walkableClimb = 0.5f,
            bmin = new RcVec3f(0, 0, 0),
            bmax = new RcVec3f(10, 1, 10),
            cs = 0.1f,
            ch = 0.1f,
            buildBvTree = true,
            offMeshConVerts = [],
            offMeshConRad = [],
            offMeshConDir = [],
            offMeshConAreas = [],
            offMeshConFlags = [],
            offMeshConUserID = [],
        };
        DtMeshData? data = DtNavMeshBuilder.CreateNavMeshData(option);
        Assert.That(data, Is.Not.Null);
        var navMesh = new DtNavMesh();
        Assert.That(navMesh.Init(data, 6, 0).Succeeded(), Is.True);
        return navMesh;
    }

    // 52000138's side rooms: a point in a room the origin's island does not reach goes to the nearest floor of the
    // island (the corridor's end at the wall); a point on the island stays; nothing of the island in the box is false.
    [Test]
    public void NearestLinkedPoint_LeavesASealedRoomForTheOriginsIsland() {
        DtNavMesh navMesh = CorridorAndSealedRoom();
        var query = new DtNavMeshQuery(navMesh);
        var filter = new DtQueryDefaultFilter();
        query.FindNearestPoly(new RcVec3f(1, 0, 5), AgentNavigation.NearestPolyExtents, filter, out long originRef, out _, out _);
        Assert.That(originRef, Is.Not.Zero);
        Assert.That(AgentNavigation.LinkedPolys(navMesh, originRef), Has.Count.EqualTo(2));

        Assert.That(AgentNavigation.NearestLinkedPoint(query, filter, originRef, new RcVec3f(8.5f, 0, 5), new RcVec3f(3, 1, 3), out RcVec3f moved), Is.True);
        Assert.That(moved.X, Is.EqualTo(6).Within(0.01));
        Assert.That(moved.Z, Is.EqualTo(5).Within(0.01));

        Assert.That(AgentNavigation.NearestLinkedPoint(query, filter, originRef, new RcVec3f(4.5f, 0, 2), new RcVec3f(3, 1, 3), out RcVec3f kept), Is.True);
        Assert.That(kept.X, Is.EqualTo(4.5).Within(0.01));
        Assert.That(kept.Z, Is.EqualTo(2).Within(0.01));

        Assert.That(AgentNavigation.NearestLinkedPoint(query, filter, originRef, new RcVec3f(9.5f, 0, 5), new RcVec3f(1, 1, 1), out _), Is.False);
    }

    // The agent on a navmesh path of the given points (navmesh space, 1 unit = 100 map units), no crowd needed.
    private static AgentNavigation OnPath(params RcVec3f[] points) {
        var navigation = (AgentNavigation) RuntimeHelpers.GetUninitializedObject(typeof(AgentNavigation));
        navigation.currentPath = points.ToList();
        return navigation;
    }

    // 10 navmesh units = 1000 map units: at 100 map units a second, 5 s is half the segment.
    [Test]
    public void Advance_MovesAlongTheSegmentBySpeedAndTime() {
        AgentNavigation navigation = OnPath(new RcVec3f(0, 0, 0), new RcVec3f(10, 0, 0));

        (Vector3 start, Vector3 end) = navigation.Advance(TimeSpan.FromSeconds(5), 100, out bool following);
        (Vector3 _, Vector3 next) = navigation.Advance(TimeSpan.FromSeconds(2), 100, out _);

        Assert.Multiple(() => {
            Assert.That(following, Is.True);
            Assert.That(start, Is.EqualTo(DotRecastHelper.FromNavMeshSpace(new RcVec3f(0, 0, 0))));
            Assert.That(Vector3.Distance(end, DotRecastHelper.FromNavMeshSpace(new RcVec3f(5, 0, 0))), Is.LessThan(0.5f));
            Assert.That(Vector3.Distance(next, DotRecastHelper.FromNavMeshSpace(new RcVec3f(7, 0, 0))), Is.LessThan(0.5f), "the progress is kept");
        });
    }

    // Past the last point the step ends on it, and a further call has nothing to follow.
    [Test]
    public void Advance_PastTheEnd_StopsOnTheLastPoint() {
        AgentNavigation navigation = OnPath(new RcVec3f(0, 0, 0), new RcVec3f(1, 0, 0), new RcVec3f(1, 0, 1));

        (Vector3 _, Vector3 end) = navigation.Advance(TimeSpan.FromSeconds(60), 100, out bool following);
        (Vector3 start, Vector3 again) = navigation.Advance(TimeSpan.FromSeconds(1), 100, out bool followingAgain);

        Assert.Multiple(() => {
            Assert.That(following, Is.True);
            Assert.That(Vector3.Distance(end, DotRecastHelper.FromNavMeshSpace(new RcVec3f(1, 0, 1))), Is.LessThan(0.5f));
            Assert.That(followingAgain, Is.False);
            Assert.That((start, again), Is.EqualTo((default(Vector3), default(Vector3))));
        });
    }

    [Test]
    public void Advance_WithoutASegmentOrWithoutSpeed_GivesNoNaN() {
        Assert.That(OnPath(new RcVec3f(1, 0, 1)).Advance(TimeSpan.FromSeconds(1), 100, out bool following), Is.EqualTo((default(Vector3), default(Vector3))));
        Assert.That(following, Is.False);

        (Vector3 _, Vector3 end) = OnPath(new RcVec3f(0, 0, 0), new RcVec3f(10, 0, 0)).Advance(TimeSpan.FromSeconds(1), 0, out _);
        Assert.That(float.IsNaN(end.X) || float.IsNaN(end.Y) || float.IsNaN(end.Z), Is.False);
    }

    // A flying move goes straight to the target: a step shorter than the distance, one that covers it, and no distance.
    [Test]
    public void FlyAdvance_StepsStraight_AndLandsOnTheTarget() {
        AgentNavigation navigation = OnPath();
        var from = new Vector3(0, 0, 0);
        var to = new Vector3(300, 400, 0);

        (Vector3 stepped, bool reached) = navigation.FlyAdvance(from, to, 100, 1);
        (Vector3 landed, bool arrived) = navigation.FlyAdvance(from, to, 100, 5);
        (Vector3 stay, bool already) = navigation.FlyAdvance(to, to, 100, 1);

        Assert.Multiple(() => {
            Assert.That(reached, Is.False);
            Assert.That(stepped.X, Is.EqualTo(60).Within(0.01));
            Assert.That(stepped.Y, Is.EqualTo(80).Within(0.01));
            Assert.That((landed, arrived), Is.EqualTo((to, true)));
            Assert.That((stay, already), Is.EqualTo((to, true)));
        });
    }

    // A floor at height 0 over navmesh x -20..0 and, past a gap (0.6, what the navmesh's erosion leaves at a bare
    // ledge), one drop units lower over x gap..20 (both z -20..20), with no link between them: a ledge. A gap of 0.8
    // is a 0.2 wall with the floor behind it. Without the lower floor the top ends over nothing.
    internal static DtNavMeshCreateParams LedgeParams(float drop, bool lower = true, float gap = 0.6f) {
        int top = (int) MathF.Round(drop / 0.1f);
        int near = 200 + (int) MathF.Round(gap / 0.1f);
        return new DtNavMeshCreateParams {
            verts = [0, top, 0, 0, top, 400, 200, top, 400, 200, top, 0, near, 0, 0, near, 0, 400, 400, 0, 400, 400, 0, 0],
            vertCount = 8,
            polys = lower
                ? [0, 1, 2, 3, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff,
                    4, 5, 6, 7, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff]
                : [0, 1, 2, 3, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff],
            polyAreas = lower ? [0, 0] : [0],
            polyFlags = lower ? [1, 1] : [1],
            polyCount = lower ? 2 : 1,
            nvp = 6,
            walkableHeight = 2,
            walkableRadius = 0.5f,
            walkableClimb = 0.5f,
            bmin = new RcVec3f(-20, -drop, -20),
            bmax = new RcVec3f(20, 1, 20),
            cs = 0.1f,
            ch = 0.1f,
            buildBvTree = true,
            offMeshConVerts = [],
            offMeshConRad = [],
            offMeshConDir = [],
            offMeshConAreas = [],
            offMeshConFlags = [],
            offMeshConUserID = [],
        };
    }

    private static DtNavMeshQuery LedgeQuery(float drop, bool lower = true, float gap = 0.6f) {
        DtMeshData? data = DtNavMeshBuilder.CreateNavMeshData(LedgeParams(drop, lower, gap));
        Assert.That(data, Is.Not.Null);
        var navMesh = new DtNavMesh();
        Assert.That(navMesh.Init(data, 6, 0).Succeeded(), Is.True);
        return new DtNavMeshQuery(navMesh);
    }

    // A move of 2 from 0.5 before the ledge, with the drop limit given (navmesh units: 1.5 is the client's push
    // probe of 150, 7.5 its dash probe of 750, infinity a push with pushFall): the end point.
    private static RcVec3f Ledge(float drop, float maxDrop, bool lower = true, float gap = 0.6f, float toX = 1.5f) {
        Assert.That(AgentNavigation.LedgeMove(LedgeQuery(drop, lower, gap), new DtQueryDefaultFilter(), new RcVec3f(-0.5f, 0, 5),
            new RcVec3f(toX, 0, 5), maxDrop, out RcVec3f end), Is.True);
        return end;
    }

    // A drop within the limit: the npc steps down, lands on the lower floor and goes on to the end of the move.
    [Test]
    public void LedgeMove_StepsDownAWithinLimitDrop() {
        RcVec3f end = Ledge(1f, 1.5f);
        Assert.Multiple(() => {
            Assert.That(end.X, Is.EqualTo(1.5f).Within(0.01));
            Assert.That(end.Z, Is.EqualTo(5).Within(0.01));
            Assert.That(end.Y, Is.EqualTo(-1f).Within(0.05));
        });
    }

    // 300 down: past a push's 150, so the push stops at the edge; within a dash's 750, so the dash steps down.
    [Test]
    public void LedgeMove_TheLimitDecides() {
        RcVec3f push = Ledge(3f, 1.5f);
        RcVec3f dash = Ledge(3f, 7.5f);
        Assert.Multiple(() => {
            Assert.That((push.X, push.Y), Is.EqualTo((0f, 0f)).Using<(float, float)>((a, b) => MathF.Abs(a.Item1 - b.Item1) < 0.01f && MathF.Abs(a.Item2 - b.Item2) < 0.05f));
            Assert.That(dash.X, Is.EqualTo(1.5f).Within(0.01));
            Assert.That(dash.Y, Is.EqualTo(-3f).Within(0.05));
        });
    }

    // pushFall: any drop, 2000 down here, lands on the floor below; a dash stops at the same edge.
    [Test]
    public void LedgeMove_PushFallGoesOverAnyDrop() {
        RcVec3f fall = Ledge(20f, float.PositiveInfinity);
        RcVec3f dash = Ledge(20f, 7.5f);
        Assert.Multiple(() => {
            Assert.That(fall.X, Is.EqualTo(1.5f).Within(0.01));
            Assert.That(fall.Y, Is.EqualTo(-20f).Within(0.05));
            Assert.That(dash.X, Is.EqualTo(0f).Within(0.01));
            Assert.That(dash.Y, Is.EqualTo(0f).Within(0.05));
        });
    }

    // No floor below, a floor at the edge's own height past it, a lower floor behind a 0.2 wall (gap 0.8), or no drop
    // allowed: the move ends at the edge, on the top. Never through a wall, whatever the limit.
    [Test]
    public void LedgeMove_StopsAtAWallOrOverNothing() {
        foreach ((float drop, float maxDrop, bool lower, float gap) in new[] {
                     (5f, float.PositiveInfinity, false, 0.6f), (0f, float.PositiveInfinity, true, 0.6f), (1f, 0f, true, 0.6f),
                     (1f, 1.5f, true, 0.8f), (1f, float.PositiveInfinity, true, 0.8f),
                 }) {
            RcVec3f end = Ledge(drop, maxDrop, lower, gap);
            Assert.That(end.X, Is.EqualTo(0f).Within(0.01), $"drop {drop}, limit {maxDrop}, floor {lower}, gap {gap}");
            Assert.That(end.Y, Is.EqualTo(0f).Within(0.05), $"drop {drop}, limit {maxDrop}, floor {lower}, gap {gap}");
        }
    }

    // A move that ends inside the gap (0.25 past the edge) still steps off: it ends on the floor's near side.
    [Test]
    public void LedgeMove_AShortMoveStillStepsOff() {
        RcVec3f end = Ledge(1f, 1.5f, toX: 0.25f);
        Assert.Multiple(() => {
            Assert.That(end.X, Is.EqualTo(0.6f).Within(0.01));
            Assert.That(end.Y, Is.EqualTo(-1f).Within(0.05));
        });
    }

    // A push 60 degrees off square to the ledge steps down too, landing where its line meets the lower floor; the
    // same push at a 0.2 wall (gap 0.8 square to it) stops at the edge.
    [Test]
    public void LedgeMove_AnObliquePushStepsDownButNotThroughAWall() {
        var from = new RcVec3f(-0.5f, 0, 5);
        var to = new RcVec3f(1.5f, 0, 5 + 2 * MathF.Tan(MathF.PI / 3));
        Assert.That(AgentNavigation.LedgeMove(LedgeQuery(1f), new DtQueryDefaultFilter(), from, to, 1.5f, out RcVec3f down), Is.True);
        Assert.That(AgentNavigation.LedgeMove(LedgeQuery(1f, gap: 0.8f), new DtQueryDefaultFilter(), from, to, 1.5f, out RcVec3f wall), Is.True);
        Assert.Multiple(() => {
            Assert.That(down.X, Is.EqualTo(to.X).Within(0.01));
            Assert.That(down.Z, Is.EqualTo(to.Z).Within(0.01));
            Assert.That(down.Y, Is.EqualTo(-1f).Within(0.05));
            Assert.That(wall.X, Is.EqualTo(0f).Within(0.01));
            Assert.That(wall.Y, Is.EqualTo(0f).Within(0.05));
        });
    }
}

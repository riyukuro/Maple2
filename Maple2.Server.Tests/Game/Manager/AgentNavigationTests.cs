using System;
using System.Collections.Generic;
using System.Linq;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Maple2.Server.Game.Manager.Field;

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

    // A floor (x 0..5, z 0..10, height 0) and a raised quad (x 5.5..7.5, height 3): the top of a breakable as the
    // navmesh has it. With ramp, a ramp poly joins the two (a ledge reached on foot); without, the top is an island.
    private static DtNavMesh TopOverFloor(bool ramp) {
        const int none = 0xffff;
        int[] polys = ramp
            ? [
                0, 1, 2, 3, none, none, none, none, 1, none, none, none,
                3, 2, 4, 5, none, none, 0, none, 2, none, none, none,
                5, 4, 6, 7, none, none, 1, none, none, none, none, none,
            ]
            : [
                0, 1, 2, 3, none, none, none, none, none, none, none, none,
                5, 4, 6, 7, none, none, none, none, none, none, none, none,
            ];
        int count = ramp ? 3 : 2;
        var option = new DtNavMeshCreateParams {
            verts = [0, 0, 0, 0, 0, 100, 50, 0, 100, 50, 0, 0, 55, 30, 100, 55, 30, 0, 75, 30, 100, 75, 30, 0],
            vertCount = 8,
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

    // An npc on the top of a breakable that broke is on a poly island 3 units up: its floor is the nearest point at
    // the breakable's base height, beside the breakable. Before, nothing moved it and it stayed in the air.
    [Test]
    public void FloorBelowBreakable_PutsAnNpcOnAnIslandTopOnTheFloor() {
        var query = new DtNavMeshQuery(TopOverFloor(ramp: false));
        var filter = new DtQueryDefaultFilter();

        Assert.That(AgentNavigation.FloorBelowBreakable(query, filter, new RcVec3f(6.5f, 3, 5), 0, 3, out RcVec3f floor), Is.True);
        Assert.That(floor.X, Is.EqualTo(5).Within(0.01));
        Assert.That(floor.Y, Is.EqualTo(0).Within(0.01));
        Assert.That(floor.Z, Is.EqualTo(5).Within(0.01));
    }

    // An npc on the floor, one on a top a path leaves (a ledge, not a breakable's top), and one beyond the reach of
    // any floor stay where they are.
    [Test]
    public void FloorBelowBreakable_LeavesNpcsThatAreNotStranded() {
        var filter = new DtQueryDefaultFilter();
        var island = new DtNavMeshQuery(TopOverFloor(ramp: false));
        Assert.That(AgentNavigation.FloorBelowBreakable(island, filter, new RcVec3f(2, 0, 5), 0, 3, out RcVec3f kept), Is.False);
        Assert.That(kept.X, Is.EqualTo(2));
        Assert.That(AgentNavigation.FloorBelowBreakable(island, filter, new RcVec3f(6.5f, 3, 5), 0, 1, out _), Is.False);
        Assert.That(AgentNavigation.FloorBelowBreakable(island, filter, new RcVec3f(float.NaN, 3, 5), 0, 3, out _), Is.False);

        var ledge = new DtNavMeshQuery(TopOverFloor(ramp: true));
        Assert.That(AgentNavigation.FloorBelowBreakable(ledge, filter, new RcVec3f(6.5f, 3, 5), 0, 3, out _), Is.False);
    }
}

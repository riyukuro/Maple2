using System;
using System.Collections.Generic;
using System.Linq;
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
}

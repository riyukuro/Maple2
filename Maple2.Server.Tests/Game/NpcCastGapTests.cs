using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using DotRecast.Recast.Toolset.Builder;
using Maple2.Server.Game.Manager.Field;
using Maple2.Tools.DotRecast;

namespace Maple2.Server.Tests.Game;

// A cast dash on a mesh of two islands with a gap between them (nav x -20 to -5 and 5 to 20, each 40 deep): the client
// ends the whole motion at the first block (CLinearMotionController, motioncontroller.cpp:293-325), so the npc stops at
// the near island's edge and never lands on the far one.
public class NpcCastGapTests : NpcCastFixture {
    private static readonly string[] Rig = ["Idle_A", "Attack_Idle_A", "Attack_01_A"];
    private static readonly System.Collections.Generic.Dictionary<string, (string Name, float Time)[]> DashKeys = new() {
        ["Attack_01_A"] = [("move0", 0.5f), ("move1", 1.5f), ("end", 2.5f)],
    };

    protected override Navigation BuildNavigation() {
        var option = new DtNavMeshCreateParams {
            // Cells of 0.1 from the bounds' minimum (-20): the islands are cells 0-150 and 250-400 in x.
            verts = [0, 0, 0, 0, 0, 400, 150, 0, 400, 150, 0, 0, 250, 0, 0, 250, 0, 400, 400, 0, 400, 400, 0, 0],
            vertCount = 8,
            polys = [0, 1, 2, 3, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff,
                     4, 5, 6, 7, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff],
            polyAreas = [0, 0],
            polyFlags = [1, 1],
            polyCount = 2,
            nvp = 6,
            walkableHeight = 2,
            walkableRadius = 0.5f,
            walkableClimb = 0.5f,
            bmin = new RcVec3f(-20, 0, -20),
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
        DtMeshData data = DtNavMeshBuilder.CreateNavMeshData(option)!;
        var navMesh = new DtNavMesh();
        Assert.That(navMesh.Init(data, 6, 0).Succeeded(), Is.True);

        var navigation = (Navigation) RuntimeHelpers.GetUninitializedObject(typeof(Navigation));
        navigation.GetType().GetField("navMesh", Any)!.SetValue(navigation, navMesh);
        navigation.GetType().GetField("navMeshQuery", Any)!.SetValue(navigation, new DtNavMeshQuery(navMesh));
        navigation.GetType().GetField("<Crowd>k__BackingField", Any)!.SetValue(navigation, new DtCrowd(new DtCrowdConfig(maxAgentRadius: 0.3f), navMesh,
            _ => new DtQueryDefaultFilter(SampleAreaModifications.SAMPLE_POLYFLAGS_ALL, SampleAreaModifications.SAMPLE_POLYFLAGS_DISABLED,
                [1f, 10f, 1f, 1f, 2f, 1.5f])));
        navigation.GetType().GetField("crowdAgentConfig", Any)!.SetValue(navigation, new DtCrowdAgentConfig());
        return navigation;
    }

    private void Play(int ms) {
        long start = Field.FieldTick;
        for (int elapsed = 0; elapsed <= ms; elapsed += 100) {
            long tick = start + elapsed;
            typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(Field, tick);
            npc.Animation.Update(tick);
            npc.MovementState.Update(tick);
        }
    }

    // The npc stands on the near island (nav x 10) and dashes 10000 map units (1000 a frame, as wide as the gap) toward the gap and the far island.
    [Test]
    public void DashTowardAGap_StopsAtTheNearIslandsEdge() {
        StartCaster(Rig, DashKeys, Skill(50100012, 10000f, ("Attack_01_A", "p0")));
        npc.Transform.LookTo(new Vector3(-1, 0, 0));
        Cast(50100012);

        Play(3000);
        RcVec3f nav = DotRecastHelper.ToNavMeshSpace(npc.Position);

        Assert.Multiple(() => {
            Assert.That(nav.X, Is.EqualTo(5).Within(0.3f), "at the near island's edge");
            Assert.That(nav.X, Is.GreaterThan(4.7f), "never across the gap");
        });
    }

    // A dash that ends on the same island is unchanged by the gap.
    [Test]
    public void DashOnTheIsland_GoesItsWholeDistance() {
        StartCaster(Rig, DashKeys, Skill(50100013, 300f, ("Attack_01_A", "p0")));
        npc.Transform.LookTo(new Vector3(-1, 0, 0));
        Vector3 start = npc.Position;
        Cast(50100013);

        Play(3000);

        Assert.That(Vector3.Distance(npc.Position, start), Is.EqualTo(300).Within(5));
    }
}

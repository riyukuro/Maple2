using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Numerics;
using Maple2.File.Flat;
using Maple2.File.Flat.maplestory2library;
using Maple2.File.Flat.standardmodellibrary;
using Maple2.File.Ingest.Helpers;
using Maple2.File.IO;
using Maple2.File.IO.Nif;
using Maple2.File.Parser.Flat;
using Maple2.File.Parser.MapXBlock;

namespace Maple2.Server.Tests.Ingest;

// The ground pass the ingest runs on npc spawn points (SpawnGround), on Tria (02000001) from the client's map data.
// Needs MS2_DATA_FOLDER (the client's Data folder): every test here calls Assert.Ignore without it, so CI runs none of
// them. MatchesOfficialPositions also needs SPAWN_GROUND_CHECK.
[NonParallelizable]
public class SpawnGroundTests {
    private const string TRIA = "02000001_tw_tria";

    private static IMapEntity[]? entities;
    private static SpawnGround? ground;

    [OneTimeSetUp]
    public void Load() {
        string? root = Environment.GetEnvironmentVariable("MS2_DATA_FOLDER");
        if (string.IsNullOrEmpty(root) || !System.IO.File.Exists(Path.Combine(root, "Resource/Exported.m2d"))) {
            Assert.Ignore("MS2_DATA_FOLDER is not set to a client Data folder");
        }
        if (entities != null) {
            return;
        }

        var modelReaders = new List<PrefixedM2dReader> {
            new("/library/", Path.Combine(root, "Resource/Library.m2d")),
            new("/model/map/", Path.Combine(root, "Resource/Model/Map.m2d")),
            new("/model/effect/", Path.Combine(root, "Resource/Model/Effect.m2d")),
            new("/model/camera/", Path.Combine(root, "Resource/Model/Camera.m2d")),
            new("/model/tool/", Path.Combine(root, "Resource/Model/Tool.m2d")),
            new("/model/item/", Path.Combine(root, "Resource/Model/Item.m2d")),
            new("/model/npc/", Path.Combine(root, "Resource/Model/Npc.m2d")),
            new("/model/path/", Path.Combine(root, "Resource/Model/Path.m2d")),
            new("/model/character/", Path.Combine(root, "Resource/Model/Character.m2d")),
            new("/model/textures/", Path.Combine(root, "Resource/Model/Textures.m2d")),
        };
        if (NifParserHelper.nifDocuments.Count == 0) {
            // The NIF tables are static; another fixture in the same run may have read them already.
            NifParserHelper.ParseNif(modelReaders);
        }

        using var exported = new M2dReader(Path.Combine(root, "Resource/Exported.m2d"));
        var parser = new XBlockParser(exported, new FlatTypeIndex(exported));
        parser.ParseMap(TRIA, map => entities = map.ToArray());
        Assert.That(entities, Is.Not.Null.And.Not.Empty);
        ground = new SpawnGround(entities!);
    }

    private static ISpawnPointNPC Spawn(string name) => entities!.OfType<ISpawnPointNPC>().Single(spawn => spawn.EntityName == name);

    private static IMS2CubeProp Cube(float x, float y, float z) => entities!.OfType<IMS2CubeProp>()
        .Single(cube => cube is IMS2MapProperties { GeneratePhysX: true } && cube.Position == new Vector3(x, y, z));

    // guard_crossbow_05's point (3225, -2775, 4200) is the shared corner of four floor cubes standing at z 3900, whose
    // GeneratePhysX boxes (150 high) top out at 4050. Its z is the top of the cube standing at 4050 east of it
    // (3450, -2700), whose footprint (x 3375..3525) does not reach the point. The ground is the floor cubes' top.
    [Test]
    public void GuardStandsOnFloorCube() {
        ISpawnPointNPC guard = Spawn("11000452_guard_crossbow_05");
        Assert.That(guard.Position, Is.EqualTo(new Vector3(3225, -2775, 4200)));

        IMS2CubeProp floor = Cube(3150, -2700, 3900);
        IMS2CubeProp east = Cube(3450, -2700, 4050);
        Assert.That(east.Position.Z + 150, Is.EqualTo(guard.Position.Z), "the point's z is the east cube's top");
        Assert.That(Math.Abs(east.Position.X - guard.Position.X), Is.GreaterThan(75), "the east cube is not under the point");

        var hit = ground!.Hit(guard.Position);
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.Value.Z, Is.EqualTo(floor.Position.Z + 150), "the floor cube's top");
        Assert.That(ground.Place(guard.Position), Is.EqualTo(new Vector3(3225, -2775, floor.Position.Z + 150 - SpawnGround.BELOW_GROUND)));
    }

    // The spear guards on the stairs stand at z 4125, inside the stair cube at z 4050, which has no GeneratePhysX box:
    // its ground is the stair's NIF PhysX mesh, whose surface is at 4140 under both points (15 above them). By the rule
    // both spear guards stand 1 under that surface, so the ground is used upward too.
    [TestCase("11000451_spear_03_Left", 750f, 1050f)]
    [TestCase("11000451_spear_03_Right", 1050f, 750f)]
    public void StairSpawnStandsOnStairSurface(string name, float cubeX, float cubeY) {
        ISpawnPointNPC spear = Spawn(name);
        IMS2CubeProp stair = entities!.OfType<IMS2CubeProp>().Single(cube => cube.Position == new Vector3(cubeX, cubeY, 4050));
        Assert.That(stair.EntityName, Does.StartWith("tr_deform_stair_"));
        Assert.That(stair is IMS2MapProperties { GeneratePhysX: true }, Is.False);

        var hit = ground!.Hit(spear.Position);
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.Value.Source, Is.SameAs(stair));
        Assert.That(hit.Value.Z, Is.EqualTo(4140).Within(0.01f), "the stair mesh's surface under the point");
        Assert.That(hit.Value.Z, Is.GreaterThan(stair.Position.Z).And.LessThan(stair.Position.Z + 150));
    }

    // Every npc spawn point in Tria finds its ground on a cube whose footprint holds the point, no more than one cube
    // below the point and no more than half a cube above it.
    [Test]
    public void EverySpawnGroundsWithinItsBlock() {
        ISpawnPointNPC[] spawns = entities!.OfType<ISpawnPointNPC>().ToArray();
        Assert.That(spawns, Is.Not.Empty);
        Assert.Multiple(() => {
            foreach (ISpawnPointNPC spawn in spawns) {
                var hit = ground!.Hit(spawn.Position);
                Assert.That(hit, Is.Not.Null, spawn.EntityName);
                if (hit is null) {
                    continue;
                }
                Assert.That(hit.Value.Z - spawn.Position.Z, Is.InRange(-150f, SpawnGround.REACH_UP), spawn.EntityName);
                Assert.That(hit.Value.Source, Is.InstanceOf<IMS2CubeProp>(), spawn.EntityName);
                Vector3 cube = ((IPlaceable) hit.Value.Source).Position;
                Assert.That(Math.Abs(cube.X - spawn.Position.X), Is.LessThanOrEqualTo(75f), spawn.EntityName);
                Assert.That(Math.Abs(cube.Y - spawn.Position.Y), Is.LessThanOrEqualTo(75f), spawn.EntityName);
            }
        });
    }

    // 02000348's spawn points over its lava stand on the gratings above it: map props with DisableCollision set, whose
    // PhysX mesh is the ground under the points, not the lava cubes below.
    [Test]
    public void SpawnStandsOnDisabledCollisionGrating() {
        string root = Environment.GetEnvironmentVariable("MS2_DATA_FOLDER")!;
        using var exported = new M2dReader(Path.Combine(root, "Resource/Exported.m2d"));
        IMapEntity[]? map = null;
        new XBlockParser(exported, new FlatTypeIndex(exported)).ParseMap("02000348_bf", parsed => map = parsed.ToArray());
        Assert.That(map, Is.Not.Null.And.Not.Empty);
        var lavaGround = new SpawnGround(map!);

        ISpawnPointNPC spawn = map!.OfType<ISpawnPointNPC>().Single(spawn => spawn.EntityName == "EventSpawnPointNPC0_207");
        var hit = lavaGround.Hit(spawn.Position);
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.Value.Source.EntityName, Does.StartWith("tr_fi_prop_grating_"));
        Assert.That(hit.Value.Source is IMS2MapProperties { DisableCollision: true }, Is.True);
        Assert.That(hit.Value.Z, Is.InRange(spawn.Position.Z - SpawnGround.REACH_UP, spawn.Position.Z));
    }

    // A local check against npc positions the official server sent for Tria: SPAWN_GROUND_CHECK names a tsv (map id,
    // npc id, x, y, z) that is not part of the repository; skipped without it. Each row whose x, y is a spawn point of
    // that npc must have the z SpawnGround places the npc at.
    [Test]
    public void MatchesOfficialPositions() {
        string? path = Environment.GetEnvironmentVariable("SPAWN_GROUND_CHECK");
        if (string.IsNullOrEmpty(path)) {
            Assert.Ignore("SPAWN_GROUND_CHECK is not set");
        }

        ISpawnPointNPC[] spawns = entities!.OfType<ISpawnPointNPC>().ToArray();
        int rows = 0, atPoint = 0, same = 0;
        var wrong = new List<string>();
        foreach (string line in System.IO.File.ReadLines(path!)) {
            string[] f = line.Split('\t');
            if (f.Length < 5 || f[0] != "2000001") {
                continue;
            }
            rows++;
            float x = float.Parse(f[2], CultureInfo.InvariantCulture), y = float.Parse(f[3], CultureInfo.InvariantCulture);
            float z = float.Parse(f[4], CultureInfo.InvariantCulture);
            ISpawnPointNPC? spawn = spawns.FirstOrDefault(spawn => spawn.NpcList.ContainsKey(f[1])
                && Math.Abs(spawn.Position.X - x) < 1 && Math.Abs(spawn.Position.Y - y) < 1);
            if (spawn is null) {
                continue;
            }
            atPoint++;
            float placed = ground!.Place(spawn.Position).Z;
            if (Math.Abs(placed - z) < 0.5f) {
                same++;
            } else {
                wrong.Add($"{spawn.EntityName} {f[1]} official {z} placed {placed}");
            }
        }
        TestContext.Out.WriteLine($"rows {rows}, at a spawn point {atPoint}, same z {same}");
        foreach (string w in wrong.Distinct()) {
            TestContext.Out.WriteLine(w);
        }
        Assert.That(atPoint, Is.GreaterThan(0));
        Assert.That(wrong, Is.Empty);
    }
}

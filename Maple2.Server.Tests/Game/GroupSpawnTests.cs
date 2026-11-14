using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Xml;
using Maple2.Database.Extensions;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// EventSpawnGroupNPC points (Dark Stream): the ingest gives each point the groupSpawn.xml npcs of its tag, and a
// spawn picks one group by weight and spawns its count of those npcs.
public class GroupSpawnTests {
    private const string Table = """
        <?xml version="1.0" encoding="utf-8"?>
        <ms2>
          <group groupId="1">
            <npc id="49000001" tag="sf" score="3" />
            <npc id="49001001" tag="dungeon" score="3" />
            <npc id="49001002" tag="dungeon" score="3" />
          </group>
          <group groupId="2">
            <npc id="49101001" tag="dungeon" score="6" />
          </group>
          <group groupId="5">
            <npc id="49400001" tag="sf,dungeon,steampunk,temple,toy" score="" />
            <npc id="49400002" tag="sf, dungeon" score="" />
          </group>
          <group groupId="111">
            <npc id="29000320" tag="skyfotress" score="2" />
          </group>
        </ms2>
        """;

    private static Dictionary<int, List<ServerGroupSpawn.Npc>> Read() =>
        ServerGroupSpawn.Read(XmlReader.Create(new StringReader(Table.Trim())));

    [Test]
    public void Read_GroupsAndTagLists() {
        Dictionary<int, List<ServerGroupSpawn.Npc>> table = Read();
        Assert.Multiple(() => {
            Assert.That(table.Keys, Is.EqualTo(new[] { 1, 2, 5, 111 }));
            Assert.That(table[1].Select(npc => npc.Id), Is.EqualTo(new[] { 49000001, 49001001, 49001002 }));
            Assert.That(table[5][1].Tags, Is.EqualTo(new[] { "sf", "dungeon" }));
        });
    }

    // 03100001's shape: groups 1/2/3 at counts 12/6/3, weights 1/1/1, tag dungeon. Group 3 has no row in the table
    // and an npc the npc table lacks is left out.
    [Test]
    public void Groups_NpcsOfTheTagThatExist() {
        List<SpawnGroupEntry> groups = ServerGroupSpawn.Groups(Read(), "dungeon", [(2, 6), (1, 12), (3, 3), (5, 1)],
            new Dictionary<int, int> { [1] = 1, [2] = 1, [3] = 1 }, id => id != 49001002);

        Assert.That(groups.Select(group => (group.GroupId, group.Count, group.Weight, string.Join(",", group.NpcIds))), Is.EqualTo(new[] {
            (1, 12, 1, "49001001"),
            (2, 6, 1, "49101001"),
            (5, 1, 0, "49400001,49400002"),
        }));
    }

    // 52010038's point whose only weight is under 104111: the group is kept with weight 0; no tag match, no group.
    [Test]
    public void Groups_WeightUnderAnotherIndex_NoTagMatch() {
        Assert.That(ServerGroupSpawn.Groups(Read(), "skyfotress", [(111, 50)], new Dictionary<int, int> { [104111] = 1 }, _ => true)
            .Single(), Has.Property("Weight").EqualTo(0));
        Assert.That(ServerGroupSpawn.Groups(Read(), "madracan", [(1, 3)], new Dictionary<int, int>(), _ => true), Is.Empty);
    }

    private static SpawnGroupEntry Group(int id, int count, int weight, params int[] npcs) => new(id, count, weight, npcs);

    [TestCase(0, 1)]
    [TestCase(2, 1)]
    [TestCase(3, 2)]
    [TestCase(3 + 2 - 1, 2)]
    [TestCase(5, 0)]
    public void Pick_ByWeight(int roll, int groupId) {
        SpawnGroupEntry[] groups = [Group(1, 12, 3, 1), Group(2, 6, 2, 2), Group(9, 1, 0, 3)];
        Assert.That(GroupSpawnRules.TotalWeight(groups), Is.EqualTo(5));
        Assert.That(GroupSpawnRules.Pick(groups, roll)?.GroupId ?? 0, Is.EqualTo(groupId));
    }

    [Test]
    public void Pick_NoWeights_EachGroupWeighsOne() {
        SpawnGroupEntry[] groups = [Group(111, 50, 0, 1), Group(112, 1, 0, 2)];
        Assert.That(GroupSpawnRules.TotalWeight(groups), Is.EqualTo(2));
        Assert.That(new[] { GroupSpawnRules.Pick(groups, 0)!.GroupId, GroupSpawnRules.Pick(groups, 1)!.GroupId }, Is.EqualTo(new[] { 111, 112 }));
    }

    private static EventSpawnGroupNPC Point(int spawnId, params SpawnGroupEntry[] groups) =>
        new("", spawnId, new Vector3(300, 0, 0), new Vector3(0, 0, 90), true, true, 0, 1, 0, 0, "", "dungeon", groups);

    [Test]
    public void NpcIds_CountOfThePickedGroup() {
        List<int> ids = GroupSpawnRules.NpcIds(Point(1001, Group(1, 12, 1, 21000001, 21000002), Group(3, 3, 0, 21000003)), new Random(7));
        Assert.Multiple(() => {
            Assert.That(ids, Has.Count.EqualTo(12));
            Assert.That(ids.All(id => id is 21000001 or 21000002), Is.True);
        });
        Assert.That(GroupSpawnRules.NpcIds(Point(1002), new Random(7)), Is.Empty);
        Assert.That(GroupSpawnRules.NpcIds(Point(1003, Group(1, 5, 1)), new Random(7)), Is.Empty);
    }

    // A 03100001 row as the ingest writes it (weight 0 left out) reads back as a group point through the map-entity
    // column's json options.
    [Test]
    public void MapEntityRow_ReadsBackAsAGroupPoint() {
        const string row = """
            {"!": 1797437029, "Groups": [{"Count": 12, "NpcIds": [49001001, 49001002], "Weight": 1, "GroupId": 1}, {"Count": 50, "NpcIds": [29000320], "GroupId": 111}],
             "NpcList": [], "Visible": true, "EntityId": "c2d93f0840aa4ee8a9f9723da703c73f", "GroupTag": "dungeon", "NpcCount": 1,
             "Position": {"X": 600, "Y": 2550, "Z": 600}, "Rotation": {"Z": 270}, "PatrolData": "", "SpawnRadius": 450, "SpawnPointId": 1115,
             "RegenCheckTime": 10, "SpawnAnimation": "Regen_A", "SpawnOnFieldCreate": true}
            """;
        JsonSerializerOptions options = PropertyBuilderExtensions.Options;
        var point = (EventSpawnGroupNPC) JsonSerializer.Deserialize<MapBlock>(row, options)!;
        Assert.Multiple(() => {
            Assert.That((point.SpawnPointId, point.GroupTag, point.SpawnRadius, point.Position), Is.EqualTo((1115, "dungeon", 450f, new Vector3(600, 2550, 600))));
            Assert.That(point.Groups.Select(group => (group.GroupId, group.Count, group.Weight, string.Join(",", group.NpcIds))),
                Is.EqualTo(new[] { (1, 12, 1, "49001001,49001002"), (111, 50, 0, "29000320") }));
            Assert.That(JsonSerializer.Deserialize<MapBlock>(JsonSerializer.Serialize<MapBlock>(point, options), options), Is.TypeOf<EventSpawnGroupNPC>());
        });
    }
}

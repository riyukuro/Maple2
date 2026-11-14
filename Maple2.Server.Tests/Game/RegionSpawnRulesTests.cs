using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The npcs a region spawn row's tags pick and their weights (RegionSpawnRules, the client's FindSpawnResult), on KR rows.
public class RegionSpawnRulesTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Ice = "\uC5BC\uC74C";
    private const string Event = "\uC774\uBCA4\uD2B8";
    private const string Machine = "\uAE30\uACC4";
    private const string BloodPig = "\uD53C\uB3FC\uC9C0";

    private static NpcMetadata Npc(int id, int difficulty, int rareDegree, string[] mainTags, string[] subTags) {
        NpcMetadata npc = TriggerNpcSpawnTests.Npc(id, 0);
        return npc with {
            Basic = npc.Basic with { Difficulty = difficulty, RareDegree = rareDegree, MainTags = mainTags, SubTags = subTags },
        };
    }

    // KR npcdata: 21091026 and 21091031 (main and sub tag ice; difficulty 54 and 56, rare degree 100) and 21091033
    // (main and sub tags ice and machine, difficulty 56).
    private static readonly NpcMetadata IceMob54 = Npc(21091026, 54, 100, [Ice], [Ice]);
    private static readonly NpcMetadata IceMob56 = Npc(21091031, 56, 100, [Ice], [Ice]);
    private static readonly NpcMetadata IceMachine = Npc(21091033, 56, 0, [Ice, Machine], [Ice, Machine]);

    private static (int Id, int Weight)[] Picked(FieldMobSpawn spawn) {
        var npcs = (WeightedSet<NpcMetadata>) typeof(FieldMobSpawn).GetField("npcs", Any)!.GetValue(spawn)!;
        return npcs.Select(entry => (entry.Item1.Id, entry.Item2)).OrderBy(entry => entry.Item1).ToArray();
    }

    // 2000264 spawn 1 (KR mapspawntag: tags ice and event, difficulty 54 to 56, population 5): the client keeps the
    // npcs whose main tags the row all names, so not 21091033 (its machine tag is not named), and weighs each by the
    // row's max difficulty less its own: 2 x 20 + 2 + 1 + (12 - 2) = 53 for 21091026, 55 for 21091031. Each npc of a
    // tag used to be taken, weighed by its own tag counts and difficulty (44 each, and 64 for 21091033).
    [Test]
    public void IceEventRegion_TakesOnlyFullyNamedNpcs() {
        Start();
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field,
            TriggerNpcSpawnTests.NpcTable(IceMob54, IceMob56, IceMachine));
        typeof(FieldManager).GetField("fieldMobSpawns", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldMobSpawn>());
        var row = new MapMetadataSpawn(1, 54, 56, 5, 0, [Ice, Event], 0, 0, new Dictionary<int, int>());
        var region = new Ms2RegionSpawn(1, false, Vector3.Zero, Vector3.Zero);

        FieldMobSpawn? spawn = Field.AddMobSpawn(row, region, [IceMob54.Id, IceMob56.Id, IceMachine.Id]);

        Assert.That(spawn, Is.Not.Null);
        Assert.That(Picked(spawn!), Is.EqualTo(new[] { (IceMob54.Id, 5300), (IceMob56.Id, 5500) }));
    }

    // A row naming only part of an npc's main tags does not spawn it: an ice-only row has no 21091033, a row of ice
    // and machine has it at 3 x 20 + 3 + 0 + 12 = 75. A difficulty outside the row's range leaves the npc out.
    [Test]
    public void Weight_NeedsEveryMainTag() {
        Assert.That(RegionSpawnRules.Weight(IceMachine.Basic, [Ice], 54, 56), Is.Null);
        Assert.That(RegionSpawnRules.Weight(IceMachine.Basic, [Ice, Machine], 54, 56), Is.EqualTo(7500));
        Assert.That(RegionSpawnRules.Weight(IceMob56.Basic, [Ice], 50, 55), Is.Null);
        Assert.That(RegionSpawnRules.Weight(IceMob54.Basic, [Event], 54, 56), Is.Null);
    }

    // 25020405 (KR NpcRegionSpawnData: no main tags, sub tag blood pig, difficulty 75, rare degree 100) comes in on
    // its sub tag alone: 1 + 2 + 1 + (12 - 0) = 16 on a row up to difficulty 75.
    [Test]
    public void Weight_SubTagAloneForAnNpcWithoutMainTags() {
        NpcMetadata pig = Npc(25020405, 75, 100, [], [BloodPig]);
        Assert.That(RegionSpawnRules.Weight(pig.Basic, [BloodPig], 70, 75), Is.EqualTo(1600));
        Assert.That(RegionSpawnRules.Hits(pig.Basic, [BloodPig, Ice]), Is.EqualTo((0, 1)));
    }
}

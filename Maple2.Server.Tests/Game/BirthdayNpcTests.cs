using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// FieldManager.SpawnBirthdayNpcs: the birthdaytable npcs of the field, on a day inside the table's day range, at their
// spawn point's position; an npc without metadata is not spawned.
public class BirthdayNpcTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int CakeId = 11003587;
    private const int DuckyId = 11003588;
    private const int MissingId = 11003599;

    private void StartField(int firstDay, int lastDay) {
        Start();
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        NpcMetadataStorage npcs = TriggerNpcSpawnTests.NpcTable(TriggerNpcSpawnTests.Npc(CakeId, 1), TriggerNpcSpawnTests.Npc(DuckyId, 1));
        // An npc not in the cache is looked up in the test's (empty) metadata store and not found.
        typeof(MetadataStorage<int, NpcMetadata>).GetField("Context", Any)!.SetValue(npcs,
            typeof(MetadataStorage<int, MapMetadata>).GetField("Context", Any)!.GetValue(test.MapMetadata));
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field, npcs);
        var field = new BirthdayTable.Field([
            new BirthdayTable.Npc(CakeId, 101, new Vector3(300, 0, 0), new Vector3(0, 0, 90)),
            new BirthdayTable.Npc(DuckyId, 102, new Vector3(-300, 0, 0), new Vector3(0, 0, 30)),
            new BirthdayTable.Npc(MissingId, 103, new Vector3(0, 300, 0), Vector3.Zero),
        ], [10221]);
        test.SetServerTable(new BirthdayTable(firstDay, lastDay, new Dictionary<int, BirthdayTable.Field> {
            [Field.MapId] = field,
            [Field.MapId + 1] = new BirthdayTable.Field([new BirthdayTable.Npc(CakeId, 101, Vector3.Zero, Vector3.Zero)], []),
        }));
    }

    // Every day of "1-7": the field's npcs at their points; the npc without metadata and the other field's are not spawned.
    [Test]
    public void SpawnsFieldNpcs() {
        StartField(1, 7);

        Field.SpawnBirthdayNpcs(DayOfWeek.Sunday);

        // The npc rotation comes back to within float rounding.
        Assert.That(Field.Npcs.Values.Select(npc => (npc.Value.Metadata.Id, npc.Position, MathF.Round(npc.Rotation.Z))), Is.EquivalentTo(new[] {
            (CakeId, new Vector3(300, 0, 0), 90f),
            (DuckyId, new Vector3(-300, 0, 0), 30f),
        }));
    }

    // Outside the day range (ISO weekdays, Monday = 1) none is spawned.
    [Test]
    public void OutsideDayRange_None() {
        StartField(1, 5);

        Field.SpawnBirthdayNpcs(DayOfWeek.Saturday);
        Assert.That(Field.Npcs, Is.Empty);
        Field.SpawnBirthdayNpcs(DayOfWeek.Friday);
        Assert.That(Field.Npcs, Has.Count.EqualTo(2));
    }

    [Test]
    public void Active_IsoWeekdays() {
        var table = new BirthdayTable(6, 7, new Dictionary<int, BirthdayTable.Field>());
        Assert.That(Enum.GetValues<DayOfWeek>().Where(table.Active), Is.EquivalentTo(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }));
    }
}

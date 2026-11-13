using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// The Add packet of an interact object always says reactable (InteractObject.WriteTo). The objects of combine spawn
// groups (a survival chest, interact 11000037) and of AI createInteractObject nodes are never updated before they are
// opened, so they start reactable too: before, they started normal and every open was refused with nothing sent.
public class SpawnedInteractStateTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldManager Field(TestGameSession test) {
        // 11000037: a mesh object, react 1000, hide 10000, reset 60000000, no react count.
        var chest = new InteractObjectMetadata(11000037, InteractType.Mesh, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0,
            WeaponItemId: 0, new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(60000000, 1000, 10000),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);
        test.SetTable(new InteractObjectTable(new Dictionary<int, InteractObjectMetadata> { [chest.Id] = chest }));

        FieldManager field = test.Session.Field!;
        typeof(FieldManager).GetField("fieldChests", Any)!.SetValue(field, new ConcurrentDictionary<string, FieldInteract>());
        var entities = (MapEntityMetadata) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        typeof(MapEntityMetadata).GetField("<RegionSpawns>k__BackingField", Any)!.SetValue(entities,
            new Dictionary<int, Ms2RegionSpawn> { [7001] = new Ms2RegionSpawn(7001, false, new Vector3(150, 300, 450), Vector3.Zero) });
        typeof(FieldManager).GetField("<Entities>k__BackingField", Any)!.SetValue(field, entities);
        return field;
    }

    private static ConcurrentDictionary<string, FieldInteract> Chests(FieldManager field) =>
        (ConcurrentDictionary<string, FieldInteract>) typeof(FieldManager).GetField("fieldChests", Any)!.GetValue(field)!;

    [Test]
    public void ACombineSpawnObjectStartsReactable() {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = Field(test);

        field.SpawnInteractObject(new SpawnInteractObjectMetadata(1, 20, 100, 7001, 11000037, "", "", "", "", 1f, false));
        FieldInteract chest = Chests(field).Values.Single();
        Assert.Multiple(() => {
            Assert.That(chest.SpawnId, Is.EqualTo(7001));
            Assert.That(chest.State, Is.EqualTo(InteractState.Reactable));
            Assert.That(chest.React(), Is.True);
            // Opened: it waits its reset time before it can be opened again.
            Assert.That(chest.State, Is.EqualTo(InteractState.Normal));
            Assert.That(chest.React(), Is.False);
        });
    }

    [Test]
    public void AnAiNodeObjectStartsReactable() {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = Field(test);

        var node = new AiMetadata.CreateInteractObjectNode("createInteractObject", [], "Opened", 11000037, LifeTime: 0, "", "Idle_A");
        FieldInteract? made = field.AddAiInteractObject(node, Vector3.Zero, Vector3.Zero);
        Assert.Multiple(() => {
            Assert.That(made, Is.Not.Null);
            Assert.That(made!.State, Is.EqualTo(InteractState.Reactable));
            Assert.That(made.React(), Is.True);
        });
    }
}

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// The map treasure chests (FieldManager.UpdateChests): a set is added when the field has chest points, every golden
// chest and some of the wooden ones, none again until a cycle has passed, and then the left ones are replaced by a new
// set. The field's points and the interact rows are made up; the id ranges are the rules' (wood 2000s, gold 3000s).
public class MapChestCycleTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int GoldInteract = 11000005;

    private FieldManager field = null!;

    private static InteractObjectMetadata Chest(int id) =>
        new(id, InteractType.Mesh, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0,
            WeaponItemId: 0, new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(60000000, 1000, 10000),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);

    private void Set(string name, object value) => typeof(FieldManager).GetField(name, Any)!.SetValue(field, value);

    [SetUp]
    public void SetUp() {
        var test = new TestGameSession();
        test.Start();
        test.SetTable(new InteractObjectTable(new Dictionary<int, InteractObjectMetadata> {
            [MapChestRules.WoodInteractId] = Chest(MapChestRules.WoodInteractId),
            [GoldInteract] = Chest(GoldInteract),
        }));
        field = test.Session.Field!;
        Set("fieldChests", new ConcurrentDictionary<string, FieldInteract>());
        var entities = (MapEntityMetadata) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        typeof(MapEntityMetadata).GetField("<RegionSpawns>k__BackingField", Any)!.SetValue(entities, new Dictionary<int, Ms2RegionSpawn> {
            [2001] = new(2001, false, new Vector3(150, 300, 450), Vector3.Zero),
            [2002] = new(2002, false, new Vector3(450, 300, 450), Vector3.Zero),
            [3001] = new(3001, false, new Vector3(750, 300, 450), Vector3.Zero),
        });
        typeof(FieldManager).GetField("<Entities>k__BackingField", Any)!.SetValue(field, entities);
        Set("mapChestsRead", true);
        Set("woodChestPoints", new List<int> { 2001, 2002 });
        Set("goldChestPoints", new Dictionary<int, int> { [3001] = GoldInteract });
    }

    private ConcurrentDictionary<string, FieldInteract> Chests =>
        (ConcurrentDictionary<string, FieldInteract>) typeof(FieldManager).GetField("fieldChests", Any)!.GetValue(field)!;

    private void Update(long tick) => typeof(FieldManager).GetMethod("UpdateChests", Any)!.Invoke(field, [tick]);

    [Test]
    public void TheFirstUpdateAddsTheGoldenChestAndSomeWoodenOnes() {
        Update(1000);

        FieldInteract[] chests = Chests.Values.ToArray();
        Assert.That(chests.Count(chest => chest.Chest == MapChestRules.Kind.Gold), Is.EqualTo(1));
        Assert.That(chests.Single(chest => chest.Chest == MapChestRules.Kind.Gold).SpawnId, Is.EqualTo(3001));
        Assert.That(chests.Count(chest => chest.Chest == MapChestRules.Kind.Wood), Is.InRange(1, 2));
        Assert.That(chests.All(chest => chest.State == InteractState.Reactable));
    }

    [Test]
    public void NothingIsAddedAgainInsideTheCycle_AndTheSetIsReplacedAfterIt() {
        Update(1000);
        FieldInteract gold = Chests.Values.Single(chest => chest.Chest == MapChestRules.Kind.Gold);

        Update(1000 + 1000);
        Assert.That(Chests.Values.Single(chest => chest.Chest == MapChestRules.Kind.Gold), Is.SameAs(gold));

        Update(1000 + (long) MapChestRules.Cycle.TotalMilliseconds + 1);
        FieldInteract replaced = Chests.Values.Single(chest => chest.Chest == MapChestRules.Kind.Gold);
        Assert.That(replaced, Is.Not.SameAs(gold));
    }

    [Test]
    public void WithoutChestPoints_NothingIsAdded() {
        Set("woodChestPoints", new List<int>());
        Set("goldChestPoints", new Dictionary<int, int>());

        Update(1000);

        Assert.That(Chests, Is.Empty);
    }
}

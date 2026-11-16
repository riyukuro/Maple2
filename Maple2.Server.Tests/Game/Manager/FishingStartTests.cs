using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// Fishing mode 1 (start of a cast) and mode 8 (the catch): the answers for a spot with no fish, a box that draws
// none, a cast that bites, and a catch that is a miss.
public class FishingStartTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Vector3 Tile = new(450, 450, 0);
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Maple2.Model.Game.Item rod = null!;

    private static FishTable.Fish Fish(int baitProbability) => new(10000001, LiquidType.water, 0, 6, 1, 3, 1, 54, 15000, 10000, baitProbability,
        new FishTable.Range<int>(20, 30), new FishTable.Range<int>(40, 50), [], 0, false);

    private void Begin(FishTable fishTable) {
        test = new TestGameSession();
        ItemMetadata rodData = TestGameSession.Metadata(32000001, type: 2, slotMax: 1,
            function: new ItemMetadataFunction(ItemFunction.FishingRod, "FishingRod", "1", false));
        test.Register(rodData);
        test.SetTable(new FishingRodTable(new Dictionary<int, FishingRodTable.Entry> { [1] = new(32000001, 0, 0, 0) }));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        test.SetServerTable(fishTable);
        rod = test.Store(rodData);
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        session.Fishing = new FishingManager(session, test.TableMetadata, test.ServerTableMetadata);
        typeof(FieldManager).GetField("<AccelerationStructure>k__BackingField", Any)!
            .SetValue(session.Field, new FieldAccelerationStructure());
        // The prepared state: the rod, one water tile and the guide object.
        typeof(FishingManager).GetField("rod", Any)!.SetValue(session.Fishing, rod);
        var entity = new FieldFluidEntity(default, Tile, Vector3.Zero, 1f, LiquidType.water, default, 0, false, false, default);
        typeof(FishingManager).GetField("tiles", Any)!.SetValue(session.Fishing, new Dictionary<Vector3, FishingTile> { [Tile] = new FishingTile(entity, 0) });
        session.GuideObject = (FieldGuideObject) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(FieldGuideObject));
    }

    private static FishTable Table(bool spot, bool box, int baitProbability) {
        var spots = new Dictionary<int, FishTable.Spot>();
        if (spot) {
            spots[2000001] = new FishTable.Spot(2000001, 0, 0, [LiquidType.water], 1, 0, 0, 0, 1, 0);
        }
        var boxes = new Dictionary<int, FishTable.FishBox>();
        if (box) {
            boxes[1] = new FishTable.FishBox(1, 10000, 0, new Dictionary<int, int> { [10000001] = 10 });
        }
        return new FishTable(new Dictionary<int, FishTable.Fish> { [10000001] = Fish(baitProbability) }, spots,
            new Dictionary<int, IReadOnlyDictionary<short, FishTable.Lure>>(), boxes, new Dictionary<int, FishTable.FishBox>());
    }

    [Test]
    public void Start_AMapWithNoSpotHasNoFish() {
        Begin(Table(spot: false, box: true, baitProbability: 10000));
        Assert.That(session.Fishing.Start(Tile), Is.EqualTo(FishingError.s_fishing_error_notexist_fish));
        Assert.That(session.Fishing.Casting, Is.False);
    }

    [Test]
    public void Start_ABoxWithNoFishHasNoFish() {
        Begin(Table(spot: true, box: false, baitProbability: 10000));
        Assert.That(session.Fishing.Start(Tile), Is.EqualTo(FishingError.s_fishing_error_notexist_fish));
        Assert.That(session.Fishing.Casting, Is.False);
    }

    [Test]
    public void Start_ATileThatIsNotPreparedIsRefused() {
        Begin(Table(spot: true, box: true, baitProbability: 10000));
        Assert.That(session.Fishing.Start(new Vector3(1, 2, 3)), Is.EqualTo(FishingError.s_fishing_error_system_error));
    }

    [Test]
    public void Start_ABitingFishOpensTheCast() {
        Begin(Table(spot: true, box: true, baitProbability: 10000));
        Assert.That(session.Fishing.Start(Tile), Is.EqualTo(FishingError.none));
        Assert.That(session.Fishing.Casting, Is.True);
    }

    [Test]
    public void Catch_AMissEndsTheCast() {
        Begin(Table(spot: true, box: true, baitProbability: 10000));
        Assert.That(session.Fishing.Start(Tile), Is.EqualTo(FishingError.none));
        // Result 1 before the bite tick is a miss: the fish got away, and one catch ends the cast.
        typeof(FishingManager).GetField("biteTick", Any)!.SetValue(session.Fishing, long.MaxValue);
        typeof(FishingManager).GetField("fishFightGame", Any)!.SetValue(session.Fishing, false);
        Assert.That(session.Fishing.Catch(true), Is.EqualTo(FishingError.none));
        Assert.That(session.Fishing.Casting, Is.False);
    }
}

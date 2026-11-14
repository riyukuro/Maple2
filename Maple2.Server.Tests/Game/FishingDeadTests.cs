using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A dead player does not fish: the client checks the player's state before it prepares (Fishing 0), so a prepare or
// start from a dead player is refused and a catch sent while dead ends the cast with no fish. A start whose rod (the
// one picked at the prepare) has left the bag is refused: the client stops fishing once the rod is gone.
public class FishingDeadTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Item rod = null!;

    private void Start() {
        test = new TestGameSession();
        ItemMetadata rodData = TestGameSession.Metadata(32000001, type: 2, slotMax: 1,
            function: new ItemMetadataFunction(ItemFunction.FishingRod, "FishingRod", "1", false));
        test.Register(rodData);
        test.SetTable(new FishingRodTable(new Dictionary<int, FishingRodTable.Entry> { [1] = new(32000001, 100, 0, 0) }));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        rod = test.Store(rodData);
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        session.Fishing = new FishingManager(session, test.TableMetadata, test.ServerTableMetadata);
        typeof(FieldManager).GetField("<AccelerationStructure>k__BackingField", Any)!
            .SetValue(session.Field, new FieldAccelerationStructure());
    }

    private void SetDead() => typeof(FieldPlayer).GetField("isDead", Any)!.SetValue(session.Player, true);

    // The prepared state Start reads: the rod picked at Prepare and the guide object it spawned.
    private void Prepared(Item preparedRod) {
        typeof(FishingManager).GetField("rod", Any)!.SetValue(session.Fishing, preparedRod);
        session.GuideObject = (FieldGuideObject) RuntimeHelpers.GetUninitializedObject(typeof(FieldGuideObject));
    }

    [Test]
    public void Prepare_RefusedWhileDead() {
        Start();
        // Alive, the rod passes to the next check, the rod's mastery (the character has none).
        Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.s_fishing_error_fishingrod_mastery));

        SetDead();
        Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.s_fishing_error_system_error));
        Assert.That(session.GuideObject, Is.Null);
    }

    [Test]
    public void Start_RefusesARodThatLeftTheBag() {
        Start();
        Prepared(rod);
        // The rod in the bag passes to the tile lookup (no tiles here).
        Assert.That(session.Fishing.Start(default), Is.EqualTo(FishingError.s_fishing_error_system_error));

        Prepared(new Item(rod.Metadata) { Uid = rod.Uid + 1000 });
        Assert.That(session.Fishing.Start(default), Is.EqualTo(FishingError.s_fishing_error_invalid_item));
    }

    [Test]
    public void Start_RefusedWhileDead() {
        Start();
        // A rod that left the bag: alive it answers invalid_item, dead the player check comes first.
        Prepared(new Item(rod.Metadata) { Uid = rod.Uid + 1000 });
        SetDead();
        Assert.That(session.Fishing.Start(default), Is.EqualTo(FishingError.s_fishing_error_system_error));
        Assert.That(session.Fishing.Casting, Is.False);
    }

    [Test]
    public void Catch_WhileDead_EndsTheCastWithNoFish() {
        Start();
        var fish = new FishTable.Fish(10000001, LiquidType.water, 80, 6, 1, 3, 1, 54, 15000, 10000, 8000,
            new FishTable.Range<int>(20, 30), new FishTable.Range<int>(40, 50), [], 0, false);
        session.Fishing.Cast((FishingTile) RuntimeHelpers.GetUninitializedObject(typeof(FishingTile)), fish,
            fishBites: true, fishBiteTick: 0, fightGame: false);
        int before = session.Mastery[MasteryType.Fishing];
        SetDead();

        Assert.That(session.Fishing.Catch(true), Is.EqualTo(FishingError.none));
        Assert.That(session.Fishing.Casting, Is.False);
        Assert.That(session.Mastery[MasteryType.Fishing], Is.EqualTo(before));
    }
}

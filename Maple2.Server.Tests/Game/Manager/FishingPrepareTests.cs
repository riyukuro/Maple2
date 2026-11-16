using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game.Manager;

// Fishing mode 0 (the rod is used): the server finds the water in front of the player, the 3x5 block box the
// facing picks (the box's near edge two blocks off to the side, its far edge three ahead), sends the tiles, puts the
// guide on the nearest one and answers Prepare. The stop (Reset) takes the guide off again. Then the cast's
// branches the other tests leave: a fish that does not bite, a seawater spot, the spot's own box and a size lure.
public class FishingPrepareTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MapId = 2000001;
    private const float Block = Constant.BlockSize;
    private const int BoreDuration = 15000;
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Maple2.Model.Game.Item rod = null!;

    private static FishTable.Fish Fish(LiquidType habitat = LiquidType.water, int baitProbability = 10000) => new(10000001, habitat, 0, 6, 1, 3, 1, 54,
        15000, 10000, baitProbability, new FishTable.Range<int>(20, 30), new FishTable.Range<int>(40, 50), [], 0, false);

    private static FishTable Table(FishTable.Fish fish, int spotMastery = 0, LiquidType[]? liquids = null, bool individualBox = false) {
        var box = new FishTable.FishBox(1, 10000, 0, new Dictionary<int, int> { [fish.Id] = 10 });
        return new FishTable(new Dictionary<int, FishTable.Fish> { [fish.Id] = fish },
            new Dictionary<int, FishTable.Spot> {
                [MapId] = new(MapId, spotMastery, 100, liquids ?? [LiquidType.water], individualBox ? 0 : 1, individualBox ? 1 : 0, 0, 0, 1, 0),
            },
            new Dictionary<int, IReadOnlyDictionary<short, FishTable.Lure>>(),
            individualBox ? new Dictionary<int, FishTable.FishBox>() : new Dictionary<int, FishTable.FishBox> { [1] = box },
            individualBox ? new Dictionary<int, FishTable.FishBox> { [1] = box } : new Dictionary<int, FishTable.FishBox>());
    }

    // A surface water block at the cell (one cube of the field's grid).
    private static FieldFluidEntity Water(Vector3S cell) {
        Vector3 at = Block * new Vector3(cell.X, cell.Y, cell.Z);
        var bounds = new BoundingBox3(at - new Vector3(Block / 2, Block / 2, 0), at + new Vector3(Block / 2, Block / 2, Block));
        return new FieldFluidEntity(new FieldEntityId(0, 0, string.Empty), at, default, 1f, LiquidType.water, bounds, 0, false, true, MapAttribute.none);
    }

    private void Begin(FishTable table, int rodMastery = 0, params Vector3S[] water) {
        test = new TestGameSession();
        ItemMetadata rodData = TestGameSession.Metadata(32000001, type: 2, slotMax: 1,
            function: new ItemMetadataFunction(ItemFunction.FishingRod, "FishingRod", "1", false));
        test.Register(rodData);
        test.SetTable(new FishingRodTable(new Dictionary<int, FishingRodTable.Entry> { [1] = new(32000001, rodMastery, 0, 0) }));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        test.SetServerTable(table);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { fisherBoreDuration = BoreDuration });
        rod = test.Store(rodData);
        test.Start();
        test.EnterMap(TestGameSession.Map(MapId));
        session.Fishing = new FishingManager(session, test.TableMetadata, test.ServerTableMetadata);
        var structure = new FieldAccelerationStructure();
        if (water.Length > 0) {
            var grid = water.ToDictionary(cell => cell, cell => new List<FieldEntity> { Water(cell) });
            var min = new Vector3S(water.Min(c => c.X), water.Min(c => c.Y), water.Min(c => c.Z));
            var max = new Vector3S(water.Max(c => c.X), water.Max(c => c.Y), water.Max(c => c.Z));
            structure.AddEntities(grid, min, max, [], 0);
        }
        typeof(FieldManager).GetField("<AccelerationStructure>k__BackingField", Any)!.SetValue(session.Field, structure);
        session.Player.Position = Vector3.Zero;
        test.TakeSent();
    }

    private static byte Command(byte[] packet) => packet[2];

    private List<byte> FishingCommands() =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Fishing).Select(Command).ToList();

    // Each facing finds the water block in its own box (one block under the player's feet, the surface).
    [TestCase(Constant.NorthEast, 2, 0)]
    [TestCase(-175f, 0, 2)]
    [TestCase(-95f, -2, 0)]
    [TestCase(Constant.SouthWest, -2, 0)]
    [TestCase(Constant.SouthEast, 0, -2)]
    public void Prepare_FindsTheWaterTheFacingPoints(float facing, int x, int y) {
        var cell = new Vector3S((short) x, (short) y, -1);
        Begin(Table(Fish()), 0, cell);
        session.Player.Rotation = new Vector3(0, 0, facing);

        Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.none));

        // The tiles (mode 4) come before the Prepare answer (mode 0); the guide stands one block above the tile.
        Assert.That(FishingCommands(), Is.EqualTo(new byte[] { 4, 0 }));
        Assert.That(session.GuideObject, Is.Not.Null);
        Assert.That(session.GuideObject!.Value, Is.InstanceOf<FishingGuideObject>());
        Assert.That(session.GuideObject.Position, Is.EqualTo(Block * new Vector3(x, y, 0)));
    }

    // Water behind the player is not in the box: there is no fish.
    [Test]
    public void Prepare_WaterBehindThePlayerIsNotFound() {
        Begin(Table(Fish()), 0, new Vector3S(-2, 0, -1));
        session.Player.Rotation = new Vector3(0, 0, Constant.NorthEast);

        Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.s_fishing_error_notexist_fish));
        Assert.That(session.GuideObject, Is.Null);
    }

    // Of two blocks in the box the guide goes to the one nearer the player.
    [Test]
    public void Prepare_TheGuideGoesToTheNearestTile() {
        Begin(Table(Fish()), 0, new Vector3S(1, 0, -1), new Vector3S(2, 0, -1), new Vector3S(3, 0, -1));
        session.Player.Rotation = new Vector3(0, 0, Constant.NorthEast);

        Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.none));
        Assert.That(session.GuideObject!.Position, Is.EqualTo(new Vector3(Block, 0, 0)));
    }

    // The rod's and the spot's mastery: below either the rod is not used.
    [Test]
    public void Prepare_BelowTheRodsMastery_IsRefused() {
        Begin(Table(Fish()), rodMastery: 10, new Vector3S(2, 0, -1));
        session.Player.Rotation = new Vector3(0, 0, Constant.NorthEast);

        Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.s_fishing_error_fishingrod_mastery));
        Assert.That(session.GuideObject, Is.Null);
    }

    [Test]
    public void Prepare_BelowTheSpotsMastery_IsRefused() {
        Begin(Table(Fish(), spotMastery: 10), 0, new Vector3S(2, 0, -1));
        session.Player.Rotation = new Vector3(0, 0, Constant.NorthEast);

        Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.s_fishing_error_lack_mastery));
        Assert.That(session.GuideObject, Is.Null);
    }

    // The stop: the client is told (mode 1), the guide is gone and a start on the old tile is refused.
    [Test]
    public void Reset_AfterPrepare_TakesTheGuideOff() {
        Begin(Table(Fish()), 0, new Vector3S(2, 0, -1));
        session.Player.Rotation = new Vector3(0, 0, Constant.NorthEast);
        Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.none));
        test.TakeSent();

        session.Fishing.Reset();

        Assert.That(FishingCommands(), Is.EqualTo(new byte[] { 1 }));
        Assert.That(session.GuideObject, Is.Null);
        Assert.That(session.Fishing.Start(Block * new Vector3(2, 0, -1)), Is.EqualTo(FishingError.s_fishing_error_system_error));
    }

    // Without a guide the stop sends nothing.
    [Test]
    public void Reset_WithoutAGuide_SendsNothing() {
        Begin(Table(Fish()));

        session.Fishing.Reset();

        Assert.That(FishingCommands(), Is.Empty);
    }

    // The last AutoFish buff ending while fishing turns the client's auto flag off (mode 10 with 0); not fishing,
    // or another AutoFish buff still held, nothing is sent.
    [TestCase(true, false, ExpectedResult = new byte[] { 10 })]
    [TestCase(true, true, ExpectedResult = new byte[0])]
    [TestCase(false, false, ExpectedResult = new byte[0])]
    public byte[] AutoFishRemoved_WhileFishing_TurnsAutoOff(bool fishing, bool stillAutoFish) {
        Begin(Table(Fish()), 0, new Vector3S(2, 0, -1));
        session.Player.Rotation = new Vector3(0, 0, Constant.NorthEast);
        if (fishing) {
            Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.none));
            test.TakeSent();
        }

        session.Fishing.AutoFishRemoved(stillAutoFish);

        return FishingCommands().ToArray();
    }

    private void Prepared(FishTable table) {
        Begin(table, 0, new Vector3S(2, 0, -1));
        session.Player.Rotation = new Vector3(0, 0, Constant.NorthEast);
        Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.none));
        test.TakeSent();
    }

    private static readonly Vector3 TilePosition = Block * new Vector3(2, 0, -1);

    // A fish that never bites still opens the cast (mode 9), with the bite tick past the bore duration: the client's
    // bar runs out first.
    [Test]
    public void Start_AFishThatDoesNotBite_OpensTheCastWithNoBite() {
        Prepared(Table(Fish(baitProbability: 0)));

        Assert.That(session.Fishing.Start(TilePosition), Is.EqualTo(FishingError.none));

        Assert.That(FishingCommands(), Is.EqualTo(new byte[] { 9 }));
        Assert.That(session.Fishing.Casting, Is.True);
        Assert.That(typeof(FishingManager).GetField("bite", Any)!.GetValue(session.Fishing), Is.False);
        long biteTick = (long) typeof(FishingManager).GetField("biteTick", Any)!.GetValue(session.Fishing)!;
        Assert.That(biteTick, Is.GreaterThan(session.Field!.FieldTick + BoreDuration));
    }

    // A water tile at a spot that has seawater is fished as seawater: a seawater fish is caught there, a water one
    // is not.
    [TestCase(LiquidType.seawater, ExpectedResult = FishingError.none)]
    [TestCase(LiquidType.water, ExpectedResult = FishingError.s_fishing_error_notexist_fish)]
    public FishingError Start_AtASeawaterSpot_TheWaterIsSeawater(LiquidType habitat) {
        Prepared(Table(Fish(habitat), liquids: [LiquidType.water, LiquidType.seawater]));

        return session.Fishing.Start(TilePosition);
    }

    // The spot's own (individual) box gives the fish when there is no global box.
    [Test]
    public void Start_TheSpotsOwnBoxGivesTheFish() {
        Prepared(Table(Fish(), individualBox: true));

        Assert.That(session.Fishing.Start(TilePosition), Is.EqualTo(FishingError.none));
        Assert.That(session.Fishing.Casting, Is.True);
    }

    // A result 0 on a cast without the reel game is the bar running out before the bite: no answer, the cast stays
    // open for the result 1 after it.
    [Test]
    public void Catch_AResultZeroWithoutTheReelGame_LeavesTheCastOpen() {
        Prepared(Table(Fish()));
        Assert.That(session.Fishing.Start(TilePosition), Is.EqualTo(FishingError.none));
        typeof(FishingManager).GetField("fishFightGame", Any)!.SetValue(session.Fishing, false);
        test.TakeSent();

        Assert.That(session.Fishing.Catch(false), Is.EqualTo(FishingError.none));

        Assert.That(FishingCommands(), Is.Empty);
        Assert.That(session.Fishing.Casting, Is.True);
    }

    // The size lure classes 0 and 1: below the small size's max, and from it to the big size's min.
    [TestCase(0, 20, 30)]
    [TestCase(1, 30, 40)]
    public void LureSize_TheSmallClasses(int sizeClass, int min, int max) {
        var lure = new FishTable.Lure(1, 1, [], [], [], sizeClass, 10000, null, null);

        int? size = FishingManager.LureSize(Fish(), [lure], 0, new Random(1));

        Assert.That(size, Is.InRange(min, max - 1));
    }
}

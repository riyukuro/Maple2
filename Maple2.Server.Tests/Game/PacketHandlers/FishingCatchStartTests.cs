using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Fishing 0x0073 as the client's fishing manager writes it: mode 9 (the cast) is the tile as an i32 (x, y, z bytes;
// FUN_140cdb1e0), mode 8 (the catch) is one byte, the result (CFishingManager::vfunc_o8_0). The handler takes them only with
// the fishing guide out, and answers a refused cast with Fishing mode 2 (i16 error).
public class FishingCatchStartTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Vector3 Tile = new(450, 450, 0);
    private const int TileBlock = 3 | (3 << 8);
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Begin(bool spot = true, bool guide = true) {
        test = new TestGameSession();
        ItemMetadata rodData = TestGameSession.Metadata(32000001, type: 2, slotMax: 1,
            function: new ItemMetadataFunction(ItemFunction.FishingRod, "FishingRod", "1", false));
        test.Register(rodData);
        var rodEntry = new FishingRodTable.Entry(32000001, 0, 0, 0);
        test.SetTable(new FishingRodTable(new Dictionary<int, FishingRodTable.Entry> { [1] = rodEntry }));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        var spotRow = new FishTable.Spot(2000001, 0, 0, [LiquidType.water], 1, 0, 0, 0, 1, 0);
        var spots = new Dictionary<int, FishTable.Spot>();
        if (spot) {
            spots[2000001] = spotRow;
        }
        var fish = new FishTable.Fish(10000001, LiquidType.water, 0, 6, 1, 3, 1, 54, 15000, 10000, 10000,
            new FishTable.Range<int>(20, 30), new FishTable.Range<int>(40, 50), [], 0, false);
        test.SetServerTable(new FishTable(new Dictionary<int, FishTable.Fish> { [10000001] = fish }, spots,
            new Dictionary<int, IReadOnlyDictionary<short, FishTable.Lure>>(),
            new Dictionary<int, FishTable.FishBox> { [1] = new(1, 10000, 0, new Dictionary<int, int> { [10000001] = 10 }) },
            new Dictionary<int, FishTable.FishBox>()));
        Maple2.Model.Game.Item rod = test.Store(rodData);
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        session.Fishing = new FishingManager(session, test.TableMetadata, test.ServerTableMetadata);
        typeof(FieldManager).GetField("<AccelerationStructure>k__BackingField", Any)!
            .SetValue(session.Field, new FieldAccelerationStructure());
        // The prepared state (Fishing mode 0 done): the rod, one water tile and the fishing guide.
        typeof(FishingManager).GetField("rod", Any)!.SetValue(session.Fishing, rod);
        var entity = new FieldFluidEntity(default, Tile, Vector3.Zero, 1f, LiquidType.water, default, 0, false, false, default);
        typeof(FishingManager).GetField("tiles", Any)!.SetValue(session.Fishing, new Dictionary<Vector3, FishingTile> { [Tile] = new FishingTile(entity, 0) });
        if (guide) {
            session.GuideObject = new FieldGuideObject(null!, 17, new FishingGuideObject(rodEntry, spotRow));
        }
        test.TakeSent();
    }

    private void Cast(int block = TileBlock) {
        new FishingHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(9);
            writer.WriteInt(block);
        }));
    }

    private void Catch(bool result) {
        new FishingHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(8);
            writer.WriteBool(result);
        }));
    }

    private List<byte[]> Errors() {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Fishing && packet[2] == 2).ToList();
    }

    // A cast on the prepared tile with a fish that bites opens the cast and sends no error.
    [Test]
    public void Cast_OnThePreparedTile_OpensTheCast() {
        Begin();

        Cast();

        Assert.That(session.Fishing.Casting, Is.True);
        Assert.That(Errors(), Is.Empty);
    }

    // A map with no fishing spot: the cast is refused with notexist_fish.
    [Test]
    public void Cast_NoSpot_SendsTheError() {
        Begin(spot: false);

        Cast();

        Assert.That(session.Fishing.Casting, Is.False);
        List<byte[]> errors = Errors();
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(new ByteReader(errors[0], 3).ReadShort(), Is.EqualTo((short) FishingError.s_fishing_error_notexist_fish));
    }

    // A tile that was not prepared is a system error.
    [Test]
    public void Cast_AnotherTile_SendsTheError() {
        Begin();

        Cast(4 | (3 << 8));

        Assert.That(session.Fishing.Casting, Is.False);
        List<byte[]> errors = Errors();
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(new ByteReader(errors[0], 3).ReadShort(), Is.EqualTo((short) FishingError.s_fishing_error_system_error));
    }

    // Without the fishing guide out, and with Fishing off, the cast and the catch are dropped.
    [Test]
    public void NoGuideOrFeatureOff_IsDropped() {
        Begin(guide: false);
        Cast();
        Assert.That(session.Fishing.Casting, Is.False);
        Assert.That(Errors(), Is.Empty);

        Begin();
        test.SetFeaturesOff(FishingHandler.Feature);
        Cast();
        Assert.That(session.Fishing.Casting, Is.False);
        Assert.That(Errors(), Is.Empty);
    }

    // A catch result 1 before the bite is a miss: the fish got away and the cast ends.
    [Test]
    public void Catch_AMiss_EndsTheCast() {
        Begin();
        Cast();
        typeof(FishingManager).GetField("biteTick", Any)!.SetValue(session.Fishing, long.MaxValue);
        typeof(FishingManager).GetField("fishFightGame", Any)!.SetValue(session.Fishing, false);

        Catch(true);

        Assert.That(session.Fishing.Casting, Is.False);
        Assert.That(Errors(), Is.Empty);
    }

    // With the guide put away the catch is dropped and the cast stays open.
    [Test]
    public void Catch_NoGuide_IsDropped() {
        Begin();
        Cast();
        session.GuideObject = null;

        Catch(true);

        Assert.That(session.Fishing.Casting, Is.True);
    }
}

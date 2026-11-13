using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class CubeEditRulesTests {
    [TestCase(false, null, 50100189, UgcMapError.s_ugcmap_ok)]
    [TestCase(true, null, 50100189, UgcMapError.s_ugcmap_ok)]
    [TestCase(false, 50100189, 50100189, UgcMapError.s_ugcmap_create_on_non_empty_area)]
    [TestCase(false, 50200381, 50100189, UgcMapError.s_ugcmap_create_on_non_empty_area)]
    [TestCase(true, 50100189, 50100189, UgcMapError.s_ugcmap_cant_replace_same_cube)]
    [TestCase(true, 50200381, 50100189, UgcMapError.s_ugcmap_ok)]
    public void TakenCell(bool isReplace, int? existing, int item, UgcMapError expected) {
        Assert.That(CubeEditRules.Taken(isReplace, existing, item), Is.EqualTo(expected));
    }

    // Group limits: a house (type 6, subtype 4), a trigger tool and an installed npc each count against their own
    // limit; one more is allowed only while the limit is above 0 and above the count. Ids are made up.
    private static ItemMetadata House => TestGameSession.Metadata(50100001, type: 6) with {
        Property = TestGameSession.Metadata(50100001, type: 6).Property with { SubType = 4 },
    };
    private static readonly ItemMetadata Plain = TestGameSession.Metadata(50100002, type: 6);
    private static FunctionCubeMetadata Function(InteractCubeControlType type, bool trigger = false) =>
        new(1, 0, ConfigurableCubeType.None, InteractCubeState.None, type, [], 0, null) { TriggerTool = trigger };
    private static readonly FunctionCubeMetadata Trigger = Function(InteractCubeControlType.FunctionUI, true);
    private static readonly FunctionCubeMetadata InstallNpc = Function(InteractCubeControlType.InstallNPC);

    // Height, Area, Maid, Trigger, InstallNpc, InstallBuilding: the home's (62000000 group 1) and a 1-house plot's.
    private static readonly UgcMapGroup.Limits Home = new(4, 0, 2, 3, 10, 0);
    private static readonly UgcMapGroup.Limits Outdoor = new(2, 4, 0, 0, 0, 1);

    private static (ItemMetadata, FunctionCubeMetadata?)[] Many(ItemMetadata item, FunctionCubeMetadata? function, int count) =>
        Enumerable.Repeat<(ItemMetadata, FunctionCubeMetadata?)>((item, function), count).ToArray();

    [Test]
    public void GroupLimit_Houses() {
        Assert.Multiple(() => {
            Assert.That(CubeEditRules.GroupLimit(Outdoor, House, null, []), Is.EqualTo(UgcMapError.s_ugcmap_ok));
            Assert.That(CubeEditRules.GroupLimit(Outdoor, House, null, Many(House, null, 1)), Is.EqualTo(UgcMapError.s_ugcmap_building_count));
            Assert.That(CubeEditRules.GroupLimit(Home, House, null, []), Is.EqualTo(UgcMapError.s_ugcmap_building_count));
            // Other cubes do not count, and a cube of no kind is never limited.
            Assert.That(CubeEditRules.GroupLimit(Outdoor, House, null, Many(Plain, Trigger, 5)), Is.EqualTo(UgcMapError.s_ugcmap_ok));
            Assert.That(CubeEditRules.GroupLimit(Home, Plain, null, Many(House, null, 5)), Is.EqualTo(UgcMapError.s_ugcmap_ok));
        });
    }

    [Test]
    public void GroupLimit_TriggerTools() {
        Assert.Multiple(() => {
            Assert.That(CubeEditRules.GroupLimit(Home, Plain, Trigger, Many(Plain, Trigger, 2)), Is.EqualTo(UgcMapError.s_ugcmap_ok));
            Assert.That(CubeEditRules.GroupLimit(Home, Plain, Trigger, Many(Plain, Trigger, 3)), Is.EqualTo(UgcMapError.s_ugcmap_trigger_count));
            Assert.That(CubeEditRules.GroupLimit(Outdoor, Plain, Trigger, []), Is.EqualTo(UgcMapError.s_ugcmap_trigger_count));
            // A FunctionUI cube that is not a trigger tool is not counted.
            Assert.That(CubeEditRules.GroupLimit(Outdoor, Plain, Function(InteractCubeControlType.FunctionUI), []), Is.EqualTo(UgcMapError.s_ugcmap_ok));
        });
    }

    [Test]
    public void GroupLimit_InstalledNpcs() {
        Assert.Multiple(() => {
            Assert.That(CubeEditRules.GroupLimit(Home, Plain, InstallNpc, Many(Plain, InstallNpc, 9)), Is.EqualTo(UgcMapError.s_ugcmap_ok));
            Assert.That(CubeEditRules.GroupLimit(Home, Plain, InstallNpc, Many(Plain, InstallNpc, 10)), Is.EqualTo(UgcMapError.s_ugcmap_installnpc_count));
            Assert.That(CubeEditRules.GroupLimit(Outdoor, Plain, InstallNpc, []), Is.EqualTo(UgcMapError.s_ugcmap_installnpc_count));
            Assert.That(CubeEditRules.GroupLimit(Outdoor, Plain, Function(InteractCubeControlType.SpawnNPC), []), Is.EqualTo(UgcMapError.s_ugcmap_ok));
        });
    }

    // The refusal keeps the edit's command and carries the object id twice, 10 bytes.
    [TestCase(CubePacket.Edit.Place, 10, UgcMapError.s_ugcmap_create_on_non_empty_area, 2)]
    [TestCase(CubePacket.Edit.Remove, 12, UgcMapError.s_ugcmap_no_cube_to_remove, 20)]
    [TestCase(CubePacket.Edit.Replace, 15, UgcMapError.s_ugcmap_cant_replace_same_cube, 53)]
    public void EditErrorBytes(CubePacket.Edit edit, int command, UgcMapError error, int code) {
        var reader = new ByteReader(CubePacket.EditError(edit, error, 5265895).ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(command));
        Assert.That(reader.ReadByte(), Is.EqualTo(code));
        Assert.That(reader.ReadInt(), Is.EqualTo(5265895));
        Assert.That(reader.ReadInt(), Is.EqualTo(5265895));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // A refused lift: the lift command, the error, the object id, 6 bytes.
    [Test]
    public void LiftupErrorBytes() {
        var reader = new ByteReader(CubePacket.LiftupError(UgcMapError.s_ugcmap_no_cube_to_lift, 3596716).ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(17));
        Assert.That(reader.ReadByte(), Is.EqualTo(37));
        Assert.That(reader.ReadInt(), Is.EqualTo(3596716));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

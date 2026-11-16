using System.Collections.Generic;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The quest conditions of a cube's rotation and removal in the own home, from a real KR row, through RequestCube.
public class HomeCubeConditionTests {
    // 90000660: <condition type="rotate_cube" code="50200094" /> and <condition type="uninstall_item" code="50200094" />.
    private const int CubeItem = 50200094, OtherCubeItem = 50200095;
    private const byte RemoveCube = 12, RotateCube = 14;

    private static ItemMetadata Furniture(int id) => TestGameSession.Metadata(id) with {
        Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false),
        Install = new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none),
    };

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Plot plot = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetTable(new FurnishingShopTable(new Dictionary<int, FurnishingShopTable.Entry>()));
        test.Register(Furniture(CubeItem), Furniture(OtherCubeItem));
        test.Start();
        session.Housing.InitNewHome("home", null);
        session.Player.Value.Home.SetArea(5);
        session.Player.Value.Home.SetHeight(4);
        plot = test.EnterHome();
    }

    private void Place(int itemId, Vector3B position) {
        ItemMetadata metadata = Furniture(itemId);
        long uid = session.Item.Furnishing.AddCube(new Item(metadata));
        var cube = new PlotCube(metadata, uid) { Type = PlotCube.CubeType.Default };
        Assert.That(session.Housing.TryPlaceCube(cube, plot, metadata, position, 0, out _), Is.True);
    }

    private void Send(byte command, Vector3B position) {
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.Write<Vector3B>(position);
            if (command == RotateCube) {
                writer.WriteBool(true);
            }
        }));
    }

    private Quest StartQuest() {
        return test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [
                new ConditionMetadata(ConditionType.rotate_cube, 2, new ConditionMetadata.Parameters(Integers: [CubeItem]), null),
                new ConditionMetadata(ConditionType.uninstall_item, 2, new ConditionMetadata.Parameters(Integers: [CubeItem]), null),
            ],
        });
    }

    // The rotation, then the removal, of the row's cube count; another cube's, or an empty cell's, do not.
    [Test]
    public void RotateAndRemove_90000660_TheCubeOfTheCode() {
        var mine = new Vector3B(-4, -4, 1);
        var other = new Vector3B(-3, -4, 1);
        Place(CubeItem, mine);
        Place(OtherCubeItem, other);
        Quest quest = StartQuest();

        Send(RotateCube, other);
        Send(RotateCube, new Vector3B(-2, -4, 1));
        Send(RemoveCube, other);
        Assert.That((quest.Conditions[0].Counter, quest.Conditions[1].Counter), Is.EqualTo((0, 0)));
        Send(RotateCube, mine);
        Assert.That((quest.Conditions[0].Counter, quest.Conditions[1].Counter), Is.EqualTo((1, 0)));
        Send(RemoveCube, mine);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(plot.Cubes, Is.Empty);
        Assert.That((quest.Conditions[0].Counter, quest.Conditions[1].Counter), Is.EqualTo((1, 1)));
    }
}

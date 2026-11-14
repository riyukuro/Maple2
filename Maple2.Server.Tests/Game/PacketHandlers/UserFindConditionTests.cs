using System.Numerics;
using System.Reflection;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Quest sub-type 28 (QuestHandler FindUser): the quest action used on another player of the field counts each
// user_find condition the found player meets. 80000422: <condition type="user_find"
// code="dict:{effectId:90000791, effectLevel:1}" value="1" target="dict:{level:20 100, equipItemIds:11850106}" />.
public class UserFindConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int QuestId = 80000422;
    private const int Worn = 11850106;

    private static long Find(int level, bool wears) {
        var finder = new TestGameSession();
        finder.Start();
        var found = new TestGameSession(finder.Options);
        if (wears) {
            found.Store(TestGameSession.Metadata(Worn, slot: EquipSlot.CP), group: ItemGroup.Outfit, slot: EquipSlot.CP);
        }
        found.Start();
        finder.EnterMap(TestGameSession.Map(2000001));
        FieldManager field = finder.Session.Field!;
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(found.Session.Player, 2);
        field.Players[2] = found.Session.Player;
        found.Session.Field = field;
        found.Session.Ride = new RideManager(found.Session);
        found.Session.Player.Value.Character.Level = (short) level;
        finder.Session.Player.Position = Vector3.Zero;
        found.Session.Player.Position = new Vector3(50, 0, 0);
        Quest quest = finder.StartQuest(TestGameSession.QuestData(QuestId) with {
            Conditions = [new ConditionMetadata(ConditionType.user_find, 1, new[] { "dict:{effectId:90000791", "effectLevel:1}" }.ConvertCodes(),
                new[] { "dict:{level:20 100", "equipItemIds:11850106}" }.ConvertCodes())],
        });

        new QuestHandler { TableMetadata = finder.TableMetadata, ServerTableMetadata = finder.ServerTableMetadata }
            .Handle(finder.Session, TestGameSession.Packet(writer => {
                writer.WriteByte(28);
                writer.WriteInt(QuestId);
                writer.WriteLong(found.Session.CharacterId);
            }));
        return quest.Conditions[0].Counter;
    }

    [Test]
    public void APlayerOfTheTargetCounts() {
        Assert.That(Find(20, wears: true), Is.EqualTo(1));
    }

    [TestCase(19, true)]
    [TestCase(20, false)]
    public void APlayerOutsideTheTargetDoesNot(int level, bool wears) {
        Assert.That(Find(level, wears), Is.Zero);
    }
}

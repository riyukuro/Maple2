using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// An item skill (consume useItem) is paid by one piece of the item named in the cast, and only an item whose own
// skill is the one cast pays for it (SkillCastRules.ItemMissing). Before, any owned item paid for any item skill, so a
// changed client cast a potion's skill for one piece of junk. The ids are made up.
public class ItemSkillCastTests : TriggerTestBase {
    private const int PotionSkill = 90000901, OtherSkill = 90000902;
    private const int Potion = 20000901, Junk = 20000902;

    private static SkillMetadata ItemSkill(int id) {
        SkillMetadata skill = QuestConditionWiringTests.CastSkill(id);
        // An item skill also passes its own condition (SkillCastCheck.Allowed): one that always holds.
        BeginCondition always = skill.Data.Condition with { Probability = 1f, Gender = Gender.All, PvpZone = PvpZoneApply.Everywhere };
        return skill with { Data = skill.Data with { Condition = always, Consume = skill.Data.Consume with { UseItem = true } } };
    }

    [SetUp]
    public void SetUp() {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        QuestConditionWiringTests.SetSkills(test, ItemSkill(PotionSkill), ItemSkill(OtherSkill));
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        // The test's player is built without its constructor: a refused cast logs through the actor's logger.
        typeof(Actor<Player>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(test.Session.Player, Serilog.Log.ForContext<Player>());
        ItemMetadata potionData = TestGameSession.Metadata(Potion) with { Skill = new ItemMetadataSkill(PotionSkill, 1, 0, 0) };
        test.Register(potionData, TestGameSession.Metadata(Junk));
        Assert.That(test.Session.Item.Inventory.Add(new Item(potionData, amount: 5), commit: true), Is.True);
        Assert.That(test.Session.Item.Inventory.Add(new Item(TestGameSession.Metadata(Junk), amount: 5), commit: true), Is.True);
        test.TakeSent();
    }

    // The player's cast of the skill at level 1 (command 0, a use) paid with the item uid.
    private void Cast(int skillId, long itemUid) {
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(1);
            writer.WriteInt((int) Field.FieldTick);
            writer.WriteInt(skillId);
            writer.WriteShort(1);
            writer.WriteByte(0);
            writer.Write(test.Session.Player.Position);
            writer.Write(Vector3.UnitY);
            writer.Write(Vector3.Zero);
            writer.WriteFloat(0);
            writer.WriteInt(0);
            writer.WriteBool(false);
            writer.WriteLong(itemUid);
            writer.WriteLong(0);
        }));
    }

    private long Uid(int itemId) => test.Session.Item.Inventory.Find(itemId).Single().Uid;

    private int Held(int itemId) => test.Session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    [Test]
    public void TheItemsOwnSkill_IsCastAndTakesOnePiece() {
        Cast(PotionSkill, Uid(Potion));
        Assert.Multiple(() => {
            Assert.That(Held(Potion), Is.EqualTo(4));
            Assert.That(test.Session.Player.ActiveSkills.Get(1), Is.Not.Null, "the cast runs");
        });
    }

    [Test]
    public void AnItemWithNoSkill_PaysForNothing() {
        Cast(PotionSkill, Uid(Junk));
        Assert.Multiple(() => {
            Assert.That((Held(Junk), Held(Potion)), Is.EqualTo((5, 5)));
            Assert.That(test.Session.Player.ActiveSkills.Get(1), Is.Null, "no cast runs");
        });
    }

    [Test]
    public void AnotherItemSkillsItem_PaysForNothing() {
        Cast(OtherSkill, Uid(Potion));
        Assert.Multiple(() => {
            Assert.That((Held(Junk), Held(Potion)), Is.EqualTo((5, 5)));
            Assert.That(test.Session.Player.ActiveSkills.Get(1), Is.Null, "no cast runs");
        });
    }
}

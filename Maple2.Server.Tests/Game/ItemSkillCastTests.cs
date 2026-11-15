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
        // The pack's condition of a KR potion: no allowMapleSurvival, which the client does not read.
        BeginCondition always = skill.Data.Condition with { Probability = 1f, Gender = Gender.All, PvpZone = PvpZoneApply.Everywhere, AllowOnSurvival = false };
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
    private void Cast(int skillId, long itemUid, long castId = 1) {
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(castId);
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

    // The client tests none of the pack's beginCondition fields that only the server's condition check reads
    // (allowMapleSurvival, level, gender and the rest; SkillCondition::Load reads none of them), so a potion is cast in
    // a Maple Survival field (KR 82000000-82000003, map type 15) and by a player under the pack's level.
    [TestCase(MapType.SurvivalSolo, (short) 0)]
    [TestCase(MapType.SurvivalTeam, (short) 0)]
    [TestCase(MapType.None, (short) 99)]
    public void ThePacksOwnCondition_DoesNotStopThePotion(MapType type, short level) {
        MapMetadata map = TestGameSession.Map(2000001);
        test.EnterMap(map with {
            Property = map.Property with { Type = type },
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true),
        });
        SkillMetadata potion = ItemSkill(PotionSkill);
        QuestConditionWiringTests.SetSkills(test, potion with { Data = potion.Data with { Condition = potion.Data.Condition with { Level = level } } });
        test.TakeSent();

        Cast(PotionSkill, Uid(Potion));

        Assert.Multiple(() => {
            Assert.That(Held(Potion), Is.EqualTo(4));
            Assert.That(test.Session.Player.ActiveSkills.Get(1), Is.Not.Null, "the cast runs");
        });
    }

    // A second use on cooldown: refused for a cooldown shown in the UI; taken for one that is not, which the client
    // casts on cooldown when a held quickslot key makes it a skill state's next record (SkillCastCheck.Allowed).
    [TestCase(true, 4)]
    [TestCase(false, 3)]
    public void ASecondUseOnCooldown_IsTakenOnlyWhenTheCooldownIsNotShown(bool shown, int left) {
        SkillMetadata potion = ItemSkill(PotionSkill);
        QuestConditionWiringTests.SetSkills(test, potion with { Data = potion.Data with { Condition = potion.Data.Condition with { CooldownTime = 5f, CooldownShownInUi = shown } } });
        Cast(PotionSkill, Uid(Potion), castId: 1);
        Cast(PotionSkill, Uid(Potion), castId: 2);
        Assert.That(Held(Potion), Is.EqualTo(left));
    }

    // The ingest reads useCoolDownShowUI from the played client's <basic><condition>, 0 when not given (made-up rows),
    // for a skill it has (Read) and for a level only it has (BuildLevel, kept by WithPackLevel).
    [Test]
    public void TheIngestReadsTheCooldownUiFlagFromThePlayedClient() {
        var document = new System.Xml.XmlDocument();
        document.LoadXml("""
            <ms2>
              <skill id="100"><basic><condition useCoolDownShowUI="1" /></basic><level value="1" cooldown="5000" /></skill>
              <skill id="101"><basic><condition /></basic><level value="1" cooldown="3000" /></skill>
            </ms2>
            """);
        var skills = document.SelectNodes("ms2/skill")!.OfType<System.Xml.XmlElement>().ToArray();
        System.Collections.Generic.Dictionary<int, PlayedClientSkills.Entry> read = PlayedClientSkills.Read([document]);
        SkillMetadataLevel pack = ItemSkill(PotionSkill).Data;
        Assert.Multiple(() => {
            Assert.That((read[100].CooldownShownInUi, read[101].CooldownShownInUi), Is.EqualTo((true, false)));
            Assert.That(skills.Select(skill => PlayedClientSkills.WithPackLevel(PlayedClientSkills.BuildLevel(skill, (System.Xml.XmlElement) skill.SelectSingleNode("level")!), pack).Condition.CooldownShownInUi),
                Is.EqualTo(new[] { true, false }));
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

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// A shard's skill is cast only while a worn shard gives it (JobSystemCli::vfunc_2, SkillCastRules.ShardSourceRefuses):
// at most at the highest worn grade, and not with feature Lapenshard01 off. A cast that names a bag item giving the
// skill is the client's inventory path and runs. The ids are made up; the shard has an itemLapenshardUpgrade row.
public class LapenshardSkillSourceTests : TriggerTestBase {
    private const int ShardSkill = 90000921;
    private const int Shard = 41000921, Potion = 20000921;

    private ItemMetadata potion = null!;

    private void Enter() {
        Start();
        MapMetadata map = TestGameSession.Map(2000001);
        test.EnterMap(map with {
            Property = map.Property with { Type = MapType.Dungeon },
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true),
        });
        SkillMetadata skill = QuestConditionWiringTests.CastSkill(ShardSkill);
        BeginCondition always = skill.Data.Condition with { Probability = 1f, Gender = Gender.All, PvpZone = PvpZoneApply.Everywhere };
        skill = skill with { Property = skill.Property with { MaxLevel = 2 }, Data = skill.Data with { Condition = always } };
        QuestConditionWiringTests.SetSkills(test, skill, skill with { Level = 2 });
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        potion = TestGameSession.Metadata(Potion) with { Skill = new ItemMetadataSkill(ShardSkill, 1, 0, 0) };
        test.Register(TestGameSession.Metadata(Shard) with { Skill = new ItemMetadataSkill(ShardSkill, 1, 0, 0) }, potion);
        test.SetTable(new LapenshardUpgradeTable(new Dictionary<int, LapenshardUpgradeTable.Entry> {
            [Shard] = new(1, Shard, 0, 1, [], 0),
        }));
        test.TakeSent();
    }

    private void Cast(short level, long itemUid = 0) {
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(1);
            writer.WriteInt((int) Field.FieldTick);
            writer.WriteInt(ShardSkill);
            writer.WriteShort(level);
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

    private bool Ran() => test.Session.Player.ActiveSkills.Get(1) != null;

    [Test]
    public void NoShardWorn_IsRefused() {
        Enter();
        Cast(1);
        Assert.That(Ran(), Is.False);
    }

    [Test]
    public void WornShard_Runs() {
        Enter();
        test.Session.Config.SetLapenshard(LapenshardSlot.Red1, Shard);
        Cast(1);
        Assert.That(Ran(), Is.True);
    }

    [Test]
    public void AboveTheWornGrade_IsRefused() {
        Enter();
        test.Session.Config.SetLapenshard(LapenshardSlot.Red1, Shard);
        Cast(2);
        Assert.That(Ran(), Is.False);
    }

    [Test]
    public void FeatureOff_IsRefused() {
        Enter();
        test.Session.Config.SetLapenshard(LapenshardSlot.Red1, Shard);
        test.SetFeaturesOff(SkillCastRules.LapenshardFeature);
        Cast(1);
        Assert.That(Ran(), Is.False);
    }

    [Test]
    public void BagItemOfTheSkill_Runs() {
        Enter();
        Assert.That(test.Session.Item.Inventory.Add(new Item(potion, amount: 1), commit: true), Is.True);
        Cast(1, test.Session.Item.Inventory.Find(Potion).Single().Uid);
        Assert.That(Ran(), Is.True);
    }
}

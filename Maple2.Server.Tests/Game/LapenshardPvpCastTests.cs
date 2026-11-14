using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// A skill a worn shard gives is not cast on a PvP field (the client's cast check answer 0x1b,
// SkillCastRules.LapenshardFieldRefuses). The skill here is an item skill so that a cast that runs shows in the
// item count; the ids are made up.
public class LapenshardPvpCastTests : TriggerTestBase {
    private const int ShardSkill = 90000911;
    private const int Potion = 20000911, Shard = 41000911;

    private void Enter(MapType type) {
        Start();
        MapMetadata map = TestGameSession.Map(2000001);
        test.EnterMap(map with {
            Property = map.Property with { Type = type },
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true),
        });
        SkillMetadata skill = QuestConditionWiringTests.CastSkill(ShardSkill);
        BeginCondition always = skill.Data.Condition with { Probability = 1f, Gender = Gender.All, PvpZone = PvpZoneApply.Everywhere };
        QuestConditionWiringTests.SetSkills(test, skill with { Data = skill.Data with { Condition = always, Consume = skill.Data.Consume with { UseItem = true } } });
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        typeof(Actor<Player>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(test.Session.Player, Serilog.Log.ForContext<Player>());
        ItemMetadata potion = TestGameSession.Metadata(Potion) with { Skill = new ItemMetadataSkill(ShardSkill, 1, 0, 0) };
        test.Register(potion, TestGameSession.Metadata(Shard) with { Skill = new ItemMetadataSkill(ShardSkill, 0, 0, 0) });
        Assert.That(test.Session.Item.Inventory.Add(new Item(potion, amount: 5), commit: true), Is.True);
        test.TakeSent();
    }

    private void Cast() {
        long uid = test.Session.Item.Inventory.Find(Potion).Single().Uid;
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(1);
            writer.WriteInt((int) Field.FieldTick);
            writer.WriteInt(ShardSkill);
            writer.WriteShort(1);
            writer.WriteByte(0);
            writer.Write(test.Session.Player.Position);
            writer.Write(Vector3.UnitY);
            writer.Write(Vector3.Zero);
            writer.WriteFloat(0);
            writer.WriteInt(0);
            writer.WriteBool(false);
            writer.WriteLong(uid);
            writer.WriteLong(0);
        }));
    }

    private int Held() => test.Session.Item.Inventory.Find(Potion).Sum(item => item.Amount);

    [Test]
    public void NoShardWorn_TheCastRunsOnAPvpField() {
        Enter(MapType.Pvp);
        Cast();
        Assert.That(Held(), Is.EqualTo(4));
    }

    [Test]
    public void WornShardSkill_IsRefusedOnAPvpField() {
        Enter(MapType.Pvp);
        test.Session.Config.SetLapenshard(LapenshardSlot.Red1, Shard);
        Cast();
        Assert.Multiple(() => {
            Assert.That(Held(), Is.EqualTo(5));
            Assert.That(test.Session.Player.ActiveSkills.Get(1), Is.Null);
        });
    }

    [Test]
    public void WornShardSkill_RunsElsewhere() {
        Enter(MapType.Dungeon);
        test.Session.Config.SetLapenshard(LapenshardSlot.Red1, Shard);
        Cast();
        Assert.That(Held(), Is.EqualTo(4));
    }
}

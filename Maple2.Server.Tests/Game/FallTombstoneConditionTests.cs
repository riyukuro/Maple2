using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The quest conditions of a fall and of a hit on another player's tombstone, from real KR rows, through the real code.
public class FallTombstoneConditionTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private Quest StartQuest(params ConditionMetadata[] conditions) {
        return test.StartQuest(TestGameSession.QuestData(91000001) with { Conditions = conditions });
    }

    private static void FullHealth(FieldPlayer player, long hp) {
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, player.Session.Field);
        typeof(FieldPlayer).GetField("healthSync", Any)!.SetValue(player, new HealthSyncClock());
        typeof(FieldPlayer).GetField("regenClock", Any)!.SetValue(player, new RegenClock());
        typeof(Actor<Player>).GetField("DamageDealers", Any)!.SetValue(player, new ConcurrentDictionary<int, DamageRecordTarget>());
        Stat health = player.Stats.Values[BasicAttribute.Health];
        health.AddTotal(hp - health.Total);
        health.Add(hp - health.Current);
    }

    // 23100012: <condition type="fall_damage" target="500" value="10" />, a fall of 500 damage or more counts;
    // 23100013: <condition type="fall_survive" value="10" />, every fall the player lives through counts.
    [Test]
    public void Fall_23100012_23100013_TheDamageAndTheSurvival() {
        Start();
        test.EnterMap(TestGameSession.Map(2000001));
        FullHealth(test.Session.Player, 10000);
        Quest quest = StartQuest(
            new ConditionMetadata(ConditionType.fall_damage, 10, null, new ConditionMetadata.Parameters(Integers: [500])),
            new ConditionMetadata(ConditionType.fall_survive, 10, null, null));

        // 10000 hp from 0 cm: 481 damage; from 100 cm, about 570 of the 9519 left.
        test.Session.Player.FallDamage(0);
        Assert.That(quest.Conditions.Values.Select(condition => condition.Counter), Is.EqualTo(new[] { 0, 1 }));
        test.Session.Player.FallDamage(100);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(test.Session.Player.IsDead, Is.False);
        Assert.That(quest.Conditions.Values.Select(condition => condition.Counter), Is.EqualTo(new[] { 1, 2 }));
    }

    // 70300055: <condition type="hit_tombstone" value="1" />: a hit on another player's tombstone counts for the hitter;
    // a hit on the own tombstone, or a second packet count, does not.
    [Test]
    public void HitTombstone_70300055_AHitOnAnothersTombstone() {
        Start();
        TestGameSession other = SecondPlayer();
        // Five hits for a first death (constants hitPerDeadCount).
        other.SetServerTable(other.ServerTableMetadata.ConstantsTable with { hitPerDeadCount = 5, maxDeadCount = 1 });
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { hitPerDeadCount = 5, maxDeadCount = 1 });
        other.Session.Player.Tombstone = new Tombstone(other.Session.Player, 0);
        test.Session.Player.Tombstone = new Tombstone(test.Session.Player, 0);
        Quest quest = StartQuest(new ConditionMetadata(ConditionType.hit_tombstone, 3, null, null));
        var handler = new TombstoneHandler();
        void Hit(int objectId, int hits) => handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteInt(objectId);
            writer.WriteInt(hits);
        }));

        Hit(test.Session.Player.ObjectId, 1);
        Hit(other.Session.Player.ObjectId, 2);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Hit(other.Session.Player.ObjectId, 1);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(other.Session.Player.Tombstone!.HitsRemaining, Is.EqualTo(4));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
}

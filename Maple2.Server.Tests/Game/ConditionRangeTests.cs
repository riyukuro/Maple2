using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The conditions that name a range (survive_map, emotion, spawner, fall_damage), and the collect conditions that
// count an item once. Made-up ids.
public class ConditionRangeTests {
    private static ConditionMetadata.Parameters Range(int min, int max) =>
        new(Range: new ConditionMetadata.Range<int>(min, max));

    private static ConditionMetadata Code(ConditionType type, ConditionMetadata.Parameters code) => new(type, 1, code, null, 0, 0);

    private static ConditionMetadata Target(ConditionType type, ConditionMetadata.Parameters target) => new(type, 1, null, target, 0, 0);

    [Test]
    public void SurviveMap_CodeRange() {
        GameSession session = new TestGameSession().Start();
        ConditionMetadata condition = Code(ConditionType.survive_map, Range(100, 200));
        Assert.Multiple(() => {
            Assert.That(condition.Check(session, codeLong: 150), Is.True);
            Assert.That(condition.Check(session, codeLong: 201), Is.False);
        });
    }

    // emotion: the target range is the map the player stands on.
    [TestCase(2000010, true)]
    [TestCase(2000100, false)]
    public void Emotion_TargetRangeIsThePlayersMap(int mapId, bool expected) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Player.Value.Character.MapId = mapId;
        Assert.That(Target(ConditionType.emotion, Range(2000000, 2000050)).Check(session), Is.EqualTo(expected));
    }

    [TestCase(ConditionType.spawner)]
    [TestCase(ConditionType.fall_damage)]
    public void SpawnerAndFallDamage_TargetRange(ConditionType type) {
        GameSession session = new TestGameSession().Start();
        ConditionMetadata condition = Target(type, Range(10, 20));
        Assert.Multiple(() => {
            Assert.That(condition.Check(session, targetLong: 15), Is.True);
            Assert.That(condition.Check(session, targetLong: 25), Is.False);
        });
    }

    // item_collect counts an item the first time only: a repeat of the same item does not count.
    [TestCase(ConditionType.item_collect)]
    [TestCase(ConditionType.item_collect_revise)]
    public void ItemCollect_CountsAnItemOnce(ConditionType type) {
        GameSession session = new TestGameSession().Start();
        ConditionMetadata condition = Code(type, Range(30000000, 30000100));

        Assert.Multiple(() => {
            Assert.That(condition.Check(session, codeLong: 30000005), Is.True);
            Assert.That(condition.Check(session, codeLong: 30000005), Is.False);
            Assert.That(condition.Check(session, codeLong: 30000006), Is.True);
            Assert.That(condition.Check(session, codeLong: 40000000), Is.False);
        });
    }
}

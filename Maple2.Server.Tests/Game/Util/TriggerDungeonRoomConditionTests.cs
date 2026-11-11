using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using TriggerConditions = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// dungeon_id, dungeon_level, dungeon_max_user_count and dungeon_round read the field's DungeonRoom.xml row. Made-up ids.
public class TriggerDungeonRoomConditionTests {
    private static ICondition Condition(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        XmlElement node = document.DocumentElement!;
        return TriggerFunctionMapping.ConditionMap[node.GetAttribute("name")](node.Attributes);
    }

    [Test]
    public void DungeonId_MatchesTheRoomTheFieldWasMadeFor() {
        Assert.That(Condition("""<condition name="dungeon_id" dungeon_id="90010003" />"""), Is.TypeOf<TriggerConditions.DungeonId>());
        // Two rooms share the boss map; each picks its own boss.
        Assert.That(TriggerContext.DungeonIdMatches(90010003, 90010003), Is.True);
        Assert.That(TriggerContext.DungeonIdMatches(90010003, 90011003), Is.False);
        // A field that is no dungeon room has id 0.
        Assert.That(TriggerContext.DungeonIdMatches(0, 90010003), Is.False);
    }

    [Test]
    public void DungeonLevel_IsTheRaidGroupType() {
        Assert.That(Condition("""<condition name="dungeon_level" level="2" />"""), Is.TypeOf<TriggerConditions.DungeonLevel>());
        Assert.That(TriggerContext.DungeonLevelMatches(DungeonGroupType.raid, 2), Is.True);
        Assert.That(TriggerContext.DungeonLevelMatches(DungeonGroupType.raid, 3), Is.False);
        Assert.That(TriggerContext.DungeonLevelMatches(DungeonGroupType.chaosRaid, 3), Is.True);
        Assert.That(TriggerContext.DungeonLevelMatches(DungeonGroupType.chaosRaid, 2), Is.False);
        // No raid room (or no dungeon room): the old answer, chaos.
        Assert.That(TriggerContext.DungeonLevelMatches(DungeonGroupType.normal, 2), Is.False);
        Assert.That(TriggerContext.DungeonLevelMatches(DungeonGroupType.normal, 3), Is.True);
        Assert.That(TriggerContext.DungeonLevelMatches(null, 3), Is.True);
    }

    [Test]
    public void DungeonMaxUserCount_IsTheClientRoomMaximum() {
        Assert.That(Condition("""<condition name="dungeon_max_user_count" value="4" />"""), Is.TypeOf<TriggerConditions.DungeonMaxUserCount>());
        // A fixed room: the table maximum, whatever the party.
        Assert.That(TriggerContext.RoomMaxUserCount(false, 4, 1), Is.EqualTo(4));
        Assert.That(TriggerContext.RoomMaxUserCount(false, 1, 3), Is.EqualTo(1));
        // isChangeMaxUser: the party size held between 2 and the maximum.
        Assert.That(TriggerContext.RoomMaxUserCount(true, 4, 3), Is.EqualTo(3));
        Assert.That(TriggerContext.RoomMaxUserCount(true, 4, 1), Is.EqualTo(2));
        Assert.That(TriggerContext.RoomMaxUserCount(true, 4, 6), Is.EqualTo(4));
        Assert.That(TriggerContext.DungeonMaxUserCountMatches(3, 3), Is.True);
        Assert.That(TriggerContext.DungeonMaxUserCountMatches(3, 1), Is.False);
        // A 10 user room: no script tests 10, so the old answer (1) keeps checkusercount going.
        Assert.That(TriggerContext.DungeonMaxUserCountMatches(10, 4), Is.False);
        Assert.That(TriggerContext.DungeonMaxUserCountMatches(10, 1), Is.True);
    }

    [Test]
    public void DungeonRound_PassesWhenTheGearScoreMeetsTheRound() {
        Assert.That(Condition("""<condition name="dungeon_round" round="2" />"""), Is.TypeOf<TriggerConditions.DungeonRound>());
        DungeonRoundMetadata[] rounds = [new(RewardId: 1, GearScore: 100), new(RewardId: 2, GearScore: 200)];
        // Round N is entry N-1.
        Assert.That(TriggerContext.DungeonRoundMet(rounds, 1, 100), Is.True);
        Assert.That(TriggerContext.DungeonRoundMet(rounds, 2, 199), Is.False);
        Assert.That(TriggerContext.DungeonRoundMet(rounds, 2, 200), Is.True);
        // A round the group does not list fails, as the old stub did.
        Assert.That(TriggerContext.DungeonRoundMet(rounds, 3, 1000), Is.False);
        Assert.That(TriggerContext.DungeonRoundMet(rounds, 0, 1000), Is.False);
    }
}

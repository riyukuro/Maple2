using System.Xml;
using Maple2.Model.Enum;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using TriggerConditions = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// dungeon_id, dungeon_level and dungeon_max_user_count read the field's DungeonRoom.xml row. Made-up ids.
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
}

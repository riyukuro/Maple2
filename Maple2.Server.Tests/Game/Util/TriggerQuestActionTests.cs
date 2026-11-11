using System.Xml;
using Maple2.Model.Enum;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using TriggerActions = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// set_quest_accept starts the quest past its requirements; set_quest_complete finishes a started one. Made-up ids.
public class TriggerQuestActionTests {
    private static IAction Action(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        XmlElement node = document.DocumentElement!;
        return TriggerFunctionMapping.ActionMap[node.GetAttribute("name")](node.Attributes);
    }

    [Test]
    public void QuestActions_ParseAndCompleteOnlyStartedQuests() {
        Assert.That(Action("""<action name="set_quest_accept" quest_id="10000001" />"""), Is.TypeOf<TriggerActions.SetQuestAccept>());
        Assert.That(Action("""<action name="set_quest_complete" quest_id="10000001" />"""), Is.TypeOf<TriggerActions.SetQuestComplete>());
        Assert.That(TriggerContext.CanForceCompleteQuest(QuestState.Started), Is.True);
        Assert.That(TriggerContext.CanForceCompleteQuest(QuestState.Completed), Is.False);
        Assert.That(TriggerContext.CanForceCompleteQuest(QuestState.None), Is.False);
    }
}

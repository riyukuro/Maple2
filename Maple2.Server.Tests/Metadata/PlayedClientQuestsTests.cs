using System.Linq;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Metadata;

// MS2_PLAYED_CLIENT_XML: quests the played client's quest table lacks are marked, and the server neither starts,
// offers nor sends them.
public class PlayedClientQuestsTests {
    // The filter the ingest passes: here feature Off is not enabled.
    private static bool Available(XmlElement node) => node.GetAttribute("feature") != "Off";

    [Test]
    public void Ids_AreTheQuestsWithAnEnabledFeature() {
        var table = new XmlDocument();
        table.LoadXml("""
            <ms2>
              <quest id="100"><basic /></quest>
              <quest id="101" feature="Off"><basic /></quest>
              <quest id="102" feature="On" />
              <quest id="0" />
              <quest id="x" />
              <other id="103" />
            </ms2>
            """);

        Assert.That(PlayedClientQuests.Ids(table, Available).OrderBy(id => id), Is.EqualTo(new[] { 100, 102 }));
    }

    // The client keeps a quest without testing a locale (unlike item environments).
    [Test]
    public void Ids_IgnoreTheLocale() {
        var table = new XmlDocument();
        table.LoadXml("""<ms2><quest id="100" locale="KR" /></ms2>""");

        Assert.That(PlayedClientQuests.Ids(table, node => node.GetAttribute("feature") == ""), Is.EqualTo(new[] { 100 }));
    }

    private static QuestMetadataBasic Basic(bool notInPlayedClient) =>
        new(ChapterId: 0, Type: QuestType.EpicQuest, Account: 0, StandardLevel: 1, Forfeitable: true, EventTag: string.Empty,
            AutoStart: false, Disabled: false, UsePostbox: false, StartNpc: 0, CompleteNpc: 0, CompleteMaps: null, ProgressMaps: null,
            NotInPlayedClient: notInPlayedClient);

    // A stored quest with one condition; the parts of the metadata the update does not read are left out.
    private static Quest Stored(int id, bool notInPlayedClient, QuestState state, ConditionType type) {
        var condition = new ConditionMetadata(type, 3, null, null);
        var metadata = new QuestMetadata(id, null, Basic(notInPlayedClient), null!, null!, null!, null!, null!, null!, null!,
            null, null, null, QuestEventMissionType.none, [condition]);
        var quest = new Quest(metadata) { State = state };
        quest.Conditions.Add(0, new Quest.Condition(condition));
        return quest;
    }

    // A stored quest the played client lacks gets no condition update (the client never loaded it); the others
    // still do when started and with a condition of the type.
    [Test]
    public void UpdateCandidates_LeaveOutQuestsThePlayedClientLacks() {
        Quest[] quests = [
            Stored(1, false, QuestState.Started, ConditionType.npc),
            Stored(2, true, QuestState.Started, ConditionType.npc),
            Stored(3, false, QuestState.Completed, ConditionType.npc),
            Stored(4, false, QuestState.Started, ConditionType.item_collect),
        ];

        Assert.That(QuestRules.UpdateCandidates(quests, ConditionType.npc).Select(quest => quest.Id), Is.EqualTo(new[] { 1 }));
    }

    // Without the mark (no played client given, or data ingested before the mark) every quest is known.
    [Test]
    public void Known_NotAQuestThePlayedClientLacks() {
        var basic = new QuestMetadataBasic(ChapterId: 0, Type: QuestType.EpicQuest, Account: 0, StandardLevel: 1, Forfeitable: true,
            EventTag: string.Empty, AutoStart: true, Disabled: false, UsePostbox: false, StartNpc: 0, CompleteNpc: 0,
            CompleteMaps: null, ProgressMaps: null);

        Assert.Multiple(() => {
            Assert.That(PlayedClientQuests.Known(basic), Is.True);
            Assert.That(PlayedClientQuests.Known(basic with { NotInPlayedClient = true }), Is.False);
        });
    }
}

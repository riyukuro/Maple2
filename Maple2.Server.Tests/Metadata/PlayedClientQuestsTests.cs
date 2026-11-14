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

    // The requires the parser does not read: guildLevel only with the attribute (0 is a require), vip and the
    // equipment list; a quest without any of them has no row.
    [Test]
    public void Requires_AreTheGuildLevelVipAndEquipment() {
        var table = new XmlDocument();
        table.LoadXml("""
            <ms2>
              <quest id="90000720"><require level="17" guildLevel="0" /></quest>
              <quest id="70700061"><require maxLevel="99" vip="1" /></quest>
              <quest id="95000001"><require level="10" equipment="11850151, 11850152" /></quest>
              <quest id="95000002" feature="Off"><require equipment="1" /></quest>
              <quest id="100"><require level="1" /></quest>
            </ms2>
            """);

        var requires = PlayedClientQuests.Requires(table, Available);

        Assert.Multiple(() => {
            Assert.That(requires.Keys.OrderBy(id => id), Is.EqualTo(new[] { 70700061, 90000720, 95000001 }));
            Assert.That((requires[90000720].GuildLevel, requires[90000720].Vip), Is.EqualTo((0, false)));
            Assert.That((requires[70700061].GuildLevel, requires[70700061].Vip), Is.EqualTo((-1, true)));
            Assert.That(requires[95000001].Equipment, Is.EqualTo(new[] { 11850151, 11850152 }));
        });
    }

    // The start and complete npcs of the enabled quests, 0 for a missing element or attribute (the played client moved
    // the completion of 10003408 from 11004779 to 11000074, which the server's quest files still name).
    [Test]
    public void Npcs_AreTheStartAndCompleteNpcOfTheEnabledQuests() {
        var table = new XmlDocument();
        table.LoadXml("""
            <ms2>
              <quest id="10003408"><start npc="11004778" /><complete npc="11000074" /></quest>
              <quest id="101" feature="Off"><start npc="1" /><complete npc="2" /></quest>
              <quest id="102"><complete npc="5" /></quest>
              <quest id="103"><start /></quest>
            </ms2>
            """);

        var npcs = PlayedClientQuests.Npcs(table, Available);

        Assert.That(npcs.Keys.OrderBy(id => id), Is.EqualTo(new[] { 102, 103, 10003408 }));
        Assert.That(npcs[10003408], Is.EqualTo((11004778, 11000074)));
        Assert.That(npcs[102], Is.EqualTo((0, 5)));
        Assert.That(npcs[103], Is.EqualTo((0, 0)));
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

    // The ingest adds the quests of the played client's table the data pack lacks, from the played client's own quest
    // files (KR Live 70000025, the level 70 guide quest the client lists at every field entry); a pack quest stays the
    // pack's, and an id outside the table is not added.
    [Test]
    public void Ingest_AddsThePlayedQuestsThePackLacks() {
        var pack = new[] { (10003408, "pack"), (100, "pack") };
        var played = new[] { (10003408, "played"), (70000025, "played"), (5, "played") };
        int playedOnly = 0;

        var quests = Maple2.File.Ingest.Mapper.QuestMapper.PackThenPlayed(pack, () => played, quest => quest.Item1,
            new System.Collections.Generic.HashSet<int> { 10003408, 100, 70000025 }, () => playedOnly++).ToList();

        Assert.Multiple(() => {
            Assert.That(quests, Is.EqualTo(new[] { (10003408, "pack"), (100, "pack"), (70000025, "played") }));
            Assert.That(playedOnly, Is.EqualTo(1));
            Assert.That(Maple2.File.Ingest.Mapper.QuestMapper.PackThenPlayed(pack, () => played, quest => quest.Item1, null),
                Is.EqualTo(pack));
        });
    }
}

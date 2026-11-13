using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game.Manager;

// CanEnterSkyFortress (questmanager_2.cpp:1723), the server's part of the sky fortress moves (Quest 0x0E and 0x0F):
// the quest of famedef.xml's sky fortress season is rewarded, and the player is not on the target field. Made-up ids.
public class SkyFortressTests {
    private const int RequireQuest = 90000013;
    private const int Target = 2000422;

    private TestGameSession test = null!;

    private void Login(FameTable fame, int mapId = 2000001) {
        test = new TestGameSession();
        test.SetTable(fame);
        test.Start();
        test.EnterMap(TestGameSession.Map(mapId));
        test.TakeSent();
    }

    private static FameTable Seasons(int skyFortressQuest) =>
        new([], new Dictionary<int, AllianceType>(), SeasonRequireQuests: new Dictionary<int, int> { [1] = skyFortressQuest, [2] = 50101050 });

    private void Hold(int questId, QuestState state) {
        test.StartQuest(TestGameSession.QuestData(questId)).State = state;
    }

    [TestCase(QuestState.Completed, true)]
    [TestCase(QuestState.Started, false)]
    public void RequireQuestRewarded(QuestState state, bool enter) {
        Login(Seasons(RequireQuest));
        Hold(RequireQuest, state);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.EqualTo(enter));
    }

    [Test]
    public void RequireQuestNotHeld() {
        Login(Seasons(RequireQuest));
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.False);
    }

    // The season's quest decides, not the constant (91000013, the same quest in KR).
    [Test]
    public void SeasonQuestNotConstant() {
        Login(Seasons(RequireQuest));
        Hold(91000013, QuestState.Completed);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.False);
    }

    // A table stored before the seasons were read: the constant's quest.
    [Test]
    public void OldTableUsesConstant() {
        Login(new FameTable([], new Dictionary<int, AllianceType>()));
        Hold(test.ServerTableMetadata.ConstantsTable.FameContentsRequireQuestID, QuestState.Completed);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.True);
    }

    // A table whose seasons lack the sky fortress: quest 0, never rewarded.
    [Test]
    public void NoSkyFortressSeason() {
        Login(new FameTable([], new Dictionary<int, AllianceType>(), SeasonRequireQuests: new Dictionary<int, int>()));
        Hold(test.ServerTableMetadata.ConstantsTable.FameContentsRequireQuestID, QuestState.Completed);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.False);
    }

    [Test]
    public void AlreadyOnTheTarget() {
        Login(Seasons(RequireQuest), Target);
        Hold(RequireQuest, QuestState.Completed);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.False);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target + 1), Is.True);
    }

    // famedef.xml's seasons by name (any case), of the features that are on; a later season of the same name wins.
    [Test]
    public void ReadFame_Seasons() {
        var empty = new XmlDocument();
        empty.LoadXml("<ms2 />");
        var fameDef = new XmlDocument();
        fameDef.LoadXml("""
            <ms2>
              <season name="SkyFotress" requireQuest="91000013" requireLv="50" />
              <season name="KritiasExped" requireQuest="50101050" requireLv="70" feature="Off" />
              <season name="georg" requireQuest="91001010" />
              <season name="Georg" requireQuest="91001011" />
            </ms2>
            """);
        FameTable table = TableMapper.ReadFame(empty, fameDef, "KR", feature => feature != "Off");
        Assert.Multiple(() => {
            Assert.That(table.SeasonRequireQuest(1), Is.EqualTo(91000013));
            Assert.That(table.SeasonRequireQuest(2), Is.Zero);
            Assert.That(table.SeasonRequireQuest(3), Is.EqualTo(91001011));
        });
    }
}

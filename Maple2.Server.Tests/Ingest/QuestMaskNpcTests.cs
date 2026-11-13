using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Ingest;

// questmasknpc.xml as CQuestMaskTable::LoadFile reads it, the client's mask test (vfunc_o10_23 over FUN_140764480)
// and the partner spawn of an unplaced masked npc. The 02000065 rows and Karl's point are the KR data's; the
// other rows are made up.
[NonParallelizable]
public class QuestMaskNpcTests {
    private Dictionary<string, int> oldFeatures = null!;
    private string oldLocale = null!;

    [SetUp]
    public void SetFilter() {
        oldFeatures = FeatureLocaleFilter.Features;
        oldLocale = FeatureLocaleFilter.Locale;
        FeatureLocaleFilter.Locale = "KR";
        FeatureLocaleFilter.Features = new Dictionary<string, int> { ["TestMask01"] = 1 };
    }

    [TearDown]
    public void RestoreFilter() {
        FeatureLocaleFilter.Features = oldFeatures;
        FeatureLocaleFilter.Locale = oldLocale;
    }

    private static QuestMaskNpcTable Read() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <field fieldID="02000065">
                <key id="11000074" enableInMask="0">
                  <mask startQuestID="10003407" startQuestState="2" endQuestID="10003418" endQuestState="2" />
                </key>
                <key id="11004779" enableInMask="1">
                  <mask startQuestID="10003407" startQuestState="2" endQuestID="10003418" endQuestState="2" />
                </key>
              </field>
              <field fieldID="02000001">
                <key id="11000001" enableInMask="0">
                  <mask startQuestID="100" startQuestState="2" endQuestID="101" endQuestState="2" />
                </key>
                <key id="11000002">
                  <mask startQuestID="200" startQuestState="1" endQuestID="201" endQuestState="1" />
                  <mask startQuestID="300" startQuestState="1" endQuestID="301" endQuestState="1" feature="TestMaskOff" />
                </key>
                <key id="11000003" enableInMask="1">
                  <mask startQuestID="0" startQuestState="2" endQuestID="101" endQuestState="2" />
                </key>
                <key id="0" enableInMask="1">
                  <mask startQuestID="100" startQuestState="2" endQuestID="101" endQuestState="2" />
                </key>
              </field>
              <field fieldID="0">
                <key id="11000004" enableInMask="1">
                  <mask startQuestID="100" startQuestState="2" endQuestID="101" endQuestState="2" />
                </key>
              </field>
              <field fieldID="02000001">
                <key id="11000001" enableInMask="1">
                  <mask startQuestID="400" startQuestState="3" endQuestID="401" endQuestState="2" feature="TestMask01" />
                </key>
              </field>
            </ms2>
            """);
        return TableMapper.QuestMaskNpcs(document);
    }

    [Test]
    public void ReadsTheMasksTheClientKeeps() {
        QuestMaskNpcTable table = Read();

        Assert.That(table.Fields.Keys, Is.EquivalentTo(new[] { 2000065, 2000001 }));
        Assert.That(table.Fields[2000065][11004779], Is.EqualTo(new QuestMaskNpcTable.Mask(true, table.Fields[2000065][11004779].Ranges)));
        Assert.That(table.Fields[2000065][11004779].Ranges, Is.EqualTo(new[] { new QuestMaskNpcTable.Range(10003407, 2, 10003418, 2) }));
        Assert.That(table.Fields[2000065][11000074].Enable, Is.False);
        // A field listed again adds to it and a key listed again replaces its mask; a key without enableInMask is 1;
        // a mask of a disabled feature is left out; a range without a start quest drops the whole mask.
        Assert.That(table.Fields[2000001].Keys, Is.EquivalentTo(new[] { 11000001, 11000002 }));
        Assert.That(table.Fields[2000001][11000001].Enable, Is.True);
        Assert.That(table.Fields[2000001][11000001].Ranges, Is.EqualTo(new[] { new QuestMaskNpcTable.Range(400, 3, 401, 2) }));
        Assert.That(table.Fields[2000001][11000002].Enable, Is.True);
        Assert.That(table.Fields[2000001][11000002].Ranges, Has.Count.EqualTo(1));
    }

    private static SpawnPointNPC Point(string entity, int npcId, Vector3 position) {
        return new SpawnPointNPC(entity, 0, position, new Vector3(0, 0, 270), true, true, 0, [new SpawnPointNPCListEntry(npcId, 1)], 10, null);
    }

    // 02000065_bf places Karl 11000074 at 300,0,300; 11004779 is in no xblock, so it stands at that point.
    [Test]
    public void KarlStandsAtTheRegularKarlsPoint() {
        IReadOnlyDictionary<int, QuestMaskNpcTable.Mask> masks = Read().Fields[2000065];
        SpawnPointNPC karl = Point("11000074_0", 11000074, new Vector3(300, 0, 300));

        SpawnPointNPC[] added = QuestMaskRules.PartnerSpawns(masks, [karl], []).ToArray();

        Assert.That(added, Has.Length.EqualTo(1));
        Assert.That(added[0].NpcList, Is.EqualTo(new[] { new SpawnPointNPCListEntry(11004779, 1) }));
        Assert.That(added[0].Position, Is.EqualTo(new Vector3(300, 0, 300)));
        Assert.That(added[0].Rotation, Is.EqualTo(karl.Rotation));
        Assert.That(added[0].SpawnOnFieldCreate, Is.True);
        Assert.That(added[0].RegenCheckTime, Is.EqualTo(10));
        Assert.That(added[0].SpawnPointId, Is.EqualTo(karl.SpawnPointId));
        Assert.That(added[0].EntityId, Is.Not.EqualTo(karl.EntityId));
    }

    [Test]
    public void NoPartnerSpawnWhenPlacedOrUnpaired() {
        IReadOnlyDictionary<int, QuestMaskNpcTable.Mask> masks = Read().Fields[2000065];
        SpawnPointNPC karl = Point("11000074_0", 11000074, new Vector3(300, 0, 300));
        // Placed already, by a regular or an event point.
        Assert.That(QuestMaskRules.PartnerSpawns(masks, [karl, Point("x", 11004779, Vector3.Zero)], []), Is.Empty);
        var eventPoint = new EventSpawnPointNPC("e", 5, Vector3.Zero, Vector3.Zero, true, false, 0, 1, [new SpawnPointNPCListEntry(11004779, 1)], 0, 0, "");
        Assert.That(QuestMaskRules.PartnerSpawns(masks, [karl], [eventPoint]), Is.Empty);
        // The partner is not placed, or only by an event point.
        Assert.That(QuestMaskRules.PartnerSpawns(masks, [], []), Is.Empty);
        Assert.That(QuestMaskRules.PartnerSpawns(masks, [], [new EventSpawnPointNPC("e", 5, Vector3.Zero, Vector3.Zero, true, false, 0, 1, [new SpawnPointNPCListEntry(11000074, 1)], 0, 0, "")]), Is.Empty);
        // Another window: no partner. No masks for the field: nothing.
        Assert.That(QuestMaskRules.PartnerSpawns(Read().Fields[2000001], [Point("a", 11000001, Vector3.Zero)], []), Is.Empty);
        Assert.That(QuestMaskRules.PartnerSpawns(null, [karl], []), Is.Empty);
    }

    // The window 10003407 (mode 2) .. 10003418 (mode 2): 11004779 is shown and 11000074 hidden from when 10003407's
    // conditions are met until 10003418 is rewarded.
    [TestCase(null, false, null, false, false)]
    [TestCase(QuestState.Started, false, null, false, false)]
    [TestCase(QuestState.Started, true, null, false, true)]
    [TestCase(QuestState.Completed, false, null, false, true)]
    [TestCase(QuestState.Completed, false, QuestState.Started, false, true)]
    [TestCase(QuestState.Completed, false, QuestState.Started, true, true)]
    [TestCase(QuestState.Completed, false, QuestState.Completed, false, false)]
    public void KarlWindow(QuestState? start, bool startMet, QuestState? end, bool endMet, bool inWindow) {
        IReadOnlyDictionary<int, QuestMaskNpcTable.Mask> masks = Read().Fields[2000065];
        Func<int, (QuestState?, bool)> quests = id => id == 10003407 ? (start, startMet) : id == 10003418 ? (end, endMet) : (null, false);

        Assert.That(QuestMaskRules.Visible(masks[11004779], quests), Is.EqualTo(inWindow));
        Assert.That(QuestMaskRules.Visible(masks[11000074], quests), Is.EqualTo(!inWindow));
    }

    [Test]
    public void StateModes() {
        // Mode 1: started and not met 0, met or rewarded 1, none 2. Mode 3: rewarded only. Other modes never met.
        Assert.That(QuestMaskRules.QuestStateOf(QuestState.Started, false, 1), Is.EqualTo(0));
        Assert.That(QuestMaskRules.QuestStateOf(QuestState.Started, true, 1), Is.EqualTo(1));
        Assert.That(QuestMaskRules.QuestStateOf(QuestState.Completed, false, 1), Is.EqualTo(1));
        Assert.That(QuestMaskRules.QuestStateOf(null, false, 1), Is.EqualTo(2));
        Assert.That(QuestMaskRules.QuestStateOf(QuestState.Completed, false, 3), Is.EqualTo(0));
        Assert.That(QuestMaskRules.QuestStateOf(QuestState.Started, true, 3), Is.EqualTo(2));
        Assert.That(QuestMaskRules.QuestStateOf(QuestState.Completed, false, 0), Is.EqualTo(2));
        Assert.That(QuestMaskRules.QuestStateOf(QuestState.None, false, 1), Is.EqualTo(2));
        // A mask with no range shows the npc.
        Assert.That(QuestMaskRules.Visible(new QuestMaskNpcTable.Mask(true, []), _ => (null, false)), Is.True);
    }
}

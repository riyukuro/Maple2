using System.Collections.Generic;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// The feature attr of a trigger action is applied by the ingest: an action whose feature is off is dropped from the
// script, so the server never runs it. 02000410's scripts send the same DungeonMissionComplete twice, one under
// DungeonRankBalance_01 (the KR rule) and one under DungeonRankBalance_02 (the CN rule); only the one of the enabled
// feature is kept. Made-up script in that form.
[NonParallelizable]
public class TriggerActionFeatureIngestTests {
    private Dictionary<string, int> oldFeatures = null!;
    private string oldLocale = null!;

    private const string Xml = """
        <ms2>
            <state name="Clear">
                <onEnter>
                    <action feature="DungeonRankBalance_01" name="DungeonMissionComplete" missionID="24090013" />
                    <action feature="DungeonRankBalance_02" name="DungeonMissionComplete" missionID="24090017" />
                    <action name="DungeonMissionComplete" missionID="24090001" />
                </onEnter>
            </state>
        </ms2>
        """;

    [SetUp]
    public void SaveFilter() {
        oldFeatures = FeatureLocaleFilter.Features;
        oldLocale = FeatureLocaleFilter.Locale;
        FeatureLocaleFilter.Locale = "KR";
    }

    [TearDown]
    public void RestoreFilter() {
        FeatureLocaleFilter.Features = oldFeatures;
        FeatureLocaleFilter.Locale = oldLocale;
    }

    private static List<string> MissionIds() {
        var xml = new XmlDocument();
        xml.LoadXml(Xml);
        TriggerMapper.NormalizeTriggerXmlNames(xml);

        var ids = new List<string>();
        foreach (XmlNode action in xml.SelectNodes("//action")!) {
            Assert.That(action.Attributes!["name"]!.Value, Is.EqualTo("dungeon_mission_complete"));
            ids.Add(action.Attributes!["mission_id"]!.Value);
        }
        return ids;
    }

    [Test]
    public void FirstFeatureOn_KeepsOnlyItsAction() {
        FeatureLocaleFilter.Features = new Dictionary<string, int> { ["DungeonRankBalance_01"] = 1 };
        Assert.That(MissionIds(), Is.EqualTo(new[] { "24090013", "24090001" }));
    }

    [Test]
    public void SecondFeatureOn_KeepsOnlyItsAction() {
        FeatureLocaleFilter.Features = new Dictionary<string, int> { ["DungeonRankBalance_02"] = 1 };
        Assert.That(MissionIds(), Is.EqualTo(new[] { "24090017", "24090001" }));
    }

    [Test]
    public void BothFeaturesOff_KeepsOnlyTheUngatedAction() {
        FeatureLocaleFilter.Features = new Dictionary<string, int>();
        Assert.That(MissionIds(), Is.EqualTo(new[] { "24090001" }));
    }
}

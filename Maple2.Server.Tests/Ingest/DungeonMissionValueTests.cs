using System.Collections.Generic;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.Ingest.Helpers;

namespace Maple2.Server.Tests.Ingest;

// The value1 of a dungeon mission as the client reads it (DungeonMissionData*::Load, commonfiles/dungeonmissiondata.cpp):
// npc, skill and buff ids as a comma list of whole numbers, a damage total as an __int64. Ids above 2^24 do not fit a
// float, so a float read moves them (23200083 reads as 23200084) and the mission never counts. Synthetic rows.
[NonParallelizable]
public class DungeonMissionValueTests {
    private Dictionary<string, int> oldFeatures = null!;
    private string oldLocale = null!;

    [SetUp]
    public void SetFilter() {
        oldFeatures = FeatureLocaleFilter.Features;
        oldLocale = FeatureLocaleFilter.Locale;
        FeatureLocaleFilter.Locale = "KR";
        FeatureLocaleFilter.Features = new Dictionary<string, int> { ["Season1"] = 1 };
    }

    [TearDown]
    public void RestoreFilter() {
        FeatureLocaleFilter.Features = oldFeatures;
        FeatureLocaleFilter.Locale = oldLocale;
    }

    private const string Table = """
        <ms2>
          <DungeonMission id="1" feature="Season1" locale="KR" type="DamageToNpc" value1="4952308" maxScore="500" />
          <DungeonMission id="1" feature="Season1" type="DamageToNpc" value1="1622394" maxScore="500" />
          <DungeonMission id="2" feature="Season1" locale="KR" type="DamageToNpc" value1="19809231" maxScore="500" />
          <DungeonMission id="3" feature="Season1" type="LastHitNpc" value1="23200083" maxScore="500" />
          <DungeonMission id="4" feature="Season1" type="LastHitNpc" value1="23200083,23200085" maxScore="500" />
          <DungeonMission id="5" feature="Season1" type="DamageToNpc" value1="1637993.55" value2="24120001" maxScore="500" />
          <DungeonMission id="6" feature="Season1" locale="NA" type="LastHitNpc" value1="7" maxScore="500" />
          <DungeonMission id="6" feature="Season1" type="LastHitNpc" value1="23200087" maxScore="500" />
          <DungeonMission id="7" feature="Season1" type="Trigger" maxScore="500" />
        </ms2>
        """;

    private static Dictionary<int, long[]> Read() {
        var document = new XmlDocument();
        document.LoadXml(Table);
        return DungeonMissionValues.Value1(document);
    }

    [Test]
    public void IdsAboveFloatPrecisionStayExact() {
        Dictionary<int, long[]> values = Read();
        Assert.That(values[3], Is.EqualTo(new long[] { 23200083 }));
        Assert.That(values[4], Is.EqualTo(new long[] { 23200083, 23200085 }));
        Assert.That(values[2], Is.EqualTo(new long[] { 19809231 }));
    }

    [Test]
    public void FirstAvailableRowOfAnIdIsRead() {
        Dictionary<int, long[]> values = Read();
        Assert.That(values[1], Is.EqualTo(new long[] { 4952308 }));
        Assert.That(values[6], Is.EqualTo(new long[] { 23200087 }));
    }

    [Test]
    public void FractionIsCutAndEmptyIsEmpty() {
        Dictionary<int, long[]> values = Read();
        Assert.That(values[5], Is.EqualTo(new long[] { 1637993 }));
        Assert.That(values[7], Is.Empty);
    }
}

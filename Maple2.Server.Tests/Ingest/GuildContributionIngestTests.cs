using System.Collections.Generic;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// guildcontribution.xml rows of the client's shape (made-up types and values).
[NonParallelizable]
public class GuildContributionIngestTests {
    private Dictionary<string, int> oldFeatures = null!;
    private string oldLocale = null!;

    [SetUp]
    public void SetFilter() {
        oldFeatures = FeatureLocaleFilter.Features;
        oldLocale = FeatureLocaleFilter.Locale;
        FeatureLocaleFilter.Locale = "KR";
        FeatureLocaleFilter.Features = new Dictionary<string, int> { ["TestGuild01"] = 1 };
    }

    [TearDown]
    public void RestoreFilter() {
        FeatureLocaleFilter.Features = oldFeatures;
        FeatureLocaleFilter.Locale = oldLocale;
    }

    [Test]
    public void ReadsTheRowsOfEnabledFeaturesAndLocale() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <contribution type="testWin" value="20" feature="TestGuild01" />
              <contribution type="testLose" value="15" feature="TestGuild01" />
              <contribution type="testOff" value="9" feature="TestGuildOff" />
              <contribution type="testNa" value="7" feature="TestGuild01" locale="NA" />
              <contribution type="" value="3" feature="TestGuild01" />
            </ms2>
            """);

        Dictionary<string, int> rows = TableMapper.GuildContributions(document);

        Assert.That(rows, Is.EquivalentTo(new Dictionary<string, int> { ["testWin"] = 20, ["testLose"] = 15 }));

        FeatureLocaleFilter.Locale = "NA";
        Assert.That(TableMapper.GuildContributions(document)["testNa"], Is.EqualTo(7));
    }

    // Two rows whose types differ only by case: the client keeps the last (contribution[kind] = value).
    [Test]
    public void TheLastRowOfATypeWithoutCaseCounts() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <contribution type="testWin" value="20" feature="TestGuild01" />
              <contribution type="TestWin" value="25" feature="TestGuild01" />
              <contribution type="testLose" value="15" feature="TestGuild01" />
            </ms2>
            """);

        Dictionary<string, int> rows = TableMapper.GuildContributions(document);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows["TestWin"], Is.EqualTo(25));
        Assert.That(new Dictionary<string, int>(rows).ContainsKey("TestWin"), Is.True, "the stored key is the last row's");
    }
}

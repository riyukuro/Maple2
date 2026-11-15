using System.Collections.Generic;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;

namespace Maple2.Server.Tests.Ingest;

// The played client's survival skin table (table/na, then table), its rows kept as the restored client keeps them:
// a row of a feature that is off or of another locale is left out, a row without either is kept. Synthetic ids.
[NonParallelizable]
public class PlayedSurvivalSkinsTests {
    private Dictionary<string, int> oldFeatures = null!;
    private string oldLocale = null!;

    [SetUp]
    public void SetFilter() {
        oldFeatures = FeatureLocaleFilter.Features;
        oldLocale = FeatureLocaleFilter.Locale;
        FeatureLocaleFilter.Locale = "KR";
        FeatureLocaleFilter.Features = new Dictionary<string, int> { ["TestOn01"] = 1 };
    }

    [TearDown]
    public void RestoreFilter() {
        FeatureLocaleFilter.Features = oldFeatures;
        FeatureLocaleFilter.Locale = oldLocale;
    }

    [Test]
    public void KeepsTheRowsTheClientKeeps() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <skinInfo id="1" type="effectTail" />
              <skinInfo id="2" type="riding" feature="TestOn01" />
              <skinInfo id="3" type="gliding" feature="TestOff01" />
              <skinInfo id="4" type="gliding" locale="NA" />
              <skinInfo id="5" type="other" />
              <skinInfo id="6" type="gliding" />
            </ms2>
            """);

        Assert.That(TableMapper.PlayedSurvivalSkins(document), Is.EqualTo(new Dictionary<int, MedalType> {
            [1] = MedalType.Tail,
            [2] = MedalType.Riding,
            [6] = MedalType.Gliding,
        }));
    }
}

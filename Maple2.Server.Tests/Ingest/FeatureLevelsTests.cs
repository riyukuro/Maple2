using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Utils;

namespace Maple2.Server.Tests.Ingest;

// The client's CFeatureManager skips a feature with level 0 (no column for the locale), so it is off; the parser's
// filter keeps it on. Every other enabled feature keeps its level (the parser picks between rows by it).
public class FeatureLevelsTests {
    [Test]
    public void LevelZeroIsDropped() {
        var parser = new Dictionary<string, int> { ["FameChallengeBuff_01"] = 0, ["FameContents"] = 32, ["Fame_Geo01"] = 47 };
        Dictionary<string, int> client = FeatureLevels.ClientEnabled(parser);
        Assert.Multiple(() => {
            Assert.That(client.ContainsKey("FameChallengeBuff_01"), Is.False);
            Assert.That(client, Is.EquivalentTo(new Dictionary<string, int> { ["FameContents"] = 32, ["Fame_Geo01"] = 47 }));
        });
    }

    // Data with a column for the locale and no level 0 keeps the same features.
    [Test]
    public void NoLevelZeroKeepsEveryFeature() {
        var parser = new Dictionary<string, int> { ["A"] = 1, ["B"] = 47 };
        Assert.That(FeatureLevels.ClientEnabled(parser), Is.EquivalentTo(parser));
    }

    // Data without a column for the locale reads as level 0 everywhere: the client has no feature on.
    [Test]
    public void NoLocaleColumnHasNoFeature() {
        var parser = new Dictionary<string, int> { ["A"] = 0, ["B"] = 0 };
        Assert.That(FeatureLevels.ClientEnabled(parser), Is.Empty);
    }

    private static XmlDocument Document(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document;
    }

    // A played client's tables (synthetic levels): KR runs at 30 on Live and 99 on Dev; there is no NA column.
    private static readonly XmlDocument Setting = Document("""
        <ms2>
          <setting type="Dev" KR="99" />
          <setting type="Live" KR="30" />
        </ms2>
        """);
    private static readonly XmlDocument Feature = Document("""
        <ms2>
          <feature name="Base" KR="1" />
          <feature name="AtLive" KR="30" />
          <feature name="AboveLive" KR="31" />
          <feature name="DevOnly" KR="99" />
          <feature name="NoKr" CN="1" />
        </ms2>
        """);

    [TestCase("Live", 30, "Base,AtLive")]
    [TestCase("live", 30, "Base,AtLive")]
    [TestCase("Dev", 99, "Base,AtLive,AboveLive,DevOnly")]
    public void PlayedTables_TurnOnTheFeaturesAtTheServerTypeLevel(string env, int level, string on) {
        Dictionary<string, int> enabled = FeatureLevels.ClientEnabled(Setting, Feature, "KR", env, out int read);
        Assert.Multiple(() => {
            Assert.That(read, Is.EqualTo(level));
            Assert.That(enabled.Keys, Is.EquivalentTo(on.Split(',')));
            Assert.That(enabled["AtLive"], Is.EqualTo(30));
        });
    }

    // No row for the server type, or no column for the locale: level 0, every feature off (the ingest stops then).
    [TestCase("KR", "Stage")]
    [TestCase("NA", "Live")]
    public void PlayedTables_WithoutALevelTurnEveryFeatureOff(string locale, string env) {
        Dictionary<string, int> enabled = FeatureLevels.ClientEnabled(Setting, Feature, locale, env, out int level);
        Assert.Multiple(() => {
            Assert.That(level, Is.EqualTo(0));
            Assert.That(enabled, Is.Empty);
        });
    }

    [TestCase(null, "Live")]
    [TestCase("", "Live")]
    [TestCase(" dev ", "Dev")]
    [TestCase("DEVSTAGE", "DevStage")]
    public void Env_ReadsTheServerType(string? value, string env) {
        Assert.That(FeatureLevels.TryReadEnv(value, out string read), Is.True);
        Assert.That(read, Is.EqualTo(env));
    }

    [Test]
    public void Env_RejectsAnUnknownServerType() {
        Assert.That(FeatureLevels.TryReadEnv("Production", out _), Is.False);
    }
}

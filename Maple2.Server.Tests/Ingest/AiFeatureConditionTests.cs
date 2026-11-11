using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// The AI feature condition (four bosses, feature FameChallengeBuff_01) is decided at ingest as the client's
// CFeatureManager decides a feature. feature.xml gives FameChallengeBuff_01 KR="35" and no NA column;
// feature_setting.xml gives the Live setting KR="78" and no NA column.
public class AiFeatureConditionTests {
    private const string Feature = "FameChallengeBuff_01";

    // The parser's filter keeps the features whose level is not above the setting, with their level.
    private static Dictionary<string, int> Enabled(int setting, params (string Name, int Level)[] features) =>
        features.Where(f => f.Level <= setting).ToDictionary(f => f.Name, f => f.Level);

    [Test]
    public void KoreanLiveLevelHasTheFeature() {
        Assert.That(AiMapper.FeatureOn(Feature, Enabled(78, (Feature, 35))), Is.True);
    }

    [Test]
    public void LevelAboveTheSettingIsOff() {
        Assert.That(AiMapper.FeatureOn(Feature, Enabled(34, (Feature, 35))), Is.False);
    }

    // No column for the locale reads as 0: the parser keeps it (0 is not above 0), the client skips it.
    [Test]
    public void NoLocaleColumnIsOff() {
        Dictionary<string, int> enabled = Enabled(0, (Feature, 0));
        Assert.Multiple(() => {
            Assert.That(enabled.ContainsKey(Feature), Is.True);
            Assert.That(AiMapper.FeatureOn(Feature, enabled), Is.False);
        });
    }

    [Test]
    public void UnknownFeatureIsOff() {
        Assert.That(AiMapper.FeatureOn("NoSuchFeature", Enabled(78, (Feature, 35))), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    public void NoFeatureNameIsOn(string? feature) {
        Assert.That(AiMapper.FeatureOn(feature!, new Dictionary<string, int>()), Is.True);
    }
}

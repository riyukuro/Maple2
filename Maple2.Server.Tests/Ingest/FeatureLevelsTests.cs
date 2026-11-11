using System;
using System.Collections.Generic;
using System.Linq;
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
}

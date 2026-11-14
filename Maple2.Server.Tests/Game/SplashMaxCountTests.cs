using System.Collections.Generic;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// stateAttr splashMaxCount: a caster holds at most that many regions of the skill; one more ends the oldest.
public class SplashMaxCountTests {
    private const int Caster = 5, Other = 6;

    [Test]
    public void PlacingPastTheLimitEndsTheOldest() {
        // 10400292 (limit 2): the caster holds two, the third ends the first. Another caster's and another skill's
        // regions do not count.
        var held = new List<(int, int, int)> { (12, Caster, 10400292), (10, Caster, 10400292), (11, Other, 10400292), (13, Caster, 10400291) };
        Assert.That(FieldManager.RegionsOverLimit(held, Caster, 10400292, 2), Is.EqualTo(new[] { 10 }));
        Assert.That(FieldManager.RegionsOverLimit(held, Other, 10400292, 2), Is.Empty);
    }

    [Test]
    public void ALimitOfOneReplacesTheRegion() {
        var held = new List<(int, int, int)> { (20, Caster, 10300193) };
        Assert.That(FieldManager.RegionsOverLimit(held, Caster, 10300193, 1), Is.EqualTo(new[] { 20 }));
    }

    [Test]
    public void NoLimitEndsNothing() {
        var held = new List<(int, int, int)> { (30, Caster, 10400291), (31, Caster, 10400291) };
        Assert.That(FieldManager.RegionsOverLimit(held, Caster, 10400291, 0), Is.Empty);
        Assert.That(FieldManager.RegionsOverLimit([], Caster, 10400292, 2), Is.Empty);
    }

    [Test]
    public void TheIngestReadsItFromThePlayedClient() {
        // Made-up rows.
        var document = new System.Xml.XmlDocument();
        document.LoadXml("""
            <ms2>
              <skill id="100"><basic><stateAttr splashMaxCount="6" /></basic></skill>
              <skill id="101"><basic /></skill>
            </ms2>
            """);
        Dictionary<int, PlayedClientSkills.Entry> read = PlayedClientSkills.Read([document]);
        Assert.That(read[100].SplashMaxCount, Is.EqualTo(6));
        Assert.That(read[101].SplashMaxCount, Is.EqualTo(0));
    }
}

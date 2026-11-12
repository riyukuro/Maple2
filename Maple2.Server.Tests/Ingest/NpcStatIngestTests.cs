using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Ingest.Utils;
using Maple2.File.Parser.Xml.Npc;
using Maple2.Model.Enum;

namespace Maple2.Server.Tests.Ingest;

// The NPC stat block: the played client's <stat> as it is when it has the NPC, else the ingested pack's base plus the
// hidden adds of the features on for the locale. Synthetic numbers.
[NonParallelizable]
public class NpcStatIngestTests {
    private Dictionary<string, int> oldFeatures = null!;
    private string oldLocale = null!;

    [SetUp]
    public void SaveFilter() {
        oldFeatures = FeatureLocaleFilter.Features;
        oldLocale = FeatureLocaleFilter.Locale;
    }

    [TearDown]
    public void RestoreFilter() {
        FeatureLocaleFilter.Features = oldFeatures;
        FeatureLocaleFilter.Locale = oldLocale;
    }

    // Base 1000 hp / 100 ndd / 200 pap and map, with the 01/02, 03 and 04 hidden adds of the ingested pack.
    private static Stat PackStat() => new() {
        hp = 1000,
        ndd = 100,
        pap = 200,
        map = 200,
        hiddenhpadd = 300,
        hiddennddadd = 30,
        hiddenwapadd = 40,
        hiddenhpadd03 = 500,
        hiddennddadd03 = 50,
        hiddenwapadd03 = 60,
        hiddenhpadd04 = 700,
        hiddennddadd04 = 70,
        hiddenwapadd04 = 80,
    };

    private static void Enable(params string[] features) {
        FeatureLocaleFilter.Features = features.ToDictionary(feature => feature, _ => 1);
    }

    private static (long Hp, long Ndd, long Pap, long Map) Values(IReadOnlyDictionary<BasicAttribute, long> stats) => (
        stats.GetValueOrDefault(BasicAttribute.Health), stats.GetValueOrDefault(BasicAttribute.Defense),
        stats.GetValueOrDefault(BasicAttribute.PhysicalAtk), stats.GetValueOrDefault(BasicAttribute.MagicalAtk));

    [TestCase(new string[0], 1000, 100, 200)]
    [TestCase(new[] { "HiddenStatAdd01", "HiddenStatAdd02" }, 1300, 130, 240)]
    [TestCase(new[] { "HiddenStatAdd03" }, 1500, 150, 260)]
    [TestCase(new[] { "HiddenStatAdd01", "HiddenStatAdd02", "HiddenStatAdd03", "HiddenStatAdd04" }, 2500, 250, 380)]
    public void PackStat_AddsTheHiddenStatsOfTheFeaturesOn(string[] features, long hp, long ndd, long atk) {
        Enable(features);

        Assert.That(Values(NpcMapper.MapStats(PackStat())), Is.EqualTo((hp, ndd, atk, atk)));
    }

    // The played client's block wins under every gating: its values have its own region's adds baked in, and the
    // client adds none (also not hidden attributes in its own <stat>).
    [TestCase("")]
    [TestCase("HiddenStatAdd03")]
    [TestCase("HiddenStatAdd01,HiddenStatAdd02,HiddenStatAdd03,HiddenStatAdd04")]
    public void PlayedStat_IsTakenAsItIs(string features) {
        Enable(features.Split(',', StringSplitOptions.RemoveEmptyEntries));
        var played = new Stat { hp = 1300, ndd = 130, pap = 240, map = 240, hiddenhpadd03 = 9000, hiddenwapadd = 9000 };

        Assert.That(Values(NpcMapper.MapStats(PackStat(), played)), Is.EqualTo((1300L, 130L, 240L, 240L)));
    }

    private static XmlDocument Document(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document;
    }

    [Test]
    public void Read_SelectsTheEnvironmentAsTheClientDoes() {
        XmlDocument file = Document("""
            <ms2>
              <npc id="100">
                <environment feature="Low"><stat hp="10" pap="1" /></environment>
                <environment feature="High"><stat hp="20" pap="2" /></environment>
                <environment feature="Off"><stat hp="30" pap="3" /></environment>
              </npc>
              <npc id="200">
                <environment feature="High"><stat hp="40" /></environment>
                <environment feature="High" locale="KR"><stat hp="50" ndd="5" /></environment>
                <environment feature="High" locale="NA"><stat hp="60" /></environment>
              </npc>
              <npc id="300">
                <environment feature="High"><stat hp="70" /></environment>
              </npc>
              <npc id="300">
                <environment feature="High"><stat hp="80" /></environment>
              </npc>
              <npc id="400">
                <environment feature="Off"><stat hp="90" /></environment>
              </npc>
              <npc id="500">
                <environment feature="High" />
              </npc>
            </ms2>
            """);
        var levels = new Dictionary<string, int> { ["Low"] = 10, ["High"] = 20 };
        Func<XmlElement, bool> available = environment => levels.ContainsKey(environment.GetAttribute("feature"))
            && environment.GetAttribute("locale") is "" or "KR";

        Dictionary<int, Stat> stats = PlayedNpcStats.Read([file], available, feature => levels.GetValueOrDefault(feature), "KR");

        Assert.Multiple(() => {
            Assert.That(stats.Keys.Order(), Is.EqualTo(new[] { 100, 200, 300, 500 }));
            Assert.That((stats[100].hp, stats[100].pap), Is.EqualTo((20L, 2L)));
            Assert.That((stats[200].hp, stats[200].ndd), Is.EqualTo((50L, 5L)));
            Assert.That(stats[300].hp, Is.EqualTo(80));
            Assert.That(stats[500].hp, Is.EqualTo(0));
        });
    }
}

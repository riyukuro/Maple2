using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Ingest.Mapper;
using Maple2.File.IO;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// The survival pass rewards left out when the played client has the item as another item (SurvivalPassRewards).
// Shapes from the KMS2 item data and the server pack's NA paid pass.
public class SurvivalPassRewardsTests {
    // 20301787 a festival firework in the KMS2 client, 20301788 a title only under ItemKR, 20300036 the same function
    // as the server pack's, 20301790 two environments that disagree.
    private const string Client = """
        <ms2>
          <item id="20301787"><environment><function name="FestivalEvent" parameter="" /></environment></item>
          <item id="20301788">
            <environment feature="ItemNA"><function name="SurvivalLevelExp" parameter="" /></environment>
            <environment feature="ItemKR"><function name="TitleScroll" parameter="10000858" /></environment>
          </item>
          <item id="20300036"><environment><function name="OpenItemBox" parameter="" /></environment></item>
          <item id="20301790">
            <environment><function name="TitleScroll" parameter="10000860" /></environment>
            <environment><function name="FestivalEvent" parameter="" /></environment>
          </item>
        </ms2>
        """;

    private static readonly Dictionary<int, string> ServerFunctions = new() {
        [20301787] = "SurvivalLevelExp",
        [20301788] = "SurvivalLevelExp",
        [20300036] = "OpenItemBox",
        [20301790] = "SurvivalLevelExp",
    };

    private static Dictionary<int, string> Played() {
        var doc = new XmlDocument();
        doc.LoadXml(Client);
        return SurvivalPassRewards.PlayedFunctions([doc], element => element.GetAttribute("feature") is "" or "ItemKR");
    }

    [Test]
    public void PlayedFunctions_TheAvailableEnvironmentsAgreeOn() {
        Assert.That(Played(), Is.EqualTo(new Dictionary<int, string> {
            [20301787] = "FestivalEvent",
            [20301788] = "TitleScroll",
            [20300036] = "OpenItemBox",
        }));
    }

    [TestCase(20301787, true)]
    [TestCase(20301788, true)]
    [TestCase(20300036, false)]
    [TestCase(20301790, false)] // the client's environments disagree
    [TestCase(20301791, false)] // the client has no such item
    public void Reused_WhenTheClientsFunctionIsAnother(int id, bool reused) {
        Assert.That(SurvivalPassRewards.Reused(id, Played(), item => ServerFunctions.GetValueOrDefault(item)), Is.EqualTo(reused));
    }

    // Paid pass level 26: a box and a 10k tonic; level 29: the tonic alone; level 1: a buff whose id is no item. The
    // tonic is left out, the box and the buff stay, and level 29 keeps its slot with no reward.
    [Test]
    public void Keep_LeavesOutTheReusedItemRewards() {
        Dictionary<int, string> played = Played();
        bool Reused(int id) => SurvivalPassRewards.Reused(id, played, item => ServerFunctions.GetValueOrDefault(item));
        var box = new MapleSurvivalTable.Reward("item", [20300036], [1], [1]);
        var tonic = new MapleSurvivalTable.Reward("item", [20301787], [2], [1]);
        var title = new MapleSurvivalTable.Reward("genderItem", [20301788, 20300036], [2, 2], [1, 1]);
        var buff = new MapleSurvivalTable.Reward("additionalEffect", [20301787], [1], []);
        int dropped = 0;
        Assert.Multiple(() => {
            Assert.That(SurvivalPassRewards.Keep([box, tonic], Reused, ref dropped), Is.EqualTo(new[] { box }));
            Assert.That(SurvivalPassRewards.Keep([tonic], Reused, ref dropped), Is.Empty);
            Assert.That(SurvivalPassRewards.Keep([title, buff], Reused, ref dropped), Is.EqualTo(new[] { buff }));
            Assert.That(dropped, Is.EqualTo(3));
        });
    }

    [TestCase(20301787, "item/2/03/20301787.xml")]
    [TestCase(11200072, "item/1/12/11200072.xml")]
    public void ServerItemFile(int id, string file) {
        Assert.That(SurvivalPassRewards.ServerItemFile(id), Is.EqualTo(file));
    }

    // The ingest over packs: the data pack's paid pass (level 26 a box and a 10k tonic, level 29 the tonic alone) and
    // its tonic item file, the played client's item data with the firework at that id. The tonic rewards are left
    // out, level 29 keeps its slot, and the box stays.
    [Test, NonParallelizable]
    public void Ingest_LeavesOutTheTonicTheClientHasAsAFirework() {
        Dictionary<string, int> oldFeatures = FeatureLocaleFilter.Features;
        string oldLocale = FeatureLocaleFilter.Locale;
        string root = Directory.CreateTempSubdirectory("ms2-survival-pass-test-").FullName;
        try {
            FeatureLocaleFilter.Locale = "KR";
            FeatureLocaleFilter.Features = new Dictionary<string, int> { ["SurvivalContents01"] = 1, ["SurvivalContents03"] = 1 };
            string Pack(string name, params (string Path, string Xml)[] files) {
                string dir = Path.Combine(root, name);
                foreach ((string path, string xml) in files) {
                    string file = Path.Combine(dir, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    System.IO.File.WriteAllText(file, xml);
                }
                string m2d = Path.Combine(root, $"{name}.m2d");
                PlayedClientOverlay.Create(PlayedClientOverlay.Files([dir]), m2d);
                return m2d;
            }
            const string row = "<survivalPassReward id=\"1\" level=\"{0}\" isSpecial=\"\" {1} feature=\"SurvivalContents03\" />";
            const string tonic = "type{0}=\"item\" id{0}=\"20301787\" value{0}=\"2\" count{0}=\"1\"";
            string paid = "<ms2>" + string.Format(row, 26, "type1=\"item\" id1=\"20300036\" value1=\"1\" count1=\"1\" " + string.Format(tonic, 2))
                + string.Format(row, 29, string.Format(tonic, 1)) + "</ms2>";
            string data = Pack("data",
                ("table/na/survivalpassreward_paid.xml", paid),
                ("item/2/03/20301787.xml", "<ms2><environment><function name=\"SurvivalLevelExp\" parameter=\"\" /></environment></ms2>"),
                ("item/2/03/20300036.xml", "<ms2><environment><function name=\"OpenItemBox\" parameter=\"\" /></environment></ms2>"));
            string played = Pack("played", ("itemdata/203.xml", "<ms2>"
                + "<item id=\"20301787\"><environment><function name=\"FestivalEvent\" parameter=\"\" /></environment></item>"
                + "<item id=\"20300036\"><environment><function name=\"OpenItemBox\" parameter=\"\" /></environment></item></ms2>"));
            using var dataReader = new M2dReader(data);
            using var playedReader = new M2dReader(played);
            var mapper = new TableMapper(dataReader, "en", playedReader, null, "KR");
            MethodInfo parse = typeof(TableMapper).GetMethod("ParseMapleSurvivalTable", BindingFlags.NonPublic | BindingFlags.Instance)!;

            var table = (MapleSurvivalTable) parse.Invoke(mapper, null)!;
            // The pack's item data is read once: the next mapper gets the same index.
            Assert.That(PlayedItemIndex.Of(playedReader), Is.SameAs(PlayedItemIndex.Of(playedReader)));
            Assert.That(PlayedItemIndex.Of(playedReader).Ids, Is.EquivalentTo(new[] { 20301787, 20300036 }));
            IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>> levels = table.PaidPass[1];
            Assert.Multiple(() => {
                Assert.That(levels.Keys, Is.EquivalentTo(new[] { 26, 29 }));
                Assert.That(levels[26].SelectMany(reward => reward.Ids), Is.EqualTo(new[] { 20300036 }));
                Assert.That(levels[29], Is.Empty);
            });
        } finally {
            FeatureLocaleFilter.Features = oldFeatures;
            FeatureLocaleFilter.Locale = oldLocale;
            Directory.Delete(root, true);
        }
    }
}

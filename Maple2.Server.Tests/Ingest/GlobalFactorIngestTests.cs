using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// table/Server/<locale>/expcontrol.xml (11 columns per level) and mesocontrol.xml (3): a rate is read by its column,
// whatever the file's attribute order. CN's level 1 row gives 0.8 for the common and elite npc kills.
public class GlobalFactorIngestTests {
    private static XmlDocument Document(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document;
    }

    [Test]
    public void Rates_ByColumn() {
        GlobalFactorTable table = ServerTableMapper.MapGlobalFactor(Document("""
            <ms2>
              <exp lv="1" npcReward_common="0.8" npcReward_dungeonElite="1" npcReward_elite="0.8" npcReward_boss="1" npcReward_dungeonBoss="1" questReward_epic="1" questReward_world="1" questReward_guide="1" miniGame="1" fishing="1" instrument="1" />
              <exp instrument="1.5" questReward_world="1.25" lv="99" />
            </ms2>
            """), Document("""
            <ms2>
              <meso lv="50" questReward_epic="1" questReward_world="1" questReward_guide="0.75" />
            </ms2>
            """));
        Assert.Multiple(() => {
            Assert.That(table.ExpRates, Has.Length.EqualTo(11));
            Assert.That(table.MesoRates, Has.Length.EqualTo(3));
            Assert.That(table.ExpRate(0, 1), Is.EqualTo(0.8f));
            Assert.That(table.ExpRate(2, 1), Is.EqualTo(0.8f));
            Assert.That(table.ExpRate(1, 1), Is.EqualTo(1f));
            Assert.That(table.ExpRate(6, 99), Is.EqualTo(1.25f));
            Assert.That(table.ExpRate(10, 99), Is.EqualTo(1.5f));
            Assert.That(table.ExpRate(5, 99), Is.EqualTo(1f));
            Assert.That(table.MesoRate(2, 50), Is.EqualTo(0.75f));
            Assert.That(table.MesoRate(2, 49), Is.EqualTo(1f));
        });
    }

    // No file, a level outside 1 to 99, and a rate that is unreadable or below 0 leave 1.0; outside the table the rate
    // is 1.0, as the client's.
    [Test]
    public void Rates_DefaultOne() {
        GlobalFactorTable none = ServerTableMapper.MapGlobalFactor(null, null);
        GlobalFactorTable table = ServerTableMapper.MapGlobalFactor(Document("""
            <ms2>
              <exp lv="0" questReward_epic="2" />
              <exp lv="100" questReward_epic="2" />
              <exp lv="x" questReward_epic="2" />
              <exp lv="5" questReward_epic="abc" questReward_world="-1" questReward_guide="NaN" />
            </ms2>
            """), null);
        Assert.Multiple(() => {
            Assert.That(none.ExpRates.Concat(none.MesoRates).SelectMany(rates => rates), Has.All.EqualTo(1f));
            Assert.That(table.ExpRates.SelectMany(rates => rates), Has.All.EqualTo(1f));
            Assert.That(table.ExpRate(11, 5), Is.EqualTo(1f));
            Assert.That(table.ExpRate(5, 0), Is.EqualTo(1f));
            Assert.That(table.MesoRate(0, 100), Is.EqualTo(1f));
        });
    }
}

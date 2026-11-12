using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game.Util;

public class MonsterExpTests {
    // Made-up values in the table's row layout.
    private const string Xml = """
        <ms2>
          <exp level="1" common="10" dungeonElite="40" elite="20" boss="30" dungeonBoss="50" />
          <exp level="2" common="11" dungeonElite="" elite="21" boss="31" dungeonBoss="51" />
          <exp level="2" common="12" dungeonElite="42" elite="22" boss="32" dungeonBoss="52" />
          <exp level="" common="99" dungeonElite="99" elite="99" boss="99" dungeonBoss="99" />
        </ms2>
        """;

    private static MonsterExpTable Table() {
        var document = new XmlDocument();
        document.LoadXml(Xml);
        return ServerTableMapper.MapMonsterExp(document);
    }

    [Test]
    public void RowsByLevel() {
        MonsterExpTable table = Table();
        Assert.Multiple(() => {
            Assert.That(table.Entries.Keys, Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(table.Entries[1], Is.EqualTo(new MonsterExpMetadata(10, 20, 30, 40, 50)));
            Assert.That(table.Entries[2], Is.EqualTo(new MonsterExpMetadata(12, 22, 32, 42, 52)));
        });
    }

    [TestCase(1, 10)]
    [TestCase(3, 20)]
    [TestCase(4, 30)]
    [TestCase(6, 50)]
    [TestCase(7, 40)]
    [TestCase(2, 10)]
    [TestCase(5, 10)]
    public void ColumnByClass(int npcClass, long exp) {
        Assert.That(Table().Entries[1].ForClass(npcClass), Is.EqualTo(exp));
    }
}

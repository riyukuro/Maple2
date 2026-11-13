using System.IO;
using System.Xml;
using Maple2.File.Ingest.Helpers;

namespace Maple2.Server.Tests.Ingest;

// The server pack's socket scroll rows (table/Server/itemSocketScroll.xml): successProb and openCount by scroll id,
// read for type 1 rows as CItemSocketScrollDataServer::Read does. The rows are as in the KR pack; a commented-out row
// (the pack keeps an old copy of every row with successProb 10000) is not read.
public class ItemSocketScrollIngestTests {
    private const string Xml = """
        <ms2>
            <scroll id="10000001" type="1" usePart="1" minLv="50" maxLv="50" slot="12" rank="4" successProb="7000" openCount="1" />
            <scroll id="10000003" type="1" usePart="1" minLv="50" maxLv="50" slot="12" rank="4" successProb="3000" openCount="3" />
            <scroll id="10000030" type="1" usePart="1" minLv="50" maxLv="50" slot="12, 19, 20" rank="4" successProb="7000" openCount="1" tradableCountDeduction="1" />
            <scroll id="10000040" type="2" usePart="1" minLv="50" maxLv="50" slot="12" rank="4" successProb="7000" openCount="1" />
            <!-- <scroll id="10000002" type="1" usePart="1" minLv="50" maxLv="50" slot="12" rank="4" successProb="10000" openCount="2" /> -->
            <scroll id="10000001" type="1" usePart="1" minLv="50" maxLv="50" slot="12" rank="4" successProb="10000" openCount="2" />
        </ms2>
        """;

    [Test]
    public void ServerRows_GiveRateAndOpenCount() {
        var rows = ServerItemSocketScroll.Read(XmlReader.Create(new StringReader(Xml)));
        Assert.Multiple(() => {
            Assert.That(rows.Keys, Is.EquivalentTo(new[] { 10000001, 10000003, 10000030 }));
            Assert.That(rows[10000001], Is.EqualTo(new ServerItemSocketScroll.Entry(7000, 1)));
            Assert.That(rows[10000003], Is.EqualTo(new ServerItemSocketScroll.Entry(3000, 3)));
            Assert.That(rows[10000030], Is.EqualTo(new ServerItemSocketScroll.Entry(7000, 1)));
        });
    }
}

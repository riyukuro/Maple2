using System.Linq;
using System.Xml;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

// MS2_PLAYED_CLIENT_XML: NPCs the played client has no NPC data for are not spawned.
public class PlayedClientNpcsTests {
    private static XmlDocument File(string npcs) {
        var document = new XmlDocument();
        document.LoadXml($"<ms2>{npcs}</ms2>");
        return document;
    }

    // The filter the ingest passes: here an environment with locale KR or feature Off is not available.
    private static bool Available(XmlElement node) => node.GetAttribute("locale") is "" or "NA" && node.GetAttribute("feature") != "Off";

    // The client keeps an <npc> with a non-zero id and an environment that passes; <item> rows are not NPCs.
    [Test]
    public void Ids_AreTheNpcsWithAnAvailableEnvironment() {
        XmlDocument first = File("""
            <npc id="100"><environment /></npc>
            <npc id="101"><environment locale="KR" /></npc>
            <npc id="102"><environment locale="KR" /><environment locale="NA" /></npc>
            <npc id="103"><environment feature="Off" /></npc>
            <npc id="104" />
            <npc id="0"><environment /></npc>
            <item id="105"><environment /></item>
            """);
        XmlDocument second = File("""<npc id="200"><environment /></npc>""");

        Assert.That(PlayedClientNpcs.Ids([first, second], Available).OrderBy(id => id), Is.EqualTo(new[] { 100, 102, 200 }));
    }

    [Test]
    public void IsNpcFile_IsTheNpcDataFolder() {
        Assert.Multiple(() => {
            Assert.That(PlayedClientNpcs.IsNpcFile("npcdata/020.xml"), Is.True);
            Assert.That(PlayedClientNpcs.IsNpcFile("NpcData/020.xml"), Is.True);
            Assert.That(PlayedClientNpcs.IsNpcFile("npc/02/02000001.xml"), Is.False);
            Assert.That(PlayedClientNpcs.IsNpcFile("table/npcdata.xml"), Is.False);
        });
    }

    // An NPC the played client lacks is spawned by no server path; without the mark (no played client given, or
    // data ingested before the mark) every NPC can be.
    [Test]
    public void CanSpawn_NotAnNpcThePlayedClientLacks() {
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(10, "10", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);

        Assert.Multiple(() => {
            Assert.That(PlayedClientNpcs.CanSpawn(metadata), Is.True);
            Assert.That(PlayedClientNpcs.CanSpawn(metadata with { Basic = basic with { NotInPlayedClient = true } }), Is.False);
        });
    }
}

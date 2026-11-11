using System.Xml;
using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// A split action (one script name, several server actions picked by an argument): with no splitter
// argument the ingest takes the first variant, and an unknown value leaves the action alone; neither throws.
public class TriggerSplitterIngestTests {
    private static XmlNodeList Normalize(string actions) {
        var xml = new XmlDocument();
        xml.LoadXml($"<ms2><state name=\"Wait\"><onEnter>{actions}</onEnter></state></ms2>");
        TriggerMapper.NormalizeTriggerXmlNames(xml);
        return xml.SelectNodes("//action")!;
    }

    [Test]
    public void Splitter_PicksByValueFirstWhenMissingAndSkipsUnknown() {
        XmlNodeList actions = Normalize("""
            <action name="ArcadeBoomBoomOcean" type="EndGame" />
            <action name="ArcadeBoomBoomOcean" lifeCount="3" />
            <action name="ArcadeBoomBoomOcean" type="NoSuchVariant" />
            """);

        Assert.That(actions[0]!.Attributes!["name"]!.Value, Is.EqualTo("arcade_boom_boom_ocean_end_game"));
        Assert.That(actions[1]!.Attributes!["name"]!.Value, Is.EqualTo("arcade_boom_boom_ocean_start_game"));
        Assert.That(actions[2]!.Attributes!["name"]!.Value, Is.EqualTo("arcade_boom_boom_ocean"));
    }
}

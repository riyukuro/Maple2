using System.Xml;
using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// set_skip and set_scene_skip name a state: the ingest must fix that name the same way as the state's own
// name, or the server never finds the skip target. Made-up script.
public class TriggerSkipStateIngestTests {
    private const string Xml = """
        <ms2>
            <state name="1-대사">
                <onEnter>
                    <action name="스킵을설정한다" arg1="2-대사" />
                    <action name="SetSceneSkip" arg1="2-대사" arg2="nextState" />
                </onEnter>
            </state>
            <state name="2-대사" />
        </ms2>
        """;

    [Test]
    public void SkipTargets_MatchTheFixedStateNames() {
        var xml = new XmlDocument();
        xml.LoadXml(Xml);
        TriggerMapper.NormalizeTriggerXmlNames(xml);

        string target = xml.SelectNodes("//state")![1]!.Attributes!["name"]!.Value;
        Assert.That(target, Is.Not.EqualTo("2-대사"));
        XmlNodeList actions = xml.SelectNodes("//action")!;
        Assert.That(actions[0]!.Attributes!["name"]!.Value, Is.EqualTo("set_skip"));
        Assert.That(actions[0]!.Attributes!["state"]!.Value, Is.EqualTo(target));
        Assert.That(actions[1]!.Attributes!["name"]!.Value, Is.EqualTo("set_scene_skip"));
        Assert.That(actions[1]!.Attributes!["state"]!.Value, Is.EqualTo(target));
    }
}

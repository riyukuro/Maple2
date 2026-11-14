using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Mapper;

namespace Maple2.Server.Tests.Ingest;

// The live-only actions and conditions have no override entry: the ingest must still snake-case their
// parameters, which is how the server reads them. Made-up script.
public class TriggerLiveParamIngestTests {
    private const string Xml = """
        <ms2>
            <state name="Start">
                <onEnter>
                    <action name="LookCenterCameraSetting" boxId="101" centerPos="1,2,3" outDist="500" />
                    <action name="SpawnItemGroup" boxId="101" groupCount="2" group1="9001" />
                </onEnter>
                <condition name="CheckNpcSlaveCount" spawnPointID="7" count="2" />
                <condition name="CheckUserAdditionalCountInRose" triggerBoxIDs="101,102" additionalEffectID="50000" />
                <condition name="CheckMaxUserCount" checkCount="4" />
                <condition name="!NpcIsDead" spawnId="7" />
            </state>
        </ms2>
        """;

    [Test]
    public void LiveOnlyParams_AreSnakeCased() {
        var xml = new XmlDocument();
        xml.LoadXml(Xml);
        TriggerMapper.NormalizeTriggerXmlNames(xml);

        XmlNodeList actions = xml.SelectNodes("//action")!;
        Assert.That(actions[0]!.Attributes!["name"]!.Value, Is.EqualTo("look_center_camera_setting"));
        Assert.That(actions[0]!.Attributes!["box_id"]?.Value, Is.EqualTo("101"));
        Assert.That(actions[0]!.Attributes!["center_pos"]?.Value, Is.EqualTo("1,2,3"));
        Assert.That(actions[0]!.Attributes!["out_dist"]?.Value, Is.EqualTo("500"));
        Assert.That(actions[1]!.Attributes!["name"]!.Value, Is.EqualTo("spawn_item_group"));
        Assert.That(actions[1]!.Attributes!["group_count"]?.Value, Is.EqualTo("2"));
        Assert.That(actions[1]!.Attributes!["group1"]?.Value, Is.EqualTo("9001"));

        XmlNodeList conditions = xml.SelectNodes("//condition")!;
        Assert.That(conditions[0]!.Attributes!["name"]!.Value, Is.EqualTo("check_npc_slave_count"));
        Assert.That(conditions[0]!.Attributes!["spawn_point_id"]?.Value, Is.EqualTo("7"));
        Assert.That(conditions[0]!.Attributes!["count"]?.Value, Is.EqualTo("2"));
        Assert.That(conditions[1]!.Attributes!["trigger_box_ids"]?.Value, Is.EqualTo("101,102"));
        Assert.That(conditions[1]!.Attributes!["additional_effect_id"]?.Value, Is.EqualTo("50000"));
        Assert.That(conditions[2]!.Attributes!["check_count"]?.Value, Is.EqualTo("4"));
        Assert.That(conditions[3]!.Attributes!["name"]!.Value, Is.EqualTo("npc_is_dead"));
        Assert.That(conditions[3]!.Attributes!["negate"]?.Value, Is.EqualTo("true"));
        Assert.That(conditions[3]!.Attributes!["spawn_id"]?.Value, Is.EqualTo("7"));
        foreach (XmlNode node in actions.Cast<XmlNode>().Concat(conditions.Cast<XmlNode>())) {
            foreach (XmlAttribute attribute in node.Attributes!) {
                Assert.That(attribute.Name, Is.EqualTo(attribute.Name.ToLowerInvariant()), node.OuterXml);
            }
        }
    }

    // The light actions' trailing args are the fade flag and seconds the client reads; NpcRemoveAdditionalEffect
    // also comes with additionalEffect (KR 52100401_qd, 02200002_dg) instead of additionalEffectID.
    [Test]
    public void LightFadeArgs_AndRemoveEffectAlias() {
        var xml = new XmlDocument();
        xml.LoadXml("""
            <ms2><state name="Start"><onEnter>
                <action name="SetAmbientLight" arg1="214,200,179" arg2="1" arg3="2.0" />
                <action name="SetDirectionalLight" arg1="255,247,239" arg2="255,255,255" arg3="1" arg4="1.5" />
                <action name="NpcRemoveAdditionalEffect" spawnPointID="2000" additionalEffect="44100004" />
            </onEnter></state></ms2>
            """);
        TriggerMapper.NormalizeTriggerXmlNames(xml);

        XmlNodeList actions = xml.SelectNodes("//action")!;
        Assert.That(actions[0]!.Attributes!["name"]!.Value, Is.EqualTo("set_ambient_light"));
        Assert.That((actions[0]!.Attributes!["primary"]?.Value, actions[0]!.Attributes!["fade"]?.Value, actions[0]!.Attributes!["fade_seconds"]?.Value),
            Is.EqualTo(("214,200,179", "1", "2.0")));
        Assert.That(actions[1]!.Attributes!["name"]!.Value, Is.EqualTo("set_directional_light"));
        Assert.That((actions[1]!.Attributes!["specular_color"]?.Value, actions[1]!.Attributes!["fade"]?.Value, actions[1]!.Attributes!["fade_seconds"]?.Value),
            Is.EqualTo(("255,255,255", "1", "1.5")));
        Assert.That(actions[2]!.Attributes!["name"]!.Value, Is.EqualTo("npc_remove_additional_effect"));
        Assert.That((actions[2]!.Attributes!["spawn_id"]?.Value, actions[2]!.Attributes!["additional_effect_id"]?.Value),
            Is.EqualTo(("2000", "44100004")));
    }
}

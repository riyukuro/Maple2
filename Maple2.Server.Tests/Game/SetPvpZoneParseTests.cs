using System.Linq;
using System.Reflection;
using System.Xml;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Tests.Game;

// set_pvp_zone as the ingest stores the 9 shipped uses (arg1-arg6 renamed by TriggerDefinitionOverride): box_id,
// prepare_time, match_time, additional_effect_id (absent in 61000035's two rounds), type and box_ids (types 3 and 4;
// 64000001 writes them with spaces after the commas).
public class SetPvpZoneParseTests {
    private static object?[] Args(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        IAction action = TriggerFunctionMapping.ActionMap["set_pvp_zone"](document.DocumentElement!.Attributes);
        Assert.That(action, Is.InstanceOf<Trigger.SetPvpZone>());
        FieldInfo[] fields = action.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        string[] order = ["boxId", "prepareTime", "matchTime", "additionalEffectId", "type", "boxIds"];
        return order.Select(name => fields.Single(field => field.Name == name || field.Name.StartsWith($"<{name}>")).GetValue(action)).ToArray();
    }

    // 65000001_bd, 66000003_gd (the Red Arena), 65000002_bd (the free-for-all).
    [TestCase("box_id=\"102\" prepare_time=\"1\" match_time=\"120\" additional_effect_id=\"90001002\" type=\"1\"", 102, 1, 120, 90001002, 1)]
    [TestCase("box_id=\"101\" prepare_time=\"5\" match_time=\"300\" additional_effect_id=\"90001002\" type=\"2\"", 101, 5, 300, 90001002, 2)]
    // 61000035_me and _me_item: no additional effect, read as 0.
    [TestCase("box_id=\"701\" prepare_time=\"5\" match_time=\"240\" type=\"5\"", 701, 5, 240, 0, 5)]
    [TestCase("box_id=\"701\" prepare_time=\"5\" match_time=\"240\" type=\"6\"", 701, 5, 240, 0, 6)]
    public void NoBoxIds(string attributes, int box, int prepare, int match, int effect, int type) {
        object?[] args = Args($"<action name=\"set_pvp_zone\" {attributes} />");
        Assert.That(args[..5], Is.EqualTo(new object[] { box, prepare, match, effect, type }));
        Assert.That(args[5], Is.Empty);
    }

    // 65000003_bd, 66000004_gd, 99999905 (Treasure Island) and 64000001_gd (the guild arena, spaced list).
    [TestCase("1,2,101,102,103", 104, 3, 600, 3, new[] { 1, 2, 101, 102, 103 })]
    [TestCase("102, 103, 112, 113, 10, 11, 1, 3", 101, 30, 120, 4, new[] { 102, 103, 112, 113, 10, 11, 1, 3 })]
    public void BoxIds(string boxIds, int box, int prepare, int match, int type, int[] expected) {
        object?[] args = Args($"<action name=\"set_pvp_zone\" box_id=\"{box}\" prepare_time=\"{prepare}\" match_time=\"{match}\" "
                              + $"additional_effect_id=\"90001002\" type=\"{type}\" box_ids=\"{boxIds}\" />");
        Assert.That(args[..5], Is.EqualTo(new object[] { box, prepare, match, 90001002, type }));
        Assert.That(args[5], Is.EqualTo(expected));
    }
}

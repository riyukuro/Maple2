using System.Linq;
using System.Reflection;
using System.Xml;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Tests.Game;

// shadow_expedition_points as the ingest stores it: the threshold is the "point" attribute (02000401_bf, 02020062_bf).
public class ShadowExpeditionPointsParseTests {
    private static int Point(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        ICondition condition = TriggerFunctionMapping.ConditionMap["shadow_expedition_points"](document.DocumentElement!.Attributes);
        Assert.That(condition, Is.InstanceOf<Trigger.ShadowExpeditionPoints>());
        FieldInfo field = condition.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(info => info.FieldType == typeof(int));
        return (int) field.GetValue(condition)!;
    }

    [TestCase("<condition name=\"shadow_expedition_points\" point=\"300\" />", 300)]
    [TestCase("<condition name=\"shadow_expedition_points\" point=\"750\"><transition state=\"a\" /></condition>", 750)]
    public void Point_IsRead(string xml, int expected) {
        Assert.That(Point(xml), Is.EqualTo(expected));
    }
}

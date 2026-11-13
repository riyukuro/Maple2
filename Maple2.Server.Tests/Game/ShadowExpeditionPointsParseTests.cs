using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using Maple2.Server.Game.Trigger;
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

    [Test]
    public void OpenGauge_HoldsUntilItsPoints() {
        Assert.That(TriggerContext.ShadowGaugeReached(0, true, 300), Is.False);
        Assert.That(TriggerContext.ShadowGaugeReached(299, true, 300), Is.False);
        Assert.That(TriggerContext.ShadowGaugeReached(300, true, 300), Is.True);
        Assert.That(TriggerContext.ShadowGaugeReached(1000, true, 300), Is.True);
    }

    [Test]
    public void NoGaugeOpen_WaitsOnlyWhereAScriptOpensOne() {
        // 52010038_qd tests 150 before its gauge script opens the gauge; a field that opens none passes.
        Assert.That(TriggerContext.ShadowGaugeReached(null, true, 150), Is.False);
        Assert.That(TriggerContext.ShadowGaugeReached(null, false, 1000), Is.True);
    }

    [Test]
    public void OpensShadowGauge_FindsTheOpenOnExitToo() {
        static IAction Action(string xml) {
            var document = new XmlDocument();
            document.LoadXml(xml);
            return TriggerFunctionMapping.ActionMap[document.DocumentElement!.GetAttribute("name")](document.DocumentElement.Attributes);
        }
        IAction open = Action("<action name=\"shadow_expedition_open_boss_gauge\" max_gauge_point=\"1000\" />");
        IAction close = Action("<action name=\"shadow_expedition_close_boss_gauge\" />");

        var closing = new Trigger([new Trigger.State("idle", null, new Trigger.State.OnEnter(new LinkedList<IAction>([close]), null), null)]);
        Assert.That(closing.OpensShadowGauge(), Is.False);
        var opening = new Trigger([new Trigger.State("gauge", null, null, new Trigger.State.OnExit(new LinkedList<IAction>([open])))]);
        Assert.That(opening.OpensShadowGauge(), Is.True);
    }
}

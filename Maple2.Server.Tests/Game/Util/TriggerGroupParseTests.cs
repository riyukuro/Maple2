using System.Linq;
using System.Xml;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// all_of / any_one: the group's inner conditions are its own; the transition and actions sit on the outer node.
public class TriggerGroupParseTests {
    private static Trigger Parse(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return TriggerCache.ParseTrigger("test", "test", document);
    }

    [Test]
    public void AllOf_GetsItsInnerConditionsAndTheOuterTransition() {
        Trigger trigger = Parse("""
            <ms2><state name="Wait">
              <condition name="all_of">
                <group>
                  <condition name="user_value" key="a" value="1" />
                  <condition name="user_value" key="b" value="2" />
                </group>
                <transition state="End" />
                <action name="set_user_value" trigger_id="1" key="c" value="3" />
              </condition>
            </state><state name="End" /></ms2>
            """);

        ICondition condition = trigger.States[0].Conditions.Single();
        var group = (IGroupCondition) condition;
        Assert.That(group, Is.InstanceOf<Trigger.GroupAllOf>());
        Assert.That(group.Conditions, Has.Count.EqualTo(2));
        Assert.That(group.Conditions.All(c => c is Trigger.UserValue), Is.True);
        Assert.That(group.NextState, Is.EqualTo("End"));
        Assert.That(group.Actions, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnyOne_IsTrueWhenOneInnerConditionHolds() {
        Trigger trigger = Parse("""
            <ms2><state name="Wait">
              <condition name="any_one">
                <group><condition name="true" /></group>
                <transition state="End" />
              </condition>
            </state><state name="End" /></ms2>
            """);

        var group = (IGroupCondition) trigger.States[0].Conditions.Single();
        Assert.That(group.Conditions, Has.Count.EqualTo(1));
        Assert.That(group.Evaluate(null!), Is.True);
        Assert.That(group.NextState, Is.EqualTo("End"));
    }
}

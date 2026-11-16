using System;
using System.Linq;
using System.Xml;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The trigger parser (TriggerCache.ParseTrigger): a state's onExit actions are kept, and a malformed node (a
// condition or action without a name, an unknown condition, a group condition without its group) is skipped with
// the rest of the state parsed.
public class TriggerParseShapeTests {
    private static Trigger Parse(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return TriggerCache.ParseTrigger("test", "test", document);
    }

    // KR 66200001_gd 11_banner_thenumberofsurvivor: BannerCheckIn shows the two team counts on enter and clears its
    // key on exit, so the next check-in is asked again; the other states' onExit is empty.
    [Test]
    public void KrBanner_KeepsTheOnExitAction() {
        Trigger trigger = Parse("""
            <ms2><state name="Wait"><onEnter></onEnter><condition name="user_value" key="BannerCheckIn" value="1"><transition state="BannerCheckIn" /></condition><onExit></onExit></state><state name="BannerCheckIn"><onEnter><action name="set_user_value_from_user_count" key="BannerNumberOfBlue" trigger_box_id="9001" user_tag_id="1" /><action name="user_value_to_number_mesh" key="BannerNumberOfBlue" start_mesh_id="700" digit_count="2" /><action name="set_user_value_from_user_count" key="BannerNumberOfRed" trigger_box_id="9001" user_tag_id="2" /><action name="user_value_to_number_mesh" key="BannerNumberOfRed" start_mesh_id="1700" digit_count="2" /></onEnter><condition name="wait_tick" wait_tick="1000"><transition state="NextWait" /></condition><onExit><action name="set_user_value" key="BannerCheckIn" value="0" /></onExit></state><state name="NextWait"><condition name="user_value" key="BannerCheckIn" value="1"><transition state="BannerCheckIn" /></condition><onExit></onExit></state></ms2>
            """);

        Assert.That(trigger.States.Select(state => state.Name), Is.EqualTo(new[] { "Wait", "BannerCheckIn", "NextWait" }));
        Trigger.State checkIn = trigger.States[1];
        Assert.That(checkIn.Enter!.Actions, Has.Count.EqualTo(4));
        Assert.That(checkIn.Conditions.Single().NextState, Is.EqualTo("NextWait"));
        Assert.That(checkIn.Exit!.Actions, Has.Count.EqualTo(1));
        Assert.That(trigger.States[0].Exit!.Actions, Is.Empty);
        Assert.That(trigger.States[0].Enter!.NextState, Is.Null);
    }

    // A transition in onEnter is the state's next state at once.
    [Test]
    public void OnEnterTransition_IsTheNextState() {
        Trigger trigger = Parse("""<ms2><state name="A"><onEnter><transition state="B" /></onEnter></state><state name="B" /></ms2>""");

        Assert.That(trigger.States[0].Enter!.NextState, Is.EqualTo("B"));
    }

    [Test]
    public void MalformedNodes_AreSkipped() {
        Trigger trigger = Parse("""
            <ms2><state name="A">
              <condition key="x" value="1"><transition state="B" /></condition>
              <condition name="no_such_condition"><transition state="B" /></condition>
              <condition name="all_of"><transition state="B" /></condition>
              <condition name="any_one"><group><condition name="no_such_condition" /><condition name="true" /></group><transition state="B" /></condition>
              <onExit><action key="x" value="1" /><action name="no_such_action" /><action name="set_user_value" key="x" value="0" /></onExit>
            </state><state name="B" /><state /></ms2>
            """);

        Assert.That(trigger.States.Select(state => state.Name), Is.EqualTo(new[] { "A", "B" }));
        var group = (IGroupCondition) trigger.States[0].Conditions.Single();
        Assert.That(group.Conditions, Has.Count.EqualTo(1));
        Assert.That(group.NextState, Is.EqualTo("B"));
        Assert.That(trigger.States[0].Exit!.Actions, Has.Count.EqualTo(1));
    }

    [Test]
    public void ARootOtherThanMs2_Throws() {
        Assert.Throws<ArgumentException>(() => Parse("""<trigger><state name="A" /></trigger>"""));
    }
}

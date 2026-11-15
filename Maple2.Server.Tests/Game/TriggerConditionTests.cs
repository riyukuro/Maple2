using System;
using System.Linq;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// Trigger conditions run through the script mapping on a field: the user value test (16927 KR uses, 12 as the
// negated name), the count_users operators and the user and tick tests. Made-up keys and values.
public class TriggerConditionTests : TriggerTestBase {
    // user_value without an operator is an equality test; a key no script set reads 0.
    [Test]
    public void UserValue_EqualByDefault() {
        Start();
        Assert.That(Condition("""<condition name="user_value" key="Phase" value="0" />"""), Is.True);
        Assert.That(Condition("""<condition name="user_value" key="Phase" value="1" />"""), Is.False);

        Action("""<action name="set_user_value" trigger_id="0" key="Phase" value="1" />""");

        Assert.That(Condition("""<condition name="user_value" key="Phase" value="1" />"""), Is.True);
        Assert.That(Condition("""<condition name="user_value" key="Phase" value="0" />"""), Is.False);
        Assert.That(Condition("""<condition name="user_value" key="phase" value="1" />"""), Is.False);
    }

    // The operators the KR scripts write (GreaterEqual 34, LessEqual 19, Less 9, Equal 4) and Greater, on a key holding 5.
    [TestCase("GreaterEqual", 5, true)]
    [TestCase("GreaterEqual", 6, false)]
    [TestCase("LessEqual", 5, true)]
    [TestCase("LessEqual", 4, false)]
    [TestCase("LessEqual", 6, true)]
    [TestCase("Less", 6, true)]
    [TestCase("Less", 5, false)]
    [TestCase("Greater", 4, true)]
    [TestCase("Greater", 5, false)]
    [TestCase("Equal", 5, true)]
    [TestCase("Equal", 4, false)]
    public void UserValue_Operators(string op, int value, bool expected) {
        Start();
        Field.UserValues.Set(1, "Count", 5);

        Assert.That(Condition($"""<condition name="user_value" key="Count" value="{value}" operator="{op}" />"""), Is.EqualTo(expected));
    }

    // The negated name ("!UserValue", 12 uses) turns the test around, for each operator.
    [Test]
    public void UserValue_Negate() {
        Start();
        Field.UserValues.Set(1, "Cleared", 1);

        Assert.That(Condition("""<condition name="user_value" key="Cleared" value="1" negate="true" />"""), Is.False);
        Assert.That(Condition("""<condition name="user_value" key="Cleared" value="2" negate="true" />"""), Is.True);
        Assert.That(Condition("""<condition name="user_value" key="Missing" value="1" negate="true" />"""), Is.True);
        Assert.That(Condition("""<condition name="user_value" key="Cleared" value="0" operator="Greater" negate="true" />"""), Is.False);
    }

    // A script reads only its own values: a value written for another trigger is not seen, and set_user_value with a
    // trigger_id writes that trigger's.
    [Test]
    public void UserValue_PerTrigger() {
        Start();

        Action("""<action name="set_user_value" trigger_id="7" key="Door" value="1" />""");

        Assert.That(Condition("""<condition name="user_value" key="Door" value="1" />"""), Is.False);
        Assert.That(Field.UserValues.Get(7, "Door"), Is.EqualTo(1));
        Action("""<action name="set_user_value" trigger_id="0" key="Door" value="1" />""");
        Assert.That(Condition("""<condition name="user_value" key="Door" value="1" />"""), Is.True);
        Assert.That(Field.UserValues.Get(TriggerUserValues.Target(1, 0), "Door"), Is.EqualTo(1));
    }

    // add_user_value counts up on the script's own key, and the compare follows.
    [Test]
    public void UserValue_AddThenCompare() {
        Start();

        Field.UserValues.Set(1, "Kills", 0);
        Action("""<action name="add_user_value" key="Kills" value="2" />""");
        Action("""<action name="add_user_value" key="Kills" value="1" />""");

        Assert.That(Condition("""<condition name="user_value" key="Kills" value="3" />"""), Is.True);
        Assert.That(Condition("""<condition name="user_value" key="Kills" value="3" operator="Greater" />"""), Is.False);
    }

    // count_users operators on the one player in box 1 (the box holds the origin).
    [TestCase("Greater", 0, true)]
    [TestCase("Greater", 1, false)]
    [TestCase("GreaterEqual", 1, true)]
    [TestCase("GreaterEqual", 2, false)]
    [TestCase("Equal", 1, true)]
    [TestCase("Equal", 2, false)]
    [TestCase("LessEqual", 1, true)]
    [TestCase("LessEqual", 0, false)]
    [TestCase("Less", 2, true)]
    [TestCase("Less", 1, false)]
    public void CountUsers_Operators(string op, int minUsers, bool expected) {
        StartInBox();

        Assert.That(Condition($"""<condition name="count_users" box_id="1" min_users="{minUsers}" operator="{op}" />"""), Is.EqualTo(expected));
        Assert.That(Condition($"""<condition name="count_users" box_id="1" min_users="{minUsers}" operator="{op}" negate="1" />"""), Is.EqualTo(!expected));
    }

    // user_count and check_user follow the players in the field: a second player changes the count, and an empty
    // field passes only the negated check_user.
    [Test]
    public void UserCount_FollowsThePlayers() {
        Start();
        Assert.That(Condition("""<condition name="user_count" check_count="1" />"""), Is.True);

        TestGameSession other = SecondPlayer();

        Assert.That(Condition("""<condition name="user_count" check_count="2" />"""), Is.True);
        Assert.That(Condition("""<condition name="user_count" check_count="1" />"""), Is.False);
        Field.Players.Clear();
        Assert.That(Condition("""<condition name="check_user" />"""), Is.False);
        Assert.That(Condition("""<condition name="check_user" negate="1" />"""), Is.True);
        Assert.That(Condition("""<condition name="user_count" check_count="0" />"""), Is.True);
        GC.KeepAlive(other);
    }

    // wait_tick passes only after the wait: at the exact tick it does not (the test is strictly later).
    [Test]
    public void WaitTick_StrictlyAfter() {
        Start();
        context.StartTick = Environment.TickCount64 + 60_000;

        Assert.That(Condition("""<condition name="wait_tick" wait_tick="0" />"""), Is.False);
        Assert.That(Condition("""<condition name="wait_tick" wait_tick="59000" />"""), Is.False);
        context.StartTick = Environment.TickCount64 - 1_000;
        Assert.That(Condition("""<condition name="wait_tick" wait_tick="500" />"""), Is.True);
        Assert.That(Condition("""<condition name="wait_tick" wait_tick="30000" />"""), Is.False);
    }

    // The base harness leaves the field's user values unmade.
    private new void Start(params Maple2.Model.Metadata.Ms2Trigger[] triggers) {
        base.Start(triggers);
        typeof(Maple2.Server.Game.Manager.Field.FieldManager).GetField(nameof(Maple2.Server.Game.Manager.Field.FieldManager.UserValues))!.SetValue(Field, new TriggerUserValues());
    }

    // Box 1 holds the player at the origin.
    private void StartInBox() {
        Start(new Maple2.Model.Metadata.Ms2TriggerBox(System.Numerics.Vector3.Zero, new System.Numerics.Vector3(300, 300, 300), 1, true));
        test.Session.Player.Position = System.Numerics.Vector3.Zero;
    }
}

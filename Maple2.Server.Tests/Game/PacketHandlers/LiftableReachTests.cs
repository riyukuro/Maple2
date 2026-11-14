using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Liftable 1: the client lifts only an object within its ActionDistance (150 by default; KR sets 120 or 150), from
// the action key (actionkeyobject.cpp:1320). The server lifted any liftable of the field from anywhere. The object
// is made up.
public class LiftableReachTests {
    private const string EntityId = "liftablereachtest0000000000000001";

    private static (TestGameSession Test, FieldLiftable Liftable) Start(Vector3 player, int count = 3, Vector3? at = null) {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = test.Session.Field!;
        FieldInfo liftables = typeof(FieldManager).GetField("fieldLiftables", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (liftables.GetValue(field) == null) {
            liftables.SetValue(field, System.Activator.CreateInstance(liftables.FieldType));
        }
        FieldLiftable liftable = field.AddLiftable(EntityId, new Liftable(30000001, count, 0, 60000, 0, "0", "", "0", "0", false,
            at ?? new Vector3(1000, 0, 0), Vector3.Zero))!;
        test.Session.Player.Position = player;
        test.Session.HeldCube = null;
        test.TakeSent();
        return (test, liftable);
    }

    private static void Pickup(TestGameSession test) {
        new LiftableHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteString(EntityId);
        }));
    }

    [Test]
    public void ALiftableAtThePlayer_IsLifted() {
        (TestGameSession test, FieldLiftable liftable) = Start(new Vector3(900, 0, 0));
        Pickup(test);
        Assert.Multiple(() => {
            Assert.That(test.Session.HeldCube, Is.Not.Null);
            Assert.That(liftable.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void ALiftableAcrossTheField_IsNotLifted() {
        (TestGameSession test, FieldLiftable liftable) = Start(Vector3.Zero);
        Pickup(test);
        Assert.Multiple(() => {
            Assert.That(test.Session.HeldCube, Is.Null);
            Assert.That(liftable.Count, Is.EqualTo(3));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    // The last one taken from a map liftable (a live case: map 2000084, the object at 4500,2850,3300) only sets its
    // state to Removed (Liftable 2, state 1). A cube remove at its cell (30,19,22) made the client log "[ERROR] No
    // cube on that position = 30, 19, 22" in chat: the object is no cube the client holds.
    [Test]
    public void TheLastOfAMapLiftable_SendsNoCubeRemove() {
        (TestGameSession test, FieldLiftable liftable) = Start(new Vector3(4500, 2850, 3250), 1, new Vector3(4500, 2850, 3300));
        Pickup(test);
        Assert.Multiple(() => {
            Assert.That(test.Session.HeldCube, Is.Not.Null);
            Assert.That(liftable.State, Is.EqualTo(LiftableState.Removed));
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Liftable, SendOp.SetCraftMode }));
        });
    }

    [Test]
    public void Reach_IsTheLargestActionDistancePlusSlack() {
        Assert.Multiple(() => {
            Assert.That(InteractRules.InReach(Vector3.Zero, new Vector3(InteractRules.LiftReach + InteractRules.ReachSlack, 0, 0), InteractRules.LiftReach), Is.True);
            Assert.That(InteractRules.InReach(Vector3.Zero, new Vector3(InteractRules.LiftReach + InteractRules.ReachSlack + 1, 0, 0), InteractRules.LiftReach), Is.False);
        });
    }
}

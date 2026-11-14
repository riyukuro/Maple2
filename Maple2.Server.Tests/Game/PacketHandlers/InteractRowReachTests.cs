using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// An object that is not gathered: the client picks it and keeps the interaction only within its row's
// interactionDistance (worldservice_1.cpp:4567, stateinteractdata.cpp:382; most KR rows 155). The server took the end
// of any such object within 1500. The object is made up.
public class InteractRowReachTests {
    private static InteractObjectMetadata Row(float distance) {
        return new InteractObjectMetadata(11, InteractType.Mesh, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0, WeaponItemId: 0,
            new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(0, 0, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), [], InteractionDistance: distance);
    }

    // The end of the object "ab" at the origin, with the player at (x, 0, 0); the answers sent.
    private static int End(float distance, float x) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.AddInteract(Row(distance), new InteractMeshObject("ab", new Ms2InteractMesh(11, Vector3.Zero, Vector3.Zero)));
        session.Player.Position = new Vector3(x, 0, 0);
        test.TakeSent();
        new InteractObjectHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteInt(1);
            writer.WriteString("ab");
        }));
        return test.TakeSent().Count;
    }

    [Test]
    public void AnEndWithinTheRowsDistance_IsTaken() {
        Assert.That(End(155, 250), Is.EqualTo(2));
    }

    [Test]
    public void AnEndBeyondTheRowsDistance_IsRefused() {
        Assert.That(End(155, 600), Is.Zero);
    }

    [Test]
    public void ARowWithNoDistance_KeepsTheFlatReach() {
        Assert.Multiple(() => {
            Assert.That(End(0, 600), Is.EqualTo(2));
            Assert.That(InteractRules.ObjectReach(0), Is.EqualTo(InteractRules.OtherReach));
            Assert.That(InteractRules.ObjectReach(900), Is.EqualTo(900));
        });
    }
}

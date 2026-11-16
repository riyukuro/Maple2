using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Network;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// GuideObjectSync 0x0035: the client moves its guide object (the construction cursor, the home ball, the fishing
// float). The records go to the field's other players; the guide object takes the last record's place and facing.
public class GuideObjectSyncHandlerTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private readonly GuideObjectSyncHandler handler = new();

    [SetUp]
    public void Field() {
        test = new TestGameSession();
        test.Start();
        session.State = SessionState.Connected;
        session.GuideObject = session.Field!.SpawnGuideObject(session.Player, new ConstructionGuideObject());
        test.TakeSent();
    }

    // i16 kind, u8 count, then per record the UserSync record (z = 30 + its number, rotation 90 + 10 per number)
    // and one i32 tick.
    private static ByteReader Packet(GuideObjectType kind, int records, ActorState state = ActorState.Idle, byte byteAfterSpeed = 2) {
        var writer = new ByteWriter();
        writer.WriteShort((short) kind);
        writer.WriteByte((byte) records);
        for (int number = 1; number <= records; number++) {
            writer.WriteByte((byte) state);
            writer.WriteByte(0);
            writer.WriteShort(10);
            writer.WriteShort(20);
            writer.WriteShort((short) (30 + number));
            writer.WriteShort((short) (90 + 10 * number));
            writer.WriteByte(9);
            writer.WriteShort(0);
            writer.WriteShort(0);
            writer.WriteShort(0);
            writer.WriteByte(byteAfterSpeed);
            writer.WriteShort(0);
            writer.WriteShort(0);
            writer.WriteByte(0);
            writer.WriteInt(number);
            writer.WriteInt(123456);
        }
        return new ByteReader(writer.ToArray(), 0);
    }

    [Test]
    public void Sync_MovesTheGuideObjectToTheLastRecord() {
        handler.Handle(session, Packet(GuideObjectType.Construction, 2));

        Vector3 position = session.GuideObject!.Position;
        Assert.That(new[] { position.X, position.Y, position.Z }, Is.EqualTo(new[] { 10f, 20f, 32f }).Within(0.01f));
        // The record's rotation is in tenths of a degree, as the client writes it for a UserSync record.
        Assert.That(session.GuideObject.Rotation.Z, Is.EqualTo(11f).Within(0.01f));
        Assert.That(test.TakeSent(), Is.Empty, "the sender is not sent its own move");
    }

    // A sync of another kind of guide object, of no record, or of a record the other clients cannot read moves
    // nothing.
    [Test]
    public void Sync_OfAnotherKind_MovesNothing() {
        Vector3 before = session.GuideObject!.Position;
        handler.Handle(session, Packet(GuideObjectType.Fishing, 1));
        Assert.That(session.GuideObject.Position, Is.EqualTo(before));
    }

    [Test]
    public void Sync_OfNoRecord_MovesNothing() {
        Vector3 before = session.GuideObject!.Position;
        handler.Handle(session, Packet(GuideObjectType.Construction, 0));
        Assert.That(session.GuideObject.Position, Is.EqualTo(before));
    }

    [Test]
    public void Sync_OfAnUnreadableRecord_MovesNothing() {
        Vector3 before = session.GuideObject!.Position;
        handler.Handle(session, Packet(GuideObjectType.Construction, 1, byteAfterSpeed: 0x14));
        Assert.That(session.GuideObject.Position, Is.EqualTo(before));
    }

    [Test]
    public void Sync_WithNoGuideObject_IsDropped() {
        session.GuideObject = null;
        Assert.DoesNotThrow(() => handler.Handle(session, Packet(GuideObjectType.Construction, 1)));
    }

    // The emote records of the micro games read their own extra fields.
    [TestCase(ActorState.MicroGameRps)]
    [TestCase(ActorState.MicroGameCoupleDance)]
    [TestCase(ActorState.WeddingEmotion)]
    public void ReadRecords_TakesTheMicroGameRecords(ActorState state) {
        var writer = new ByteWriter();
        writer.WriteByte(1);
        StateSync typed = state switch {
            ActorState.MicroGameRps => new StateSyncRps { State = state },
            ActorState.MicroGameCoupleDance => new StateSyncCoupleDance { State = state },
            _ => new StateSyncWeddingEmotion { State = state },
        };
        typed.WriteTo(writer);
        writer.WriteInt(77);
        var packet = new ByteReader(writer.ToArray(), 0);

        StateSync[]? records = GuideObjectSyncHandler.ReadRecords(packet, out int[] ticks);

        Assert.That(records, Is.Not.Null);
        Assert.That(records![0].GetType(), Is.EqualTo(typed.GetType()));
        Assert.That(ticks, Is.EqualTo(new[] { 77 }));
        Assert.That(packet.Available, Is.EqualTo(0));
    }
}

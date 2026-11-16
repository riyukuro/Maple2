using System;
using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// GuideObjectSync (i16 kind, u8 count, records): the owner's guide object moves go to the others on the field as
// GuideObject 2 (int id, then the records the guide reads, CSceneStateGame::OnGuideObject scenestategame.cpp:12341),
// and the server's guide object takes the last record's place.
public class GuideObjectSyncHandlerTests {
    private const int GuideId = 4343;

    private TestGameSession test = null!;
    private TestGameSession otherTest = null!;
    private GameSession session => test.Session;
    private FieldGuideObject guide = null!;
    private readonly GuideObjectSyncHandler handler = new();

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        otherTest = new TestGameSession();
        otherTest.Start();
        otherTest.Session.Field = session.Field;
        session.Field!.Players[session.Player.ObjectId] = session.Player;
        session.Field.Players[otherTest.Session.Player.ObjectId + 1000] = otherTest.Session.Player;
        guide = new FieldGuideObject(session.Field, GuideId, new ConstructionGuideObject()) { CharacterId = session.CharacterId };
        session.GuideObject = guide;
        session.State = SessionState.Connected;
        test.TakeSent();
        otherTest.TakeSent();
    }

    private static void Record(IByteWriter writer, short z, short rotation) {
        writer.WriteByte((byte) ActorState.Idle);
        writer.WriteByte(0);
        writer.WriteShort(10);
        writer.WriteShort(20);
        writer.WriteShort(z);
        writer.WriteShort(rotation);
        writer.WriteByte(9);
        writer.WriteShort(0);
        writer.WriteShort(0);
        writer.WriteShort(0);
        writer.WriteByte(2);
        writer.WriteShort(0);
        writer.WriteShort(0);
        writer.WriteByte(0);
        writer.WriteInt(1);
    }

    private void Sync(GuideObjectType type, int count) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteShort((short) type);
            writer.WriteByte((byte) count);
            for (int i = 0; i < count; i++) {
                Record(writer, (short) (30 + i), (short) (900 + i));
                writer.WriteInt(123456);
            }
        }));
    }

    [Test]
    public void Sync_GoesToTheOthers_AndMovesTheGuide() {
        Sync(GuideObjectType.Construction, 2);

        byte[] sent = otherTest.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.GuideObject);
        var expected = new ByteWriter();
        expected.Write<SendOp>(SendOp.GuideObject);
        expected.WriteByte(2);
        expected.WriteInt(GuideId);
        expected.WriteByte(2);
        Record(expected, 30, 900);
        Record(expected, 31, 901);
        Assert.Multiple(() => {
            Assert.That(Convert.ToHexString(sent), Is.EqualTo(Convert.ToHexString(expected.ToArray())));
            Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.GuideObject), Is.Empty);
            Assert.That(guide.Position.Z, Is.EqualTo(31).Within(0.01f));
            Assert.That(guide.Rotation.Z, Is.EqualTo(90.1f).Within(0.01f));
            Assert.That(guide.Heading!.Value.Z, Is.EqualTo(90.1f).Within(0.01f));
        });
    }

    [Test]
    public void Sync_OtherKind_Ignored() {
        Vector3 before = guide.Position;
        Sync(GuideObjectType.Fishing, 1);

        Assert.Multiple(() => {
            Assert.That(otherTest.TakeSent(), Is.Empty);
            Assert.That(guide.Position, Is.EqualTo(before));
        });
    }

    [Test]
    public void Sync_NoRecord_Ignored() {
        Sync(GuideObjectType.Construction, 0);

        Assert.That(otherTest.TakeSent(), Is.Empty);
    }

    [Test]
    public void Sync_WithoutAGuide_Ignored() {
        session.GuideObject = null;
        Sync(GuideObjectType.Construction, 1);

        Assert.That(otherTest.TakeSent(), Is.Empty);
    }

    [Test]
    public void Sync_BeforeTheFieldIsEntered_Ignored() {
        session.State = SessionState.ChangeMap;
        Sync(GuideObjectType.Construction, 1);

        Assert.That(otherTest.TakeSent(), Is.Empty);
    
    }

    // A player who comes later gets the guide where its owner last moved it, facing the last record's way.
    [Test]
    public void Sync_ThenCreate_SendsTheLastPlaceAndHeading() {
        guide.Heading = new Vector3(0, 0, 270f);
        Sync(GuideObjectType.Construction, 2);

        var reader = new ByteReader(Maple2.Server.Game.Packets.GuideObjectPacket.Create(guide).ToArray());
        reader.Read<SendOp>();
        reader.ReadByte();
        reader.ReadShort();
        reader.ReadInt();
        reader.ReadLong();
        Vector3 position = reader.Read<Vector3>();
        Vector3 heading = reader.Read<Vector3>();
        Assert.Multiple(() => {
            Assert.That(position.Z, Is.EqualTo(31).Within(0.01f));
            Assert.That(heading.Z, Is.EqualTo(90.1f).Within(0.01f));
        });
    }
}

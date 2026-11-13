using System;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The layouts are the client's readers: CSceneStateGame::OnReactor, CReactor::FUN_140b79490 / FUN_140b79a10 and
// ReactorContext::Read.
public class ReactorPacketTests {
    [Test]
    public void Add_WritesWhatTheClientReads() {
        ByteWriter writer = ReactorPacket.Add(77, "Reactor_5", 10000002, new Vector3(1, 2, 3), new Vector3(0, 0, 90),
            stateIndex: 1, stateTime: 1234, characterIds: [11L, 12L], unknown: 9);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Reactor));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(77));
        Assert.That(reader.ReadString(), Is.EqualTo("Reactor_5"));
        Assert.That(reader.ReadInt(), Is.EqualTo(10000002));
        Assert.That(reader.Read<Vector3>(), Is.EqualTo(new Vector3(1, 2, 3)));
        Assert.That(reader.Read<Vector3>(), Is.EqualTo(new Vector3(0, 0, 90)));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(1234));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(12));
        Assert.That(reader.ReadInt(), Is.EqualTo(9));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void OtherModes_WriteWhatTheClientReads() {
        Assert.That(Bytes(ReactorPacket.Remove(77)), Is.EqualTo(new byte[] { 1, 77, 0, 0, 0 }));
        // Mode 5 carries the reactor id (the reactor table key), here 10000001.
        Assert.That(Bytes(ReactorPacket.StartReact(10000001)), Is.EqualTo(new byte[] { 5, 0x81, 0x96, 0x98, 0 }));
        Assert.That(Bytes(ReactorPacket.UpdateState(77, 2, 0, [])),
            Is.EqualTo(new byte[] { 3, 77, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        // The amount is a short.
        Assert.That(Bytes(ReactorPacket.Reward([new ReactorTable.Reward(0x01020304, 1, 3)])),
            Is.EqualTo(new byte[] { 4, 1, 0, 0, 0, 4, 3, 2, 1, 3, 0 }));
    }

    // The connected players go behind a u8 count: never more ids than the count says (ReactorRules.MaxConnected).
    [Test]
    public void UpdateState_CountsAtMost255Players() {
        long[] players = new long[300];
        for (int i = 0; i < players.Length; i++) {
            players[i] = i + 1;
        }
        byte[] bytes = Bytes(ReactorPacket.UpdateState(77, 2, 0, players));
        Assert.That(bytes[17], Is.EqualTo(255));
        Assert.That(bytes.Length, Is.EqualTo(1 + 4 + 4 + 8 + 1 + 255 * 8 + 4));
        Assert.That(BitConverter.ToInt64(bytes, 18 + 254 * 8), Is.EqualTo(255));
    }

    [Test]
    public void GameEvent_ReactorIsNamedAndLaidOutForTheClient() {
        var metadata = new GameEventMetadata(900080, GameEventType.reactor, DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(1000),
            TimeSpan.Zero, TimeSpan.Zero, [], new Reactor([2000064], 3), "02000064", "3", "", "");
        var writer = new ByteWriter();
        new GameEvent(metadata).WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Reactor"));
        Assert.That(reader.ReadInt(), Is.EqualTo(900080)); // GameEvent::vfunc_4
        Assert.That(reader.ReadInt(), Is.EqualTo(900080)); // ReactorContext::Read
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(1000));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(2000064));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    private static byte[] Bytes(ByteWriter writer) {
        return writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();
    }
}

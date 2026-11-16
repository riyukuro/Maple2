using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// FieldRemoveUser, FieldRemoveNpc and FieldRemovePet against CSceneStateGame::OnFieldRemoveUser, OnFieldRemoveNpc and
// OnFieldRemovePet (client/ms2/scenestategame.cpp:2387, 3587, 9614): one int object id. ChangeBackground against
// OnChangeBackground (scenestategame.cpp:6675): an ANSI string (CInPacket::ReadString). DungeonMission give up against
// RoomStageDungeonCli::OnGiveUp: mode byte only.
public class FieldRemoveLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    [Test]
    public void RemoveModes() {
        foreach ((ByteWriter writer, SendOp op) in new[] {
                     (FieldPacket.RemovePlayer(41), SendOp.FieldRemoveUser), (FieldPacket.RemoveNpc(41), SendOp.FieldRemoveNpc),
                     (FieldPacket.RemovePet(41), SendOp.FieldRemovePet),
                 }) {
            ByteReader reader = Read(writer, op);
            Assert.That(reader.ReadInt(), Is.EqualTo(41));
            Assert.That(reader.Available, Is.EqualTo(0));
        }
    }

    [Test]
    public void BackgroundAndGiveUp() {
        ByteReader reader = Read(FieldPropertyPacket.Background("Bg/Night.dds"), SendOp.ChangeBackground);
        Assert.That(reader.ReadString(), Is.EqualTo("Bg/Night.dds"));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(DungeonMissionPacket.GiveUp(), SendOp.DungeonMission);
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

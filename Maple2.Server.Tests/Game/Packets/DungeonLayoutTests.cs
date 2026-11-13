using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class DungeonLayoutTests {
    private static ByteReader Open(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(op));
        return reader;
    }

    // DungeonWaiting 0xF0, UIDungeonLobby::OnRoomPacket (client/ms2/uidungeonlobby.cpp:68): no mode, the room id and
    // the party size (two u32).
    [Test]
    public void WaitingIsRoomThenSize() {
        ByteWriter writer = DungeonWaitingPacket.Set(24091005, 4);
        ByteReader reader = Open(writer, SendOp.DungeonWaiting);
        Assert.That(reader.ReadInt(), Is.EqualTo(24091005));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // DungeonMission 0x100 mode 1, RoomStageDungeonCli::OnUpdateMissions (client/ms2/roomstagedungeoncli.cpp:152):
    // i32 n, then per mission its id and DungeonMissionCli::vfunc_2 of its type (dungeonmissioncli.cpp): every type
    // reads an i16 score; PlayTime then two i64, LimitUserCount nothing, the DungeonMissionEvent types an i16 count.
    [Test]
    public void UpdateIsCountThenTypedMissions() {
        var play = new DungeonMission(new DungeonMissionMetadata(1, DungeonMissionType.PlayTime, [], 600, 300, 0, false));
        var hits = new DungeonMission(new DungeonMissionMetadata(2, DungeonMissionType.LastHitNpc, [21000001], 0, 600, 3, false));
        var users = new DungeonMission(new DungeonMissionMetadata(3, DungeonMissionType.LimitUserCount, [], 0, 100, 0, false));
        hits.AddLastHit(21000001);

        ByteWriter writer = DungeonMissionPacket.Update(play, hits, users);
        ByteReader reader = Open(writer, SendOp.DungeonMission);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadShort(), Is.EqualTo(200));
        Assert.That(reader.ReadShort(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 4, RoomStageDungeonCli::OnFlag (roomstagedungeoncli.cpp:195): one bool.
    [Test]
    public void SetAbandonIsOneBool() {
        ByteWriter writer = DungeonMissionPacket.SetAbandon(true);
        ByteReader reader = Open(writer, SendOp.DungeonMission);
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }
}

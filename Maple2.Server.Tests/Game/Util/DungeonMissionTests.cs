using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Util;

public class DungeonMissionTests {
    private static DungeonMissionMetadata Mission(int id, DungeonMissionType type, short maxScore = 500, short applyCount = 0,
                                                  bool penalty = false, params long[] value1) {
        return new DungeonMissionMetadata(id, type, value1, 0, maxScore, applyCount, penalty);
    }

    [Test]
    public void FillMissions_PartyThenUserMissionsInTheClientOrder() {
        var table = new Dictionary<int, DungeonMissionMetadata> {
            [1] = Mission(1, DungeonMissionType.DeathCount),
            [2] = Mission(2, DungeonMissionType.DamageToNpc),
            [3] = Mission(3, DungeonMissionType.Trigger),
        };
        var record = new DungeonUserRecord(100, 1);
        // Id 9 has no mission row and is skipped.
        DungeonManager.FillMissions(record, [3], [2, 9, 1], table);
        Assert.That(record.Missions.Keys.ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
        Assert.That(record.Missions[3].IsUserMission, Is.False);
        Assert.That(record.Missions[2].IsUserMission, Is.True);

        // A player who enters the room again keeps the missions of the run.
        record.Missions[3].Complete();
        DungeonManager.FillMissions(record, [3], [2, 9, 1], table);
        Assert.That(record.Missions[3].Score, Is.EqualTo(500));
    }

    [Test]
    public void DungeonMissionPacket_LoadAsTheClientReadsIt() {
        var record = new DungeonUserRecord(100, 1);
        DungeonManager.FillMissions(record, [], [5, 6], new Dictionary<int, DungeonMissionMetadata> {
            [5] = Mission(5, DungeonMissionType.DamageToNpc),
            [6] = Mission(6, DungeonMissionType.LimitUserCount),
        });
        ByteWriter writer = DungeonMissionPacket.Load(record.Missions);
        var reader = new ByteReader(writer.Buffer, 0);
        reader.ReadShort(); // opcode
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        // Event types: id, score, count.
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        // The user count type: id and score only.
        Assert.That(reader.ReadInt(), Is.EqualTo(6));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void RoomStageDungeonPacket_SetAsTheClientReadsIt() {
        // OnRoomStageDungeon mode 0: int room id, byte stage, bool flag.
        ByteWriter writer = RoomStageDungeonPacket.Set(24091005);
        var reader = new ByteReader(writer.Buffer, 0);
        reader.ReadShort();
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(24091005));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }
}

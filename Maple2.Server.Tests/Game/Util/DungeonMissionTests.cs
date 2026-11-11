using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
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
    [Test]
    public void Score_FollowsTheClientRule() {
        // DungeonMissionData::GetScore: maxScore in applyCount steps, a penalty mission counts down.
        DungeonMissionMetadata steps = Mission(1, DungeonMissionType.LastHitNpc, 960, 6);
        Assert.That(DungeonMission.ScoreOf(steps, 0), Is.EqualTo(0));
        Assert.That(DungeonMission.ScoreOf(steps, 2), Is.EqualTo(320));
        Assert.That(DungeonMission.ScoreOf(steps, 9), Is.EqualTo(960));
        DungeonMissionMetadata penalty = Mission(2, DungeonMissionType.DeathCount, 300, 3, true);
        Assert.That(DungeonMission.ScoreOf(penalty, 0), Is.EqualTo(300));
        Assert.That(DungeonMission.ScoreOf(penalty, 1), Is.EqualTo(200));
        Assert.That(DungeonMission.ScoreOf(penalty, 5), Is.EqualTo(0));
        Assert.That(new DungeonMission(penalty).Score, Is.EqualTo(300));
        // No applyCount in the row: one step, as the client's default.
        Assert.That(DungeonMission.ScoreOf(Mission(3, DungeonMissionType.Trigger), 1), Is.EqualTo(500));
    }

    [Test]
    public void DamageToNpc_ScoresWhenTheDamageIsDealt() {
        var mission = new DungeonMission(Mission(1, DungeonMissionType.DamageToNpc, value1: 1000), true);
        Assert.That(mission.AddDamage(23200083, 600), Is.False);
        Assert.That(mission.Score, Is.EqualTo(0));
        Assert.That(mission.AddDamage(23200083, 400), Is.True);
        Assert.That(mission.Score, Is.EqualTo(500));
        Assert.That(mission.AddDamage(23200083, 400), Is.False);

        // Steps: value1 / applyCount damage each. value2 names the npc.
        var stepped = new DungeonMission(new DungeonMissionMetadata(2, DungeonMissionType.DamageToNpc, [1000], 7, 500, 5, false));
        Assert.That(stepped.AddDamage(8, 900), Is.False);
        Assert.That(stepped.AddDamage(7, 450), Is.True);
        Assert.That(stepped.Counter, Is.EqualTo(2));
        Assert.That(stepped.Score, Is.EqualTo(200));
    }

    [Test]
    public void LastHitNpc_CountsKillsOfTheListedNpcs() {
        var mission = new DungeonMission(Mission(1, DungeonMissionType.LastHitNpc, 960, 6, false, 11, 12));
        Assert.That(mission.AddLastHit(13), Is.False);
        Assert.That(mission.AddLastHit(11), Is.True);
        Assert.That(mission.AddLastHit(12), Is.True);
        Assert.That(mission.Score, Is.EqualTo(320));
        Assert.That(new DungeonMission(Mission(2, DungeonMissionType.DamageToNpc, value1: 10)).AddLastHit(11), Is.False);
    }

    [Test]
    public void CountMissions_UserMissionsForThePlayerPartyMissionsForTheRoom() {
        var table = new Dictionary<int, DungeonMissionMetadata> {
            [1] = Mission(1, DungeonMissionType.LastHitNpc, value1: 5),
            [2] = Mission(2, DungeonMissionType.LastHitNpc, value1: 5),
        };
        var room = new DungeonRoomRecord(null!);
        foreach (long characterId in new long[] { 10, 20 }) {
            var record = new DungeonUserRecord(100, characterId);
            DungeonManager.FillMissions(record, [1], [2], table);
            room.UserResults[characterId] = record;
        }

        Dictionary<long, List<DungeonMission>> changed = DungeonFieldManager.CountMissions(room, 10, mission => mission.AddLastHit(5));
        Assert.That(changed[10].Select(mission => mission.Id), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(changed[20].Select(mission => mission.Id), Is.EqualTo(new[] { 1 }));
        Assert.That(room.UserResults[20].Missions[2].Score, Is.EqualTo(0));

        // A room with a result counts nothing more.
        room.State = DungeonState.Clear;
        Assert.That(DungeonFieldManager.CountMissions(room, 20, mission => mission.AddLastHit(5)), Is.Empty);
    }
}

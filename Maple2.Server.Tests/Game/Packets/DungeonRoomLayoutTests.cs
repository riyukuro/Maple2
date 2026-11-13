using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Dungeon;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// Vibrate, HideVibrate and ShowVibrate against CSceneStateGame::OnVibrate (scenestategame.cpp:5981) and the two
// handlers after it; RoomTimer against RoomTimerManager::Dispatch (roomtimermanager.cpp:32); RoomDungeon against
// DungeonRecordManagerCli::OnPacket (dungeonrecordcli.cpp:203); RoomStageDungeon against
// CNetworkService::OnRoomStageDungeon (networkservice.cpp:1082).
public class DungeonRoomLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    // Modes 0 and 1: narrow entity id, i64 cast uid, i32 skill, i16 level, u8 motion, u8 attack, Vector3Short,
    // i32 serial (the server's tick, skipped), narrow bone, u8. Mode 2: narrow id, narrow model, u8.
    [Test]
    public void Vibrate() {
        var record = (DamageRecord) RuntimeHelpers.GetUninitializedObject(typeof(DamageRecord));
        typeof(DamageRecord).GetField("<SkillUid>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(record, 0x0102030405060708);
        record.SkillId = 0x11;
        record.Level = 0x12;
        record.MotionPoint = 1;
        record.AttackPoint = 2;
        record.Position = new Vector3(3, 4, 5);

        var attack = new ByteWriter();
        attack.WriteByte(1);
        attack.WriteString("entity");
        attack.WriteLong(0x0102030405060708);
        attack.WriteInt(0x11);
        attack.WriteShort(0x12);
        attack.WriteByte(1);
        attack.WriteByte(2);
        attack.WriteShort(3);
        attack.WriteShort(4);
        attack.WriteShort(5);
        byte[] head = attack.ToArray();
        byte[] body = Body(VibratePacket.Attack("entity", record));
        Assert.That(body[..head.Length], Is.EqualTo(head));
        Assert.That(body[(head.Length + 4)..], Is.EqualTo(new byte[] { 0, 0, 0 }));

        var invoke = new ByteWriter();
        invoke.WriteByte(2);
        invoke.WriteString("entity");
        invoke.WriteString("");
        invoke.WriteByte(0);
        Assert.That(Body(VibratePacket.Invoke("entity")), Is.EqualTo(invoke.ToArray()));

        var name = new ByteWriter();
        name.WriteString("entity");
        Assert.That(Body(VibratePacket.Hide("entity")), Is.EqualTo(name.ToArray()));
        Assert.That(Body(VibratePacket.Show("entity")), Is.EqualTo(name.ToArray()));
    }

    // 0 and 2: u8 type, i32 start, i32 duration, i32 offset. 1: i32 start, i32 duration, i32 notice, i32 delta.
    // 3: i32 n, n x (u32 id, i32 time). 4 and 5: nothing.
    [Test]
    public void RoomTimer() {
        var timer = (RoomTimer) RuntimeHelpers.GetUninitializedObject(typeof(RoomTimer));
        var clock = new RoomTimerClock(0x22);
        clock.Start(0x21);
        typeof(RoomTimer).GetField(nameof(Maple2.Server.Game.Model.RoomTimer.Type))!.SetValue(timer, RoomTimerType.Clock);
        typeof(RoomTimer).GetField(nameof(Maple2.Server.Game.Model.RoomTimer.Clock))!.SetValue(timer, clock);

        foreach ((byte command, ByteWriter packet) in new[] { ((byte) 0, RoomTimerPacket.Start(timer)), ((byte) 2, RoomTimerPacket.Restart(timer)) }) {
            var expected = new ByteWriter();
            expected.WriteByte(command);
            expected.WriteByte((byte) RoomTimerType.Clock);
            expected.WriteInt(0x21);
            expected.WriteInt(0x22);
            expected.WriteInt(0);
            Assert.That(Body(packet), Is.EqualTo(expected.ToArray()));
        }

        var modify = new ByteWriter();
        modify.WriteByte(1);
        modify.WriteInt(0x21);
        modify.WriteInt(0x22);
        modify.WriteInt(1);
        modify.WriteInt(-5000);
        Assert.That(Body(RoomTimerPacket.Modify(timer, -5000)), Is.EqualTo(modify.ToArray()));

        var laps = new ByteWriter();
        laps.WriteByte(3);
        laps.WriteInt(1);
        laps.WriteInt(0x31);
        laps.WriteInt(0x32);
        Assert.That(Body(RoomTimerPacket.Laps(new Dictionary<int, int> { [0x31] = 0x32 })), Is.EqualTo(laps.ToArray()));

        Assert.That(Body(RoomTimerPacket.Stop()), Is.EqualTo(new byte[] { 4 }));
        Assert.That(Body(RoomTimerPacket.Cancel()), Is.EqualTo(new byte[] { 5 }));
    }

    // DungeonRecord (dungeonrecord.cpp:200): u32 room, i64 expire, u8, (u8, i64), u8, (u8, i64), i32, u16,
    // (u16, i64), u8 flags.
    private static void Record(ByteWriter writer) {
        writer.WriteInt(0x01020304);
        writer.WriteLong(0x41);
        writer.WriteByte(1);
        writer.WriteByte(2);
        writer.WriteLong(0x42);
        writer.WriteByte(3);
        writer.WriteByte(4);
        writer.WriteLong(0x43);
        writer.WriteInt(0x44);
        writer.WriteShort(-1);
        writer.WriteShort(0x45);
        writer.WriteLong(0x46);
        writer.WriteByte((byte) DungeonRecordFlag.Veteran);
    }

    private static DungeonRecord NewRecord() => new(0x01020304) {
        UnionCooldownTimestamp = 0x41,
        UnionClears = 1,
        UnionSubClears = 2,
        UnionSubCooldownTimestamp = 0x42,
        ExtraSubClears = 3,
        ExtraClears = 4,
        ExtraCooldownTimestamp = 0x43,
        TotalClears = 0x44,
        CurrentRecord = 0x45,
        CooldownTimestamp = 0x46,
        Flag = DungeonRecordFlag.Veteran,
    };

    // 5: i32 n, n x (u32 key, record); 6: one record.
    [Test]
    public void Records() {
        var load = new ByteWriter();
        load.WriteByte(5);
        load.WriteInt(1);
        load.WriteInt(0x01020304);
        Record(load);
        Assert.That(Body(DungeonRoomPacket.Load(new Dictionary<int, DungeonRecord> { [0x01020304] = NewRecord() })),
            Is.EqualTo(load.ToArray()));

        var update = new ByteWriter();
        update.WriteByte(6);
        Record(update);
        Assert.That(Body(DungeonRoomPacket.Update(NewRecord())), Is.EqualTo(update.ToArray()));
    }

    // 7: u8 kind, i32 value. 0x12: i32 error, i32 arg. 0x13: i32 n, n x (u32 key, u32 id, i32, i64).
    [Test]
    public void NoticeErrorRewards() {
        var modify = new ByteWriter();
        modify.WriteByte(7);
        modify.WriteByte((byte) DungeonRoomModify.ChangeToExpert);
        modify.WriteInt(0x51);
        Assert.That(Body(DungeonRoomPacket.Modify(DungeonRoomModify.ChangeToExpert, 0x51)), Is.EqualTo(modify.ToArray()));

        var error = new ByteWriter();
        error.WriteByte(0x12);
        error.WriteInt((int) DungeonRoomError.s_room_party_err_cooldown);
        error.WriteInt(0x52);
        Assert.That(Body(DungeonRoomPacket.Error(DungeonRoomError.s_room_party_err_cooldown, 0x52)), Is.EqualTo(error.ToArray()));

        var rewards = new ByteWriter();
        rewards.WriteByte(0x13);
        rewards.WriteInt(1);
        rewards.WriteInt(0x53);
        rewards.WriteInt(0x53);
        rewards.WriteInt(2);
        rewards.WriteLong(0x54);
        var reward = new DungeonRankReward(0x53) { RankClaimed = 2, UpdatedTimestamp = 0x54 };
        Assert.That(Body(DungeonRoomPacket.RankRewards(new Dictionary<int, DungeonRankReward> { [0x53] = reward })),
            Is.EqualTo(rewards.ToArray()));
    }

    // 10 (UIDungeonClosingPhotoDialog::OnClosingPhotoPacket, uidungeonclosingphotodialog.cpp:377): u8 result,
    // i32 n, n x (i64 character, i32 rank, i32 record type, i32 value).
    [Test]
    public void DungeonResult() {
        var result = new DungeonUserResult(0x0102030405060708, DungeonAccumulationRecordType.TotalHealing, 0x61) {
            MissionRank = DungeonMissionRank.S,
        };
        var expected = new ByteWriter();
        expected.WriteByte(10);
        expected.WriteByte((byte) DungeonState.Clear);
        expected.WriteInt(1);
        expected.WriteLong(0x0102030405060708);
        expected.WriteInt((int) DungeonMissionRank.S);
        expected.WriteInt((int) DungeonAccumulationRecordType.TotalHealing);
        expected.WriteInt(0x61);
        Assert.That(Body(DungeonRoomPacket.DungeonResult(DungeonState.Clear,
            new Dictionary<long, DungeonUserResult> { [0x0102030405060708] = result })), Is.EqualTo(expected.ToArray()));
    }

    // 0: i32 dungeon, u8, bool; 1: nothing.
    [Test]
    public void RoomStageDungeon() {
        var set = new ByteWriter();
        set.WriteByte(0);
        set.WriteInt(0x71);
        set.WriteByte(2);
        set.WriteBool(true);
        Assert.That(Body(RoomStageDungeonPacket.Set(0x71, 2, true)), Is.EqualTo(set.ToArray()));
        Assert.That(Body(RoomStageDungeonPacket.Clear()), Is.EqualTo(new byte[] { 1 }));
    }
}

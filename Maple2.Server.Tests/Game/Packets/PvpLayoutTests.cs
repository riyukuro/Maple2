using System;
using System.Collections.Generic;
using Maple2.Model.Game.Pvp;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Pvp 0xB6 as the client reads it: CSceneStateGame::OnPvp (9, 10, 11, 12 CGameRule::ReadValues, 0x64
// CPvPInfo::ReadPointTeamMode, 0x65, 0x66), then CPVPManager::OnMessage: Unk_PvpTimer::OnMessage (0x67-0x6a),
// Unk_PvpEntryBlock::OnMessage (0x6b-0x71) and CPvpRankDuelComponent::OnMessage (0x16, 0x17 CIngredientInfo::Read).
// The other rank duel modes are in RankDuelPacketTests.
public class PvpLayoutTests {
    private const string Id = "0807060504030201";
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Arena() {
        Assert.That(Hex(PvpPacket.ArenaRegistered(3)), Is.EqualTo("B600" + "09" + "03000000"));
        Assert.That(Hex(PvpPacket.ArenaUnregistered()), Is.EqualTo("B600" + "0A"));
        Assert.That(Hex(PvpPacket.ArenaMatchFound(3)), Is.EqualTo("B600" + "0B" + "03000000"));
        // A count byte, then key and value ints.
        Assert.That(Hex(PvpPacket.GameRuleValues([(1, 2), (3, 4)])),
            Is.EqualTo("B600" + "0C" + "02" + "01000000" + "02000000" + "03000000" + "04000000"));
    }

    [Test]
    public void RankDuelRecord() {
        var record = new RankDuelRecord {
            Season = 5,
            Rating = 1200,
            BestRating = 1300,
            WeeklyReward = 7,
            Jobs = new Dictionary<int, RankDuelJobRecord> {
                [20] = new() { Wins = 2, Losses = 1 },
                [10] = new() { Wins = 3, Losses = 4 },
            },
        };
        Assert.That(Hex(PvpPacket.RankDuelOwnRecord(record)),
            Is.EqualTo("B600" + "16" + "05000000" + "B0040000" + "14050000" + "07000000"));
        // An int count, then (job, wins, losses) per row.
        Assert.That(Hex(PvpPacket.RankDuelRecords(record)),
            Is.EqualTo("B600" + "17" + "02000000" + "0A000000" + "03000000" + "04000000" + "14000000" + "02000000" + "01000000"));
    }

    [Test]
    public void PointAndPose() {
        Assert.That(Hex(PvpPacket.Point(0x11223344, 5, 1, 2)), Is.EqualTo("B600" + "64" + "44332211" + "05000000" + "01" + "02"));
        Assert.That(Hex(PvpPacket.WinLose(true)), Is.EqualTo("B600" + "65" + "01"));
        Assert.That(Hex(PvpPacket.WinLose(false)), Is.EqualTo("B600" + "65" + "00"));
        Assert.That(Hex(PvpPacket.ClearRanking()), Is.EqualTo("B600" + "66"));
    }

    // Unk_PvpTimer::OnMessage: 0x67 i64, i32 x4; 0x68 i32; 0x69 i32, wstr; 0x6a i64, i32.
    [Test]
    public void Ranking() {
        Assert.That(Hex(PvpPacket.RankRow(0x0102030405060708, 10, 3, 2)),
            Is.EqualTo("B600" + "67" + Id + "0A000000" + "03000000" + "02000000" + "00000000"));
        Assert.That(Hex(PvpPacket.LastRank(2)), Is.EqualTo("B600" + "68" + "02000000"));
        Assert.That(Hex(PvpPacket.KillNotice(0x1234, "ab")), Is.EqualTo("B600" + "69" + "34120000" + "0200" + "61006200"));
        Assert.That(Hex(PvpPacket.Damage(0x0102030405060708, 300)), Is.EqualTo("B600" + "6A" + Id + "2C010000"));
        Assert.That(Hex(PvpPacket.Damage(0x0102030405060708, 5_000_000_000)), Is.EqualTo("B600" + "6A" + Id + "FFFFFFFF"));
        Assert.That(Hex(PvpPacket.Damage(0x0102030405060708, -5)), Is.EqualTo("B600" + "6A" + Id + "00000000"));
    }

    // Unk_PvpEntryBlock::OnMessage: 0x6b nothing; 0x6c, 0x6e, 0x6f, 0x70 i32, i32; 0x6d i64 then
    // CPvPInfo::ReadRecordFromPacket's three ints (read only for a known player; they are the packet's end);
    // 0x71 wstr, u8.
    [Test]
    public void Team() {
        Assert.That(Hex(PvpPacket.TeamReset()), Is.EqualTo("B600" + "6B"));
        Assert.That(Hex(PvpPacket.TeamScore(3, 4)), Is.EqualTo("B600" + "6C" + "03000000" + "04000000"));
        Assert.That(Hex(PvpPacket.TeamRecord(0x0102030405060708, 3, 2)),
            Is.EqualTo("B600" + "6D" + Id + "03000000" + "02000000" + "00000000"));
        Assert.That(Hex(PvpPacket.TeamResult(201, 5)), Is.EqualTo("B600" + "6E" + "C9000000" + "05000000"));
        Assert.That(Hex(PvpPacket.TeamGauge(600)), Is.EqualTo("B600" + "6F" + "00000000" + "58020000"));
        Assert.That(Hex(PvpPacket.TeamFlag(2, 1)), Is.EqualTo("B600" + "70" + "02000000" + "01000000"));
        Assert.That(Hex(PvpPacket.TeamPlunder("ab", 2)), Is.EqualTo("B600" + "71" + "0200" + "61006200" + "02"));
    }
}

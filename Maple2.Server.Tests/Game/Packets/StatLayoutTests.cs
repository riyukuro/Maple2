using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: both stat readers take count 0x23. The owner's reader (MyPC::vfunc_90) reads every stat, three values each,
// stat 4 as long and the rest as int. The reader for other players (PC::vfunc_90) reads five stats, three values each:
// long stat 4, int stats 0xd, 0xe, 0x20, 0x16.
// The Stat packet (CSceneStateGame::OnStat, scenestategame.cpp:9424): int object id, byte flag (stored in the actor's
// +0x1bc), then the actor's own reader. IsPercentStat (stat.cpp:314) is true for stat 4 (health) alone, so in a list
// only health is a long.
public class StatLayoutTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Stats Sample() {
        return new Stats(new Dictionary<BasicAttribute, long> {
            { BasicAttribute.Strength, 11 },
            { BasicAttribute.Health, 5000 },
            { BasicAttribute.AttackSpeed, 120 },
            { BasicAttribute.MovementSpeed, 130 },
            { BasicAttribute.JumpHeight, 140 },
        }, JobCode.Newbie);
    }

    [Test]
    public void OwnerFormIsEveryStat() {
        var writer = new ByteWriter();
        writer.WriteAllStats(Sample());
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(0x23));
        for (int type = 0; type < 0x23; type++) {
            for (int i = 0; i < 3; i++) {
                long value = type == 4 ? reader.ReadLong() : reader.ReadInt();
                if (i == 0 && type == 0) {
                    Assert.That(value, Is.EqualTo(11));
                }
                if (i == 0 && type == 4) {
                    Assert.That(value, Is.EqualTo(5000));
                }
            }
        }
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void OtherPlayerFormIsFiveStats() {
        var writer = new ByteWriter();
        writer.WritePlayerStats(Sample());
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(0x23));
        for (int i = 0; i < 3; i++) {
            long health = reader.ReadLong();
            int attackSpeed = reader.ReadInt();
            int moveSpeed = reader.ReadInt();
            int mountSpeed = reader.ReadInt();
            int jumpHeight = reader.ReadInt();
            if (i == 0) {
                Assert.That(health, Is.EqualTo(5000));
                Assert.That(attackSpeed, Is.EqualTo(120));
                Assert.That(moveSpeed, Is.EqualTo(130));
                Assert.That(mountSpeed, Is.EqualTo(100));
                Assert.That(jumpHeight, Is.EqualTo(140));
            }
        }
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // The two forms share the count byte and nothing else, so one must never be sent in place of the other.
    [Test]
    public void FormsDifferInLength() {
        var owner = new ByteWriter();
        owner.WriteAllStats(Sample());
        var other = new ByteWriter();
        other.WritePlayerStats(Sample());
        Assert.That(owner.Length, Is.EqualTo(1 + 3 * (8 + 34 * 4)));
        Assert.That(other.Length, Is.EqualTo(1 + 3 * (8 + 4 * 4)));
    }

    private static FieldPlayer Player() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        return test.Session.Player;
    }

    // An npc with only what the writer reads: its object id and an empty stat table.
    private static FieldNpc Npc(int objectId) {
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        var stats = (StatsManager) RuntimeHelpers.GetUninitializedObject(typeof(StatsManager));
        typeof(StatsManager).GetField("<Values>k__BackingField", Any)!.SetValue(stats, new Stats(new Dictionary<BasicAttribute, long>(), 0));
        typeof(Actor<Maple2.Model.Game.Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(npc, objectId);
        typeof(Actor<Maple2.Model.Game.Npc>).GetField("<Stats>k__BackingField", Any)!.SetValue(npc, stats);
        return npc;
    }

    private static ByteReader Reader(ByteWriter writer, int objectId) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Stat));
        Assert.That(reader.ReadInt(), Is.EqualTo(objectId));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        return reader;
    }

    // A player's list (PCBase/PC/MyPC::vfunc_90, count != 0x23): byte n, n x (byte type, 3 values).
    [Test]
    public void PlayerList_OnePacket() {
        FieldPlayer player = Player();
        ByteWriter[] packets = StatsPacket.Updates(player, [BasicAttribute.Health, BasicAttribute.Spirit]).ToArray();
        Assert.That(packets, Has.Length.EqualTo(1));
        ByteReader reader = Reader(packets[0], player.ObjectId);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) BasicAttribute.Health));
        for (int i = 0; i < 3; i++) {
            Assert.That(reader.ReadLong(), Is.EqualTo(player.Stats.Values[BasicAttribute.Health][i]));
        }
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) BasicAttribute.Spirit));
        for (int i = 0; i < 3; i++) {
            Assert.That(reader.ReadInt(), Is.EqualTo((int) player.Stats.Values[BasicAttribute.Spirit][i]));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // An npc (CNpc::vfunc_90, npcactorandvibrate.cpp:2253) reads byte 1 as one (byte type, 3 values) and any other
    // byte as 3 x (long health, int attack speed), so two stats go as two packets of one.
    [Test]
    public void NpcList_OnePacketPerStat() {
        FieldNpc npc = Npc(77);
        npc.Stats.Values[BasicAttribute.Health].Total = 5_000_000_000;
        npc.Stats.Values[BasicAttribute.Health].Base = 4_000_000_000;
        npc.Stats.Values[BasicAttribute.Health].Current = 3_000_000_000;
        npc.Stats.Values[BasicAttribute.Spirit].Current = 40;
        ByteWriter[] packets = StatsPacket.Updates(npc, [BasicAttribute.Health, BasicAttribute.Spirit]).ToArray();
        Assert.That(packets, Has.Length.EqualTo(2));

        ByteReader health = Reader(packets[0], 77);
        Assert.That(health.ReadByte(), Is.EqualTo(1));
        Assert.That(health.ReadByte(), Is.EqualTo((byte) BasicAttribute.Health));
        Assert.That((health.ReadLong(), health.ReadLong(), health.ReadLong()), Is.EqualTo((5_000_000_000L, 4_000_000_000L, 3_000_000_000L)));
        Assert.That(health.Available, Is.EqualTo(0));

        ByteReader spirit = Reader(packets[1], 77);
        Assert.That(spirit.ReadByte(), Is.EqualTo(1));
        Assert.That(spirit.ReadByte(), Is.EqualTo((byte) BasicAttribute.Spirit));
        Assert.That((spirit.ReadInt(), spirit.ReadInt(), spirit.ReadInt()), Is.EqualTo((0, 0, 40)));
        Assert.That(spirit.Available, Is.EqualTo(0));

        Assert.That(StatsPacket.Updates(npc, []), Is.Empty);
    }
}

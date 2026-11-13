using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// FieldAddNpc against the client's reader, CSceneStateGame::OnFieldAddNpc (scenestategame.cpp:3533), up to its last
// read (the npc state sync is not part of this packet): int object id, int npc id, two Vector3; a boss's narrow kfm
// name; CNpc::vfunc_90's stats (byte 35: 3 x (long hp, int attack speed)); byte dead; the buff list; long maid uid,
// byte talkable, int level, int reward item; a boss's unicode owner effect, its skill list and an owner object id; a
// "damaged_hp_upper_hp_bar" npc's (property tag 5) long damage; byte flag.
public class FieldAddNpcLayoutTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    private static FieldNpc Npc(FieldManager field, int npcClass, int friendly, string[] tags, NpcMetadataSkill.Entry[] skills) {
        var basic = new NpcMetadataBasic(friendly, 0, 0, 0, 0, 0, 0, 0x1234, npcClass, false, 0, 0, 0, 0, [], [], 0, tags);
        var stat = new NpcMetadataStat(new Dictionary<BasicAttribute, long> {
            [BasicAttribute.Health] = 0x0102030405,
            [BasicAttribute.AttackSpeed] = 0x64,
        }, [], [], [], []);
        var metadata = new NpcMetadata(21000001, "npc", "", new NpcMetadataModel("boss_kfm", 1, 1), stat, basic, null!,
            new NpcMetadataSkill(skills, 0), null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(npc, new Npc(metadata, null));
        typeof(Actor<Npc>).GetField("<Field>k__BackingField", Any)!.SetValue(npc, field);
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(npc, 0x0A0B0C0D);
        typeof(Actor<Npc>).GetField("<Transform>k__BackingField", Any)!.SetValue(npc, new Transform());
        typeof(Actor<Npc>).GetField("<Stats>k__BackingField", Any)!.SetValue(npc, new StatsManager(npc));
        typeof(Actor<Npc>).GetField("<Buffs>k__BackingField", Any)!.SetValue(npc, new BuffManager(npc));
        typeof(Actor<Npc>).GetField("Logger", Any)!.SetValue(npc, Serilog.Log.Logger);
        typeof(FieldNpc).GetField("<MaidUid>k__BackingField", Any)!.SetValue(npc, 0x1122334455667788);
        typeof(FieldNpc).GetField("<AccumulatedDamage>k__BackingField", Any)!.SetValue(npc, 0x77L);
        npc.Position = new Vector3(1, 2, 3);
        return npc;
    }

    private static void Head(ByteWriter expected, FieldNpc npc) {
        expected.WriteInt(0x0A0B0C0D);
        expected.WriteInt(21000001);
        expected.WriteFloat(1);
        expected.WriteFloat(2);
        expected.WriteFloat(3);
        expected.WriteFloat(npc.Rotation.X);
        expected.WriteFloat(npc.Rotation.Y);
        expected.WriteFloat(npc.Rotation.Z);
    }

    private static void Stats(ByteWriter expected, FieldNpc npc) {
        expected.WriteByte(35); // not 1: vfunc_90's hp/attack speed branch
        for (int i = 0; i < 3; i++) {
            expected.WriteLong(npc.Stats.Values[BasicAttribute.Health][i]);
            expected.WriteInt((int) npc.Stats.Values[BasicAttribute.AttackSpeed][i]);
        }
    }

    private static void Middle(ByteWriter expected) {
        expected.WriteByte(0); // alive
        expected.WriteShort(0); // no buff
        expected.WriteLong(0x1122334455667788); // maid uid
        expected.WriteByte(0); // talkable: no interact
        expected.WriteInt(0x1234); // level (+0xe58)
        expected.WriteInt(0); // no reward item (+0xea8)
    }

    // A friendly class 1 npc without tags: no boss block, no damage.
    [Test]
    public void PlainNpc() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldNpc npc = Npc(test.Session.Field!, 1, 2, [], []);

        var expected = new ByteWriter();
        Head(expected, npc);
        Stats(expected, npc);
        Middle(expected);
        expected.WriteByte(0); // flag
        Assert.That(Body(FieldPacket.AddNpc(npc)), Is.EqualTo(expected.ToArray()));
    }

    // A hostile class 3 npc (CNpcData::IsBoss: friendly 0, class >= 3) with property tag 5: the name after the
    // directions, the boss block after the reward item and the damage before the flag.
    [Test]
    public void BossWithDamagedHpBar() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldNpc npc = Npc(test.Session.Field!, 3, 0, ["Damaged_HP_Upper_HP_Bar"], [new NpcMetadataSkill.Entry(0x0C0D0E0F, 2)]);

        var expected = new ByteWriter();
        Head(expected, npc);
        expected.WriteString("boss_kfm"); // narrow
        Stats(expected, npc);
        Middle(expected);
        expected.WriteUnicodeString(""); // owner effect
        expected.WriteInt(1);
        expected.WriteInt(0x0C0D0E0F);
        expected.WriteShort(2);
        expected.WriteInt(0); // owner object id
        expected.WriteLong(0x77); // damage taken
        expected.WriteByte(0); // flag
        Assert.That(Body(FieldPacket.AddNpc(npc)), Is.EqualTo(expected.ToArray()));
    }
}

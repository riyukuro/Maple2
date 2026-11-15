using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// NpcControl (0x58) against the client's readers. CSceneStateGame::OnNpcControl (scenestategame.cpp:3615) reads a
// ushort count, then per npc a ushort size and that many bytes into a buffer, whose first int is the object id;
// CNpc::ApplySyncBlock (unit_14108e1e0_npcactorandvibrate.cpp:883) reads the rest: byte flags (bit 1 battle), a
// Vector3Short position, short rotation x10, a Vector3Short velocity, short speed x100, a boss's int owner actor, byte
// state, short sequence, ushort serial; for sequence -2 / -3 a byte absolute and two Vector3 and two floats, or one
// Vector3; then state 17 (spawn) an int spawn point and state 13 (hit) three floats and a byte. The entry is sized, so
// the server writes the spawn and hit fields with every such entry (the reader takes them when the serial changes).
public class NpcControlLayoutTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int NpcId = 21000001;

    private record Entry(int Id, byte Flags, short[] Position, short Rotation, short[] Velocity, short Speed, int? Owner,
        byte State, short Sequence, ushort Serial, byte[] Tail);

    private FieldNpc Spawn(bool boss) {
        Start();
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        NpcMetadata metadata = AiStateTests.Npc(NpcId);
        // CNpcData::IsBoss: a hostile npc (friendly 0) of class 3 or more.
        metadata = metadata with { Basic = metadata.Basic with { Friendly = 0, Class = boss ? 3 : 1 } };
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field, AiStateTests.NpcTable([], metadata));
        typeof(FieldManager).GetField("<AiMetadata>k__BackingField", Any)!.SetValue(Field, AiStateTests.AiTable());
        FieldNpc npc = Field.SpawnNpc(metadata, new Vector3(1000, -200, 300), new Vector3(0, 0, 90), disableAi: true)!;
        test.TakeSent();
        return npc;
    }

    private static void SetState(FieldNpc npc, ActorState state, NpcControlRules.HitEntry? hit = null) {
        typeof(MovementState).GetField("<State>k__BackingField", Any)!.SetValue(npc.MovementState, state);
        typeof(MovementState).GetField("<HitEntry>k__BackingField", Any)!.SetValue(npc.MovementState, hit);
    }

    // The client's reads, one entry per npc; each entry's buffer must be read to its last byte.
    private static List<Entry> Read(ByteWriter packet, params bool[] boss) {
        var reader = new ByteReader(packet.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.NpcControl));
        int count = reader.ReadShort();
        Assert.That(count, Is.EqualTo(boss.Length));
        var entries = new List<Entry>();
        for (int i = 0; i < count; i++) {
            int size = (ushort) reader.ReadShort();
            var buffer = new ByteReader(reader.ReadBytes(size), 0);
            int id = buffer.ReadInt();
            byte flags = buffer.ReadByte();
            short[] position = [buffer.ReadShort(), buffer.ReadShort(), buffer.ReadShort()];
            short rotation = buffer.ReadShort();
            short[] velocity = [buffer.ReadShort(), buffer.ReadShort(), buffer.ReadShort()];
            short speed = buffer.ReadShort();
            int? owner = boss[i] ? buffer.ReadInt() : null;
            byte state = buffer.ReadByte();
            short sequence = buffer.ReadShort();
            var serial = (ushort) buffer.ReadShort();
            var tail = new ByteWriter();
            if (sequence is -2 or -3) {
                bool absolute = buffer.ReadBool();
                tail.WriteBool(absolute);
                tail.WriteBytes(buffer.ReadBytes(absolute ? 32 : 12));
            }
            if (state == 17) {
                tail.WriteBytes(buffer.ReadBytes(4));
            } else if (state == 13) {
                tail.WriteBytes(buffer.ReadBytes(13));
            }
            Assert.That(buffer.Available, Is.Zero, $"entry {i}: bytes the client reader leaves");
            entries.Add(new Entry(id, flags, position, rotation, velocity, speed, owner, state, sequence, serial, tail.ToArray()));
        }
        Assert.That(reader.Available, Is.Zero);
        return entries;
    }

    private static void AssertPlace(Entry entry, FieldNpc npc) {
        Assert.Multiple(() => {
            Assert.That(entry.Id, Is.EqualTo(npc.ObjectId));
            // Whole units (CInPacket::ReadVector3Short) and the facing in tenths of a degree.
            Assert.That(entry.Position, Is.EqualTo(new short[] { 1000, -200, 300 }));
            // 90 degrees; the angle goes through a float, so 899 is as good (ReadShortDiv10: 0.1 degree).
            Assert.That(entry.Rotation, Is.InRange((short) 899, (short) 900));
            Assert.That(entry.Velocity, Is.EqualTo(new short[] { 0, 0, 0 }));
            // The sequence's own speed (ReadShortDivScaled(100): 1.0).
            Assert.That(entry.Speed, Is.EqualTo((short) 100));
        });
    }

    [Test]
    public void Idle_NoOwnerNoTail() {
        FieldNpc npc = Spawn(boss: false);
        SetState(npc, ActorState.Idle);
        Entry entry = Read(NpcControlPacket.Control(npc), false)[0];
        AssertPlace(entry, npc);
        Assert.Multiple(() => {
            Assert.That(entry.Flags, Is.Zero);
            Assert.That(entry.Owner, Is.Null);
            Assert.That(entry.State, Is.EqualTo((byte) ActorState.Idle));
            Assert.That(entry.Tail, Is.Empty);
        });
    }

    // A boss carries its target as the owner actor, and the battle flag while it has one.
    [Test]
    public void Boss_OwnerActorAndBattleFlag() {
        FieldNpc npc = Spawn(boss: true);
        npc.BattleState.ForceTarget(test.Session.Player);
        SetState(npc, ActorState.Idle);
        Entry entry = Read(NpcControlPacket.Control(npc), true)[0];
        AssertPlace(entry, npc);
        Assert.Multiple(() => {
            Assert.That(entry.Flags, Is.EqualTo(NpcControlRules.BattleFlag));
            Assert.That(entry.Owner, Is.EqualTo(test.Session.Player.ObjectId));
            Assert.That(entry.Tail, Is.Empty);
        });
    }

    // Two npcs in one packet, each in its own sized buffer.
    [Test]
    public void TwoNpcs_EachSized() {
        FieldNpc npc = Spawn(boss: false);
        SetState(npc, ActorState.Idle);
        List<Entry> entries = Read(NpcControlPacket.Control(npc, npc), false, false);
        Assert.That(entries.Select(entry => entry.Id), Is.EqualTo(new[] { npc.ObjectId, npc.ObjectId }));
    }

    [Test]
    public void Hit_StateHitData() {
        FieldNpc npc = Spawn(boss: false);
        SetState(npc, ActorState.Hit, new NpcControlRules.HitEntry(7, 0.25f, 0.5f, 10f, true));
        Entry entry = Read(NpcControlPacket.Control(npc), false)[0];
        var expected = new ByteWriter();
        expected.WriteFloat(0.25f); // delay (StateHitData +0x1c)
        expected.WriteFloat(0.5f); // push time (+0x24)
        expected.WriteFloat(10f); // lift (+0x28)
        expected.WriteBool(true); // down (+0x2c)
        Assert.Multiple(() => {
            Assert.That(entry.State, Is.EqualTo((byte) ActorState.Hit));
            Assert.That(entry.Sequence, Is.EqualTo((short) 7));
            Assert.That(entry.Tail, Is.EqualTo(expected.ToArray()));
        });
    }

    // The hold after the hit entry went out: state 0 with sequence -1 and no hit fields.
    [Test]
    public void HitHold_StateZeroNoTail() {
        FieldNpc npc = Spawn(boss: false);
        SetState(npc, ActorState.Hit);
        Entry entry = Read(NpcControlPacket.Control(npc), false)[0];
        Assert.Multiple(() => {
            Assert.That(entry.State, Is.Zero);
            Assert.That(entry.Sequence, Is.EqualTo((short) -1));
            Assert.That(entry.Tail, Is.Empty);
        });
    }

    [Test]
    public void Spawn_SpawnPointId() {
        FieldNpc npc = Spawn(boss: false);
        npc.SpawnPointId = 0x0A0B0C0D;
        SetState(npc, ActorState.Spawn);
        Entry entry = Read(NpcControlPacket.Control(npc), false)[0];
        Assert.Multiple(() => {
            Assert.That(entry.State, Is.EqualTo((byte) 17));
            Assert.That(entry.Tail, Is.EqualTo(new byte[] { 0x0D, 0x0C, 0x0B, 0x0A }));
        });
    }

    [Test]
    public void Talk_State22NoSequence() {
        FieldNpc npc = Spawn(boss: false);
        Entry entry = Read(NpcControlPacket.Talk(npc), false)[0];
        AssertPlace(entry, npc);
        Assert.Multiple(() => {
            Assert.That(entry.State, Is.EqualTo((byte) 22));
            Assert.That(entry.Sequence, Is.EqualTo((short) -1));
            Assert.That(entry.Tail, Is.Empty);
        });
    }

    [Test]
    public void Dead_NoFlagsStateZero() {
        FieldNpc npc = Spawn(boss: true);
        npc.BattleState.ForceTarget(test.Session.Player);
        Entry entry = Read(NpcControlPacket.Dead(npc), true)[0];
        Assert.Multiple(() => {
            Assert.That(entry.Flags, Is.Zero);
            Assert.That(entry.Owner, Is.Zero);
            Assert.That(entry.State, Is.Zero);
            Assert.That(entry.Sequence, Is.EqualTo((short) -1));
            Assert.That(entry.Tail, Is.Empty);
        });
    }

    // The jump node: sequence -2 with the absolute block (two world points, the duration and the height scale).
    [Test]
    public void Jump_AbsoluteBlock() {
        FieldNpc npc = Spawn(boss: false);
        Entry entry = Read(NpcControlPacket.Jump(npc, NpcControlPacket.ANI_JUMP_A, new Vector3(1, 2, 3), new Vector3(4, 5, 6), 1.5f, 0.75f), false)[0];
        var expected = new ByteWriter();
        expected.WriteBool(true);
        foreach (float value in new[] { 1f, 2, 3, 4, 5, 6, 1.5f, 0.75f }) {
            expected.WriteFloat(value);
        }
        Assert.Multiple(() => {
            Assert.That(entry.State, Is.EqualTo((byte) ActorState.Jump));
            Assert.That(entry.Sequence, Is.EqualTo(NpcControlPacket.ANI_JUMP_A));
            Assert.That(entry.Tail, Is.EqualTo(expected.ToArray()));
        });
    }
}

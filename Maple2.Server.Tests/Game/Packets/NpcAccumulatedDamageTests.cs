using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// NpcAccumulatedDamage against the client's reader. Opcode 0xe7 (SendOp.NpcRide) goes to CSceneStateGame::OnNpcRide,
// which reads the npc object id and hands the rest to CNpc 0x141092c50: byte mode, then for mode 0 a long, the damage
// the top hp bar shows (UIUpperHpBar, uiupperhpbar.cpp:73; the same value FieldAddNpc gives a "damaged_hp_upper_hp_bar"
// npc). Mode 1 (an int) and mode 2 (nothing) set another npc field and are not sent.
public class NpcAccumulatedDamageTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void ModeZeroCarriesTheDamage() {
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(npc, 0x0A0B0C0D);
        typeof(FieldNpc).GetField("<AccumulatedDamage>k__BackingField", Any)!.SetValue(npc, 0x0102030405060708L);

        ByteWriter writer = FieldPacket.NpcAccumulatedDamage(npc);
        var reader = new ByteReader(writer.Buffer.AsSpan(0, writer.Length).ToArray());

        Assert.That((SendOp) reader.ReadShort(), Is.EqualTo(SendOp.NpcRide));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x0A0B0C0D));
        Assert.That(reader.ReadByte(), Is.EqualTo(0)); // mode 0: the accumulated damage
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708L));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

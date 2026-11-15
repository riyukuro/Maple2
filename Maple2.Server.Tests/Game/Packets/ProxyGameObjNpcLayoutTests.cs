using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// ProxyGameObj 0x7E's npc and pet modes against the client's reader, CSceneStateGame::FUN_14172f500
// (scenestategame.cpp:11395-11446): 6 int id, int npc id, byte kind, int, Vector3; 7 and 10 int id; 8 and 11 int id,
// byte kind, Vector3; 9 int id, int skin, int npc id, byte kind, Vector3. The kind byte is the dead flag.
public class ProxyGameObjNpcLayoutTests : NpcLayoutTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static ByteReader Reader(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ProxyGameObj));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    private static void AssertPosition(ByteReader reader, Vector3 position) {
        Assert.That(new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo(position));
    }

    [Test]
    public void AddNpc_Mode6() {
        FieldNpc npc = Spawn(boss: false);
        npc.SpawnPointId = 0x0A0B0C0D;
        ByteReader reader = Reader(ProxyObjectPacket.AddNpc(npc), 6);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(npc.ObjectId));
            Assert.That(reader.ReadInt(), Is.EqualTo(NpcId));
            Assert.That(reader.ReadByte(), Is.Zero); // alive
            Assert.That(reader.ReadInt(), Is.EqualTo(0x0A0B0C0D));
            AssertPosition(reader, npc.Position);
            Assert.That(reader.Available, Is.Zero);
        });
    }

    [Test]
    public void RemoveAndUpdateNpc_Modes7And8() {
        FieldNpc npc = Spawn(boss: false);
        ByteReader remove = Reader(ProxyObjectPacket.RemoveNpc(npc.ObjectId), 7);
        Assert.Multiple(() => {
            Assert.That(remove.ReadInt(), Is.EqualTo(npc.ObjectId));
            Assert.That(remove.Available, Is.Zero);
        });
        ByteReader update = Reader(ProxyObjectPacket.UpdateNpc(npc), 8);
        Assert.Multiple(() => {
            Assert.That(update.ReadInt(), Is.EqualTo(npc.ObjectId));
            Assert.That(update.ReadByte(), Is.Zero);
            AssertPosition(update, npc.Position);
            Assert.That(update.Available, Is.Zero);
        });
    }

    private static FieldPet Pet(int objectId, int npcId, int skinId, bool dead) {
        var pet = (FieldPet) RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        NpcMetadata metadata = AiStateTests.Npc(npcId);
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(pet, new Npc(metadata, null));
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(pet, objectId);
        typeof(Actor<Npc>).GetField("<Transform>k__BackingField", Any)!.SetValue(pet, new Transform());
        typeof(Actor<Npc>).GetField("<IsDead>k__BackingField", Any)!.SetValue(pet, dead);
        typeof(FieldPet).GetField("<SkinId>k__BackingField", Any)!.SetValue(pet, skinId);
        pet.Position = new Vector3(10, 20, 30);
        return pet;
    }

    [Test]
    public void Pet_Modes9To11() {
        FieldPet pet = Pet(0x0102, 61000001, 61000099, dead: true);
        ByteReader add = Reader(ProxyObjectPacket.AddPet(pet), 9);
        Assert.Multiple(() => {
            Assert.That(add.ReadInt(), Is.EqualTo(0x0102));
            Assert.That(add.ReadInt(), Is.EqualTo(61000099)); // skin
            Assert.That(add.ReadInt(), Is.EqualTo(61000001)); // npc id
            Assert.That(add.ReadByte(), Is.EqualTo(1)); // dead
            AssertPosition(add, new Vector3(10, 20, 30));
            Assert.That(add.Available, Is.Zero);
        });
        ByteReader remove = Reader(ProxyObjectPacket.RemovePet(0x0102), 10);
        Assert.Multiple(() => {
            Assert.That(remove.ReadInt(), Is.EqualTo(0x0102));
            Assert.That(remove.Available, Is.Zero);
        });
        ByteReader update = Reader(ProxyObjectPacket.UpdatePet(pet), 11);
        Assert.Multiple(() => {
            Assert.That(update.ReadInt(), Is.EqualTo(0x0102));
            Assert.That(update.ReadByte(), Is.EqualTo(1));
            AssertPosition(update, new Vector3(10, 20, 30));
            Assert.That(update.Available, Is.Zero);
        });
    }
}

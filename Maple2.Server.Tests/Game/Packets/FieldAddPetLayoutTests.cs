using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game.Packets;

// FieldAddPet against CSceneStateGame::OnFieldAddPet (client/ms2/scenestategame.cpp, after OnFieldAddNpc): int object id,
// int npc id (the skin), int pet npc id, Vector3 position, Vector3 direction, float scale, int owner object id, CNpc::
// vfunc_90's stats (u8 35, 3 x (long hp, int attack speed): the same block FieldAddNpcLayoutTests reads), long maid uid
// (the pet's item uid), u8, i16 level, i16 grade (taming rank), i32, unicode name.
public class FieldAddPetLayoutTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldPet Pet(Item item, short tamingRank) {
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 42, 0, false, 0, 0, 0, 0, [], [], 0);
        var stat = new NpcMetadataStat(new Dictionary<BasicAttribute, long> {
            [BasicAttribute.Health] = 500,
            [BasicAttribute.AttackSpeed] = 100,
        }, [], [], [], []);
        var metadata = new NpcMetadata(61000001, "pet", "", new NpcMetadataModel("pet", 1, 1), stat, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var pet = (FieldPet) RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(pet, new Npc(metadata, null));
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(pet, 0x0102);
        typeof(Actor<Npc>).GetField("<Transform>k__BackingField", Any)!.SetValue(pet, new Transform());
        typeof(Actor<Npc>).GetField("<Stats>k__BackingField", Any)!.SetValue(pet, new StatsManager(pet));
        typeof(FieldPet).GetField("<SkinId>k__BackingField", Any)!.SetValue(pet, 61000099);
        typeof(FieldPet).GetField(nameof(FieldPet.Pet), Any)!.SetValue(pet, item);
        typeof(FieldPet).GetField(nameof(FieldPet.Scale), Any)!.SetValue(pet, 1.5f);
        var petMetadata = (PetMetadata) RuntimeHelpers.GetUninitializedObject(typeof(PetMetadata));
        typeof(FieldPet).GetField(nameof(FieldPet.Metadata), Any)!.SetValue(pet, petMetadata);
        pet.TamingRank = tamingRank;
        pet.Position = new Vector3(10, 20, 30);
        return pet;
    }

    [Test]
    public void AddPet() {
        var item = new Item(TestGameSession.Metadata(60000001), 3, 1) { Uid = 0x1122334455667788 };
        FieldPet pet = Pet(item, 4);

        ByteWriter packet = FieldPacket.AddPet(pet);
        var reader = new ByteReader(packet.ToArray(), 0);
        Assert.Multiple(() => {
            Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.FieldAddPet));
            Assert.That(reader.ReadInt(), Is.EqualTo(0x0102));
            Assert.That(reader.ReadInt(), Is.EqualTo(61000099)); // the skin npc
            Assert.That(reader.ReadInt(), Is.EqualTo(61000001)); // the pet's npc id
            Assert.That(new[] { reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat() }, Is.EqualTo(new[] { 10f, 20f, 30f }));
            reader.Skip(12); // direction
            Assert.That(reader.ReadFloat(), Is.EqualTo(1.5f));
            Assert.That(reader.ReadInt(), Is.Zero); // no owner
            Assert.That(reader.ReadByte(), Is.EqualTo(35));
            for (int i = 0; i < 3; i++) {
                reader.ReadLong();
                reader.ReadInt();
            }
            Assert.That(reader.ReadLong(), Is.EqualTo(0x1122334455667788));
            reader.ReadByte();
            Assert.That(reader.ReadShort(), Is.EqualTo(42)); // not summoned: the npc level
            Assert.That(reader.ReadShort(), Is.EqualTo(4));
            reader.ReadInt();
            Assert.That(reader.ReadUnicodeString(), Is.Empty);
            Assert.That(reader.Available, Is.Zero);
        });
    }
}

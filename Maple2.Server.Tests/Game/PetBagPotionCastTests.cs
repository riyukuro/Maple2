using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// A potion in the summoned pet's bag, cast with its uid (the client looks in the pet's bag after the owner's,
// MyPC, mypc.cpp:1171-1173): the cast takes one piece from the bag, and the last piece leaves the bag. The bag's move
// onto a taken slot swaps the two. The ids are made up.
public class PetBagPotionCastTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int PotionSkill = 90000963, Potion = 20000963, Juice = 20000964, PetId = 60000963;

    private ItemCollection bag = null!;
    private Item potion = null!;

    private void Setup(int amount) {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        SkillMetadata skill = QuestConditionWiringTests.CastSkill(PotionSkill);
        BeginCondition always = skill.Data.Condition with { Probability = 1f, Gender = Gender.All, PvpZone = PvpZoneApply.Everywhere, AllowOnSurvival = false };
        QuestConditionWiringTests.SetSkills(test, skill with { Data = skill.Data with { Condition = always, Consume = skill.Data.Consume with { UseItem = true } } });
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        typeof(Actor<Player>).GetField("Logger", Any)!.SetValue(test.Session.Player, Serilog.Log.ForContext<Player>());
        ItemMetadata potionData = TestGameSession.Metadata(Potion) with { Skill = new ItemMetadataSkill(PotionSkill, 1, 0, 0) };
        test.Register(potionData, TestGameSession.Metadata(Juice));

        ItemMetadata petData = TestGameSession.Metadata(PetId, type: 11, slotMax: 1);
        petData = petData with { Property = petData.Property with { PetId = PetId } };
        test.Register(petData);
        var petItem = new Item(petData);
        Assert.That(test.Session.Item.Inventory.Add(petItem, commit: true), Is.True);
        bag = new ItemCollection(6);
        potion = new Item(potionData, amount: amount) { Uid = 9_000_001, Slot = 0 };
        Assert.That(bag.Add(potion).Count, Is.EqualTo(1));
        var fieldPet = (FieldPet) RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(FieldPet).GetField(nameof(FieldPet.Pet), Any)!.SetValue(fieldPet, petItem);
        var manager = (PetManager) RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
        typeof(PetManager).GetField("session", Any)!.SetValue(manager, test.Session);
        typeof(PetManager).GetField("pet", Any)!.SetValue(manager, fieldPet);
        typeof(PetManager).GetField("items", Any)!.SetValue(manager, bag);
        typeof(PetManager).GetField("config", Any)!.SetValue(manager, new PetConfig());
        test.Session.Pet = manager;
        test.TakeSent();
    }

    private void Cast(long itemUid) {
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(1);
            writer.WriteInt((int) Field.FieldTick);
            writer.WriteInt(PotionSkill);
            writer.WriteShort(1);
            writer.WriteByte(0);
            writer.Write(test.Session.Player.Position);
            writer.Write(Vector3.UnitY);
            writer.Write(Vector3.Zero);
            writer.WriteFloat(0);
            writer.WriteInt(0);
            writer.WriteBool(false);
            writer.WriteLong(itemUid);
            writer.WriteLong(0);
        }));
    }

    [Test]
    public void APotionInThePetsBag_PaysOnePieceOfTheCast() {
        Setup(3);
        Cast(potion.Uid);
        Assert.Multiple(() => {
            Assert.That(test.Session.Player.ActiveSkills.Get(1), Is.Not.Null, "the cast runs");
            Assert.That(potion.Amount, Is.EqualTo(2));
            Assert.That(test.TakeSent().Select(System.Convert.ToHexString),
                Does.Contain(System.Convert.ToHexString(PetInventoryPacket.Update(potion.Uid, 2).ToArray())));
        });
    }

    [Test]
    public void ThePetsLastPotion_LeavesTheBag() {
        Setup(1);
        Cast(potion.Uid);
        Assert.Multiple(() => {
            Assert.That(test.Session.Player.ActiveSkills.Get(1), Is.Not.Null, "the cast runs");
            Assert.That(bag.Contains(potion.Uid), Is.False);
            Assert.That(test.TakeSent().Select(System.Convert.ToHexString),
                Does.Contain(System.Convert.ToHexString(PetInventoryPacket.Remove(potion.Uid).ToArray())));
        });
    }

    // RequestPetInventory 3 onto a slot the bag fills: the two items change places.
    [Test]
    public void AMoveOntoATakenSlot_SwapsTheTwo() {
        Setup(3);
        var juice = new Item(TestGameSession.Metadata(Juice), amount: 2) { Uid = 9_000_002, Slot = 4 };
        Assert.That(bag.Add(juice).Count, Is.EqualTo(1));
        new PetInventoryHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(potion.Uid);
            writer.WriteShort(4);
        }));
        Assert.Multiple(() => {
            Assert.That((potion.Slot, juice.Slot), Is.EqualTo(((short) 4, (short) 0)));
            Assert.That(test.TakeSent(), Is.EqualTo(new[] { PetInventoryPacket.Move(juice.Uid, 0, potion.Uid, 4).ToArray() }));
        });
    }
}

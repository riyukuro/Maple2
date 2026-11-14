using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.PacketLib.Tools;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The pet modes against the client's own checks. A pet switch (mode 3) comes from the client after its own state and field checks only (mypc.cpp FUN_14152b8d0);
// a next pet that is not in the pet bag, or that the field or the character's level refuses, leaves the summoned
// pet out. Before, the summoned pet was put away first and nothing came back.
public class PetHandlerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int PetId = 60000001;

    private static (TestGameSession Test, GameSession Session, PetManager Summoned, Item Other) Setup(bool fieldPet = true, int otherLevel = 0) {
        var test = new TestGameSession();
        ItemMetadata petData = TestGameSession.Metadata(PetId, type: 11, slotMax: 1);
        petData = petData with { Property = petData.Property with { PetId = PetId } };
        Item pet = test.Store(petData);
        Item other = test.Store(petData with { Id = PetId + 1, Limit = petData.Limit with { Level = otherLevel } });
        GameSession session = test.Start();
        MapMetadata map = TestGameSession.Map(2000001) with {
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, fieldPet),
        };
        test.EnterMap(map);
        session.Player.Value.Character.Level = 10;

        var fieldPetObject = (FieldPet) RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(FieldPet).GetField(nameof(FieldPet.Pet), Any)!.SetValue(fieldPetObject, session.Item.Inventory.Get(pet.Uid)!);
        var manager = (PetManager) RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
        typeof(PetManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(PetManager).GetField("pet", Any)!.SetValue(manager, fieldPetObject);
        typeof(PetManager).GetField("items", Any)!.SetValue(manager, new ItemCollection(0));
        typeof(PetManager).GetField("config", Any)!.SetValue(manager, new PetConfig());
        session.Pet = manager;
        test.TakeSent();
        return (test, session, manager, other);
    }

    private static void Switch(GameSession session, long uid) {
        var handler = new PetHandler { BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(uid);
        }));
    }

    [Test]
    public void ASwitchToAPetNotInTheBag_KeepsTheSummonedPet() {
        (TestGameSession test, GameSession session, PetManager summoned, _) = Setup();

        Switch(session, 999999);

        Assert.Multiple(() => {
            Assert.That(session.Pet, Is.SameAs(summoned));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    [Test]
    public void ASwitchToAPetAboveTheCharactersLevel_KeepsTheSummonedPet() {
        (TestGameSession test, GameSession session, PetManager summoned, Item other) = Setup(otherLevel: 11);
        Assert.That(session.Item.Inventory.Get(other.Uid, InventoryType.Pets), Is.Not.Null);

        Switch(session, other.Uid);

        Assert.Multiple(() => {
            Assert.That(session.Pet, Is.SameAs(summoned));
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(PetHandler.CanSwitchTo(session, other.Uid), Is.False);
        });
        session.Player.Value.Character.Level = 11;
        Assert.That(PetHandler.CanSwitchTo(session, other.Uid), Is.True);
    }

    [Test]
    public void ASwitchInAFieldWithoutPets_KeepsTheSummonedPet() {
        (TestGameSession test, GameSession session, PetManager summoned, Item other) = Setup(fieldPet: false);

        Switch(session, other.Uid);

        Assert.Multiple(() => {
            Assert.That(session.Pet, Is.SameAs(summoned));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    // The auto-potion window writes one entry per row, two rows (Unk_PetOptions::Write); a longer list is refused
    // and the stored settings stay as they were.
    [Test]
    public void APotionListLongerThanTheWindow_IsRefused() {
        (TestGameSession test, GameSession session, _, _) = Setup();
        var handler = new PetHandler { BanWordStorage = null! };
        ByteReader Potions(int count) => TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteByte((byte) count);
            for (int i = 0; i < count; i++) {
                writer.WriteInt(i);
                writer.WriteFloat(0.5f);
                writer.WriteInt(20000001 + i);
            }
        });
        var config = (PetConfig) typeof(PetManager).GetField("config", Any)!.GetValue(session.Pet)!;

        handler.Handle(session, Potions(3));
        Assert.Multiple(() => {
            Assert.That(config.PotionConfig, Is.Empty);
            Assert.That(test.TakeSent(), Is.Empty);
        });

        handler.Handle(session, Potions(2));
        Assert.Multiple(() => {
            Assert.That(config.PotionConfig, Has.Length.EqualTo(2));
            Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
        });
    }
}

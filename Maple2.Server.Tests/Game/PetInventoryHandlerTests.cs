using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The pet bag modes (RequestPetInventory 0 put in, 1 take out, 3 move, 7 load) as the client sends them: uid, slot
// and count for 0 and 1, uid and slot for 3. A refused move is answered with its message box and nothing moves;
// without a summoned pet no mode answers.
public class PetInventoryHandlerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int PetId = 60000001;
    private const int Potion = 2000001;
    private const int BagSize = 6;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private ItemCollection bag = null!;
    private Item pet = null!;
    private Item held = null!;
    private readonly PetInventoryHandler handler = new();

    private void Setup(int heldAmount = 5, bool summon = true) {
        test = new TestGameSession();
        ItemMetadata petData = TestGameSession.Metadata(PetId, type: 11, slotMax: 1);
        petData = petData with { Property = petData.Property with { PetId = PetId } };
        // The in-memory store numbers items and characters alike: a filler keeps the pet's uid off the character id.
        test.Store(TestGameSession.Metadata(2000002));
        pet = test.Store(petData);
        held = test.Store(TestGameSession.Metadata(Potion), heldAmount);
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        bag = new ItemCollection(BagSize);
        if (summon) {
            var fieldPet = (FieldPet) RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
            typeof(FieldPet).GetField(nameof(FieldPet.Pet), Any)!.SetValue(fieldPet, session.Item.Inventory.Get(pet.Uid)!);
            var manager = (PetManager) RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
            typeof(PetManager).GetField("session", Any)!.SetValue(manager, session);
            typeof(PetManager).GetField("pet", Any)!.SetValue(manager, fieldPet);
            typeof(PetManager).GetField("items", Any)!.SetValue(manager, bag);
            typeof(PetManager).GetField("config", Any)!.SetValue(manager, new PetConfig());
            session.Pet = manager;
        }
        test.TakeSent();
    }

    private static ByteReader Transfer(byte mode, long uid, short slot, int amount) => TestGameSession.Packet(writer => {
        writer.WriteByte(mode);
        writer.WriteLong(uid);
        writer.WriteShort(slot);
        writer.WriteInt(amount);
    });

    private static ByteReader Move(long uid, short slot) => TestGameSession.Packet(writer => {
        writer.WriteByte(3);
        writer.WriteLong(uid);
        writer.WriteShort(slot);
    });

    private static byte[] Box(StringCode code) => NoticePacket.MessageBox(code).ToArray();

    private int HeldAmount() => session.Item.Inventory.Find(Potion).Sum(item => item.Amount);

    [Test]
    public void PutIn_MovesTheCountIntoTheBagSlot() {
        Setup();

        handler.Handle(session, Transfer(0, held.Uid, 2, 5));

        List<byte[]> sent = test.TakeSent();
        Item? inBag = bag.Get(held.Uid);
        Assert.Multiple(() => {
            Assert.That(inBag, Is.Not.Null);
            Assert.That(inBag!.Slot, Is.EqualTo(2));
            Assert.That(inBag.Amount, Is.EqualTo(5));
            Assert.That(HeldAmount(), Is.EqualTo(0));
            Assert.That(sent.Select(TestGameSession.Op), Does.Contain(SendOp.PetInventory));
            Assert.That(sent, Has.None.EqualTo(Box(StringCode.s_item_err_invalid_count)));
        });
    }

    [TestCase(6)]
    [TestCase(0)]
    [TestCase(-1)]
    public void PutIn_OfACountNotHeld_IsRefused(int amount) {
        Setup();

        handler.Handle(session, Transfer(0, held.Uid, 0, amount));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.EqualTo(new[] { Box(StringCode.s_item_err_invalid_count) }));
            Assert.That(bag.Count, Is.EqualTo(0));
            Assert.That(HeldAmount(), Is.EqualTo(5));
        });
    }

    [Test]
    public void PutIn_OfAPet_IsRefused() {
        Setup();

        handler.Handle(session, Transfer(0, pet.Uid, 0, 1));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.EqualTo(new[] { Box(StringCode.s_pet_inventory_not_sendin_petitem) }));
            Assert.That(bag.Count, Is.EqualTo(0));
            Assert.That(session.Item.Inventory.Get(pet.Uid), Is.Not.Null);
        });
    }

    [Test]
    public void TakeOut_GivesThePartBackToTheBag() {
        Setup();
        handler.Handle(session, Transfer(0, held.Uid, 0, 5));
        test.TakeSent();

        handler.Handle(session, Transfer(1, held.Uid, -1, 2));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Has.None.EqualTo(Box(StringCode.s_item_err_invalid_count)));
            Assert.That(bag.Get(held.Uid)?.Amount, Is.EqualTo(3));
            Assert.That(HeldAmount(), Is.EqualTo(2));
        });
    }

    [Test]
    public void TakeOut_OfMoreThanTheBagHolds_IsRefused() {
        Setup();
        handler.Handle(session, Transfer(0, held.Uid, 0, 5));
        test.TakeSent();

        handler.Handle(session, Transfer(1, held.Uid, -1, 6));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.EqualTo(new[] { Box(StringCode.s_item_err_invalid_count) }));
            Assert.That(bag.Get(held.Uid)?.Amount, Is.EqualTo(5));
            Assert.That(HeldAmount(), Is.EqualTo(0));
        });
    }

    [Test]
    public void TakeOut_OfAnItemNotInTheBag_IsRefused() {
        Setup();

        handler.Handle(session, Transfer(1, held.Uid, -1, 1));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.EqualTo(new[] { Box(StringCode.s_item_err_invalid_count) }));
            Assert.That(HeldAmount(), Is.EqualTo(5));
        });
    }

    [Test]
    public void Move_ToAnEmptySlot_SendsTheMove() {
        Setup();
        handler.Handle(session, Transfer(0, held.Uid, 0, 5));
        test.TakeSent();

        handler.Handle(session, Move(held.Uid, 4));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.EqualTo(new[] { PetInventoryPacket.Move(0, 0, held.Uid, 4).ToArray() }));
            Assert.That(bag.Get(held.Uid)?.Slot, Is.EqualTo(4));
        });
    }

    [TestCase((short) -1)]
    [TestCase((short) BagSize)]
    public void Move_ToASlotOutsideTheBag_IsRefused(short slot) {
        Setup();
        handler.Handle(session, Transfer(0, held.Uid, 0, 5));
        test.TakeSent();

        handler.Handle(session, Move(held.Uid, slot));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.EqualTo(new[] { Box(StringCode.s_item_err_Invalid_slot) }));
            Assert.That(bag.Get(held.Uid)?.Slot, Is.EqualTo(0));
        });
    }

    [Test]
    public void Load_SendsTheResetThenTheItems() {
        Setup();
        handler.Handle(session, Transfer(0, held.Uid, 0, 5));
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(7)));

        Assert.That(test.TakeSent(), Is.EqualTo(new[] {
            PetInventoryPacket.Reset().ToArray(),
            PetInventoryPacket.Load(bag.ToList()).ToArray(),
        }));
    }

    [Test]
    public void WithoutASummonedPet_NoModeAnswersOrMovesAnything() {
        Setup(summon: false);

        handler.Handle(session, Transfer(0, held.Uid, 0, 5));
        handler.Handle(session, Transfer(1, held.Uid, -1, 1));
        handler.Handle(session, Move(held.Uid, 1));
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(7)));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(HeldAmount(), Is.EqualTo(5));
        });
    }

    // The save before a move stores the summoned pet (its config and its bag) before it gives the account lock back,
    // and the pet's dispose at LeaveField, after the move, writes nothing more: the next server owns the rows from its
    // load (here it stored a config of its own, gone from the table). Before, the pet was stored only at that dispose,
    // with no lock, so its write landed after the next server's load and stores.
    [Test]
    public void MigrationSave_StoresThePet_AndTheLeaveAfterItWritesNothing() {
        Setup();
        test.SavingConfig(session);
        typeof(GameSession).GetField("preMigrationSaved", Any)!.SetValue(session, false);

        Assert.That(session.MigrationSave(), Is.True);
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            context.PetConfig.Remove(context.PetConfig.Single(row => row.ItemUid == pet.Uid));
            context.SaveChanges();
        }
        session.Field = null; // off the half-built test field: the dispose's writes come before the pet leaves it
        session.Pet!.Dispose(fieldChange: true);

        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            Assert.That(context.PetConfig.Any(row => row.ItemUid == pet.Uid), Is.False);
        }
    }
}
